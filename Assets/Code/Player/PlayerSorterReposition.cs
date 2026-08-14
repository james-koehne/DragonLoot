using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Dedicated telekinetic carry session for <see cref="CoinSortingStation"/>.
/// Treasure buckets stay intact; this is not part of <see cref="PlayerCarry"/>.
/// </summary>
[DisallowMultipleComponent]
public class PlayerSorterReposition : MonoBehaviour
{
	enum Phase
	{
		Idle,
		Charging,
		Carrying,
		Settling
	}

	static readonly List<Collider> ColliderScratch = new List<Collider>( 16 );
	static readonly List<Renderer> OutlineScratch = new List<Renderer>( 16 );
	static readonly Collider[] OverlapScratch = new Collider[ 64 ];
	static InteractionProgressRingUI s_ring;

	PlayerController _player;
	GameInput _input;
	Phase _phase;
	CoinSortingStation _station;
	PlayerPlacementDefinition _placementDef;

	float _charge;
	float _holdSeconds = 0.5f;
	float _yawDegrees;
	float _smoothedYaw;
	float _yawVelocity;
	Vector3 _followVelocity;
	float _bobTime;
	bool _placementValid;
	Vector3 _placePosition;
	Quaternion _placeRotation = Quaternion.identity;
	Vector3 _settleStartPos;
	Quaternion _settleStartRot = Quaternion.identity;
	Vector3 _settleEndPos;
	Quaternion _settleEndRot = Quaternion.identity;
	float _settleElapsed;
	float _settleDuration = 0.28f;
	Collider[] _ignoredPlayerColliders;
	bool _playerCollisionIgnored;
	bool _hasLastValidPose;
	Vector3 _lastValidPos;
	Quaternion _lastValidRot = Quaternion.identity;

	public bool IsBusy => _phase != Phase.Idle;
	public bool IsCharging => _phase == Phase.Charging;
	public bool IsCarrying => _phase == Phase.Carrying || _phase == Phase.Settling;
	public bool IsRepositioning => IsCarrying;
	public bool HasValidPlacement => _phase == Phase.Carrying && _placementValid;
	public CoinSortingStation ActiveStation => _station;

	public float ChargeProgress01
	{
		get
		{
			if ( _phase != Phase.Charging || _holdSeconds <= 0.01f )
				return 0f;
			return Mathf.Clamp01( _charge / _holdSeconds );
		}
	}

	public static void Register( InteractionProgressRingUI ring )
	{
		s_ring = ring;
	}

	public void Setup( PlayerController player )
	{
		_player = player;
	}

	void OnDestroy()
	{
		RestorePlayerCollision();
		ClearValidityOutline();
	}

	void Update()
	{
		if ( _player == null )
			return;

		if ( !_player.GameplayInputEnabled )
		{
			if ( _phase == Phase.Charging )
				CancelCharge();
			else if ( _phase == Phase.Settling )
				TickSettle();
			return;
		}

		GameInput input = ResolveInput();
		switch ( _phase )
		{
			case Phase.Idle:
			case Phase.Charging:
				TickCharge( input );
				break;
			case Phase.Carrying:
				TickCarry( input );
				break;
			case Phase.Settling:
				TickSettle();
				break;
		}
	}

	void LateUpdate()
	{
		UpdateChargeRing();
		if ( _phase == Phase.Carrying )
			UpdateValidityOutline();
	}

	void FixedUpdate()
	{
		if ( _phase != Phase.Carrying || _station == null )
			return;

		TickFloatPhysics( Time.fixedDeltaTime );
	}

