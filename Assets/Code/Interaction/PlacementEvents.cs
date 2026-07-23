using UnityEngine;

public enum PlacementFailReason
{
	None,
	InvalidTarget,
	RejectedByTarget,
	NoHeldItem,
	OutOfRange
}

public struct PlacementStartedEvent
{
	public ITreasurePlacementTarget Target;
	public TreasureItem Item;
	public TreasureDefinition Definition;
	public Vector3 Position;
	public Quaternion Rotation;
}

public struct PlacementCompletedEvent
{
	public ITreasurePlacementTarget Target;
	public TreasureItem Item;
	public TreasureDefinition Definition;
}

public struct PlacementFailedEvent
{
	public ITreasurePlacementTarget Target;
	public TreasureItem Item;
	public TreasureDefinition Definition;
	public PlacementFailReason Reason;
}

public struct TreasureRemovedEvent
{
	public ITreasurePlacementTarget Target;
	public TreasureItem Item;
	public TreasureDefinition Definition;
}
