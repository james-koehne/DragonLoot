using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Debug free-fly camera: detaches the camera rig, freezes the player, and drives look/move for videos.
/// Toggle from the debug Camera section. Esc exits when the overlay is closed.
/// Hides all InterfaceController canvases (game HUD) while active; IMGUI debug overlay stays.
/// </summary>
[DisallowMultipleComponent]
public sealed class DebugFlyCamera : MonoBehaviour
{
	const float DefaultSpeed = 12f;
	const float DefaultBoostMultiplier = 3f;
	const float MinSpeed = 1f;
	const float MaxSpeed = 80f;

	static DebugFlyCamera s_instance;

	CameraController _cameraRig;
	FirstPersonCameraController _firstPerson;
	PlayerController _player;
	readonly List<Canvas> _hiddenCanvases = new List<Canvas>( 16 );
	float _yaw;
	float _pitch;
	float _speed = DefaultSpeed;
	float _boostMultiplier = DefaultBoostMultiplier;
	bool _active;
	bool _gameUiHidden;

	public static bool IsActive => s_instance != null && s_instance._active;

	public static float Speed
	{
		get => s_instance != null ? s_instance._speed : DefaultSpeed;
		set
		{
			EnsureExists();
			s_instance._speed = Mathf.Clamp( value, MinSpeed, MaxSpeed );
		}
	}

	public static float BoostMultiplier
	{
		get => s_instance != null ? s_instance._boostMultiplier : DefaultBoostMultiplier;
		set
		{
			EnsureExists();
			s_instance._boostMultiplier = Mathf.Clamp( value, 1f, 10f );
		}
	}

	public static float MinFlySpeed => MinSpeed;
	public static float MaxFlySpeed => MaxSpeed;

	public static DebugFlyCamera EnsureExists()
	{
		if ( s_instance != null )
			return s_instance;

		GameObject host = GameMode.Instance != null ? GameMode.Instance.gameObject : null;
		if ( host == null )
		{
			host = new GameObject( "DebugFlyCamera" );
			DontDestroyOnLoad( host );
		}

		s_instance = host.GetComponent<DebugFlyCamera>();
		if ( s_instance == null )
			s_instance = host.AddComponent<DebugFlyCamera>();
		return s_instance;
	}

	public static void Toggle()
	{
		EnsureExists();
		if ( s_instance._active )
			s_instance.Exit();
		else
			s_instance.Enter();
	}

	public static void ExitIfActive()
	{
		if ( s_instance != null && s_instance._active )
			s_instance.Exit();
	}

	void Awake()
	{
		if ( s_instance != null && s_instance != this )
		{
			Destroy( this );
			return;
		}

		s_instance = this;
	}

	void OnDestroy()
	{
		if ( _active )
			Exit();
		if ( s_instance == this )
			s_instance = null;
	}

	void Update()
	{
		if ( !_active )
			return;

		if ( DebugOverlay.IsOpen || PauseMenuUI.IsOpen || MapUI.IsOpen )
			return;

		if ( _cameraRig == null )
		{
			Exit();
			return;
		}

		HandleLook();
		HandleMove();
	}

	void Enter()
	{
		if ( _active )
			return;

		_player = DebugOverlay.GetPlayer();
		_firstPerson = DebugOverlay.GetCameraLook();
		_cameraRig = GameMode.Instance != null ? GameMode.Instance.cameraController : null;
		if ( _cameraRig == null && _firstPerson != null )
			_cameraRig = _firstPerson.GetComponentInParent<CameraController>();

		if ( _cameraRig == null || _player == null || _player.CameraMount == null )
		{
			DebugOverlay.SetLastAction( "Fly cam: no camera/player" );
			return;
		}

		Transform rig = _cameraRig.transform;
		Vector3 euler = rig.rotation.eulerAngles;
		_yaw = euler.y;
		_pitch = NormalizePitch( euler.x );

		rig.SetParent( null, true );
		_player.SetCinematicCameraDetached( true );
		_player.SetCinematicBodyLock( true );
		SetCarryHidden( true );

		if ( _firstPerson != null )
		{
			_firstPerson.SetLookLocked( true );
			_firstPerson.transform.localPosition = Vector3.zero;
			_firstPerson.transform.localRotation = Quaternion.identity;
		}

		ApplyRigPose();
		SetGameUiHidden( true );
		_active = true;
		DebugOverlay.SetLastAction( "Fly cam ON (Esc exit)" );
	}

