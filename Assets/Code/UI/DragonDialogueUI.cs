using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dragon subtitle caption driven by <see cref="QuestDialogueChangedEvent"/>.
/// Wire references on the Interface prefab.
/// </summary>
public class DragonDialogueUI : MonoBehaviour
{
	[SerializeField] CanvasGroup group;
	[SerializeField] Text speaker;
	[SerializeField] Text body;

	bool _subscribed;

	public void Setup()
	{
		Subscribe();
		SetVisible( false );
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
		EventBus.Subscribe<QuestDialogueChangedEvent>( OnDialogueChanged );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;
		EventBus.Unsubscribe<QuestDialogueChangedEvent>( OnDialogueChanged );
		_subscribed = false;
	}

	void OnDialogueChanged( QuestDialogueChangedEvent evt )
	{
		if ( !evt.Visible )
		{
			SetVisible( false );
			return;
		}

		if ( speaker != null )
			speaker.text = evt.Speaker;
		if ( body != null )
			body.text = evt.Text;
		SetVisible( true );
	}

	void SetVisible( bool visible )
	{
		if ( group != null )
			group.alpha = visible ? 1f : 0f;
	}
}
