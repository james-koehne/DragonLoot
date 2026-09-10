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
	float _lookSensitivityMultiplier = 1f;
	bool _invertYInitialized;
	bool _invertY;
	float _pitch;
	bool _inputEnabled = true;
	bool _lookLocked;
	float _baseFieldOfView;
	bool _cinematicDetachedLook;
	float _cinematicYawOffset;
	float _cinematicPitchOffset;
	bool _fovOverrideActive;
	bool _wantMovementFov;
	float _movementFovWeight;
	Light _softFillLight;

	const string SoftFillLightChildName = "PlayerSoftFillLight";

	public float Pitch => _pitch;

	public float CinematicYawOffset => _cinematicYawOffset;

	public float CinematicPitchOffset => _cinematicPitchOffset;

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

	float SpeedFovBoost => RuntimeDefinition.Get( Definition, d => d.speedFovBoost, 8f );
	float SpeedFovBlendTime => RuntimeDefinition.Get( Definition, d => d.speedFovBlendTime, 0.2f );

	void Awake()
	{
		Camera = GetComponent<Camera>();
		_baseFieldOfView = Camera.fieldOfView;
	}

	void Update()
	{
		if ( !_inputEnabled || _lookLocked || _body == null )
			return;

		HandleLookInput();
	}

	void LateUpdate()
	{
		ApplyMovementFieldOfView();
	}

	public void Setup( Transform body )
	{
		_body = body;

		if ( _body != null )
			_pitch = Mathf.Clamp( NormalizePitch( transform.localEulerAngles.x ), MinPitch, MaxPitch );

		EnsureSoftFillLight();
	}

	void EnsureSoftFillLight()
	{
		CameraDefinition def = Definition;
		bool enabled = def == null || def.softFillLightEnabled;
		Color color = def != null ? def.softFillLightColor : new Color( 1f, 0.5f, 0.15f, 1f );
		float intensity = def != null ? def.softFillLightIntensity : 0.45f;
		float range = def != null ? def.softFillLightRange : 4.5f;
		Vector3 localOffset = def != null ? def.softFillLightLocalOffset : new Vector3( 0f, -0.15f, 0.25f );

		if ( !enabled || intensity <= 0.0001f )
		{
			if ( _softFillLight != null )
				_softFillLight.enabled = false;
			return;
		}

		if ( _softFillLight == null )
		{
			Transform existing = transform.Find( SoftFillLightChildName );
			if ( existing != null )
				_softFillLight = existing.GetComponent<Light>();

			if ( _softFillLight == null )
			{
				GameObject lightGo = new GameObject( SoftFillLightChildName );
				lightGo.transform.SetParent( transform, false );
				_softFillLight = lightGo.AddComponent<Light>();
			}
		}

		Transform lightTransform = _softFillLight.transform;
		lightTransform.localPosition = localOffset;
		lightTransform.localRotation = Quaternion.identity;
		lightTransform.localScale = Vector3.one;

		_softFillLight.type = LightType.Point;
		_softFillLight.color = color;
		_softFillLight.intensity = intensity;
		_softFillLight.range = range;
		_softFillLight.shadows = LightShadows.None;
		_softFillLight.enabled = true;
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

	public void SetLookLocked( bool locked )
	{
		_lookLocked = locked;
	}

	public void SetPitch( float pitch )
	{
		_pitch = Mathf.Clamp( NormalizePitch( pitch ), MinPitch, MaxPitch );
		transform.localRotation = Quaternion.Euler( _pitch, 0f, 0f );
	}

	/// <summary>
	/// Look accumulates as yaw/pitch offsets only — does not move the player body or camera transform.
	/// </summary>
	public void BeginCinematicDetachedLook()
	{
		_cinematicDetachedLook = true;
		_cinematicYawOffset = 0f;
		_cinematicPitchOffset = 0f;
	}

	public void EndCinematicDetachedLook( float restorePitch )
	{
		if ( !_cinematicDetachedLook )
			return;

		_cinematicDetachedLook = false;
		_cinematicYawOffset = 0f;
		_cinematicPitchOffset = 0f;
		SetPitch( restorePitch );
	}

	public void SetFieldOfView( float fov )
	{
		if ( Camera != null )
			Camera.fieldOfView = fov;
	}

	public void ResetFieldOfView()
	{
		_wantMovementFov = false;
		_movementFovWeight = 0f;
		SetFieldOfView( _baseFieldOfView );
	}

	public void SetFieldOfViewOverride( bool active )
	{
		_fovOverrideActive = active;
	}

	public void SetMovementFovActive( bool active )
	{
		_wantMovementFov = active;
	}

	void ApplyMovementFieldOfView()
	{
		if ( _fovOverrideActive || Camera == null )
			return;

		float target = _wantMovementFov ? 1f : 0f;
		float blendTime = SpeedFovBlendTime;
		float step = blendTime > 0.0001f ? Time.deltaTime / blendTime : 1f;
		_movementFovWeight = Mathf.MoveTowards( _movementFovWeight, target, step );
		Camera.fieldOfView = _baseFieldOfView + SpeedFovBoost * _movementFovWeight;
	}

	public void SetLookSensitivityMultiplier( float multiplier )
	{
		_lookSensitivityMultiplier = Mathf.Max( 0f, multiplier );
	}

	public void ResetLookSensitivityMultiplier()
	{
		_lookSensitivityMultiplier = 1f;
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

		float sensitivity = CurrentLookSensitivity * _lookSensitivityMultiplier;
		float yawDelta = delta.x * sensitivity;
		float pitchDelta = delta.y * sensitivity;
		if ( !InvertY )
			pitchDelta = -pitchDelta;

		if ( _cinematicDetachedLook )
		{
			_cinematicYawOffset += yawDelta;
			_cinematicPitchOffset = Mathf.Clamp( _cinematicPitchOffset + pitchDelta, MinPitch, MaxPitch );
			return;
		}

		_body.Rotate( 0f, yawDelta, 0f, Space.World );

		_pitch = Mathf.Clamp( _pitch + pitchDelta, MinPitch, MaxPitch );
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
