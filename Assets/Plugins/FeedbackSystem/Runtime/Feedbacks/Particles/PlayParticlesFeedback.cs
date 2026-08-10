using System;

using UnityEngine;

namespace FeedbackSystem
{
	[Serializable]
	[FeedbackInfo( "Particles/Play Particles" )]
	public class PlayParticlesFeedback : Feedback
	{
		public ParticleSystem ParticleSystem;

		public override void Play()
		{
			if ( ParticleSystem == null )
				return;

			ParticleSystem.Play();
		}

		public override void Stop()
		{
			if ( ParticleSystem == null )
				return;

			ParticleSystem.Stop( true, ParticleSystemStopBehavior.StopEmitting );
		}
	}
}
