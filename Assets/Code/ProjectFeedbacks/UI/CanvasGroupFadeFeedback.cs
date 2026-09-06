using System;

using FeedbackSystem;

using UnityEngine;

/// <summary>Fades a CanvasGroup alpha over time (unscaled).</summary>
[Serializable]
[FeedbackInfo( "UI/Canvas Group Fade" )]
public class CanvasGroupFadeFeedback : Feedback, IFeedbackTick
{
	public CanvasGroup Target;

	[Range( 0f, 1f )]
	public float From = 0f;

	[Range( 0f, 1f )]
	public float To = 1f;

	[Min( 0f )]
	public float Duration = 0.2f;

	public AnimationCurve Curve = AnimationCurve.EaseInOut( 0f, 0f, 1f, 1f );
	public bool UseUnscaledTime = true;
	public bool CaptureCurrentAsFrom;

	float _elapsed;
	bool _running;
	float _from;

	public override void Play()
	{
		if ( Target == null )
			return;

		Cancel();
		_from = CaptureCurrentAsFrom ? Target.alpha : From;
		Target.alpha = _from;
		if ( Duration <= 0f )
		{
			Target.alpha = To;
			return;
		}

		_elapsed = 0f;
		_running = true;
		RegisterTick( this );
	}

	public override void Stop()
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
			Target.alpha = To;
			_running = false;
			return false;
		}

		float curve = Curve != null ? Curve.Evaluate( t ) : t;
		Target.alpha = Mathf.LerpUnclamped( _from, To, curve );
		return true;
	}

	public void Cancel()
	{
		_running = false;
		UnregisterTick( this );
	}

	public override float GetHoldDuration()
	{
		return Duration;
	}
}
