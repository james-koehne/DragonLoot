public interface IInteractable
{
	string InteractionName { get; }
	bool CanInteract( PlayerController player );
	void Interact( PlayerController player );
}
