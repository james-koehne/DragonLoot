using UnityEngine;

/// <summary>
/// World hammer prop. ContextualInteract publishes the tutorial event and flies the hammer into the hand,
/// entering build mode with a world-pickup animation (overrides the default draw toss/spin).
/// </summary>
public class WorldHammerInteractable : InteractableBase
{
	bool _taken;

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
		if ( !IsAvailable || player == null || !player.GameplayInputEnabled )
			return false;
		if ( _taken )
			return false;
		if ( player.IsInBuildMode )
			return false;
		return true;
	}

	public override void Interact( PlayerController player )
	{
		EventBus.Publish( new WorldHammerInteractedEvent() );

		PlayerBuildMode buildMode = player != null ? player.BuildMode : null;
		if ( buildMode == null )
			return;

		if ( buildMode.TryTakeWorldHammer( this ) )
			_taken = true;
	}

	/// <summary>Called by build mode when the hammer is returned (exit build mode).</summary>
	public void RestoreToWorld()
	{
		_taken = false;
		if ( !gameObject.activeSelf )
			gameObject.SetActive( true );
	}
}
