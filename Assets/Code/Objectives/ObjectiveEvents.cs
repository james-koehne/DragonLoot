/// <summary>Published when one objective sub-step is completed.</summary>
public struct ObjectiveSubCompletedEvent
{
	public string ObjectiveId;
	public string SubId;
}

/// <summary>Published when an objective finishes (all subs complete + rewards granted).</summary>
public struct ObjectiveCompletedEvent
{
	public string ObjectiveId;
	public ObjectiveDefinition Definition;
}
