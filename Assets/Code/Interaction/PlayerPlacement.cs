using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Drives placement preview ghost and place attempts while the player is holding treasure.
/// Reuses the single raycast from <see cref="PlayerInteraction"/> (no extra raycast).
/// </summary>
public class PlayerPlacement : MonoBehaviour
{
	PlayerPlacementDefinition _definition;
	PlayerController _player;
	PlayerInteraction _interaction;
	FloorPlacementTarget _floorTarget;
	GroundTreasureStackTarget _groundStackTarget;
	PlacementGhost _ghost;
	ITreasurePlacementTarget _activeTarget;
	PlacementPreview _activePreview;
	bool _hasPreview;
	bool _inputEnabled = true;
	Vector3 _smoothedPreviewPos;
	Quaternion _smoothedPreviewRot = Quaternion.identity;
	bool _hasSmoothedPreview;

	PlayerPlacementDefinition Definition => RuntimeDefinition.Resolve( ref _definition );

	public ITreasurePlacementTarget ActiveTarget => _activeTarget;
	public bool HasValidPlacement => _hasPreview && _activePreview.IsValid && _activeTarget != null;
	public bool HasActiveStackVolumePreview =>
		_hasPreview && _activePreview.GhostStyle == PlacementGhostStyle.StackVolume;
	public FloorPlacementTarget FloorTarget => _floorTarget;
	public float PlacementArcHeight => RuntimeDefinition.Get( Definition, d => d.placementArcHeight, 0.12f );
	public float CoinFlipSpeed => RuntimeDefinition.Get( Definition, d => d.coinFlipSpeed, 1f );

	float DropUpBias => RuntimeDefinition.Get( Definition, d => d.dropUpBias, 0.05f );
	float GroundStackUpBias => RuntimeDefinition.Get( Definition, d => d.groundStackUpBias, 0.02f );
	float GroundStackReleaseSpeedScale => RuntimeDefinition.Get( Definition, d => d.groundStackReleaseSpeedScale, 0.05f );
	float GroundStackReleaseUpScale => RuntimeDefinition.Get( Definition, d => d.groundStackReleaseUpScale, 0.1f );
	bool ShowGroundStackPreview => RuntimeDefinition.Get( Definition, d => d.showGroundStackPreview, true );
	float GroundStackSnapRadius => RuntimeDefinition.Get( Definition, d => d.groundStackSnapRadius, 0.42f );
	float PreviewSmoothSpeed => RuntimeDefinition.Get( Definition, d => d.previewSmoothSpeed, 18f );

	static readonly Collider[] StackSnapOverlap = new Collider[ 48 ];
	static readonly RaycastHit[] StackSnapSphereCastHits = new RaycastHit[ 24 ];
	static readonly HashSet<int> StackSnapColumnIds = new HashSet<int>();
	static readonly List<TreasureItem> StackSnapColumnBuffer = new List<TreasureItem>( 32 );

	public void Setup( PlayerController player, PlayerInteraction interaction )
	{
		_player = player;
		_interaction = interaction;
		_floorTarget = new FloorPlacementTarget( DropUpBias );
		_groundStackTarget = new GroundTreasureStackTarget(
			GroundStackUpBias,
			GroundStackReleaseSpeedScale,
			GroundStackReleaseUpScale );
	}

	public void SetInputEnabled( bool enabled )
	{
		_inputEnabled = enabled;
		if ( !_inputEnabled )
			ClearPreview();
	}

	void OnDestroy()
	{
		if ( _ghost != null )
		{
			_ghost.Destroy();
			_ghost = null;
		}
	}

	void LateUpdate()
	{
		UpdatePreview();
	}

