using System;

using UnityEngine;

namespace FeedbackSystem
{
	[Serializable]
	[FeedbackInfo( "Transforms/Move Transform" )]
	public class MoveTransformFeedback : Feedback, IFeedbackTick
	{
		public Transform Target;
		public Vector3 TargetPosition;

		[Min( 0f )]
		public float Duration = 0.25f;

		public AnimationCurve Curve = AnimationCurve.Linear( 0f, 0f, 1f, 1f );
		public bool WorldSpace = true;

		Vector3 _startPosition;
		float _elapsed;
		bool _running;

		public override void Play()
		{
			if ( Target == null || Duration <= 0f )
			{
				ApplyPosition( TargetPosition );
				return;
			}

			CancelMotion( false );
			_startPosition = WorldSpace ? Target.position : Target.localPosition;
			_elapsed = 0f;
			_running = true;
			RegisterTick( this );
		}

		public override void Stop()
		{
			CancelMotion( false );
		}

		public bool Tick( float deltaTime )
		{
			if ( !_running || Target == null )
			{
				_running = false;
				return false;
			}

			_elapsed += deltaTime;
			float t = _elapsed / Duration;
			if ( t >= 1f )
			{
				ApplyPosition( TargetPosition );
				_running = false;
				return false;
			}

			float curve = Curve != null ? Curve.Evaluate( t ) : t;
			ApplyPosition( Vector3.LerpUnclamped( _startPosition, TargetPosition, curve ) );
			return true;
		}

		public void Cancel()
		{
			_running = false;
		}

		void CancelMotion( bool snapToEnd )
		{
			if ( _running && snapToEnd )
				ApplyPosition( TargetPosition );

			_running = false;
			UnregisterTick( this );
		}

		void ApplyPosition( Vector3 position )
		{
			if ( Target == null )
				return;

			if ( WorldSpace )
				Target.position = position;
			else
				Target.localPosition = position;
		}
	}
}
