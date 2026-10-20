using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Captures piles, stations, displays, constellations, chests, platforms, and map fog.
/// </summary>
public sealed class GameSaveExtendedWorldContributor : IGameSaveContributor
{
	public string Key => "extendedWorld";

	public void Capture( GameSaveData data )
	{
		if ( data == null )
			return;
		if ( data.world == null )
			data.world = new GameSaveWorldData();

		GameSaveWorldData world = data.world;
		EnsureLists( world );

		CapturePiles( world.piles );
		CaptureStations( world.stations );
		CaptureDisplays( world.displays );
		CaptureConstellations( world.constellations );
		CaptureChests( world.chests );
		CapturePlatforms( world.platforms );

		MapSystem map = MapSystem.Instance;
		if ( map != null && map.TryCaptureDiscoveryForSave( out GameSaveMapFogEntry fog ) )
			world.mapFog = fog;
		else
			world.mapFog = null;
	}

	public void Apply( GameSaveData data )
	{
		if ( data == null || data.world == null )
			return;

		GameSaveWorldData world = data.world;
		ApplyPiles( world.piles );
		ApplyStations( world.stations );
		ApplyDisplays( world.displays );
		ApplyConstellations( world.constellations );
		ApplyChests( world.chests );
		ApplyPlatforms( world.platforms );

		MapSystem map = MapSystem.Instance;
		if ( map != null && world.mapFog != null )
			map.ApplyDiscoveryFromSave( world.mapFog );
	}

	static void EnsureLists( GameSaveWorldData world )
	{
		if ( world.piles == null )
			world.piles = new List<GameSavePileEntry>();
		else
			world.piles.Clear();

		if ( world.stations == null )
			world.stations = new List<GameSaveStationEntry>();
		else
			world.stations.Clear();

		if ( world.displays == null )
			world.displays = new List<GameSaveDisplayTableEntry>();
		else
			world.displays.Clear();

		if ( world.constellations == null )
			world.constellations = new List<GameSaveConstellationEntry>();
		else
			world.constellations.Clear();

		if ( world.chests == null )
			world.chests = new List<GameSaveChestEntry>();
		else
			world.chests.Clear();

		if ( world.platforms == null )
			world.platforms = new List<GameSavePlatformGroupEntry>();
		else
			world.platforms.Clear();
	}

	static void CapturePiles( List<GameSavePileEntry> into )
	{
		TreasurePileVisual[] piles = Object.FindObjectsByType<TreasurePileVisual>( FindObjectsInactive.Exclude );
		for ( int i = 0; i < piles.Length; i++ )
		{
			TreasurePileVisual pile = piles[ i ];
			if ( pile == null )
				continue;
			if ( pile.TryCaptureSave( out GameSavePileEntry entry ) && entry != null )
				into.Add( entry );
		}
	}

	static void ApplyPiles( List<GameSavePileEntry> entries )
	{
		if ( entries == null )
			return;

		Dictionary<string, GameSavePileEntry> byId = new Dictionary<string, GameSavePileEntry>();
		for ( int i = 0; i < entries.Count; i++ )
		{
			GameSavePileEntry entry = entries[ i ];
			if ( entry == null || string.IsNullOrEmpty( entry.persistenceId ) )
				continue;
			byId[ entry.persistenceId ] = entry;
		}

		TreasurePileVisual[] piles = Object.FindObjectsByType<TreasurePileVisual>( FindObjectsInactive.Exclude );
		for ( int i = 0; i < piles.Length; i++ )
		{
			TreasurePileVisual pile = piles[ i ];
			if ( pile == null )
				continue;
			string id = pile.ResolvePersistenceId();
			if ( string.IsNullOrEmpty( id ) || !byId.TryGetValue( id, out GameSavePileEntry entry ) )
				continue;
			pile.ApplyFromSave( entry );
		}
	}

	static void CaptureStations( List<GameSaveStationEntry> into )
	{
		RecyclingMachine[] recycling = Object.FindObjectsByType<RecyclingMachine>( FindObjectsInactive.Exclude );
		for ( int i = 0; i < recycling.Length; i++ )
		{
			if ( recycling[ i ] == null )
				continue;
			GameSaveStationEntry entry = new GameSaveStationEntry();
			recycling[ i ].CaptureSave( entry );
			if ( !string.IsNullOrEmpty( entry.persistenceId ) )
				into.Add( entry );
		}

		CleaningStationInteractable[] cleaning = Object.FindObjectsByType<CleaningStationInteractable>( FindObjectsInactive.Exclude );
		for ( int i = 0; i < cleaning.Length; i++ )
		{
			if ( cleaning[ i ] == null )
				continue;
			GameSaveStationEntry entry = new GameSaveStationEntry();
			cleaning[ i ].CaptureSave( entry );
			if ( !string.IsNullOrEmpty( entry.persistenceId ) )
				into.Add( entry );
		}

		CoinSortingStation[] sorting = Object.FindObjectsByType<CoinSortingStation>( FindObjectsInactive.Exclude );
		for ( int i = 0; i < sorting.Length; i++ )
		{
			if ( sorting[ i ] == null )
				continue;
			GameSaveStationEntry entry = new GameSaveStationEntry();
			sorting[ i ].CaptureSave( entry );
			if ( !string.IsNullOrEmpty( entry.persistenceId ) )
				into.Add( entry );
		}
	}

