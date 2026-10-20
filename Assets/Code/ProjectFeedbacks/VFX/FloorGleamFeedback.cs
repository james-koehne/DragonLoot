using System;

using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Plays a <see cref="RoomFloorGleamController"/> sweep once.
/// </summary>
[Serializable]
[FeedbackInfo( "VFX/Floor Gleam" )]
public class FloorGleamFeedback : Feedback
{
	public RoomFloorGleamController Controller;

	public override void Play()
	{
		if ( Controller == null )
			return;

		Controller.Play();
	}

	public override void Stop()
	{
		if ( Controller == null )
			return;

		Controller.Stop();
	}

	public override float GetHoldDuration()
	{
		return 0f;
	}
}
