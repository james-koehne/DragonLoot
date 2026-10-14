using System;

using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Rotates through clips with A/B overlap crossfade. Use <see cref="AdvanceMode.EachPlay"/> for manual pulses
/// or <see cref="AdvanceMode.WhileActive"/> to advance on an interval while the feedback is active.
/// </summary>
[Serializable]
[FeedbackInfo( "Audio/Staggered Clip SFX" )]
public class StaggeredClipSfxFeedback : Feedback, IFeedbackTick
{
	public enum AdvanceMode
	{
		EachPlay = 0,
		WhileActive = 1
	}

	public enum ClipSource
	{
		Serialized = 0,
		CoinSortingCrank = 1,
		CoinSortingSort = 2
	}

	public AdvanceMode Mode = AdvanceMode.EachPlay;

	[Tooltip( "When not Serialized, clips and timing are read from the station definition on the Feedbacks owner." )]
	public ClipSource ClipsFrom = ClipSource.Serialized;

	public AudioClip[] Clips;

	[Min( 0.05f )]
	public float PlayInterval = 0.05f;

	[Min( 0f )]
	public float OverlapFadeSeconds = 0.12f;

	public FeedbackSfxSettings Playback = new FeedbackSfxSettings
	{
		SpatialBlend = 1f,
		MinDistance = 1.5f,
		MaxDistance = 18f
	};

	AudioSource _current;
	AudioSource _fading;
	int _clipIndex = -1;
	float _nextPlayTime;
	float _fadeRemaining;
	float _fadeDuration;
	float _fadeStartVolume;
	bool _active;
	bool _tickRegistered;

	public override void Play()
	{
		if ( Mode == AdvanceMode.EachPlay )
		{
			TryPlayNext();
			EnsureFadeTick();
			return;
		}

		_active = true;
		_nextPlayTime = 0f;
		EnsureFadeTick();
	}

	public override void Stop()
	{
		_active = false;
		StopSources();
		UnregisterFadeTick();
	}

	public void Cancel()
	{
		Stop();
	}

	public override void Reset()
	{
		Stop();
	}

	public override float GetHoldDuration()
	{
		return Mode == AdvanceMode.WhileActive ? 3600f : base.GetHoldDuration();
	}

	public bool Tick( float deltaTime )
	{
		if ( !FeedbackSfxPlayback.Enabled )
		{
			StopSources();
			return Mode == AdvanceMode.WhileActive && _active;
		}

		TickFade( deltaTime );

		if ( Mode != AdvanceMode.WhileActive || !_active )
			return _fadeRemaining > 0f;

		if ( Owner != null && Owner.GetComponent<CoinSortingStation>() is CoinSortingStation station )
		{
			if ( station.IsRepositioning || !station.IsProcessing )
			{
				BeginFadePrevious( ResolveOverlapFade( null ) );
				return true;
			}
		}

		if ( Time.time < _nextPlayTime )
			return true;

		CoinSortingStationDefinition def = ResolveCoinSortingDefinition();
		float interval = def != null ? def.sortPlayInterval : PlayInterval;
		_nextPlayTime = Time.time + Mathf.Max( 0.05f, interval );
		TryPlayNext();
		return true;
	}

	void TryPlayNext()
	{
		if ( !FeedbackSfxPlayback.Enabled )
			return;

		AudioClip[] clips = ResolveClips();
		if ( !HasClipArray( clips ) )
			return;

		if ( Mode == AdvanceMode.EachPlay && ClipsFrom == ClipSource.CoinSortingCrank )
		{
			CoinSortingStation station = Owner != null ? Owner.GetComponent<CoinSortingStation>() : null;
			if ( station == null || station.IsRepositioning )
				return;
			CoinSortingStationDefinition def = station.Definition;
			if ( def == null || !def.RequiresCrank( station.StationLevel ) )
				return;
			if ( Time.time < _nextPlayTime )
				return;
			float interval = def != null ? def.crankPlayInterval : PlayInterval;
			_nextPlayTime = Time.time + Mathf.Max( 0.05f, interval );
		}

		EnsureSources();
		if ( _current == null )
			return;

		int next = NextValidIndex( clips, _clipIndex );
		if ( next < 0 )
			return;

		_clipIndex = next;
		AudioClip clip = clips[ _clipIndex ];
		if ( clip == null )
			return;

		BeginFadePrevious( ResolveOverlapFade( ResolveCoinSortingDefinition() ) );

		_current.clip = clip;
		_current.loop = false;
		_current.pitch = ResolvePitch( ResolveCoinSortingDefinition() );
		_current.volume = ResolveVolume( ResolveCoinSortingDefinition() );
		_current.Play();
		EnsureFadeTick();
	}

	void EnsureFadeTick()
	{
		if ( _tickRegistered )
			return;

		_tickRegistered = true;
		RegisterTick( this );
	}

	void UnregisterFadeTick()
	{
		if ( !_tickRegistered )
			return;

		_tickRegistered = false;
		UnregisterTick( this );
	}

	void TickFade( float deltaTime )
	{
		if ( _fading == null || _fadeRemaining <= 0f )
		{
			if ( !_active && _fadeRemaining <= 0f && Mode == AdvanceMode.EachPlay )
				UnregisterFadeTick();
			return;
		}

		_fadeRemaining -= deltaTime;
		if ( _fadeRemaining <= 0f || !_fading.isPlaying )
		{
			_fading.Stop();
			_fading.volume = 0f;
			_fadeRemaining = 0f;
			if ( !_active && Mode == AdvanceMode.EachPlay )
				UnregisterFadeTick();
			return;
		}

		float t = _fadeDuration > 0.0001f ? _fadeRemaining / _fadeDuration : 0f;
		_fading.volume = _fadeStartVolume * Mathf.Clamp01( t );
	}

