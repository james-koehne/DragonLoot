using System;

using UnityEngine;

namespace FeedbackSystem
{
	[Serializable]
	[FeedbackInfo( "Audio/Play SFX" )]
	public class PlaySFXFeedback : Feedback
	{
		public AudioClip Clip;

		[Range( 0f, 1f )]
		public float Volume = 1f;

		[Range( -3f, 3f )]
		public float Pitch = 1f;

		public AudioSource AudioSource;

		public override void Play()
		{
			if ( Clip == null )
				return;

			if ( AudioSource != null )
			{
				AudioSource.pitch = Pitch <= 0f ? 1f : Pitch;
				AudioSource.PlayOneShot( Clip, Volume );
				return;
			}

			Vector3 position = Vector3.zero;
			if ( Owner != null )
				position = Owner.transform.position;

			FeedbackAudioPool.Play( Clip, Volume, Pitch, position );
		}
	}
}
