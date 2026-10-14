using FeedbackSystem;

using UnityEngine;

static class LoopSfxSourceUtil
{
	public static void EnsureAudioSource( GameObject owner, ref AudioSource audioSource )
	{
		if ( audioSource != null || owner == null )
			return;

		audioSource = owner.GetComponent<AudioSource>();
		if ( audioSource == null )
			audioSource = owner.gameObject.AddComponent<AudioSource>();
	}

	public static void ConfigureLoopSource( AudioSource audioSource, FeedbackSfxSettings playback )
	{
		if ( audioSource == null )
			return;

		audioSource.playOnAwake = false;
		audioSource.loop = true;
		if ( playback != null )
			playback.ApplyToSource( audioSource );
	}

	public static bool TickFxMutedPause( AudioSource audioSource )
	{
		if ( FeedbackSfxPlayback.Enabled )
			return false;

		if ( audioSource != null && audioSource.isPlaying )
			audioSource.Pause();
		return true;
	}

	public static float ApplyVolumeGain( float baseVolume, float fadeMul, float brakeMul )
	{
		float gain = baseVolume * Mathf.Clamp01( fadeMul ) * Mathf.Clamp01( brakeMul );
		return FeedbackSfxPlayback.ApplyGainToVolume( gain );
	}
}