	void BeginFadePrevious( float fadeSeconds )
	{
		if ( _current == null || !_current.isPlaying )
			return;

		if ( _fading != null && _fading.isPlaying )
			_fading.Stop();

		AudioSource previous = _current;
		_current = _fading;
		_fading = previous;

		if ( fadeSeconds < 0.0001f )
		{
			_fading.Stop();
			_fadeRemaining = 0f;
			return;
		}

		_fadeStartVolume = _fading.volume;
		_fadeDuration = fadeSeconds;
		_fadeRemaining = fadeSeconds;
	}

	void StopSources()
	{
		if ( _current != null && _current.isPlaying )
			_current.Stop();
		if ( _fading != null && _fading.isPlaying )
			_fading.Stop();
		_fadeRemaining = 0f;
	}

	void EnsureSources()
	{
		if ( _current == null )
			_current = CreateSource( "StaggeredSfxA" );
		if ( _fading == null )
			_fading = CreateSource( "StaggeredSfxB" );
	}

	AudioSource CreateSource( string hostName )
	{
		if ( Owner == null )
			return null;

		Transform existing = Owner.transform.Find( hostName );
		GameObject host = existing != null ? existing.gameObject : new GameObject( hostName );
		if ( existing == null )
			host.transform.SetParent( Owner.transform, false );

		AudioSource source = host.GetComponent<AudioSource>();
		if ( source == null )
			source = host.AddComponent<AudioSource>();

		source.playOnAwake = false;
		source.loop = false;
		if ( Playback != null )
			Playback.ApplyToSource( source );
		return source;
	}

	AudioClip[] ResolveClips()
	{
		if ( ClipsFrom == ClipSource.Serialized )
			return Clips;

		CoinSortingStationDefinition def = ResolveCoinSortingDefinition();
		if ( def == null )
			return Clips;

		if ( ClipsFrom == ClipSource.CoinSortingCrank )
			return def.crankLoopClips;
		return def.sortLoopClips;
	}

	float ResolveOverlapFade( CoinSortingStationDefinition def )
	{
		if ( def != null && ( ClipsFrom == ClipSource.CoinSortingCrank || ClipsFrom == ClipSource.CoinSortingSort ) )
			return Mathf.Max( 0f, def.crankOverlapFadeSeconds );
		return OverlapFadeSeconds;
	}

	CoinSortingStationDefinition ResolveCoinSortingDefinition()
	{
		if ( Owner == null )
			return null;

		CoinSortingStation station = Owner.GetComponent<CoinSortingStation>();
		if ( station == null )
			return null;

		return station.Definition;
	}

	float ResolvePitch( CoinSortingStationDefinition def )
	{
		if ( ClipsFrom == ClipSource.CoinSortingSort && def != null )
			return FeedbackSfxPlayback.SamplePitch( def.sortPitchMin, def.sortPitchMax );

		if ( ClipsFrom == ClipSource.CoinSortingCrank )
		{
			CoinSortingStation station = Owner != null ? Owner.GetComponent<CoinSortingStation>() : null;
			float lo = def != null ? def.crankPitchMin : 0.85f;
			float hi = def != null ? def.crankPitchMax : 1.25f;
			float t = station != null ? station.ReserveNormalized : 0f;
			float pitch = Mathf.Lerp( lo, hi, t );
			float warning = def != null ? def.gaugeEmptyWarningNormalized : 0.15f;
			if ( station != null && t <= warning )
				pitch *= 0.88f;
			if ( pitch <= 0f )
				return 1f;
			return Mathf.Clamp( pitch, -3f, 3f );
		}

		return FeedbackSfxPlayback.SamplePitch( Playback.PitchMin, Playback.PitchMax );
	}

	float ResolveVolume( CoinSortingStationDefinition def )
	{
		if ( ClipsFrom == ClipSource.CoinSortingSort && def != null )
			return FeedbackSfxPlayback.ApplyGainToVolume( def.sortVolume );

		if ( ClipsFrom == ClipSource.CoinSortingCrank )
		{
			CoinSortingStation station = Owner != null ? Owner.GetComponent<CoinSortingStation>() : null;
			float volume = def != null ? def.crankVolume : 0.7f;
			float warning = def != null ? def.gaugeEmptyWarningNormalized : 0.15f;
			if ( station != null && station.ReserveNormalized <= warning )
				volume *= 0.55f;
			return FeedbackSfxPlayback.ApplyGainToVolume( volume );
		}

		return FeedbackSfxPlayback.ApplyGainToVolume( FeedbackSfxPlayback.SampleVolume( Playback.VolumeMin, Playback.VolumeMax ) );
	}

	static bool HasClipArray( AudioClip[] clips )
	{
		if ( clips == null || clips.Length == 0 )
			return false;

		for ( int i = 0; i < clips.Length; i++ )
		{
			if ( clips[ i ] != null )
				return true;
		}

		return false;
	}

	static int NextValidIndex( AudioClip[] clips, int current )
	{
		if ( clips == null || clips.Length == 0 )
			return -1;

		int start = current < 0 ? 0 : ( current + 1 ) % clips.Length;
		for ( int offset = 0; offset < clips.Length; offset++ )
		{
			int i = ( start + offset ) % clips.Length;
			if ( clips[ i ] != null )
				return i;
		}

		return -1;
	}
}
