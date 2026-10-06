using System;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Dialogue callout: letterbox bars, FOV zoom, input lock, and either first-person look-at
/// or an authored third-person camera frame (when <see cref="_cameraFrame"/> is set).
/// </summary>
public class CinematicCalloutController : MonoBehaviour
{
	public const string MainCaveEntranceCalloutId = "main_cave_entrance_callout";
	public const string TalkToDragonCalloutId = "talk_to_dragon_callout";

	static readonly Dictionary<string, CinematicCalloutController> Controllers =
		new Dictionary<string, CinematicCalloutController>();

	[SerializeField]
	string _calloutId = MainCaveEntranceCalloutId;

	[SerializeField]
	[Tooltip( "Fallback look point when dragon head is unavailable." )]
	Transform _lookTarget;

	[SerializeField]
	[Tooltip( "Preferred look source — aims at the dragon head bone when set." )]
	DragonController _dragon;

	[Header( "Third Person Frame" )]
	[SerializeField]
	[Tooltip( "When set, detaches the camera to this authored pose instead of first-person look-at." )]
	Transform _cameraFrame;

	[SerializeField]
	[Tooltip( "Optional stand pose. Player snaps here when the third-person frame begins." )]
	Transform _standPose;

	[SerializeField]
	[Tooltip( "Root that rises off the ground during the talk. Leave empty to skip floating." )]
	Transform _floatRoot;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "How far the float root rises above its ground pose." )]
	float _floatHeight = 0.85f;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Seconds to rise (begin) and lower (end)." )]
	float _floatDuration = 0.85f;

	[Header( "Envelope" )]
	[SerializeField]
	[Min( 0f )]
	float _blendIn = 0.45f;

	[SerializeField]
	[Min( 0f )]
	float _blendOut = 0.4f;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Letterbox bar height (0-1 screen fraction)." )]
	float _letterboxPeak = 0.12f;

	[SerializeField]
	[Tooltip( "FOV delta from the player's base FOV. Negative zooms in." )]
	float _fovZoomDelta = -8f;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Seconds to ease the camera toward the look target / frame when the callout starts." )]
	float _lookBlendDuration = 0.5f;

	[Header( "Look Motion" )]
	[SerializeField]
	[Min( 0.01f )]
	[Tooltip( "How quickly yaw/pitch catch the moving head aim (first-person mode)." )]
	float _lookSmoothTime = 0.35f;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Degrees of slow yaw drift while holding." )]
	float _idleSwayYawDegrees = 1.2f;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Degrees of slow pitch drift while holding." )]
	float _idleSwayPitchDegrees = 0.7f;

	[SerializeField]
	[Min( 0.01f )]
	float _idleSwayFrequency = 0.35f;

	bool _active;
	bool _ending;
	bool _cameraDetached;
	bool _detachedLookActive;
	bool _floatActive;
	float _envelope;
	float _targetEnvelope;
	float _lookBlend;
	float _floatWeight;
	float _currentBodyYaw;
	float _currentPitch;
	float _yawVelocity;
	float _pitchVelocity;
	float _swayTime;
	float _baseFov;
	float _restorePitch;
	Vector3 _floatGroundPos;
	Vector3 _frameBlendStartPos;
	Quaternion _frameBlendStartRot;
	FirstPersonCameraController _camera;
	CameraController _cameraRig;
	PlayerController _player;
	CinematicLetterboxUI _letterbox;
	Action _onEnded;

	public string CalloutId => _calloutId;
	public bool IsActive => _active;
	public bool IsThirdPersonFrame => _cameraFrame != null;

	public static bool TryBegin( string calloutId )
	{
		CinematicCalloutController controller;
		if ( !TryGet( calloutId, out controller ) )
		{
			Debug.LogWarning( "CinematicCalloutController: no controller registered for id '" + calloutId + "'." );
			return false;
		}

		controller.Begin();
		return true;
	}

	public static bool TryEnd( string calloutId, Action onEnded = null )
	{
		CinematicCalloutController controller;
		if ( !TryGet( calloutId, out controller ) )
		{
			if ( onEnded != null )
				onEnded();
			return false;
		}

		controller.End( onEnded );
		return true;
	}

	public static bool TryGet( string calloutId, out CinematicCalloutController controller )
	{
		controller = null;
		if ( string.IsNullOrEmpty( calloutId ) )
			return false;
		return Controllers.TryGetValue( calloutId, out controller ) && controller != null;
	}

	public static bool IsAnyActive()
	{
		foreach ( KeyValuePair<string, CinematicCalloutController> pair in Controllers )
		{
			if ( pair.Value != null && pair.Value._active )
				return true;
		}

		return false;
	}

	void OnEnable()
	{
		if ( string.IsNullOrEmpty( _calloutId ) )
			return;
		Controllers[ _calloutId ] = this;
	}

	void OnDisable()
	{
		if ( !string.IsNullOrEmpty( _calloutId ) &&
		     Controllers.TryGetValue( _calloutId, out CinematicCalloutController existing ) &&
		     existing == this )
			Controllers.Remove( _calloutId );

		if ( _active )
			ForceRestore();
	}

	public void Begin()
	{
		if ( _active && !_ending )
			return;

		ResolveRefs();
		if ( _player == null || _camera == null )
		{
			Debug.LogWarning( "CinematicCalloutController: missing player or camera — skipping callout." );
			return;
		}

		_active = true;
		_ending = false;
		_onEnded = null;
		_targetEnvelope = 1f;
		_lookBlend = 0f;
		_floatWeight = 0f;
		_swayTime = 0f;
		_yawVelocity = 0f;
		_pitchVelocity = 0f;
		_currentBodyYaw = _player.transform.eulerAngles.y;
		_currentPitch = _camera.Pitch;
		_restorePitch = _camera.Pitch;

		_baseFov = _camera.Camera != null ? _camera.Camera.fieldOfView : 60f;
		_camera.SetFieldOfViewOverride( true );
		_player.SetCinematicInputLock( true );

		BeginPlatformFloat();

		if ( IsThirdPersonFrame )
			BeginThirdPersonFrame();
		else
			AttachPlayerToStandPose();

		_letterbox = CinematicLetterboxUI.EnsureExists();
		ApplyEnvelope( _envelope );
	}

	public void End( Action onEnded = null )
	{
		if ( !_active )
		{
			if ( onEnded != null )
				onEnded();
			return;
		}

		_ending = true;
		_targetEnvelope = 0f;
		_onEnded = onEnded;
		bool floatWillLower = _floatRoot != null && _floatHeight > 0f && _floatDuration > 0f && _floatWeight > 0f;
		if ( _blendOut <= 0f && !floatWillLower )
			FinishEnd();
	}

	void LateUpdate()
	{
		if ( !_active )
			return;

		float dt = Time.unscaledDeltaTime;
		float blendTime = _ending ? _blendOut : _blendIn;
		float step = blendTime > 0.0001f ? dt / blendTime : 1f;
		_envelope = Mathf.MoveTowards( _envelope, _targetEnvelope, step );

		if ( !_ending && _lookBlendDuration > 0.0001f )
			_lookBlend = Mathf.MoveTowards( _lookBlend, 1f, dt / _lookBlendDuration );
		else if ( !_ending )
			_lookBlend = 1f;

		_swayTime += dt;
		ApplyEnvelope( _envelope );
		UpdatePlatformFloat( dt );
		AttachPlayerToStandPose();

		if ( IsThirdPersonFrame && _cameraDetached )
			ApplyThirdPersonFrame();
		else
			ApplyLook( dt );

		if ( _ending && _envelope <= 0.0001f && !_floatActive )
			FinishEnd();
	}

	void BeginPlatformFloat()
	{
		_floatActive = false;
		_floatWeight = 0f;
		if ( _floatRoot == null || _floatHeight <= 0f )
			return;

		_floatGroundPos = _floatRoot.position;
		_floatActive = true;
		ApplyPlatformFloatPose( 0f );
	}

	void UpdatePlatformFloat( float dt )
	{
		if ( !_floatActive || _floatRoot == null )
			return;

		float target = _ending ? 0f : 1f;
		float duration = Mathf.Max( 0.0001f, _floatDuration );
		float step = dt / duration;
		_floatWeight = Mathf.MoveTowards( _floatWeight, target, step );
		ApplyPlatformFloatPose( EaseInOut( _floatWeight ) );

		if ( _ending && _floatWeight <= 0.0001f )
			_floatActive = false;
	}

	void ApplyPlatformFloatPose( float raisedWeight )
	{
		if ( _floatRoot == null )
			return;

		Vector3 pos = _floatGroundPos;
		pos.y += _floatHeight * Mathf.Clamp01( raisedWeight );
		_floatRoot.position = pos;
	}

	void AttachPlayerToStandPose()
	{
		if ( _player == null || _standPose == null )
			return;

		_player.SnapToWorldPose( _standPose.position, _standPose.rotation );
		_currentBodyYaw = _standPose.eulerAngles.y;
	}

	static float EaseInOut( float t )
	{
		t = Mathf.Clamp01( t );
		return t * t * ( 3f - 2f * t );
	}

	void BeginThirdPersonFrame()
	{
		AttachPlayerToStandPose();

		if ( _cameraRig == null )
			return;

		if ( _camera.Camera != null )
		{
			_frameBlendStartPos = _camera.Camera.transform.position;
			_frameBlendStartRot = _camera.Camera.transform.rotation;
		}
		else
		{
			_frameBlendStartPos = _cameraRig.transform.position;
			_frameBlendStartRot = _cameraRig.transform.rotation;
		}

		DetachCamera();
		_camera.BeginCinematicDetachedLook();
		_detachedLookActive = true;

		if ( _player.Carry != null )
			_player.Carry.SetCinematicHidden( true );

		ApplyThirdPersonFrame();
	}

	void DetachCamera()
	{
		if ( _cameraRig == null || _cameraDetached )
			return;

		_cameraRig.transform.SetParent( null, true );
		_cameraDetached = true;
		_player.SetCinematicCameraDetached( true );
	}

	void ReparentCameraToMount()
	{
		if ( _cameraRig == null || _player == null || _player.CameraMount == null )
			return;

		_cameraRig.transform.SetParent( _player.CameraMount, false );
		_cameraRig.ResetLocalPose();
		_cameraDetached = false;
		_player.SetCinematicCameraDetached( false );
	}

	void ApplyThirdPersonFrame()
	{
		if ( _cameraRig == null || _cameraFrame == null )
			return;

		// Hold the authored frame through letterbox fade-out; ForceRestore reparents to FP.
		float blend = _ending ? 1f : Mathf.Clamp01( _lookBlend );

		Vector3 targetPos = _cameraFrame.position;
		Quaternion targetRot = _cameraFrame.rotation;

		float swayWeight = blend * ( _ending ? _envelope : 1f );
		float swayYaw = Mathf.Sin( _swayTime * _idleSwayFrequency * Mathf.PI * 2f ) * _idleSwayYawDegrees * swayWeight;
		float swayPitch = Mathf.Sin( _swayTime * _idleSwayFrequency * 0.73f * Mathf.PI * 2f + 1.1f ) *
		                  _idleSwayPitchDegrees *
		                  swayWeight;
		targetRot = targetRot * Quaternion.Euler( swayPitch, swayYaw, 0f );

		Vector3 pos = Vector3.Lerp( _frameBlendStartPos, targetPos, blend );
		Quaternion rot = Quaternion.Slerp( _frameBlendStartRot, targetRot, blend );

		if ( _camera != null )
		{
			_camera.transform.localPosition = Vector3.zero;
			_camera.transform.localRotation = Quaternion.identity;
		}

		_cameraRig.SetWorldPose( pos, rot );
	}

	void ResolveRefs()
	{
		if ( GameMode.Instance == null )
			return;

		_player = GameMode.Instance.Player;
		_cameraRig = GameMode.Instance.cameraController;
		if ( _cameraRig != null )
			_camera = _cameraRig.FirstPerson;
		if ( _camera == null && _player != null )
			_camera = _player.GetComponentInChildren<FirstPersonCameraController>( true );

		if ( _dragon == null && _lookTarget != null )
		{
			_dragon = _lookTarget.GetComponentInParent<DragonController>();
			if ( _dragon == null )
				_dragon = _lookTarget.GetComponentInChildren<DragonController>( true );
		}
	}

	bool TryGetLookPoint( out Vector3 lookPoint )
	{
		lookPoint = Vector3.zero;
		if ( _dragon != null && _dragon.HeadBone != null )
		{
			lookPoint = _dragon.HeadBone.position;
			return true;
		}

		if ( _lookTarget != null )
		{
			lookPoint = _lookTarget.position;
			return true;
		}

		return false;
	}

	void ApplyLook( float dt )
	{
		if ( _player == null || _camera == null || _camera.Camera == null )
			return;

		float desiredYaw = _currentBodyYaw;
		float desiredPitch = _currentPitch;
		if ( TryGetLookPoint( out Vector3 lookPoint ) )
		{
			Vector3 origin = _camera.Camera.transform.position;
			Vector3 toTarget = lookPoint - origin;
			if ( toTarget.sqrMagnitude > 0.0001f )
			{
				Quaternion look = Quaternion.LookRotation( toTarget.normalized, Vector3.up );
				Vector3 euler = look.eulerAngles;
				desiredYaw = euler.y;
				desiredPitch = NormalizePitch( euler.x );
			}
		}

		float swayWeight = _lookBlend * ( _ending ? _envelope : 1f );
		float swayYaw = Mathf.Sin( _swayTime * _idleSwayFrequency * Mathf.PI * 2f ) * _idleSwayYawDegrees * swayWeight;
		float swayPitch = Mathf.Sin( _swayTime * _idleSwayFrequency * 0.73f * Mathf.PI * 2f + 1.1f ) *
		                  _idleSwayPitchDegrees *
		                  swayWeight;
		desiredYaw += swayYaw;
		desiredPitch = Mathf.Clamp( desiredPitch + swayPitch, -85f, 85f );

		float blend = Mathf.Clamp01( _lookBlend );
		float blendedYaw = Mathf.LerpAngle( _currentBodyYaw, desiredYaw, blend );
		float blendedPitch = Mathf.Lerp( _currentPitch, desiredPitch, blend );

		float smooth = Mathf.Max( 0.01f, _lookSmoothTime );
		_currentBodyYaw = Mathf.SmoothDampAngle( _currentBodyYaw, blendedYaw, ref _yawVelocity, smooth, Mathf.Infinity, dt );
		_currentPitch = Mathf.SmoothDamp( _currentPitch, blendedPitch, ref _pitchVelocity, smooth, Mathf.Infinity, dt );

		_player.transform.rotation = Quaternion.Euler( 0f, _currentBodyYaw, 0f );
		_camera.SetPitch( _currentPitch );
	}

	void ApplyEnvelope( float amount )
	{
		if ( _letterbox != null )
			_letterbox.SetLetterboxAmount( _letterboxPeak * amount );

		if ( _camera != null )
			_camera.SetFieldOfView( _baseFov + _fovZoomDelta * amount );
	}

	void FinishEnd()
	{
		ForceRestore();
		Action callback = _onEnded;
		_onEnded = null;
		if ( callback != null )
			callback();
	}

	void ForceRestore()
	{
		if ( _letterbox != null )
			_letterbox.SetLetterboxAmount( 0f );

		if ( _floatRoot != null && ( _floatActive || _floatWeight > 0f ) )
		{
			_floatWeight = 0f;
			ApplyPlatformFloatPose( 0f );
			_floatActive = false;
		}

		AttachPlayerToStandPose();

		if ( _cameraDetached && _cameraRig != null && _player != null )
			ReparentCameraToMount();
		else if ( _player != null )
			_player.SetCinematicCameraDetached( false );

		if ( _camera != null )
		{
			if ( _detachedLookActive )
			{
				_camera.EndCinematicDetachedLook( _restorePitch );
				_detachedLookActive = false;
			}

			_camera.SetFieldOfViewOverride( false );
			_camera.ResetFieldOfView();
		}

		if ( _player != null )
		{
			if ( _player.Carry != null )
				_player.Carry.SetCinematicHidden( false );
			_player.SetCinematicInputLock( false );
		}

		_active = false;
		_ending = false;
		_cameraDetached = false;
		_envelope = 0f;
		_targetEnvelope = 0f;
		_lookBlend = 0f;
		_floatWeight = 0f;
		_yawVelocity = 0f;
		_pitchVelocity = 0f;
	}

	static float NormalizePitch( float eulerX )
	{
		if ( eulerX > 180f )
			eulerX -= 360f;
		return eulerX;
	}
}
