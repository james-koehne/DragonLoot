using System.Collections.Generic;

namespace FeedbackSystem
{
	public sealed class FeedbackPlayer
	{
		readonly List<Feedback> _list;

		int _index = -1;
		float _holdRemaining;
		bool _playing;
		FeedbackContext _context;

		public bool IsPlaying
		{
			get { return _playing; }
		}

		public FeedbackPlayer( List<Feedback> list )
		{
			_list = list;
		}

		public void Play( FeedbackContext context )
		{
			Stop();
			_context = context;
			_playing = true;
			_index = -1;
			_holdRemaining = 0f;
			Advance();
		}

		public void Stop()
		{
			_playing = false;
			_index = -1;
			_holdRemaining = 0f;

			if ( _list == null )
				return;

			for ( int i = 0; i < _list.Count; i++ )
			{
				Feedback feedback = _list[i];
				if ( feedback == null )
					continue;

				feedback.Stop();
			}
		}

		public void ResetAll()
		{
			Stop();

			if ( _list == null )
				return;

			for ( int i = 0; i < _list.Count; i++ )
			{
				Feedback feedback = _list[i];
				if ( feedback == null )
					continue;

				feedback.Reset();
			}
		}

		public void Tick( float deltaTime )
		{
			if ( !_playing )
				return;

			if ( _holdRemaining > 0f )
			{
				_holdRemaining -= deltaTime;
				if ( _holdRemaining > 0f )
					return;
			}

			Advance();
		}

		void Advance()
		{
			if ( _list == null )
			{
				_playing = false;
				return;
			}

			while ( _playing )
			{
				_index++;
				if ( _index >= _list.Count )
				{
					_playing = false;
					_index = -1;
					return;
				}

				Feedback feedback = _list[_index];
				if ( feedback == null || !feedback.Enabled )
					continue;

				feedback.SetContext( _context );
				feedback.Play();

				float hold = feedback.GetHoldDuration();
				if ( hold > 0f )
				{
					_holdRemaining = hold;
					return;
				}
			}
		}
	}
}
