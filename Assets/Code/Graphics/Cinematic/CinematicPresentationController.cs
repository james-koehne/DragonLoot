using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Serialization;

public struct CinematicPresentationOverrides
{
	public float FovPeak;
	public float LetterboxPeak;
	public float Rise;
	public float Hold;
	public float Fall;
}

/// <summary>
/// FOV widen, UI letterbox, crosshair fade, full input lock, camera pose blend, and optional XY look oval.
/// The player is placed at <see cref="_revealCameraTarget"/> when the presentation ends.
/// Scene setup (manual in Editor):
/// 1. Add <see cref="CinematicPresentationController"/> to the level (same object as LanternRevealSweepController is fine).
/// 2. Set presentation id to <see cref="IntroLedgePresentationId"/>.
/// 3. Place an empty Transform for the framed camera pose and assign <see cref="_revealCameraTarget"/>.
/// 4. Tune rise/hold/fall, peaks, and oval orbit in Inspector.
///
/// Testing:
/// 1. Play Mode → Debug overlay → World Events → Ledge.
/// 2. Expect bars + FOV widen; move/look/jump/interact disabled; cursor stays locked.
/// 3. Camera eases to the reveal target, look traces a clockwise XY oval, then the player is left at that pose.
/// 4. Walk into volume_hallway_end naturally for the full intro_ledge beat (once per profile).
/// </summary>
public class CinematicPresentationController : MonoBehaviour
{
	public const string IntroLedgePresentationId = "intro_ledge_cinematic";

	const float DefaultBaseFov = 60f;
	const float DefaultBarPeakHeight = 0.12f;
	const int OvalGizmoSegments = 32;

	static readonly Dictionary<string, CinematicPresentationController> Controllers = new Dictionary<string, CinematicPresentationController>();

	[SerializeField]
	string _presentationId = IntroLedgePresentationId;

	[SerializeField]
	float _baseFov = DefaultBaseFov;

	[FormerlySerializedAs( "_cameraPoseTarget" )]
	[SerializeField]
	[Tooltip( "World-space camera pose to blend toward. The player is placed here when the cinematic ends. Leave empty to skip pose blend." )]
	Transform _revealCameraTarget;

	[SerializeField]
	RevealPunchChannel _fovChannel = new RevealPunchChannel
	{
		rise = 0.5f,
		hold = 4f,
		fall = 1.2f,
		peak = 80f
	};

	[SerializeField]
	RevealPunchChannel _letterboxChannel = new RevealPunchChannel
	{
		rise = 0.5f,
		hold = 4f,
		fall = 1.2f,
		peak = DefaultBarPeakHeight
	};

	[FormerlySerializedAs( "_microPushChannel" )]
	[SerializeField]
	[Tooltip( "Envelope for blending camera position/rotation toward the reveal target. Fall is ignored (pose stays at the target)." )]
	RevealPunchChannel _cameraPoseChannel = new RevealPunchChannel
	{
		rise = 0.5f,
		hold = 4f,
		fall = 1.2f,
		peak = 1f
	};

	[Header( "Oval look" )]
	[SerializeField]
	[Tooltip( "When true, look traces a clockwise oval in camera XY (yaw/pitch) starting at the bottom. Position is unchanged." )]
	bool _ovalEnabled = true;

	[SerializeField]
	[Range( 0f, 1f )]
	[Tooltip( "Normalized presentation time (0-1) when the oval starts." )]
	float _ovalStartNormalized = 0.25f;

	[SerializeField]
	[Min( 0.01f )]
	[Tooltip( "Seconds for one clockwise oval. Clamped so it finishes before the presentation ends." )]
	float _ovalDuration = 3f;

	[SerializeField]
	[Min( 0.01f )]
	[Tooltip( "Full clockwise loops during the oval window. 1 = one closed oval." )]
	float _ovalTurns = 1f;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Yaw amplitude in degrees (camera-local X / look right-left)." )]
	float _ovalYawDegrees = 12f;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Pitch amplitude in degrees (camera-local Y / look up-down)." )]
	float _ovalPitchDegrees = 6f;

