using FeedbackSystem;

using UnityEngine;

public enum AudioChannel
{
	Fx,
	Music,
	Ambience
}

/// <summary>
/// Combines designer <see cref="AudioDefinition.masterVolume"/> with the user's saved master volume
/// and applies the product to <see cref="AudioListener.volume"/> so all game audio scales together.
/// Debug channel toggles mute FX / music / ambience independently (session-only, default on).
/// </summary>
public static class AudioMaster
{
	const float PersistDelaySeconds = 0.5f;
	const float DefaultMaster = 1f;

	static AudioDefinition _definition;
	static float _userMaster = DefaultMaster;
	static float _queuedPersistTime = -1f;
	static bool _fxEnabled = true;
	static bool _musicEnabled = true;
	static bool _ambienceEnabled = true;

	static AudioDefinition Definition => RuntimeDefinition.Resolve( ref _definition );

	public static float UserMasterVolume
	{
		get
		{
			ProfileSaveData save = GetSave();
			if ( save == null )
				return _userMaster;
			return Mathf.Clamp01( save.masterVolume );
		}
	}

	public static float DesignerMasterVolume
	{
		get
		{
			AudioDefinition def = Definition;
			if ( def == null )
				return DefaultMaster;
			return Mathf.Clamp01( def.masterVolume );
		}
	}

	public static float CombinedMasterVolume => Mathf.Clamp01( UserMasterVolume * DesignerMasterVolume );

	public static bool IsChannelEnabled( AudioChannel channel )
	{
		switch ( channel )
		{
			case AudioChannel.Fx:
				return _fxEnabled;
			case AudioChannel.Music:
				return _musicEnabled;
			case AudioChannel.Ambience:
				return _ambienceEnabled;
			default:
				return true;
		}
	}

	public static float GetChannelGain( AudioChannel channel )
	{
		return IsChannelEnabled( channel ) ? 1f : 0f;
	}

	public static void SetChannelEnabled( AudioChannel channel, bool enabled )
	{
		switch ( channel )
		{
			case AudioChannel.Fx:
				_fxEnabled = enabled;
				break;
			case AudioChannel.Music:
				_musicEnabled = enabled;
				break;
			case AudioChannel.Ambience:
				_ambienceEnabled = enabled;
				break;
		}

		Apply();
		if ( channel == AudioChannel.Fx && !enabled )
			FeedbackAudioPool.StopAll();
	}

	public static void Tick()
	{
		Apply();
		if ( _queuedPersistTime < 0f )
			return;

		ProfileSaveData save = GetSave();
		if ( save != null )
			save.masterVolume = _userMaster;

		if ( Time.unscaledTime < _queuedPersistTime )
			return;
		FlushPersist();
	}

	public static void Apply()
	{
		AudioListener.volume = CombinedMasterVolume;
		FeedbackSfxPlayback.Enabled = _fxEnabled;
	}

	public static void SetUserMasterVolume( float value )
	{
		value = Mathf.Clamp01( value );
		_userMaster = value;
		ProfileSaveData save = GetSave();
		if ( save != null )
			save.masterVolume = value;
		Apply();
		_queuedPersistTime = Time.unscaledTime + PersistDelaySeconds;
	}

	public static void FlushPersist()
	{
		if ( _queuedPersistTime < 0f )
			return;

		ProfileManager manager = ProfileManager.Instance;
		if ( manager == null || manager.ProfileSaveData == null )
			return;

		manager.ProfileSaveData.masterVolume = _userMaster;
		_queuedPersistTime = -1f;
		manager.SaveCurrentStatsToProfile();
	}

	static ProfileSaveData GetSave()
	{
		ProfileManager manager = ProfileManager.Instance;
		if ( manager == null )
			return null;
		return manager.ProfileSaveData;
	}
}
