using System;

using UnityEngine;
using UnityEngine.Serialization;

namespace FeedbackSystem
{
	[Serializable]
	[FeedbackInfo( "Audio/Play SFX" )]
	public class PlaySFXFeedback : Feedback, ISerializationCallbackReceiver
	{
		public AudioClip Clip;

		public FeedbackSfxSettings Playback = new FeedbackSfxSettings();

		[FormerlySerializedAs( "VolumeMin" )]
		[SerializeField, HideInInspector]
		float _migrateVolumeMin = 1f;

		[FormerlySerializedAs( "VolumeMax" )]
		[FormerlySerializedAs( "Volume" )]
		[SerializeField, HideInInspector]
		float _migrateVolumeMax = 1f;

		[FormerlySerializedAs( "PitchMin" )]
		[SerializeField, HideInInspector]
		float _migratePitchMin = 1f;

		[FormerlySerializedAs( "PitchMax" )]
		[FormerlySerializedAs( "Pitch" )]
		[SerializeField, HideInInspector]
		float _migratePitchMax = 1f;

		[FormerlySerializedAs( "SpatialBlend" )]
		[SerializeField, HideInInspector]
		float _migrateSpatialBlend;

		[FormerlySerializedAs( "MinDistance" )]
		[SerializeField, HideInInspector]
		float _migrateMinDistance = 1f;

		[FormerlySerializedAs( "MaxDistance" )]
		[SerializeField, HideInInspector]
		float _migrateMaxDistance = 20f;

		[FormerlySerializedAs( "FollowCallerTransform" )]
		[SerializeField, HideInInspector]
		bool _migrateFollowCallerTransform;

		[FormerlySerializedAs( "AudioSource" )]
		[SerializeField, HideInInspector]
		AudioSource _migrateAudioSource;

		[SerializeField, HideInInspector]
		bool _serializedPlaybackMerged;

		[NonSerialized]
		FeedbackOneShotVoice _voice = new FeedbackOneShotVoice();

		public override void Play()
		{
			GameObject ownerGo = Owner != null ? Owner.gameObject : null;
			FeedbackSfxPlayback.Play(
				Clip,
				Playback,
				FeedbackSfxResolve.ResolvePosition( Context, ownerGo ),
				FeedbackSfxResolve.ResolveFollowTransform( Playback.FollowCallerTransform, Context, ownerGo ),
				_voice );
		}

		public override void Stop()
		{
			_voice.StopAll();
		}

		public override void Reset()
		{
			_voice.StopAll();
		}

		public void OnBeforeSerialize()
		{
			MergeMigrateIntoPlayback();
		}

		public void OnAfterDeserialize()
		{
			MergeMigrateIntoPlayback();
		}

		void MergeMigrateIntoPlayback()
		{
			if ( _serializedPlaybackMerged )
				return;

			if ( Playback == null )
				Playback = new FeedbackSfxSettings();

			if ( NeedsFlatFieldMigration() )
			{
				Playback.VolumeMin = _migrateVolumeMin;
				Playback.VolumeMax = _migrateVolumeMax;
				Playback.PitchMin = _migratePitchMin;
				Playback.PitchMax = _migratePitchMax;
				Playback.SpatialBlend = _migrateSpatialBlend;
				Playback.MinDistance = _migrateMinDistance;
				Playback.MaxDistance = _migrateMaxDistance;
				Playback.FollowCallerTransform = _migrateFollowCallerTransform;
				Playback.AudioSource = _migrateAudioSource;
			}

			_serializedPlaybackMerged = true;
		}

		bool NeedsFlatFieldMigration()
		{
			// ReferenceEquals: OnAfterDeserialize can run off the main thread; Unity Object.!= is unsafe there.
			if ( !ReferenceEquals( _migrateAudioSource, null ) || _migrateFollowCallerTransform || _migrateSpatialBlend > 0f )
				return true;

			if ( !Mathf.Approximately( _migrateVolumeMin, 1f ) || !Mathf.Approximately( _migrateVolumeMax, 1f ) )
				return true;

			if ( !Mathf.Approximately( _migratePitchMin, 1f ) || !Mathf.Approximately( _migratePitchMax, 1f ) )
				return true;

			if ( !Mathf.Approximately( _migrateMinDistance, 1f ) || !Mathf.Approximately( _migrateMaxDistance, 20f ) )
				return true;

			return false;
		}
	}
}
