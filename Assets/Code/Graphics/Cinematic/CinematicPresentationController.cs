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
/// FOV widen, UI letterbox, crosshair fade, look dampening, and optional camera pose blend.
/// Player body stays frozen and cannot move; only the camera blends to the pose target and fades back.
/// Scene setup (manual in Editor):
/// 1. Add <see cref="CinematicPresentationController"/> to the level (same object as LanternRevealSweepController is fine).
/// 2. Set presentation id to <see cref="IntroLedgePresentationId"/>.
/// 3. Place an empty Transform for the framed camera pose and assign <see cref="_cameraPoseTarget"/>.
/// 4. Tune rise/hold/fall and peaks in Inspector (defaults: 0.5s / 4s / 1.2s, FOV 80, letterbox 0.12).
///
/// Testing:
/// 1. Play Mode → Debug overlay → World Events → Ledge.
/// 2. Expect bars + FOV widen; look at 25% sensitivity; player root does not move/rotate.
/// 3. Camera eases to the pose target, then fades back to the starting camera view.
/// 4. Walk into volume_hallway_end naturally for the full intro_ledge beat (once per profile).
/// </summary>
public class CinematicPresentationController : MonoBehaviour
{
	public const string IntroLedgePresentationId = "intro_ledge_cinematic";

	const float DefaultBaseFov = 60f;
	const float DefaultBarPeakHeight = 0.12f;
	const float LookSensitivityDampened = 0.25f;

	static readonly Dictionary<string, CinematicPresentationController> Controllers = new Dictionary<string, CinematicPresentationController>();

	[SerializeField]
	string _presentationId = IntroLedgePresentationId;

	[SerializeField]
	float _baseFov = DefaultBaseFov;

	[SerializeField]
	[Tooltip( "World-space camera pose to blend toward during the presentation. Leave empty to skip pose blend." )]
	Transform _cameraPoseTarget;

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
	[Tooltip( "Envelope for blending camera position/rotation toward the pose target and back. Peak is unused (weight only)." )]
	RevealPunchChannel _cameraPoseChannel = new RevealPunchChannel
	{
		rise = 0.5f,
		hold = 4f,
		fall = 1.2f,
		peak = 1f
	};

	Coroutine _presentationRoutine;
	bool _poseBlendActive;
	float _restorePitch;

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
		bool blendPose = _cameraPoseTarget != null;

		float duration = Mathf.Max( fovChannel.TotalDuration, letterboxChannel.TotalDuration, blendPose ? poseChannel.TotalDuration : 0f );
		float elapsed = 0f;

		LockPlayerMovementForCinematic( true );

		CinematicLetterboxUI letterbox = CinematicLetterboxUI.EnsureExists();
		FirstPersonCameraController firstPerson = ResolveFirstPersonCamera();
		CameraController cameraRig = ResolveCameraRig();
		CrosshairUI crosshair = CrosshairUI.Instance;

		if ( firstPerson != null && firstPerson.Camera != null && _baseFov <= 0f )
			_baseFov = firstPerson.Camera.fieldOfView;

		float baseFov = _baseFov > 0f ? _baseFov : DefaultBaseFov;

		Vector3 startCameraWorldPos = Vector3.zero;
		Quaternion startCameraWorldRot = Quaternion.identity;
		Vector3 targetCameraWorldPos = Vector3.zero;
		Quaternion targetCameraWorldRot = Quaternion.identity;
		if ( blendPose && firstPerson != null && cameraRig != null )
		{
			_restorePitch = firstPerson.Pitch;
			startCameraWorldPos = cameraRig.transform.position;
			startCameraWorldRot = ResolveCameraWorldRotation( cameraRig, firstPerson );
			targetCameraWorldPos = _cameraPoseTarget.position;
			targetCameraWorldRot = _cameraPoseTarget.rotation;
			firstPerson.BeginCinematicDetachedLook();
			_poseBlendActive = true;
		}

