using System.Collections.Generic;

using UnityEngine;

public class PlayerInteraction : MonoBehaviour
{
	const int RaycastBufferSize = 32;

	PlayerInteractionDefinition _definition;
	readonly RaycastHit[] _rayHits = new RaycastHit[ RaycastBufferSize ];

	PlayerController _player;
	FirstPersonCameraController _cameraLook;
	PlayerPlacement _placement;
	bool _inputEnabled = true;
	bool _maskInitialized;
	LayerMask _resolvedMask;
	IInteractable _current;
	IInteractable _previousPrimaryFocus;
	RaycastHit _lastHit;
	bool _hasLastHit;
	RaycastHit _surfaceHit;
	bool _hasSurfaceHit;

	PlayerInteractionDefinition Definition => RuntimeDefinition.Resolve( ref _definition );

	float _interactRange = -1f;
	float _throwForce = -1f;
	float _softThrowSpeedScale = -1f;
	float _throwPlaceRepeatInterval = -1f;
	float _pickupRepeatInterval = -1f;
	float _throwPlaceHoldInitialDelay = -1f;
	float _pickupHoldInitialDelay = -1f;
	float _secondaryRepeatTimer;
	float _primaryRepeatTimer;
	bool _primaryPastInitialDelay;
	bool _secondaryPastInitialDelay;
	HoverOutlineVisualSettings _cachedPickableOutline = HoverOutlineVisualSettings.DefaultPickable();

	public IInteractable Current => _current;
	public bool HasInteractableFocus => _current != null;

	public float InteractRange
	{
		get
		{
			EnsureInteractRangeInitialized();
			return _interactRange;
		}
	}

	public LayerMask InteractMask
	{
		get
		{
			EnsureInteractMask();
			return _resolvedMask;
		}
	}

	public float DropUpBias => RuntimeDefinition.Get( Definition, d => d.dropUpBias, 0.05f );

	public float ThrowForce
	{
		get
		{
			EnsureThrowForceInitialized();
			return _throwForce;
		}
	}

	public float HeavyThrowForce => RuntimeDefinition.Get( Definition, d => d.heavyThrowForce, 3.5f );
	public float ThrowSpeed => ThrowForce;
	public float ThrowUpBias => RuntimeDefinition.Get( Definition, d => d.throwUpBias, 0.35f );
	public float ThrowInheritPlanarScale => RuntimeDefinition.Get( Definition, d => d.throwInheritPlanarScale, 1f );

	public float SoftThrowSpeedScale
	{
		get
		{
			EnsureSoftThrowSpeedScaleInitialized();
			return _softThrowSpeedScale;
		}
	}

	public float SoftThrowUpScale => RuntimeDefinition.Get( Definition, d => d.softThrowUpScale, 0.35f );

	public float ThrowPlaceRepeatInterval
	{
		get
		{
			EnsureThrowPlaceRepeatIntervalInitialized();
			return _throwPlaceRepeatInterval;
		}
	}

	public float PickupRepeatInterval
	{
		get
		{
			EnsurePickupRepeatIntervalInitialized();
			return _pickupRepeatInterval;
		}
	}

	public float ThrowPlaceHoldInitialDelay
	{
		get
		{
			EnsureThrowPlaceHoldInitialDelayInitialized();
			return _throwPlaceHoldInitialDelay;
		}
	}

	public float PickupHoldInitialDelay
	{
		get
		{
			EnsurePickupHoldInitialDelayInitialized();
			return _pickupHoldInitialDelay;
		}
	}

	/// <summary>0–1 progress toward the next repeated throw/place while secondary interact is held.</summary>
	public float SecondaryThrowPlaceRepeatProgress
	{
		get
		{
			float wait = GetSecondaryHoldWait();
			if ( wait <= 0f )
				return 0f;

			return Mathf.Clamp01( _secondaryRepeatTimer / wait );
		}
	}

	/// <summary>0–1 progress toward the next repeated pickup while primary interact is held.</summary>
	public float PrimaryPickupRepeatProgress
	{
		get
		{
			float wait = GetPrimaryHoldWait();
			if ( wait <= 0f )
				return 0f;

			return Mathf.Clamp01( _primaryRepeatTimer / wait );
		}
	}

	public void SetInteractRange( float value )
	{
		EnsureInteractRangeInitialized();
		_interactRange = Mathf.Max( 0.1f, value );
	}

