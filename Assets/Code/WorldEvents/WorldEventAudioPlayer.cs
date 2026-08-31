using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Plays one-shot audio for <see cref="WorldEventActionType.PlayAudio"/> via FeedbackSystem.
/// </summary>
public static class WorldEventAudioPlayer
{
	static Feedbacks _host;
	static PlaySFXFeedback _sfx;

	public static void Play( WorldEventAction action, Vector3 position )
	{
		if ( action == null || action.audioClip == null )
			return;

		EnsureHost();
		if ( _host == null || _sfx == null )
			return;

		_sfx.Clip = action.audioClip;
		_sfx.VolumeMin = Mathf.Clamp01( action.audioVolumeMin );
		_sfx.VolumeMax = Mathf.Clamp01( action.audioVolumeMax );
		_sfx.PitchMin = action.audioPitchMin;
		_sfx.PitchMax = action.audioPitchMax;
		_sfx.SpatialBlend = Mathf.Clamp01( action.audioSpatialBlend );
		_sfx.MinDistance = Mathf.Max( 0.01f, action.audioMinDistance );
		_sfx.MaxDistance = Mathf.Max( _sfx.MinDistance, action.audioMaxDistance );

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
