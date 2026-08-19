/// <summary>
/// Published whenever a gold-bar display table's occupied count changes.
/// </summary>
public struct GoldBarDisplayTableChangedEvent
{
	public GoldBarDisplayTableInteractable Table;
	public int Count;
	public int Capacity;
}

/// <summary>
/// Published when a gold-bar display table fills every slot (or every slot at max stack).
/// </summary>
public struct GoldBarDisplayTableCompletedEvent
{
	public GoldBarDisplayTableInteractable Table;
	public TreasureDefinition AcceptedBar;
}
