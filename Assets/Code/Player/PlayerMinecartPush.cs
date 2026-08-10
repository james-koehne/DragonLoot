using UnityEngine;

/// <summary>
/// Hold-Interact push session: cart translates along the player's planar facing while Interact is held.
/// </summary>
[DisallowMultipleComponent]
public class PlayerMinecartPush : MonoBehaviour
{
	PlayerController _player;
	MinecartInteractable _cart;
	GameInput _input;

	public bool IsPushing => _cart != null;

	public MinecartInteractable ActiveCart => _cart;

	public void Setup( PlayerController player )
	{
		_player = player;
	}

	public void BeginPush( MinecartInteractable cart )
	{
		if ( cart == null || !cart.isActiveAndEnabled )
			return;

		_cart = cart;
	}

	public void EndPush()
	{
		_cart = null;
	}

	void Update()
	{
		if ( _cart == null )
			return;

		if ( !_cart.isActiveAndEnabled )
		{
			EndPush();
			return;
		}

		GameInput input = ResolveInput();
		if ( input == null || !input.Interact.IsPressed() )
		{
			EndPush();
			return;
		}

		Transform playerTransform = _player != null ? _player.transform : transform;
		float attach = _cart.PushAttachRadius;
		Vector3 toCart = _cart.transform.position - playerTransform.position;
		toCart.y = 0f;
		if ( toCart.sqrMagnitude > attach * attach )
		{
			EndPush();
			return;
		}

		Vector3 facing = playerTransform.forward;
		facing.y = 0f;
		if ( facing.sqrMagnitude < 0.0001f )
			return;

		facing.Normalize();
		float speed = _cart.GetPushSpeed();
		_cart.MovePlanar( facing * ( speed * Time.deltaTime ) );
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
