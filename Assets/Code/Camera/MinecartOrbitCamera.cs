using UnityEngine;

/// <summary>
/// While driving a minecart, dollies the GameCamera rig from first person to a cart-orbit chase cam
/// and back on exit. Keeps the rig parented under CameraMount; owns world pose via CameraController.
/// Orbit yaw is stored relative to the cart so rail turns do not spin the framing.
/// </summary>
[DefaultExecutionOrder( 200 )]
[DisallowMultipleComponent]
public class MinecartOrbitCamera : MonoBehaviour
{
	enum Phase
	{
		Idle = 0,
		Entering = 1,
		Orbiting = 2,
		Exiting = 3
	}

	static readonly RaycastHit[] CollisionHits = new RaycastHit[ 24 ];

	/// <summary>Yaw offset from cart forward: 180 = directly behind the cart.</summary>
	const float BehindCartYawOffset = 180f;

	CameraController _rig;
	FirstPersonCameraController _firstPerson;
	CameraDefinition _definition;
	Phase _phase;
	MinecartInteractable _cart;
	PlayerController _player;
	float _blend;
	float _yawOffsetFromCart;
	float _orbitPitch;
	float _enterSeedYawOffset;
	float _enterSeedPitch;
	float _idleLookSeconds;
	int _collisionMask = -1;

	CameraDefinition Definition => RuntimeDefinition.Resolve( ref _definition );

	public bool IsActive => _phase != Phase.Idle;

	float OrbitDistance => RuntimeDefinition.Get( Definition, d => d.minecartOrbitDistance, 4.5f );
	float OrbitHeight => RuntimeDefinition.Get( Definition, d => d.minecartOrbitHeight, 1.6f );
	float OrbitLookahead => RuntimeDefinition.Get( Definition, d => d.minecartOrbitLookahead, 2.5f );
	float OrbitBlendTime => RuntimeDefinition.Get( Definition, d => d.minecartOrbitBlendTime, 0.45f );
	float OrbitDefaultPitch => RuntimeDefinition.Get( Definition, d => d.minecartOrbitDefaultPitch, 12f );
	float OrbitMinPitch => RuntimeDefinition.Get( Definition, d => d.minecartOrbitMinPitch, -15f );
	float OrbitMaxPitch => RuntimeDefinition.Get( Definition, d => d.minecartOrbitMaxPitch, 70f );
	float CollisionRadius => RuntimeDefinition.Get( Definition, d => d.minecartOrbitCollisionRadius, 0.25f );
	float CollisionSkin => RuntimeDefinition.Get( Definition, d => d.minecartOrbitCollisionSkin, 0.1f );
	bool AutoRecenter => RuntimeDefinition.Get( Definition, d => d.minecartOrbitAutoRecenter, true );
	float RecenterDelay => RuntimeDefinition.Get( Definition, d => d.minecartOrbitRecenterDelay, 1.5f );
	float RecenterSpeed => RuntimeDefinition.Get( Definition, d => d.minecartOrbitRecenterSpeed, 90f );

	void Awake()
	{
		_rig = GetComponent<CameraController>();
		if ( _rig == null )
			_rig = GetComponentInParent<CameraController>();
		_firstPerson = GetComponentInChildren<FirstPersonCameraController>( true );
	}

	void OnEnable()
	{
		EventBus.Subscribe<MinecartDriveEnteredEvent>( OnDriveEntered );
		EventBus.Subscribe<MinecartDriveExitedEvent>( OnDriveExited );
	}

	void OnDisable()
	{
		EventBus.Unsubscribe<MinecartDriveEnteredEvent>( OnDriveEntered );
		EventBus.Unsubscribe<MinecartDriveExitedEvent>( OnDriveExited );
		ForceRestoreImmediate();
	}

	void Update()
	{
		if ( _phase == Phase.Idle )
			return;

		EnsureLookLocked();
		if ( CanProcessOrbitLook() )
		{
			TickLookInput();
			TickRecenter( Time.deltaTime );
		}

		TickBlend( Time.deltaTime );
		ApplyCrosshair();
	}

	void LateUpdate()
	{
		if ( _phase == Phase.Idle )
			return;

		if ( IsCinematicOwningCamera() )
			return;

		EnsureLookLocked();
		ApplyOrbitPose();
	}

	void OnDriveEntered( MinecartDriveEnteredEvent evt )
	{
		if ( evt.Cart == null )
			return;

		EnsureRefs();
		_cart = evt.Cart;
		_player = ResolvePlayer();
		SeedOrbitFromFirstPerson();
		_enterSeedYawOffset = _yawOffsetFromCart;
		_enterSeedPitch = _orbitPitch;
		_idleLookSeconds = 0f;
		_blend = 0f;
		_phase = Phase.Entering;
		EnsureLookLocked();
		SetFillLightForOrbit( true );
		SetCarryHidden( true );
	}

