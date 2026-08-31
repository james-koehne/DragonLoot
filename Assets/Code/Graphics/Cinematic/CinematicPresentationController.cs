using System.Collections;
using System.Collections.Generic;

using UnityEngine;

public struct CinematicPresentationOverrides
{
	public float FovPeak;
	public float LetterboxPeak;
	public bool OverrideMicroPush;
	public bool EnableMicroPush;
	public float MicroPushDistance;
	public float Rise;
	public float Hold;
	public float Fall;
}

/// <summary>
/// FOV widen, UI letterbox, crosshair fade, look dampening, and optional micro camera push.
/// Scene setup (manual in Editor):
/// 1. Add <see cref="CinematicPresentationController"/> to the level (same object as LanternRevealSweepController is fine).
/// 2. Set presentation id to <see cref="IntroLedgePresentationId"/>.
/// 3. Tune rise/hold/fall and peaks in Inspector (defaults: 0.5s / 4s / 1.2s, FOV 80, letterbox 0.12, push 0.2m).
/// 4. Toggle <c>_enableMicroPush</c> to enable/disable the forward dolly.
///
/// Testing:
/// 1. Play Mode → Debug overlay → World Events → Ledge.
/// 2. Expect bars + FOV widen over ~0.5s; hold ~4s; fall ~1.2s; crosshair fades; look dampened.
/// 3. With micro push on, expect subtle forward camera nudge. Toggle off and re-fire to confirm no push.
/// 4. Walk into volume_hallway_end naturally for the full intro_ledge beat (once per profile).
/// </summary>
public class CinematicPresentationController : MonoBehaviour
{
	public const string IntroLedgePresentationId = "intro_ledge_cinematic";

	const float DefaultBaseFov = 60f;
	const float DefaultBarPeakHeight = 0.12f;
	const float LookSensitivityDampened = 0.35f;

	static readonly Dictionary<string, CinematicPresentationController> Controllers = new Dictionary<string, CinematicPresentationController>();

	[SerializeField]
	string _presentationId = IntroLedgePresentationId;

	[SerializeField]
	float _baseFov = DefaultBaseFov;

	[SerializeField]
	bool _enableMicroPush = true;

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

	[SerializeField]
	RevealPunchChannel _microPushChannel = new RevealPunchChannel
	{
		rise = 0.5f,
		hold = 4f,
		fall = 1.2f,
		peak = 0.2f
	};

	Coroutine _presentationRoutine;

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
		RevealPunchChannel pushChannel = ResolveChannel( _microPushChannel, overrides.Rise, overrides.Hold, overrides.Fall, overrides.MicroPushDistance );
		bool enableMicroPush = ResolveMicroPushEnabled( overrides );

		float duration = Mathf.Max( fovChannel.TotalDuration, letterboxChannel.TotalDuration, enableMicroPush ? pushChannel.TotalDuration : 0f );
		float elapsed = 0f;

		CinematicLetterboxUI letterbox = CinematicLetterboxUI.EnsureExists();
		FirstPersonCameraController firstPerson = ResolveFirstPersonCamera();
		CameraController cameraRig = ResolveCameraRig();
		CrosshairUI crosshair = CrosshairUI.Instance;

		if ( firstPerson != null && firstPerson.Camera != null && _baseFov <= 0f )
			_baseFov = firstPerson.Camera.fieldOfView;

		float baseFov = _baseFov > 0f ? _baseFov : DefaultBaseFov;

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

			if ( enableMicroPush && cameraRig != null )
			{
				float pushWeight = pushChannel.EvaluateWeight( elapsed );
				cameraRig.SetLocalPositionZOffset( pushChannel.peak * pushWeight );
			}

			yield return null;
		}

		ApplyFinalFrame( fovChannel, letterboxChannel, pushChannel, enableMicroPush, baseFov, letterbox, firstPerson, cameraRig, crosshair, duration );
		ResetPresentationState();
		_presentationRoutine = null;
	}

	static void ApplyFinalFrame(
		RevealPunchChannel fovChannel,
		RevealPunchChannel letterboxChannel,
		RevealPunchChannel pushChannel,
		bool enableMicroPush,
		float baseFov,
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

		if ( enableMicroPush && cameraRig != null )
		{
			float pushWeight = pushChannel.EvaluateWeight( elapsed );
			cameraRig.SetLocalPositionZOffset( pushChannel.peak * pushWeight );
		}
	}

	void ResetPresentationState()
	{
		FirstPersonCameraController firstPerson = ResolveFirstPersonCamera();
		if ( firstPerson != null )
		{
			firstPerson.ResetFieldOfView();
			firstPerson.ResetLookSensitivityMultiplier();
		}

		CameraController cameraRig = ResolveCameraRig();
		if ( cameraRig != null )
			cameraRig.ResetLocalPosition();

		CinematicLetterboxUI letterbox = CinematicLetterboxUI.Instance;
		if ( letterbox != null )
			letterbox.SetLetterboxAmount( 0f );

		CrosshairUI crosshair = CrosshairUI.Instance;
		if ( crosshair != null )
			crosshair.SetAlpha( 1f );
	}

	bool ResolveMicroPushEnabled( CinematicPresentationOverrides overrides )
	{
		if ( overrides.OverrideMicroPush )
			return overrides.EnableMicroPush;

		return _enableMicroPush;
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
}
