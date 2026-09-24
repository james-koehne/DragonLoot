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
	int _anchorId;
	bool _running;

	public override void Play()
	{
		if ( Target == null )
			return;

		if ( _running )
			return;

		_anchorId = TransformShakeAnchor.Retain( Target, out _basePosition, out _baseRotation );
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

	public override float GetHoldDuration()
	{
		return 3600f;
	}

	public bool Tick( float deltaTime )
	{
		if ( !_running || Target == null )
		{
			End( false );
			return false;
		}

		Vector3 offset = UnityEngine.Random.insideUnitSphere * Strength;
		Target.localPosition = _basePosition + offset;
		Target.localRotation = _baseRotation * Quaternion.Euler( offset * RotationScale );
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
