using System;

using FeedbackSystem;

using UnityEngine;

/// <summary>Punch local Z rotation using unscaled time (UI / pause-safe).</summary>
[Serializable]
[FeedbackInfo( "UI/Punch Rotation" )]
public class UiPunchRotationFeedback : Feedback, IFeedbackTick
{
	public Transform Target;
	public Vector3 PunchEuler = new Vector3( 0f, 0f, 4f );

	[Min( 0f )]
	public float Duration = 0.22f;

	public AnimationCurve Curve = new AnimationCurve(
		new Keyframe( 0f, 0f ),
		new Keyframe( 0.4f, 1f ),
		new Keyframe( 1f, 0f ) );

	public bool UseUnscaledTime = true;

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

		float dt = UseUnscaledTime ? Time.unscaledDeltaTime : deltaTime;
		_elapsed += dt;
		float t = Duration > 0f ? _elapsed / Duration : 1f;
		if ( t >= 1f )
		{
			Target.localRotation = _baseRotation;
			_running = false;
			return false;
		}

		float curve = Curve != null ? Curve.Evaluate( t ) : Mathf.Sin( t * Mathf.PI );
		Target.localRotation = _baseRotation * Quaternion.Euler( PunchEuler * curve );
		return true;
	}

	public void Cancel()
	{
		if ( _running && Target != null )
			Target.localRotation = _baseRotation;

		_running = false;
		UnregisterTick( this );
	}

	public override float GetHoldDuration()
	{
		return Duration;
	}
}
