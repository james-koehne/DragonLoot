using UnityEngine;

/// <summary>
/// World hammer prop. ContextualInteract publishes <see cref="WorldHammerInteractedEvent"/> to start the build tutorial.
/// </summary>
public class WorldHammerInteractable : InteractableBase
{
	void Reset()
	{
		SetInteractionName( "Hammer" );
	}

	void Awake()
	{
		if ( string.IsNullOrEmpty( InteractionName ) || InteractionName == "Interactable" )
			SetInteractionName( "Hammer" );
	}

	public override bool CanInteract( PlayerController player )
	{
		return IsAvailable && player != null && player.GameplayInputEnabled;
	}

	public override void Interact( PlayerController player )
	{
		EventBus.Publish( new WorldHammerInteractedEvent() );
	}
}
