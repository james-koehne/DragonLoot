using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Splines;

public enum CinematicCueType
{
	PlayAudio = 0,
	LanternReveal = 1
}

public enum CinematicPresentationPhase
{
	Idle = 0,
	Walk = 1,
	SplineEnter = 2,
	Tour = 3,
	Reattach = 4
}

[Serializable]
public class CinematicCue
{
	[Min( 0f )]
	[Tooltip( "Seconds after cinematic start before this cue fires." )]
	public float delay;

	public CinematicCueType type;

	[Tooltip( "Clip when type is PlayAudio." )]
	public AudioClip audioClip;

	[Range( 0f, 1f )]
	public float audioVolume = 1f;

	[Tooltip( "Reveal id when type is LanternReveal. Matches LanternRevealSweepController." )]
	public string lanternRevealId;
}

/// <summary>
/// Intro ledge cinematic: detach camera, lag-walk the player to a ledge, tour an open camera
/// spline (look path ahead), then return to the player while looking at the dragon.
/// Letterbox, FOV punch, input lock, HUD hide, timed cues, and <see cref="CinematicPresentationEndedEvent"/> remain.
///
/// Scene setup (manual in Editor):
/// 1. Add <see cref="CinematicPresentationController"/> to the level.
/// 2. Set presentation id to <see cref="IntroLedgePresentationId"/>.
/// 3. Assign <see cref="_gnomeLedgeTarget"/> and <see cref="_returnLookTarget"/> (dragon).
/// 4. Dragon Loot → Cinematic → Create Intro Splines, draw open camera + look rails (not a loop).
/// 5. Shape <see cref="_tourProgressCurve"/> for fine speed control (steep = fast, flat = slow).
/// 6. Assign <see cref="_lanternReveal"/> and author sweep / skylight / punches on this inspector.
/// 7. Author timed cues (lantern reveal, audio) here — not on the world event.
///
/// Testing:
/// 1. Play Mode → Debug overlay → World Events → Fly Camera / Ledge.
/// 2. Camera tours to path end, then returns to the player looking at the dragon.
/// </summary>
public class CinematicPresentationController : MonoBehaviour
{
	public const string IntroLedgePresentationId = "intro_ledge_cinematic";
	public const string IntroCameraPathName = "IntroCameraPath";
	public const string IntroLookPathName = "IntroLookPath";

	const float DefaultBaseFov = 60f;
	const float DefaultBarPeakHeight = 0.12f;
	const int LookGizmoSegments = 24;

	static readonly Dictionary<string, CinematicPresentationController> Controllers = new Dictionary<string, CinematicPresentationController>();

	[SerializeField]
	string _presentationId = IntroLedgePresentationId;

	[SerializeField]
	float _baseFov = DefaultBaseFov;

	[FormerlySerializedAs( "_revealCameraTarget" )]
	[FormerlySerializedAs( "_cameraPoseTarget" )]
	[SerializeField]
	[Tooltip( "World pose the gnome walks to. Player stays here when the cinematic ends." )]
	Transform _gnomeLedgeTarget;

	[SerializeField]
	[Tooltip( "Look-at target while returning to the player after the tour (e.g. dragon)." )]
	Transform _returnLookTarget;

	[Header( "UI Envelope" )]
	[SerializeField]
	[Min( 0f )]
	float _envelopeRise = 0.5f;

	[SerializeField]
	[Min( 0f )]
	float _envelopeHold = 4f;

	[SerializeField]
	[Min( 0f )]
	float _envelopeFall = 1.2f;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Peak FOV during the cinematic." )]
	float _fovPeak = 80f;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Letterbox bar height (0-1 screen fraction)." )]
	float _letterboxPeak = DefaultBarPeakHeight;

	[Header( "Walk phase" )]
	[SerializeField]
	[Min( 0.1f )]
	[Tooltip( "Player walk speed toward the ledge (m/s)." )]
	float _gnomeWalkSpeed = 6f;

	[SerializeField]
	[Min( 0.1f )]
	[Tooltip( "Detached camera speed toward the ledge (m/s). Keep below gnome speed so the gnome pulls ahead." )]
	float _cameraWalkSpeed = 3.5f;

	[SerializeField]
	[Min( 0.05f )]
	float _arriveRadius = 0.2f;

	[SerializeField]
	[Tooltip( "World Y for the lagging camera during walk. 0 = keep eye height from detach." )]
	float _walkCameraHeight;

	[Header( "Spline tour" )]
	[SerializeField]
	SplineContainer _cameraPath;

	[SerializeField]
	SplineContainer _lookPath;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Seconds to blend from walk camera speed onto the tour path with matched velocities." )]
	float _splineEnterDuration = 1.2f;

	[SerializeField]
	[Min( 0.1f )]
	[Tooltip( "Total seconds to travel camera path from start to end (open path, not a loop)." )]
	float _tourDuration = 10f;

