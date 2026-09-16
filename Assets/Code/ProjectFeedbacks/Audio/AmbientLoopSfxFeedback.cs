using System;

using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Constant looping spatial SFX (companion ambience). Always audible while playing;
/// volume / pitch / 3D rolloff are inspector-tweakable.
/// </summary>
[Serializable]
[FeedbackInfo( "Audio/Ambient Loop SFX" )]
public class AmbientLoopSfxFeedback : Feedback, IFeedbackTick
{
	public AudioClip Clip;

	public AudioSource AudioSource;

	[Range( 0f, 1f )]
	public float Volume = 0.55f;

	[Range( -3f, 3f )]
	public float Pitch = 1f;

	[Min( 0f )]
	public float FadeInSeconds = 0.35f;

	[Min( 0f )]
	public float FadeOutSeconds = 0.45f;

	[Range( 0f, 1f )]
	[Tooltip( "0 = 2D, 1 = full 3D." )]
	public float SpatialBlend = 1f;

	[Min( 0.01f )]
	public float MinDistance = 1.5f;

	[Min( 0.01f )]
	public float MaxDistance = 18f;

	[Range( 0f, 5f )]
	public float DopplerLevel = 0.15f;

	public AudioRolloffMode RolloffMode = AudioRolloffMode.Linear;

	float _fade;
	bool _running;
	bool _fadingOut;

	public override void Play()
	{
		if ( Clip == null )
			return;

		EnsureAudioSource();
		if ( AudioSource == null )
			return;

		Cancel( hardStop: false );
		ConfigureSource();
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

		if ( !FeedbackSfxPlayback.Enabled )
		{
			if ( AudioSource.isPlaying )
				AudioSource.Pause();
			return true;
		}

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

	void ConfigureSource()
	{
		AudioSource.playOnAwake = false;
		AudioSource.loop = true;
		AudioSource.spatialBlend = Mathf.Clamp01( SpatialBlend );
		AudioSource.minDistance = Mathf.Max( 0.01f, MinDistance );
		AudioSource.maxDistance = Mathf.Max( AudioSource.minDistance, MaxDistance );
		AudioSource.dopplerLevel = Mathf.Max( 0f, DopplerLevel );
		AudioSource.rolloffMode = RolloffMode;
	}

	void ApplyMix()
	{
		float pitch = Pitch;
		if ( pitch <= 0f )
			pitch = 1f;
		AudioSource.pitch = Mathf.Clamp( pitch, -3f, 3f );
		AudioSource.volume = Mathf.Clamp01( Volume * Mathf.Clamp01( _fade ) );
	}

	void EnsureAudioSource()
	{
		if ( AudioSource != null || Owner == null )
			return;

		AudioSource = Owner.GetComponent<AudioSource>();
		if ( AudioSource == null )
			AudioSource = Owner.gameObject.AddComponent<AudioSource>();
	}
}
