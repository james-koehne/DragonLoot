using UnityEngine;

public abstract class InteractableBase : MonoBehaviour, IInteractable
{
	[SerializeField]
	string interactionName = "Interactable";

	public string InteractionName => interactionName;

	public bool IsAvailable => isActiveAndEnabled;

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
