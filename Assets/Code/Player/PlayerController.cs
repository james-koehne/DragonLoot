using UnityEngine;

public enum PlayerMovementState
{
	Walking,
	Sprinting,
	Climbing,
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

	[Header( "Debug" )]
	[SerializeField] bool drawMovementGizmos;
	[SerializeField] [Min( 0.1f )] float movementGizmoScale = 2f;

	PlayerControllerDefinition _definition;
	FirstPersonCameraController _cameraLook;
	PlayerInteraction _interaction;
	PlayerCarry _carry;
	PlayerPlacement _placement;
	PlayerTreasurePilePull _pilePull;
	PlayerMinecartPush _minecartPush;
	PlayerSorterReposition _sorterReposition;
	PlayerAbilities _abilities;
	PlayerCleaning _cleaning;
	PlayerWholeStackInteraction _wholeStack;
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
	Collider _groundCollider;
	bool _isSliding;
	bool _isClimbing;
	float _climbReleaseTimer;
	float _slideEnterCharge;
	bool _slideExitBoostActive;
	float _slideExitBoostSpeed;
	Vector3 _slideExitBoostDirection;
	bool _wantsSprint;
	bool _jumpAvailable = true;
	bool _doubleJumpAvailable = true;
	bool _isGliding;
	bool _ignoreGrounding;
	float _coyoteTimer;
	Vector3 _lastSlideTravelDirection;

	// Cached for optional movement gizmos (updated each move tick).
	Vector3 _debugFlatMoveIntent;
	Vector3 _debugGroundMoveIntent;
	Vector3 _debugMoveVelocity;
	Vector3 _debugFlatForward;

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
	bool ClimbingEnabled => RuntimeDefinition.Get( Definition, d => d.climbingEnabled, true );
	float ClimbSpeed => RuntimeDefinition.Get( Definition, d => d.climbSpeed, 3.5f );
	float ClimbSprintSpeed => RuntimeDefinition.Get( Definition, d => d.climbSprintSpeed, 5f );
	float ClimbAcceleration => RuntimeDefinition.Get( Definition, d => d.climbAcceleration, 35f );
	float ClimbDeceleration => RuntimeDefinition.Get( Definition, d => d.climbDeceleration, 40f );
	float ClimbSurfacePull => RuntimeDefinition.Get( Definition, d => d.climbSurfacePull, 12f );
	float ClimbJumpForce => RuntimeDefinition.Get( Definition, d => d.climbJumpForce, 9f );
	float ClimbExitHysteresis => RuntimeDefinition.Get( Definition, d => d.climbExitHysteresis, 5f );
	float ClimbReleaseHoldTime => RuntimeDefinition.Get( Definition, d => d.climbReleaseHoldTime, 0.25f );
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
	float SlideExitBoostMaxSpeed => RuntimeDefinition.Get( Definition, d => d.slideExitBoostMaxSpeed, 24f );
	float SlideExitBoostMultiplier => RuntimeDefinition.Get( Definition, d => d.slideExitBoostMultiplier, 1f );
	float SlideExitBoostDecay => RuntimeDefinition.Get( Definition, d => d.slideExitBoostDecay, 70f );
	float SlideExitBoostSteer => RuntimeDefinition.Get( Definition, d => d.slideExitBoostSteer, 25f );

	public Transform CameraMount => cameraMount;
	public PlayerInteraction Interaction => _interaction;
	public PlayerCarry Carry => _carry;
	public PlayerPlacement Placement => _placement;
	public PlayerTreasurePilePull PilePull => _pilePull;

	public PlayerMinecartPush MinecartPush => _minecartPush;
	public PlayerSorterReposition SorterReposition => _sorterReposition;
	public PlayerAbilities Abilities => _abilities;
	public PlayerCleaning Cleaning => _cleaning;
	public PlayerWholeStackInteraction WholeStack => _wholeStack;

	public bool IsGrounded { get; private set; }
	public PlayerMovementState MovementState { get; private set; } = PlayerMovementState.Walking;
	public bool WasLandingThisFrame { get; private set; }
	public bool WasJumpThisFrame { get; private set; }
	public Collider GroundCollider => _groundCollider;
	public Vector3 PlanarVelocity => _planarVelocity;
	public float PlanarSpeed => _planarVelocity.magnitude;
	public Vector3 LocalPlanarVelocity => _localPlanarVelocity;