	public void SetThrowForce( float value )
	{
		EnsureThrowForceInitialized();
		_throwForce = Mathf.Max( 0f, value );
	}

	public void SetSoftThrowSpeedScale( float value )
	{
		EnsureSoftThrowSpeedScaleInitialized();
		_softThrowSpeedScale = Mathf.Clamp( value, 0f, 1f );
	}

	public void SetThrowPlaceRepeatInterval( float value )
	{
		EnsureThrowPlaceRepeatIntervalInitialized();
		_throwPlaceRepeatInterval = Mathf.Max( 0.05f, value );
	}

	public void SetPickupRepeatInterval( float value )
	{
		EnsurePickupRepeatIntervalInitialized();
		_pickupRepeatInterval = Mathf.Max( 0.05f, value );
	}

	public void SetThrowPlaceHoldInitialDelay( float value )
	{
		EnsureThrowPlaceHoldInitialDelayInitialized();
		_throwPlaceHoldInitialDelay = Mathf.Max( 0f, value );
	}

	public void SetPickupHoldInitialDelay( float value )
	{
		EnsurePickupHoldInitialDelayInitialized();
		_pickupHoldInitialDelay = Mathf.Max( 0f, value );
	}

	void EnsureInteractRangeInitialized()
	{
		if ( _interactRange >= 0f )
			return;
		_interactRange = RuntimeDefinition.Get( Definition, d => d.interactRange, 8f );
	}

	void EnsureThrowForceInitialized()
	{
		if ( _throwForce >= 0f )
			return;

		float force = RuntimeDefinition.Get( Definition, d => d.throwForce, 0f );
		if ( force > 0f )
			_throwForce = force;
		else
			_throwForce = RuntimeDefinition.Get( Definition, d => d.throwSpeed, 5f );
	}

	void EnsureSoftThrowSpeedScaleInitialized()
	{
		if ( _softThrowSpeedScale >= 0f )
			return;
		_softThrowSpeedScale = RuntimeDefinition.Get( Definition, d => d.softThrowSpeedScale, 0.25f );
	}

	void EnsureThrowPlaceRepeatIntervalInitialized()
	{
		if ( _throwPlaceRepeatInterval >= 0f )
			return;
		_throwPlaceRepeatInterval = RuntimeDefinition.Get( Definition, d => d.throwPlaceRepeatInterval, 0.25f );
	}

	void EnsurePickupRepeatIntervalInitialized()
	{
		if ( _pickupRepeatInterval >= 0f )
			return;
		_pickupRepeatInterval = RuntimeDefinition.Get( Definition, d => d.pickupRepeatInterval, 0.25f );
	}

	void EnsureThrowPlaceHoldInitialDelayInitialized()
	{
		if ( _throwPlaceHoldInitialDelay >= 0f )
			return;
		_throwPlaceHoldInitialDelay = RuntimeDefinition.Get( Definition, d => d.throwPlaceHoldInitialDelay, 0.2f );
	}

	void EnsurePickupHoldInitialDelayInitialized()
	{
		if ( _pickupHoldInitialDelay >= 0f )
			return;
		_pickupHoldInitialDelay = RuntimeDefinition.Get( Definition, d => d.pickupHoldInitialDelay, 0.2f );
	}

	/// <summary>Legacy alias used by placement targets.</summary>
	public float interactRange => InteractRange;

	public bool TryGetLastHit( out RaycastHit hit )
	{
		hit = _lastHit;
		return _hasLastHit;
	}

	/// <summary>
	/// Nearest non-treasure / non-interactable surface along the aim ray (for floor placement).
	/// </summary>
	public bool TryGetSurfaceHit( out RaycastHit hit )
	{
		hit = _surfaceHit;
		return _hasSurfaceHit;
	}

	public bool TryGetAimRay( out Ray ray )
	{
		ray = default;
		if ( _cameraLook == null )
			return false;

		Transform cam = _cameraLook.transform;
		if ( cam == null )
			return false;

		ray = new Ray( cam.position, _cameraLook.GetCameraForward() );
		return true;
	}

	public Vector3 GetReleaseVelocity()
	{
		return GetReleaseVelocity( heavy: false );
	}

