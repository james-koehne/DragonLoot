/// <summary>
/// Published when a Coin Hall expands to a new level (animated or snapped).
/// </summary>
public struct CoinHallExpandedEvent
{
	public string HallId;
	public int LevelIndex;
	public string LevelId;
	public bool FromLoad;
}