	void UpdatePreview()
	{
		if ( !_inputEnabled || _player == null || _interaction == null )
		{
			ClearPreview();
			return;
		}

		PlayerCarry carry = _player.Carry;
		if ( carry == null || carry.Count <= 0 )
		{
			ClearPreview();
			return;
		}

		if ( !carry.TryPeekActive( out TreasureItem item ) || item == null )
		{
			ClearPreview();
			return;
		}

		RefreshGroundStackTuning();
		GroundTreasureStackTarget.ClearPreviewReservation();
		PlacementQuery query = BuildQuery();
		ITreasurePlacementTarget target = ResolveTarget( in query );
		if ( target is GemConstellationInteractable
			&& item.Definition != null
			&& item.Definition.category != TreasureCategory.Gem )
		{
			// Non-gems throw through the constellation — no invalid placement ghost.
			ClearPreview();
			return;
		}

		if ( target == null )
		{
			PlacementQuery floorQuery = BuildFloorQuery();
			EnsureFloorTarget();
			// Show floor ghost even when invalid (red) so rejected aim isn't invisible.
			if ( _floorTarget != null && floorQuery.HasHit )
			{
				target = _floorTarget;
				query = floorQuery;
			}
			else
			{
				ClearPreview();
				return;
			}
		}
		else if ( target == _floorTarget )
		{
			// Always seat floor preview on the dedicated surface hit, never a buried secondary hit.
			query = BuildFloorQuery();
			if ( !query.HasHit )
			{
				ClearPreview();
				return;
			}

			// Coin floor deposits create/join GroundCoinStack — prefer that target for ghost + place.
			if ( GroundCoinStack.IsGroundStackableCoin( item ) )
			{
				GroundCoinStack nearby = FindNearbyGroundCoinStack( in query );
				if ( nearby != null )
					target = nearby;
			}
		}

		if ( ShouldSuppressPlacementGhost( target, in query ) )
		{
			ClearPreview();
			return;
		}

		if ( target is GroundCoinStack || target == _groundStackTarget )
		{
			if ( !ShowGroundStackPreview )
			{
				ClearPreview();
				return;
			}
		}
		else if ( target == _floorTarget && GroundCoinStack.IsGroundStackableCoin( item ) )
		{
			if ( !ShowGroundStackPreview )
			{
				ClearPreview();
				return;
			}
		}

		if ( target == _groundStackTarget )
			UpdateGroundStackPreviewReservation( item );

		if ( !target.TryGetPlacementPreview( item, in query, out PlacementPreview preview ) )
		{
			ClearPreview();
			return;
		}

		preview = SmoothPreview( in preview );

		_activeTarget = target;
		_activePreview = preview;
		_hasPreview = true;

		if ( preview.GhostStyle == PlacementGhostStyle.Suppressed )
		{
			HoverOutlineRegistrar.Clear();
			if ( _ghost != null )
				_ghost.SetVisible( false );
			return;
		}

		EnsureGhost();
		ConfigureGhostVisuals();
		_ghost.SetVisible( true );

		if ( preview.GhostStyle == PlacementGhostStyle.StackVolume )
		{
			_ghost.UpdateStackVolume(
				preview.VolumeContact,
				preview.Rotation,
				preview.VolumeHeight,
				preview.VolumeDiameter,
				preview.IsValid );
			UpdateStackHoverOutline( preview.IsValid );
		}
		else
		{
			_ghost.SyncFromItem( item );
			_ghost.UpdatePose( in preview );
		}
	}

	void UpdateStackHoverOutline( bool valid )
	{
		if ( _ghost == null )
		{
			HoverOutlineRegistrar.Clear();
			return;
		}

		Renderer renderer;
		HoverOutlineVisualSettings settings;
		if ( !_ghost.TryGetStackVolumeOutline( valid, out renderer, out settings ) )
		{
			HoverOutlineRegistrar.Clear();
			return;
		}

		HoverOutlineRegistrar.SetTarget( new[] { renderer }, settings );
	}

	void ConfigureGhostVisuals()
	{
		if ( _ghost == null )
			return;

		PlayerPlacementDefinition def = Definition;
		Color valid = def != null ? def.validGhostColor : PlacementFeedbackColors.ValidGhost;
		Color invalid = def != null ? def.invalidGhostColor : PlacementFeedbackColors.InvalidGhost;
		float fresnelPower = def != null ? def.ghostFresnelPower : 2.4f;
		float fresnelBoost = def != null ? def.ghostFresnelBoost : 0.7f;
		float pulseAmount = def != null ? def.ghostPulseAmount : 0.12f;
		float pulseSpeed = def != null ? def.ghostPulseSpeed : 0.85f;
		float rimIntensity = def != null ? def.ghostRimIntensity : 1.15f;
		float coreIntensity = def != null ? def.ghostCoreIntensity : 0.28f;
		float stackVolumeOversize = def != null ? def.stackVolumeOversize : 1.1f;
		HoverOutlineVisualSettings stackOutline = def != null && def.stackOutline != null
			? def.stackOutline
			: HoverOutlineVisualSettings.DefaultStack();

		_ghost.ConfigureVisuals(
			valid,
			invalid,
			fresnelPower,
			fresnelBoost,
			pulseAmount,
			pulseSpeed,
			rimIntensity,
			coreIntensity,
			stackVolumeOversize,
			stackOutline );
	}

	PlacementPreview SmoothPreview( in PlacementPreview preview )
	{
		PlacementPreview smoothed = preview;
		float speed = PreviewSmoothSpeed;
		float dt = Time.deltaTime;
		if ( !_hasSmoothedPreview || speed <= 0.01f )
		{
			_smoothedPreviewPos = preview.GhostStyle == PlacementGhostStyle.StackVolume
				? preview.VolumeContact
				: preview.Position;
			_smoothedPreviewRot = preview.Rotation;
			_hasSmoothedPreview = true;
			if ( preview.GhostStyle == PlacementGhostStyle.StackVolume )
			{
				smoothed.VolumeContact = _smoothedPreviewPos;
				smoothed.Position = preview.Position;
			}
			return smoothed;
		}

		float t = 1f - Mathf.Exp( -speed * dt );
		Vector3 targetPos = preview.GhostStyle == PlacementGhostStyle.StackVolume
			? preview.VolumeContact
			: preview.Position;
		_smoothedPreviewPos = Vector3.Lerp( _smoothedPreviewPos, targetPos, t );
		_smoothedPreviewRot = Quaternion.Slerp( _smoothedPreviewRot, preview.Rotation, t );
		if ( preview.GhostStyle == PlacementGhostStyle.StackVolume )
		{
			smoothed.VolumeContact = _smoothedPreviewPos;
			smoothed.Position = preview.Position;
		}
		else
			smoothed.Position = _smoothedPreviewPos;
		smoothed.Rotation = _smoothedPreviewRot;
		return smoothed;
	}