	[FormerlySerializedAs( "_tourEase" )]
	[SerializeField]
	[Tooltip( "Normalized time (0-1) → normalized path position (0-1). Steep = fast, flat = slow. Add keys for fine control." )]
	AnimationCurve _tourProgressCurve = AnimationCurve.Linear( 0f, 0f, 1f, 1f );

	[SerializeField]
	[Range( 0f, 1f )]
	[Tooltip( "Look samples this much further along the look spline than the camera's normalized t." )]
	float _lookAheadNormalized = 0.1f;

	[Header( "Return to player" )]
	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Seconds to move from path end back to CameraMount while looking at the return target." )]
	float _reattachDuration = 1.2f;

	[Header( "Lantern Reveal" )]
	[SerializeField]
	[Tooltip( "Linked sweep controller authored from this cinematic. Cue LanternReveal starts this (or matches by reveal id)." )]
	LanternRevealSweepController _lanternReveal;

	[Header( "Cues" )]
	[SerializeField]
	[Tooltip( "Timed beats fired from cinematic start (lantern reveal, audio). Authored here, not on the world event." )]
	CinematicCue[] _cues =
	{
		new CinematicCue
		{
			delay = 0f,
			type = CinematicCueType.LanternReveal,
			lanternRevealId = LanternActivator.IntroLedgeRevealId,
			audioVolume = 1f
		},
		new CinematicCue
		{
			delay = 2f,
			type = CinematicCueType.PlayAudio,
			audioVolume = 1f
		}
	};

	Coroutine _presentationRoutine;
	bool _cameraDetached;
	bool _detachedLookActive;
	float _restorePitch;
	float _endPitch;
	bool _placePlayerAtLedge;
	Vector3 _playerEndPos;
	Quaternion _playerEndRot;
	Vector3 _walkExitCameraVelocity;
	CinematicPresentationPhase _phase = CinematicPresentationPhase.Idle;
	float _elapsed;
	bool[] _cueFired;

#if UNITY_EDITOR
	[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.SubsystemRegistration )]
	static void ResetStatics()
	{
		Controllers.Clear();
	}
