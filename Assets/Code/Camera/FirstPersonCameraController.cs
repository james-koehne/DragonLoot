using UnityEngine;

[RequireComponent( typeof( Camera ) )]
public class FirstPersonCameraController : MonoBehaviour
{
	public Camera Camera { get; private set; }

	Transform _body;
	CameraDefinition _definition;
	CameraDefinition Definition => RuntimeDefinition.Resolve( ref _definition );

	float MinPitch => RuntimeDefinition.Get( Definition, d => d.minPitch, -85f );
	float MaxPitch => RuntimeDefinition.Get( Definition, d => d.maxPitch, 85f );

	float _lookSensitivity = -1f;
	bool _invertYInitialized;
	bool _invertY;
	float _pitch;
	bool _inputEnabled = true;

	public float Pitch => _pitch;

	public float MinLookSensitivity => RuntimeDefinition.Get( Definition, d => d.minLookSensitivity, 0.01f );
	public float MaxLookSensitivity => RuntimeDefinition.Get( Definition, d => d.maxLookSensitivity, 5f );

	public float CurrentLookSensitivity
	{
		get
		{
			EnsureLookSensitivityInitialized();
			return _lookSensitivity;
		}
	}

	public bool InvertY
	{
		get
		{
			EnsureInvertYInitialized();
			return _invertY;
		}
		set
		{
			_invertYInitialized = true;
			_invertY = value;
		}
	}

	void Awake()
	{
		Camera = GetComponent<Camera>();
	}

	void Update()
	{
		if ( !_inputEnabled || _body == null )
			return;

		HandleLookInput();
	}

	public void Setup( Transform body )
	{
		_body = body;

		if ( _body != null )
			_pitch = Mathf.Clamp( NormalizePitch( transform.localEulerAngles.x ), MinPitch, MaxPitch );
	}

	public Vector3 GetCameraForward()
	{
		return transform.forward;
	}

	public void SetInputEnabled( bool enabled )
	{
		_inputEnabled = enabled;
		ApplyCursorState( enabled );
	}

	public void SetPitch( float pitch )
	{
		_pitch = Mathf.Clamp( NormalizePitch( pitch ), MinPitch, MaxPitch );
		transform.localRotation = Quaternion.Euler( _pitch, 0f, 0f );
	}

	public float AdjustLookSensitivity( float delta )
	{
		return SetLookSensitivity( CurrentLookSensitivity + delta );
	}

	public float SetLookSensitivity( float value )
	{
		EnsureLookSensitivityInitialized();
		float min = MinLookSensitivity;
		float max = MaxLookSensitivity;
		if ( max < min )
			max = min;
		_lookSensitivity = Mathf.Clamp( value, min, max );
		return _lookSensitivity;
	}

	void EnsureLookSensitivityInitialized()
	{
		if ( _lookSensitivity >= 0f )
			return;

		_lookSensitivity = RuntimeDefinition.Get( Definition, d => d.lookSensitivity, 1f );
	}

	void EnsureInvertYInitialized()
	{
		if ( _invertYInitialized )
			return;

		_invertYInitialized = true;
		_invertY = RuntimeDefinition.Get( Definition, d => d.invertY, false );
	}

	void HandleLookInput()
	{
		GameInput input = GetGameInput();
		if ( input == null )
			return;

		Vector2 delta = input.CameraDelta.ReadValue<Vector2>();
		if ( Mathf.Approximately( delta.x, 0f ) && Mathf.Approximately( delta.y, 0f ) )
			return;

		float sensitivity = CurrentLookSensitivity;
		_body.Rotate( 0f, delta.x * sensitivity, 0f, Space.World );

		float pitchDelta = delta.y * sensitivity;
		if ( InvertY )
			_pitch += pitchDelta;
		else
			_pitch -= pitchDelta;

		_pitch = Mathf.Clamp( _pitch, MinPitch, MaxPitch );
		transform.localRotation = Quaternion.Euler( _pitch, 0f, 0f );
	}

	static float NormalizePitch( float eulerX )
	{
		if ( eulerX > 180f )
			eulerX -= 360f;
		return eulerX;
	}

	static void ApplyCursorState( bool locked )
	{
		Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
		Cursor.visible = !locked;
	}

	static GameInput GetGameInput()
	{
		return InputController.Instance != null ? InputController.Instance.GameInput : null;
	}
}