	/// <summary>
	/// Attempts placement using the current LateUpdate ghost preview when valid.
	/// </summary>
	public bool TryPlaceAtAim()
	{
		if ( !_inputEnabled || _player == null )
			return false;

		PlayerCarry carry = _player.Carry;
		if ( carry == null || !carry.TryPeekActive( out TreasureItem item ) || item == null )
		{
			PublishFailed( null, null, PlacementFailReason.NoHeldItem );
			return false;
		}

		RefreshGroundStackTuning();
		UpdatePreview();
		if ( HasValidPlacement )
			return TryPlaceFromActivePreview( item );

		if ( TryPlaceWithAutoFindValidSlot( _activeTarget, item ) )
			return true;

		return false;
	}

	/// <summary>
	/// Right-click: place on aimed surface when valid; throw into empty space.
	/// Invalid constellation aim with a non-gem throws normally. Other invalid surfaces do nothing.
	/// </summary>
	public bool TrySecondaryPlace()
	{
		if ( !_inputEnabled || _player == null )
			return false;

		PlayerCarry carry = _player.Carry;
		if ( carry == null || carry.Count <= 0 )
			return false;

		if ( !carry.TryPeekActive( out TreasureItem item ) || item == null )
			return false;

		RefreshGroundStackTuning();
		PlacementQuery query = BuildQuery();

		// Empty space (no aim hit): throw Active item.
		if ( !query.HasHit || query.Hit.collider == null )
			return TryThrowActive();

		// Honor the LateUpdate ghost — preview and place must use the same target.
		if ( _hasPreview )
		{
			if ( _activePreview.IsValid )
				return TryPlaceFromActivePreview( item );

			if ( ShouldThrowAtRejectedConstellation( _activeTarget, item ) )
				return TryThrowActive();

			if ( TryPlaceWithAutoFindValidSlot( _activeTarget, item ) )
				return true;

			// Invalid surface aim: never throw.
			return false;
		}

		if ( TryPlaceLooseVerticalStack( item, in query ) )
			return true;

		ITreasurePlacementTarget aimTarget = ResolveTarget( in query, allowGroundStack: true );

		// Aimed at a dedicated surface: place only if accepted — never throw / soft-drop.
		if ( aimTarget != null && aimTarget != _floorTarget )
		{
			query.AutoFindValidSlot = true;
			if ( !aimTarget.CanPlace( item, in query ) )
			{
				if ( ShouldThrowAtRejectedConstellation( aimTarget, item ) )
					return TryThrowActive();
				return false;
			}

			return ExecutePlace( aimTarget, item, in query );
		}

		// Aimed at open ground / floor surface: place on floor when valid.
		if ( aimTarget == _floorTarget || IsAimingFloorSurface( in query ) )
		{
			if ( TryPlaceLooseVerticalStack( item, in query ) )
				return true;

			if ( TryPlaceOnFloor() )
				return true;

			return false;
		}

		// Aimed at a non-placeable surface (e.g. interactable without placement): do nothing.
		return false;
	}

	static bool ShouldThrowAtRejectedConstellation( ITreasurePlacementTarget target, TreasureItem item )
	{
		if ( !( target is GemConstellationInteractable ) || item == null || item.Definition == null )
			return false;

		return item.Definition.category != TreasureCategory.Gem;
	}

	/// <summary>
	/// Slot-grid display tables keep the preview on the aimed pile (valid or not), but placement
	/// should snap to the nearest open slot when the hovered one is full or mismatched.
	/// </summary>
	static bool SupportsAutoFindValidSlot( ITreasurePlacementTarget target )
	{
		return target is TypedDisplayTableInteractable || target is MixedDisplayTableInteractable;
	}

	bool TryPlaceWithAutoFindValidSlot( ITreasurePlacementTarget target, TreasureItem item )
	{
		if ( !SupportsAutoFindValidSlot( target ) || item == null )
			return false;

		PlacementQuery query = BuildQuery();
		query.AutoFindValidSlot = true;
		if ( !target.CanPlace( item, in query ) )
			return false;

		return ExecutePlace( target, item, in query );
	}

	bool TryPlaceFromActivePreview( TreasureItem item )
	{
		if ( !HasValidPlacement || item == null )
			return false;

		ITreasurePlacementTarget target = _activeTarget;

		// Floor volume ghost for coins means create/join GroundCoinStack, not FloorPlacementTarget.
		if ( target == _floorTarget && GroundCoinStack.IsGroundStackableCoin( item ) )
			return TryPlaceOnFloor();

		PlacementQuery query = target == _floorTarget ? BuildFloorQuery() : BuildQuery();
		query.AutoFindValidSlot = true;

		GroundCoinStack previewStack = target as GroundCoinStack;
		if ( previewStack != null )
		{
			if ( previewStack.IsFull || !previewStack.CanPlace( item, in query ) )
				return false;

			return ExecutePlace( previewStack, item, in query );
		}

		if ( target == _groundStackTarget )
		{
			if ( !_groundStackTarget.CanPlace( item, in query ) )
				return false;

			return ExecutePlace( _groundStackTarget, item, in query );
		}

		if ( !target.CanPlace( item, in query ) )
			return false;

		return ExecutePlace( target, item, in query );
	}

