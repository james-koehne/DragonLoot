using System;
using System.Collections.Generic;

using UnityEngine;

namespace FeedbackSystem
{
	[Serializable]
	[FeedbackInfo( "Composition/Sequence" )]
	public class SequenceFeedback : Feedback, IFeedbackTick
	{
		[SerializeReference]
		public List<Feedback> Feedbacks = new List<Feedback>();

		FeedbackPlayer _player;
		bool _registered;

		public override void Initialize()
		{
			InitializeChildren( Feedbacks );
			_player = new FeedbackPlayer( Feedbacks );
		}

		public override void Play()
		{
			if ( _player == null )
				_player = new FeedbackPlayer( Feedbacks );

			_player.Play( Context );
			if ( _player.IsPlaying )
			{
				_registered = true;
				RegisterTick( this );
			}
		}

		public override void Stop()
		{
			if ( _player != null )
				_player.Stop();

			if ( _registered )
			{
				_registered = false;
				UnregisterTick( this );
			}
		}

		public override void Reset()
		{
			if ( _player != null )
				_player.ResetAll();
			else
				ResetChildren( Feedbacks );
		}

		public override float GetHoldDuration()
		{
			if ( Feedbacks == null )
				return 0f;

			float total = 0f;
			for ( int i = 0; i < Feedbacks.Count; i++ )
			{
				Feedback child = Feedbacks[i];
				if ( child == null || !child.Enabled )
					continue;

				total += child.GetHoldDuration();
			}

			return total;
		}

		public bool Tick( float deltaTime )
		{
			if ( _player == null || !_player.IsPlaying )
			{
				_registered = false;
				return false;
			}

			_player.Tick( deltaTime );
			if ( _player.IsPlaying )
				return true;

			_registered = false;
			return false;
		}

		public void Cancel()
		{
			_registered = false;
			if ( _player != null )
				_player.Stop();
		}
	}
}
