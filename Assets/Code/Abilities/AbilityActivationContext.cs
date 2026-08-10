using UnityEngine;

public readonly struct AbilityActivationContext
{
	public readonly AbilityDefinition Definition;
	public readonly PlayerController Player;
	public readonly int SlotIndex;

	public AbilityActivationContext( AbilityDefinition definition, PlayerController player, int slotIndex )
	{
		Definition = definition;
		Player = player;
		SlotIndex = slotIndex;
	}

	public Transform PlayerTransform => Player != null ? Player.transform : null;
}