	void TickCharge( GameInput input )
	{
		if ( input == null || input.Interact == null || !input.Interact.IsPressed() )
		{
			CancelCharge();
			return;
		}

		PlayerInteraction interaction = _player.Interaction;

		if ( _phase == Phase.Charging && _station != null )
		{
			if ( !IsStillAimingMoveTarget( interaction, _station ) )
			{
				CancelCharge();
				return;
			}

			_charge += Time.deltaTime;
			if ( _charge < _holdSeconds )
				return;

			BeginCarry( _station );
			return;
		}

		CoinSortingStationMoveInteractable move =
			interaction != null ? interaction.Current as CoinSortingStationMoveInteractable : null;

		if ( move == null || !move.CanInteract( _player ) )
			return;

		CoinSortingStation station = move.Station;
		if ( station == null )
			return;

		_phase = Phase.Charging;
		_station = station;
		_charge = 0f;
		RefreshHoldSeconds( station );
		_charge += Time.deltaTime;
		if ( _charge < _holdSeconds )
			return;

		BeginCarry( station );
	}

	static bool IsStillAimingMoveTarget( PlayerInteraction interaction, CoinSortingStation station )
	{
		if ( interaction == null || station == null )
			return false;

		CoinSortingStationMoveInteractable move = interaction.Current as CoinSortingStationMoveInteractable;
		if ( move != null && move.Station == station )
			return true;

		if ( !interaction.TryGetLastHit( out RaycastHit hit ) || hit.collider == null )
			return false;

		if ( hit.collider.GetComponentInParent<CoinSortingCrankInteractable>() != null )
			return false;

		CoinSortingStation hitStation = hit.collider.GetComponentInParent<CoinSortingStation>();
		return hitStation == station;
	}

	void CancelCharge()
	{
		if ( _phase != Phase.Charging )
			return;

		_phase = Phase.Idle;
		_station = null;
		_charge = 0f;
	}

	void RefreshHoldSeconds( CoinSortingStation station )
	{
		CoinSortingStationDefinition def = station != null ? station.Definition : null;
		_holdSeconds = def != null ? Mathf.Max( 0.1f, def.moveHoldSeconds ) : 0.5f;
	}

	void BeginCarry( CoinSortingStation station )
	{
		if ( station == null )
			return;

		_station = station;
		_phase = Phase.Carrying;
		_charge = 0f;
		_followVelocity = Vector3.zero;
		_bobTime = 0f;
		_placementValid = false;

		float yaw = station.transform.eulerAngles.y;
		_yawDegrees = yaw;
		_smoothedYaw = yaw;

		_lastValidPos = station.transform.position;
		_lastValidRot = station.transform.rotation;
		// Ground contact is normal at pickup — do not treat floor support as "invalid/stuck".
		_hasLastValidPose = true;

		station.BeginRepositioning();
		IgnorePlayerCollision( station );

		if ( _player != null )
			_player.CancelSlideVelocity();

		UpdateValidityOutline();
	}

	void TickCarry( GameInput input )
	{
		if ( _station == null || !_station.isActiveAndEnabled )
		{
			ForceEndCarry();
			return;
		}

		ApplyRotationInput( input );

		if ( input != null
			&& input.SecondaryInteract != null
			&& input.SecondaryInteract.WasPressedThisFrame() )
		{
			TryConfirmPlace();
		}
	}

	void ApplyRotationInput( GameInput input )
	{
		if ( input == null )
			return;

		CoinSortingStationDefinition def = _station != null ? _station.Definition : null;
		float step = def != null ? def.rotateStepDegrees : 15f;

		if ( input.ScrollWheel != null )
		{
			Vector2 scroll = input.ScrollWheel.ReadValue<Vector2>();
			if ( Mathf.Abs( scroll.y ) > 0.01f )
				_yawDegrees += Mathf.Sign( scroll.y ) * step;
		}

		if ( input.RotateLeft != null && input.RotateLeft.WasPressedThisFrame() )
			_yawDegrees -= step;
		if ( input.RotateRight != null && input.RotateRight.WasPressedThisFrame() )
			_yawDegrees += step;
	}

