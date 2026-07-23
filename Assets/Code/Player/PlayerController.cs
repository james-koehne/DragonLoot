using UnityEngine;

public enum PlayerMovementState
{
	Walking,
	Sprinting,
	Sliding,
	Airborne,
	Gliding
}

[RequireComponent( typeof( CharacterController ) )]
public class PlayerController : MonoBehaviour
{
	const float SlideExitSpeedThreshold = 1.5f;
	const float MinDownhillSqr = 0.0001f;

	[Header( "View" )]
	public Transform cameraMount;

	PlayerControllerDefinition _definition;
	FirstPersonCameraController _cameraLook;
	PlayerInteraction _interaction;
	PlayerCarry _carry;
	PlayerPlacement _placement;
	PlayerTreasurePilePull _pilePull;
	CharacterController _characterController;
	bool gameplayInputEnabled = true;
	float _verticalVelocity;
	bool _wasGrounded;
	Vector3 _planarVelocity;
	Vector3 _localPlanarVelocity;
	Vector3 _groundNormal = Vector3.up;
	float _groundAngle;
	bool _hasGroundHit;
	float _groundDistance;
	bool _isSliding;
	float _slideEnterCharge;
	bool _wantsSprint;
	bool _jumpAvailable = true;
	bool _doubleJumpAvailable = true;
	bool _isGliding;
	bool _ignoreGrounding;
	float _coyoteTimer;

	PlayerControllerDefinition Definition => RuntimeDefinition.Resolve( ref _definition );

	float _walkSpeed = -1f;
	float _jumpForce = -1f;
	float _gravity = float.NaN;

	float WalkSpeed
	{
		get
		{
			EnsureWalkSpeedInitialized();
			return _walkSpeed;
		}
	}

	float WalkAcceleration => RuntimeDefinition.Get( Definition, d => d.walkAcceleration, 40f );
	float WalkDeceleration => RuntimeDefinition.Get( Definition, d => d.walkDeceleration, 50f );

	float JumpForce
	{
		get
		{
			EnsureJumpForceInitialized();
			return _jumpForce;
		}
	}

	float CoyoteTime => RuntimeDefinition.Get( Definition, d => d.coyoteTime, 0.12f );
	float DoubleJumpForce => RuntimeDefinition.Get( Definition, d => d.doubleJumpForce, 4f );
	float GlideGravity => RuntimeDefinition.Get( Definition, d => d.glideGravity, -6f );
	float GlideMaxFallSpeed => RuntimeDefinition.Get( Definition, d => d.glideMaxFallSpeed, -5f );
	float GlideAcceleration => RuntimeDefinition.Get( Definition, d => d.glideAcceleration, 10f );
	float GlideDeceleration => RuntimeDefinition.Get( Definition, d => d.glideDeceleration, 8f );
	bool GlideRequiresJumpHeld => RuntimeDefinition.Get( Definition, d => d.glideRequiresJumpHeld, true );
	float AirAcceleration => RuntimeDefinition.Get( Definition, d => d.airAcceleration, 6f );
	float AirDeceleration => RuntimeDefinition.Get( Definition, d => d.airDeceleration, 4f );
	float SprintSpeed => RuntimeDefinition.Get( Definition, d => d.sprintSpeed, 9f );
	float SprintAcceleration => RuntimeDefinition.Get( Definition, d => d.sprintAcceleration, 50f );
	float SprintDeceleration => RuntimeDefinition.Get( Definition, d => d.sprintDeceleration, 45f );
	float SprintAirControl => RuntimeDefinition.Get( Definition, d => d.sprintAirControl, 8f );

