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

/// <summary>Empty-handed tap shove sent a minecart along the track.</summary>
public struct MinecartShovedEvent
{
	public MinecartInteractable Cart;
}

/// <summary>Interact was held long enough to start follow-push on a minecart.</summary>
public struct MinecartHoldPushStartedEvent
{
	public MinecartInteractable Cart;
}

/// <summary>An item was added to a minecart cargo bed.</summary>
public struct MinecartCargoLoadedEvent
{
	public MinecartInteractable Cart;
	public TreasureItem Item;
}

/// <summary>Player sat in a drive minecart.</summary>
public struct MinecartDriveEnteredEvent
{
	public MinecartInteractable Cart;
}

/// <summary>A cinematic presentation finished its rise/hold/fall (not an abort on disable).</summary>
public struct CinematicPresentationEndedEvent
{
	public string PresentationId;
}