	bool IsAimingFloorSurface( in PlacementQuery query )
	{
		if ( !query.HasHit || query.Hit.collider == null )
			return false;

		return PlacementFloorSurface.IsFloorCollider( query.Hit.collider );
	}

	/// <summary>Throws the Active item into empty space with configurable force.</summary>
	public bool TryThrowActive()
	{
		return TryReleaseToSurface( useSoftVelocity: false );
	}

	/// <summary>
	/// Soft-drop held treasure forward from the hand (legacy / sorting table).
	/// </summary>
	public bool TrySoftDrop()
	{
		return TryReleaseToSurface( useSoftVelocity: true );
	}

	bool TryReleaseToSurface( bool useSoftVelocity )
	{
		if ( !_inputEnabled || _player == null )
			return false;

		PlayerCarry carry = _player.Carry;
		PlayerInteraction interaction = _interaction;
		if ( carry == null || interaction == null )
			return false;

		if ( !carry.TryPeekActive( out TreasureItem item ) || item == null )
		{
			PublishFailed( null, null, PlacementFailReason.NoHeldItem );
			return false;
		}

		Vector3 throwVelocity = useSoftVelocity
			? interaction.GetSoftReleaseVelocity()
			: interaction.GetReleaseVelocity( item );

		if ( !carry.TryRemoveBottomCluster( out System.Collections.Generic.List<TreasureItem> cluster )
			|| cluster == null
			|| cluster.Count == 0 )
			return false;

		TreasureSurfaceWorld world = TreasureSurfaceWorld.EnsureExists();
		TreasureSurfaceDefinition surfaceDef = world.Definition;
		float gravity = surfaceDef != null ? surfaceDef.throwBallisticGravity : 18f;
		float landScale = surfaceDef != null ? surfaceDef.throwLandingSpeedScale : 0.85f;

		Vector3[] ends = new Vector3[ cluster.Count ];
		Quaternion[] rots = new Quaternion[ cluster.Count ];
		Vector3[] landVels = new Vector3[ cluster.Count ];
		float[] flightTimes = new float[ cluster.Count ];

		for ( int i = 0; i < cluster.Count; i++ )
		{
			TreasureItem member = cluster[ i ];
			if ( member == null )
				continue;

			Vector3 start = member.transform.position;
			Vector3 memberVelocity = throwVelocity;

			if ( !TreasureSurfaceThrow.TryPredictLanding(
				world,
				start,
				memberVelocity,
				out Vector3 landPos,
				out Vector3 landVel,
				out float flightTime ) )
			{
				Vector3 planarThrow = new Vector3( throwVelocity.x, 0f, throwVelocity.z );
				Vector3 flatDir = planarThrow.sqrMagnitude > 0.0001f
					? planarThrow.normalized
					: Vector3.forward;

				landPos = start + flatDir * 2f;
				landPos.y = surfaceDef != null ? surfaceDef.baseHeight : start.y;
				landVel = planarThrow * landScale;
				flightTime = 0.35f;
			}

			ends[ i ] = landPos;
			rots[ i ] = TreasureOrientation.FlattenUpright( member.transform.rotation );
			landVels[ i ] = landVel;
			flightTimes[ i ] = flightTime;
			member.BeginFlight();
		}

		// Stack cluster members vertically at the shared landing point.
		float stackedY = 0f;
		for ( int i = 0; i < cluster.Count; i++ )
		{
			if ( cluster[ i ] == null )
				continue;

			ends[ i ].y += stackedY;
			stackedY += TreasureStackSpacing.GetStep( cluster[ i ] );
		}

		TreasureSurfaceThrow.ResolveFlightSpins( item, out float spins );

		TreasureMotionHost.Run( TreasureSurfaceThrow.AnimateThrowCluster(
			cluster,
			throwVelocity,
			gravity,
			ends,
			rots,
			landVels,
			flightTimes,
			spins,
			CoinFlipSpeed ) );

		EventBus.Publish( new PlacementCompletedEvent
		{
			Target = useSoftVelocity ? ( ITreasurePlacementTarget )_floorTarget : null,
			Item = item,
			Definition = item.Definition
		} );
		return true;
	}

	/// <summary>
	/// Right-click: stack held treasure onto aimed loose ground treasure.
	/// </summary>
	public bool TryStackOntoAim()
	{
		if ( !_inputEnabled || _player == null )
			return false;

		PlayerCarry carry = _player.Carry;
		if ( carry == null || !carry.TryPeekActive( out TreasureItem item ) || item == null )
		{
			PublishFailed( null, null, PlacementFailReason.NoHeldItem );
			return false;
		}

		RefreshGroundStackTuning();
		TreasureItem baseItem = ResolveGroundStackBase();
		if ( baseItem == null )
			return false;

		_groundStackTarget.SetBaseItem( baseItem );
		PlacementQuery query = BuildQuery();
		if ( !_groundStackTarget.CanPlace( item, in query ) )
			return false;

		return ExecutePlace( _groundStackTarget, item, in query );
	}