	void TickFloatPhysics( float dt )
	{
		if ( _station == null || _player == null )
			return;

		CoinSortingStationDefinition def = _station.Definition;
		float distance = def != null ? def.carryDistance : 1.8f;
		float minDistance = def != null ? def.minCarryDistance : 1.45f;
		float heightOffset = def != null ? def.carryHeightOffset : -0.55f;
		float floorClearance = def != null ? def.floorHoverClearance : 0.08f;
		float unstickDistance = def != null ? def.unstickSearchDistance : 1.1f;
		float followSmooth = def != null ? def.followSmoothTime : 0.18f;
		float yawSmooth = def != null ? def.yawInertiaSmoothTime : 0.22f;
		float bobAmp = def != null ? def.bobAmplitude : 0.04f;
		float bobFreq = def != null ? def.bobFrequency : 1.6f;
		float bobFullSpeed = def != null ? def.bobFullSpeed : 5f;

		Transform cam = ResolveCameraTransform();
		if ( cam == null )
			return;

		Vector3 camForwardFlat = cam.forward;
		camForwardFlat.y = 0f;
		if ( camForwardFlat.sqrMagnitude < 0.0001f )
			camForwardFlat = _player.transform.forward;
		camForwardFlat.Normalize();

		_smoothedYaw = Mathf.SmoothDampAngle( _smoothedYaw, _yawDegrees, ref _yawVelocity, yawSmooth, Mathf.Infinity, dt );
		Quaternion desiredRot = Quaternion.Euler( 0f, _smoothedYaw, 0f );

		Vector3 desiredPos = cam.position + camForwardFlat * distance;
		desiredPos.y = cam.position.y + heightOffset;

		float speed01 = Mathf.Clamp01( _player.PlanarSpeed / Mathf.Max( 0.1f, bobFullSpeed ) );
		_bobTime += dt * bobFreq * ( 0.35f + speed01 );
		desiredPos.y += Mathf.Sin( _bobTime * Mathf.PI * 2f ) * bobAmp * speed01;

		desiredPos = EnforceMinPlayerDistance( desiredPos, camForwardFlat, minDistance );
		desiredPos = EnforceFloorClearance( desiredPos, desiredRot, floorClearance );

		Vector3 current = _station.transform.position;
		Quaternion currentRot = _station.transform.rotation;

		// Never rotate into a wall — keep previous yaw if the new facing clips.
		Quaternion moveRot = desiredRot;
		if ( IsOverlappingAt( current, moveRot ) && !IsOverlappingAt( current, currentRot ) )
			moveRot = currentRot;

		Vector3 target = Vector3.SmoothDamp( current, desiredPos, ref _followVelocity, followSmooth, Mathf.Infinity, dt );
		target = EnforceMinPlayerDistance( target, camForwardFlat, minDistance );
		target = EnforceFloorClearance( target, moveRot, floorClearance );

		Vector3 next = MoveWithoutEnteringGeometry( current, target, moveRot, camForwardFlat, unstickDistance );

		// Min-distance can shove the sorter into a wall — re-constrain afterward.
		Vector3 afterMin = EnforceMinPlayerDistance( next, camForwardFlat, minDistance );
		afterMin = EnforceFloorClearance( afterMin, moveRot, floorClearance );
		if ( afterMin != next )
			next = MoveWithoutEnteringGeometry( next, afterMin, moveRot, camForwardFlat, unstickDistance );

		if ( IsOverlappingAt( next, moveRot ) )
		{
			if ( _hasLastValidPose )
			{
				next = _lastValidPos;
				moveRot = _lastValidRot;
				_followVelocity = Vector3.zero;
			}
			else
			{
				next = ResolveStuckPosition( current, moveRot, -camForwardFlat, unstickDistance );
			}
		}

		if ( !IsOverlappingAt( next, moveRot ) )
		{
			_lastValidPos = next;
			_lastValidRot = moveRot;
			_hasLastValidPose = true;
		}

		Rigidbody body = _station.Body;
		if ( body != null )
		{
			body.MovePosition( next );
			body.MoveRotation( moveRot );
		}
		else
		{
			_station.transform.SetPositionAndRotation( next, moveRot );
		}

		RefreshPlacementValidity();
	}