	float Gravity
	{
		get
		{
			EnsureGravityInitialized();
			return _gravity;
		}
	}
	float GroundStickVelocity => RuntimeDefinition.Get( Definition, d => d.groundStickVelocity, -8f );
	float GroundCheckDistance => RuntimeDefinition.Get( Definition, d => d.groundCheckDistance, 0.2f );
	float GroundSnapDistance => RuntimeDefinition.Get( Definition, d => d.groundSnapDistance, 0.35f );
	float GroundSnapSpeed => RuntimeDefinition.Get( Definition, d => d.groundSnapSpeed, 12f );
	float SlideAngle => RuntimeDefinition.Get( Definition, d => d.slideAngle, 45f );
	float SlideGravityScale => RuntimeDefinition.Get( Definition, d => d.slideGravityScale, 1f );
	float SlideSteer => RuntimeDefinition.Get( Definition, d => d.slideSteer, 12f );
	float SlideBrake => RuntimeDefinition.Get( Definition, d => d.slideBrake, 18f );
	float SlideExitDot => RuntimeDefinition.Get( Definition, d => d.slideExitDot, 0.55f );
	float SlideExitHysteresis => RuntimeDefinition.Get( Definition, d => d.slideExitHysteresis, 5f );
	float SlideEnterHoldTime => RuntimeDefinition.Get( Definition, d => d.slideEnterHoldTime, 0.4f );
	float SlideEnterMinPitch => RuntimeDefinition.Get( Definition, d => d.slideEnterMinPitch, 20f );
	float SlideEnterDownhillDot => RuntimeDefinition.Get( Definition, d => d.slideEnterDownhillDot, 0.5f );
	float SlideEnterForwardInput => RuntimeDefinition.Get( Definition, d => d.slideEnterForwardInput, 0.1f );
	float SlideEnterMoveSpeedScale => RuntimeDefinition.Get( Definition, d => d.slideEnterMoveSpeedScale, 0.85f );
	float SlideBrakeExitDot => RuntimeDefinition.Get( Definition, d => d.slideBrakeExitDot, 0.4f );
	float SlideBrakeExitSpeed => RuntimeDefinition.Get( Definition, d => d.slideBrakeExitSpeed, 2.5f );
	float SlideReverseBrakeScale => RuntimeDefinition.Get( Definition, d => d.slideReverseBrakeScale, 2f );

	public Transform CameraMount => cameraMount;
	public PlayerInteraction Interaction => _interaction;
	public PlayerCarry Carry => _carry;
	public PlayerPlacement Placement => _placement;
	public PlayerTreasurePilePull PilePull => _pilePull;
	public bool IsGrounded { get; private set; }
	public PlayerMovementState MovementState { get; private set; } = PlayerMovementState.Walking;
	public bool WasLandingThisFrame { get; private set; }
	public Vector3 PlanarVelocity => _planarVelocity;
	public float PlanarSpeed => _planarVelocity.magnitude;
	public Vector3 LocalPlanarVelocity => _localPlanarVelocity;
	public bool IsSliding => _isSliding;
	public bool IsGliding => _isGliding;
	public float GroundAngle => _groundAngle;

	/// <summary>0–1 progress toward committed slide while entry conditions are held.</summary>
	public float SlideEnterChargeProgress
	{
		get
		{
			float hold = SlideEnterHoldTime;
			if ( hold <= 0.0001f )
				return 0f;
			return Mathf.Clamp01( _slideEnterCharge / hold );
		}
	}

	public float CurrentWalkSpeed
	{
		get
		{
			EnsureWalkSpeedInitialized();
			return _walkSpeed;
		}
	}

	public float CurrentJumpForce
	{
		get
		{
			EnsureJumpForceInitialized();
			return _jumpForce;
		}
	}

	public float CurrentGravity
	{
		get
		{
			EnsureGravityInitialized();
			return _gravity;
		}
	}

	public void SetWalkSpeed( float value )
	{
		EnsureWalkSpeedInitialized();
		_walkSpeed = Mathf.Max( 0.1f, value );
	}

	public void SetJumpForce( float value )
	{
		EnsureJumpForceInitialized();
		_jumpForce = Mathf.Max( 0f, value );
	}

	public void SetGravity( float value )
	{
		EnsureGravityInitialized();
		_gravity = value;
	}

	void EnsureWalkSpeedInitialized()
	{
		if ( _walkSpeed >= 0f )
			return;
		_walkSpeed = RuntimeDefinition.Get( Definition, d => d.walkSpeed, 6f );
	}

	void EnsureJumpForceInitialized()
	{
		if ( _jumpForce >= 0f )
			return;
		_jumpForce = RuntimeDefinition.Get( Definition, d => d.jumpForce, 7f );
	}

	void EnsureGravityInitialized()
	{
		if ( !float.IsNaN( _gravity ) )
			return;
		_gravity = RuntimeDefinition.Get( Definition, d => d.gravity, -25f );
	}

	void Awake()
	{
		_characterController = GetComponent<CharacterController>();
		PhysicsLayers.EnsurePlayerLayer( gameObject );
		EnsureInteraction();
		EnsureCarry();
		EnsurePlacement();
		EnsurePilePull();

		if ( cameraMount == null )
		{
			Transform existing = transform.Find( "CameraMount" );
			if ( existing != null )
				cameraMount = existing;
		}
	}

