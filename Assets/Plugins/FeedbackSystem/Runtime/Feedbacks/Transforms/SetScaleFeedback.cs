using System;

using UnityEngine;

namespace FeedbackSystem
{
	[Serializable]
	[FeedbackInfo( "Transforms/Set Scale" )]
	public class SetScaleFeedback : Feedback
	{
		public Transform Target;
		public Vector3 Scale = Vector3.one;

		public override void Play()
		{
			if ( Target == null )
				return;

			Target.localScale = Scale;
		}
	}
}