	public Vector3 GetReleaseVelocity( TreasureItem item )
	{
		bool heavy = item != null
			&& item.Definition != null
			&& ( item.Definition.usesHeavyThrow || item.Definition.exclusiveCarry );
		float forceScale = 1f;
		float upBiasScale = 1f;
		if ( item != null && item.Definition != null )
		{
			forceScale = item.Definition.throwForceScale;
			upBiasScale = item.Definition.throwUpBiasScale;
		}

		return BuildReleaseVelocity( heavy, forceScale, upBiasScale, speedScale: 1f, upScale: 1f );
	}

	public Vector3 GetReleaseVelocity( bool heavy )
	{
		return BuildReleaseVelocity( heavy, forceScale: 1f, upBiasScale: 1f, speedScale: 1f, upScale: 1f );
	}

	public Vector3 GetSoftReleaseVelocity()
	{
		return BuildReleaseVelocity(
			heavy: false,
			forceScale: 1f,
			upBiasScale: 1f,
			speedScale: SoftThrowSpeedScale,
			upScale: SoftThrowUpScale );
	}

	Vector3 BuildReleaseVelocity(
		bool heavy,
		float forceScale,
		float upBiasScale,
		float speedScale,
		float upScale )
	{
		Vector3 aim = Vector3.forward;
		if ( _cameraLook != null )
			aim = _cameraLook.GetCameraForward();

		if ( aim.sqrMagnitude < 0.0001f )
			aim = Vector3.forward;
		else
			aim.Normalize();

		// Looking straight up/down has no planar component — keep a body-forward bias so the
		// throw still travels in facing direction instead of dropping straight down/up.
		Vector3 flatAim = Vector3.ProjectOnPlane( aim, Vector3.up );
		if ( flatAim.sqrMagnitude < 0.0001f )
		{
			Vector3 bodyForward = _player != null ? _player.transform.forward : Vector3.forward;
			flatAim = Vector3.ProjectOnPlane( bodyForward, Vector3.up );
			if ( flatAim.sqrMagnitude < 0.0001f )
				flatAim = Vector3.forward;
			else
				flatAim.Normalize();

			float pitchSign = aim.y >= 0f ? 1f : -1f;
			aim = ( flatAim + Vector3.up * pitchSign ).normalized;
		}

		float speed = ( heavy ? HeavyThrowForce : ThrowForce ) * Mathf.Max( 0f, forceScale ) * speedScale;
		float upBias = ThrowUpBias * Mathf.Max( 0f, upBiasScale ) * upScale;
		Vector3 velocity = aim * speed + Vector3.up * upBias;

		if ( _player != null )
		{
			float inherit = ThrowInheritPlanarScale;
			if ( inherit > 0.0001f )
				velocity += _player.ThrowInheritVelocity * inherit;
		}

		return velocity;
	}

	void Awake()
	{
		EnsureInteractMask();
	}

	public void Setup( PlayerController player, FirstPersonCameraController cameraLook )
	{
		_player = player;
		_cameraLook = cameraLook;
		_placement = player != null ? player.Placement : null;
		if ( _placement == null && player != null )
			_placement = player.GetComponent<PlayerPlacement>();
		_maskInitialized = false;
		EnsureInteractMask();
		EnsurePickableOutlineSettings();
	}

	void EnsurePickableOutlineSettings()
	{
		PlayerInteractionDefinition def = Definition;
		_cachedPickableOutline = def != null && def.pickableOutline != null
			? def.pickableOutline.Clone()
			: HoverOutlineVisualSettings.DefaultPickable();
		_cachedPickableOutline.Validate();
	}

	void EnsureInteractMask()
	{
		if ( _maskInitialized )
			return;

		_maskInitialized = true;
		LayerMask mask = RuntimeDefinition.GetLayerMask( Definition, d => d.interactMask, 0 );
		if ( mask.value == 0 )
			mask = Physics.DefaultRaycastLayers;

		int collectable = LayerMask.NameToLayer( "Collectable" );
		if ( collectable >= 0 )
			mask |= 1 << collectable;

		_resolvedMask = mask;
	}

	public void SetInputEnabled( bool enabled )
	{
		_inputEnabled = enabled;
		if ( !_inputEnabled )
		{
			_current = null;
			_previousPrimaryFocus = null;
			ResetSecondaryRepeatState();
			ResetPrimaryRepeatState();
			ClearPickableIndicator();
		}
	}

