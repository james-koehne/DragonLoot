using UnityEngine;

/// <summary>
/// Tap-ContextualInteract shove (coasts with drag) or hold-ContextualInteract follow-push/pull.
/// Hold motion matches the player's along-track walk and mouse look.
/// </summary>
[DisallowMultipleComponent]
public class PlayerMinecartPush : MonoBehaviour
{
	PlayerController _player;
	MinecartInteractable _cart;
	GameInput _input;
	bool _pending;
	bool _follow;
	float _heldTime;
	bool _pushMoving;

	public bool IsPushing => _follow && _cart != null;

	public bool IsHandlingInteract => _cart != null && ( _pending || _follow );

	public MinecartInteractable ActiveCart => _cart;

	public void Setup( PlayerController player )
	{
		_player = player;
	}

	public void BeginPress( MinecartInteractable cart )
	{
		if ( cart == null || !cart.isActiveAndEnabled )
			return;

		if ( _pending || _follow )
			return;

		_cart = cart;
		_pending = true;
		_follow = false;
		_heldTime = 0f;
		_pushMoving = false;
	}

	public void EndPush()
	{
		StopPushFeedback();
		if ( _cart != null )
			_cart.SetHoldPush( false );

		_cart = null;
		_pending = false;
		_follow = false;
		_heldTime = 0f;
		_pushMoving = false;
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
		if ( input == null )
		{
			EndPush();
			return;
		}

		if ( _pending )
			TickPending( input );
	}

	void LateUpdate()
	{
		if ( !_follow || _cart == null )
			return;

		GameInput input = ResolveInput();
		if ( input == null )
		{
			EndPush();
			return;
		}

		TickFollow( input );
	}

	void TickPending( GameInput input )
	{
		if ( input.ContextualInteract == null || !input.ContextualInteract.IsPressed() )
		{
			MinecartInteractable cart = _cart;
			EndPush();
			if ( cart != null && cart.isActiveAndEnabled )
				ResolveTap( cart );

			return;
		}

		_heldTime += Time.deltaTime;
		if ( _heldTime < ResolveHoldThreshold() )
			return;

		if ( !IsWithinAttach( _cart ) )
		{
			EndPush();
			return;
		}

		_pending = false;
		_follow = true;
		_cart.SetHoldPush( true );
		EventBus.Publish( new MinecartHoldPushStartedEvent { Cart = _cart } );
	}

	void TickFollow( GameInput input )
	{
		if ( input.ContextualInteract == null || !input.ContextualInteract.IsPressed() || !IsWithinAttach( _cart ) )
		{
			EndPush();
			return;
		}

		_cart.SetHoldPush( true );

		Vector3 tangent;
		if ( !_cart.TryGetTrackTangent( out tangent ) )
		{
			SetMoving( false );
			return;
		}

		float signed = WalkAlongTangent( tangent ) + MouseSteerAlongTangent( input, tangent );
		bool moved = Mathf.Abs( signed ) > 0.00001f && _cart.TryPushAlong( signed );
		SetMoving( moved );
	}

	float WalkAlongTangent( Vector3 tangent )
	{
		if ( _player == null )
			return 0f;

		Vector3 intent = _player.FlatMoveIntent;
		intent.y = 0f;
		if ( intent.sqrMagnitude < 0.0001f )
			return 0f;

		return Vector3.Dot( intent.normalized, tangent ) * _player.DesiredPlanarSpeed * Time.deltaTime;
	}

	float MouseSteerAlongTangent( GameInput input, Vector3 tangent )
	{
		if ( input == null || input.CameraDelta == null || _cart == null )
			return 0f;

		Vector2 look = input.CameraDelta.ReadValue<Vector2>();
		if ( look.sqrMagnitude < 0.000001f )
			return 0f;

		Camera cam = ResolveCameraComponent();
		if ( cam != null )
		{
			Vector3 origin = _cart.transform.position;
			Vector3 a = cam.WorldToScreenPoint( origin );
			Vector3 b = cam.WorldToScreenPoint( origin + tangent );
			if ( a.z > 0.01f && b.z > 0.01f )
			{
				Vector2 screenDir = new Vector2( b.x - a.x, b.y - a.y );
				if ( screenDir.sqrMagnitude > 0.0001f )
					return Vector2.Dot( look, screenDir.normalized ) * _cart.MouseSteerScale;
			}
		}

		Transform camTransform = ResolveCamera();
		Vector3 right = camTransform.right;
		right.y = 0f;
		Vector3 forward = camTransform.forward;
		forward.y = 0f;
		if ( right.sqrMagnitude < 0.0001f && forward.sqrMagnitude < 0.0001f )
			return 0f;

		if ( right.sqrMagnitude > 0.0001f )
			right.Normalize();
		if ( forward.sqrMagnitude > 0.0001f )
			forward.Normalize();

		Vector3 mouseWorld = right * look.x + forward * look.y;
		return Vector3.Dot( mouseWorld, tangent ) * _cart.MouseSteerScale;
	}

	Camera ResolveCameraComponent()
	{
		Transform mount = ResolveCamera();
		if ( mount == null )
			return null;

		Camera cam = mount.GetComponent<Camera>();
		if ( cam != null )
			return cam;

		return mount.GetComponentInChildren<Camera>();
	}

	Transform ResolveCamera()
	{
		if ( _player != null && _player.CameraMount != null )
			return _player.CameraMount;

		if ( _player != null )
			return _player.transform;

		return transform;
	}

	void ResolveTap( MinecartInteractable cart )
	{
		if ( _player == null )
			return;

		Vector3 facing = ResolveCamera().forward;
		facing.y = 0f;
		if ( facing.sqrMagnitude < 0.0001f )
			facing = _player.transform.forward;

		if ( cart.TryShove( facing ) )
			EventBus.Publish( new MinecartShovedEvent { Cart = cart } );
	}

	bool IsWithinAttach( MinecartInteractable cart )
	{
		if ( cart == null )
			return false;

		Vector3 toCart = cart.transform.position - ResolvePlayerPosition();
		toCart.y = 0f;
		float attach = cart.PushAttachRadius;
		return toCart.sqrMagnitude <= attach * attach;
	}

	float ResolveHoldThreshold()
	{
		if ( _cart != null )
			return _cart.PushHoldThreshold;

		return 0.18f;
	}

	Vector3 ResolvePlayerPosition()
	{
		if ( _player != null )
			return _player.transform.position;

		return transform.position;
	}

	void SetMoving( bool moving )
	{
		if ( _pushMoving == moving )
			return;

		_pushMoving = moving;
		if ( _cart != null )
			_cart.SetPushMoving( moving );
	}

	void StopPushFeedback()
	{
		if ( _cart != null && _pushMoving )
			_cart.SetPushMoving( false );

		_pushMoving = false;
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
