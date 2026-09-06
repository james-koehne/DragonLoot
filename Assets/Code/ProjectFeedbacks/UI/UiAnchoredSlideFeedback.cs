using System;

using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Slides a RectTransform's anchoredPosition from (currentRest + FromOffset) to (currentRest + ToOffset).
/// Rest is sampled at Play start. Unscaled-time friendly for UI.
/// </summary>
[Serializable]
[FeedbackInfo( "UI/Anchored Slide" )]
public class UiAnchoredSlideFeedback : Feedback, IFeedbackTick
{
	public RectTransform Target;
	public Vector2 FromOffset = new Vector2( 0f, -28f );
	public Vector2 ToOffset = Vector2.zero;

	[Min( 0f )]
	public float Duration = 0.28f;

	public AnimationCurve Curve = AnimationCurve.EaseInOut( 0f, 0f, 1f, 1f );
	public bool UseUnscaledTime = true;

	Vector2 _rest;
	float _elapsed;
	bool _running;

	public override void Play()
	{
		if ( Target == null )
			return;

		CancelMotion( false );
		_rest = Target.anchoredPosition;
		Target.anchoredPosition = _rest + FromOffset;
		if ( Duration <= 0f )
		{
			Target.anchoredPosition = _rest + ToOffset;
			return;
		}

		_elapsed = 0f;
		_running = true;
		RegisterTick( this );
	}

	public override void Stop()
	{
		CancelMotion( false );
	}

	public override void Reset()
	{
		CancelMotion( true );
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
			Target.anchoredPosition = _rest + ToOffset;
			_running = false;
			return false;
		}

		float curve = Curve != null ? Curve.Evaluate( t ) : t;
		Target.anchoredPosition = Vector2.LerpUnclamped( _rest + FromOffset, _rest + ToOffset, curve );
		return true;
	}

	public void Cancel()
	{
		CancelMotion( false );
	}

	void CancelMotion( bool snapToEnd )
	{
		if ( _running && Target != null && snapToEnd )
			Target.anchoredPosition = _rest + ToOffset;

		_running = false;
		UnregisterTick( this );
	}

	public override float GetHoldDuration()
	{
		return Duration;
	}
}
