using UnityEngine;

/// <summary>
/// Scene ids for event volumes / targets. Runtime only creates missing intro volumes — never stub targets.
/// </summary>
public static class EventSceneAutoWire
{
	public const string AreaStarting = "starting_area";

	public const string IdVolumeHallwayEnter = "volume_hallway_enter";
	public const string IdVolumeHallwayEnd = "volume_hallway_end";

	public static void EnsureWired()
	{
		EnsureVolume( IdVolumeHallwayEnter, new Vector3( 4f, 3f, 6f ) );
		EnsureVolume( IdVolumeHallwayEnd, new Vector3( 8f, 4f, 8f ) );
	}

	static void EnsureVolume( string id, Vector3 size )
	{
		if ( string.IsNullOrEmpty( id ) )
			return;
		if ( EventTargetRegistry.TryGetVolume( id, out QuestVolume existing ) && existing != null )
			return;

		GameObject go = new GameObject( "EventVolume_" + id );
		BoxCollider box = go.AddComponent<BoxCollider>();
		box.isTrigger = true;
		box.size = size;

		QuestVolume volume = go.AddComponent<QuestVolume>();
		volume.SetId( id );
	}
}
