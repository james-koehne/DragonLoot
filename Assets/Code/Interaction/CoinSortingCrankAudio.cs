using UnityEngine;

/// <summary>
/// Crank clicks plus prize-wheel one-shots while the station is sorting.
/// </summary>
[DisallowMultipleComponent]
public class CoinSortingCrankAudio : MonoBehaviour
{
	[SerializeField]
	CoinSortingStation _station;

	AudioSource _current;
	AudioSource _fading;
	AudioSource _sortCurrent;
	AudioSource _sortFading;
	int _clipIndex = -1;
	int _sortClipIndex = -1;
	float _nextPlayTime;
	float _nextSortPlayTime;
	float _fadeRemaining;
	float _fadeDuration;
	float _fadeStartVolume;
	float _sortFadeRemaining;
	float _sortFadeDuration;
	float _sortFadeStartVolume;
	bool _wasProcessing;

	void Awake()
	{
		if ( _station == null )
			_station = GetComponent<CoinSortingStation>();
		if ( _station == null )
			_station = GetComponentInParent<CoinSortingStation>();
		EnsureSources();
	}

	void Update()
	{
		if ( !AudioMaster.IsChannelEnabled( AudioChannel.Fx ) )
		{
			StopSources();
			StopSortSources();
			_wasProcessing = false;
			return;
		}

		TickFade();
		TickSortFade();
		TickSortLoop();
	}

	public void NotifyPulse()
	{
		if ( !AudioMaster.IsChannelEnabled( AudioChannel.Fx ) )
			return;
		if ( _station == null )
			return;
		if ( _station.IsRepositioning )
			return;

		CoinSortingStationDefinition def = _station.Definition;
		if ( def == null || !def.RequiresCrank( _station.StationLevel ) )
			return;

		if ( !HasClips( def ) )
			return;

		if ( Time.time < _nextPlayTime )
			return;

		float interval = Mathf.Max( 0.05f, def.crankPlayInterval );
		_nextPlayTime = Time.time + interval;
		PlayNext( def );
	}

	void TickSortLoop()
	{
		if ( _station == null || _station.IsRepositioning )
		{
			if ( _wasProcessing )
			{
				BeginSortFadePrevious( _station != null ? _station.Definition : null );
				_wasProcessing = false;
			}

			return;
		}

		bool processing = _station.IsProcessing;
		if ( !processing )
		{
			if ( _wasProcessing )
			{
				BeginSortFadePrevious( _station.Definition );
				_wasProcessing = false;
			}

			return;
		}

		CoinSortingStationDefinition def = _station.Definition;
		if ( def == null || !HasSortClips( def ) )
			return;

		if ( !_wasProcessing )
		{
			_wasProcessing = true;
			_nextSortPlayTime = 0f;
		}

		if ( Time.time < _nextSortPlayTime )
			return;

		float interval = Mathf.Max( 0.05f, def.sortPlayInterval );
		_nextSortPlayTime = Time.time + interval;
		PlayNextSort( def );
	}

	void PlayNext( CoinSortingStationDefinition def )
	{
		if ( !AudioMaster.IsChannelEnabled( AudioChannel.Fx ) )
			return;

		EnsureSources();
		if ( _current == null || def == null )
			return;

		AudioClip[] clips = def.crankLoopClips;
		int next = NextValidIndex( clips, _clipIndex );
		if ( next < 0 )
			return;

		_clipIndex = next;
		AudioClip clip = clips[ _clipIndex ];
		if ( clip == null )
			return;

		BeginFadePrevious( def );

		_current.clip = clip;
		_current.loop = false;
		_current.pitch = ResolvePitch( def );
		_current.volume = ResolveVolume( def );
		_current.Play();
	}

	void PlayNextSort( CoinSortingStationDefinition def )
	{
		if ( !AudioMaster.IsChannelEnabled( AudioChannel.Fx ) )
			return;

		EnsureSources();
		if ( _sortCurrent == null || def == null )
			return;

		AudioClip[] clips = def.sortLoopClips;
		int next = NextValidIndex( clips, _sortClipIndex );
		if ( next < 0 )
			return;

		_sortClipIndex = next;
		AudioClip clip = clips[ _sortClipIndex ];
		if ( clip == null )
			return;

		BeginSortFadePrevious( def );

		_sortCurrent.clip = clip;
		_sortCurrent.loop = false;
		_sortCurrent.pitch = SamplePitch( def.sortPitchMin, def.sortPitchMax );
		_sortCurrent.volume = Mathf.Clamp01( def.sortVolume );
		_sortCurrent.Play();
	}

	void BeginFadePrevious( CoinSortingStationDefinition def )
	{
		if ( _current == null || !_current.isPlaying )
			return;

		if ( _fading != null && _fading.isPlaying )
			_fading.Stop();

		AudioSource previous = _current;
		_current = _fading;
		_fading = previous;

		float fade = def != null ? Mathf.Max( 0f, def.crankOverlapFadeSeconds ) : 0.12f;
		if ( fade < 0.0001f )
		{
			_fading.Stop();
			_fadeRemaining = 0f;
			return;
		}

		_fadeStartVolume = _fading.volume;
		_fadeDuration = fade;
		_fadeRemaining = fade;
	}

