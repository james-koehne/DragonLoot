using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Plays a <see cref="Feedbacks"/> chain once when a specific gem constellation is completed by the player.
/// Skips juice on save / start-fill restore (already complete, or <see cref="GemConstellationCompletedEvent.FromPlayer"/> false).
/// </summary>
[DisallowMultipleComponent]
public class GemConstellationFeedbackPlayer : MonoBehaviour
{
	[Tooltip( "Constellation that must complete. Empty = resolve from scene / children." )]
	[SerializeField]
	GemConstellationInteractable _constellation;

	[SerializeField]
	Feedbacks _feedbacks;

	bool _played;
	bool _subscribed;

	public GemConstellationInteractable Constellation => _constellation;

	void Awake()
	{
		ResolveConstellation();
	}

	void OnEnable()
	{
		ResolveConstellation();
		Subscribe();

		if ( _constellation != null && _constellation.IsComplete )
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

	void ResolveConstellation()
	{
		if ( _constellation != null )
			return;

		_constellation = GetComponent<GemConstellationInteractable>();
		if ( _constellation == null )
			_constellation = GetComponentInParent<GemConstellationInteractable>();
		if ( _constellation == null )
			_constellation = GetComponentInChildren<GemConstellationInteractable>( true );
	}

	void Subscribe()
	{
		if ( _subscribed )
			return;

		EventBus.Subscribe<GemConstellationCompletedEvent>( OnConstellationCompleted );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;

		EventBus.Unsubscribe<GemConstellationCompletedEvent>( OnConstellationCompleted );
		_subscribed = false;
	}

	void OnConstellationCompleted( GemConstellationCompletedEvent evt )
	{
		if ( _played )
			return;
		if ( _constellation == null || evt.Constellation != _constellation )
			return;

		if ( !evt.FromPlayer )
		{
			_played = true;
			return;
		}

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
	public void EditorAssign( GemConstellationInteractable constellation, Feedbacks feedbacks )
	{
		_constellation = constellation;
		_feedbacks = feedbacks;
	}
#endif
}
