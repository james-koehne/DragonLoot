using System;

using UnityEngine;

namespace FeedbackSystem
{
	[Serializable]
	[FeedbackInfo( "GameObjects/Destroy" )]
	public class DestroyGameObjectFeedback : Feedback, IFeedbackTick
	{
		public GameObject Target;

		[Min( 0f )]
		public float Delay;

		float _elapsed;
		bool _running;

		public override void Play()
		{
			if ( Target == null )
				return;

			CancelPending();

			if ( Delay <= 0f )
			{
				UnityEngine.Object.Destroy( Target );
				return;
			}

			_elapsed = 0f;
			_running = true;
			RegisterTick( this );
		}

		public override void Stop()
		{
			CancelPending();
		}

		public bool Tick( float deltaTime )
		{
			if ( !_running )
				return false;

			_elapsed += deltaTime;
			if ( _elapsed < Delay )
				return true;

			_running = false;
			if ( Target != null )
				UnityEngine.Object.Destroy( Target );

			return false;
		}

		public void Cancel()
		{
			_running = false;
		}

		void CancelPending()
		{
			_running = false;
			UnregisterTick( this );
		}
	}
}
