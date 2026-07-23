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
	float _secondaryRepeatTimer;
	float _primaryRepeatTimer;

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

	/// <summary>0–1 progress toward the next repeated throw/place while secondary interact is held.</summary>
	public float SecondaryThrowPlaceRepeatProgress
	{
		get
		{
			float interval = ThrowPlaceRepeatInterval;
			if ( interval <= 0f )
				return 0f;

			return Mathf.Clamp01( _secondaryRepeatTimer / interval );
		}
	}

	/// <summary>0–1 progress toward the next repeated pickup while primary interact is held.</summary>
	public float PrimaryPickupRepeatProgress
	{
		get
		{
			float interval = PickupRepeatInterval;
			if ( interval <= 0f )
				return 0f;

			return Mathf.Clamp01( _primaryRepeatTimer / interval );
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

		float speed = ( heavy ? HeavyThrowForce : ThrowForce ) * Mathf.Max( 0f, forceScale ) * speedScale;
		float upBias = ThrowUpBias * Mathf.Max( 0f, upBiasScale ) * upScale;
		Vector3 velocity = aim * speed + Vector3.up * upBias;

		if ( _player != null )
		{
			float inherit = ThrowInheritPlanarScale;
			if ( inherit > 0.0001f )
				velocity += _player.PlanarVelocity * inherit;
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
			ResetSecondaryRepeatState();
			ResetPrimaryRepeatState();
		}
	}

	void Update()
	{
		if ( !_inputEnabled || _player == null || _cameraLook == null )
		{
			_current = null;
			_hasLastHit = false;
			_hasSurfaceHit = false;
			return;
		}

		UpdateFocus();
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
		int hitCount = Physics.RaycastNonAlloc(
			ray,
			_rayHits,
			InteractRange,
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

		for ( int i = 0; i < hitCount; i++ )
		{
			RaycastHit hit = _rayHits[ i ];
			if ( hit.collider == null )
				continue;

			// CharacterController / player colliders sit around the camera; hitting them
			// makes floor placement ghost at the near-clip instead of the aim surface.
			if ( IsPlayerOwnedHit( hit.collider, playerRoot ) )
				continue;

			if ( !hasNearest || hit.distance < nearestDist )
			{
				nearestDist = hit.distance;
				nearestHit = hit;
				hasNearest = true;
			}

			if ( PlacementFloorSurface.IsFloorCollider( hit.collider ) )
			{
				if ( !hasSurface || hit.distance < surfaceDist )
				{
					surfaceDist = hit.distance;
					surfaceHit = hit;
					hasSurface = true;
				}
			}

			InteractableBase interactable = ResolveInteractableFromHit( hit.collider );
			if ( interactable == null || !interactable.CanInteract( _player ) )
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
			else if ( hit.distance < bestOtherDist )
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

		// Never target interactables (gold piles, stacks, etc.) through a closer floor surface.
		if ( hasSurface )
		{
			if ( bestItem != null && surfaceDist < bestItemDist - 0.001f )
				bestItem = null;
			if ( bestOther != null && surfaceDist < bestOtherDist - 0.001f )
				bestOther = null;
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

	static bool IsFloorSurfaceHit( Collider collider )
	{
		return PlacementFloorSurface.IsFloorCollider( collider );
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

	void TryPrimaryRepeatInput( GameInput input )
	{
		if ( input.Interact.WasReleasedThisFrame() || !input.Interact.IsPressed() )
		{
			ResetPrimaryRepeatState();
			return;
		}

		if ( input.Interact.WasPressedThisFrame() )
		{
			TryInteractWithFocus();
			_primaryRepeatTimer = 0f;
			return;
		}

		float interval = PickupRepeatInterval;
		_primaryRepeatTimer += Time.deltaTime;
		if ( _primaryRepeatTimer < interval )
			return;

		_primaryRepeatTimer -= interval;
		TryInteractWithFocus();
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
	}

	static InteractableBase ResolveInteractableFromHit( Collider collider )
	{
		if ( collider == null )
			return null;

		TreasureItemInteractable treasureInteractable = TreasureItemInteractable.ResolveFromCollider( collider );
		if ( treasureInteractable != null )
			return treasureInteractable;

		return collider.GetComponentInParent<InteractableBase>();
	}

	void ResetPrimaryRepeatState()
	{
		_primaryRepeatTimer = 0f;
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
			_placement.TrySecondaryPlace();
			_secondaryRepeatTimer = 0f;
			return;
		}

		float interval = ThrowPlaceRepeatInterval;
		_secondaryRepeatTimer += Time.deltaTime;
		if ( _secondaryRepeatTimer < interval )
			return;

		_secondaryRepeatTimer -= interval;
		_placement.TrySecondaryPlace();
	}

	void ResetSecondaryRepeatState()
	{
		_secondaryRepeatTimer = 0f;
	}

	static bool IsPlayerOwnedHit( Collider collider, Transform playerRoot )
	{
		if ( collider == null || playerRoot == null )
			return false;

		Transform hitTransform = collider.transform;
		return hitTransform == playerRoot || hitTransform.IsChildOf( playerRoot );
	}

	static GameInput GetGameInput()
	{
		return InputController.Instance != null ? InputController.Instance.GameInput : null;
	}
}