	Vector3 EnforceMinPlayerDistance( Vector3 position, Vector3 fallbackForward, float minDistance )
	{
		if ( _player == null || minDistance <= 0.01f )
			return position;

		Vector3 playerPos = _player.transform.position;
		Vector3 planar = position - playerPos;
		planar.y = 0f;
		float planarDist = planar.magnitude;
		if ( planarDist >= minDistance )
			return position;

		Vector3 away = planarDist > 0.001f ? planar / planarDist : fallbackForward;
		if ( away.sqrMagnitude < 0.0001f )
			away = Vector3.forward;
		away.Normalize();

		Vector3 pushed = playerPos + away * minDistance;
		pushed.y = position.y;
		return pushed;
	}

	Vector3 EnforceFloorClearance( Vector3 position, Quaternion rotation, float clearance )
	{
		if ( _station == null )
			return position;

		float bottomLocalY = ResolveLocalBottomY();
		float probeHeight = 3f;
		Vector3 origin = position + Vector3.up * probeHeight;
		if ( !Physics.Raycast(
			origin,
			Vector3.down,
			out RaycastHit hit,
			probeHeight + 2f,
			Physics.DefaultRaycastLayers,
			QueryTriggerInteraction.Ignore ) )
		{
			return position;
		}

		if ( _station.IsOwnCollider( hit.collider ) )
			return position;

		if ( !PlacementFloorSurface.IsFloorCollider( hit.collider )
			&& !PlacementFloorSurface.IsWalkableFloorHit( in hit ) )
			return position;

		float minRootY = hit.point.y - bottomLocalY + Mathf.Max( 0f, clearance );
		if ( position.y < minRootY )
			position.y = minRootY;

		return position;
	}

	float ResolveLocalBottomY()
	{
		if ( _station == null )
			return 0f;

		Bounds combined = _station.GetCombinedPhysicalBounds();
		return combined.min.y - _station.transform.position.y;
	}

	/// <summary>
	/// Moves as far as possible toward <paramref name="to"/> without overlapping world geometry.
	/// Never advances into a blocked volume; slides along axes when possible.
	/// </summary>
	Vector3 MoveWithoutEnteringGeometry(
		Vector3 from,
		Vector3 to,
		Quaternion rotation,
		Vector3 preferredAway,
		float unstickDistance )
	{
		if ( IsOverlappingAt( from, rotation ) )
		{
			// Escape only — never chase the follow target through solids.
			Vector3 freed = ResolveStuckPosition( from, rotation, preferredAway, unstickDistance );
			if ( !IsOverlappingAt( freed, rotation ) )
				return freed;

			if ( _hasLastValidPose && !IsOverlappingAt( _lastValidPos, _lastValidRot ) )
				return _lastValidPos;

			return from;
		}

		if ( !IsOverlappingAt( to, rotation ) )
			return to;

		// Sweep / binary search the last free point along the path.
		Vector3 lo = from;
		Vector3 hi = to;
		for ( int i = 0; i < 12; i++ )
		{
			Vector3 mid = ( lo + hi ) * 0.5f;
			if ( IsOverlappingAt( mid, rotation ) )
				hi = mid;
			else
				lo = mid;
		}

		// Skin: back off slightly from the contact surface.
		Vector3 delta = to - from;
		if ( delta.sqrMagnitude > 0.0001f )
			lo -= delta.normalized * 0.02f;

		if ( IsOverlappingAt( lo, rotation ) )
			lo = from;

		// Axis slides for remaining motion (stairs / corners).
		lo = TryAxisSlide( lo, to, rotation );
		return lo;
	}