	void OnDriveExited( MinecartDriveExitedEvent evt )
	{
		if ( _phase == Phase.Idle )
			return;

		if ( _phase == Phase.Exiting )
			return;

		_phase = Phase.Exiting;
		_idleLookSeconds = 0f;
	}

	void TickBlend( float dt )
	{
		float blendTime = OrbitBlendTime;
		float step = blendTime > 0.0001f ? dt / blendTime : 1f;

		if ( _phase == Phase.Entering )
		{
			_blend = Mathf.MoveTowards( _blend, 1f, step );
			EaseAnglesTowardDrivingFrame( _blend );
			if ( _blend >= 0.999f )
			{
				_blend = 1f;
				_phase = Phase.Orbiting;
			}
		}
		else if ( _phase == Phase.Exiting )
		{
			_blend = Mathf.MoveTowards( _blend, 0f, step );
			if ( _blend <= 0.001f )
			{
				_blend = 0f;
				FinishExitRestore();
			}
		}
	}

	void EaseAnglesTowardDrivingFrame( float blend )
	{
		if ( _cart == null )
			return;

		float t = Mathf.SmoothStep( 0f, 1f, Mathf.Clamp01( blend ) );
		float targetPitch = Mathf.Clamp( OrbitDefaultPitch, OrbitMinPitch, OrbitMaxPitch );
		_yawOffsetFromCart = Mathf.LerpAngle( _enterSeedYawOffset, BehindCartYawOffset, t );
		_orbitPitch = Mathf.Lerp( _enterSeedPitch, targetPitch, t );
		_orbitPitch = Mathf.Clamp( _orbitPitch, OrbitMinPitch, OrbitMaxPitch );
	}

	void TickLookInput()
	{
		if ( _phase != Phase.Orbiting )
			return;

		if ( IsCinematicOwningCamera() )
			return;

		GameInput input = ResolveInput();
		if ( input == null || input.CameraDelta == null )
			return;

		Vector2 delta = input.CameraDelta.ReadValue<Vector2>();
		if ( Mathf.Approximately( delta.x, 0f ) && Mathf.Approximately( delta.y, 0f ) )
			return;

		if ( _firstPerson == null )
			return;

		float sensitivity = _firstPerson.CurrentLookSensitivity;
		float yawDelta = delta.x * sensitivity;
		float pitchDelta = delta.y * sensitivity;
		if ( !_firstPerson.InvertY )
			pitchDelta = -pitchDelta;

		_yawOffsetFromCart += yawDelta;
		_orbitPitch = Mathf.Clamp( _orbitPitch + pitchDelta, OrbitMinPitch, OrbitMaxPitch );
		_idleLookSeconds = 0f;
	}

	void TickRecenter( float dt )
	{
		if ( _phase != Phase.Orbiting || !AutoRecenter || _cart == null )
			return;

		GameInput input = ResolveInput();
		bool looking = false;
		if ( input != null && input.CameraDelta != null )
		{
			Vector2 delta = input.CameraDelta.ReadValue<Vector2>();
			looking = !Mathf.Approximately( delta.x, 0f ) || !Mathf.Approximately( delta.y, 0f );
		}

		if ( looking )
		{
			_idleLookSeconds = 0f;
			return;
		}

		_idleLookSeconds += dt;
		if ( _idleLookSeconds < RecenterDelay )
			return;

		float targetPitch = Mathf.Clamp( OrbitDefaultPitch, OrbitMinPitch, OrbitMaxPitch );
		float maxStep = RecenterSpeed * dt;
		_yawOffsetFromCart = Mathf.MoveTowardsAngle( _yawOffsetFromCart, BehindCartYawOffset, maxStep );
		_orbitPitch = Mathf.MoveTowards( _orbitPitch, targetPitch, maxStep );
	}

	void ApplyOrbitPose()
	{
		EnsureRefs();
		if ( _rig == null || _firstPerson == null )
			return;

		float weight = Mathf.SmoothStep( 0f, 1f, Mathf.Clamp01( _blend ) );
		ResolveFirstPersonPose( out Vector3 fpPos, out Quaternion fpRot );
		ResolveOrbitPose( out Vector3 orbitPos, out Quaternion orbitRot );

		Vector3 pos = Vector3.Lerp( fpPos, orbitPos, weight );
		Quaternion rot = Quaternion.Slerp( fpRot, orbitRot, weight );

		_firstPerson.transform.localPosition = Vector3.zero;
		_firstPerson.transform.localRotation = Quaternion.identity;
		_rig.SetWorldPose( pos, rot );
	}

