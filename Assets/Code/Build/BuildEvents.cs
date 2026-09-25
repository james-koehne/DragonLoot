/// <summary>Player entered build mode (F).</summary>
public struct BuildModeEnteredEvent
{
}

/// <summary>Player exited build mode.</summary>
public struct BuildModeExitedEvent
{
}

/// <summary>A buildable finished construction.</summary>
public struct BuildableCompletedEvent
{
	public BuildableObject Buildable;
}

/// <summary>Player interacted with the world hammer prop.</summary>
public struct WorldHammerInteractedEvent
{
}