	Vector3 TryAxisSlide( Vector3 from, Vector3 to, Quaternion rotation )
	{
		Vector3 result = from;
		Vector3 rem = to - result;

		Vector3 candX = result + new Vector3( rem.x, 0f, 0f );
		if ( !IsOverlappingAt( candX, rotation ) )
			result = candX;

		rem = to - result;
		Vector3 candZ = result + new Vector3( 0f, 0f, rem.z );
		if ( !IsOverlappingAt( candZ, rotation ) )
			result = candZ;

		rem = to - result;
		Vector3 candY = result + new Vector3( 0f, rem.y, 0f );
		if ( !IsOverlappingAt( candY, rotation ) )
			result = candY;

		// Combined horizontal slide if single-axis failed.
		rem = to - result;
		Vector3 candXZ = result + new Vector3( rem.x, 0f, rem.z );
		if ( !IsOverlappingAt( candXZ, rotation ) )
			result = candXZ;

		return result;
	}

	Vector3 ResolveStuckPosition(
		Vector3 position,
		Quaternion rotation,
		Vector3 preferredAway,
		float searchDistance )
	{
		position = ResolvePenetration( position, rotation );
		if ( !IsOverlappingAt( position, rotation ) )
			return position;

		if ( preferredAway.sqrMagnitude < 0.0001f )
			preferredAway = Vector3.forward;
		preferredAway.Normalize();

		Vector3 right = Vector3.Cross( Vector3.up, preferredAway );
		if ( right.sqrMagnitude < 0.0001f )
			right = Vector3.right;
		right.Normalize();

		Vector3 towardPlayer = Vector3.zero;
		if ( _player != null )
		{
			towardPlayer = _player.transform.position - position;
			towardPlayer.y = 0f;
			if ( towardPlayer.sqrMagnitude > 0.0001f )
				towardPlayer.Normalize();
		}

		Vector3[] dirs =
		{
			towardPlayer.sqrMagnitude > 0.0001f ? towardPlayer : -preferredAway,
			-preferredAway,
			Vector3.up,
			preferredAway,
			right,
			-right,
			( -preferredAway + Vector3.up ).normalized,
			( towardPlayer + Vector3.up ).normalized,
			( right + Vector3.up ).normalized,
			( -right + Vector3.up ).normalized
		};

		float[] steps = { 0.08f, 0.16f, 0.28f, 0.42f, 0.6f, 0.85f, searchDistance };

		for ( int d = 0; d < dirs.Length; d++ )
		{
			Vector3 dir = dirs[ d ];
			if ( dir.sqrMagnitude < 0.0001f )
				continue;

			for ( int s = 0; s < steps.Length; s++ )
			{
				float step = steps[ s ];
				if ( step > searchDistance + 0.001f )
					break;

				Vector3 candidate = position + dir * step;
				candidate = ResolvePenetration( candidate, rotation );
				if ( !IsOverlappingAt( candidate, rotation ) )
					return candidate;
			}
		}

		if ( _hasLastValidPose && !IsOverlappingAt( _lastValidPos, _lastValidRot ) )
			return _lastValidPos;

		return position;
	}

	bool IsOverlappingAt( Vector3 position, Quaternion rotation )
	{
		if ( _station == null )
			return false;

		_station.CollectPhysicalColliders( ColliderScratch );
		for ( int i = 0; i < ColliderScratch.Count; i++ )
		{
			Collider col = ColliderScratch[ i ];
			if ( col == null )
				continue;

			if ( !TryGetWorldBox( col, position, rotation, out Vector3 center, out Vector3 half, out Quaternion orient ) )
				continue;

			// Slight inflate so we refuse near-flush embeds before they become stuck.
			int hits = Physics.OverlapBoxNonAlloc(
				center,
				half * 1.02f,
				OverlapScratch,
				orient,
				Physics.DefaultRaycastLayers,
				QueryTriggerInteraction.Ignore );

			for ( int h = 0; h < hits; h++ )
			{
				Collider other = OverlapScratch[ h ];
				if ( other == null || _station.IsOwnCollider( other ) )
					continue;
				if ( IsIgnoredPlayerCollider( other ) )
					continue;
				if ( other.isTrigger )
					continue;
				if ( IsSupportingFloorOverlap( other, center ) )
					continue;
				return true;
			}
		}

		return false;
	}