	void Update()
	{
		if ( !_inputEnabled || _player == null || _cameraLook == null )
		{
			_current = null;
			_hasLastHit = false;
			_hasSurfaceHit = false;
			ClearPickableIndicator();
			return;
		}

		PlayerSorterReposition sorter = _player.SorterReposition;
		if ( sorter != null && sorter.IsCarrying )
		{
			_current = null;
			_hasLastHit = false;
			_hasSurfaceHit = false;
			ClearPickableIndicator();
			TrySorterPlaceInput();
			return;
		}

		UpdateFocus();
		UpdatePickableIndicator();
		TryInteractInput();
	}

	void UpdateFocus()
	{
		_current = null;
		_hasLastHit = false;
		_hasSurfaceHit = false;

		Transform cam = _cameraLook.transform;
		if ( cam == null )
			return;

		EnsureInteractMask();
		Ray ray = new Ray( cam.position, _cameraLook.GetCameraForward() );
		float interactRange = InteractRange;
		float aimRayLength = Mathf.Max( interactRange + 8f, interactRange * 3f );
		int hitCount = Physics.RaycastNonAlloc(
			ray,
			_rayHits,
			aimRayLength,
			_resolvedMask,
			QueryTriggerInteraction.Ignore );

		if ( hitCount <= 0 )
			return;

		// Prefer pickable TreasureItemInteractable along the ray so pile MeshColliders
		// don't steal focus from surface coins sitting slightly inside/against the mesh.
		InteractableBase bestItem = null;
		InteractableBase bestOther = null;
		RaycastHit bestItemHit = default;
		RaycastHit bestOtherHit = default;
		float bestItemDist = float.MaxValue;
		float bestOtherDist = float.MaxValue;
		float nearestDist = float.MaxValue;
		RaycastHit nearestHit = default;
		bool hasNearest = false;
		float surfaceDist = float.MaxValue;
		RaycastHit surfaceHit = default;
		bool hasSurface = false;

		Transform playerRoot = _player != null ? _player.transform : null;
		Vector3 playerPos = playerRoot != null ? playerRoot.position : cam.position;
		float rangeSq = interactRange * interactRange;

		for ( int i = 0; i < hitCount; i++ )
		{
			RaycastHit hit = _rayHits[ i ];
			if ( hit.collider == null )
				continue;

			// CharacterController / player colliders sit around the camera; hitting them
			// makes floor placement ghost at the near-clip instead of the aim surface.
			if ( IsPlayerOwnedHit( hit.collider, playerRoot ) )
				continue;

			bool within3dRange = hit.distance <= interactRange + 0.001f;

			if ( within3dRange && ( !hasNearest || hit.distance < nearestDist ) )
			{
				nearestDist = hit.distance;
				nearestHit = hit;
				hasNearest = true;
			}

			if ( within3dRange && PlacementFloorSurface.IsFloorCollider( hit.collider ) )
			{
				if ( !hasSurface || hit.distance < surfaceDist )
				{
					surfaceDist = hit.distance;
					surfaceHit = hit;
					hasSurface = true;
				}
			}

			InteractableBase interactable = ResolveInteractableFromHit( hit.collider );
			interactable = PromoteSorterMoveFocus( interactable, hit.collider );
			if ( interactable == null )
				continue;

			interactable = PromoteStackedCoinToOwnerStack( interactable );

			if ( !IsWithinFocusRange( interactable, hit, playerPos, interactRange, rangeSq ) )
				continue;

			bool canInteract = interactable.CanInteract( _player );
			bool canOutline = HoverOutlineTargetUtility.CanOutlineFocus( interactable, _player );
			if ( !canInteract && !canOutline )
				continue;

			if ( interactable is TreasureItemInteractable itemInteractable )
			{
				TreasureItem treasure = itemInteractable.Item;
				if ( treasure != null && IsBuriedOrOccludedTreasure( treasure, hit, bestOther, bestOtherDist ) )
					continue;

				if ( hit.distance < bestItemDist )
				{
					bestItemDist = hit.distance;
					bestItem = interactable;
					bestItemHit = hit;
				}
			}
			else if ( ShouldPreferNonTreasureFocus( interactable, hit.distance, bestOther, bestOtherDist ) )
			{
				bestOtherDist = hit.distance;
				bestOther = interactable;
				bestOtherHit = hit;
			}
		}

		if ( hasSurface )
		{
			_surfaceHit = surfaceHit;
			_hasSurfaceHit = true;
		}

		// Prefer exposed loose treasure, but never through a closer pile mesh hit.
		if ( bestItem != null )
		{
			TreasureItemInteractable bestItemInteractable = bestItem as TreasureItemInteractable;
			TreasureItem treasure = bestItemInteractable != null ? bestItemInteractable.Item : null;
			if ( treasure != null
				&& bestOther is TreasurePileInteractable
				&& bestOtherDist < bestItemDist - 0.001f )
			{
				bestItem = null;
			}
			else if ( treasure != null && IsTreasureBuriedInAnyLinkedPile( treasure ) )
			{
				bestItem = null;
			}
		}

		// Never target interactables through a closer floor surface.
		// Ground coin stacks sit on the floor — their capsule is often behind the floor hit
		// along glancing rays, so keep stack focus when the stack was hit at all.
		if ( hasSurface )
		{
			if ( bestItem != null && surfaceDist < bestItemDist - 0.001f )
				bestItem = null;
			if ( bestOther != null
				&& surfaceDist < bestOtherDist - 0.001f
				&& !IsFloorExemptOutlineInteractable( bestOther ) )
			{
				bestOther = null;
			}
		}

		if ( bestItem != null )
		{
			_current = bestItem;
			_lastHit = bestItemHit;
			_hasLastHit = true;
			return;
		}

		if ( bestOther != null )
		{
			_current = bestOther;
			_lastHit = bestOtherHit;
			_hasLastHit = true;
			return;
		}

		if ( hasNearest )
		{
			_lastHit = nearestHit;
			_hasLastHit = true;
		}
	}

