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
		bool _running;

		public override void Play()
		{
			if ( Target == null )
				return;

			Cancel();
			_basePosition = Target.localPosition;
			_baseRotation = Target.localRotation;
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
				_running = false;
				return false;
			}

			if ( Duration <= 0f )
			{
				Restore();
				_running = false;
				return false;
			}

			_elapsed += deltaTime;
			float t = _elapsed / Duration;
			if ( t >= 1f )
			{
				Restore();
				_running = false;
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
			if ( _running )
				Restore();

			_running = false;
			UnregisterTick( this );
		}

		void Restore()
		{
			if ( Target == null )
				return;

			Target.localPosition = _basePosition;
			Target.localRotation = _baseRotation;
		}
	}
}
