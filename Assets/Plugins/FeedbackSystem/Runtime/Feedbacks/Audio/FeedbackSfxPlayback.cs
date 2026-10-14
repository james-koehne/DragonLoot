using UnityEngine;

namespace FeedbackSystem
{
	/// <summary>
	/// Shared one-shot playback for built-in audio feedbacks (pool or assigned <see cref="AudioSource"/>).
	/// </summary>
	public static class FeedbackSfxPlayback
	{
		/// <summary>When false, one-shots are skipped. Default true. Game code uses this as the FX mute gate.</summary>
		public static bool Enabled = true;

		public static float SampleVolume( float min, float max )
		{
			float lo = Mathf.Max( 0f, Mathf.Min( min, max ) );
			float hi = Mathf.Max( 0f, Mathf.Max( min, max ) );
			if ( Mathf.Approximately( lo, hi ) )
				return lo;

			return UnityEngine.Random.Range( lo, hi );
		}

		public static float SamplePitch( float min, float max )
		{
			float lo = Mathf.Clamp( Mathf.Min( min, max ), -3f, 3f );
			float hi = Mathf.Clamp( Mathf.Max( min, max ), -3f, 3f );
			float pitch;
			if ( Mathf.Approximately( lo, hi ) )
				pitch = lo;
			else
				pitch = UnityEngine.Random.Range( lo, hi );

			return pitch <= 0f ? 1f : pitch;
		}

		public static AudioSource Play(
			AudioClip clip,
			FeedbackSfxSettings settings,
			Vector3 position,
			Transform followTransform,
			FeedbackOneShotVoice voiceTracker = null )
		{
			if ( !Enabled || clip == null || settings == null )
				return null;

			float volume = SampleVolume( settings.VolumeMin, settings.VolumeMax );
			float pitch = SamplePitch( settings.PitchMin, settings.PitchMax );

			if ( followTransform != null )
				position = followTransform.position;

			AudioSource audioSource = settings.AudioSource;
			if ( audioSource != null )
			{
				audioSource.transform.position = position;
				settings.ApplyToSource( audioSource );
				audioSource.pitch = pitch;
				ApplyVolumeAndPlay( audioSource, clip, volume );
				FeedbackAudioFollower.Attach( audioSource, followTransform );
				if ( voiceTracker != null )
					voiceTracker.Register( audioSource );
				return audioSource;
			}

			AudioSource pooled = FeedbackAudioPool.Play( clip, volume, pitch, position, settings, followTransform );
			if ( voiceTracker != null && pooled != null )
				voiceTracker.Register( pooled );
			return pooled;
		}

		public static void Play(
			AudioClip clip,
			float volumeMin,
			float volumeMax,
			float pitchMin,
			float pitchMax,
			AudioSource audioSource,
			Vector3 position,
			float spatialBlend = 0f,
			float minDistance = 1f,
			float maxDistance = 20f,
			Transform followTransform = null )
		{
			FeedbackSfxSettings settings = new FeedbackSfxSettings
			{
				VolumeMin = volumeMin,
				VolumeMax = volumeMax,
				PitchMin = pitchMin,
				PitchMax = pitchMax,
				SpatialBlend = spatialBlend,
				MinDistance = minDistance,
				MaxDistance = maxDistance,
				AudioSource = audioSource
			};
			Play( clip, settings, position, followTransform );
		}

		/// <summary>
		/// PlayOneShot volumeScale is effectively 0–1; values above 1 boost via AudioSource.volume.
		/// </summary>
		public static void ApplyVolumeAndPlay( AudioSource source, AudioClip clip, float volume )
		{
			if ( source == null || clip == null )
				return;

			float gain = Mathf.Max( 0f, volume );
			if ( gain <= 1f )
			{
				source.volume = 1f;
				source.PlayOneShot( clip, gain );
				return;
			}

			source.volume = Mathf.Min( gain, 4f );
			source.PlayOneShot( clip, 1f );
		}

		/// <summary>Applies gain to AudioSource.volume (supports boost up to 4).</summary>
		public static float ApplyGainToVolume( float gain )
		{
			return Mathf.Min( Mathf.Max( 0f, gain ), 4f );
		}
	}
}
