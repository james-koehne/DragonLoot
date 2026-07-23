/// <summary>
/// Published whenever a gem display table's occupied count changes.
/// </summary>
public struct GemDisplayTableChangedEvent
{
	public GemDisplayTableInteractable Table;
	public int Count;
	public int Capacity;
}

/// <summary>
/// Published when a gem display table fills every slot.
/// </summary>
public struct GemDisplayTableCompletedEvent
{
	public GemDisplayTableInteractable Table;
	public TreasureDefinition AcceptedGem;
}