	/// <summary>Floor place when no interactable focus and floor is valid.</summary>
	public bool TryPlaceOnFloor()
	{
		if ( !_inputEnabled || _player == null || _floorTarget == null )
			return false;

		PlayerCarry carry = _player.Carry;
		if ( carry == null || !carry.TryPeekActive( out TreasureItem item ) || item == null )
		{
			PublishFailed( null, null, PlacementFailReason.NoHeldItem );
			return false;
		}

		PlacementQuery query = BuildFloorQuery();

		// Stackable coins never become free floor seeds — create/join an owned stack.
		if ( GroundCoinStack.IsGroundStackableCoin( item ) )
		{
			if ( !CanPlaceCoinOnFloorSurface( item, in query ) )
				return false;

			GroundCoinStack nearby = FindNearbyGroundCoinStack( in query );
			if ( nearby != null )
				return ExecutePlace( nearby, item, in query );

			return TryPlaceCreatingGroundCoinStackOnFloor( item, in query );
		}

		if ( !_floorTarget.CanPlace( item, in query ) )
			return false;

		return ExecutePlace( _floorTarget, item, in query );
	}

	bool CanPlaceCoinOnFloorSurface( TreasureItem item, in PlacementQuery query )
	{
		if ( item == null || !query.HasHit )
			return false;

		return PlacementFloorSurface.IsWalkableFloorHit( in query.Hit );
	}

	bool TryPlaceCreatingGroundCoinStackOnFloor( TreasureItem item, in PlacementQuery query )
	{
		PlayerController player = query.Player;
		PlayerCarry carry = player != null ? player.Carry : null;
		if ( carry == null || item == null || _floorTarget == null )
			return false;

		if ( !_floorTarget.TryGetPlacementPreview( item, in query, out PlacementPreview preview ) )
			return false;

		if ( !carry.TryRemoveBottomCluster( out System.Collections.Generic.List<TreasureItem> cluster )
			|| cluster == null
			|| cluster.Count == 0 )
			return false;

		GroundCoinStack stack = GroundCoinStack.CreateAt( preview.Position, preview.Rotation );
		for ( int i = 0; i < cluster.Count; i++ )
		{
			TreasureItem member = cluster[ i ];
			if ( member == null )
				continue;

			if ( !stack.CanAccept( member.Definition ) )
			{
				member.EnterPhysics( member.transform.position, member.transform.rotation );
				continue;
			}

			stack.BeginAppendFlight( member, stack.transform.rotation );
		}

		stack.AbsorbNearbyLooseCoins();
		stack.TryMergeNearby();
		return true;
	}

	public void NotifyPlacementFailed( PlacementFailReason reason )
	{
		PlayerCarry carry = _player != null ? _player.Carry : null;
		TreasureItem item = null;
		if ( carry != null )
			carry.TryPeekActive( out item );

		PublishFailed( _activeTarget, item, reason );
	}

	bool ExecutePlace( ITreasurePlacementTarget target, TreasureItem item, in PlacementQuery query )
	{
		if ( target == null || item == null )
			return false;

		TreasureDefinition definition = item.Definition;
		PlacementPreview preview;
		if ( !target.TryGetPlacementPreview( item, in query, out preview ) )
			preview = _activePreview;

		EventBus.Publish( new PlacementStartedEvent
		{
			Target = target,
			Item = item,
			Definition = definition,
			Position = preview.Position,
			Rotation = preview.Rotation
		} );

		if ( !target.TryPlace( item, in query ) )
		{
			EventBus.Publish( new PlacementFailedEvent
			{
				Target = target,
				Item = item,
				Definition = definition,
				Reason = PlacementFailReason.RejectedByTarget
			} );
			return false;
		}

		EventBus.Publish( new PlacementCompletedEvent
		{
			Target = target,
			Item = item,
			Definition = definition
		} );
		return true;
	}

	PlacementQuery BuildQuery()
	{
		PlacementQuery query = new PlacementQuery
		{
			Player = _player,
			InteractRange = _interaction != null ? _interaction.InteractRange : 3f
		};

		if ( _interaction != null && _interaction.TryGetLastHit( out RaycastHit hit ) )
		{
			query.Hit = hit;
			query.HasHit = true;
		}

		return query;
	}

	/// <summary>
	/// Floor placement uses the closest floor surface along the aim ray.
	/// Coin piles and other interactables are never treated as floor.
	/// </summary>
	PlacementQuery BuildFloorQuery()
	{
		PlacementQuery query = new PlacementQuery
		{
			Player = _player,
			InteractRange = _interaction != null ? _interaction.InteractRange : 3f
		};

		if ( _interaction != null && _interaction.TryGetSurfaceHit( out RaycastHit surfaceHit ) )
		{
			query.Hit = surfaceHit;
			query.HasHit = true;
		}

		return query;
	}

	ITreasurePlacementTarget ResolveTarget( in PlacementQuery query, bool allowGroundStack = true )
	{
		if ( !query.HasHit || query.Hit.collider == null )
			return null;

		// Never resolve placement onto targets behind a closer floor surface (e.g. gold pile under floor).
		// Loose coin columns on the floor still win — the walkable surface is always closer than the coin.
		if ( IsHitOccludedByCloserFloor( in query ) && !IsAimingLooseStackDeposit( in query ) )
		{
			EnsureFloorTarget();
			return _floorTarget;
		}

		// Never resolve floor placement onto coin piles — deposit uses ITreasurePlacementTarget above.
		if ( !PlacementFloorSurface.IsFloorCollider( query.Hit.collider ) )
		{
			if ( allowGroundStack && TryResolveLooseVerticalStackTarget( in query, out ITreasurePlacementTarget groundStack ) )
				return groundStack;

			ITreasurePlacementTarget onHit = query.Hit.collider.GetComponentInParent<ITreasurePlacementTarget>();
			if ( onHit != null && !ReferenceEquals( onHit, _floorTarget ) )
				return onHit;

			return null;
		}

		if ( allowGroundStack && TryResolveLooseVerticalStackTarget( in query, out ITreasurePlacementTarget floorNearbyStack ) )
			return floorNearbyStack;

		EnsureFloorTarget();
		return _floorTarget;
	}

