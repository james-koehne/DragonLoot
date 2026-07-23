/// <summary>
/// Published whenever a coin display table's occupied count changes.
/// </summary>
public struct CoinDisplayTableChangedEvent
{
	public CoinDisplayTableInteractable Table;
	public int Count;
	public int Capacity;
}

/// <summary>
/// Published when a coin display table fills every slot.
/// </summary>
public struct CoinDisplayTableCompletedEvent
{
	public CoinDisplayTableInteractable Table;
	public TreasureDefinition AcceptedCoin;
}
