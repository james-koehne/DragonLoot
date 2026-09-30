using UnityEngine;

/// <summary>
/// Sit in a drive minecart: W accelerates cart-forward along the path, S cart-back (no body flip).
/// A/D pick junction branches relative to the cart body (camera look never chooses a branch). E or Jump exits.
/// </summary>
[DefaultExecutionOrder( -50 )]
[DisallowMultipleComponent]
public class PlayerMinecartDrive : MonoBehaviour
{
	const float StopEpsilon = 0.05f;
	const float InputDeadzone = 0.12f;

	PlayerController _player;
	CharacterController _controller;
	MinecartInteractable _cart;
	GameInput _input;
	bool _ignoreInteractUntilRelease;
	float _signedSpeed;
	bool _wasReverseIntent;
	int _heldForwardSign = 1;
	bool _junctionTravelLatched;
	int _junctionTravelSign = 1;

	public bool IsDriving => _cart != null;

	public bool IsHandlingInteract => _cart != null;

	public MinecartInteractable ActiveCart => _cart;

	public void Setup( PlayerController player )
	{
		_player = player;
		_controller = player != null ? player.GetComponent<CharacterController>() : GetComponent<CharacterController>();
	}

	public void TryToggle( MinecartInteractable cart )
	{
		if ( cart == null || !cart.IsDriveCart )
			return;

		if ( _cart == cart )
		{
			Exit( inheritVelocity: true );
			return;
		}

		if ( _cart != null )
			Exit( inheritVelocity: false );

		Enter( cart );
	}

	/// <summary>
	/// After a junction ride starts or handoff completes, latch W to the remapped travel sign until the cart nose
	/// agrees (or we stop) so Evaluate-sign remaps do not dump throttle through a brake.
	/// </summary>
	public void NotifyJunctionTravelSign( int exitTravelSign )
	{
		if ( _cart == null )
			return;

		int sign = exitTravelSign >= 0 ? 1 : -1;
		_signedSpeed = _cart.DriveSpeed;
		float speed = Mathf.Abs( _signedSpeed );
		if ( speed < StopEpsilon )
			speed = Mathf.Abs( _cart.AlongTrackSpeed );

		if ( speed > StopEpsilon )
		{
			_junctionTravelSign = sign;
			_junctionTravelLatched = true;
			_heldForwardSign = sign;
		}
	}

	void OnDisable()
	{
		Exit( inheritVelocity: false );
	}

	void Update()
	{
		if ( _cart == null )
			return;

		if ( !_cart.isActiveAndEnabled )
		{
			Exit( inheritVelocity: false );
			return;
		}

		GameInput input = ResolveInput();
		if ( input == null )
		{
			Exit( inheritVelocity: false );
			return;
		}

		if ( _ignoreInteractUntilRelease )
		{
			if ( input.ContextualInteract == null || !input.ContextualInteract.IsPressed() )
				_ignoreInteractUntilRelease = false;
		}
		else if ( input.ContextualInteract != null && input.ContextualInteract.WasPressedThisFrame() )
		{
			Exit( inheritVelocity: true );
			return;
		}

		if ( input.Jump != null && input.Jump.WasPressedThisFrame() )
		{
			Exit( inheritVelocity: true );
			return;
		}

		TickDriveInput( input, Time.deltaTime );
	}

	void LateUpdate()
	{
		if ( _cart == null )
			return;

		ApplyCartFacing();

		if ( _controller == null || !_controller.enabled )
			return;

		Vector3 seat = _cart.ResolveSeatWorldPosition();
		Vector3 delta = seat - transform.position;
		if ( delta.sqrMagnitude > 0.0000001f )
			_controller.Move( delta );
	}