	/// <summary>
	/// Velocity baked into thrown treasure: full planar motion, plus vertical only while airborne
	/// (skips grounded stick velocity so throws aren't yanked into the floor).
	/// </summary>
	public Vector3 ThrowInheritVelocity
	{
		get
		{
			Vector3 v = _planarVelocity;
			v.y = 0f;
			if ( !IsGrounded )
				v.y = _verticalVelocity;
			return v;
		}
	}
	public bool IsSliding => _isSliding;
	public bool IsClimbing => _isClimbing;
	public bool IsClimbingEnabled => ClimbingEnabled;
	public bool IsSlideExitBoostActive => _slideExitBoostActive;
	public float SlideExitBoostSpeed => _slideExitBoostSpeed;
	public bool IsGliding => _isGliding;
	public float GroundAngle => _groundAngle;
	public bool GameplayInputEnabled => gameplayInputEnabled;

	/// <summary>
	/// Ends an active slide and kills residual slide/coast planar velocity.
	/// Call from interact / place / throw so those actions plant the player.
	/// </summary>
	public void CancelSlideVelocity()
	{
		_isSliding = false;
		_slideEnterCharge = 0f;
		ClearSlideExitBoost();
		_planarVelocity = Vector3.zero;
		UpdateMovementState();
	}

	void ClearClimb()
	{
		_isClimbing = false;
		_climbReleaseTimer = 0f;
	}

	/// <summary>Enable or disable Climbing movement. Clears an active climb when turned off.</summary>
	public void SetClimbingEnabled( bool enabled )
	{
		PlayerControllerDefinition definition = Definition;
		if ( definition == null )
			return;

		definition.climbingEnabled = enabled;
		if ( !enabled )
			ClearClimb();
	}

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
		PhysicsLayers.EnsureCharacterControllerIgnoresCollectables( _characterController );
		EnsureInteraction();
		EnsureCarry();
		EnsurePlacement();
		EnsurePilePull();
		EnsureMinecartPush();
		EnsureSorterReposition();
		EnsureAbilities();
		EnsureCleaning();
		EnsureWholeStack();
		EnsureFootsteps();
		EnsureUpgrades();