	public void Setup( FirstPersonCameraController cameraLook )
	{
		_cameraLook = cameraLook;

		if ( _cameraLook != null )
			_cameraLook.Setup( transform );

		EnsureInteraction();
		EnsureCarry();
		EnsurePlacement();
		EnsurePilePull();

		_interaction.Setup( this, _cameraLook );
		_placement.Setup( this, _interaction );
		_carry.Setup( this, _cameraLook );
	}

	public bool CanReceiveInteractable( IInteractable interactable )
	{
		if ( _carry == null )
			return false;

		if ( interactable is TreasureItemInteractable itemInteractable )
		{
			TreasureItem item = itemInteractable.Item;
			return item != null && _carry.CanAdd( item );
		}

		if ( !TryGetTreasure( interactable, out TreasureDefinition treasure ) )
			return false;

		return _carry.CanAdd( treasure );
	}

	/// <summary>
	/// Receive carryable loot into the hand stack when capacity allows.
	/// </summary>
	public bool TryReceiveInteractable( IInteractable interactable )
	{
		if ( _carry == null )
			return false;

		if ( interactable is TreasureItemInteractable itemInteractable )
		{
			TreasureItem item = itemInteractable.Item;
			return item != null && _carry.TryAddExisting( item );
		}

		if ( !TryGetTreasure( interactable, out TreasureDefinition treasure ) )
			return false;

		return _carry.TryAdd( treasure );
	}

	public bool CanReceiveTreasureItem( TreasureItem item )
	{
		return _carry != null && _carry.CanAdd( item );
	}

	public bool TryReceiveTreasureItem( TreasureItem item )
	{
		return _carry != null && _carry.TryAddExisting( item );
	}

	public bool CanReceiveTreasureItems( System.Collections.Generic.IReadOnlyList<TreasureItem> items )
	{
		return _carry != null && _carry.CanAddAll( items );
	}

	public bool TryReceiveSupportStack( System.Collections.Generic.IReadOnlyList<TreasureItem> items )
	{
		return _carry != null && _carry.TryAddSupportStack( items );
	}

	static bool TryGetTreasure( IInteractable interactable, out TreasureDefinition treasure )
	{
		treasure = null;

		if ( interactable is PickupInteractable pickup )
		{
			treasure = pickup.Treasure;
			return treasure != null;
		}

		if ( interactable is StackInteractable stack )
		{
			treasure = stack.Treasure;
			return treasure != null;
		}

		return false;
	}

	void EnsureInteraction()
	{
		if ( _interaction == null )
			_interaction = GetComponent<PlayerInteraction>();
		if ( _interaction == null )
			_interaction = gameObject.AddComponent<PlayerInteraction>();
	}

	void EnsureCarry()
	{
		if ( _carry == null )
			_carry = GetComponent<PlayerCarry>();
		if ( _carry == null )
			_carry = gameObject.AddComponent<PlayerCarry>();
	}

	void EnsurePlacement()
	{
		if ( _placement == null )
			_placement = GetComponent<PlayerPlacement>();
		if ( _placement == null )
			_placement = gameObject.AddComponent<PlayerPlacement>();
	}

	void EnsurePilePull()
	{
		if ( _pilePull == null )
			_pilePull = GetComponent<PlayerTreasurePilePull>();
		if ( _pilePull == null )
			_pilePull = gameObject.AddComponent<PlayerTreasurePilePull>();
	}

	void Update()
	{
		UpdateGroundedState();
		ApplyGravityAndMove();
	}

	void UpdateGroundedState()
	{
		bool grounded = ProbeGround();
		WasLandingThisFrame = grounded && !_wasGrounded;
		IsGrounded = grounded;

		if ( !grounded )
			_isSliding = false;

		if ( WasLandingThisFrame )
		{
			_jumpAvailable = true;
			_doubleJumpAvailable = true;
			_isGliding = false;
			_coyoteTimer = 0f;
		}
		else if ( _wasGrounded && !grounded )
		{
			// Walked off an edge — allow a brief jump window. Jump takeoff clears
			// _wasGrounded itself so it does not start coyote after a real jump.
			_coyoteTimer = _jumpAvailable ? CoyoteTime : 0f;
		}
		else if ( !grounded && _coyoteTimer > 0f )
		{
			_coyoteTimer -= Time.deltaTime;
			if ( _coyoteTimer < 0f )
				_coyoteTimer = 0f;
		}
		else if ( grounded )
		{
			_coyoteTimer = 0f;
		}

		UpdateMovementState();
		_wasGrounded = grounded;
	}

