/// <summary>
/// Published when a world ground coin stack gains or loses coins (not machine buffers).
/// </summary>
public struct CoinStackChangedEvent
{
	public GroundCoinStack Stack;
	public int Count;
	public TreasureDefinition TopCoin;
}