#endif

	public string PresentationId => _presentationId;

	public SplineContainer CameraPath => _cameraPath;

	public SplineContainer LookPath => _lookPath;

	public Transform GnomeLedgeTarget => _gnomeLedgeTarget;

	public Transform ReturnLookTarget => _returnLookTarget;

	public float EnvelopeRise => _envelopeRise;

	public float EnvelopeHold => _envelopeHold;

	public float EnvelopeFall => _envelopeFall;

	public float FovPeak => _fovPeak;

	public float LetterboxPeak => _letterboxPeak;

	public float GnomeWalkSpeed => _gnomeWalkSpeed;

	public float CameraWalkSpeed => _cameraWalkSpeed;

	public float SplineEnterDuration => _splineEnterDuration;

	public float TourDuration => _tourDuration;

	public float ReattachDuration => _reattachDuration;

	public LanternRevealSweepController LanternReveal => _lanternReveal;

	public CinematicCue[] Cues => _cues;

	public CinematicPresentationPhase Phase => _phase;

	public float Elapsed => _elapsed;

	public bool IsPlaying => _presentationRoutine != null;

	public static bool IsAnyPlaying
	{
		get
		{
			foreach ( KeyValuePair<string, CinematicPresentationController> pair in Controllers )
			{
				if ( pair.Value != null && pair.Value.IsPlaying )
					return true;
			}

			return false;
		}
	}

	public float EstimateWalkDurationForEditor()
	{
		PlayerController player = ResolvePlayer();
		return EstimateWalkDuration( player );
	}

	public float EstimateContentDurationForEditor()
	{
		float walk = EstimateWalkDurationForEditor();
		float tourPart = HasUsableSpline( _cameraPath ) ? _splineEnterDuration + _tourDuration : 0f;
		float cameraContent = walk + tourPart + _reattachDuration;
		LanternRevealSweepController lantern = ResolveLanternReveal();
		float lanternCueDelay = ResolveFirstLanternCueDelay();
		float lanternContent = lantern != null
			? lanternCueDelay + lantern.EstimateRevealDuration()
			: 0f;
		return Mathf.Max( cameraContent, lanternContent );
	}

	public float ResolveFirstLanternCueDelay()
	{
		if ( _cues == null )
			return 0f;

		float delay = float.PositiveInfinity;
		for ( int i = 0; i < _cues.Length; i++ )
		{
			CinematicCue cue = _cues[ i ];
			if ( cue == null || cue.type != CinematicCueType.LanternReveal )
				continue;
			if ( cue.delay < delay )
				delay = cue.delay;
		}

		return float.IsPositiveInfinity( delay ) ? 0f : delay;
	}

	public LanternRevealSweepController ResolveLanternReveal()
	{
		if ( _lanternReveal != null )
			return _lanternReveal;

		string revealId = null;
		if ( _cues != null )
		{
			for ( int i = 0; i < _cues.Length; i++ )
			{
				CinematicCue cue = _cues[ i ];
				if ( cue == null || cue.type != CinematicCueType.LanternReveal )
					continue;
				if ( !string.IsNullOrEmpty( cue.lanternRevealId ) )
				{
					revealId = cue.lanternRevealId;
					break;
				}
			}
		}

		if ( string.IsNullOrEmpty( revealId ) )
			revealId = LanternActivator.IntroLedgeRevealId;

		LanternRevealSweepController found;
		if ( LanternRevealSweepController.TryFindInOpenScenes( revealId, out found ) )
			return found;

		return null;
	}

	public void EditorAssignPaths( SplineContainer cameraPath, SplineContainer lookPath )
	{
		if ( cameraPath != null )
			_cameraPath = cameraPath;
		if ( lookPath != null )
			_lookPath = lookPath;
	}

	public void EditorAssignLanternReveal( LanternRevealSweepController lantern )
	{
		_lanternReveal = lantern;
	}

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

	public static bool TryPlay( string presentationId )
	{
		if ( string.IsNullOrEmpty( presentationId ) )
			return false;

		CinematicPresentationController controller;
		if ( !TryGet( presentationId, out controller ) )
		{
			Debug.LogWarning( "CinematicPresentationController: no controller registered for presentation id '" + presentationId + "'." );
			return false;
		}

		controller.StartPresentation();
		return true;
	}

	public static bool TryGet( string presentationId, out CinematicPresentationController controller )
	{
		controller = null;
		if ( string.IsNullOrEmpty( presentationId ) )
			return false;

		if ( !Controllers.TryGetValue( presentationId, out controller ) || controller == null )
		{
			controller = null;
			return false;
		}

		return true;
	}

	/// <summary>
	/// Stops a playing presentation, restores HUD/camera/input, and leaves the player where they are.
	/// Used by debug skip-intro / teleports so pouches and look are post-intro.
	/// </summary>
	public static void DebugAbortPlayingKeepPlayer()
	{
		foreach ( KeyValuePair<string, CinematicPresentationController> pair in Controllers )
		{
			CinematicPresentationController controller = pair.Value;
			if ( controller == null || !controller.IsPlaying )
				continue;
			controller.AbortPlayingKeepPlayer();
		}
	}

	void AbortPlayingKeepPlayer()
	{
		if ( _presentationRoutine != null )
		{
			StopCoroutine( _presentationRoutine );
			_presentationRoutine = null;
		}

		_placePlayerAtLedge = false;
		ResetPresentationState();
	}

	public void StartPresentation()
	{
		if ( _presentationRoutine != null )
		{
			StopCoroutine( _presentationRoutine );
			_presentationRoutine = null;
			ResetPresentationState();
		}

		EnsureTourProgressCurve();
		_walkExitCameraVelocity = Vector3.zero;
		_presentationRoutine = StartCoroutine( PresentationRoutine() );
	}

	void EnsureTourProgressCurve()
	{
		if ( _tourProgressCurve != null && _tourProgressCurve.length > 0 )
			return;

		_tourProgressCurve = AnimationCurve.Linear( 0f, 0f, 1f, 1f );
	}

	IEnumerator PresentationRoutine()
	{
		RevealPunchChannel fovChannel = BuildFovChannel();
		RevealPunchChannel letterboxChannel = BuildLetterboxChannel();

		LockPlayerInputForCinematic( true );
		SetTutorialObjectiveHudHidden( true );

		CinematicLetterboxUI letterbox = CinematicLetterboxUI.EnsureExists();
		FirstPersonCameraController firstPerson = ResolveFirstPersonCamera();
		CameraController cameraRig = ResolveCameraRig();
		CrosshairUI crosshair = CrosshairUI.Instance;
		PlayerController player = ResolvePlayer();
		PlayerCarry carry = player != null ? player.Carry : null;

		if ( firstPerson != null )
			firstPerson.SetFieldOfViewOverride( true );

		if ( firstPerson != null && firstPerson.Camera != null && _baseFov <= 0f )
			_baseFov = firstPerson.Camera.fieldOfView;

		float baseFov = _baseFov > 0f ? _baseFov : DefaultBaseFov;

		_placePlayerAtLedge = false;
		_endPitch = 0f;
		_restorePitch = firstPerson != null ? firstPerson.Pitch : 0f;
		_elapsed = 0f;
		_phase = CinematicPresentationPhase.Idle;
		PrepareCues();

		bool hasLedge = _gnomeLedgeTarget != null && player != null && cameraRig != null && firstPerson != null;
		if ( !hasLedge && _presentationId == IntroLedgePresentationId )
			Debug.LogWarning( "CinematicPresentationController: missing gnome ledge target, player, or camera — envelope only." );

		if ( hasLedge )
		{
			if ( carry != null )
				carry.SetCinematicHidden( true );

			_playerEndPos = _gnomeLedgeTarget.position;
			_playerEndRot = FlattenYaw( _gnomeLedgeTarget.rotation );
			_endPitch = NormalizePitch( _gnomeLedgeTarget.rotation.eulerAngles.x );
			_placePlayerAtLedge = true;

			DetachCamera( cameraRig );
			firstPerson.BeginCinematicDetachedLook();
			_detachedLookActive = true;
		}

		if ( hasLedge )
		{
			float estimatedWalk = EstimateWalkDuration( player );
			float tourPart = HasUsableSpline( _cameraPath ) ? _splineEnterDuration + _tourDuration : 0f;
			float contentDuration = estimatedWalk + tourPart + _reattachDuration;
			ExtendHoldForContent( ref fovChannel, contentDuration );
			ExtendHoldForContent( ref letterboxChannel, contentDuration );

			_phase = CinematicPresentationPhase.Walk;
			yield return WalkPhase(
				player,
				cameraRig,
				firstPerson,
				fovChannel,
				letterboxChannel,
				letterbox,
				crosshair,
				baseFov );

			if ( HasUsableSpline( _cameraPath ) )
			{
				yield return SplineTourPhase(
					cameraRig,
					firstPerson,
					fovChannel,
					letterboxChannel,
					letterbox,
					crosshair,
					baseFov );
			}
			else if ( _cameraPath == null )
				Debug.LogWarning( "CinematicPresentationController: no camera spline assigned — skipping tour." );
			else
				Debug.LogWarning( "CinematicPresentationController: camera spline is empty — skipping tour." );

			_phase = CinematicPresentationPhase.Reattach;
			yield return ReattachPhase(
				player,
				cameraRig,
				firstPerson,
				fovChannel,
				letterboxChannel,
				letterbox,
				crosshair,
				baseFov );
		}
		else
		{
			float duration = Mathf.Max( fovChannel.TotalDuration, letterboxChannel.TotalDuration );
			while ( _elapsed < duration )
			{
				float dt = Time.deltaTime;
				_elapsed += dt;
				TickCues();
				ApplyUiEnvelope( fovChannel, letterboxChannel, letterbox, firstPerson, crosshair, baseFov, _elapsed );
				yield return null;
			}
		}

		ApplyUiEnvelope( fovChannel, letterboxChannel, letterbox, firstPerson, crosshair, baseFov, _elapsed );
		ResetPresentationState();
		_presentationRoutine = null;
		EventBus.Publish( new CinematicPresentationEndedEvent { PresentationId = _presentationId } );
	}

	void PrepareCues()
	{
		if ( _cues == null || _cues.Length == 0 )
		{
			_cueFired = Array.Empty<bool>();
			return;
		}

		_cueFired = new bool[ _cues.Length ];
	}

	void TickCues()
	{
		if ( _cues == null || _cueFired == null )
			return;

		for ( int i = 0; i < _cues.Length; i++ )
		{
			if ( _cueFired[ i ] )
				continue;

			CinematicCue cue = _cues[ i ];
			if ( cue == null )
			{
				_cueFired[ i ] = true;
				continue;
			}

			if ( _elapsed + 0.0001f < cue.delay )
				continue;

			FireCue( cue );
			_cueFired[ i ] = true;
		}
	}

	void FireCue( CinematicCue cue )
	{
		if ( cue == null )
			return;

		switch ( cue.type )
		{
			case CinematicCueType.PlayAudio:
				WorldEventAudioPlayer.PlayClipAtPlayer( cue.audioClip, cue.audioVolume );
				break;
			case CinematicCueType.LanternReveal:
				FireLanternRevealCue( cue );
				break;
		}
	}

	void FireLanternRevealCue( CinematicCue cue )
	{
		if ( _lanternReveal != null )
		{
			if ( string.IsNullOrEmpty( cue.lanternRevealId ) ||
			     cue.lanternRevealId == _lanternReveal.RevealId )
			{
				_lanternReveal.StartReveal( default );
				return;
			}
		}

		if ( !string.IsNullOrEmpty( cue.lanternRevealId ) )
			LanternRevealSweepController.TryStartReveal( cue.lanternRevealId );
	}

	RevealPunchChannel BuildFovChannel()
	{
		return new RevealPunchChannel
		{
			rise = Mathf.Max( 0f, _envelopeRise ),
			hold = Mathf.Max( 0f, _envelopeHold ),
			fall = Mathf.Max( 0f, _envelopeFall ),
			peak = Mathf.Max( 0f, _fovPeak )
		};
	}

	RevealPunchChannel BuildLetterboxChannel()
	{
		return new RevealPunchChannel
		{
			rise = Mathf.Max( 0f, _envelopeRise ),
			hold = Mathf.Max( 0f, _envelopeHold ),
			fall = Mathf.Max( 0f, _envelopeFall ),
			peak = Mathf.Max( 0f, _letterboxPeak )
		};
	}

	IEnumerator WalkPhase(
		PlayerController player,
		CameraController cameraRig,
		FirstPersonCameraController firstPerson,
		RevealPunchChannel fovChannel,
		RevealPunchChannel letterboxChannel,
		CinematicLetterboxUI letterbox,
		CrosshairUI crosshair,
		float baseFov )
	{
		float cameraY = _walkCameraHeight > 0.0001f ? _walkCameraHeight : cameraRig.transform.position.y;
		float gnomeSpeed = Mathf.Max( 0.1f, _gnomeWalkSpeed );
		float cameraSpeed = Mathf.Max( 0.1f, _cameraWalkSpeed );
		Quaternion walkLookRot = FlattenYaw( ResolveCameraWorldRotation( cameraRig, firstPerson ) );
		_walkExitCameraVelocity = Vector3.zero;

		while ( true )
		{
			float dt = Time.deltaTime;
			_elapsed += dt;
			TickCues();

			Vector3 playerPos = player.transform.position;
			Vector3 ledgePos = _playerEndPos;
			Vector3 toLedge = ledgePos - playerPos;
			toLedge.y = 0f;
			float planarDist = toLedge.magnitude;
			if ( planarDist <= _arriveRadius )
			{
				player.SnapToWorldPose( _playerEndPos, _playerEndRot );
				player.NotifyCinematicTravel( Vector3.zero, dt );
				ApplyCameraPose( cameraRig, firstPerson, FlattenCameraY( cameraRig.transform.position, cameraY ), walkLookRot );
				ApplyUiEnvelope( fovChannel, letterboxChannel, letterbox, firstPerson, crosshair, baseFov, _elapsed );
				yield return null;
				yield break;
			}

			Vector3 walkDir = toLedge / planarDist;
			float step = Mathf.Min( planarDist, gnomeSpeed * dt );
			Vector3 nextPlayerPos = playerPos + walkDir * step;
			nextPlayerPos.y = Mathf.Lerp( playerPos.y, ledgePos.y, Mathf.Clamp01( step / Mathf.Max( planarDist, 0.0001f ) ) );
			Quaternion nextPlayerRot = Quaternion.LookRotation( walkDir, Vector3.up );
			Vector3 travelDelta = nextPlayerPos - playerPos;
			player.SnapToWorldPose( nextPlayerPos, nextPlayerRot );
			player.NotifyCinematicTravel( travelDelta, dt );

			Vector3 camPos = cameraRig.transform.position;
			Vector3 camTarget = new Vector3( ledgePos.x, cameraY, ledgePos.z );
			Vector3 camDelta = camTarget - camPos;
			float camDist = camDelta.magnitude;
			Vector3 camTravel = Vector3.zero;
			if ( camDist > 0.0001f )
			{
				float camStep = Mathf.Min( camDist, cameraSpeed * dt );
				camTravel = camDelta / camDist * camStep;
				camPos += camTravel;
			}

			camPos.y = cameraY;
			_walkExitCameraVelocity = dt > 0.0001f ? camTravel / dt : Vector3.zero;
			if ( _walkExitCameraVelocity.sqrMagnitude < 0.0001f && camDist > 0.0001f )
				_walkExitCameraVelocity = camDelta.normalized * cameraSpeed;

			ApplyCameraPose( cameraRig, firstPerson, camPos, walkLookRot );
			ApplyUiEnvelope( fovChannel, letterboxChannel, letterbox, firstPerson, crosshair, baseFov, _elapsed );
			yield return null;
		}
	}

	IEnumerator SplineTourPhase(
		CameraController cameraRig,
		FirstPersonCameraController firstPerson,
		RevealPunchChannel fovChannel,
		RevealPunchChannel letterboxChannel,
		CinematicLetterboxUI letterbox,
		CrosshairUI crosshair,
		float baseFov )
	{
		Vector3 enterStartPos = cameraRig.transform.position;
		Quaternion enterStartRot = ResolveCameraWorldRotation( cameraRig, firstPerson );

		Vector3 splineStartPos;
		Vector3 splineStartTangent;
		Vector3 splineStartUp;
		if ( !CinematicSplineLook.TryEvaluate( _cameraPath, 0f, out splineStartPos, out splineStartTangent, out splineStartUp ) )
			yield break;

		Vector3 lookStart;
		if ( !TryResolveLookPoint( splineStartPos, ResolveLookAheadT( 0f ), out lookStart ) )
			lookStart = splineStartPos + splineStartTangent;
		Quaternion splineStartRot = CinematicSplineLook.LookRotation( splineStartPos, lookStart, Vector3.up );

		float tourDuration = Mathf.Max( 0.1f, _tourDuration );
		float pathLength = Mathf.Max( 0.01f, _cameraPath.CalculateLength() );
		float tourStartSpeed = EstimateTourSpeedAlongPath( 0f, pathLength, tourDuration );

		Vector3 startVel = _walkExitCameraVelocity;
		if ( startVel.sqrMagnitude < 0.0001f )
		{
			Vector3 toStart = splineStartPos - enterStartPos;
			startVel = toStart.sqrMagnitude > 0.0001f
				? toStart.normalized * _cameraWalkSpeed
				: splineStartTangent * _cameraWalkSpeed;
		}

		Vector3 endVel = splineStartTangent.sqrMagnitude > 0.0001f
			? splineStartTangent.normalized * tourStartSpeed
			: startVel.normalized * tourStartSpeed;

		float enterDuration = Mathf.Max( 0f, _splineEnterDuration );
		float enterDist = Vector3.Distance( enterStartPos, splineStartPos );
		if ( enterDist < 0.05f )
			enterDuration = 0f;

		if ( enterDuration > 0.0001f )
		{
			_phase = CinematicPresentationPhase.SplineEnter;
			float avgSpeed = 0.5f * ( startVel.magnitude + endVel.magnitude );
			float naturalDist = Mathf.Max( 0.01f, avgSpeed * enterDuration );
			float tangentScale = Mathf.Clamp( enterDist / naturalDist, 0.15f, 1.5f );
			Vector3 m0 = startVel * enterDuration * tangentScale;
			Vector3 m1 = endVel * enterDuration * tangentScale;

			float enterElapsed = 0f;
			while ( enterElapsed < enterDuration )
			{
				float dt = Time.deltaTime;
				enterElapsed += dt;
				_elapsed += dt;
				TickCues();
				float u = Mathf.Clamp01( enterElapsed / enterDuration );
				Vector3 pos = EvaluateHermite( enterStartPos, m0, splineStartPos, m1, u );
				Vector3 lookPoint;
				if ( !TryResolveLookPoint( pos, ResolveLookAheadT( 0f ), out lookPoint ) )
					lookPoint = lookStart;
				Quaternion targetRot = CinematicSplineLook.LookRotation( pos, lookPoint, Vector3.up );
				Quaternion rot = Quaternion.Slerp( enterStartRot, targetRot, u );
				ApplyCameraPose( cameraRig, firstPerson, pos, rot );
				ApplyUiEnvelope( fovChannel, letterboxChannel, letterbox, firstPerson, crosshair, baseFov, _elapsed );
				yield return null;
			}
		}

		ApplyCameraPose( cameraRig, firstPerson, splineStartPos, splineStartRot );

		_phase = CinematicPresentationPhase.Tour;
		float tourElapsed = 0f;
		while ( tourElapsed < tourDuration )
		{
			float dt = Time.deltaTime;
			tourElapsed += dt;
			_elapsed += dt;
			TickCues();
			float rawT = Mathf.Clamp01( tourElapsed / tourDuration );
			float t = EvaluateTourProgress( rawT );

			Vector3 camPos;
			if ( !CinematicSplineLook.TryEvaluatePosition( _cameraPath, t, out camPos ) )
				break;

			Vector3 lookPoint;
			if ( !TryResolveLookPoint( camPos, ResolveLookAheadT( t ), out lookPoint ) )
				lookPoint = camPos + cameraRig.transform.forward;

			Quaternion camRot = CinematicSplineLook.LookRotation( camPos, lookPoint, Vector3.up );
			ApplyCameraPose( cameraRig, firstPerson, camPos, camRot );
			ApplyUiEnvelope( fovChannel, letterboxChannel, letterbox, firstPerson, crosshair, baseFov, _elapsed );
			yield return null;
		}

		Vector3 endPos;
		if ( CinematicSplineLook.TryEvaluatePosition( _cameraPath, 1f, out endPos ) )
		{
			Vector3 endLook;
			if ( !TryResolveLookPoint( endPos, 1f, out endLook ) )
				endLook = endPos + cameraRig.transform.forward;
			ApplyCameraPose( cameraRig, firstPerson, endPos, CinematicSplineLook.LookRotation( endPos, endLook, Vector3.up ) );
		}
	}

	float EstimateTourSpeedAlongPath( float normalizedTime, float pathLength, float tourDuration )
	{
		const float SampleEps = 0.02f;
		float t0 = EvaluateTourProgress( normalizedTime );
		float t1 = EvaluateTourProgress( Mathf.Min( 1f, normalizedTime + SampleEps ) );
		float progressPerNormTime = Mathf.Abs( t1 - t0 ) / SampleEps;
		return progressPerNormTime * pathLength / Mathf.Max( 0.1f, tourDuration );
	}

	static Vector3 EvaluateHermite( Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1, float u )
	{
		float u2 = u * u;
		float u3 = u2 * u;
		return ( 2f * u3 - 3f * u2 + 1f ) * p0
			+ ( u3 - 2f * u2 + u ) * m0
			+ ( -2f * u3 + 3f * u2 ) * p1
			+ ( u3 - u2 ) * m1;
	}

	IEnumerator ReattachPhase(
		PlayerController player,
		CameraController cameraRig,
		FirstPersonCameraController firstPerson,
		RevealPunchChannel fovChannel,
		RevealPunchChannel letterboxChannel,
		CinematicLetterboxUI letterbox,
		CrosshairUI crosshair,
		float baseFov )
	{
		if ( _placePlayerAtLedge )
			player.SnapToWorldPose( _playerEndPos, _playerEndRot );

		Transform mount = player.CameraMount;
		if ( mount == null )
			yield break;

		Vector3 startPos = cameraRig.transform.position;
		float duration = Mathf.Max( 0f, _reattachDuration );
		float localElapsed = 0f;

		while ( localElapsed < duration )
		{
			float dt = Time.deltaTime;
			localElapsed += dt;
			_elapsed += dt;
			TickCues();

			Vector3 targetPos = mount.position;
			float w = duration > 0.0001f ? Mathf.SmoothStep( 0f, 1f, Mathf.Clamp01( localElapsed / duration ) ) : 1f;
			Vector3 pos = Vector3.Lerp( startPos, targetPos, w );
			Quaternion rot = ResolveReturnLookRotation( pos, mount );
			ApplyCameraPose( cameraRig, firstPerson, pos, rot );
			ApplyUiEnvelope( fovChannel, letterboxChannel, letterbox, firstPerson, crosshair, baseFov, _elapsed );
			yield return null;
		}

		Quaternion finalRot = ResolveReturnLookRotation( mount.position, mount );
		ApplyCameraPose( cameraRig, firstPerson, mount.position, finalRot );
		_endPitch = NormalizePitch( finalRot.eulerAngles.x );
		if ( _returnLookTarget != null )
		{
			Vector3 toDragon = _returnLookTarget.position - mount.position;
			toDragon.y = 0f;
			if ( toDragon.sqrMagnitude > 0.0001f )
			{
				_playerEndRot = Quaternion.LookRotation( toDragon.normalized, Vector3.up );
				player.SnapToWorldPose( _playerEndPos, _playerEndRot );
			}
		}

		ReparentCameraToMount( cameraRig, player );
	}

	Quaternion ResolveReturnLookRotation( Vector3 cameraPosition, Transform mount )
	{
		if ( _returnLookTarget != null )
			return CinematicSplineLook.LookRotation( cameraPosition, _returnLookTarget.position, Vector3.up );

		if ( mount != null )
			return mount.rotation;

		return Quaternion.identity;
	}

	float EvaluateTourProgress( float normalizedTime )
	{
		float t = _tourProgressCurve != null
			? _tourProgressCurve.Evaluate( Mathf.Clamp01( normalizedTime ) )
			: normalizedTime;
		return Mathf.Clamp01( t );
	}

	float ResolveLookAheadT( float cameraNormalizedT )
	{
		return Mathf.Clamp01( cameraNormalizedT + Mathf.Clamp01( _lookAheadNormalized ) );
	}

	bool TryResolveLookPoint( Vector3 cameraPosition, float lookNormalizedT, out Vector3 lookPoint )
	{
		lookPoint = cameraPosition + Vector3.forward * 10f;

		Vector3 evaluated;
		if ( HasUsableSpline( _lookPath ) && CinematicSplineLook.TryEvaluatePosition( _lookPath, lookNormalizedT, out evaluated ) )
		{
			lookPoint = evaluated;
			return true;
		}

		if ( HasUsableSpline( _cameraPath ) && CinematicSplineLook.TryEvaluatePosition( _cameraPath, lookNormalizedT, out evaluated ) )
		{
			lookPoint = evaluated;
			return true;
		}

		return false;
	}

	float EstimateWalkDuration( PlayerController player )
	{
		if ( player == null || _gnomeLedgeTarget == null )
			return 0f;

		Vector3 delta = _gnomeLedgeTarget.position - player.transform.position;
		delta.y = 0f;
		return delta.magnitude / Mathf.Max( 0.1f, _gnomeWalkSpeed );
	}

	static void ExtendHoldForContent( ref RevealPunchChannel channel, float contentDuration )
	{
		float neededHold = contentDuration - channel.rise - channel.fall;
		if ( neededHold > channel.hold )
			channel.hold = neededHold;
	}

	static void ApplyUiEnvelope(
		RevealPunchChannel fovChannel,
		RevealPunchChannel letterboxChannel,
		CinematicLetterboxUI letterbox,
		FirstPersonCameraController firstPerson,
		CrosshairUI crosshair,
		float baseFov,
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
	}

	void DetachCamera( CameraController cameraRig )
	{
		if ( cameraRig == null || _cameraDetached )
			return;

		cameraRig.transform.SetParent( null, true );
		_cameraDetached = true;

		PlayerController player = ResolvePlayer();
		if ( player != null )
			player.SetCinematicCameraDetached( true );
	}

	void ReparentCameraToMount( CameraController cameraRig, PlayerController player )
	{
		if ( cameraRig == null || player == null || player.CameraMount == null )
			return;

		cameraRig.transform.SetParent( player.CameraMount, false );
		cameraRig.ResetLocalPose();
		_cameraDetached = false;
		player.SetCinematicCameraDetached( false );
	}

	static void ApplyCameraPose(
		CameraController cameraRig,
		FirstPersonCameraController firstPerson,
		Vector3 pos,
		Quaternion rot )
	{
		if ( firstPerson != null )
		{
			firstPerson.transform.localPosition = Vector3.zero;
			firstPerson.transform.localRotation = Quaternion.identity;
		}

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

	static Quaternion FlattenYaw( Quaternion rotation )
	{
		Vector3 euler = rotation.eulerAngles;
		return Quaternion.Euler( 0f, euler.y, 0f );
	}

	static Vector3 FlattenCameraY( Vector3 position, float cameraY )
	{
		position.y = cameraY;
		return position;
	}

	static float NormalizePitch( float eulerX )
	{
		if ( eulerX > 180f )
			eulerX -= 360f;
		return eulerX;
	}

	static bool HasUsableSpline( SplineContainer container )
	{
		return container != null && container.Spline != null && container.Spline.Count >= 2;
	}

	void ResetPresentationState()
	{
		PlayerController player = ResolvePlayer();
		if ( _placePlayerAtLedge && player != null )
			player.SnapToWorldPose( _playerEndPos, _playerEndRot );

		CameraController cameraRig = ResolveCameraRig();
		if ( _cameraDetached && cameraRig != null && player != null )
			ReparentCameraToMount( cameraRig, player );
		else if ( cameraRig != null && !_cameraDetached )
			cameraRig.ResetLocalPose();

		if ( player != null && player.Carry != null )
			player.Carry.SetCinematicHidden( false );

		if ( player != null )
			player.SetCinematicCameraDetached( false );

		LockPlayerInputForCinematic( false );
		SetTutorialObjectiveHudHidden( false );

		FirstPersonCameraController firstPerson = ResolveFirstPersonCamera();
		if ( firstPerson != null )
		{
			if ( _detachedLookActive )
				firstPerson.EndCinematicDetachedLook( _placePlayerAtLedge ? _endPitch : _restorePitch );

			firstPerson.SetFieldOfViewOverride( false );
			firstPerson.ResetFieldOfView();
			firstPerson.ResetLookSensitivityMultiplier();
		}

		_detachedLookActive = false;
		_placePlayerAtLedge = false;
		_cameraDetached = false;
		_phase = CinematicPresentationPhase.Idle;

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

	static void SetTutorialObjectiveHudHidden( bool hidden )
	{
		if ( GameMode.Instance == null || GameMode.Instance.InterfaceController == null )
			return;

		Transform root = GameMode.Instance.InterfaceController.transform;
		QuestObjectiveUI objective = root.GetComponentInChildren<QuestObjectiveUI>( true );
		if ( objective != null )
			objective.SetCinematicHidden( hidden );

		TutorialPopupUI tutorial = root.GetComponentInChildren<TutorialPopupUI>( true );
		if ( tutorial != null )
			tutorial.SetCinematicHidden( hidden );

		QuestCompassUI compass = root.GetComponentInChildren<QuestCompassUI>( true );
		if ( compass != null )
			compass.SetCinematicHidden( hidden );

		QuestWorldMarker marker = root.GetComponentInChildren<QuestWorldMarker>( true );
		if ( marker != null )
			marker.SetCinematicHidden( hidden );

		PouchBarUI pouchBar = root.GetComponentInChildren<PouchBarUI>( true );
		if ( pouchBar != null )
			pouchBar.SetCinematicHidden( hidden );
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
		if ( _gnomeLedgeTarget != null )
		{
			Gizmos.color = new Color( 0.3f, 0.85f, 1f, 0.9f );
			Gizmos.DrawWireSphere( _gnomeLedgeTarget.position, Mathf.Max( 0.12f, _arriveRadius ) );
			Gizmos.DrawLine( _gnomeLedgeTarget.position, _gnomeLedgeTarget.position + _gnomeLedgeTarget.forward * 0.75f );
		}

		if ( _returnLookTarget != null )
		{
			Gizmos.color = new Color( 1f, 0.45f, 0.2f, 0.9f );
			Gizmos.DrawWireSphere( _returnLookTarget.position, 0.35f );
		}

		DrawSplineGizmo( _cameraPath, new Color( 0.4f, 0.9f, 1f, 0.85f ) );
		DrawSplineGizmo( _lookPath, new Color( 1f, 0.75f, 0.25f, 0.85f ) );
	}

	static void DrawSplineGizmo( SplineContainer container, Color color )
	{
		if ( !HasUsableSpline( container ) )
			return;

		Gizmos.color = color;
		Vector3 prev;
		if ( !CinematicSplineLook.TryEvaluatePosition( container, 0f, out prev ) )
			return;

		for ( int i = 1; i <= LookGizmoSegments; i++ )
		{
			float t = i / (float)LookGizmoSegments;
			Vector3 next;
			if ( !CinematicSplineLook.TryEvaluatePosition( container, t, out next ) )
				continue;
			Gizmos.DrawLine( prev, next );
			prev = next;
		}
	}
#endif
}
