using System;

using UnityEngine;

namespace FeedbackSystem
{
	[Serializable]
	[FeedbackInfo( "Utility/Delay" )]
	public class DelayFeedback : Feedback
	{
		[Min( 0f )]
		public float Duration = 0.2f;

		public override void Play()
		{
		}

		public override float GetHoldDuration()
		{
			return Duration < 0f ? 0f : Duration;
		}
	}
}
