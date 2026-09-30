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
			if ( !Enabled || clip == null )
				return;

			float volume = SampleVolume( volumeMin, volumeMax );
			float pitch = SamplePitch( pitchMin, pitchMax );

			if ( followTransform != null )
				position = followTransform.position;

			if ( audioSource != null )
			{
				audioSource.transform.position = position;
				audioSource.spatialBlend = Mathf.Clamp01( spatialBlend );
				audioSource.minDistance = Mathf.Max( 0.01f, minDistance );
				audioSource.maxDistance = Mathf.Max( audioSource.minDistance, maxDistance );
				audioSource.pitch = pitch;
				ApplyVolumeAndPlay( audioSource, clip, volume );
				FeedbackAudioFollower.Attach( audioSource, followTransform );
				return;
			}

			FeedbackAudioPool.Play( clip, volume, pitch, position, spatialBlend, minDistance, maxDistance, followTransform );
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
	}
}
