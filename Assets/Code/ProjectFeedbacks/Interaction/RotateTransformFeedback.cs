using System;

using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Lerps a transform rotation over time. Use per door leaf in open/close feedback chains.
/// </summary>
[Serializable]
[FeedbackInfo( "Transforms/Rotate Transform" )]
public class RotateTransformFeedback : Feedback, IFeedbackTick
{
	public Transform Target;
	public Vector3 TargetEuler;

	[Min( 0f )]
	public float Duration = 0.4f;

	public AnimationCurve Curve = AnimationCurve.EaseInOut( 0f, 0f, 1f, 1f );
	public bool WorldSpace;

	Quaternion _startRotation;
	float _elapsed;
	bool _running;

	public override void Play()
	{
		if ( Target == null || Duration <= 0f )
		{
			ApplyRotation( TargetEuler );
			return;
		}

		CancelMotion( false );
		_startRotation = WorldSpace ? Target.rotation : Target.localRotation;
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
			ApplyRotation( TargetEuler );
			_running = false;
			return false;
		}

		float curve = Curve != null ? Curve.Evaluate( t ) : t;
		Quaternion target = Quaternion.Euler( TargetEuler );
		ApplyRotationQuaternion( Quaternion.SlerpUnclamped( _startRotation, target, curve ) );
		return true;
	}

	public void Cancel()
	{
		_running = false;
	}

	public override float GetHoldDuration()
	{
		return Duration;
	}

	void CancelMotion( bool snapToEnd )
	{
		if ( _running && snapToEnd )
			ApplyRotation( TargetEuler );

		_running = false;
		UnregisterTick( this );
	}

	void ApplyRotation( Vector3 euler )
	{
		ApplyRotationQuaternion( Quaternion.Euler( euler ) );
	}

	void ApplyRotationQuaternion( Quaternion rotation )
	{
		if ( Target == null )
			return;

		if ( WorldSpace )
			Target.rotation = rotation;
		else
			Target.localRotation = rotation;
	}
}
