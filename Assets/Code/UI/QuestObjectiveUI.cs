using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Current quest objective text. Wire references on the Interface prefab.
/// </summary>
public class QuestObjectiveUI : MonoBehaviour
{
	[SerializeField] CanvasGroup group;
	[SerializeField] Text title;
	[SerializeField] Text objective;

	bool _subscribed;

	public void Setup()
	{
		Subscribe();
		if ( group != null )
			group.alpha = 0f;
	}

	void OnEnable()
	{
		Subscribe();
	}

	void OnDisable()
	{
		Unsubscribe();
	}

	void OnDestroy()
	{
		Unsubscribe();
	}

	void Subscribe()
	{
		if ( _subscribed )
			return;
		EventBus.Subscribe<QuestHudChangedEvent>( OnHudChanged );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;
		EventBus.Unsubscribe<QuestHudChangedEvent>( OnHudChanged );
		_subscribed = false;
	}

	void OnHudChanged( QuestHudChangedEvent evt )
	{
		bool show = !evt.CatalogComplete && !string.IsNullOrEmpty( evt.ObjectiveText );
		if ( group != null )
			group.alpha = show ? 1f : 0f;

		if ( title != null )
			title.text = string.IsNullOrEmpty( evt.QuestTitle ) ? "Quest" : evt.QuestTitle;
		if ( objective != null )
			objective.text = evt.ObjectiveText ?? string.Empty;
	}
}
