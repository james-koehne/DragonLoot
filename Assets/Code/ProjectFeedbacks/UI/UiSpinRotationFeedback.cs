using System;

using FeedbackSystem;

using UnityEngine;

/// <summary>Finite local Z spin using unscaled time (UI / pause-safe).</summary>
[Serializable]
[FeedbackInfo( "UI/Spin Rotation" )]
public class UiSpinRotationFeedback : Feedback, IFeedbackTick
{
	public Transform Target;

	public float DegreesPerSecond = 160f;

	[Min( 0f )]
	public float Duration = 8f;

	public bool UseUnscaledTime = true;
	public bool RestoreOnComplete = true;

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
		if ( Duration > 0f && _elapsed >= Duration )
		{
			if ( RestoreOnComplete )
				Target.localRotation = _baseRotation;
			_running = false;
			return false;
		}

		Target.localRotation = _baseRotation * Quaternion.Euler( 0f, 0f, DegreesPerSecond * _elapsed );
		return true;
	}

	public void Cancel()
	{
		if ( _running && Target != null && RestoreOnComplete )
			Target.localRotation = _baseRotation;

		_running = false;
		UnregisterTick( this );
	}

	public override float GetHoldDuration()
	{
		return Duration;
	}
}
