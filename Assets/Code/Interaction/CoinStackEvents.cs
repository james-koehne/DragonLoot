/// <summary>
/// Published when a world ground coin stack gains or loses coins (not machine buffers).
/// </summary>
public struct CoinStackChangedEvent
{
	public GroundCoinStack Stack;
	public int Count;
	public TreasureDefinition TopCoin;
}

/// <summary>Player took a single coin from a ground stack of 2+ (not whole-stack pickup).</summary>
public struct CoinTakenFromStackEvent
{
	public GroundCoinStack Stack;
	public TreasureDefinition Coin;
}