	bool ProbeGround()
	{
		_hasGroundHit = false;
		_groundNormal = Vector3.up;
		_groundAngle = 0f;
		_groundDistance = float.MaxValue;

		// After jump, ignore stick/snap/probe until falling (or a real floor collision clears this).
		if ( _ignoreGrounding )
			return false;

		float radius = _characterController.radius * 0.9f;
		Vector3 origin = GetCapsuleBottomSphereOrigin( radius );
		float checkDistance = GroundCheckDistance + _characterController.skinWidth;
		float snapDistance = Mathf.Max( checkDistance, GroundSnapDistance + _characterController.skinWidth );

		bool castHit = Physics.SphereCast(
			origin,
			radius,
			Vector3.down,
			out RaycastHit hit,
			snapDistance,
			Physics.DefaultRaycastLayers,
			QueryTriggerInteraction.Ignore );

		if ( castHit )
		{
			_hasGroundHit = true;
			_groundNormal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : Vector3.up;
			_groundAngle = Vector3.Angle( _groundNormal, Vector3.up );
			_groundDistance = hit.distance;
		}

		if ( _characterController.isGrounded )
			return true;

		if ( castHit && hit.distance <= checkDistance )
			return true;

		// Snap onto descending slopes / uneven piles to avoid micro-airborne frames.
		bool wantsSnap = castHit
			&& hit.distance <= snapDistance
			&& ( _verticalVelocity <= 0f || _wasGrounded );

		if ( wantsSnap )
		{
			float gap = hit.distance - _characterController.skinWidth;
			if ( gap > 0f )
			{
				float snapStep = Mathf.Min( gap, GroundSnapSpeed * Time.deltaTime );
				_characterController.Move( Vector3.down * snapStep );
			}

			return true;
		}

		return false;
	}

	Vector3 GetCapsuleBottomSphereOrigin( float radius )
	{
		return transform.position
			+ Vector3.up * ( _characterController.center.y - _characterController.height * 0.5f + radius );
	}

	void ApplyGravityAndMove()
	{
		Vector2 moveInput = Vector2.zero;
		_wantsSprint = false;
		bool jumpHeld = false;
		bool jumpPressed = false;

		if ( gameplayInputEnabled )
		{
			GameInput input = GetGameInput();
			if ( input != null )
			{
				moveInput = input.Move.ReadValue<Vector2>();
				if ( moveInput.sqrMagnitude > 1f )
					moveInput.Normalize();

				_wantsSprint = input.Sprint.IsPressed();
				jumpHeld = input.Jump.IsPressed();
				jumpPressed = input.Jump.WasPressedThisFrame();

				bool canCoyoteJump = _coyoteTimer > 0f;
				if ( _jumpAvailable
				     && !_isSliding
				     && ( IsGrounded || canCoyoteJump )
				     && jumpPressed )
				{
					_verticalVelocity = JumpForce;
					_jumpAvailable = false;
					_coyoteTimer = 0f;
					_ignoreGrounding = true;
					IsGrounded = false;
					_wasGrounded = false;
					_isSliding = false;
					_hasGroundHit = false;
				}
				else if ( _doubleJumpAvailable
				          && !_jumpAvailable
				          && !IsGrounded
				          && !_isSliding
				          && jumpPressed )
				{
					_verticalVelocity = DoubleJumpForce;
					_doubleJumpAvailable = false;
					_isGliding = true;
					_ignoreGrounding = true;
					_isSliding = false;
					_hasGroundHit = false;
				}
			}
		}

		if ( _isGliding && GlideRequiresJumpHeld && !jumpHeld )
			_isGliding = false;

		GetFlatAxes( out Vector3 flatForward, out Vector3 flatRight );
		Vector3 moveIntent = flatForward * moveInput.y + flatRight * moveInput.x;

		if ( IsGrounded && _verticalVelocity < 0f )
			_verticalVelocity = GroundStickVelocity;

		if ( IsGrounded )
			UpdateSlideState( moveIntent, moveInput.y );
		else
			_slideEnterCharge = 0f;

		Vector3 velocity;
		if ( _isSliding && IsGrounded )
		{
			ApplySlideMovement( moveIntent, flatRight, Time.deltaTime );
			velocity = _planarVelocity;
			if ( _verticalVelocity < 0f )
				velocity += Vector3.up * _verticalVelocity;
		}
		else
		{
			ApplyStandardPlanarMovement( moveIntent, Time.deltaTime );
			if ( _isGliding )
			{
				_verticalVelocity += GlideGravity * Time.deltaTime;
				if ( _verticalVelocity < GlideMaxFallSpeed )
					_verticalVelocity = GlideMaxFallSpeed;
			}
			else
				_verticalVelocity += Gravity * Time.deltaTime;

			Vector3 horizontal = _planarVelocity;
			if ( IsGrounded && _hasGroundHit )
			{
				horizontal = Vector3.ProjectOnPlane( _planarVelocity, _groundNormal );
				if ( horizontal.sqrMagnitude > 0.0001f && _planarVelocity.sqrMagnitude > 0.0001f )
					horizontal = horizontal.normalized * _planarVelocity.magnitude;
			}

			velocity = horizontal + Vector3.up * _verticalVelocity;
		}

		CollisionFlags collisionFlags = _characterController.Move( velocity * Time.deltaTime );
		ResolveJumpGroundingSuppress( collisionFlags );

		UpdateLocalPlanarVelocity( flatForward, flatRight );
		UpdateMovementState();
	}