	static bool IsBuriedOrOccludedTreasure(
		TreasureItem treasure,
		RaycastHit itemHit,
		InteractableBase bestOther,
		float bestOtherDist )
	{
		if ( treasure == null )
			return true;

		if ( IsTreasureBuriedInAnyLinkedPile( treasure ) )
			return true;

		if ( bestOther is TreasurePileInteractable && bestOtherDist < itemHit.distance - 0.001f )
			return true;

		return false;
	}

	static bool IsTreasureBuriedInAnyLinkedPile( TreasureItem treasure )
	{
		if ( treasure == null )
			return false;

		TreasurePileVisual origin = treasure.OriginPile;
		if ( origin != null && origin.IsTreasureBuried( treasure ) )
			return true;

		TreasurePileVisual ownerPile = treasure.PileOwner;
		if ( ownerPile != null && ownerPile.IsTreasureBuried( treasure ) )
			return true;

		return false;
	}

	void TryInteractInput()
	{
		GameInput input = GetGameInput();
		if ( input == null )
			return;

		PlayerCarry carry = _player != null ? _player.Carry : null;
		bool holding = carry != null && carry.Count > 0;

		TrySecondaryRepeatInput( input, holding );
		TryPrimaryRepeatInput( input );
	}

	void TrySorterPlaceInput()
	{
		GameInput input = GetGameInput();
		if ( input == null || input.SecondaryInteract == null )
			return;

		if ( !input.SecondaryInteract.WasPressedThisFrame() )
			return;

		PlayerSorterReposition sorter = _player != null ? _player.SorterReposition : null;
		if ( sorter == null || !sorter.IsCarrying )
			return;

		// Placement confirmation is owned by PlayerSorterReposition (invalid press no-ops).
		if ( _player != null )
			_player.CancelSlideVelocity();
	}

	static InteractableBase PromoteSorterMoveFocus( InteractableBase interactable, Collider collider )
	{
		if ( collider == null )
			return interactable;

		if ( interactable is CoinSortingCrankInteractable )
			return interactable;

		if ( collider.GetComponentInParent<CoinSortingCrankInteractable>() != null )
			return interactable;

		CoinSortingStation station = collider.GetComponentInParent<CoinSortingStation>();
		if ( station == null )
			return interactable;

		GroundCoinStack stack = interactable as GroundCoinStack;
		if ( stack != null && !station.IsHopperStack( stack ) )
			return interactable;

		CoinSortingStationMoveInteractable move = station.MoveInteractable;
		if ( move == null )
			move = station.GetComponentInChildren<CoinSortingStationMoveInteractable>( true );

		return move != null ? move : interactable;
	}