	static bool IsSupportingFloorOverlap( Collider other, Vector3 queryCenter )
	{
		if ( other == null )
			return false;

		if ( !PlacementFloorSurface.IsFloorCollider( other ) )
			return false;

		Vector3 closest = other.ClosestPoint( queryCenter );
		Vector3 away = queryCenter - closest;
		if ( away.sqrMagnitude < 0.0001f )
		{
			// Fully inside / degenerate — only treat as support if the collider sits mostly below.
			return other.bounds.center.y <= queryCenter.y;
		}

		// Supporting contact pushes mostly upward into the sorter.
		return Vector3.Dot( away.normalized, Vector3.up ) > 0.45f;
	}

	Vector3 ResolvePenetration( Vector3 position, Quaternion rotation )
	{
		_station.CollectPhysicalColliders( ColliderScratch );

		for ( int pass = 0; pass < 10; pass++ )
		{
			bool pushed = false;
			Vector3 accum = Vector3.zero;
			int pushCount = 0;

			for ( int i = 0; i < ColliderScratch.Count; i++ )
			{
				Collider col = ColliderScratch[ i ];
				if ( col == null )
					continue;

				if ( !TryGetWorldBox( col, position, rotation, out Vector3 center, out Vector3 half, out Quaternion orient ) )
					continue;

				int hits = Physics.OverlapBoxNonAlloc(
					center,
					half,
					OverlapScratch,
					orient,
					Physics.DefaultRaycastLayers,
					QueryTriggerInteraction.Ignore );

				for ( int h = 0; h < hits; h++ )
				{
					Collider other = OverlapScratch[ h ];
					if ( other == null || _station.IsOwnCollider( other ) )
						continue;
					if ( IsIgnoredPlayerCollider( other ) )
						continue;
					if ( other.isTrigger )
						continue;
					if ( IsSupportingFloorOverlap( other, center ) )
						continue;

					Vector3 closest = other.ClosestPoint( center );
					Vector3 push = center - closest;
					float depth;
					if ( push.sqrMagnitude < 0.0001f )
					{
						push = center - other.bounds.center;
						if ( push.sqrMagnitude < 0.0001f )
							push = Vector3.up;
						depth = Mathf.Max( half.x, half.y, half.z ) * 0.4f;
					}
					else
					{
						depth = Mathf.Max( push.magnitude, 0.05f );
					}

					push.Normalize();
					accum += push * ( depth + 0.03f );
					pushCount++;
					pushed = true;
				}
			}

			if ( !pushed || pushCount <= 0 )
				break;

			position += accum / pushCount;
		}

		return position;
	}

	static bool TryGetWorldBox(
		Collider col,
		Vector3 rootPos,
		Quaternion rootRot,
		out Vector3 center,
		out Vector3 halfExtents,
		out Quaternion orientation )
	{
		center = Vector3.zero;
		halfExtents = Vector3.one * 0.5f;
		orientation = rootRot;

		CoinSortingStation station = col.GetComponentInParent<CoinSortingStation>();
		Transform stationRoot = station != null ? station.transform : col.transform.root;

		BoxCollider box = col as BoxCollider;
		if ( box != null )
		{
			Vector3 localCenter = stationRoot.InverseTransformPoint( box.transform.TransformPoint( box.center ) );
			center = rootPos + rootRot * localCenter;
			Vector3 lossy = box.transform.lossyScale;
			halfExtents = Vector3.Scale( box.size, new Vector3( Mathf.Abs( lossy.x ), Mathf.Abs( lossy.y ), Mathf.Abs( lossy.z ) ) ) * 0.5f;
			Quaternion localRot = Quaternion.Inverse( stationRoot.rotation ) * box.transform.rotation;
			orientation = rootRot * localRot;
			return true;
		}

		SphereCollider sphere = col as SphereCollider;
		if ( sphere != null )
		{
			Vector3 localCenter = stationRoot.InverseTransformPoint( sphere.transform.TransformPoint( sphere.center ) );
			center = rootPos + rootRot * localCenter;
			float maxScale = Mathf.Max(
				Mathf.Abs( sphere.transform.lossyScale.x ),
				Mathf.Abs( sphere.transform.lossyScale.y ),
				Mathf.Abs( sphere.transform.lossyScale.z ) );
			halfExtents = Vector3.one * ( sphere.radius * maxScale );
			orientation = Quaternion.identity;
			return true;
		}

		Bounds b = col.bounds;
		Vector3 offset = b.center - stationRoot.position;
		center = rootPos + rootRot * ( Quaternion.Inverse( stationRoot.rotation ) * offset );
		halfExtents = b.extents;
		return true;
	}