	void ResolveJumpGroundingSuppress( CollisionFlags collisionFlags )
	{
		if ( !_ignoreGrounding )
			return;

		// Rising jump: keep ignoring grounded helpers until apex/fall...
		if ( _verticalVelocity > 0f )
		{
			// ...unless we immediately hit a floor (low ceiling ledge, adjacent surface, etc.).
			if ( ( collisionFlags & CollisionFlags.Below ) != 0 )
			{
				_ignoreGrounding = false;
				_jumpAvailable = true;
			}

			return;
		}

		// Past apex — allow stick/snap/landing again.
		_ignoreGrounding = false;
	}

	void ApplyStandardPlanarMovement( Vector3 moveIntent, float dt )
	{
		// Keep planar velocity horizontal outside of slope projection used only for Move.
		_planarVelocity.y = 0f;

		bool sprinting = _wantsSprint && moveIntent.sqrMagnitude > 0.0001f;
		float carryScale = _carry != null ? _carry.MoveSpeedMultiplier : 1f;
		float targetSpeed = ( sprinting ? SprintSpeed : WalkSpeed ) * carryScale;
		if ( _slideEnterCharge > 0f && !_isSliding )
			targetSpeed *= SlideEnterMoveSpeedScale;
		Vector3 desired = moveIntent * targetSpeed;
		float accel;
		float decel;

		if ( IsGrounded )
		{
			accel = sprinting ? SprintAcceleration : WalkAcceleration;
			decel = sprinting ? SprintDeceleration : WalkDeceleration;
		}
		else if ( _isGliding )
		{
			accel = GlideAcceleration;
			decel = GlideDeceleration;
		}
		else if ( _wantsSprint )
		{
			accel = SprintAirControl;
			decel = SprintAirControl;
		}
		else
		{
			accel = AirAcceleration;
			decel = AirDeceleration;
		}

		float rate = desired.sqrMagnitude >= _planarVelocity.sqrMagnitude ? accel : decel;
		if ( desired.sqrMagnitude < 0.0001f )
			rate = decel;

		_planarVelocity = Vector3.MoveTowards( _planarVelocity, desired, rate * dt );
	}

