using UnityEngine;

/// <summary>
/// Plays music and ambience playlists from <see cref="AudioDefinition"/> simultaneously.
/// Music starts at a random track each session; ambience starts at index 0. Both wrap forever.
/// Each bed can replay a clip N times before advancing, with optional dual-source crossfade.
/// </summary>
public class MusicAmbienceSystem : MonoBehaviour
{
	struct BedChannel
	{
		public AudioSource Primary;
		public AudioSource Secondary;
		public int Index;
		public int PlayIndex;
		public bool Crossfading;
		public float CrossfadeElapsed;
		public float CrossfadeDuration;
	}

	static MusicAmbienceSystem _instance;

	[SerializeField]
	[Tooltip( "Optional override. When null, resolves AudioDefinition via Addressables." )]
	AudioDefinition _definition;

	BedChannel _music;
	BedChannel _ambience;
	bool _started;

	public static MusicAmbienceSystem Instance => _instance;

	AudioDefinition Definition => RuntimeDefinition.Resolve( ref _definition );

	public static MusicAmbienceSystem EnsureExists()
	{
		if ( _instance != null )
			return _instance;

		GameObject go = new GameObject( "MusicAmbienceSystem" );
		_instance = go.AddComponent<MusicAmbienceSystem>();
		Object.DontDestroyOnLoad( go );
		return _instance;
	}

	void Awake()
	{
		if ( _instance != null && _instance != this )
		{
			Destroy( gameObject );
			return;
		}

		_instance = this;
		EnsureSources();
		AudioMaster.Apply();
	}

	void OnDestroy()
	{
		if ( _instance == this )
			_instance = null;

		AudioMaster.FlushPersist();
	}

	void Start()
	{
		AudioMaster.Apply();
		BeginPlaylists();
	}

	void Update()
	{
		AudioMaster.Tick();

		if ( !_started )
			BeginPlaylists();

		AudioDefinition def = Definition;
		if ( def == null )
			return;

		TickBed(
			ref _music,
			def.musicTracks,
			ResolveBedVolume( def.musicVolume, AudioChannel.Music ),
			Mathf.Max( 1, def.musicPlaysPerTrack ),
			Mathf.Max( 0f, def.musicCrossfadeSeconds ) );

		TickBed(
			ref _ambience,
			def.ambienceTracks,
			ResolveBedVolume( def.ambienceVolume, AudioChannel.Ambience ),
			Mathf.Max( 1, def.ambiencePlaysPerTrack ),
			Mathf.Max( 0f, def.ambienceCrossfadeSeconds ) );
	}

	void BeginPlaylists()
	{
		AudioDefinition def = Definition;
		if ( def == null )
			return;

		EnsureSources();
		_started = true;

		_music.Index = PickStartIndex( def.musicTracks, randomStart: true );
		_music.PlayIndex = 0;
		_music.Crossfading = false;

		_ambience.Index = PickStartIndex( def.ambienceTracks, randomStart: false );
		_ambience.PlayIndex = 0;
		_ambience.Crossfading = false;

		if ( _music.Index >= 0 )
			PlayImmediate( ref _music, def.musicTracks[_music.Index], ResolveBedVolume( def.musicVolume, AudioChannel.Music ) );

		if ( _ambience.Index >= 0 )
			PlayImmediate( ref _ambience, def.ambienceTracks[_ambience.Index], ResolveBedVolume( def.ambienceVolume, AudioChannel.Ambience ) );
	}

	void TickBed(
		ref BedChannel bed,
		AudioClip[] tracks,
		float targetVolume,
		int playsPerTrack,
		float crossfade )
	{
		if ( bed.Primary == null || bed.Secondary == null )
			return;

		if ( tracks == null || tracks.Length == 0 || bed.Index < 0 )
			return;

		if ( bed.Crossfading )
		{
			TickCrossfade( ref bed, targetVolume );
			return;
		}

		if ( !bed.Primary.isPlaying )
		{
			HandleClipEnded( ref bed, tracks, targetVolume, playsPerTrack );
			return;
		}

		bed.Primary.volume = targetVolume;

		bool onLastPlay = bed.PlayIndex >= playsPerTrack - 1;
		if ( !onLastPlay || crossfade <= 0.0001f )
			return;

		if ( bed.Primary.clip == null || bed.Primary.clip.length <= crossfade )
			return;

		float remaining = bed.Primary.clip.length - bed.Primary.time;
		if ( remaining <= crossfade )
			BeginCrossfade( ref bed, tracks, crossfade );
	}

