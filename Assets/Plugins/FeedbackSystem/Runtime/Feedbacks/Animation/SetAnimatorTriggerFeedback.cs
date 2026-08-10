using System;

using UnityEngine;

namespace FeedbackSystem
{
	[Serializable]
	[FeedbackInfo( "Animation/Set Animator Trigger" )]
	public class SetAnimatorTriggerFeedback : Feedback
	{
		public Animator Animator;
		public string Trigger;

		int _triggerHash;

		public override void Initialize()
		{
			_triggerHash = string.IsNullOrEmpty( Trigger ) ? 0 : Animator.StringToHash( Trigger );
		}

		public override void Play()
		{
			if ( Animator == null || string.IsNullOrEmpty( Trigger ) )
				return;

			if ( _triggerHash == 0 )
				_triggerHash = Animator.StringToHash( Trigger );

			Animator.SetTrigger( _triggerHash );
		}
	}
}