	void UpdateSlideState( Vector3 moveIntent, float forwardInput )
	{
		Vector3 downhill = GetDownhillDirection();
		bool hasDownhill = downhill.sqrMagnitude > MinDownhillSqr;
		// Move intent is horizontal — compare against flattened downhill so steep
		// slopes (where 3D downhill is mostly vertical) can still align with W.
		Vector3 flatDownhill = GetFlatDirection( downhill );
		bool hasFlatDownhill = flatDownhill.sqrMagnitude > MinDownhillSqr;

		if ( _isSliding )
		{
			float exitAngle = Mathf.Max( 0f, SlideAngle - SlideExitHysteresis );
			if ( !IsGrounded || _groundAngle < exitAngle )
			{
				_isSliding = false;
				_planarVelocity.y = 0f;
				_slideEnterCharge = 0f;
				return;
			}

			if ( hasFlatDownhill && moveIntent.sqrMagnitude > 0.0001f )
			{
				Vector3 uphill = -flatDownhill;
				float climbDot = Vector3.Dot( moveIntent.normalized, uphill );
				if ( climbDot >= SlideExitDot
				     && ( climbDot > 0.85f || _planarVelocity.magnitude <= SlideExitSpeedThreshold ) )
				{
					_isSliding = false;
					_planarVelocity.y = 0f;
					_slideEnterCharge = 0f;
				}
			}

			return;
		}

		if ( !IsGrounded || !hasDownhill || !hasFlatDownhill || _groundAngle < SlideAngle )
		{
			_slideEnterCharge = 0f;
			return;
		}

		// Uphill intent must never charge a slide.
		if ( moveIntent.sqrMagnitude > 0.0001f )
		{
			float uphillDot = Vector3.Dot( moveIntent.normalized, -flatDownhill );
			if ( uphillDot >= SlideExitDot )
			{
				_slideEnterCharge = 0f;
				return;
			}
		}

		bool lookingDown = _cameraLook != null && _cameraLook.Pitch >= SlideEnterMinPitch;
		bool forwardHeld = forwardInput >= SlideEnterForwardInput;
		bool alignedDownhill = moveIntent.sqrMagnitude > 0.0001f
			&& Vector3.Dot( moveIntent.normalized, flatDownhill ) >= SlideEnterDownhillDot;

		if ( lookingDown && forwardHeld && alignedDownhill )
		{
			_slideEnterCharge += Time.deltaTime;
			float holdTime = SlideEnterHoldTime;
			if ( holdTime <= 0.0001f || _slideEnterCharge >= holdTime )
			{
				_isSliding = true;
				_slideEnterCharge = 0f;
			}
		}
		else
			_slideEnterCharge = 0f;
	}

	void ApplySlideMovement( Vector3 moveIntent, Vector3 flatRight, float dt )
	{
		Vector3 downhill = GetDownhillDirection();
		if ( downhill.sqrMagnitude <= MinDownhillSqr )
		{
			_isSliding = false;
			ApplyStandardPlanarMovement( moveIntent, dt );
			return;
		}

		float slopeRad = _groundAngle * Mathf.Deg2Rad;
		float slideAccel = Mathf.Abs( Gravity ) * Mathf.Sin( slopeRad ) * SlideGravityScale;
		_planarVelocity += downhill * ( slideAccel * dt );

		if ( moveIntent.sqrMagnitude > 0.0001f )
		{
			Vector3 intent = moveIntent.normalized;
			Vector3 velDir = _planarVelocity.sqrMagnitude > 0.0001f
				? _planarVelocity.normalized
				: downhill;

			Vector3 lateralAxis = Vector3.Cross( Vector3.up, velDir ).normalized;
			if ( lateralAxis.sqrMagnitude < 0.0001f )
				lateralAxis = flatRight;

			float lateral = Vector3.Dot( intent, lateralAxis );
			_planarVelocity += lateralAxis * ( lateral * SlideSteer * dt );

			float alongVelocity = Vector3.Dot( intent, velDir );
			if ( alongVelocity < -SlideBrakeExitDot )
			{
				float reverseBrake = SlideBrake * SlideReverseBrakeScale;
				_planarVelocity += velDir * ( alongVelocity * reverseBrake * dt );
			}
			else if ( alongVelocity < 0f )
				_planarVelocity += velDir * ( alongVelocity * SlideBrake * dt );
			else if ( Vector3.Dot( intent, -downhill ) > 0.2f )
				_planarVelocity += intent * ( SlideBrake * 0.35f * dt );
		}

		if ( _hasGroundHit )
			_planarVelocity = Vector3.ProjectOnPlane( _planarVelocity, _groundNormal );

		if ( _isSliding && _planarVelocity.sqrMagnitude <= SlideBrakeExitSpeed * SlideBrakeExitSpeed
		     && moveIntent.sqrMagnitude > 0.0001f )
		{
			Vector3 velDir = _planarVelocity.sqrMagnitude > 0.0001f
				? _planarVelocity.normalized
				: downhill;
			float alongVelocity = Vector3.Dot( moveIntent.normalized, velDir );
			if ( alongVelocity < -SlideBrakeExitDot )
			{
				_isSliding = false;
				_planarVelocity = Vector3.zero;
			}
		}

		// Keep a grounded stick contribution while sliding so we don't bounce off the mesh.
		if ( _verticalVelocity > GroundStickVelocity )
			_verticalVelocity = GroundStickVelocity;
	}