		if ( cameraMount == null )
		{
			Transform existing = transform.Find( "CameraMount" );
			if ( existing != null )
				cameraMount = existing;
		}
	}

	void OnDestroy()
	{
		UpgradeSystem.ClearInstanceIfOwner( this );
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
		EnsureMinecartPush();
		EnsureSorterReposition();
		EnsureAbilities();
		EnsureCleaning();
		EnsureWholeStack();
		EnsureFootsteps();
		EnsureUpgrades();

		_interaction.Setup( this, _cameraLook );
		_placement.Setup( this, _interaction );
		_carry.Setup( this, _cameraLook );
		_abilities.Setup( this );
		if ( _cleaning != null )
			_cleaning.Setup( this );
		if ( _wholeStack != null )
			_wholeStack.Setup( this, _interaction, _placement );
		if ( _minecartPush != null )
			_minecartPush.Setup( this );
		if ( _sorterReposition != null )
			_sorterReposition.Setup( this );
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

	void EnsureMinecartPush()
	{
		if ( _minecartPush == null )
			_minecartPush = GetComponent<PlayerMinecartPush>();
		if ( _minecartPush == null )
			_minecartPush = gameObject.AddComponent<PlayerMinecartPush>();
	}

	void EnsureSorterReposition()
	{
		if ( _sorterReposition == null )
			_sorterReposition = GetComponent<PlayerSorterReposition>();
		if ( _sorterReposition == null )
			_sorterReposition = gameObject.AddComponent<PlayerSorterReposition>();
	}

	void EnsureAbilities()
	{
		if ( _abilities == null )
			_abilities = GetComponent<PlayerAbilities>();
		if ( _abilities == null )
			_abilities = gameObject.AddComponent<PlayerAbilities>();
	}

	void EnsureCleaning()
	{
		if ( _cleaning == null )
			_cleaning = GetComponent<PlayerCleaning>();
		if ( _cleaning == null )
			_cleaning = gameObject.AddComponent<PlayerCleaning>();
	}

	void EnsureWholeStack()
	{
		if ( _wholeStack == null )
			_wholeStack = GetComponent<PlayerWholeStackInteraction>();
		if ( _wholeStack == null )
			_wholeStack = gameObject.AddComponent<PlayerWholeStackInteraction>();
	}

	void EnsureFootsteps()
	{
		if ( GetComponent<PlayerFootsteps>() == null )
			gameObject.AddComponent<PlayerFootsteps>();
	}

	void EnsureUpgrades()
	{
		UpgradeSystem.Ensure( this );
	}

	void Update()
	{
		ApplyGravityAndMove();
	}

	bool ProbeGround( Vector3 flatMoveIntent, Vector3 priorGroundNormal, bool hadPriorGround )
	{
		_hasGroundHit = false;
		_groundCollider = null;
		_groundNormal = Vector3.up;
		_groundAngle = 0f;
		_groundDistance = float.MaxValue;

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
			_groundCollider = hit.collider;
			_groundNormal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : Vector3.up;
			_groundAngle = Vector3.Angle( _groundNormal, Vector3.up );
			_groundDistance = hit.distance;
		}

		if ( _characterController.isGrounded )
			return true;

		if ( castHit && hit.distance <= checkDistance )
			return true;

		bool suppressSnap = _isClimbing
			|| ( hadPriorGround
			     && _wasGrounded
			     && HasSteepUphillFlatIntent( flatMoveIntent, priorGroundNormal ) );

		bool wantsSnap = castHit
			&& hit.distance <= snapDistance
			&& ( _verticalVelocity <= 0f || _wasGrounded )
			&& !suppressSnap;

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
			}
		}

		GetFlatAxes( out Vector3 flatForward, out Vector3 flatRight );
		Vector3 flatMoveIntent = flatForward * moveInput.y + flatRight * moveInput.x;

		Vector3 priorGroundNormal = _groundNormal;
		bool hadPriorGround = _hasGroundHit;
		bool grounded = ProbeGround( flatMoveIntent, priorGroundNormal, hadPriorGround );
		WasLandingThisFrame = grounded && !_wasGrounded;
		WasJumpThisFrame = false;
		IsGrounded = grounded;

		if ( !grounded && _isSliding )
			ExitSlide( retainMomentum: true );

		if ( !grounded )
			ClearClimb();

		if ( WasLandingThisFrame )
		{
			_jumpAvailable = true;
			_doubleJumpAvailable = true;
			_isGliding = false;
			_coyoteTimer = 0f;
		}
		else if ( _wasGrounded && !grounded )
			_coyoteTimer = _jumpAvailable ? CoyoteTime : 0f;
		else if ( !grounded && _coyoteTimer > 0f )
		{
			_coyoteTimer -= Time.deltaTime;
			if ( _coyoteTimer < 0f )
				_coyoteTimer = 0f;
		}
		else if ( grounded )
			_coyoteTimer = 0f;

		if ( gameplayInputEnabled )
		{
			GameInput input = GetGameInput();
			if ( input != null )
			{
				bool canCoyoteJump = _coyoteTimer > 0f;
				if ( _isClimbing
				     && IsGrounded
				     && !_isSliding
				     && jumpPressed )
				{
					PerformClimbJumpOff();
				}
				else if ( _jumpAvailable
				     && !_isSliding
				     && !_isClimbing
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
					ClearClimb();
					ClearSlideExitBoost();
					_hasGroundHit = false;
					_groundCollider = null;
					WasJumpThisFrame = true;
				}
				else if ( _isGliding
				          && !IsGrounded
				          && !_isSliding
				          && jumpPressed )
				{
					_isGliding = false;
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
					ClearClimb();
					ClearSlideExitBoost();
					_hasGroundHit = false;
					_groundCollider = null;
				}
			}
		}

		if ( _isGliding && GlideRequiresJumpHeld && !jumpHeld )
			_isGliding = false;

		Vector3 moveIntent = flatMoveIntent;
		if ( IsGrounded && _hasGroundHit && flatMoveIntent.sqrMagnitude > 0.0001f )
			moveIntent = AlignMoveIntentToGround( flatMoveIntent );

		_debugFlatForward = flatForward;
		_debugFlatMoveIntent = flatMoveIntent;
		_debugGroundMoveIntent = moveIntent;

		if ( IsGrounded && !_isClimbing && _verticalVelocity < 0f )
			_verticalVelocity = GroundStickVelocity;

		if ( IsGrounded )
		{
			UpdateSlideState( flatMoveIntent, moveInput.y );
			UpdateClimbState( flatMoveIntent );
		}
		else
		{
			_slideEnterCharge = 0f;
			ClearClimb();
		}

		Vector3 velocity;
		if ( _isSliding && IsGrounded )
		{
			ApplySlideMovement( flatMoveIntent, flatRight, Time.deltaTime );
			velocity = _planarVelocity;
			if ( _verticalVelocity < 0f )
				velocity += Vector3.up * _verticalVelocity;
		}
		else if ( _isClimbing && IsGrounded )
		{
			ApplyClimbMovement( flatMoveIntent, Time.deltaTime );
			velocity = ComposeClimbMoveVelocity( _planarVelocity );
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

			if ( IsGrounded && _hasGroundHit )
				velocity = ComposeGroundedMoveVelocity( _planarVelocity );
			else
				velocity = new Vector3( _planarVelocity.x, _verticalVelocity, _planarVelocity.z );
		}

		_debugMoveVelocity = velocity;

		CollisionFlags collisionFlags = _characterController.Move( velocity * Time.deltaTime );
		ResolveJumpGroundingSuppress( collisionFlags );

		UpdateLocalPlanarVelocity( flatForward, flatRight );
		_wasGrounded = grounded;
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
		if ( IsGrounded && _hasGroundHit )
			_planarVelocity = Vector3.ProjectOnPlane( _planarVelocity, _groundNormal );
		else if ( !IsGrounded )
			_planarVelocity.y = 0f;

		bool sprinting = _wantsSprint && moveIntent.sqrMagnitude > 0.0001f;
		float carryScale = _carry != null ? _carry.MoveSpeedMultiplier : 1f;
		float targetSpeed = ( sprinting ? SprintSpeed : WalkSpeed ) * carryScale;
		if ( _slideEnterCharge > 0f && !_isSliding )
			targetSpeed *= SlideEnterMoveSpeedScale;
		Vector3 desired = moveIntent.sqrMagnitude > 0.0001f
			? moveIntent.normalized * targetSpeed
			: Vector3.zero;
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

		if ( _slideExitBoostActive )
		{
			ApplySlideExitBoost( moveIntent, targetSpeed, dt );
			return;
		}

		_planarVelocity = Vector3.MoveTowards( _planarVelocity, desired, rate * dt );
	}

	void BeginSlideExitBoost( float slideSpeed, Vector3 exitDirection )
	{
		if ( slideSpeed <= 0.01f )
		{
			ClearSlideExitBoost();
			return;
		}

		Vector3 direction = exitDirection;
		direction.y = 0f;
		if ( direction.sqrMagnitude < 0.0001f )
		{
			ClearSlideExitBoost();
			return;
		}

		direction.Normalize();
		float boostedSpeed = slideSpeed * SlideExitBoostMultiplier;
		_slideExitBoostSpeed = Mathf.Min( boostedSpeed, SlideExitBoostMaxSpeed );
		_slideExitBoostDirection = direction;
		_slideExitBoostActive = true;
		_planarVelocity = direction * _slideExitBoostSpeed;
	}

	void ClearSlideExitBoost()
	{
		_slideExitBoostActive = false;
		_slideExitBoostSpeed = 0f;
		_slideExitBoostDirection = Vector3.zero;
	}

	void ExitSlide( bool retainMomentum )
	{
		_isSliding = false;
		_slideEnterCharge = 0f;
		if ( !retainMomentum )
		{
			_planarVelocity.y = 0f;
			ClearSlideExitBoost();
			return;
		}

		float slideSpeed = _planarVelocity.magnitude;
		Vector3 exitDirection = ResolveSlideExitDirection();
		BeginSlideExitBoost( slideSpeed, exitDirection );
	}

	void ApplySlideExitBoost( Vector3 moveIntent, float targetWalkSpeed, float dt )
	{
		if ( moveIntent.sqrMagnitude > 0.0001f )
		{
			Vector3 flatIntent = moveIntent;
			flatIntent.y = 0f;
			if ( flatIntent.sqrMagnitude > 0.0001f )
			{
				float steerT = Mathf.Clamp01( SlideExitBoostSteer * dt );
				_slideExitBoostDirection = Vector3.Slerp(
					_slideExitBoostDirection, flatIntent.normalized, steerT ).normalized;
			}
		}

		_slideExitBoostSpeed = Mathf.MoveTowards(
			_slideExitBoostSpeed, targetWalkSpeed, SlideExitBoostDecay * dt );
		_planarVelocity = _slideExitBoostDirection * _slideExitBoostSpeed;

		if ( Mathf.Abs( _slideExitBoostSpeed - targetWalkSpeed ) <= 0.05f )
			ClearSlideExitBoost();
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
				ExitSlide( retainMomentum: true );
				return;
			}

			if ( hasFlatDownhill && moveIntent.sqrMagnitude > 0.0001f )
			{
				Vector3 uphill = -flatDownhill;
				float climbDot = Vector3.Dot( moveIntent.normalized, uphill );
				if ( climbDot >= SlideExitDot
				     && ( climbDot > 0.85f || _planarVelocity.magnitude <= SlideExitSpeedThreshold ) )
				{
					ExitSlide( retainMomentum: true );
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
				ClearClimb();
				ClearSlideExitBoost();
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
			ExitSlide( retainMomentum: true );
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

		UpdateLastSlideTravelDirection( downhill );

		if ( _isSliding && _planarVelocity.sqrMagnitude <= SlideBrakeExitSpeed * SlideBrakeExitSpeed
		     && moveIntent.sqrMagnitude > 0.0001f )
		{
			Vector3 velDir = _planarVelocity.sqrMagnitude > 0.0001f
				? _planarVelocity.normalized
				: downhill;
			float alongVelocity = Vector3.Dot( moveIntent.normalized, velDir );
			if ( alongVelocity < -SlideBrakeExitDot )
			{
				ExitSlide( retainMomentum: false );
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

	void UpdateLastSlideTravelDirection( Vector3 downhill )
	{
		Vector3 flatVelocity = _planarVelocity;
		flatVelocity.y = 0f;
		if ( flatVelocity.sqrMagnitude > 0.25f )
		{
			_lastSlideTravelDirection = flatVelocity.normalized;
			return;
		}

		Vector3 flatDownhill = GetFlatDirection( downhill );
		if ( flatDownhill.sqrMagnitude > MinDownhillSqr )
			_lastSlideTravelDirection = flatDownhill;
	}

	Vector3 ResolveSlideExitDirection()
	{
		Vector3 horizontal = _planarVelocity;
		horizontal.y = 0f;
		if ( horizontal.sqrMagnitude > 0.0001f )
			return horizontal.normalized;

		if ( _lastSlideTravelDirection.sqrMagnitude > MinDownhillSqr )
			return _lastSlideTravelDirection;

		GetFlatAxes( out Vector3 flatForward, out Vector3 flatRight );
		return flatForward;
	}

	bool HasSteepUphillFlatIntent( Vector3 flatMoveIntent, Vector3 groundNormal )
	{
		if ( groundNormal.sqrMagnitude < 0.0001f || flatMoveIntent.sqrMagnitude < 0.0001f )
			return false;

		float angle = Vector3.Angle( groundNormal, Vector3.up );
		if ( angle < SlideAngle )
			return false;

		Vector3 downhill = Vector3.ProjectOnPlane( Vector3.down, groundNormal );
		Vector3 flatDownhill = GetFlatDirection( downhill );
		if ( flatDownhill.sqrMagnitude <= MinDownhillSqr )
			return false;

		return Vector3.Dot( flatMoveIntent.normalized, -flatDownhill ) >= SlideExitDot;
	}

	void UpdateClimbState( Vector3 flatMoveIntent )
	{
		if ( !ClimbingEnabled || _isSliding || !IsGrounded || !_hasGroundHit )
		{
			ClearClimb();
			return;
		}

		float exitAngle = Mathf.Max( 0f, SlideAngle - ClimbExitHysteresis );
		bool steepEnough = _isClimbing
			? _groundAngle >= exitAngle
			: _groundAngle >= SlideAngle;

		if ( !steepEnough )
		{
			ClearClimb();
			return;
		}

		bool hasMoveIntent = flatMoveIntent.sqrMagnitude > 0.0001f;

		if ( _isClimbing )
		{
			if ( hasMoveIntent )
			{
				_climbReleaseTimer = 0f;
				return;
			}

			_climbReleaseTimer += Time.deltaTime;
			if ( _climbReleaseTimer >= ClimbReleaseHoldTime )
				ClearClimb();
			return;
		}

		if ( !hasMoveIntent )
			return;

		Vector3 downhill = GetDownhillDirection();
		Vector3 flatDownhill = GetFlatDirection( downhill );
		if ( flatDownhill.sqrMagnitude <= MinDownhillSqr )
			return;

		float uphillDot = Vector3.Dot( flatMoveIntent.normalized, -flatDownhill );
		if ( uphillDot >= SlideExitDot )
		{
			_isClimbing = true;
			_climbReleaseTimer = 0f;
			ClearSlideExitBoost();
		}
	}

	void ApplyClimbMovement( Vector3 flatMoveIntent, float dt )
	{
		if ( !_hasGroundHit )
		{
			ClearClimb();
			ApplyStandardPlanarMovement( flatMoveIntent, dt );
			return;
		}

		GetClimbSurfaceAxes( out Vector3 climbUp, out Vector3 climbRight );
		Vector3 surfaceIntent = Vector3.zero;
		if ( flatMoveIntent.sqrMagnitude > 0.0001f )
		{
			GetFlatAxes( out Vector3 flatForward, out Vector3 flatRight );
			float forward = Vector3.Dot( flatMoveIntent, flatForward );
			float right = Vector3.Dot( flatMoveIntent, flatRight );
			surfaceIntent = climbUp * forward + climbRight * right;
			if ( surfaceIntent.sqrMagnitude > 0.0001f )
				surfaceIntent = surfaceIntent.normalized * flatMoveIntent.magnitude;
			else
				surfaceIntent = Vector3.ProjectOnPlane( flatMoveIntent, _groundNormal );
		}

		float carryScale = _carry != null ? _carry.MoveSpeedMultiplier : 1f;
		float targetSpeed = ( _wantsSprint ? ClimbSprintSpeed : ClimbSpeed ) * carryScale;
		Vector3 desired = surfaceIntent.sqrMagnitude > 0.0001f
			? surfaceIntent.normalized * targetSpeed
			: Vector3.zero;

		float rate = desired.sqrMagnitude >= _planarVelocity.sqrMagnitude
			? ClimbAcceleration
			: ClimbDeceleration;
		if ( desired.sqrMagnitude < 0.0001f )
			rate = ClimbDeceleration;

		_planarVelocity = Vector3.MoveTowards( _planarVelocity, desired, rate * dt );
		_planarVelocity = Vector3.ProjectOnPlane( _planarVelocity, _groundNormal );
	}

	Vector3 ComposeClimbMoveVelocity( Vector3 slopeVelocity )
	{
		Vector3 alongSurface = Vector3.ProjectOnPlane( slopeVelocity, _groundNormal );
		return alongSurface - _groundNormal * ClimbSurfacePull;
	}

	void GetClimbSurfaceAxes( out Vector3 climbUp, out Vector3 climbRight )
	{
		climbUp = Vector3.ProjectOnPlane( Vector3.up, _groundNormal );
		if ( climbUp.sqrMagnitude < 0.0001f )
		{
			Vector3 downhill = GetDownhillDirection();
			climbUp = downhill.sqrMagnitude > MinDownhillSqr ? -downhill : transform.forward;
			climbUp = Vector3.ProjectOnPlane( climbUp, _groundNormal );
		}

		if ( climbUp.sqrMagnitude > 0.0001f )
			climbUp.Normalize();
		else
			climbUp = Vector3.forward;

		climbRight = Vector3.Cross( _groundNormal, climbUp );
		if ( climbRight.sqrMagnitude > 0.0001f )
			climbRight.Normalize();
		else
			climbRight = transform.right;
	}

	void PerformClimbJumpOff()
	{
		Vector3 jumpDir = _groundNormal + Vector3.up;
		if ( jumpDir.sqrMagnitude < 0.0001f )
			jumpDir = Vector3.up;
		else
			jumpDir.Normalize();

		Vector3 impulse = jumpDir * ClimbJumpForce;
		_planarVelocity = new Vector3( impulse.x, 0f, impulse.z );
		_verticalVelocity = impulse.y;
		_jumpAvailable = false;
		_coyoteTimer = 0f;
		_ignoreGrounding = true;
		IsGrounded = false;
		_wasGrounded = false;
		_isSliding = false;
		ClearClimb();
		ClearSlideExitBoost();
		_hasGroundHit = false;
		_groundCollider = null;
		WasJumpThisFrame = true;
	}

	Vector3 ComposeGroundedMoveVelocity( Vector3 slopeVelocity )
	{
		Vector3 alongSurface = Vector3.ProjectOnPlane( slopeVelocity, _groundNormal );
		return alongSurface + Vector3.up * GroundStickVelocity;
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

	/// <summary>
	/// Maps horizontal move intent onto the ground plane while preserving XZ heading.
	/// Intersection of the ground with the vertical plane of the intent — slope adds
	/// climb/descend only; a sideways-tilted normal cannot veer travel left/right.
	/// </summary>
	Vector3 AlignMoveIntentToGround( Vector3 flatIntent )
	{
		float magnitude = flatIntent.magnitude;
		if ( magnitude < 0.0001f )
			return Vector3.zero;

		Vector3 flatDir = flatIntent / magnitude;
		Vector3 verticalPlaneNormal = Vector3.Cross( flatDir, Vector3.up );
		if ( verticalPlaneNormal.sqrMagnitude < 0.0001f )
			return Vector3.zero;

		Vector3 alongSurface = Vector3.Cross( _groundNormal, verticalPlaneNormal );
		if ( alongSurface.sqrMagnitude < 0.0001f )
		{
			alongSurface = Vector3.ProjectOnPlane( flatDir, _groundNormal );
			if ( alongSurface.sqrMagnitude < 0.0001f )
				return Vector3.zero;
		}

		alongSurface.Normalize();
		if ( Vector3.Dot( alongSurface, flatDir ) < 0f )
			alongSurface = -alongSurface;

		return alongSurface * magnitude;
	}

	void OnDrawGizmos()
	{
		if ( !drawMovementGizmos || !Application.isPlaying )
			return;

		Vector3 origin = transform.position + Vector3.up * 0.15f;
		float scale = movementGizmoScale;

		if ( _hasGroundHit )
		{
			Gizmos.color = Color.cyan;
			Gizmos.DrawRay( origin, _groundNormal * scale );

			Vector3 downhill = GetDownhillDirection();
			if ( downhill.sqrMagnitude > 0.0001f )
			{
				Gizmos.color = new Color( 1f, 0.35f, 0.1f, 1f );
				Gizmos.DrawRay( origin, downhill * scale );
			}
		}

		Gizmos.color = Color.white;
		Gizmos.DrawRay( origin, _debugFlatForward * scale );

		if ( _debugFlatMoveIntent.sqrMagnitude > 0.0001f )
		{
			Gizmos.color = Color.yellow;
			Gizmos.DrawRay( origin, _debugFlatMoveIntent.normalized * scale );
		}

		if ( _debugGroundMoveIntent.sqrMagnitude > 0.0001f )
		{
			Gizmos.color = Color.green;
			Gizmos.DrawRay( origin, _debugGroundMoveIntent.normalized * scale );
		}

		if ( _planarVelocity.sqrMagnitude > 0.0001f )
		{
			Gizmos.color = Color.magenta;
			Gizmos.DrawRay( origin, _planarVelocity.normalized * scale );
		}

		if ( _debugMoveVelocity.sqrMagnitude > 0.0001f )
		{
			Gizmos.color = new Color( 0.2f, 0.85f, 1f, 1f );
			Gizmos.DrawRay( origin + Vector3.up * 0.05f, _debugMoveVelocity.normalized * scale );
		}
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

		if ( _isClimbing )
		{
			MovementState = PlayerMovementState.Climbing;
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
		ClearClimb();
		_slideEnterCharge = 0f;
		ClearSlideExitBoost();
		_wantsSprint = false;
		_jumpAvailable = true;
		_doubleJumpAvailable = true;
		_isGliding = false;
		_ignoreGrounding = false;
		_coyoteTimer = 0f;
		_lastSlideTravelDirection = Vector3.zero;
		_groundNormal = Vector3.up;
		_groundAngle = 0f;
		_hasGroundHit = false;
		_groundCollider = null;
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
		ClearClimb();
		_slideEnterCharge = 0f;
		ClearSlideExitBoost();
		_wantsSprint = false;
		_jumpAvailable = true;
		_doubleJumpAvailable = true;
		_isGliding = false;
		_ignoreGrounding = false;
		_coyoteTimer = 0f;
		_lastSlideTravelDirection = Vector3.zero;
		_groundNormal = Vector3.up;
		_groundAngle = 0f;
		_hasGroundHit = false;
		_groundCollider = null;
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

		if ( _abilities != null )
			_abilities.SetInputEnabled( enabled );

		if ( _cleaning != null )
			_cleaning.SetInputEnabled( enabled );

		if ( _wholeStack != null )
			_wholeStack.SetInputEnabled( enabled );
	}

	static GameInput GetGameInput()
	{
		return InputController.Instance != null ? InputController.Instance.GameInput : null;
	}
}
