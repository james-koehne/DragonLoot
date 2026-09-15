using System.Collections.Generic;

using FeedbackSystem;

using UnityEngine;

public enum FloatingPlatformTriggerType
{
	Manual = 0,
	TutorialCompleted = 1,
	WorldEventFired = 2,
	AbilityUnlocked = 3,
	VolumeEntered = 4
}

/// <summary>
/// Scene-authored group of one or more <see cref="FloatingPlatform"/> slabs.
/// Activates from tutorial / progression / volume, rises with stagger + overshoot, then hovers.
/// </summary>
[DisallowMultipleComponent]
public class FloatingPlatformGroup : MonoBehaviour
{
	static readonly List<FloatingPlatformGroup> All = new List<FloatingPlatformGroup>( 8 );

	[SerializeField] string groupId = "floating_platforms";

	[Header( "Platforms" )]
	[SerializeField] FloatingPlatform[] platforms = System.Array.Empty<FloatingPlatform>();

	[Header( "Activation" )]
	[SerializeField] FloatingPlatformTriggerType triggerType = FloatingPlatformTriggerType.Manual;

	[Tooltip( "Tutorial id, world event id, ability id, or volume id depending on trigger type." )]
	[SerializeField] string triggerId;

	[Header( "Rise" )]
	[Tooltip( "Y offset from rest pose while sunk (typically negative)." )]
	[SerializeField] float sunkYOffset = -150f;

	[SerializeField] [Min( 0.05f )] float riseDuration = 1.6f;

	[Tooltip( "Seconds between each platform starting its rise (array order)." )]
	[SerializeField] [Min( 0f )] float staggerDelay = 0.18f;

	[Tooltip( "Normalized rise curve. Ease-out with values above 1 for a slight overshoot, ending at 1." )]
	[SerializeField] AnimationCurve riseCurve = CreateDefaultOvershootCurve();

	[Header( "Hover" )]
	[SerializeField] [Min( 0f )] float hoverAmplitude = 0.06f;

	[Tooltip( "Large bob right after the platform reaches top, then eases down to Hover Amplitude." )]
	[SerializeField] [Min( 0f )] float hoverLandingAmplitude = 0.35f;

	[SerializeField] [Min( 0f )] float hoverFrequency = 0.35f;
	[SerializeField] [Min( 0f )] float hoverPhaseStep = 0.7f;

	[Tooltip( "Seconds to blend the rise ending into hover: residual offset fades out, landing bob eases down to Hover Amplitude." )]
	[SerializeField] [Min( 0.05f )] float hoverBlendDuration = 2f;

	[Header( "Feedbacks" )]
	[SerializeField] Feedbacks onRiseStart;

	bool _subscribed;
	bool _activated;
	bool _initialized;

	public static IReadOnlyList<FloatingPlatformGroup> ActiveGroups => All;

	public string GroupId => groupId;
	public bool IsActivated => _activated;
	public FloatingPlatformTriggerType TriggerType => triggerType;
	public string TriggerId => triggerId;
	public FloatingPlatform[] Platforms => platforms;

	void OnEnable()
	{
		if ( !All.Contains( this ) )
			All.Add( this );

		Subscribe();
		InitializeState();
	}

	void OnDisable()
	{
		Unsubscribe();
		All.Remove( this );
	}

	void OnValidate()
	{
		if ( riseCurve == null || riseCurve.length == 0 )
			riseCurve = CreateDefaultOvershootCurve();
	}

	void InitializeState()
	{
		ConfigureAllPlatforms();

		if ( ShouldRestoreActivated() )
		{
			SnapActivated();
			_initialized = true;
			return;
		}

		SinkAll();
		_activated = false;
		_initialized = true;
	}

	void ConfigureAllPlatforms()
	{
		if ( platforms == null )
			return;

		for ( int i = 0; i < platforms.Length; i++ )
		{
			FloatingPlatform platform = platforms[ i ];
			if ( platform == null )
				continue;

			if ( !platform.HasRestPose )
				platform.CaptureRestPose();

			platform.ConfigureMotion( sunkYOffset, riseDuration, riseCurve, hoverAmplitude, hoverLandingAmplitude, hoverFrequency, i * hoverPhaseStep, hoverBlendDuration );
		}
	}

