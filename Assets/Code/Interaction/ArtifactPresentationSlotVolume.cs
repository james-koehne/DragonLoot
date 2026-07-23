using UnityEngine;

/// <summary>
/// Thin aim volume on a slot anchor so placement resolves to the correct slot index.
/// </summary>
[DisallowMultipleComponent]
public class ArtifactPresentationSlotVolume : MonoBehaviour
{
	[SerializeField]
	ArtifactPresentationTableInteractable table;

	[SerializeField]
	int slotIndex = -1;

	public int SlotIndex => slotIndex;

	public void Configure( ArtifactPresentationTableInteractable owner, int index )
	{
		table = owner;
		slotIndex = index;
	}

	public static bool TryResolveSlotIndex( Collider collider, out int slotIndex )
	{
		slotIndex = -1;
		if ( collider == null )
			return false;

		ArtifactPresentationSlotVolume volume = collider.GetComponent<ArtifactPresentationSlotVolume>();
		if ( volume == null )
			volume = collider.GetComponentInParent<ArtifactPresentationSlotVolume>();
		if ( volume == null || volume.slotIndex < 0 )
			return false;

		slotIndex = volume.slotIndex;
		return true;
	}
}
