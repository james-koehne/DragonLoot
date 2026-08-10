using System.Collections.Generic;

using UnityEngine;

namespace FeedbackSystem
{
	[DisallowMultipleComponent]
	[AddComponentMenu( "FeedbackSystem/Feedbacks" )]
	public class Feedbacks : MonoBehaviour
	{
		[SerializeReference]
		List<Feedback> _feedbacks = new List<Feedback>();

		[System.NonSerialized]
		FeedbackPlayer _player;

		[System.NonSerialized]
		FeedbackTicker _ticker;

		[System.NonSerialized]
		bool _initialized;

		public FeedbackTicker Ticker
		{
			get { return _ticker; }
		}

		public List<Feedback> FeedbackList
		{
			get { return _feedbacks; }
		}

		public bool IsPlaying
		{
			get { return _player != null && _player.IsPlaying; }
		}

		void Awake()
		{
			Initialize();
		}

		public void Initialize()
		{
			if ( _initialized )
				return;

			if ( _feedbacks == null )
				_feedbacks = new List<Feedback>();

			_ticker = new FeedbackTicker();

			for ( int i = 0; i < _feedbacks.Count; i++ )
			{
				Feedback feedback = _feedbacks[i];
				if ( feedback == null )
					continue;

				feedback.Bind( this );
				feedback.Initialize();
			}

			_player = new FeedbackPlayer( _feedbacks );
			_initialized = true;
		}

		public void AddFeedback( Feedback feedback )
		{
			if ( feedback == null )
				return;

			if ( _feedbacks == null )
				_feedbacks = new List<Feedback>();

			_feedbacks.Add( feedback );

			if ( !_initialized )
				return;

			feedback.Bind( this );
			feedback.Initialize();
		}

		public void Play()
		{
			Play( null );
		}

		public void Play( FeedbackContext context )
		{
			if ( !_initialized )
				Initialize();

			_player.Play( context );
		}

		public void Stop()
		{
			if ( _player != null )
				_player.Stop();

			if ( _ticker != null )
				_ticker.StopAll();
		}

		public void ResetFeedbacks()
		{
			if ( !_initialized )
				Initialize();

			if ( _player != null )
				_player.ResetAll();

			if ( _ticker != null )
				_ticker.StopAll();
		}

		public void Reset()
		{
			if ( !Application.isPlaying )
				return;

			ResetFeedbacks();
		}

		void Update()
		{
			bool playerActive = _player != null && _player.IsPlaying;
			bool tickerActive = _ticker != null && _ticker.HasActive;
			if ( !playerActive && !tickerActive )
				return;

			float deltaTime = Time.deltaTime;
			if ( tickerActive )
				_ticker.Tick( deltaTime );

			if ( playerActive )
				_player.Tick( deltaTime );
		}

		void OnDisable()
		{
			Stop();
		}

		void OnDestroy()
		{
			Stop();
		}
	}
}
