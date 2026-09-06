using UnityEngine;

/// <summary>
/// Scene ids for event volumes / targets. Runtime only creates missing intro volumes — never stub targets.
/// Author in Level (not auto-created):
/// - <see cref="IdVolumeMainCave"/> (`volume_main_cave`) — Digging tutorial context
/// - <see cref="IdVolumeCoinHall"/> (`volume_coin_hall`) — Coin Displays tutorial context
/// Optional: assign Icon sprites on MapLabelMarker components for full-screen map icon+text.
/// Map labels used by tutorials (exact text): "Coin Hall", "Workshop".
/// </summary>
public static class EventSceneAutoWire
{
	public const string AreaStarting = "starting_area";

	public const string IdVolumeHallwayEnter = "volume_hallway_enter";
	public const string IdVolumeHallwayEnd = "volume_hallway_end";

	/// <summary>Author in Level: covers diggable main cave (Tutorial Digging show context).</summary>
	public const string IdVolumeMainCave = "volume_main_cave";

	/// <summary>Author in Level: Coin Hall entry (Tutorial Coin Displays show context).</summary>
	public const string IdVolumeCoinHall = "volume_coin_hall";

	/// <summary>Author in Level: map label text for Workshop / coin sorter (exact: "Workshop").</summary>
	public const string MapLabelWorkshop = "Workshop";
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