	void ResolveFirstPersonPose( out Vector3 position, out Quaternion rotation )
	{
		Transform mount = _player != null ? _player.CameraMount : null;
		position = mount != null ? mount.position : transform.position;

		if ( _phase == Phase.Exiting )
		{
			rotation = Quaternion.Euler( _orbitPitch, ResolveWorldOrbitYaw(), 0f );
			return;
		}

		float pitch = _firstPerson != null ? _firstPerson.Pitch : 0f;
		float yaw = 0f;
		if ( _player != null )
			yaw = _player.transform.eulerAngles.y;
		else if ( mount != null )
			yaw = mount.eulerAngles.y;

		rotation = Quaternion.Euler( pitch, yaw, 0f );
	}

	void ResolveOrbitPose( out Vector3 position, out Quaternion rotation )
	{
		Vector3 pivot = ResolvePivot();
		float distance = OrbitDistance;
		float worldYaw = ResolveWorldOrbitYaw();
		Quaternion orbitRot = Quaternion.Euler( _orbitPitch, worldYaw, 0f );
		Vector3 desired = pivot + orbitRot * ( Vector3.back * distance );
		desired = ClampAgainstCollision( pivot, desired );
		position = desired;

		Vector3 lookAt = pivot;
		if ( _cart != null )
		{
			Vector3 ahead = _cart.transform.forward;
			ahead.y = 0f;
			if ( ahead.sqrMagnitude > 0.0001f )
				lookAt += ahead.normalized * OrbitLookahead;
		}

		Vector3 toTarget = lookAt - position;
		if ( toTarget.sqrMagnitude < 0.0001f )
			rotation = orbitRot;
		else
			rotation = Quaternion.LookRotation( toTarget.normalized, Vector3.up );
	}

	float ResolveWorldOrbitYaw()
	{
		float cartYaw = _cart != null ? PlanarYaw( _cart.transform ) : 0f;
		return cartYaw + _yawOffsetFromCart;
	}

	Vector3 ResolvePivot()
	{
		if ( _cart != null )
			return _cart.transform.position + Vector3.up * OrbitHeight;

		if ( _player != null )
			return _player.transform.position + Vector3.up * OrbitHeight;

		return transform.position;
	}

	Vector3 ClampAgainstCollision( Vector3 pivot, Vector3 desired )
	{
		Vector3 delta = desired - pivot;
		float distance = delta.magnitude;
		if ( distance <= 0.0001f )
			return desired;

		Vector3 direction = delta / distance;
		EnsureCollisionMask();
		float radius = CollisionRadius;
		float skin = CollisionSkin;
		int count = Physics.SphereCastNonAlloc( pivot, radius, direction, CollisionHits, distance, _collisionMask, QueryTriggerInteraction.Ignore );
		float best = distance;
		for ( int i = 0; i < count; i++ )
		{
			RaycastHit hit = CollisionHits[ i ];
			if ( hit.collider == null )
				continue;

			if ( ShouldIgnoreCollision( hit.collider ) )
				continue;

			float allowed = hit.distance - skin;
			if ( allowed < best )
				best = allowed;
		}

		best = Mathf.Max( 0f, best );
		return pivot + direction * best;
	}

	bool ShouldIgnoreCollision( Collider collider )
	{
		if ( collider == null )
			return true;

		if ( _player != null )
		{
			Transform playerRoot = _player.transform;
			if ( collider.transform == playerRoot || collider.transform.IsChildOf( playerRoot ) )
				return true;
		}

		MinecartInteractable hitCart = collider.GetComponentInParent<MinecartInteractable>();
		if ( hitCart != null && _cart != null && hitCart.SharesConsistWith( _cart ) )
			return true;

		return false;
	}

	void EnsureCollisionMask()
	{
		if ( _collisionMask >= 0 )
			return;

		int mask = Physics.DefaultRaycastLayers;
		int playerLayer = PhysicsLayers.PlayerLayer;
		if ( playerLayer >= 0 )
			mask &= ~( 1 << playerLayer );

		int collectableLayer = PhysicsLayers.CollectableLayer;
		if ( collectableLayer >= 0 )
			mask &= ~( 1 << collectableLayer );

		_collisionMask = mask;
	}

