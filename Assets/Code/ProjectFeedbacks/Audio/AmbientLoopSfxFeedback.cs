using System;

using FeedbackSystem;

using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Constant looping spatial SFX (companion ambience). Always audible while playing;
/// volume / pitch / 3D rolloff are inspector-tweakable.
/// </summary>
[Serializable]
[FeedbackInfo( "Audio/Ambient Loop SFX" )]
public class AmbientLoopSfxFeedback : Feedback, IFeedbackTick, ISerializationCallbackReceiver
{
	public AudioClip Clip;

	public AudioSource AudioSource;

	public FeedbackSfxSettings Playback = new FeedbackSfxSettings
	{
		SpatialBlend = 1f,
		MinDistance = 1.5f,
		MaxDistance = 18f,
		DopplerLevel = 0.15f,
		RolloffMode = AudioRolloffMode.Linear
	};

	[Min( 0f )]
	public float Volume = 0.55f;

	[Range( -3f, 3f )]
	public float Pitch = 1f;

	[Min( 0f )]
	public float FadeInSeconds = 0.35f;

	[Min( 0f )]
	public float FadeOutSeconds = 0.45f;

	[FormerlySerializedAs( "SpatialBlend" )]
	[SerializeField, HideInInspector]
	float _migrateSpatialBlend = 1f;

	[FormerlySerializedAs( "MinDistance" )]
	[SerializeField, HideInInspector]
	float _migrateMinDistance = 1.5f;

	[FormerlySerializedAs( "MaxDistance" )]
	[SerializeField, HideInInspector]
	float _migrateMaxDistance = 18f;

	[FormerlySerializedAs( "DopplerLevel" )]
	[SerializeField, HideInInspector]
	float _migrateDopplerLevel = 0.15f;

	[FormerlySerializedAs( "RolloffMode" )]
	[SerializeField, HideInInspector]
	AudioRolloffMode _migrateRolloffMode = AudioRolloffMode.Linear;

	[SerializeField, HideInInspector]
	bool _serializedPlaybackMerged;

	float _fade;
	bool _running;
	bool _fadingOut;

	public override void Play()
	{
		if ( Clip == null )
			return;

		LoopSfxSourceUtil.EnsureAudioSource( Owner != null ? Owner.gameObject : null, ref AudioSource );
		if ( AudioSource == null )
			return;

		Cancel( hardStop: false );
		LoopSfxSourceUtil.ConfigureLoopSource( AudioSource, Playback );
		AudioSource.clip = Clip;
		AudioSource.loop = true;
		_fade = 0f;
		_fadingOut = false;
		_running = true;
		ApplyMix();
		RegisterTick( this );
	}

	public override void Stop()
	{
		if ( !_running || AudioSource == null )
		{
			Cancel( hardStop: true );
			return;
		}

		_fadingOut = true;
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

		float fadeTarget = _fadingOut ? 0f : 1f;
		float fadeSeconds = _fadingOut ? FadeOutSeconds : FadeInSeconds;
		float fadeRate = fadeSeconds > 0.0001f ? 1f / fadeSeconds : 1000f;
		_fade = Mathf.MoveTowards( _fade, fadeTarget, fadeRate * deltaTime );
		ApplyMix();

		if ( _fade > 0.0001f )
		{
			if ( !AudioSource.isPlaying )
				AudioSource.Play();
		}
		else if ( _fadingOut )
		{
			Cancel( hardStop: true );
			return false;
		}
		else if ( AudioSource.isPlaying )
		{
			AudioSource.Pause();
		}

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
		_fadingOut = false;
		_fade = 0f;

		if ( AudioSource == null )
			return;

		if ( hardStop )
		{
			AudioSource.Stop();
			AudioSource.volume = 0f;
		}
	}

	void ApplyMix()
	{
		float pitch = Pitch;
		if ( pitch <= 0f )
			pitch = 1f;
		AudioSource.pitch = Mathf.Clamp( pitch, -3f, 3f );
		AudioSource.volume = LoopSfxSourceUtil.ApplyVolumeGain( Volume, _fade, 1f );
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
			Playback.RolloffMode = _migrateRolloffMode;
		}

		_serializedPlaybackMerged = true;
	}

	bool NeedsSpatialMigration()
	{
		if ( !Mathf.Approximately( _migrateSpatialBlend, 1f ) )
			return true;
		if ( !Mathf.Approximately( _migrateMinDistance, 1.5f ) )
			return true;
		if ( !Mathf.Approximately( _migrateMaxDistance, 18f ) )
			return true;
		if ( !Mathf.Approximately( _migrateDopplerLevel, 0.15f ) )
			return true;
		if ( _migrateRolloffMode != AudioRolloffMode.Linear )
			return true;
		return false;
	}
}
