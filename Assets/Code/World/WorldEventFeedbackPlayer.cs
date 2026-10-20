using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Plays a <see cref="Feedbacks"/> chain once when a catalog world event fires.
/// Skips juice on save / session restore (event already fired).
/// </summary>
[DisallowMultipleComponent]
public class WorldEventFeedbackPlayer : MonoBehaviour
{
	[Tooltip( "Catalog world event id that triggers the Feedbacks chain." )]
	[SerializeField]
	string _triggerEventId;

	[SerializeField]
	Feedbacks _feedbacks;

	bool _played;
	bool _subscribed;

	public string TriggerEventId => _triggerEventId;

	void OnEnable()
	{
		Subscribe();

		if ( HasEventAlreadyFired() )
		{
			_played = true;
			return;
		}

		_played = false;
	}

	void OnDisable()
	{
		Unsubscribe();
	}

	bool HasEventAlreadyFired()
	{
		if ( string.IsNullOrEmpty( _triggerEventId ) )
			return false;

		WorldEventSystem worldEvents = WorldEventSystem.Instance;
		if ( worldEvents != null )
			return worldEvents.HasEventFired( _triggerEventId );

		ProfileSaveData save = ProfileManager.Instance != null ? ProfileManager.Instance.ProfileSaveData : null;
		if ( save == null )
			return false;

		save.EnsureWorldEventProgress();
		return save.firedWorldEventIds != null && save.firedWorldEventIds.Contains( _triggerEventId );
	}

	void Subscribe()
	{
		if ( _subscribed )
			return;

		EventBus.Subscribe<WorldEventFiredEvent>( OnWorldEventFired );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;

		EventBus.Unsubscribe<WorldEventFiredEvent>( OnWorldEventFired );
		_subscribed = false;
	}

	void OnWorldEventFired( WorldEventFiredEvent evt )
	{
		if ( _played )
			return;
		if ( string.IsNullOrEmpty( _triggerEventId ) || evt.Id != _triggerEventId )
			return;

		PlayOnce();
	}

	void PlayOnce()
	{
		if ( _played )
			return;

		_played = true;
		if ( _feedbacks != null )
			_feedbacks.Play();
	}

#if UNITY_EDITOR
	public void EditorAssign( string triggerEventId, Feedbacks feedbacks )
	{
		_triggerEventId = triggerEventId;
		_feedbacks = feedbacks;
	}
#endif
}