	bool IsHitOccludedByCloserFloor( in PlacementQuery query )
	{
		if ( !query.HasHit || query.Hit.collider == null )
			return false;

		if ( PlacementFloorSurface.IsFloorCollider( query.Hit.collider ) )
			return false;

		if ( _interaction == null || !_interaction.TryGetSurfaceHit( out RaycastHit surfaceHit ) )
			return false;

		return surfaceHit.distance < query.Hit.distance - 0.001f;
	}

	TreasureItem ResolveGroundStackBase()
	{
		return ResolveGroundStackBase( BuildQuery() );
	}

	bool IsHeldItemStackable()
	{
		PlayerCarry carry = _player != null ? _player.Carry : null;
		if ( carry == null || !carry.TryPeekActive( out TreasureItem held ) || held == null )
			return false;

		return GroundCoinStack.IsGroundStackableCoin( held );
	}

	bool TryPlaceLooseVerticalStack( TreasureItem item, in PlacementQuery query )
	{
		if ( item == null || !query.HasHit )
			return false;

		if ( !GroundCoinStack.IsGroundStackableCoin( item ) )
			return false;

		GroundCoinStack owned = ResolveGroundCoinStack( in query );
		if ( owned != null )
			return ExecutePlace( owned, item, in query );

		TreasureItem baseItem = ResolveGroundStackBase( in query );
		if ( baseItem == null || !GroundCoinStack.IsGroundStackableCoin( baseItem ) )
			return false;

		return TryPlaceCreatingGroundCoinStack( item, baseItem, in query );
	}

	/// <summary>
	/// Prefer an existing owned stack near the aim contact. Spam-clicking often hits floor beside
	/// the capsule instead of the stack itself — without this, each click seeds a new loose coin.
	/// </summary>
	GroundCoinStack FindNearbyGroundCoinStack( in PlacementQuery query )
	{
		if ( query.HasHit && query.Hit.collider != null )
		{
			GroundCoinStack onHit = query.Hit.collider.GetComponentInParent<GroundCoinStack>();
			if ( onHit != null && !onHit.IsFull )
				return onHit;
		}

		float radius = GroundStackSnapRadius;
		if ( radius <= 0.0001f )
			radius = GroundCoinStack.DefaultJoinRadius;

		Vector3 probe = query.HasHit ? query.Hit.point : default;
		if ( !query.HasHit )
		{
			if ( _interaction == null || !_interaction.TryGetAimRay( out Ray aimRay ) )
				return null;

			float range = query.InteractRange > 0.01f ? query.InteractRange : _interaction.InteractRange;
			probe = aimRay.GetPoint( Mathf.Min( range, 8f ) );
		}

		GroundCoinStack nearest = GroundCoinStack.FindNearest( probe, radius );
		if ( nearest != null && !nearest.IsFull )
			return nearest;

		// Also score stacks by perpendicular distance to the aim ray (hit can be floor underfoot
		// while the stack sits slightly off-axis within snap radius).
		if ( _interaction != null && _interaction.TryGetAimRay( out Ray ray ) )
			return GroundCoinStack.FindNearestAlongRay( ray, radius, query.InteractRange > 0.01f ? query.InteractRange : 8f );

		return null;
	}

	bool TryPlaceCreatingGroundCoinStack( TreasureItem item, TreasureItem baseItem, in PlacementQuery query )
	{
		PlayerController player = query.Player;
		PlayerCarry carry = player != null ? player.Carry : null;
		if ( carry == null || baseItem == null || item == null )
			return false;

		if ( !carry.TryRemoveBottomCluster( out System.Collections.Generic.List<TreasureItem> cluster )
			|| cluster == null
			|| cluster.Count == 0 )
			return false;

		TreasureItem incoming = cluster[ 0 ];
		Vector3 contact = baseItem.transform.position;
		Quaternion rot = baseItem.transform.rotation;

		// Prefer any nearby owned stack (aim hit / snap), not only the base coin's owner.
		GroundCoinStack stack = FindNearbyGroundCoinStack( in query );
		if ( stack == null )
			stack = GroundCoinStack.FindStackForLooseCoin( baseItem );

		if ( stack == null )
		{
			stack = GroundCoinStack.CreateAt( contact, rot );
			stack.AbsorbSettledImmediate( baseItem );
		}
		else if ( !( baseItem.Owner is GroundCoinStack owned && owned == stack ) )
		{
			stack.TryAbsorbLooseImmediate( baseItem );
		}

		for ( int i = 0; i < cluster.Count; i++ )
		{
			TreasureItem member = cluster[ i ];
			if ( member == null )
				continue;

			if ( !stack.CanAccept( member.Definition ) )
			{
				member.EnterPhysics( member.transform.position, member.transform.rotation );
				continue;
			}

			stack.BeginAppendFlight( member, stack.transform.rotation );
		}

		stack.AbsorbNearbyLooseCoins();
		stack.TryMergeNearby();
		return true;
	}

