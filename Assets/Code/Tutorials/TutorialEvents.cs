using UnityEngine;

/// <summary>Selected carry bucket or held count for that bucket changed.</summary>
public struct PlayerHeldCategoryChangedEvent
{
	public TreasureCategory Category;
	public bool IsHolding;
	public PlayerCarry Carry;
}

/// <summary>Player threw held treasure (not a soft drop / floor place).</summary>
public struct TreasureThrownEvent
{
	public TreasureItem Item;
	public TreasureDefinition Definition;
}

/// <summary>Whole-stack hold pickup finished successfully.</summary>
public struct WholeStackPickupCompletedEvent
{
	public CarryBucketKind Bucket;
}

/// <summary>Whole-stack hold place finished successfully.</summary>
public struct WholeStackPlaceCompletedEvent
{
	public CarryBucketKind Bucket;
}

/// <summary>Interaction focus aiming at a treasure pile started or stopped.</summary>
public struct TreasurePileAimChangedEvent
{
	public bool IsAiming;
	public TreasurePileInteractable Pile;
}

/// <summary>Full-screen map finished opening (or was opened).</summary>
public struct MapOpenedEvent
{
}

/// <summary>A chest finished opening and spawned loot.</summary>
public struct ChestOpenedEvent
{
	public ChestInteractable Chest;
}