	void Enter( MinecartInteractable cart )
	{
		_cart = cart;
		_ignoreInteractUntilRelease = true;
		_signedSpeed = cart.DriveSpeed;
		if ( Mathf.Abs( _signedSpeed ) < StopEpsilon )
			_signedSpeed = cart.AlongTrackSpeed;
		_wasReverseIntent = false;
		_junctionTravelLatched = false;
		_heldForwardSign = ResolveCartForwardSign();
		cart.SetDriveSeatCollidersEnabled( false );
		cart.SetDriveBraking( false );
		cart.PlayDriveEnterFeedback();

		if ( _player != null )
			_player.SnapToWorldPosition( cart.ResolveSeatWorldPosition() );

		ApplyCartFacing();

		EventBus.Publish( new MinecartDriveEnteredEvent { Cart = cart } );

		PlayerMinecartPush push = _player != null ? _player.MinecartPush : null;
		if ( push != null )
			push.EndPush();

		PlayerMinecartRide ride = _player != null ? _player.MinecartRide : null;
		if ( ride != null )
			ride.NotifyDriveStarted();
	}

	void Exit( bool inheritVelocity )
	{
		if ( _cart == null )
			return;

		MinecartInteractable leaving = _cart;

		_cart.SetDriveBraking( false );
		_cart.PlayDriveExitFeedback();

		Vector3 inherit = Vector3.zero;
		if ( inheritVelocity && _player != null )
		{
			Vector3 tangent;
			if ( _cart.TryGetTrackTangent( out tangent ) )
				inherit = tangent * _signedSpeed;
		}

		Vector3 exitPos = transform.position + _cart.transform.right * 0.9f;
		if ( _player != null )
			_player.SnapToWorldPosition( exitPos );
		else if ( _controller != null && _controller.enabled )
			_controller.Move( _cart.transform.right * 0.9f );

		if ( inherit.sqrMagnitude > 0.0001f && _player != null )
			_player.AddPlanarVelocity( inherit );

		_cart.SetDriveSeatCollidersEnabled( true );
		_cart.SetDriveSpeed( _signedSpeed, false );
		_cart.ClearJunctionSteer();

		_cart = null;
		_signedSpeed = 0f;
		_ignoreInteractUntilRelease = false;
		_wasReverseIntent = false;
		_junctionTravelLatched = false;

		PlayerMinecartRide ride = _player != null ? _player.MinecartRide : null;
		if ( ride != null )
			ride.NotifyDriveEnded();

		EventBus.Publish( new MinecartDriveExitedEvent { Cart = leaving } );
	}

