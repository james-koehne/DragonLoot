using System.Collections.Generic;

using UnityEngine;

namespace FeedbackSystem
{
	/// <summary>
	/// Tracks active one-shot sources started by a feedback so <see cref="Feedback.Stop"/> can cut them.
	/// </summary>
	public sealed class FeedbackOneShotVoice
	{
		readonly List<AudioSource> _active = new List<AudioSource>( 4 );

		public void Register( AudioSource source )
		{
			if ( source == null )
				return;

			if ( !_active.Contains( source ) )
				_active.Add( source );
		}

		public void StopAll()
		{
			for ( int i = 0; i < _active.Count; i++ )
			{
				AudioSource source = _active[i];
				if ( source == null )
					continue;

				if ( source.isPlaying )
					source.Stop();

				FeedbackAudioFollower.Detach( source );
			}

			_active.Clear();
		}
	}
}
