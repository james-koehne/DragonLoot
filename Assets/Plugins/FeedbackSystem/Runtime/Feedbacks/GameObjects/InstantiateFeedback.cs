using System;

using UnityEngine;

namespace FeedbackSystem
{
	[Serializable]
	[FeedbackInfo( "GameObjects/Instantiate" )]
	public class InstantiateFeedback : Feedback
	{
		public GameObject Prefab;
		public Transform Parent;
		public Vector3 Position;
		public Vector3 Rotation;

		public override void Play()
		{
			if ( Prefab == null )
				return;

			Quaternion rotation = Quaternion.Euler( Rotation );
			if ( Parent != null )
				UnityEngine.Object.Instantiate( Prefab, Position, rotation, Parent );
			else
				UnityEngine.Object.Instantiate( Prefab, Position, rotation );
		}
	}
}
