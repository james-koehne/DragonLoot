using UnityEngine;

public abstract class InteractableBase : MonoBehaviour, IInteractable
{
	[SerializeField]
	string interactionName = "Interactable";

	public string InteractionName => interactionName;

	public bool IsAvailable => isActiveAndEnabled;

	/// <summary>
	/// Pickup/dig uses primary Interact (LMB). Everything else is ContextualInteract (E).
	/// </summary>
	public virtual bool UsesPickupInteract => false;

	public static bool IsPickupInteract( IInteractable interactable )
	{
		InteractableBase target = interactable as InteractableBase;
		return target != null && target.UsesPickupInteract;
	}

	public virtual bool CanInteract( PlayerController player )
	{
		return IsAvailable && player != null;
	}

	public abstract void Interact( PlayerController player );

	protected void SetInteractionName( string displayName )
	{
		interactionName = displayName;
	}
}
