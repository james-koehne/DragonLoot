using System;

using UnityEngine;

namespace FeedbackSystem
{
	[Serializable]
	[FeedbackInfo( "GameObjects/Set Active" )]
	public class SetActiveFeedback : Feedback
	{
		public GameObject Target;
		public bool Active = true;

		public override void Play()
		{
			if ( Target == null )
				return;

			Target.SetActive( Active );
		}
	}
}