	void Exit()
	{
		if ( !_active )
			return;

		_active = false;
		SetGameUiHidden( false );
		SetCarryHidden( false );

		if ( _cameraRig != null && _player != null && _player.CameraMount != null )
		{
			_cameraRig.transform.SetParent( _player.CameraMount, false );
			_cameraRig.ResetLocalPose();
			_player.SetCinematicCameraDetached( false );
		}

		if ( _firstPerson != null )
		{
			_firstPerson.SetLookLocked( false );
			if ( _player != null )
				_firstPerson.Setup( _player.transform );
		}

		if ( _player != null )
			_player.SetCinematicBodyLock( false );

		if ( !DebugOverlay.IsOpen && !PauseMenuUI.IsOpen && !MapUI.IsOpen && _player != null )
			_player.SetGameplayInputEnabled( true );

		_cameraRig = null;
		_firstPerson = null;
		_player = null;
		DebugOverlay.SetLastAction( "Fly cam OFF" );
	}

	void SetCarryHidden( bool hidden )
	{
		if ( _player == null || _player.Carry == null )
			return;
		_player.Carry.SetCinematicHidden( hidden );
	}

	void SetGameUiHidden( bool hidden )
	{
		if ( hidden == _gameUiHidden )
			return;

		if ( hidden )
		{
			_hiddenCanvases.Clear();
			if ( GameMode.Instance == null || GameMode.Instance.InterfaceController == null )
			{
				_gameUiHidden = true;
				return;
			}

			Canvas[] canvases = GameMode.Instance.InterfaceController.GetComponentsInChildren<Canvas>( true );
			for ( int i = 0; i < canvases.Length; i++ )
			{
				Canvas canvas = canvases[ i ];
				if ( canvas == null || !canvas.enabled )
					continue;
				canvas.enabled = false;
				_hiddenCanvases.Add( canvas );
			}

			_gameUiHidden = true;
			return;
		}

		for ( int i = 0; i < _hiddenCanvases.Count; i++ )
		{
			Canvas canvas = _hiddenCanvases[ i ];
			if ( canvas != null )
				canvas.enabled = true;
		}

		_hiddenCanvases.Clear();
		_gameUiHidden = false;
	}

	void HandleLook()
	{
		GameInput input = GetGameInput();
		if ( input == null || _firstPerson == null )
			return;

		Vector2 delta = input.CameraDelta.ReadValue<Vector2>();
		if ( Mathf.Approximately( delta.x, 0f ) && Mathf.Approximately( delta.y, 0f ) )
			return;

		float sensitivity = _firstPerson.CurrentLookSensitivity;
		float yawDelta = delta.x * sensitivity;
		float pitchDelta = delta.y * sensitivity;
		if ( !_firstPerson.InvertY )
			pitchDelta = -pitchDelta;

		_yaw += yawDelta;
		_pitch = Mathf.Clamp( _pitch + pitchDelta, -89f, 89f );
		ApplyRigPose();
	}

	void HandleMove()
	{
		GameInput input = GetGameInput();
		if ( input == null || _cameraRig == null )
			return;

		Vector2 move = input.Move.ReadValue<Vector2>();
		float vertical = 0f;
		if ( input.Jump.IsPressed() )
			vertical += 1f;

		Keyboard keyboard = Keyboard.current;
		if ( keyboard != null )
		{
			if ( keyboard[ Key.E ].isPressed )
				vertical += 1f;
			if ( keyboard[ Key.Q ].isPressed )
				vertical -= 1f;
		}

		bool boosting = input.Sprint.IsPressed();
		float speed = _speed * ( boosting ? _boostMultiplier : 1f );
		Transform rig = _cameraRig.transform;
		Vector3 forward = rig.forward;
		Vector3 right = rig.right;
		Vector3 wish = forward * move.y + right * move.x + Vector3.up * vertical;
		if ( wish.sqrMagnitude > 1f )
			wish.Normalize();

		rig.position += wish * ( speed * Time.unscaledDeltaTime );
	}

	void ApplyRigPose()
	{
		if ( _cameraRig == null )
			return;

		_cameraRig.SetWorldPose( _cameraRig.transform.position, Quaternion.Euler( _pitch, _yaw, 0f ) );
		if ( _firstPerson != null )
		{
			_firstPerson.transform.localPosition = Vector3.zero;
			_firstPerson.transform.localRotation = Quaternion.identity;
		}
	}

	static float NormalizePitch( float eulerX )
	{
		if ( eulerX > 180f )
			eulerX -= 360f;
		return eulerX;
	}

	static GameInput GetGameInput()
	{
		return InputController.Instance != null ? InputController.Instance.GameInput : null;
	}
}
