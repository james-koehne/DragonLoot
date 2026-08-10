using System;

using UnityEngine;

namespace FeedbackSystem
{
	[Serializable]
	[FeedbackInfo( "Audio/Play Random SFX" )]
	public class PlayRandomSFXFeedback : Feedback
	{
		public AudioClip[] Clips;

		[Range( 0f, 1f )]
		public float Volume = 1f;

		[Range( -3f, 3f )]
		public float Pitch = 1f;

		public AudioSource AudioSource;

		public override void Play()
		{
			if ( Clips == null || Clips.Length == 0 )
				return;

			AudioClip clip = Clips[UnityEngine.Random.Range( 0, Clips.Length )];
			if ( clip == null )
				return;

			if ( AudioSource != null )
			{
				AudioSource.pitch = Pitch <= 0f ? 1f : Pitch;
				AudioSource.PlayOneShot( clip, Volume );
				return;
			}

			Vector3 position = Vector3.zero;
			if ( Owner != null )
				position = Owner.transform.position;

			FeedbackAudioPool.Play( clip, Volume, Pitch, position );
		}
	}
}
