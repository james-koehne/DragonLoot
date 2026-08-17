using UnityEngine;

/// <summary>
/// Crank one-shots: play on press, then every <see cref="CoinSortingStationDefinition.crankPlayInterval"/>
/// while pulses continue. The previous clip fades out quickly so the new one can overlap.
/// </summary>
[DisallowMultipleComponent]
public class CoinSortingCrankAudio : MonoBehaviour
{
	[SerializeField]
	CoinSortingStation _station;

	AudioSource _current;
	AudioSource _fading;
	int _clipIndex = -1;
	float _nextPlayTime;
	float _fadeRemaining;
	float _fadeDuration;
	float _fadeStartVolume;

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
			return;
		}

		TickFade();
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
		_current.pitch = SamplePitch( def.crankPitchMin, def.crankPitchMax );
		_current.volume = Mathf.Clamp01( def.crankVolume );
		_current.Play();
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
			_current = CreateSource( "CrankAudioA" );
		if ( _fading == null )
			_fading = CreateSource( "CrankAudioB" );
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
		AudioClip[] clips = def != null ? def.crankLoopClips : null;
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
