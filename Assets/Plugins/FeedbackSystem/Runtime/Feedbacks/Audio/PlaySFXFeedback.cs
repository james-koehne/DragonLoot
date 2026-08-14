using System;

using UnityEngine;
using UnityEngine.Serialization;

namespace FeedbackSystem
{
	[Serializable]
	[FeedbackInfo( "Audio/Play SFX" )]
	public class PlaySFXFeedback : Feedback
	{
		public AudioClip Clip;

		[Range( 0f, 1f )]
		public float VolumeMin = 1f;

		[Range( 0f, 1f )]
		[FormerlySerializedAs( "Volume" )]
		public float VolumeMax = 1f;

		[Range( -3f, 3f )]
		public float PitchMin = 1f;

		[Range( -3f, 3f )]
		[FormerlySerializedAs( "Pitch" )]
		public float PitchMax = 1f;

		[Range( 0f, 1f )]
		[Tooltip( "0 = 2D, 1 = full 3D at the play position." )]
		public float SpatialBlend;

		[Min( 0.01f )]
		public float MinDistance = 1f;

		[Min( 0.01f )]
		public float MaxDistance = 20f;

		public AudioSource AudioSource;

		public override void Play()
		{
			FeedbackSfxPlayback.Play(
				Clip,
				VolumeMin,
				VolumeMax,
				PitchMin,
				PitchMax,
				AudioSource,
				ResolvePosition(),
				SpatialBlend,
				MinDistance,
				MaxDistance );
		}

		Vector3 ResolvePosition()
		{
			if ( Context != null )
				return Context.Position;

			if ( Owner != null )
				return Owner.transform.position;

			return Vector3.zero;
		}
	}
}