	Coroutine _presentationRoutine;
	bool _poseBlendActive;
	float _restorePitch;
	float _endPitch;
	bool _placePlayerAtReveal;
	Vector3 _playerEndPos;
	Quaternion _playerEndRot;

#if UNITY_EDITOR
	[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.SubsystemRegistration )]
	static void ResetStatics()
	{
		Controllers.Clear();
	}
#endif

	void OnEnable()
	{
		if ( string.IsNullOrEmpty( _presentationId ) )
			return;

		Controllers[ _presentationId ] = this;
	}

	void OnDisable()
	{
		if ( !string.IsNullOrEmpty( _presentationId ) &&
		     Controllers.TryGetValue( _presentationId, out CinematicPresentationController existing ) &&
		     existing == this )
			Controllers.Remove( _presentationId );

		if ( _presentationRoutine != null )
		{
			StopCoroutine( _presentationRoutine );
			_presentationRoutine = null;
		}

		ResetPresentationState();
	}

	public static bool TryPlay( string presentationId, CinematicPresentationOverrides overrides )
	{
		if ( string.IsNullOrEmpty( presentationId ) )
			return false;

		if ( !Controllers.TryGetValue( presentationId, out CinematicPresentationController controller ) || controller == null )
		{
			Debug.LogWarning( "CinematicPresentationController: no controller registered for presentation id '" + presentationId + "'." );
			return false;
		}

		controller.StartPresentation( overrides );
		return true;
	}

	public void StartPresentation( CinematicPresentationOverrides overrides )
	{
		if ( _presentationRoutine != null )
		{
			StopCoroutine( _presentationRoutine );
			_presentationRoutine = null;
			ResetPresentationState();
		}

		_presentationRoutine = StartCoroutine( PresentationRoutine( overrides ) );
	}

	IEnumerator PresentationRoutine( CinematicPresentationOverrides overrides )
	{
		RevealPunchChannel fovChannel = ResolveChannel( _fovChannel, overrides.Rise, overrides.Hold, overrides.Fall, overrides.FovPeak );
		RevealPunchChannel letterboxChannel = ResolveChannel( _letterboxChannel, overrides.Rise, overrides.Hold, overrides.Fall, overrides.LetterboxPeak );
		RevealPunchChannel poseChannel = ResolveChannel( _cameraPoseChannel, overrides.Rise, overrides.Hold, overrides.Fall, 0f );
		bool blendPose = _revealCameraTarget != null;

		float duration = Mathf.Max( fovChannel.TotalDuration, letterboxChannel.TotalDuration, blendPose ? poseChannel.TotalDuration : 0f );
		float elapsed = 0f;

		LockPlayerInputForCinematic( true );

		CinematicLetterboxUI letterbox = CinematicLetterboxUI.EnsureExists();
		FirstPersonCameraController firstPerson = ResolveFirstPersonCamera();
		CameraController cameraRig = ResolveCameraRig();
		CrosshairUI crosshair = CrosshairUI.Instance;
		PlayerController player = ResolvePlayer();

		if ( firstPerson != null )
			firstPerson.SetFieldOfViewOverride( true );

		if ( firstPerson != null && firstPerson.Camera != null && _baseFov <= 0f )
			_baseFov = firstPerson.Camera.fieldOfView;

		float baseFov = _baseFov > 0f ? _baseFov : DefaultBaseFov;

		Vector3 startCameraWorldPos = Vector3.zero;
		Quaternion startCameraWorldRot = Quaternion.identity;
		Vector3 targetCameraWorldPos = Vector3.zero;
		Quaternion targetCameraWorldRot = Quaternion.identity;
		_placePlayerAtReveal = false;
		_endPitch = 0f;
		Vector3 playerStartPos = Vector3.zero;
		Quaternion playerStartRot = Quaternion.identity;
		if ( blendPose && firstPerson != null && cameraRig != null )
		{
			_restorePitch = firstPerson.Pitch;
			startCameraWorldPos = cameraRig.transform.position;
			startCameraWorldRot = ResolveCameraWorldRotation( cameraRig, firstPerson );
			targetCameraWorldPos = _revealCameraTarget.position;
			targetCameraWorldRot = _revealCameraTarget.rotation;
			if ( player != null )
			{
				playerStartPos = player.transform.position;
				playerStartRot = player.transform.rotation;
				ResolvePlayerPoseFromCameraPose(
					playerStartPos,
					playerStartRot,
					startCameraWorldPos,
					targetCameraWorldPos,
					targetCameraWorldRot,
					out _playerEndPos,
					out _playerEndRot,
					out _endPitch );
				_placePlayerAtReveal = true;
			}
			else
				_endPitch = NormalizePitch( targetCameraWorldRot.eulerAngles.x );

			firstPerson.BeginCinematicDetachedLook();
			_poseBlendActive = true;
		}

		while ( elapsed < duration )
		{
			elapsed += Time.deltaTime;

			float fovWeight = fovChannel.EvaluateWeight( elapsed );
			float letterboxWeight = letterboxChannel.EvaluateWeight( elapsed );
			float poseWeight = blendPose ? poseChannel.EvaluateArriveWeight( elapsed ) : 0f;

			if ( firstPerson != null )
				firstPerson.SetFieldOfView( Mathf.Lerp( baseFov, fovChannel.peak, fovWeight ) );

			if ( letterbox != null )
			{
				float letterboxHeight = letterboxWeight * letterboxChannel.peak;
				letterbox.SetLetterboxAmount( letterboxHeight );
			}

			if ( crosshair != null )
				crosshair.SetAlpha( 1f - letterboxWeight );

			if ( _placePlayerAtReveal && player != null )
			{
				Vector3 nextPos = Vector3.Lerp( playerStartPos, _playerEndPos, poseWeight );
				Quaternion nextRot = Quaternion.Slerp( playerStartRot, _playerEndRot, poseWeight );
				Vector3 travelDelta = nextPos - player.transform.position;
				player.SnapToWorldPose( nextPos, nextRot );
				player.NotifyCinematicTravel( travelDelta, Time.deltaTime );
			}

			if ( blendPose && _poseBlendActive && firstPerson != null && cameraRig != null )
			{
				Vector3 blendedPos = Vector3.Lerp( startCameraWorldPos, targetCameraWorldPos, poseWeight );
				Quaternion blendedRot = Quaternion.Slerp( startCameraWorldRot, targetCameraWorldRot, poseWeight );
				ApplyCameraPose( cameraRig, firstPerson, blendedPos, EvaluateOvalLook( elapsed, duration, blendedRot ) );
			}

			yield return null;
		}

		ApplyFinalFrame( fovChannel, letterboxChannel, poseChannel, blendPose, baseFov, startCameraWorldPos, startCameraWorldRot, targetCameraWorldPos, targetCameraWorldRot, letterbox, firstPerson, cameraRig, crosshair, duration );
		ResetPresentationState();
		_presentationRoutine = null;
		EventBus.Publish( new CinematicPresentationEndedEvent { PresentationId = _presentationId } );
	}

	static void ApplyFinalFrame(
		RevealPunchChannel fovChannel,
		RevealPunchChannel letterboxChannel,
		RevealPunchChannel poseChannel,
		bool blendPose,
		float baseFov,
		Vector3 startCameraWorldPos,
		Quaternion startCameraWorldRot,
		Vector3 targetCameraWorldPos,
		Quaternion targetCameraWorldRot,
		CinematicLetterboxUI letterbox,
		FirstPersonCameraController firstPerson,
		CameraController cameraRig,
		CrosshairUI crosshair,
		float elapsed )
	{
		float fovWeight = fovChannel.EvaluateWeight( elapsed );
		float letterboxWeight = letterboxChannel.EvaluateWeight( elapsed );

		if ( firstPerson != null )
			firstPerson.SetFieldOfView( Mathf.Lerp( baseFov, fovChannel.peak, fovWeight ) );

		if ( letterbox != null )
			letterbox.SetLetterboxAmount( letterboxWeight * letterboxChannel.peak );

		if ( crosshair != null )
			crosshair.SetAlpha( 1f - letterboxWeight );

		if ( blendPose && firstPerson != null && cameraRig != null )
		{
			float poseWeight = poseChannel.EvaluateArriveWeight( elapsed );
			Vector3 blendedPos = Vector3.Lerp( startCameraWorldPos, targetCameraWorldPos, poseWeight );
			Quaternion blendedRot = Quaternion.Slerp( startCameraWorldRot, targetCameraWorldRot, poseWeight );
			ApplyCameraPose( cameraRig, firstPerson, blendedPos, blendedRot );
		}
	}

	Quaternion EvaluateOvalLook( float elapsed, float presentationDuration, Quaternion poseRotation )
	{
		float t;
		if ( !TryEvaluateOvalT( elapsed, presentationDuration, out t ) )
			return poseRotation;

		t = Mathf.SmoothStep( 0f, 1f, t );
		return ApplyOvalLook( poseRotation, t );
	}

	bool TryEvaluateOvalT( float elapsed, float presentationDuration, out float t )
	{
		t = 0f;
		if ( !_ovalEnabled || _ovalDuration <= 0.0001f || _ovalTurns <= 0f )
			return false;

		if ( _ovalYawDegrees <= 0f && _ovalPitchDegrees <= 0f )
			return false;

		float start = Mathf.Clamp01( _ovalStartNormalized ) * presentationDuration;
		float remaining = presentationDuration - start;
		if ( remaining <= 0.0001f )
			return false;

		float ovalDuration = Mathf.Min( _ovalDuration, remaining );
		float local = elapsed - start;
		if ( local < 0f || local > ovalDuration )
			return false;

		t = ovalDuration > 0.0001f ? Mathf.Clamp01( local / ovalDuration ) : 1f;
		return true;
	}

	Quaternion ApplyOvalLook( Quaternion poseRotation, float t )
	{
		float angle = 2f * Mathf.PI * _ovalTurns * t;
		float yawDeg = -_ovalYawDegrees * Mathf.Sin( angle );
		float pitchDeg = -_ovalPitchDegrees * ( 1f - Mathf.Cos( angle ) );
		return poseRotation * Quaternion.Euler( pitchDeg, yawDeg, 0f );
	}

	static void ApplyCameraPose(
		CameraController cameraRig,
		FirstPersonCameraController firstPerson,
		Vector3 pos,
		Quaternion rot )
	{
		firstPerson.transform.localPosition = Vector3.zero;
		firstPerson.transform.localRotation = Quaternion.identity;
		cameraRig.SetWorldPose( pos, rot );
	}

	static Quaternion ResolveCameraWorldRotation( CameraController cameraRig, FirstPersonCameraController firstPerson )
	{
		if ( firstPerson != null && firstPerson.Camera != null )
			return firstPerson.Camera.transform.rotation;

		if ( cameraRig != null )
			return cameraRig.transform.rotation;

		return Quaternion.identity;
	}

	static void ResolvePlayerPoseFromCameraPose(
		Vector3 playerStartPos,
		Quaternion playerStartRot,
		Vector3 cameraStartPos,
		Vector3 targetCameraPos,
		Quaternion targetCameraRot,
		out Vector3 playerPos,
		out Quaternion playerRot,
		out float pitch )
	{
		Vector3 camLocal = Quaternion.Inverse( playerStartRot ) * ( cameraStartPos - playerStartPos );
		float yaw = targetCameraRot.eulerAngles.y;
		playerRot = Quaternion.Euler( 0f, yaw, 0f );
		playerPos = targetCameraPos - playerRot * camLocal;
		pitch = NormalizePitch( targetCameraRot.eulerAngles.x );
	}

	static float NormalizePitch( float eulerX )
	{
		if ( eulerX > 180f )
			eulerX -= 360f;
		return eulerX;
	}

	void ResetPresentationState()
	{
		PlayerController player = ResolvePlayer();
		if ( _placePlayerAtReveal && player != null )
			player.SnapToWorldPose( _playerEndPos, _playerEndRot );

		LockPlayerInputForCinematic( false );

		FirstPersonCameraController firstPerson = ResolveFirstPersonCamera();
		if ( firstPerson != null )
		{
			if ( _poseBlendActive )
				firstPerson.EndCinematicDetachedLook( _placePlayerAtReveal ? _endPitch : _restorePitch );

			firstPerson.SetFieldOfViewOverride( false );
			firstPerson.ResetFieldOfView();
			firstPerson.ResetLookSensitivityMultiplier();
		}

		_poseBlendActive = false;
		_placePlayerAtReveal = false;

		CameraController cameraRig = ResolveCameraRig();
		if ( cameraRig != null )
			cameraRig.ResetLocalPose();

		CinematicLetterboxUI letterbox = CinematicLetterboxUI.Instance;
		if ( letterbox != null )
			letterbox.SetLetterboxAmount( 0f );

		CrosshairUI crosshair = CrosshairUI.Instance;
		if ( crosshair != null )
			crosshair.SetAlpha( 1f );
	}

	static void LockPlayerInputForCinematic( bool locked )
	{
		PlayerController player = ResolvePlayer();
		if ( player == null )
			return;

		player.SetCinematicInputLock( locked );
	}

	static RevealPunchChannel ResolveChannel( RevealPunchChannel defaults, float riseOverride, float holdOverride, float fallOverride, float peakOverride )
	{
		RevealPunchChannel channel = defaults;
		if ( peakOverride > 0f )
			channel.peak = peakOverride;
		if ( riseOverride > 0f )
			channel.rise = riseOverride;
		if ( holdOverride > 0f )
			channel.hold = holdOverride;
		if ( fallOverride > 0f )
			channel.fall = fallOverride;
		return channel;
	}

	static PlayerController ResolvePlayer()
	{
		if ( GameMode.Instance == null )
			return null;

		return GameMode.Instance.Player;
	}

	static FirstPersonCameraController ResolveFirstPersonCamera()
	{
		if ( GameMode.Instance == null || GameMode.Instance.cameraController == null )
			return null;

		return GameMode.Instance.cameraController.FirstPerson;
	}

	static CameraController ResolveCameraRig()
	{
		if ( GameMode.Instance == null )
			return null;

		return GameMode.Instance.cameraController;
	}