	void RefreshPlacementValidity()
	{
		if ( _station == null )
		{
			_placementValid = false;
			return;
		}

		_placementValid = _station.EvaluatePlacementPose(
			_station.transform.position,
			Quaternion.Euler( 0f, _yawDegrees, 0f ),
			out _placePosition,
			out _placeRotation,
			out _ );
	}

	void UpdateValidityOutline()
	{
		if ( _station == null )
		{
			ClearValidityOutline();
			return;
		}

		_station.CollectOutlineRenderers( OutlineScratch );
		if ( OutlineScratch.Count == 0 )
		{
			ClearValidityOutline();
			return;
		}

		HoverOutlineRegistrar.SetTarget(
			HoverOutlineRegistrar.Owner.StackVolume,
			OutlineScratch,
			ResolveValidityOutlineSettings( _placementValid ) );
	}

	HoverOutlineVisualSettings ResolveValidityOutlineSettings( bool valid )
	{
		PlayerPlacementDefinition def = RuntimeDefinition.Resolve( ref _placementDef );
		HoverOutlineVisualSettings settings = def != null && def.stackOutline != null
			? def.stackOutline.Clone()
			: HoverOutlineVisualSettings.DefaultStack();
		settings.Validate();

		Color ghost = valid
			? ( def != null ? def.validGhostColor : PlacementFeedbackColors.ValidGhost )
			: ( def != null ? def.invalidGhostColor : PlacementFeedbackColors.InvalidGhost );
		Color rgb = new Color( ghost.r, ghost.g, ghost.b, 1f );
		settings.outlineColor = new Color( rgb.r * 1.2f, rgb.g * 1.15f, rgb.b, 1f );
		return settings;
	}

	void ClearValidityOutline()
	{
		HoverOutlineRegistrar.ClearIfOwner( HoverOutlineRegistrar.Owner.StackVolume );
	}

	bool TryConfirmPlace()
	{
		if ( _phase != Phase.Carrying || _station == null )
			return false;

		RefreshPlacementValidity();
		if ( !_placementValid )
			return false;

		_settleStartPos = _station.transform.position;
		_settleStartRot = _station.transform.rotation;
		_settleEndPos = _placePosition;
		_settleEndRot = _placeRotation;
		_settleElapsed = 0f;

		CoinSortingStationDefinition def = _station.Definition;
		_settleDuration = def != null ? Mathf.Max( 0.05f, def.placeTweenSeconds ) : 0.28f;
		_phase = Phase.Settling;
		ClearValidityOutline();
		return true;
	}

	void TickSettle()
	{
		if ( _station == null )
		{
			_phase = Phase.Idle;
			return;
		}

		_settleElapsed += Time.deltaTime;
		float t = Mathf.Clamp01( _settleElapsed / Mathf.Max( 0.05f, _settleDuration ) );
		float eased = t * t * ( 3f - 2f * t );

		Vector3 pos = Vector3.Lerp( _settleStartPos, _settleEndPos, eased );
		Quaternion rot = Quaternion.Slerp( _settleStartRot, _settleEndRot, eased );

		Rigidbody body = _station.Body;
		if ( body != null )
		{
			body.MovePosition( pos );
			body.MoveRotation( rot );
		}
		else
		{
			_station.transform.SetPositionAndRotation( pos, rot );
		}

		if ( t < 1f )
			return;

		_station.transform.SetPositionAndRotation( _settleEndPos, _settleEndRot );
		CompletePlace();
	}