	static void ApplyStations( List<GameSaveStationEntry> entries )
	{
		if ( entries == null )
			return;

		Dictionary<string, GameSaveStationEntry> byId = IndexStations( entries );

		RecyclingMachine[] recycling = Object.FindObjectsByType<RecyclingMachine>( FindObjectsInactive.Exclude );
		for ( int i = 0; i < recycling.Length; i++ )
		{
			RecyclingMachine station = recycling[ i ];
			if ( station == null )
				continue;
			if ( byId.TryGetValue( station.ResolvePersistenceId(), out GameSaveStationEntry entry ) && entry.kind == "recycling" )
				station.ApplySave( entry );
		}

		CleaningStationInteractable[] cleaning = Object.FindObjectsByType<CleaningStationInteractable>( FindObjectsInactive.Exclude );
		for ( int i = 0; i < cleaning.Length; i++ )
		{
			CleaningStationInteractable station = cleaning[ i ];
			if ( station == null )
				continue;
			if ( byId.TryGetValue( station.ResolvePersistenceId(), out GameSaveStationEntry entry ) && entry.kind == "cleaning" )
				station.ApplySave( entry );
		}

		CoinSortingStation[] sorting = Object.FindObjectsByType<CoinSortingStation>( FindObjectsInactive.Exclude );
		for ( int i = 0; i < sorting.Length; i++ )
		{
			CoinSortingStation station = sorting[ i ];
			if ( station == null )
				continue;
			if ( byId.TryGetValue( station.ResolvePersistenceId(), out GameSaveStationEntry entry ) && entry.kind == "coinSorting" )
				station.ApplySave( entry );
		}
	}

	static Dictionary<string, GameSaveStationEntry> IndexStations( List<GameSaveStationEntry> entries )
	{
		Dictionary<string, GameSaveStationEntry> byId = new Dictionary<string, GameSaveStationEntry>();
		for ( int i = 0; i < entries.Count; i++ )
		{
			GameSaveStationEntry entry = entries[ i ];
			if ( entry == null || string.IsNullOrEmpty( entry.persistenceId ) )
				continue;
			byId[ entry.persistenceId ] = entry;
		}

		return byId;
	}

	static void CaptureDisplays( List<GameSaveDisplayTableEntry> into )
	{
		TypedDisplayTableInteractable[] tables = Object.FindObjectsByType<TypedDisplayTableInteractable>( FindObjectsInactive.Exclude );
		for ( int i = 0; i < tables.Length; i++ )
		{
			TypedDisplayTableInteractable table = tables[ i ];
			if ( table == null )
				continue;
			GameSaveDisplayTableEntry entry = new GameSaveDisplayTableEntry();
			table.CaptureSave( entry );
			if ( !string.IsNullOrEmpty( entry.persistenceId ) )
				into.Add( entry );
		}

		MixedDisplayTableInteractable[] mixed = Object.FindObjectsByType<MixedDisplayTableInteractable>( FindObjectsInactive.Exclude );
		for ( int i = 0; i < mixed.Length; i++ )
		{
			MixedDisplayTableInteractable table = mixed[ i ];
			if ( table == null )
				continue;
			GameSaveDisplayTableEntry entry = new GameSaveDisplayTableEntry();
			table.CaptureSave( entry );
			if ( !string.IsNullOrEmpty( entry.persistenceId ) )
				into.Add( entry );
		}
	}

	static void ApplyDisplays( List<GameSaveDisplayTableEntry> entries )
	{
		if ( entries == null )
			return;

		Dictionary<string, GameSaveDisplayTableEntry> byId = new Dictionary<string, GameSaveDisplayTableEntry>();
		for ( int i = 0; i < entries.Count; i++ )
		{
			GameSaveDisplayTableEntry entry = entries[ i ];
			if ( entry == null || string.IsNullOrEmpty( entry.persistenceId ) )
				continue;
			byId[ entry.persistenceId ] = entry;
		}

		TypedDisplayTableInteractable[] tables = Object.FindObjectsByType<TypedDisplayTableInteractable>( FindObjectsInactive.Exclude );
		for ( int i = 0; i < tables.Length; i++ )
		{
			TypedDisplayTableInteractable table = tables[ i ];
			if ( table == null )
				continue;
			if ( byId.TryGetValue( table.ResolvePersistenceId(), out GameSaveDisplayTableEntry entry ) )
				table.ApplySave( entry );
		}

		MixedDisplayTableInteractable[] mixed = Object.FindObjectsByType<MixedDisplayTableInteractable>( FindObjectsInactive.Exclude );
		for ( int i = 0; i < mixed.Length; i++ )
		{
			MixedDisplayTableInteractable table = mixed[ i ];
			if ( table == null )
				continue;
			if ( byId.TryGetValue( table.ResolvePersistenceId(), out GameSaveDisplayTableEntry entry ) )
				table.ApplySave( entry );
		}
	}