	void SinkAll()
	{
		if ( platforms == null )
			return;

		for ( int i = 0; i < platforms.Length; i++ )
		{
			FloatingPlatform platform = platforms[ i ];
			if ( platform == null )
				continue;
			platform.ConfigureMotion( sunkYOffset, riseDuration, riseCurve, hoverAmplitude, hoverLandingAmplitude, hoverFrequency, i * hoverPhaseStep, hoverBlendDuration );
			platform.SinkImmediate();
		}
	}

	void SnapActivated()
	{
		_activated = true;
		if ( platforms == null )
			return;

		for ( int i = 0; i < platforms.Length; i++ )
		{
			FloatingPlatform platform = platforms[ i ];
			if ( platform == null )
				continue;
			platform.ConfigureMotion( sunkYOffset, riseDuration, riseCurve, hoverAmplitude, hoverLandingAmplitude, hoverFrequency, i * hoverPhaseStep, hoverBlendDuration );
			platform.SnapToRestAndHover();
		}
	}

	/// <summary>Begin the staggered rise. No-op if already activated.</summary>
	public void Activate()
	{
		if ( _activated )
			return;

		_activated = true;
		ConfigureAllPlatforms();

		if ( onRiseStart != null )
			onRiseStart.Play();

		if ( platforms == null )
			return;

		for ( int i = 0; i < platforms.Length; i++ )
		{
			FloatingPlatform platform = platforms[ i ];
			if ( platform == null )
				continue;
			platform.BeginRise( staggerDelay * i );
		}
	}

	/// <summary>Force activate even if already up (replays rise from sunk).</summary>
	public void ForceActivate()
	{
		_activated = false;
		SinkAll();
		Activate();
	}

	/// <summary>Sink all platforms again (playtest / debug).</summary>
	public void ResetToSunk()
	{
		_activated = false;
		SinkAll();
	}

	bool ShouldRestoreActivated()
	{
		if ( string.IsNullOrEmpty( triggerId ) && triggerType != FloatingPlatformTriggerType.Manual )
			return false;

		switch ( triggerType )
		{
			case FloatingPlatformTriggerType.TutorialCompleted:
				return IsTutorialCompleted( triggerId );
			case FloatingPlatformTriggerType.WorldEventFired:
				return IsWorldEventFired( triggerId );
			case FloatingPlatformTriggerType.AbilityUnlocked:
				return IsAbilityUnlocked( triggerId );
			default:
				return false;
		}
	}

	void Subscribe()
	{
		if ( _subscribed )
			return;

		EventBus.Subscribe<TutorialCompletedEvent>( OnTutorialCompleted );
		EventBus.Subscribe<WorldEventFiredEvent>( OnWorldEventFired );
		EventBus.Subscribe<AbilityUnlockedEvent>( OnAbilityUnlocked );
		EventBus.Subscribe<VolumeEnteredEvent>( OnVolumeEntered );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;

		EventBus.Unsubscribe<TutorialCompletedEvent>( OnTutorialCompleted );
		EventBus.Unsubscribe<WorldEventFiredEvent>( OnWorldEventFired );
		EventBus.Unsubscribe<AbilityUnlockedEvent>( OnAbilityUnlocked );
		EventBus.Unsubscribe<VolumeEnteredEvent>( OnVolumeEntered );
		_subscribed = false;
	}

	void OnTutorialCompleted( TutorialCompletedEvent evt )
	{
		if ( !_initialized || triggerType != FloatingPlatformTriggerType.TutorialCompleted )
			return;
		if ( string.IsNullOrEmpty( triggerId ) || evt.TutorialId != triggerId )
			return;
		Activate();
	}

	void OnWorldEventFired( WorldEventFiredEvent evt )
	{
		if ( !_initialized || triggerType != FloatingPlatformTriggerType.WorldEventFired )
			return;
		if ( string.IsNullOrEmpty( triggerId ) || evt.Id != triggerId )
			return;
		Activate();
	}

