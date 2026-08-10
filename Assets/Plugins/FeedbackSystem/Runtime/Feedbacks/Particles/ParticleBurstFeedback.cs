using System;

using UnityEngine;

namespace FeedbackSystem
{
	[Serializable]
	[FeedbackInfo( "Particles/Particle Burst" )]
	public class ParticleBurstFeedback : Feedback
	{
		public ParticleSystem ParticleSystem;

		[Min( 1 )]
		public int Count = 16;

		public override void Play()
		{
			if ( ParticleSystem == null )
				return;

			ParticleSystem.Emit( Count );
		}
	}
}
