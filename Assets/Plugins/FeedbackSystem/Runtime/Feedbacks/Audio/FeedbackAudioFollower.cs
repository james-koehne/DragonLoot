using UnityEngine;

namespace FeedbackSystem
{
	/// <summary>
	/// Keeps an <see cref="AudioSource"/> world position matched to a transform until playback ends.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class FeedbackAudioFollower : MonoBehaviour
	{
		Transform _follow;
		AudioSource _source;

		void Awake()
		{
			_source = GetComponent<AudioSource>();
		}

		public void Begin( Transform follow )
		{
			_follow = follow;
			enabled = true;
			if ( _follow != null )
				transform.position = _follow.position;
		}

		void LateUpdate()
		{
			if ( _follow == null || _source == null || !_source.isPlaying )
			{
				_follow = null;
				enabled = false;
				return;
			}

			transform.position = _follow.position;
		}

		public static void Attach( AudioSource source, Transform follow )
		{
			if ( source == null || follow == null )
				return;

			FeedbackAudioFollower follower = source.GetComponent<FeedbackAudioFollower>();
			if ( follower == null )
				follower = source.gameObject.AddComponent<FeedbackAudioFollower>();

			follower.Begin( follow );
		}
	}
}
