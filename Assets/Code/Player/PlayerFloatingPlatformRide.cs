using UnityEngine;

/// <summary>
/// Carries the player with a floating platform they are standing on.
/// Locks player Y to the platform surface so hover/rise feels smooth.
/// Does not parent the CharacterController (same pattern as <see cref="PlayerMinecartRide"/>).
/// </summary>
[DisallowMultipleComponent]
public class PlayerFloatingPlatformRide : MonoBehaviour
{
	const float DetachCoyote = 0.12f;
	const float InheritVelocityMinSqr = 0.04f;

	PlayerController _player;
	CharacterController _controller;
	FloatingPlatform _platform;
	Vector3 _lastPlatformPos;
	Quaternion _lastPlatformRot;
	Vector3 _platformVelocity;
	float _feetOffsetY;
	bool _hasPose;
	float _airborneTime;

	public FloatingPlatform ActivePlatform => _platform;
	public bool IsRiding => _platform != null;

	public void Setup( PlayerController player )
	{
		_player = player;
		_controller = player != null ? player.GetComponent<CharacterController>() : GetComponent<CharacterController>();
	}

	void OnDisable()
	{
		Detach( inheritVelocity: false );
	}

	void LateUpdate()
	{
		if ( _player == null || _controller == null || !_controller.enabled )
		{
			Detach( inheritVelocity: false );
			return;
		}

		FloatingPlatform standing = ResolveStandingPlatform();
		if ( standing != null && standing.IsRideable )
		{
			_airborneTime = 0f;
			if ( _player.WasLandingThisFrame )
				standing.NotifyPlayerLanded();

			if ( standing != _platform )
			{
				Detach( inheritVelocity: false );
				Attach( standing );
			}

			ApplyCarry();
			return;
		}

		if ( _platform != null && CanKeepCoyoteAttach() )
		{
			_airborneTime += Time.deltaTime;
			ApplyCarry();
			return;
		}

		Detach( inheritVelocity: true );
	}

	bool CanKeepCoyoteAttach()
	{
		if ( _platform == null || !_platform.isActiveAndEnabled || !_platform.IsRideable )
			return false;

		if ( _player != null && _player.WasJumpThisFrame )
			return false;

		if ( _airborneTime >= DetachCoyote )
			return false;

		Vector3 toPlatform = transform.position - _platform.transform.position;
		toPlatform.y = 0f;
		return toPlatform.sqrMagnitude <= 4f;
	}

	void Attach( FloatingPlatform platform )
	{
		_platform = platform;
		_hasPose = false;
		_platformVelocity = Vector3.zero;
		_airborneTime = 0f;
		CaptureFeetOffset();
		StorePose();
		if ( _player != null )
			_player.NotifyFloatingPlatformRide( true );
	}

	void Detach( bool inheritVelocity )
	{
		if ( _platform == null )
			return;

		if ( inheritVelocity && _player != null && _platform.IsRising && _platformVelocity.sqrMagnitude > InheritVelocityMinSqr )
		{
			Vector3 planar = _platformVelocity;
			planar.y = 0f;
			if ( planar.sqrMagnitude > InheritVelocityMinSqr )
				_player.AddPlanarVelocity( planar );
		}

		if ( _player != null )
			_player.NotifyFloatingPlatformRide( false );

		_platform = null;
		_hasPose = false;
		_platformVelocity = Vector3.zero;
		_airborneTime = 0f;
	}

	void CaptureFeetOffset()
	{
		if ( _platform == null )
		{
			_feetOffsetY = 0f;
			return;
		}

		_feetOffsetY = transform.position.y - _platform.transform.position.y;
	}

	void ApplyCarry()
	{
		if ( _platform == null )
			return;

		Vector3 platformPos = _platform.transform.position;
		Quaternion platformRot = _platform.transform.rotation;
		if ( !_hasPose )
		{
			CaptureFeetOffset();
			StorePose();
			return;
		}

		float dt = Time.deltaTime;
		Vector3 deltaPos = platformPos - _lastPlatformPos;
		if ( dt > 0.0001f )
			_platformVelocity = deltaPos / dt;

		// Follow platform XZ (and yaw) from last pose, but lock Y to captured feet offset.
		Vector3 local = Quaternion.Inverse( _lastPlatformRot ) * ( transform.position - _lastPlatformPos );
		Vector3 target = platformPos + platformRot * local;
		target.y = platformPos.y + _feetOffsetY;

		Vector3 carry = target - transform.position;
		if ( carry.sqrMagnitude > 0.0000001f )
			_controller.Move( carry );

		StorePose();
	}

	void StorePose()
	{
		if ( _platform == null )
		{
			_hasPose = false;
			return;
		}

		_lastPlatformPos = _platform.transform.position;
		_lastPlatformRot = _platform.transform.rotation;
		_hasPose = true;
	}

	FloatingPlatform ResolveStandingPlatform()
	{
		if ( _player == null || !_player.IsGrounded )
			return null;

		Collider ground = _player.GroundCollider;
		if ( ground == null )
			return null;

		return ground.GetComponentInParent<FloatingPlatform>();
	}
}
