/// <summary>
/// Published when a treasure pile's logical count reaches zero.
/// </summary>
public struct TreasurePileEmptiedEvent
{
	public TreasurePileInteractable Pile;
	public TreasureDefinition Treasure;
}
