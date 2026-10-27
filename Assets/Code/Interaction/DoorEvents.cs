/// <summary>Published when a door should become interactable (quest trigger, volume, scripted moment).</summary>
public struct DoorUnlockedEvent
{
	public string DoorId;
}

/// <summary>Published when the player starts opening a door (BeginOpen).</summary>
public struct DoorOpenedEvent
{
	public string DoorId;
}