	void CompletePlace()
	{
		CoinSortingStation station = _station;
		RestorePlayerCollision();
		ClearValidityOutline();

		if ( station != null )
		{
			station.ClearOutputStackBindings();
			station.EndRepositioning();
			station.PlayPlacedFeedback();
		}

		_station = null;
		_phase = Phase.Idle;
		_placementValid = false;
		_followVelocity = Vector3.zero;
		_hasLastValidPose = false;
	}

	void ForceEndCarry()
	{
		RestorePlayerCollision();
		ClearValidityOutline();
		if ( _station != null )
			_station.EndRepositioning();

		_station = null;
		_phase = Phase.Idle;
		_charge = 0f;
		_placementValid = false;
		_hasLastValidPose = false;
	}

	void IgnorePlayerCollision( CoinSortingStation station )
	{
		RestorePlayerCollision();
		if ( station == null || _player == null )
			return;

		CharacterController cc = _player.GetComponent<CharacterController>();
		Collider[] stationCols = station.GetComponentsInChildren<Collider>( true );
		List<Collider> playerCols = new List<Collider>( 4 );
		if ( cc != null )
			playerCols.Add( cc );
		Collider[] extras = _player.GetComponentsInChildren<Collider>( true );
		for ( int i = 0; i < extras.Length; i++ )
		{
			if ( extras[ i ] != null && !playerCols.Contains( extras[ i ] ) )
				playerCols.Add( extras[ i ] );
		}

		_ignoredPlayerColliders = playerCols.ToArray();
		for ( int s = 0; s < stationCols.Length; s++ )
		{
			Collider a = stationCols[ s ];
			if ( a == null || a.isTrigger )
				continue;
			for ( int p = 0; p < _ignoredPlayerColliders.Length; p++ )
			{
				Collider b = _ignoredPlayerColliders[ p ];
				if ( b == null )
					continue;
				Physics.IgnoreCollision( a, b, true );
			}
		}

		_playerCollisionIgnored = true;
	}

	void RestorePlayerCollision()
	{
		if ( !_playerCollisionIgnored || _station == null || _ignoredPlayerColliders == null )
		{
			_playerCollisionIgnored = false;
			_ignoredPlayerColliders = null;
			return;
		}

		Collider[] stationCols = _station.GetComponentsInChildren<Collider>( true );
		for ( int s = 0; s < stationCols.Length; s++ )
		{
			Collider a = stationCols[ s ];
			if ( a == null )
				continue;
			for ( int p = 0; p < _ignoredPlayerColliders.Length; p++ )
			{
				Collider b = _ignoredPlayerColliders[ p ];
				if ( b == null )
					continue;
				Physics.IgnoreCollision( a, b, false );
			}
		}

		_playerCollisionIgnored = false;
		_ignoredPlayerColliders = null;
	}

	bool IsIgnoredPlayerCollider( Collider collider )
	{
		if ( collider == null || _ignoredPlayerColliders == null )
			return false;

		for ( int i = 0; i < _ignoredPlayerColliders.Length; i++ )
		{
			if ( _ignoredPlayerColliders[ i ] == collider )
				return true;
		}

		int playerLayer = PhysicsLayers.PlayerLayer;
		return playerLayer >= 0 && collider.gameObject.layer == playerLayer;
	}

	void UpdateChargeRing()
	{
		InteractionProgressRingUI ring = s_ring;
		if ( ring == null )
			return;

		if ( _phase == Phase.Charging )
		{
			ring.SetProgress( ChargeProgress01, valid: true );
			return;
		}
	}

	Transform ResolveCameraTransform()
	{
		if ( _player == null )
			return null;

		if ( _player.CameraMount != null )
			return _player.CameraMount;

		return _player.transform;
	}

	GameInput ResolveInput()
	{
		if ( _input != null )
			return _input;

		if ( InputController.Instance != null )
			_input = InputController.Instance.GameInput;

		return _input;
	}
}
