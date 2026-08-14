using UnityEngine;

/// <summary>
/// Ensures quest target ids and volumes exist in the loaded level when not pre-wired.
/// Creates standalone objects (does not attach to existing interactables).
/// </summary>
public static class QuestSceneAutoWire
{
	public const string IdStartingDoorPile = "starting_door_pile";
	public const string IdStartingSortingPlinth = "starting_sorting_plinth";
	public const string IdVolumeDoorwayGold = "volume_doorway_gold";
	public const string IdVolumeCoinSorter = "volume_coin_sorter";
	public const string IdCoinSorter = "coin_sorter";
	public const string IdCoinPlinth = "coin_plinth";
	public const string IdVolumeConstellation = "volume_constellation";
	public const string IdConstellation = "constellation";
	public const string IdVolumeMuseum = "volume_museum";
	public const string IdMuseumTable = "museum_table";

	public static void EnsureWired()
	{
		EnsureStandaloneTarget( IdStartingDoorPile );
		EnsureStandaloneTarget( IdStartingSortingPlinth );
		EnsureStandaloneTarget( IdCoinSorter );
		EnsureStandaloneTarget( IdCoinPlinth );
		EnsureStandaloneTarget( IdConstellation );
		EnsureStandaloneTarget( IdMuseumTable );

		EnsureVolume( IdVolumeDoorwayGold, new Vector3( 4f, 3f, 4f ) );
		EnsureVolume( IdVolumeCoinSorter, new Vector3( 5f, 3f, 5f ) );
		EnsureVolume( IdVolumeConstellation, new Vector3( 5f, 3f, 5f ) );
		EnsureVolume( IdVolumeMuseum, new Vector3( 5f, 3f, 5f ) );
	}

	static void EnsureStandaloneTarget( string id )
	{
		if ( string.IsNullOrEmpty( id ) )
			return;
		if ( QuestTargetRegistry.TryGetTarget( id, out QuestTarget existing ) && existing != null )
			return;

		GameObject go = new GameObject( "QuestTarget_" + id );
		QuestMarkerAnchor anchor = go.AddComponent<QuestMarkerAnchor>();
		QuestTarget target = go.AddComponent<QuestTarget>();
		target.SetId( id );
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