	void BeginSortFadePrevious( CoinSortingStationDefinition def )
	{
		if ( _sortCurrent == null || !_sortCurrent.isPlaying )
			return;

		if ( _sortFading != null && _sortFading.isPlaying )
			_sortFading.Stop();

		AudioSource previous = _sortCurrent;
		_sortCurrent = _sortFading;
		_sortFading = previous;

		float fade = def != null ? Mathf.Max( 0f, def.crankOverlapFadeSeconds ) : 0.12f;
		if ( fade < 0.0001f )
		{
			_sortFading.Stop();
			_sortFadeRemaining = 0f;
			return;
		}

		_sortFadeStartVolume = _sortFading.volume;
		_sortFadeDuration = fade;
		_sortFadeRemaining = fade;
	}

	void TickFade()
	{
		if ( _fading == null || _fadeRemaining <= 0f )
			return;

		_fadeRemaining -= Time.deltaTime;
		if ( _fadeRemaining <= 0f || !_fading.isPlaying )
		{
			_fading.Stop();
			_fading.volume = 0f;
			_fadeRemaining = 0f;
			return;
		}

		float t = _fadeDuration > 0.0001f ? _fadeRemaining / _fadeDuration : 0f;
		_fading.volume = _fadeStartVolume * Mathf.Clamp01( t );
	}

	void TickSortFade()
	{
		if ( _sortFading == null || _sortFadeRemaining <= 0f )
			return;

		_sortFadeRemaining -= Time.deltaTime;
		if ( _sortFadeRemaining <= 0f || !_sortFading.isPlaying )
		{
			_sortFading.Stop();
			_sortFading.volume = 0f;
			_sortFadeRemaining = 0f;
			return;
		}

		float t = _sortFadeDuration > 0.0001f ? _sortFadeRemaining / _sortFadeDuration : 0f;
		_sortFading.volume = _sortFadeStartVolume * Mathf.Clamp01( t );
	}

	void StopSources()
	{
		if ( _current != null && _current.isPlaying )
			_current.Stop();
		if ( _fading != null && _fading.isPlaying )
			_fading.Stop();
		_fadeRemaining = 0f;
	}

	void StopSortSources()
	{
		if ( _sortCurrent != null && _sortCurrent.isPlaying )
			_sortCurrent.Stop();
		if ( _sortFading != null && _sortFading.isPlaying )
			_sortFading.Stop();
		_sortFadeRemaining = 0f;
	}

	void EnsureSources()
	{
		if ( _current == null )
			_current = CreateSource( "CrankAudioA" );
		if ( _fading == null )
			_fading = CreateSource( "CrankAudioB" );
		if ( _sortCurrent == null )
			_sortCurrent = CreateSource( "SortAudioA" );
		if ( _sortFading == null )
			_sortFading = CreateSource( "SortAudioB" );
	}

	AudioSource CreateSource( string hostName )
	{
		Transform existing = transform.Find( hostName );
		GameObject host = existing != null ? existing.gameObject : new GameObject( hostName );
		if ( existing == null )
			host.transform.SetParent( transform, false );

		AudioSource source = host.GetComponent<AudioSource>();
		if ( source == null )
			source = host.AddComponent<AudioSource>();

		source.playOnAwake = false;
		source.loop = false;
		source.spatialBlend = 1f;
		source.minDistance = 1.5f;
		source.maxDistance = 18f;
		source.volume = 1f;
		return source;
	}

	static bool HasClips( CoinSortingStationDefinition def )
	{
		return HasClipArray( def != null ? def.crankLoopClips : null );
	}

	static bool HasSortClips( CoinSortingStationDefinition def )
	{
		return HasClipArray( def != null ? def.sortLoopClips : null );
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

	static float SamplePitch( float min, float max )
	{
		float lo = Mathf.Clamp( Mathf.Min( min, max ), -3f, 3f );
		float hi = Mathf.Clamp( Mathf.Max( min, max ), -3f, 3f );
		float pitch = Mathf.Approximately( lo, hi ) ? lo : Random.Range( lo, hi );
		return pitch <= 0f ? 1f : pitch;
	}

	float ResolvePitch( CoinSortingStationDefinition def )
	{
		float lo = def != null ? def.crankPitchMin : 0.85f;
		float hi = def != null ? def.crankPitchMax : 1.25f;
		float t = _station != null ? _station.ReserveNormalized : 0f;
		float pitch = Mathf.Lerp( lo, hi, t );
		float warning = def != null ? def.gaugeEmptyWarningNormalized : 0.15f;
		if ( _station != null && t <= warning )
			pitch *= 0.88f;
		if ( pitch <= 0f )
			return 1f;
		return Mathf.Clamp( pitch, -3f, 3f );
	}

	float ResolveVolume( CoinSortingStationDefinition def )
	{
		float volume = def != null ? Mathf.Clamp01( def.crankVolume ) : 0.7f;
		float warning = def != null ? def.gaugeEmptyWarningNormalized : 0.15f;
		if ( _station != null && _station.ReserveNormalized <= warning )
			volume *= 0.55f;
		return volume;
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
