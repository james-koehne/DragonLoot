using UnityEngine;

/// <summary>
/// Sit in a drive minecart: W accelerates, S brakes, S after stop reverses. E or Jump exits.
/// </summary>
[DefaultExecutionOrder( 100 )]
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
	bool _moveLoopPlaying;
	bool _wasBraking;
	bool _wasReverseIntent;
	bool _hasCartYaw;
	float _lastCartYaw;
	int _heldForwardSign = 1;

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

		ApplyCartYawToLook();

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
		_signedSpeed = cart.AlongTrackSpeed;
		_moveLoopPlaying = false;
		_wasBraking = false;
		_wasReverseIntent = false;
		_hasCartYaw = false;
		_lastCartYaw = PlanarYaw( cart.transform );
		_heldForwardSign = ResolveForwardSign();
		cart.PlayDriveEnterFeedback();

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

		StopMoveLoop();
		_cart.PlayDriveExitFeedback();

		if ( inheritVelocity && _player != null )
		{
			Vector3 tangent;
			if ( _cart.TryGetTrackTangent( out tangent ) )
				_player.AddPlanarVelocity( tangent * _signedSpeed );
		}

		Vector3 side = _cart.transform.right * 0.9f;
		if ( _controller != null && _controller.enabled )
			_controller.Move( side );

		_cart.SetDriveSpeed( _signedSpeed, false );

		_cart = null;
		_signedSpeed = 0f;
		_ignoreInteractUntilRelease = false;
		_wasBraking = false;
		_wasReverseIntent = false;
		_hasCartYaw = false;

		PlayerMinecartRide ride = _player != null ? _player.MinecartRide : null;
		if ( ride != null )
			ride.NotifyDriveEnded();
	}

	void TickDriveInput( GameInput input, float dt )
	{
		_signedSpeed = _cart.AlongTrackSpeed;
		float axis = 0f;
		if ( input.Move != null )
			axis = input.Move.ReadValue<Vector2>().y;

		float maxSpeed = _cart.DriveMaxSpeed;
		float accel = _cart.DriveAcceleration;
		float brake = _cart.DriveBrake;
		int forward = ResolveForwardSign();
		float forwardTarget = forward * maxSpeed;
		float reverseTarget = -forward * maxSpeed;
		bool powered = true;
		bool braking = false;

		bool reverseIntent = false;
		if ( axis > InputDeadzone )
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

		if ( braking && !_wasBraking )
			_cart.PlayDriveBrakeFeedback();

		if ( reverseIntent && !_wasReverseIntent )
			_cart.PlayDriveReverseFeedback();

		_wasBraking = braking;
		_wasReverseIntent = reverseIntent;
		_cart.SetDriveSpeed( _signedSpeed, powered );
		SetMoveLoop( Mathf.Abs( _signedSpeed ) > StopEpsilon );
	}

	void ApplyCartYawToLook()
	{
		float yaw = PlanarYaw( _cart.transform );
		if ( _hasCartYaw )
		{
			float delta = Mathf.DeltaAngle( _lastCartYaw, yaw );
			if ( Mathf.Abs( delta ) > 0.0001f )
				transform.Rotate( 0f, delta, 0f, Space.World );
		}

		_lastCartYaw = yaw;
		_hasCartYaw = true;
	}

	int ResolveForwardSign()
	{
		Vector3 tangent;
		if ( _cart == null || !_cart.TryGetTrackTangent( out tangent ) )
			return _heldForwardSign;

		Vector3 facing = ResolveLookFlat();
		tangent.y = 0f;
		if ( facing.sqrMagnitude < 0.0001f || tangent.sqrMagnitude < 0.0001f )
			return _heldForwardSign;

		float align = Vector3.Dot( facing.normalized, tangent.normalized );
		if ( Mathf.Abs( align ) < 0.15f )
			return _heldForwardSign;

		_heldForwardSign = align >= 0f ? 1 : -1;
		return _heldForwardSign;
	}

	Vector3 ResolveLookFlat()
	{
		Transform mount = _player != null ? _player.CameraMount : null;
		Vector3 facing = mount != null ? mount.forward : transform.forward;
		facing.y = 0f;
		return facing;
	}

	static float PlanarYaw( Transform target )
	{
		if ( target == null )
			return 0f;

		Vector3 forward = target.forward;
		forward.y = 0f;
		if ( forward.sqrMagnitude < 0.0001f )
		{
			forward = target.up;
			forward.y = 0f;
		}

		if ( forward.sqrMagnitude < 0.0001f )
			return target.eulerAngles.y;

		return Mathf.Atan2( forward.x, forward.z ) * Mathf.Rad2Deg;
	}

	void SetMoveLoop( bool playing )
	{
		if ( playing == _moveLoopPlaying || _cart == null )
			return;

		_moveLoopPlaying = playing;
		_cart.SetDriveMoveLoop( playing );
	}

	void StopMoveLoop()
	{
		if ( !_moveLoopPlaying || _cart == null )
			return;

		_moveLoopPlaying = false;
		_cart.SetDriveMoveLoop( false );
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
