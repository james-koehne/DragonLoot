using System;

using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Looping spatial SFX whose pitch and volume follow <see cref="IFeedbackIntensity"/>.
/// Idle fades volume to zero and pauses the source (does not require <see cref="Feedbacks.Stop"/>).
/// </summary>
[Serializable]
[FeedbackInfo( "Audio/Loop SFX" )]
public class LoopSfxFeedback : Feedback, IFeedbackTick
{
	const float IntensityEpsilon = 0.02f;

	public AudioClip Clip;

	public AudioSource AudioSource;

	[Range( -3f, 3f )]
	[Tooltip( "Pitch at intensity 0." )]
	public float PitchAtRest = 0.75f;

	[Range( -3f, 3f )]
	[Tooltip( "Pitch at intensity 1." )]
	public float PitchAtMax = 1.2f;

	[Range( 0f, 1f )]
	[Tooltip( "Volume at intensity 0 (before fade)." )]
	public float VolumeAtRest = 0f;

	[Range( 0f, 1f )]
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

	[Range( 0f, 1f )]
	public float SpatialBlend = 1f;

	[Min( 0.01f )]
	public float MinDistance = 2f;

	[Min( 0.01f )]
	public float MaxDistance = 30f;

	[Range( 0f, 5f )]
	public float DopplerLevel = 0.4f;

	float _fade;
	float _brakeMul = 1f;
	bool _running;
	IFeedbackIntensity _intensity;

	public override void Play()
	{
		if ( Clip == null )
			return;

		EnsureAudioSource();
		if ( AudioSource == null )
			return;

		Cancel( hardStop: false );
		_intensity = ResolveIntensity();
		ConfigureSource();
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

		if ( !FeedbackSfxPlayback.Enabled )
		{
			if ( AudioSource.isPlaying )
				AudioSource.Pause();
			return true;
		}

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

	void ConfigureSource()
	{
		AudioSource.playOnAwake = false;
		AudioSource.loop = true;
		AudioSource.spatialBlend = Mathf.Clamp01( SpatialBlend );
		AudioSource.minDistance = Mathf.Max( 0.01f, MinDistance );
		AudioSource.maxDistance = Mathf.Max( AudioSource.minDistance, MaxDistance );
		AudioSource.dopplerLevel = Mathf.Max( 0f, DopplerLevel );
		AudioSource.rolloffMode = AudioRolloffMode.Linear;
	}

	void ApplyMix( float intensity )
	{
		float pitch = Mathf.Lerp( PitchAtRest, PitchAtMax, intensity );
		if ( pitch <= 0f )
			pitch = 1f;
		AudioSource.pitch = Mathf.Clamp( pitch, -3f, 3f );

		float volume = Mathf.Lerp( VolumeAtRest, VolumeAtMax, intensity );
		volume *= Mathf.Clamp01( _fade ) * Mathf.Clamp01( _brakeMul );
		AudioSource.volume = Mathf.Clamp01( volume );
	}

	IFeedbackIntensity ResolveIntensity()
	{
		if ( Owner == null )
			return null;

		return Owner.GetComponentInParent<IFeedbackIntensity>();
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
