using UnityEngine;

/// <summary>
/// Carries the player with a minecart they are standing on, while still allowing
/// walking around on the bed. Does not parent the CharacterController.
/// </summary>
[DisallowMultipleComponent]
public class PlayerMinecartRide : MonoBehaviour
{
	const float DetachCoyote = 0.12f;

	PlayerController _player;
	CharacterController _controller;
	MinecartInteractable _cart;
	Vector3 _lastCartPos;
	Quaternion _lastCartRot;
	Vector3 _cartVelocity;
	bool _hasPose;
	float _airborneTime;

	bool _driveLocked;

	public MinecartInteractable ActiveCart => _cart;

	public void NotifyDriveStarted()
	{
		_driveLocked = true;
		Detach( inheritVelocity: false );
	}

	public void NotifyDriveEnded()
	{
		_driveLocked = false;
	}

	public void Setup( PlayerController player )
	{
		_player = player;
		_controller = player != null ? player.GetComponent<CharacterController>() : GetComponent<CharacterController>();
	}

	void OnDisable()
	{
		_driveLocked = false;
		Detach( inheritVelocity: false );
	}

	void LateUpdate()
	{
		if ( _driveLocked )
			return;

		if ( _player == null || _controller == null || !_controller.enabled )
		{
			Detach( inheritVelocity: false );
			return;
		}

		MinecartInteractable standing = ResolveStandingCart();
		if ( standing != null && standing.AttachPlayerWhenStanding )
		{
			_airborneTime = 0f;
			if ( standing != _cart )
			{
				Detach( inheritVelocity: false );
				Attach( standing );
			}

			ApplyCarry();
			return;
		}

		if ( _cart != null && CanKeepCoyoteAttach() )
		{
			_airborneTime += Time.deltaTime;
			ApplyCarry();
			return;
		}

		Detach( inheritVelocity: true );
	}

	bool CanKeepCoyoteAttach()
	{
		if ( _cart == null || !_cart.isActiveAndEnabled || !_cart.AttachPlayerWhenStanding )
			return false;

		if ( _player != null && _player.WasJumpThisFrame )
			return false;

		if ( _airborneTime >= DetachCoyote )
			return false;

		Vector3 toCart = transform.position - _cart.transform.position;
		toCart.y = 0f;
		float radius = Mathf.Max( 0.6f, _cart.BlockingHalfLength );
		return toCart.sqrMagnitude <= radius * radius;
	}

	void Attach( MinecartInteractable cart )
	{
		_cart = cart;
		_hasPose = false;
		_cartVelocity = Vector3.zero;
		_airborneTime = 0f;
		if ( _cart != null )
			_cart.SetRiderPresent( true );

		StorePose();
	}

	void Detach( bool inheritVelocity )
	{
		if ( _cart == null )
			return;

		if ( inheritVelocity && _player != null && _cartVelocity.sqrMagnitude > 0.0001f )
			_player.AddPlanarVelocity( _cartVelocity );

		_cart.SetRiderPresent( false );
		_cart = null;
		_hasPose = false;
		_cartVelocity = Vector3.zero;
		_airborneTime = 0f;
	}

	void ApplyCarry()
	{
		if ( _cart == null )
			return;

		Vector3 cartPos = _cart.transform.position;
		Quaternion cartRot = _cart.transform.rotation;
		if ( !_hasPose )
		{
			StorePose();
			return;
		}

		float dt = Time.deltaTime;
		Vector3 deltaPos = cartPos - _lastCartPos;
		if ( dt > 0.0001f )
			_cartVelocity = deltaPos / dt;

		Vector3 local = Quaternion.Inverse( _lastCartRot ) * ( transform.position - _lastCartPos );
		Vector3 target = cartPos + cartRot * local;
		Vector3 carry = target - transform.position;
		if ( carry.sqrMagnitude > 0.0000001f )
			_controller.Move( carry );

		StorePose();
	}

	void StorePose()
	{
		if ( _cart == null )
		{
			_hasPose = false;
			return;
		}

		_lastCartPos = _cart.transform.position;
		_lastCartRot = _cart.transform.rotation;
		_hasPose = true;
	}

	MinecartInteractable ResolveStandingCart()
	{
		if ( _player == null || !_player.IsGrounded )
			return null;

		Collider ground = _player.GroundCollider;
		if ( ground == null )
			return null;

		MinecartInteractable cart = ground.GetComponentInParent<MinecartInteractable>();
		if ( cart != null )
			return cart;

		TreasureItem item = ground.GetComponentInParent<TreasureItem>();
		if ( item == null )
			return null;

		return item.Owner as MinecartInteractable;
	}
}
