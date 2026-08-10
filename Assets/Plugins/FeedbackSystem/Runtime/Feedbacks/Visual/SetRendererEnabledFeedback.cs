using System;

using UnityEngine;

namespace FeedbackSystem
{
	[Serializable]
	[FeedbackInfo( "Visual/Set Renderer Enabled" )]
	public class SetRendererEnabledFeedback : Feedback
	{
		public Renderer Renderer;

		[Tooltip( "Value assigned to Renderer.enabled." )]
		public bool RendererEnabled = true;

		public override void Play()
		{
			if ( Renderer == null )
				return;

			Renderer.enabled = RendererEnabled;
		}
	}
}
