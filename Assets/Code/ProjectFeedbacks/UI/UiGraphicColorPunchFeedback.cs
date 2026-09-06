using System;

using FeedbackSystem;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Punches a UI Graphic color toward a highlight then back to its rest color (unscaled).
/// </summary>
[Serializable]
[FeedbackInfo( "UI/Graphic Color Punch" )]
public class UiGraphicColorPunchFeedback : Feedback, IFeedbackTick
{
	public Graphic Target;
	public Color PunchColor = new Color( 0.6f, 0.85f, 0.6f, 1f );

	[Min( 0f )]
	public float Duration = 0.28f;

	public AnimationCurve Curve = new AnimationCurve(
		new Keyframe( 0f, 0f ),
		new Keyframe( 0.35f, 1f ),
		new Keyframe( 1f, 0f ) );

	public bool UseUnscaledTime = true;
	public bool CaptureRestOnPlay = true;

	Color _restColor = Color.white;
	bool _restCaptured;
	float _elapsed;
	bool _running;

	public override void Play()
	{
		if ( Target == null )
			return;

		Cancel();
		if ( CaptureRestOnPlay || !_restCaptured )
		{
			_restColor = Target.color;
			_restCaptured = true;
		}

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
			Target.color = _restColor;
			_running = false;
			return false;
		}

		float curve = Curve != null ? Curve.Evaluate( t ) : Mathf.Sin( t * Mathf.PI );
		Target.color = Color.LerpUnclamped( _restColor, PunchColor, curve );
		return true;
	}

	public void Cancel()
	{
		if ( _running && Target != null )
			Target.color = _restColor;

		_running = false;
		UnregisterTick( this );
	}

	public override float GetHoldDuration()
	{
		return Duration;
	}
}
