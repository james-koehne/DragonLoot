using System;

using FeedbackSystem;

using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Looping spatial SFX whose pitch and volume follow <see cref="IFeedbackIntensity"/>.
/// Idle fades volume to zero and pauses the source (does not require <see cref="Feedbacks.Stop"/>).
/// </summary>
[Serializable]
[FeedbackInfo( "Audio/Loop SFX" )]
public class LoopSfxFeedback : Feedback, IFeedbackTick, ISerializationCallbackReceiver
{
	const float IntensityEpsilon = 0.02f;

	public AudioClip Clip;

	public AudioSource AudioSource;

	public FeedbackSfxSettings Playback = new FeedbackSfxSettings
	{
		SpatialBlend = 1f,
		MinDistance = 2f,
		MaxDistance = 30f,
		DopplerLevel = 0.4f,
		RolloffMode = AudioRolloffMode.Linear
	};

	[Range( -3f, 3f )]
	[Tooltip( "Pitch at intensity 0." )]
	public float PitchAtRest = 0.75f;

	[Range( -3f, 3f )]
	[Tooltip( "Pitch at intensity 1." )]
	public float PitchAtMax = 1.2f;

	[Min( 0f )]
	[Tooltip( "Volume at intensity 0 (before fade)." )]
	public float VolumeAtRest;

	[Min( 0f )]
	[Tooltip( "Volume at intensity 1 (before fade / brake duck)." )]
	public float VolumeAtMax = 0.85f;

	[Min( 0f )]
	public float FadeInSeconds = 0.12f;

	[Min( 0f )]
	public float FadeOutSeconds = 0.28f;

	[Range( 0f, 1f )]
	[Tooltip( "Volume multiplier while FeedbackBraking is true. Ignored when RequireBraking is set." )]
	public float BrakeDuck = 0.55f;

	[Min( 0f )]
	public float BrakeDuckBlendSeconds = 0.1f;

	[Tooltip( "When true, only audible while FeedbackBraking and intensity > 0 (speed-fades to silence at stop)." )]
	public bool RequireBraking;

	[FormerlySerializedAs( "SpatialBlend" )]
	[SerializeField, HideInInspector]
	float _migrateSpatialBlend = 1f;

	[FormerlySerializedAs( "MinDistance" )]
	[SerializeField, HideInInspector]
	float _migrateMinDistance = 2f;

	[FormerlySerializedAs( "MaxDistance" )]
	[SerializeField, HideInInspector]
	float _migrateMaxDistance = 30f;

	[FormerlySerializedAs( "DopplerLevel" )]
	[SerializeField, HideInInspector]
	float _migrateDopplerLevel = 0.4f;

	[SerializeField, HideInInspector]
	bool _serializedPlaybackMerged;

	float _fade;
	float _brakeMul = 1f;
	bool _running;
	IFeedbackIntensity _intensity;

	public override void Play()
	{
		if ( Clip == null )
			return;

		LoopSfxSourceUtil.EnsureAudioSource( Owner != null ? Owner.gameObject : null, ref AudioSource );
		if ( AudioSource == null )
			return;

		Cancel( hardStop: false );
		_intensity = ResolveIntensity();
		LoopSfxSourceUtil.ConfigureLoopSource( AudioSource, Playback );
		AudioSource.clip = Clip;
		AudioSource.loop = true;
		_fade = 0f;
		_brakeMul = 1f;
		_running = true;
		ApplyMix( 0f );
		RegisterTick( this );
	}

	public override void Stop()
	{
		Cancel( hardStop: true );
	}

	public override void Reset()
	{
		Cancel( hardStop: true );
	}

	public override float GetHoldDuration()
	{
		return 3600f;
	}