	Vector3 GetDownhillDirection()
	{
		if ( !_hasGroundHit )
			return Vector3.zero;

		Vector3 downhill = Vector3.ProjectOnPlane( Vector3.down, _groundNormal );
		if ( downhill.sqrMagnitude <= MinDownhillSqr )
			return Vector3.zero;

		return downhill.normalized;
	}

	/// <summary>Horizontal (XZ) unit direction for comparing against flat move intent.</summary>
	static Vector3 GetFlatDirection( Vector3 direction )
	{
		direction.y = 0f;
		if ( direction.sqrMagnitude <= MinDownhillSqr )
			return Vector3.zero;

		return direction.normalized;
	}

	void GetFlatAxes( out Vector3 flatForward, out Vector3 flatRight )
	{
		flatForward = transform.forward;
		flatForward.y = 0f;
		if ( flatForward.sqrMagnitude > 0.0001f )
			flatForward.Normalize();
		else
			flatForward = Vector3.forward;

		flatRight = transform.right;
		flatRight.y = 0f;
		if ( flatRight.sqrMagnitude > 0.0001f )
			flatRight.Normalize();
		else
			flatRight = Vector3.right;
	}

	void UpdateLocalPlanarVelocity( Vector3 flatForward, Vector3 flatRight )
	{
		_localPlanarVelocity = new Vector3(
			Vector3.Dot( _planarVelocity, flatRight ),
			0f,
			Vector3.Dot( _planarVelocity, flatForward ) );
	}

	void UpdateMovementState()
	{
		if ( !IsGrounded )
		{
			MovementState = _isGliding ? PlayerMovementState.Gliding : PlayerMovementState.Airborne;
			return;
		}

		if ( _isSliding )
		{
			MovementState = PlayerMovementState.Sliding;
			return;
		}

		if ( _wantsSprint && _planarVelocity.sqrMagnitude > 0.01f )
		{
			MovementState = PlayerMovementState.Sprinting;
			return;
		}

		MovementState = PlayerMovementState.Walking;
	}

	public void ResetForNewRun()
	{
		_verticalVelocity = 0f;
		WasLandingThisFrame = false;
		_wasGrounded = IsGrounded;
		_planarVelocity = Vector3.zero;
		_localPlanarVelocity = Vector3.zero;
		_isSliding = false;
		_slideEnterCharge = 0f;
		_wantsSprint = false;
		_jumpAvailable = true;
		_doubleJumpAvailable = true;
		_isGliding = false;
		_ignoreGrounding = false;
		_coyoteTimer = 0f;
		_groundNormal = Vector3.up;
		_groundAngle = 0f;
		_hasGroundHit = false;
		UpdateMovementState();

		if ( _carry != null )
			_carry.Clear();
	}

	public void TeleportTo( Vector3 position, Quaternion rotation )
	{
		if ( _characterController != null )
			_characterController.enabled = false;

		transform.SetPositionAndRotation( position, rotation );
		_verticalVelocity = 0f;
		WasLandingThisFrame = false;
		_wasGrounded = false;
		IsGrounded = false;
		_planarVelocity = Vector3.zero;
		_localPlanarVelocity = Vector3.zero;
		_isSliding = false;
		_slideEnterCharge = 0f;
		_wantsSprint = false;
		_jumpAvailable = true;
		_doubleJumpAvailable = true;
		_isGliding = false;
		_ignoreGrounding = false;
		_coyoteTimer = 0f;
		_groundNormal = Vector3.up;
		_groundAngle = 0f;
		_hasGroundHit = false;
		MovementState = PlayerMovementState.Airborne;

		if ( _characterController != null )
			_characterController.enabled = true;
	}

	public void SetGameplayInputEnabled( bool enabled )
	{
		gameplayInputEnabled = enabled;

		if ( _cameraLook != null )
			_cameraLook.SetInputEnabled( enabled );

		if ( _interaction != null )
			_interaction.SetInputEnabled( enabled );

		if ( _placement != null )
			_placement.SetInputEnabled( enabled );
	}

	static GameInput GetGameInput()
	{
		return InputController.Instance != null ? InputController.Instance.GameInput : null;
	}
}
