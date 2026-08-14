using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Plays the optional SFX authored on a <see cref="QuestDialogueLine"/> (volume only, fixed pitch).
/// </summary>
public static class QuestDialogueSfx
{
	static Feedbacks _host;
	static PlaySFXFeedback _sfx;

	public static void PlayLine( QuestDialogueLine line )
	{
		if ( line == null || line.sfx == null )
			return;

		EnsureHost();
		if ( _host == null || _sfx == null )
			return;

		float volume = Mathf.Clamp01( line.sfxVol );
		_sfx.Clip = line.sfx;
		_sfx.VolumeMin = volume;
		_sfx.VolumeMax = volume;
		_sfx.PitchMin = 1f;
		_sfx.PitchMax = 1f;
		_sfx.SpatialBlend = 0f;

		_host.Play();
	}

	static void EnsureHost()
	{
		if ( _host != null && _sfx != null )
			return;

		GameObject go = new GameObject( "[QuestDialogueSfx]" );
		Object.DontDestroyOnLoad( go );
		_host = go.AddComponent<Feedbacks>();
		_sfx = new PlaySFXFeedback();
		_host.AddFeedback( _sfx );
	}
}