	static void CaptureConstellations( List<GameSaveConstellationEntry> into )
	{
		GemConstellationInteractable[] constellations = Object.FindObjectsByType<GemConstellationInteractable>( FindObjectsInactive.Exclude );
		for ( int i = 0; i < constellations.Length; i++ )
		{
			GemConstellationInteractable constellation = constellations[ i ];
			if ( constellation == null )
				continue;
			GameSaveConstellationEntry entry = new GameSaveConstellationEntry();
			constellation.CaptureSave( entry );
			if ( !string.IsNullOrEmpty( entry.persistenceId ) )
				into.Add( entry );
		}
	}

	static void ApplyConstellations( List<GameSaveConstellationEntry> entries )
	{
		if ( entries == null )
			return;

		Dictionary<string, GameSaveConstellationEntry> byId = new Dictionary<string, GameSaveConstellationEntry>();
		for ( int i = 0; i < entries.Count; i++ )
		{
			GameSaveConstellationEntry entry = entries[ i ];
			if ( entry == null || string.IsNullOrEmpty( entry.persistenceId ) )
				continue;
			byId[ entry.persistenceId ] = entry;
		}

		GemConstellationInteractable[] constellations = Object.FindObjectsByType<GemConstellationInteractable>( FindObjectsInactive.Exclude );
		for ( int i = 0; i < constellations.Length; i++ )
		{
			GemConstellationInteractable constellation = constellations[ i ];
			if ( constellation == null )
				continue;
			if ( byId.TryGetValue( constellation.ResolvePersistenceId(), out GameSaveConstellationEntry entry ) )
				constellation.ApplySave( entry );
		}
	}

	static void CaptureChests( List<GameSaveChestEntry> into )
	{
		ChestInteractable[] chests = Object.FindObjectsByType<ChestInteractable>( FindObjectsInactive.Exclude );
		for ( int i = 0; i < chests.Length; i++ )
		{
			ChestInteractable chest = chests[ i ];
			if ( chest == null )
				continue;
			string id = chest.ResolvePersistenceId();
			if ( string.IsNullOrEmpty( id ) )
				continue;
			into.Add( new GameSaveChestEntry
			{
				persistenceId = id,
				state = (int)chest.State,
				destroyed = false
			} );
		}
	}

	static void ApplyChests( List<GameSaveChestEntry> entries )
	{
		if ( entries == null )
			return;

		Dictionary<string, GameSaveChestEntry> byId = new Dictionary<string, GameSaveChestEntry>();
		for ( int i = 0; i < entries.Count; i++ )
		{
			GameSaveChestEntry entry = entries[ i ];
			if ( entry == null || string.IsNullOrEmpty( entry.persistenceId ) )
				continue;
			byId[ entry.persistenceId ] = entry;
		}

		ChestInteractable[] chests = Object.FindObjectsByType<ChestInteractable>( FindObjectsInactive.Exclude );
		for ( int i = 0; i < chests.Length; i++ )
		{
			ChestInteractable chest = chests[ i ];
			if ( chest == null )
				continue;
			string id = chest.ResolvePersistenceId();
			if ( string.IsNullOrEmpty( id ) || !byId.TryGetValue( id, out GameSaveChestEntry entry ) )
				continue;

			ChestState state = ChestState.Locked;
			if ( entry.state >= 0 && entry.state <= (int)ChestState.Opened )
				state = (ChestState)entry.state;
			chest.ApplySavedState( state, entry.destroyed );
		}
	}

	static void CapturePlatforms( List<GameSavePlatformGroupEntry> into )
	{
		IReadOnlyList<FloatingPlatformGroup> groups = FloatingPlatformGroup.ActiveGroups;
		for ( int i = 0; i < groups.Count; i++ )
		{
			FloatingPlatformGroup group = groups[ i ];
			if ( group == null || string.IsNullOrEmpty( group.GroupId ) )
				continue;
			into.Add( new GameSavePlatformGroupEntry
			{
				groupId = group.GroupId,
				activated = group.IsActivated
			} );
		}
	}

	static void ApplyPlatforms( List<GameSavePlatformGroupEntry> entries )
	{
		if ( entries == null )
			return;

		Dictionary<string, bool> byId = new Dictionary<string, bool>();
		for ( int i = 0; i < entries.Count; i++ )
		{
			GameSavePlatformGroupEntry entry = entries[ i ];
			if ( entry == null || string.IsNullOrEmpty( entry.groupId ) )
				continue;
			byId[ entry.groupId ] = entry.activated;
		}

		IReadOnlyList<FloatingPlatformGroup> groups = FloatingPlatformGroup.ActiveGroups;
		for ( int i = 0; i < groups.Count; i++ )
		{
			FloatingPlatformGroup group = groups[ i ];
			if ( group == null || string.IsNullOrEmpty( group.GroupId ) )
				continue;
			if ( !byId.TryGetValue( group.GroupId, out bool activated ) )
				continue;
			group.ApplySavedActivated( activated );
		}
	}
}
