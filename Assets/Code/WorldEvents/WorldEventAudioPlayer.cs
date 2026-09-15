using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Plays one-shot audio for world events and cinematic cues via FeedbackSystem.
/// </summary>
public static class WorldEventAudioPlayer
{
	static Feedbacks _host;
	static PlaySFXFeedback _sfx;

	public static void Play( WorldEventAction action, Vector3 position )
	{
		if ( action == null || action.audioClip == null )
			return;

		PlayClip(
			action.audioClip,
			Mathf.Clamp01( action.audioVolumeMin ),
			Mathf.Clamp01( action.audioVolumeMax ),
			action.audioPitchMin,
			action.audioPitchMax,
			Mathf.Clamp01( action.audioSpatialBlend ),
			Mathf.Max( 0.01f, action.audioMinDistance ),
			Mathf.Max( 0.01f, action.audioMaxDistance ),
			position );
	}

	public static void PlayClipAtPlayer( AudioClip clip, float volume )
	{
		Vector3 position = Vector3.zero;
		if ( GameMode.Instance != null && GameMode.Instance.Player != null )
			position = GameMode.Instance.Player.transform.position;

		PlayClip( clip, volume, volume, 1f, 1f, 0f, 1f, 20f, position );
	}

	public static void PlayClip(
		AudioClip clip,
		float volumeMin,
		float volumeMax,
		float pitchMin,
		float pitchMax,
		float spatialBlend,
		float minDistance,
		float maxDistance,
		Vector3 position )
	{
		if ( clip == null )
			return;

		EnsureHost();
		if ( _host == null || _sfx == null )
			return;

		_sfx.Clip = clip;
		_sfx.VolumeMin = Mathf.Clamp01( volumeMin );
		_sfx.VolumeMax = Mathf.Clamp01( volumeMax );
		_sfx.PitchMin = pitchMin;
		_sfx.PitchMax = pitchMax;
		_sfx.SpatialBlend = Mathf.Clamp01( spatialBlend );
		_sfx.MinDistance = Mathf.Max( 0.01f, minDistance );
		_sfx.MaxDistance = Mathf.Max( _sfx.MinDistance, maxDistance );

		FeedbackContext context = new FeedbackContext();
		context.Position = position;
		_host.Play( context );
	}

	static void EnsureHost()
	{
		if ( _host != null && _sfx != null )
			return;

		GameObject go = new GameObject( "[WorldEventAudioPlayer]" );
		Object.DontDestroyOnLoad( go );
		_host = go.AddComponent<Feedbacks>();
		_sfx = new PlaySFXFeedback();
		_host.AddFeedback( _sfx );
	}
}
