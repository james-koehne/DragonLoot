using System;

using UnityEngine;

namespace FeedbackSystem
{
	[Serializable]
	[FeedbackInfo( "Transforms/Set Position" )]
	public class SetPositionFeedback : Feedback
	{
		public Transform Target;
		public Vector3 Position;
		public bool WorldSpace = true;

		public override void Play()
		{
			if ( Target == null )
				return;

			if ( WorldSpace )
				Target.position = Position;
			else
				Target.localPosition = Position;
		}
	}
}
