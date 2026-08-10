using System;
using System.Collections.Generic;

using UnityEngine;

namespace FeedbackSystem
{
	[Serializable]
	[FeedbackInfo( "Composition/Parallel" )]
	public class ParallelFeedback : Feedback
	{
		[SerializeReference]
		public List<Feedback> Feedbacks = new List<Feedback>();

		public override void Initialize()
		{
			InitializeChildren( Feedbacks );
		}

		public override void Play()
		{
			if ( Feedbacks == null )
				return;

			FeedbackContext context = Context;
			for ( int i = 0; i < Feedbacks.Count; i++ )
			{
				Feedback child = Feedbacks[i];
				if ( child == null || !child.Enabled )
					continue;

				child.SetContext( context );
				child.Play();
			}
		}

		public override void Stop()
		{
			StopChildren( Feedbacks );
		}

		public override void Reset()
		{
			ResetChildren( Feedbacks );
		}

		public override float GetHoldDuration()
		{
			if ( Feedbacks == null )
				return 0f;

			float max = 0f;
			for ( int i = 0; i < Feedbacks.Count; i++ )
			{
				Feedback child = Feedbacks[i];
				if ( child == null || !child.Enabled )
					continue;

				float hold = child.GetHoldDuration();
				if ( hold > max )
					max = hold;
			}

			return max;
		}
	}
}