	/// <summary>
	/// Body and crank colliders overlap in X; prefer the crank when the ray is near it so
	/// "Hold to crank" wins over "Hold to move sorter".
	/// </summary>
	static bool ShouldPreferNonTreasureFocus(
		InteractableBase candidate,
		float candidateDist,
		InteractableBase currentBest,
		float bestDist )
	{
		if ( candidate == null )
			return false;

		if ( currentBest == null )
			return true;

		const float crankPreferSlack = 0.5f;

		if ( candidate is CoinSortingCrankInteractable
			&& currentBest is CoinSortingStationMoveInteractable )
			return candidateDist <= bestDist + crankPreferSlack;

		if ( candidate is CoinSortingStationMoveInteractable
			&& currentBest is CoinSortingCrankInteractable )
			return candidateDist + crankPreferSlack < bestDist;

		return candidateDist < bestDist;
	}

	void TryPrimaryRepeatInput( GameInput input )
	{
		if ( input.Interact.WasReleasedThisFrame() || !input.Interact.IsPressed() )
		{
			ResetPrimaryRepeatState();
			_previousPrimaryFocus = null;
			return;
		}

		if ( input.Interact.WasPressedThisFrame() )
		{
			TryInteractWithFocus();
			_primaryRepeatTimer = 0f;
			_primaryPastInitialDelay = false;
			if ( _current != null )
				_previousPrimaryFocus = _current;
			return;
		}

		// Skip hold delay only when the cursor moves onto a different live interactable.
		// Do not treat "previous item was just picked up" as a cursor move.
		if ( _current != null
			&& !ReferenceEquals( _current, _previousPrimaryFocus )
			&& IsFocusStillHoverable( _previousPrimaryFocus )
			&& _current.CanInteract( _player ) )
		{
			TryInteractWithFocus();
			_primaryRepeatTimer = 0f;
			_previousPrimaryFocus = _current;
			return;
		}

		if ( _current != null )
			_previousPrimaryFocus = _current;

		float wait = GetPrimaryHoldWait();
		_primaryRepeatTimer += Time.deltaTime;
		if ( _primaryRepeatTimer < wait )
			return;

		_primaryRepeatTimer -= wait;
		_primaryPastInitialDelay = true;
		TryInteractWithFocus();
	}

	bool IsFocusStillHoverable( IInteractable focus )
	{
		InteractableBase interactable = focus as InteractableBase;
		if ( interactable == null )
			return false;

		return HoverOutlineTargetUtility.CanOutlineFocus( interactable, _player )
			|| interactable.CanInteract( _player );
	}

	void TryInteractWithFocus()
	{
		if ( _current == null )
			return;

		if ( !_current.CanInteract( _player ) )
		{
			_current = null;
			return;
		}

		_current.Interact( _player );
		if ( _player != null )
			_player.CancelSlideVelocity();
	}

	static InteractableBase ResolveInteractableFromHit( Collider collider )
	{
		if ( collider == null )
			return null;

		TreasureItemInteractable treasureInteractable = TreasureItemInteractable.ResolveFromCollider( collider );
		if ( treasureInteractable != null )
		{
			TreasureItem item = treasureInteractable.Item;
			if ( item != null
				&& item.Definition != null
				&& item.Definition.category == TreasureCategory.Chest )
			{
				ChestInteractable chest = ChestInteractable.ResolveFromItem( item );
				if ( chest != null )
					return chest;
			}

			return treasureInteractable;
		}

		return collider.GetComponentInParent<InteractableBase>();
	}

	static InteractableBase PromoteStackedCoinToOwnerStack( InteractableBase interactable )
	{
		TreasureItemInteractable itemInteractable = interactable as TreasureItemInteractable;
		if ( itemInteractable == null )
			return interactable;

		TreasureItem item = itemInteractable.Item;
		if ( item == null )
			return interactable;

		MinecartInteractable minecart = item.Owner as MinecartInteractable;
		if ( minecart != null )
			return minecart;

		if ( item.State != TreasureItemState.Stacked )
			return interactable;

		GroundCoinStack groundStack = item.Owner as GroundCoinStack;
		if ( groundStack != null )
			return groundStack;

		CoinStackInteractable coinStack = item.Owner as CoinStackInteractable;
		if ( coinStack != null )
			return coinStack;

		return interactable;
	}

