using System;

using UnityEngine;

namespace FeedbackSystem
{
	[Serializable]
	[FeedbackInfo( "Transforms/Punch Rotation" )]
	public class PunchRotationFeedback : Feedback, IFeedbackTick
	{
		public Transform Target;
		public Vector3 Punch = new Vector3( 80f, 0f, 0f );

		[Min( 0f )]
		public float Duration = 0.2f;

		public AnimationCurve Curve = new AnimationCurve(
			new Keyframe( 0f, 0f ),
			new Keyframe( 0.5f, 1f ),
			new Keyframe( 1f, 0f ) );

		Quaternion _baseRotation;
		float _elapsed;
		bool _running;

		public override void Play()
		{
			if ( Target == null )
				return;

			Cancel();
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
				Target.localRotation = _baseRotation;
				_running = false;
				return false;
			}

			_elapsed += deltaTime;
			float t = _elapsed / Duration;
			if ( t >= 1f )
			{
				Target.localRotation = _baseRotation;
				_running = false;
				return false;
			}

			float curve = Curve != null ? Curve.Evaluate( t ) : Mathf.Sin( t * Mathf.PI );
			Target.localRotation = _baseRotation * Quaternion.Euler( Punch * curve );
			return true;
		}

		public void Cancel()
		{
			if ( _running && Target != null )
				Target.localRotation = _baseRotation;

			_running = false;
			UnregisterTick( this );
		}
	}
}
