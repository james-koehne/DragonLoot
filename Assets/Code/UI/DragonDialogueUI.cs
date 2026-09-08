using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dragon subtitle caption driven by <see cref="DragonDialogueChangedEvent"/>.
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
		if ( speaker != null )
			speaker.gameObject.SetActive( false );
		if ( body != null )
			body.alignment = TextAnchor.MiddleCenter;
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
		EventBus.Subscribe<DragonDialogueChangedEvent>( OnDialogueChanged );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;
		EventBus.Unsubscribe<DragonDialogueChangedEvent>( OnDialogueChanged );
		_subscribed = false;
	}

	void OnDialogueChanged( DragonDialogueChangedEvent evt )
	{
		if ( !evt.Visible )
		{
			SetVisible( false );
			return;
		}

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
