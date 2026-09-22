using System;

using FeedbackSystem;

using UnityEngine;

/// <summary>Pulses local scale with a sine wave for a finite duration (UI / pause-safe).</summary>
[Serializable]
[FeedbackInfo( "UI/Pulse Scale" )]
public class UiPulseScaleFeedback : Feedback, IFeedbackTick
{
	public Transform Target;
	public Vector3 Amplitude = new Vector3( 0.2f, 0.2f, 0f );

	[Min( 0.01f )]
	public float CyclesPerSecond = 1.2f;

	[Min( 0f )]
	public float Duration = 8f;

	public bool UseUnscaledTime = true;
	public bool RestoreOnComplete = true;

	Vector3 _baseScale;
	float _elapsed;
	bool _running;

	public override void Play()
	{
		if ( Target == null )
			return;

		Cancel();
		_baseScale = Target.localScale;
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
				Target.localScale = _baseScale;
			_running = false;
			return false;
		}

		float wave = Mathf.Sin( _elapsed * CyclesPerSecond * Mathf.PI * 2f );
		float envelope = 1f;
		if ( Duration > 0.01f )
		{
			float t = _elapsed / Duration;
			if ( t > 0.7f )
				envelope = Mathf.Clamp01( 1f - ( ( t - 0.7f ) / 0.3f ) );
		}

		Target.localScale = _baseScale + Amplitude * ( wave * 0.5f + 0.5f ) * envelope;
		return true;
	}

	public void Cancel()
	{
		if ( _running && Target != null && RestoreOnComplete )
			Target.localScale = _baseScale;

		_running = false;
		UnregisterTick( this );
	}

	public override float GetHoldDuration()
	{
		return Duration;
	}
}