	void HandleClipEnded( ref BedChannel bed, AudioClip[] tracks, float targetVolume, int playsPerTrack )
	{
		bed.PlayIndex++;
		if ( bed.PlayIndex < playsPerTrack )
		{
			AudioClip same = tracks[bed.Index];
			if ( same != null )
				PlayImmediate( ref bed, same, targetVolume );
			return;
		}

		int next = NextValidIndex( tracks, bed.Index );
		if ( next < 0 )
		{
			bed.Index = -1;
			return;
		}

		bed.Index = next;
		bed.PlayIndex = 0;
		PlayImmediate( ref bed, tracks[bed.Index], targetVolume );
	}

	void BeginCrossfade( ref BedChannel bed, AudioClip[] tracks, float crossfade )
	{
		int next = NextValidIndex( tracks, bed.Index );
		if ( next < 0 )
			return;

		if ( next == bed.Index && CountValid( tracks ) <= 1 )
			return;

		AudioClip nextClip = tracks[next];
		if ( nextClip == null )
			return;

		bed.Index = next;
		bed.PlayIndex = 0;

		bed.Secondary.clip = nextClip;
		bed.Secondary.loop = false;
		bed.Secondary.volume = 0f;
		bed.Secondary.Play();

		bed.Crossfading = true;
		bed.CrossfadeElapsed = 0f;
		bed.CrossfadeDuration = crossfade;
	}

	void TickCrossfade( ref BedChannel bed, float targetVolume )
	{
		if ( bed.CrossfadeDuration <= 0.0001f )
		{
			FinishCrossfade( ref bed, targetVolume );
			return;
		}

		bed.CrossfadeElapsed += Time.unscaledDeltaTime;
		float t = Mathf.Clamp01( bed.CrossfadeElapsed / bed.CrossfadeDuration );
		bed.Primary.volume = targetVolume * ( 1f - t );
		bed.Secondary.volume = targetVolume * t;

		if ( t < 1f )
			return;

		FinishCrossfade( ref bed, targetVolume );
	}

	static void FinishCrossfade( ref BedChannel bed, float targetVolume )
	{
		bed.Primary.Stop();
		bed.Primary.volume = 0f;

		AudioSource swap = bed.Primary;
		bed.Primary = bed.Secondary;
		bed.Secondary = swap;

		bed.Primary.volume = targetVolume;
		bed.Crossfading = false;
		bed.CrossfadeElapsed = 0f;
		bed.CrossfadeDuration = 0f;
	}

	static float ResolveBedVolume( float designerVolume, AudioChannel channel )
	{
		return Mathf.Clamp01( designerVolume ) * AudioMaster.GetChannelGain( channel );
	}

	static void PlayImmediate( ref BedChannel bed, AudioClip clip, float targetVolume )
	{
		if ( bed.Primary == null || clip == null )
			return;

		bed.Primary.clip = clip;
		bed.Primary.loop = false;
		bed.Primary.volume = targetVolume;
		bed.Primary.Play();
	}

	static int PickStartIndex( AudioClip[] tracks, bool randomStart )
	{
		if ( tracks == null || tracks.Length == 0 )
			return -1;

		int count = CountValid( tracks );
		if ( count <= 0 )
			return -1;

		if ( !randomStart )
			return NextValidIndex( tracks, -1 );

		int pick = UnityEngine.Random.Range( 0, count );
		int seen = 0;
		for ( int i = 0; i < tracks.Length; i++ )
		{
			if ( tracks[i] == null )
				continue;
			if ( seen == pick )
				return i;
			seen++;
		}

		return NextValidIndex( tracks, -1 );
	}

	static int CountValid( AudioClip[] tracks )
	{
		int count = 0;
		for ( int i = 0; i < tracks.Length; i++ )
		{
			if ( tracks[i] != null )
				count++;
		}

		return count;
	}

	static int NextValidIndex( AudioClip[] tracks, int current )
	{
		if ( tracks == null || tracks.Length == 0 )
			return -1;

		int start = current < 0 ? 0 : ( current + 1 ) % tracks.Length;
		for ( int offset = 0; offset < tracks.Length; offset++ )
		{
			int i = ( start + offset ) % tracks.Length;
			if ( tracks[i] != null )
				return i;
		}

		return -1;
	}

	void EnsureSources()
	{
		if ( _music.Primary == null )
			_music.Primary = CreateSource( "MusicA" );
		if ( _music.Secondary == null )
			_music.Secondary = CreateSource( "MusicB" );
		if ( _ambience.Primary == null )
			_ambience.Primary = CreateSource( "AmbienceA" );
		if ( _ambience.Secondary == null )
			_ambience.Secondary = CreateSource( "AmbienceB" );
	}

	AudioSource CreateSource( string name )
	{
		GameObject child = new GameObject( name );
		child.transform.SetParent( transform, false );
		AudioSource source = child.AddComponent<AudioSource>();
		source.playOnAwake = false;
		source.loop = false;
		source.spatialBlend = 0f;
		source.volume = 1f;
		return source;
	}
}
