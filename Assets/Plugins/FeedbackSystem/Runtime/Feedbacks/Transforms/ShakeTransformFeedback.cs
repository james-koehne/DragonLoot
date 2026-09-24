using System;

using UnityEngine;

namespace FeedbackSystem
{
	[Serializable]
	[FeedbackInfo( "Transforms/Shake Transform" )]
	public class ShakeTransformFeedback : Feedback, IFeedbackTick
	{
		public Transform Target;

		[Min( 0f )]
		public float Duration = 0.3f;

		public float Strength = 0.15f;

		Vector3 _basePosition;
		Quaternion _baseRotation;
		float _elapsed;
		int _anchorId;
		bool _running;

		public override void Play()
		{
			if ( Target == null )
				return;

			if ( _running )
			{
				_elapsed = 0f;
				return;
			}

			_anchorId = TransformShakeAnchor.Retain( Target, out _basePosition, out _baseRotation );
			_elapsed = 0f;
			_running = true;
			RegisterTick( this );
		}

		public override void Stop()
		{
			Cancel();
		}

		public override void Reset()
		{
			Cancel();
		}

		public bool Tick( float deltaTime )
		{
			if ( !_running || Target == null )
			{
				End( false );
				return false;
			}

			if ( Duration <= 0f )
			{
				End( true );
				return false;
			}

			_elapsed += deltaTime;
			float t = _elapsed / Duration;
			if ( t >= 1f )
			{
				End( true );
				return false;
			}

			float damper = 1f - t;
			Vector3 offset = UnityEngine.Random.insideUnitSphere * ( Strength * damper );
			Target.localPosition = _basePosition + offset;
			Target.localRotation = _baseRotation * Quaternion.Euler( offset * 40f );
			return true;
		}

		public void Cancel()
		{
			End( true );
		}

		void End( bool restore )
		{
			if ( !_running )
				return;

			_running = false;
			TransformShakeAnchor.Release( _anchorId, Target, restore );
			UnregisterTick( this );
		}
	}
}