		while ( elapsed < duration )
		{
			elapsed += Time.deltaTime;

			float fovWeight = fovChannel.EvaluateWeight( elapsed );
			float letterboxWeight = letterboxChannel.EvaluateWeight( elapsed );

			if ( firstPerson != null )
			{
				firstPerson.SetFieldOfView( Mathf.Lerp( baseFov, fovChannel.peak, fovWeight ) );
				firstPerson.SetLookSensitivityMultiplier( Mathf.Lerp( 1f, LookSensitivityDampened, letterboxWeight ) );
			}

			if ( letterbox != null )
			{
				float letterboxHeight = letterboxWeight * letterboxChannel.peak;
				letterbox.SetLetterboxAmount( letterboxHeight );
			}

			if ( crosshair != null )
				crosshair.SetAlpha( 1f - letterboxWeight );

			if ( blendPose && _poseBlendActive && firstPerson != null && cameraRig != null )
				ApplyCameraPoseBlend( cameraRig, firstPerson, startCameraWorldPos, startCameraWorldRot, targetCameraWorldPos, targetCameraWorldRot, poseChannel.EvaluateWeight( elapsed ) );

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
		{
			firstPerson.SetFieldOfView( Mathf.Lerp( baseFov, fovChannel.peak, fovWeight ) );
			firstPerson.SetLookSensitivityMultiplier( Mathf.Lerp( 1f, LookSensitivityDampened, letterboxWeight ) );
		}

		if ( letterbox != null )
			letterbox.SetLetterboxAmount( letterboxWeight * letterboxChannel.peak );

		if ( crosshair != null )
			crosshair.SetAlpha( 1f - letterboxWeight );

		if ( blendPose && firstPerson != null && cameraRig != null )
			ApplyCameraPoseBlend( cameraRig, firstPerson, startCameraWorldPos, startCameraWorldRot, targetCameraWorldPos, targetCameraWorldRot, poseChannel.EvaluateWeight( elapsed ) );
	}

	static void ApplyCameraPoseBlend(
		CameraController cameraRig,
		FirstPersonCameraController firstPerson,
		Vector3 startPos,
		Quaternion startRot,
		Vector3 targetPos,
		Quaternion targetRot,
		float poseWeight )
	{
		Vector3 pos = Vector3.Lerp( startPos, targetPos, poseWeight );
		Quaternion rot = Quaternion.Slerp( startRot, targetRot, poseWeight );

		// Free-look offsets fade with the pose weight so return settles on the original view.
		float yawOffset = firstPerson.CinematicYawOffset * poseWeight;
		float pitchOffset = firstPerson.CinematicPitchOffset * poseWeight;
		rot = Quaternion.AngleAxis( yawOffset, Vector3.up ) * rot;
		rot = rot * Quaternion.AngleAxis( pitchOffset, Vector3.right );

		// Parent carries world pose; child pitch stays identity while detached.
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

	void ResetPresentationState()
	{
		LockPlayerMovementForCinematic( false );

		FirstPersonCameraController firstPerson = ResolveFirstPersonCamera();
		if ( firstPerson != null )
		{
			if ( _poseBlendActive )
				firstPerson.EndCinematicDetachedLook( _restorePitch );

			firstPerson.ResetFieldOfView();
			firstPerson.ResetLookSensitivityMultiplier();
		}

		_poseBlendActive = false;

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

	static void LockPlayerMovementForCinematic( bool locked )
	{
		if ( GameMode.Instance == null || GameMode.Instance.Player == null )
			return;

		GameMode.Instance.Player.SetCinematicPlanarMovementLock( locked );
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
		if ( _cameraPoseTarget == null )
			return;

		Gizmos.color = new Color( 0.3f, 0.85f, 1f, 0.9f );
		Gizmos.DrawWireSphere( _cameraPoseTarget.position, 0.12f );
		Gizmos.DrawLine( _cameraPoseTarget.position, _cameraPoseTarget.position + _cameraPoseTarget.forward * 0.75f );
	}
#endif
}
