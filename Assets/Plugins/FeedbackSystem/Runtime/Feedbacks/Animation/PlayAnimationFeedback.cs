using System;

using UnityEngine;

namespace FeedbackSystem
{
	[Serializable]
	[FeedbackInfo( "Animation/Play Animation" )]
	public class PlayAnimationFeedback : Feedback
	{
		public Animator Animator;
		public string StateName;

		int _stateHash;

		public override void Initialize()
		{
			_stateHash = string.IsNullOrEmpty( StateName ) ? 0 : Animator.StringToHash( StateName );
		}

		public override void Play()
		{
			if ( Animator == null || string.IsNullOrEmpty( StateName ) )
				return;

			if ( _stateHash == 0 )
				_stateHash = Animator.StringToHash( StateName );

			Animator.Play( _stateHash );
		}
	}
}