	bool TryResolveLooseVerticalStackTarget( in PlacementQuery query, out ITreasurePlacementTarget target )
	{
		target = null;
		if ( !query.HasHit || !IsHeldItemStackable() )
			return false;

		GroundCoinStack owned = ResolveGroundCoinStack( in query );
		if ( owned != null )
		{
			target = owned;
			return true;
		}

		PlayerCarry carry = _player != null ? _player.Carry : null;
		if ( carry == null || !carry.TryPeekActive( out TreasureItem held ) || held == null )
			return false;

		TreasureItem baseItem = ResolveGroundStackBase( in query );
		if ( baseItem == null || !GroundCoinStack.IsGroundStackableCoin( baseItem ) )
			return false;

		// Preview uses a transient wrapper around creating/joining via owned stack ghost pose.
		_groundStackTarget.SetBaseItem( baseItem );
		target = _groundStackTarget;
		return true;
	}

	GroundCoinStack ResolveGroundCoinStack( in PlacementQuery query )
	{
		PlayerCarry carry = _player != null ? _player.Carry : null;
		if ( carry == null || !carry.TryPeekActive( out TreasureItem held ) || held == null )
			return null;

		if ( !GroundCoinStack.IsGroundStackableCoin( held ) )
			return null;

		return FindNearbyGroundCoinStack( in query );
	}

	bool IsAimingLooseStackDeposit( in PlacementQuery query )
	{
		if ( !query.HasHit || !IsHeldItemStackable() )
			return false;

		if ( ResolveGroundCoinStack( in query ) != null )
			return true;

		PlayerCarry carry = _player != null ? _player.Carry : null;
		if ( carry == null || !carry.TryPeekActive( out TreasureItem held ) || held == null )
			return false;

		TreasureItem baseItem = ResolveGroundStackBase( in query );
		if ( baseItem == null )
			return false;

		return GroundCoinStack.IsGroundStackableCoin( baseItem )
			&& GroundTreasureStackTarget.CanStackLoose( held, baseItem );
	}

	TreasureItem ResolveGroundStackBase( in PlacementQuery query )
	{
		PlayerCarry carry = _player != null ? _player.Carry : null;
		if ( carry == null || !carry.TryPeekActive( out TreasureItem held ) || held == null )
			return null;

		TreasureItem fromHit = null;
		if ( query.HasHit && query.Hit.collider != null )
			fromHit = ResolveGroundStackBaseFromHit( query.Hit );

		TreasureItem fromSnap = FindStackSnapColumnBottom( in query, held );
		if ( fromHit == null )
			return fromSnap;

		if ( fromSnap == null )
			return fromHit;

		TreasureItem hitColumn = TreasureSupportStack.FindColumnBottom( fromHit );
		TreasureItem snapColumn = TreasureSupportStack.FindColumnBottom( fromSnap );
		if ( hitColumn == snapColumn )
			return hitColumn;

		return PickCloserStackColumn( hitColumn, snapColumn, in query );
	}

	TreasureItem FindStackSnapColumnBottom( in PlacementQuery query, TreasureItem held )
	{
		if ( held == null || _interaction == null )
			return null;

		float radius = GroundStackSnapRadius;
		if ( radius <= 0.0001f )
			return null;

		float range = query.InteractRange > 0.01f ? query.InteractRange : _interaction.InteractRange;
		LayerMask mask = _interaction != null ? _interaction.InteractMask : (LayerMask)Physics.DefaultRaycastLayers;
		StackSnapColumnIds.Clear();

		TreasureItem best = null;
		float bestScore = float.MaxValue;

		if ( _interaction.TryGetAimRay( out Ray aimRay ) )
		{
			int castCount = Physics.SphereCastNonAlloc(
				aimRay.origin,
				radius,
				aimRay.direction,
				StackSnapSphereCastHits,
				range,
				mask,
				QueryTriggerInteraction.Ignore );

			for ( int i = 0; i < castCount; i++ )
			{
				Collider col = StackSnapSphereCastHits[ i ].collider;
				if ( col == null )
					continue;

				TryRegisterStackSnapCandidate( col, held, in query, ref best, ref bestScore );
			}
		}

		if ( query.HasHit )
		{
			Vector3 center = query.Hit.point;
			int overlapCount = Physics.OverlapSphereNonAlloc(
				center,
				radius,
				StackSnapOverlap,
				mask,
				QueryTriggerInteraction.Ignore );

			for ( int i = 0; i < overlapCount; i++ )
			{
				Collider col = StackSnapOverlap[ i ];
				if ( col == null )
					continue;

				TryRegisterStackSnapCandidate( col, held, in query, ref best, ref bestScore );
			}
		}

		return best;
	}

	bool TryRegisterStackSnapCandidate(
		Collider col,
		TreasureItem held,
		in PlacementQuery query,
		ref TreasureItem best,
		ref float bestScore )
	{
		if ( col == null || held == null )
			return false;

		TreasureItem item = col.GetComponentInParent<TreasureItem>();
		if ( !TryGetLooseStackColumnBottom( item, held, out TreasureItem columnBottom ) )
			return false;

		int id = columnBottom.GetInstanceID();
		if ( !StackSnapColumnIds.Add( id ) )
			return false;

		float score = ScoreStackColumnForSnap( columnBottom, in query );
		if ( score >= bestScore )
			return false;

		bestScore = score;
		best = columnBottom;
		return true;
	}

