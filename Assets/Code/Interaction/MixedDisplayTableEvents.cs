/// <summary>
/// Published whenever a mixed display table's item count changes.
/// </summary>
public struct MixedDisplayTableChangedEvent
{
	public MixedDisplayTableInteractable Table;
	public int ItemCount;
	public int SlotCount;
}
