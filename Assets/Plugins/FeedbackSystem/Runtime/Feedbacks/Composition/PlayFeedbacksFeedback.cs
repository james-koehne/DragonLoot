using System;

using UnityEngine;

namespace FeedbackSystem
{
	[Serializable]
	[FeedbackInfo( "Composition/Play Feedbacks" )]
	public class PlayFeedbacksFeedback : Feedback
	{
		public enum FeedbacksAction
		{
			Play = 0,
			Stop = 1,
			Reset = 2
		}

		[Tooltip( "Other Feedbacks component to control. Must not be this owner (avoids recursion)." )]
		public Feedbacks Target;

		public FeedbacksAction Action = FeedbacksAction.Play;

		[Tooltip( "When Action is Play, forward this feedback's Context to the target." )]
		public bool PassContext = true;

		[Tooltip( "When Action is Play, skip if the target is already playing." )]
		public bool OnlyIfNotPlaying;

		[Tooltip( "When Action is Play, hold the parent sequence for the target's sequential hold duration." )]
		public bool WaitForCompletion;

		[Range( 0f, 1f )]
		[Tooltip( "Probability that this feedback runs (1 = always)." )]
		public float Chance = 1f;

		[Tooltip( "When this feedback is stopped, also Stop the target (useful if you Play'd it)." )]
		public bool StopTargetOnStop = true;

		[Tooltip( "When this feedback is reset, also Reset the target." )]
		public bool ResetTargetOnReset;

		float _holdDuration;

		public override void Play()
		{
			_holdDuration = 0f;

			if ( Target == null || Target == Owner )
				return;

			if ( Chance < 1f && UnityEngine.Random.value > Chance )
				return;

			switch ( Action )
			{
				case FeedbacksAction.Play:
					if ( OnlyIfNotPlaying && Target.IsPlaying )
						return;

					if ( PassContext )
						Target.Play( Context );
					else
						Target.Play();

					if ( WaitForCompletion )
						_holdDuration = ComputeTargetHoldDuration( Target );
					break;

				case FeedbacksAction.Stop:
					Target.Stop();
					break;

				case FeedbacksAction.Reset:
					Target.ResetFeedbacks();
					break;
			}
		}

		public override void Stop()
		{
			_holdDuration = 0f;

			if ( !StopTargetOnStop || Target == null || Target == Owner )
				return;

			Target.Stop();
		}

		public override void Reset()
		{
			_holdDuration = 0f;

			if ( !ResetTargetOnReset || Target == null || Target == Owner )
				return;

			Target.ResetFeedbacks();
		}

		public override float GetHoldDuration()
		{
			return _holdDuration < 0f ? 0f : _holdDuration;
		}

		static float ComputeTargetHoldDuration( Feedbacks target )
		{
			if ( target == null || target.FeedbackList == null )
				return 0f;

			float total = 0f;
			for ( int i = 0; i < target.FeedbackList.Count; i++ )
			{
				Feedback feedback = target.FeedbackList[i];
				if ( feedback == null || !feedback.Enabled )
					continue;

				total += feedback.GetHoldDuration();
			}

			return total;
		}
	}
}