	static bool TryGetLooseStackColumnBottom( TreasureItem item, TreasureItem held, out TreasureItem columnBottom )
	{
		columnBottom = null;
		if ( item == null || held == null || !item.IsWorldLoose || item.IsReclaiming )
			return false;

		if ( item.Definition == null )
			return false;

		if ( item.Definition.category == TreasureCategory.Gem || !item.Definition.canStack )
			return false;

		columnBottom = TreasureSupportStack.FindColumnBottom( item );
		if ( columnBottom == null )
			return false;

		return GroundTreasureStackTarget.CanStackLoose( held, columnBottom );
	}

	float ScoreStackColumnForSnap( TreasureItem columnBottom, in PlacementQuery query )
	{
		if ( columnBottom == null )
			return float.MaxValue;

		TreasureSupportStack.CollectColumn( columnBottom, StackSnapColumnBuffer );
		Vector3 sample = columnBottom.transform.position;
		if ( StackSnapColumnBuffer.Count > 0 )
		{
			TreasureItem top = StackSnapColumnBuffer[ StackSnapColumnBuffer.Count - 1 ];
			if ( top != null )
				sample = top.transform.position;
		}

		StackSnapColumnBuffer.Clear();

		if ( _interaction != null && _interaction.TryGetAimRay( out Ray ray ) )
		{
			Vector3 toSample = sample - ray.origin;
			float perp = Vector3.Cross( ray.direction, toSample ).magnitude;
			float along = Vector3.Dot( ray.direction, toSample );
			if ( along < 0f )
				perp += 2f;

			return perp;
		}

		if ( query.HasHit )
			return ( sample - query.Hit.point ).sqrMagnitude;

		return sample.sqrMagnitude;
	}

	TreasureItem PickCloserStackColumn( TreasureItem a, TreasureItem b, in PlacementQuery query )
	{
		float scoreA = ScoreStackColumnForSnap( a, in query );
		float scoreB = ScoreStackColumnForSnap( b, in query );
		return scoreA <= scoreB ? a : b;
	}

	static TreasureItem ResolveGroundStackBaseFromHit( RaycastHit hit )
	{
		if ( hit.collider == null )
			return null;

		TreasureItem item = hit.collider.GetComponentInParent<TreasureItem>();
		if ( item == null || !item.IsWorldLoose || item.IsReclaiming )
			return null;

		if ( item.Definition == null )
			return null;

		if ( item.Definition.category == TreasureCategory.Gem || !item.Definition.canStack )
			return null;

		// Any stackable item in a column resolves to the column bottom; placement still uses the top.
		return TreasureSupportStack.FindColumnBottom( item );
	}

	void RefreshGroundStackTuning()
	{
		if ( _groundStackTarget == null )
		{
			_groundStackTarget = new GroundTreasureStackTarget(
				GroundStackUpBias,
				GroundStackReleaseSpeedScale,
				GroundStackReleaseUpScale );
			return;
		}

		_groundStackTarget.SetTuning(
			GroundStackUpBias,
			GroundStackReleaseSpeedScale,
			GroundStackReleaseUpScale );
	}

	void EnsureFloorTarget()
	{
		if ( _floorTarget == null )
			_floorTarget = new FloorPlacementTarget( DropUpBias );
	}

	void EnsureGhost()
	{
		if ( _ghost != null )
			return;

		_ghost = new PlacementGhost();
	}

	void UpdateGroundStackPreviewReservation( TreasureItem placing )
	{
		if ( placing == null || _groundStackTarget == null || _groundStackTarget.BaseItem == null )
			return;

		TreasureItem bottom = TreasureSupportStack.FindColumnBottom( _groundStackTarget.BaseItem );
		if ( bottom == null )
			return;

		float height = TreasureStackSpacing.GetStep( placing );
		GroundTreasureStackTarget.SetPreviewReservation( bottom, height );
	}

	static bool ShouldSuppressPlacementGhost( ITreasurePlacementTarget target, in PlacementQuery query )
	{
		if ( target != null && PlacementFloorSurface.IsHeightfieldPilePlacementTarget( target ) )
			return true;

		if ( query.HasHit && query.Hit.collider != null
			&& PlacementFloorSurface.IsHeightfieldPileCollider( query.Hit.collider ) )
			return true;

		return false;
	}

	void ClearPreview()
	{
		GroundTreasureStackTarget.ClearPreviewReservation();
		_activeTarget = null;
		_hasPreview = false;
		_activePreview = default;
		_hasSmoothedPreview = false;
		HoverOutlineRegistrar.Clear();
		if ( _ghost != null )
			_ghost.SetVisible( false );
	}

	static void PublishFailed( ITreasurePlacementTarget target, TreasureItem item, PlacementFailReason reason )
	{
		EventBus.Publish( new PlacementFailedEvent
		{
			Target = target,
			Item = item,
			Definition = item != null ? item.Definition : null,
			Reason = reason
		} );
	}
}