	public bool Tick( float deltaTime )
	{
		if ( !_running || AudioSource == null || Clip == null )
		{
			_running = false;
			return false;
		}

		if ( LoopSfxSourceUtil.TickFxMutedPause( AudioSource ) )
			return true;

		if ( _intensity == null )
			_intensity = ResolveIntensity();

		float intensity = _intensity != null ? Mathf.Clamp01( _intensity.FeedbackIntensity ) : 0f;
		bool braking = _intensity != null && _intensity.FeedbackBraking;
		bool wantAudible = intensity >= IntensityEpsilon;
		if ( RequireBraking )
			wantAudible = wantAudible && braking;

		float fadeTarget = wantAudible ? 1f : 0f;
		float fadeRate = fadeTarget > _fade
			? ( FadeInSeconds > 0.0001f ? 1f / FadeInSeconds : 1000f )
			: ( FadeOutSeconds > 0.0001f ? 1f / FadeOutSeconds : 1000f );
		_fade = Mathf.MoveTowards( _fade, fadeTarget, fadeRate * deltaTime );

		float brakeTarget = 1f;
		if ( !RequireBraking )
			brakeTarget = braking ? Mathf.Clamp01( BrakeDuck ) : 1f;
		float brakeRate = BrakeDuckBlendSeconds > 0.0001f ? 1f / BrakeDuckBlendSeconds : 1000f;
		_brakeMul = Mathf.MoveTowards( _brakeMul, brakeTarget, brakeRate * deltaTime );

		ApplyMix( intensity );

		if ( _fade > 0.0001f )
		{
			if ( !AudioSource.isPlaying )
				AudioSource.Play();
		}
		else if ( AudioSource.isPlaying )
			AudioSource.Pause();

		return true;
	}

	public void Cancel()
	{
		Cancel( hardStop: true );
	}

	void Cancel( bool hardStop )
	{
		UnregisterTick( this );
		_running = false;
		_intensity = null;
		_fade = 0f;
		_brakeMul = 1f;

		if ( AudioSource == null )
			return;

		if ( hardStop )
		{
			AudioSource.Stop();
			AudioSource.volume = 0f;
		}
	}

	void ApplyMix( float intensity )
	{
		float pitch = Mathf.Lerp( PitchAtRest, PitchAtMax, intensity );
		if ( pitch <= 0f )
			pitch = 1f;
		AudioSource.pitch = Mathf.Clamp( pitch, -3f, 3f );

		float volume = Mathf.Lerp( VolumeAtRest, VolumeAtMax, intensity );
		AudioSource.volume = LoopSfxSourceUtil.ApplyVolumeGain( volume, _fade, _brakeMul );
	}

	IFeedbackIntensity ResolveIntensity()
	{
		if ( Owner == null )
			return null;

		return Owner.GetComponentInParent<IFeedbackIntensity>();
	}

	public void OnBeforeSerialize()
	{
		MergeMigrateIntoPlayback();
	}

	public void OnAfterDeserialize()
	{
		MergeMigrateIntoPlayback();
	}

	void MergeMigrateIntoPlayback()
	{
		if ( _serializedPlaybackMerged )
			return;

		if ( Playback == null )
			Playback = new FeedbackSfxSettings();

		if ( NeedsSpatialMigration() )
		{
			Playback.SpatialBlend = _migrateSpatialBlend;
			Playback.MinDistance = _migrateMinDistance;
			Playback.MaxDistance = _migrateMaxDistance;
			Playback.DopplerLevel = _migrateDopplerLevel;
			Playback.RolloffMode = AudioRolloffMode.Linear;
		}

		_serializedPlaybackMerged = true;
	}

	bool NeedsSpatialMigration()
	{
		if ( !Mathf.Approximately( _migrateSpatialBlend, 1f ) )
			return true;
		if ( !Mathf.Approximately( _migrateMinDistance, 2f ) )
			return true;
		if ( !Mathf.Approximately( _migrateMaxDistance, 30f ) )
			return true;
		if ( !Mathf.Approximately( _migrateDopplerLevel, 0.4f ) )
			return true;
		return false;
	}
}