	void OnAbilityUnlocked( AbilityUnlockedEvent evt )
	{
		if ( !_initialized || triggerType != FloatingPlatformTriggerType.AbilityUnlocked )
			return;
		if ( string.IsNullOrEmpty( triggerId ) || evt.AbilityId != triggerId )
			return;
		Activate();
	}

	void OnVolumeEntered( VolumeEnteredEvent evt )
	{
		if ( !_initialized || triggerType != FloatingPlatformTriggerType.VolumeEntered )
			return;
		if ( string.IsNullOrEmpty( triggerId ) || evt.VolumeId != triggerId )
			return;
		Activate();
	}

	static bool IsTutorialCompleted( string tutorialId )
	{
		TutorialManager tutorials = TutorialManager.Instance;
		return tutorials != null && tutorials.IsCompleted( tutorialId );
	}

	static bool IsWorldEventFired( string eventId )
	{
		if ( string.IsNullOrEmpty( eventId ) )
			return false;

		ProfileSaveData save = ProfileManager.Instance != null ? ProfileManager.Instance.ProfileSaveData : null;
		if ( save == null )
			return false;

		save.EnsureWorldEventProgress();
		return save.firedWorldEventIds != null && save.firedWorldEventIds.Contains( eventId );
	}

	static bool IsAbilityUnlocked( string abilityId )
	{
		AbilitySystem system = AbilitySystem.Instance;
		return system != null && system.IsUnlocked( abilityId );
	}

	static AnimationCurve CreateDefaultOvershootCurve()
	{
		// Ease-out rise, slight overshoot (~7%), then a slower settle back to rest (1).
		return new AnimationCurve(
			new Keyframe( 0f, 0f, 0f, 2.8f ),
			new Keyframe( 0.5f, 0.92f, 0.9f, 0.9f ),
			new Keyframe( 0.72f, 1.07f, 0.05f, 0.05f ),
			new Keyframe( 1f, 1f, -0.25f, 0f ) );
	}

#if UNITY_EDITOR
	public void EditorSetPlatforms( FloatingPlatform[] next )
	{
		platforms = next != null ? next : System.Array.Empty<FloatingPlatform>();
		UnityEditor.EditorUtility.SetDirty( this );
	}

	public void EditorCaptureAllRestPoses()
	{
		if ( platforms == null )
			return;

		for ( int i = 0; i < platforms.Length; i++ )
		{
			FloatingPlatform platform = platforms[ i ];
			if ( platform == null )
				continue;
			platform.EditorCaptureRestPose();
		}
	}

	void OnDrawGizmosSelected()
	{
		if ( platforms == null )
			return;

		for ( int i = 0; i < platforms.Length; i++ )
		{
			FloatingPlatform platform = platforms[ i ];
			if ( platform == null )
				continue;

			Vector3 rest = platform.HasRestPose ? platform.RestPosition : platform.transform.position;
			Vector3 sunk = rest + new Vector3( 0f, sunkYOffset, 0f );

			Gizmos.color = new Color( 0.35f, 0.85f, 1f, 0.9f );
			Gizmos.DrawWireCube( rest, new Vector3( 1.2f, 0.15f, 1.2f ) );

			Gizmos.color = new Color( 0.5f, 0.5f, 0.7f, 0.35f );
			Gizmos.DrawWireCube( sunk, new Vector3( 1.2f, 0.15f, 1.2f ) );

			Gizmos.color = new Color( 1f, 0.85f, 0.3f, 0.8f );
			Gizmos.DrawLine( sunk, rest );

#if UNITY_EDITOR
			UnityEditor.Handles.Label( rest + Vector3.up * 0.4f, "#" + i + " " + groupId );
			if ( !string.IsNullOrEmpty( triggerId ) )
				UnityEditor.Handles.Label( rest + Vector3.up * 0.7f, triggerType + ": " + triggerId );
#endif
		}
	}
#endif
}