	void FinishExitRestore()
	{
		EnsureRefs();

		float worldYaw = ResolveWorldOrbitYaw();
		if ( _player != null )
			_player.transform.rotation = Quaternion.Euler( 0f, worldYaw, 0f );

		if ( _rig != null )
			_rig.ResetLocalPose();

		if ( _firstPerson != null )
		{
			_firstPerson.SetPitch( _orbitPitch );
			_firstPerson.SetLookLocked( false );
			_firstPerson.SetSoftFillLightForcedOff( false );
		}

		ApplyCrosshairAlpha( 1f );
		SetCarryHidden( false );
		_cart = null;
		_phase = Phase.Idle;
		_blend = 0f;
	}

	void ForceRestoreImmediate()
	{
		if ( _phase == Phase.Idle )
			return;

		EnsureRefs();
		if ( _rig != null )
			_rig.ResetLocalPose();

		if ( _firstPerson != null )
		{
			_firstPerson.SetLookLocked( false );
			_firstPerson.SetSoftFillLightForcedOff( false );
		}

		ApplyCrosshairAlpha( 1f );
		SetCarryHidden( false );
		_cart = null;
		_phase = Phase.Idle;
		_blend = 0f;
	}

	void SeedOrbitFromFirstPerson()
	{
		EnsureRefs();
		float cartYaw = _cart != null ? PlanarYaw( _cart.transform ) : 0f;
		float worldYaw = cartYaw + BehindCartYawOffset;

		if ( _firstPerson != null && _firstPerson.Camera != null )
		{
			Vector3 forward = _firstPerson.Camera.transform.forward;
			forward.y = 0f;
			if ( forward.sqrMagnitude > 0.0001f )
				worldYaw = Mathf.Atan2( forward.x, forward.z ) * Mathf.Rad2Deg;
			else if ( _player != null )
				worldYaw = _player.transform.eulerAngles.y;

			_orbitPitch = Mathf.Clamp( _firstPerson.Pitch, OrbitMinPitch, OrbitMaxPitch );
		}
		else
		{
			if ( _player != null )
				worldYaw = _player.transform.eulerAngles.y;

			_orbitPitch = Mathf.Clamp( OrbitDefaultPitch, OrbitMinPitch, OrbitMaxPitch );
		}

		_yawOffsetFromCart = Mathf.DeltaAngle( cartYaw, worldYaw );
	}

	void EnsureLookLocked()
	{
		if ( _firstPerson != null )
			_firstPerson.SetLookLocked( true );
	}

	void SetFillLightForOrbit( bool orbiting )
	{
		EnsureRefs();
		if ( _firstPerson == null )
			return;

		_firstPerson.SetSoftFillLightForcedOff( orbiting );
	}

	void SetCarryHidden( bool hidden )
	{
		if ( _player == null )
			_player = ResolvePlayer();
		if ( _player == null || _player.Carry == null )
			return;

		_player.Carry.SetMinecartDriveHidden( hidden );
	}

	void ApplyCrosshair()
	{
		float alpha = 1f - Mathf.SmoothStep( 0f, 1f, Mathf.Clamp01( _blend ) );
		ApplyCrosshairAlpha( alpha );
	}

	static void ApplyCrosshairAlpha( float alpha )
	{
		if ( CrosshairUI.Instance != null )
			CrosshairUI.Instance.SetAlpha( alpha );
	}

	bool CanProcessOrbitLook()
	{
		if ( !Application.isFocused )
			return false;

		if ( DebugOverlay.IsOpen || PauseMenuUI.IsOpen || MapUI.IsOpen )
			return false;

		if ( _player == null )
			_player = ResolvePlayer();

		if ( _player != null && !_player.GameplayInputEnabled )
			return false;

		return true;
	}

	bool IsCinematicOwningCamera()
	{
		if ( _player == null )
			_player = ResolvePlayer();

		return _player != null && _player.IsCinematicLocked;
	}

	void EnsureRefs()
	{
		if ( _rig == null )
		{
			_rig = GetComponent<CameraController>();
			if ( _rig == null )
				_rig = GetComponentInParent<CameraController>();
		}

		if ( _firstPerson == null )
		{
			if ( _rig != null && _rig.FirstPerson != null )
				_firstPerson = _rig.FirstPerson;
			else
				_firstPerson = GetComponentInChildren<FirstPersonCameraController>( true );
		}
	}

	PlayerController ResolvePlayer()
	{
		if ( GameMode.Instance != null && GameMode.Instance.Player != null )
			return GameMode.Instance.Player;

		return _player;
	}

	static GameInput ResolveInput()
	{
		return InputController.Instance != null ? InputController.Instance.GameInput : null;
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
}
