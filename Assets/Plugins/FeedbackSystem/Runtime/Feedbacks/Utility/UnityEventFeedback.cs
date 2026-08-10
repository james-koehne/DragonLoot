using System;

using UnityEngine.Events;

namespace FeedbackSystem
{
	[Serializable]
	[FeedbackInfo( "Utility/Unity Event" )]
	public class UnityEventFeedback : Feedback
	{
		public UnityEvent Event = new UnityEvent();

		public override void Play()
		{
			if ( Event == null )
				return;

			Event.Invoke();
		}
	}
}
