using System;

using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Enables a <see cref="TrailRenderer"/> while playing; disables and clears on stop.
/// </summary>
[Serializable]
[FeedbackInfo( "VFX/Trail Renderer" )]
public class TrailRendererFeedback : Feedback
{
	public TrailRenderer Trail;

	public override void Play()
	{
		if ( Trail == null )
			return;

		Trail.emitting = true;
	}

	public override void Stop()
	{
		if ( Trail == null )
			return;

		Trail.emitting = false;
		Trail.Clear();
	}
}