	void TickDriveInput( GameInput input, float dt )
	{
		// Prefer commanded drive speed; while coasting DriveSpeed is 0 — pick up residual along-track speed.
		_signedSpeed = _cart.DriveSpeed;
		if ( Mathf.Abs( _signedSpeed ) < StopEpsilon )
			_signedSpeed = _cart.AlongTrackSpeed;

		Vector2 move = Vector2.zero;
		if ( input.Move != null )
			move = input.Move.ReadValue<Vector2>();

		// Junction branches: raw A/D only. Camera / look must never choose an exit.
		_cart.SetJunctionSteer( move.x );

		float axis = move.y;
		float maxSpeed = _cart.DriveMaxSpeed;
		float accel = _cart.DriveAcceleration;
		float brake = _cart.DriveBrake;
		int forward = ResolveCartForwardSign();
		float forwardTarget = forward * maxSpeed;
		float reverseTarget = -forward * maxSpeed;
		bool powered = true;
		bool braking = false;
		bool reverseIntent = false;
		bool onJunctionRide = _cart.IsJunctionRiding;

		if ( _cart.IsHopping )
		{
			// Mid-hop: always accelerate to drive max along hop travel; ignore brake / coast / S.
			int hopSign = _cart.HopTravelSign;
			if ( hopSign == 0 )
				hopSign = forward;
			_signedSpeed = Mathf.MoveTowards( _signedSpeed, hopSign * maxSpeed, accel * dt );
			braking = false;
			powered = true;
			_cart.SetDriveBraking( false );
			_cart.SetDriveSpeed( _signedSpeed, powered );
			_wasReverseIntent = false;
			return;
		}

		if ( onJunctionRide )
		{
			// Hold speed through the curve; W/S stay cart +Z (nose), never rail exit sign.
			int noseSign = forward;
			float mag = Mathf.Abs( _signedSpeed );
			if ( mag < StopEpsilon )
				mag = Mathf.Abs( _cart.AlongTrackSpeed );
			int travelAlongTrack = Mathf.Abs( _signedSpeed ) > StopEpsilon
				? ( _signedSpeed >= 0f ? 1 : -1 )
				: noseSign;

			if ( axis > InputDeadzone )
				_signedSpeed = Mathf.MoveTowards( _signedSpeed, noseSign * maxSpeed, accel * dt );
			else if ( axis < -InputDeadzone )
			{
				reverseIntent = true;
				_signedSpeed = Mathf.MoveTowards( _signedSpeed, -noseSign * maxSpeed, accel * dt );
			}
			else if ( mag > StopEpsilon )
				_signedSpeed = travelAlongTrack * mag;
			else
				powered = false;

			braking = false;
			_junctionTravelLatched = true;
			_junctionTravelSign = noseSign;
			_heldForwardSign = noseSign;
		}
		else if ( axis > InputDeadzone )
		{
			if ( _signedSpeed * forward < -StopEpsilon )
			{
				braking = true;
				_signedSpeed = Mathf.MoveTowards( _signedSpeed, 0f, brake * dt );
			}
			else
				_signedSpeed = Mathf.MoveTowards( _signedSpeed, forwardTarget, accel * dt );
		}
		else if ( axis < -InputDeadzone )
		{
			if ( _signedSpeed * forward > StopEpsilon )
			{
				braking = true;
				_signedSpeed = Mathf.MoveTowards( _signedSpeed, 0f, brake * dt );
			}
			else
			{
				reverseIntent = true;
				_signedSpeed = Mathf.MoveTowards( _signedSpeed, reverseTarget, accel * dt );
			}
		}
		else
			powered = false;

		if ( reverseIntent && !_wasReverseIntent )
			_cart.PlayDriveReverseFeedback();

		_wasReverseIntent = reverseIntent;
		_cart.SetDriveBraking( braking );
		_cart.SetDriveSpeed( _signedSpeed, powered );

		// While coasting, mirror cart decay so re-throttle picks up residual speed.
		if ( !powered )
			_signedSpeed = _cart.AlongTrackSpeed;
	}

	void ApplyCartFacing()
	{
		if ( _cart == null )
			return;

		// Drive cart forward is local +Z (WheelFront).
		Vector3 driveForward = _cart.transform.forward;
		driveForward.y = 0f;
		if ( driveForward.sqrMagnitude < 0.0001f )
			return;

		float yaw = Mathf.Atan2( driveForward.x, driveForward.z ) * Mathf.Rad2Deg;
		transform.rotation = Quaternion.Euler( 0f, yaw, 0f );
	}

	int ResolveCartForwardSign()
	{
		Vector3 tangent;
		int noseSign = _heldForwardSign;
		if ( _cart != null && _cart.TryGetTrackTangent( out tangent ) )
		{
			Vector3 driveForward = _cart.transform.forward;
			driveForward.y = 0f;
			tangent.y = 0f;
			if ( driveForward.sqrMagnitude > 0.0001f && tangent.sqrMagnitude > 0.0001f )
			{
				float align = Vector3.Dot( driveForward.normalized, tangent.normalized );
				if ( Mathf.Abs( align ) >= 0.15f )
					noseSign = align >= 0f ? 1 : -1;
			}
		}

		// After a junction remap, keep W aligned with exit travel until the nose agrees or we stop.
		if ( _junctionTravelLatched )
		{
			float speed = Mathf.Abs( _signedSpeed );
			if ( _cart != null )
				speed = Mathf.Max( speed, Mathf.Abs( _cart.DriveSpeed ) );

			if ( speed <= StopEpsilon )
				_junctionTravelLatched = false;
			else if ( noseSign == _junctionTravelSign )
				_junctionTravelLatched = false;
			else
			{
				_heldForwardSign = _junctionTravelSign;
				return _junctionTravelSign;
			}
		}

		_heldForwardSign = noseSign;
		return _heldForwardSign;
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
