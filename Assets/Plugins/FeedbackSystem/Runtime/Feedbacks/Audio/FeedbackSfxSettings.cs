using System;

using UnityEngine;
using UnityEngine.Audio;

namespace FeedbackSystem
{
	[Serializable]
	public class FeedbackSfxSettings
	{
		[Tooltip( "Can exceed 1 for boost (e.g. 1.5 = +50%)." )]
		[Min( 0f )]
		public float VolumeMin = 1f;

		[Tooltip( "Can exceed 1 for boost (e.g. 1.5 = +50%)." )]
		[Min( 0f )]
		public float VolumeMax = 1f;

		[Range( -3f, 3f )]
		public float PitchMin = 1f;

		[Range( -3f, 3f )]
		public float PitchMax = 1f;

		[Range( 0f, 1f )]
		[Tooltip( "0 = 2D, 1 = full 3D at the play position." )]
		public float SpatialBlend;

		[Min( 0.01f )]
		public float MinDistance = 1f;

		[Min( 0.01f )]
		public float MaxDistance = 20f;

		[Range( 0f, 5f )]
		public float DopplerLevel;

		public AudioRolloffMode RolloffMode = AudioRolloffMode.Linear;

		[Tooltip( "When enabled, the AudioSource tracks Context.Source (or the Feedbacks owner) for the duration of playback." )]
		public bool FollowCallerTransform;

		public AudioMixerGroup OutputMixerGroup;

		[Tooltip( "Optional. When null, pooled one-shots are used." )]
		public AudioSource AudioSource;

		public void ApplyToSource( AudioSource source )
		{
			if ( source == null )
				return;

			source.spatialBlend = Mathf.Clamp01( SpatialBlend );
			source.minDistance = Mathf.Max( 0.01f, MinDistance );
			source.maxDistance = Mathf.Max( source.minDistance, MaxDistance );
			source.dopplerLevel = Mathf.Max( 0f, DopplerLevel );
			source.rolloffMode = RolloffMode;
			source.outputAudioMixerGroup = OutputMixerGroup;
		}
	}
}
