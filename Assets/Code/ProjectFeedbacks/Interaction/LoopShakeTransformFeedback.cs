using System;

using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Constant transform rumble until <see cref="Feedback.Stop"/>. Used for machine processing.
/// </summary>
[Serializable]
[FeedbackInfo( "Transforms/Loop Shake Transform" )]
public class LoopShakeTransformFeedback : Feedback, IFeedbackTick
{
	public Transform Target;

	[Min( 0f )]
	public float Strength = 0.02f;

	[Min( 0f )]
	public float RotationScale = 18f;

	Vector3 _basePosition;
	Quaternion _baseRotation;
	bool _running;

	public override void Play()
	{
		if ( Target == null )
			return;

		Cancel( false );
		_basePosition = Target.localPosition;
		_baseRotation = Target.localRotation;
		_running = true;
		RegisterTick( this );
	}

	public override void Stop()
	{
		Cancel( true );
	}

	public override void Reset()
	{
		Cancel( true );
	}

	public override float GetHoldDuration()
	{
		return 3600f;
	}

	public bool Tick( float deltaTime )
	{
		if ( !_running || Target == null )
		{
			_running = false;
			return false;
		}

		Vector3 offset = UnityEngine.Random.insideUnitSphere * Strength;
		Target.localPosition = _basePosition + offset;
		Target.localRotation = _baseRotation * Quaternion.Euler( offset * RotationScale );
		return true;
	}

	void Cancel( bool restore )
	{
		if ( _running && restore )
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

	public void Cancel()
	{
		Cancel( true );
	}
}
