/// <summary>
/// Published whenever a gem constellation's occupied slot count changes.
/// </summary>
public struct GemConstellationChangedEvent
{
	public GemConstellationInteractable Constellation;
	public int Count;
	public int Capacity;
}

/// <summary>
/// Published when every slot on a gem constellation is filled.
/// </summary>
public struct GemConstellationCompletedEvent
{
	public GemConstellationInteractable Constellation;
	public bool FromPlayer;
}

/// <summary>
/// Published when a connection edge becomes lit or dimmed (both endpoints filled vs not).
/// </summary>
public struct GemConstellationConnectionChangedEvent
{
	public GemConstellationInteractable Constellation;
	public int SlotA;
	public int SlotB;
	public bool IsLit;
}
