using UnityEngine;

/// <summary>
/// Scene ids for quest targets / volumes. Level wiring should attach these to real interactables.
/// Runtime only creates missing volumes — never stub targets.
/// </summary>
public static class QuestSceneAutoWire
{
	public const string AreaStarting = "starting_area";

	public const string IdStartingDoorPile = "starting_door_pile";
	public const string IdStartingCoinPile = "starting_coin_pile";
	public const string IdVolumeDoorwayGold = "volume_doorway_gold";
	public const string IdVolumeCoinSorter = "volume_coin_sorter";
	public const string IdCoinSorter = "coin_sorter";
	public const string IdVolumeConstellation = "volume_constellation";
	public const string IdConstellation = "constellation";
	public const string IdMuseumTable = "museum_table";
	public const string IdGoldBarTable = "gold_bar_table";
	public const string IdVolumeHallwayEnter = "volume_hallway_enter";
	public const string IdVolumeHallwayEnd = "volume_hallway_end";

	public static void EnsureWired()
	{
		EnsureVolume( IdVolumeDoorwayGold, new Vector3( 4f, 3f, 4f ) );
		EnsureVolume( IdVolumeCoinSorter, new Vector3( 5f, 3f, 5f ) );
		EnsureVolume( IdVolumeConstellation, new Vector3( 5f, 3f, 5f ) );
		EnsureVolume( IdVolumeHallwayEnter, new Vector3( 4f, 3f, 6f ) );
		EnsureVolume( IdVolumeHallwayEnd, new Vector3( 8f, 4f, 8f ) );
	}

	static void EnsureVolume( string id, Vector3 size )
	{
		if ( string.IsNullOrEmpty( id ) )
			return;
		if ( QuestTargetRegistry.TryGetVolume( id, out QuestVolume existing ) && existing != null )
			return;

		GameObject go = new GameObject( "QuestVolume_" + id );
		BoxCollider box = go.AddComponent<BoxCollider>();
		box.isTrigger = true;
		box.size = size;

		QuestVolume volume = go.AddComponent<QuestVolume>();
		volume.SetId( id );
	}
}
