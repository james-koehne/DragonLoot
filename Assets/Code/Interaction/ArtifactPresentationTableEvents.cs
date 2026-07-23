/// <summary>
/// Published whenever an artifact presentation table's occupied count changes.
/// </summary>
public struct ArtifactPresentationTableChangedEvent
{
	public ArtifactPresentationTableInteractable Table;
	public int Count;
	public int Capacity;
}

/// <summary>
/// Published when every slot on an artifact presentation table is filled.
/// </summary>
public struct ArtifactPresentationTableCompletedEvent
{
	public ArtifactPresentationTableInteractable Table;
}
