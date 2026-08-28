using UnityEngine;

/// <summary>
/// Published when the player digs / steals units from a treasure pile.
/// </summary>
public struct TreasurePileDigEvent
{
	public TreasurePileInteractable Pile;
	public TreasureDefinition Treasure;
	public int Amount;
	public Vector3 DigPoint;
}

/// <summary>
/// Published when a treasure pile's logical count reaches zero.
/// </summary>
public struct TreasurePileEmptiedEvent
{
	public TreasurePileInteractable Pile;
	public TreasureDefinition Treasure;
}