	static bool IsFloorExemptOutlineInteractable( InteractableBase interactable )
	{
		return interactable is GroundCoinStack || interactable is CoinStackInteractable;
	}

	/// <summary>
	/// Coin stacks use planar XZ reach so aiming at the top of a tall stack still works.
	/// Everything else uses the 3D ray hit distance.
	/// </summary>
	static bool IsWithinFocusRange(
		InteractableBase interactable,
		RaycastHit hit,
		Vector3 playerPos,
		float interactRange,
		float rangeSq )
	{
		if ( interactable == null )
			return false;

		if ( interactable is GroundCoinStack || interactable is CoinStackInteractable )
		{
			Vector3 stackPos = interactable.transform.position;
			float dx = stackPos.x - playerPos.x;
			float dz = stackPos.z - playerPos.z;
			return dx * dx + dz * dz <= rangeSq;
		}

		return hit.distance <= interactRange + 0.001f;
	}

	void ResetPrimaryRepeatState()
	{
		_primaryRepeatTimer = 0f;
		_primaryPastInitialDelay = false;
	}

	void TrySecondaryRepeatInput( GameInput input, bool holding )
	{
		if ( input.SecondaryInteract.WasReleasedThisFrame() || !input.SecondaryInteract.IsPressed() )
		{
			ResetSecondaryRepeatState();
			return;
		}

		if ( !holding || _placement == null )
			return;

		if ( input.SecondaryInteract.WasPressedThisFrame() )
		{
			if ( _placement.TrySecondaryPlace() && _player != null )
				_player.CancelSlideVelocity();
			_secondaryRepeatTimer = 0f;
			_secondaryPastInitialDelay = false;
			return;
		}

		float wait = GetSecondaryHoldWait();
		_secondaryRepeatTimer += Time.deltaTime;
		if ( _secondaryRepeatTimer < wait )
			return;

		_secondaryRepeatTimer -= wait;
		_secondaryPastInitialDelay = true;
		if ( _placement.TrySecondaryPlace() && _player != null )
			_player.CancelSlideVelocity();
	}

	void ResetSecondaryRepeatState()
	{
		_secondaryRepeatTimer = 0f;
		_secondaryPastInitialDelay = false;
	}

	float GetPrimaryHoldWait()
	{
		if ( !_primaryPastInitialDelay )
		{
			float initial = PickupHoldInitialDelay;
			if ( initial > 0f )
				return initial;
		}

		return PickupRepeatInterval;
	}

	float GetSecondaryHoldWait()
	{
		if ( !_secondaryPastInitialDelay )
		{
			float initial = ThrowPlaceHoldInitialDelay;
			if ( initial > 0f )
				return initial;
		}

		return ThrowPlaceRepeatInterval;
	}

	static bool IsPlayerOwnedHit( Collider collider, Transform playerRoot )
	{
		if ( collider == null || playerRoot == null )
			return false;

		Transform hitTransform = collider.transform;
		return hitTransform == playerRoot || hitTransform.IsChildOf( playerRoot );
	}

	void UpdatePickableIndicator()
	{
		EnsurePickableOutlineSettings();

		if ( _placement != null && _placement.HasActivePlacementOutline )
			return;

		if ( _current == null )
		{
			HoverOutlineRegistrar.ClearIfOwner( HoverOutlineRegistrar.Owner.Pickable );
			return;
		}

		IReadOnlyList<Renderer> renderers = HoverOutlineTargetUtility.CollectFromFocus( _current );
		if ( renderers == null || renderers.Count == 0 )
		{
			HoverOutlineRegistrar.ClearIfOwner( HoverOutlineRegistrar.Owner.Pickable );
			return;
		}

		HoverOutlineRegistrar.SetTarget(
			HoverOutlineRegistrar.Owner.Pickable,
			renderers,
			_cachedPickableOutline );
	}

	void ClearPickableIndicator()
	{
		HoverOutlineRegistrar.ClearIfOwner( HoverOutlineRegistrar.Owner.Pickable );
	}

	static GameInput GetGameInput()
	{
		return InputController.Instance != null ? InputController.Instance.GameInput : null;
	}
}
