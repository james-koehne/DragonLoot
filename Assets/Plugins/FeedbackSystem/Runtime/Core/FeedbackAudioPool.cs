using UnityEngine;

namespace FeedbackSystem
{
	public static class FeedbackAudioPool
	{
		const int InitialSize = 8;
		const int GrowSize = 4;
		const string RootName = "[FeedbackSystem AudioPool]";

		static AudioSource[] _sources;
		static Transform _root;
		static int _next;

		public static void Play( AudioClip clip, float volume, float pitch, Vector3 position )
		{
			Play( clip, volume, pitch, position, spatialBlend: 0f, minDistance: 1f, maxDistance: 20f );
		}

		public static void Play(
			AudioClip clip,
			float volume,
			float pitch,
			Vector3 position,
			float spatialBlend,
			float minDistance,
			float maxDistance )
		{
			if ( clip == null )
				return;

			Ensure();
			AudioSource source = NextSource();
			if ( source == null )
				return;

			source.transform.position = position;
			source.spatialBlend = Mathf.Clamp01( spatialBlend );
			source.minDistance = Mathf.Max( 0.01f, minDistance );
			source.maxDistance = Mathf.Max( source.minDistance, maxDistance );
			source.pitch = pitch <= 0f ? 1f : pitch;
			source.PlayOneShot( clip, volume );
		}

		static void Ensure()
		{
			if ( _root != null && _sources != null )
				return;

			GameObject rootObject = new GameObject( RootName );
			Object.DontDestroyOnLoad( rootObject );
			_root = rootObject.transform;

			_sources = new AudioSource[InitialSize];
			for ( int i = 0; i < InitialSize; i++ )
				_sources[i] = CreateSource( i );

			_next = 0;
		}

		static AudioSource CreateSource( int index )
		{
			GameObject sourceObject = new GameObject( "OneShot " + index );
			sourceObject.transform.SetParent( _root, false );
			AudioSource source = sourceObject.AddComponent<AudioSource>();
			source.playOnAwake = false;
			source.spatialBlend = 0f;
			source.rolloffMode = AudioRolloffMode.Linear;
			source.minDistance = 1f;
			source.maxDistance = 20f;
			source.volume = 1f;
			return source;
		}

		static AudioSource NextSource()
		{
			if ( _sources == null )
				return null;

			for ( int i = 0; i < _sources.Length; i++ )
			{
				int index = ( _next + i ) % _sources.Length;
				AudioSource source = _sources[index];
				if ( source != null && !source.isPlaying )
				{
					_next = ( index + 1 ) % _sources.Length;
					return source;
				}
			}

			int oldLength = _sources.Length;
			AudioSource[] grown = new AudioSource[oldLength + GrowSize];
			for ( int i = 0; i < oldLength; i++ )
				grown[i] = _sources[i];

			for ( int i = oldLength; i < grown.Length; i++ )
				grown[i] = CreateSource( i );

			_sources = grown;
			_next = oldLength + 1;
			return _sources[oldLength];
		}
	}
}