#if UNITY_EDITOR
	void OnDrawGizmosSelected()
	{
		if ( _revealCameraTarget == null )
			return;

		Gizmos.color = new Color( 0.3f, 0.85f, 1f, 0.9f );
		Gizmos.DrawWireSphere( _revealCameraTarget.position, 0.12f );
		Gizmos.DrawLine( _revealCameraTarget.position, _revealCameraTarget.position + _revealCameraTarget.forward * 0.75f );

		if ( !_ovalEnabled )
			return;

		const float lookGizmoDistance = 2f;
		Gizmos.color = new Color( 1f, 0.75f, 0.2f, 0.9f );
		Vector3 origin = _revealCameraTarget.position;
		Vector3 prev = EvaluateOvalLookPoint( 0f, origin, lookGizmoDistance );
		for ( int i = 1; i <= OvalGizmoSegments; i++ )
		{
			float t = i / (float)OvalGizmoSegments;
			Vector3 next = EvaluateOvalLookPoint( t, origin, lookGizmoDistance );
			Gizmos.DrawLine( prev, next );
			prev = next;
		}
	}

	Vector3 EvaluateOvalLookPoint( float t, Vector3 origin, float distance )
	{
		Quaternion look = ApplyOvalLook( _revealCameraTarget.rotation, t );
		return origin + look * Vector3.forward * distance;
	}
#endif
}
