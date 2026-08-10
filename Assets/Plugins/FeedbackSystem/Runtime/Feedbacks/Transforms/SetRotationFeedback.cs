using System;

using UnityEngine;

namespace FeedbackSystem
{
	[Serializable]
	[FeedbackInfo( "Transforms/Set Rotation" )]
	public class SetRotationFeedback : Feedback
	{
		public Transform Target;
		public Vector3 Rotation;
		public bool WorldSpace = true;

		public override void Play()
		{
			if ( Target == null )
				return;

			if ( WorldSpace )
				Target.rotation = Quaternion.Euler( Rotation );
			else
				Target.localRotation = Quaternion.Euler( Rotation );
		}
	}
}
