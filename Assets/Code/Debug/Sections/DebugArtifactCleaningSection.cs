using UnityEngine;

public class DebugArtifactCleaningSection : DebugOverlaySection
{
	public string Title => "Artifact Cleaning";

	public void Draw()
	{
		PlayerController player = DebugOverlay.GetPlayer();
		if ( player == null )
		{
			GUILayout.Label( "No player" );
			return;
		}

		PlayerCarry carry = player.Carry;
		if ( carry == null || !carry.TryPeekActive( out TreasureItem item ) || item == null )
		{
			GUILayout.Label( "No held treasure" );
		}
		else
		{
			GUILayout.Label( $"Held: {item.Definition?.displayName ?? "?"}" );
			GUILayout.Label( $"Requires cleaning: {item.RequiresCleaning}" );
			GUILayout.Label( $"Clean: {item.CleanProgress * 100f:0}%" );
			GUILayout.Label( $"IsClean: {item.IsClean}" );

			GUILayout.BeginHorizontal();
			if ( GUILayout.Button( "Set Dirty" ) )
				item.SetDirty();
			if ( GUILayout.Button( "Set Clean" ) )
				item.SetClean();
			GUILayout.EndHorizontal();
		}

		if ( player.Cleaning != null )
			GUILayout.Label( $"Manual progress: {player.Cleaning.CleaningProgress * 100f:0}%" );

		CleaningStationInteractable[] stations = Object.FindObjectsByType<CleaningStationInteractable>(
			FindObjectsInactive.Exclude,
			FindObjectsSortMode.None );
		GUILayout.Label( $"Stations: {stations.Length}" );
		for ( int i = 0; i < stations.Length; i++ )
		{
			CleaningStationInteractable station = stations[ i ];
			if ( station == null )
				continue;
			string occ = station.Occupant != null
				? station.Occupant.Definition?.displayName ?? "?"
				: "(empty)";
			GUILayout.Label( $"  [{i}] unlocked={station.IsUnlocked} occupied={station.IsOccupied} pickup={station.AllowsPickup} item={occ}" );
		}

		TreasureCleaningDefinition def = GameInstance.GetDefinition<TreasureCleaningDefinition>();
		if ( def != null )
		{
			bool nextEnabled = GUILayout.Toggle( def.cleaningEnabled, "Cleaning enabled" );
			if ( nextEnabled != def.cleaningEnabled )
			{
				def.cleaningEnabled = nextEnabled;
				if ( !nextEnabled )
					ForceCleanAllTreasure();
			}

			GUILayout.Label( $"Manual duration: {def.manualCleanDurationSeconds:0.00}s" );
			GUILayout.Label( $"Belt travel: {def.stationBeltTravelSeconds:0.00}s" );
			bool nextUnlocked = GUILayout.Toggle( def.stationUnlocked, "Station unlocked" );
			if ( nextUnlocked != def.stationUnlocked )
				def.stationUnlocked = nextUnlocked;
		}
		else
		{
			GUILayout.Label( "TreasureCleaningDefinition not loaded" );
		}
	}

	static void ForceCleanAllTreasure()
	{
		TreasureItem[] items = Object.FindObjectsByType<TreasureItem>(
			FindObjectsInactive.Include,
			FindObjectsSortMode.None );
		for ( int i = 0; i < items.Length; i++ )
		{
			TreasureItem item = items[ i ];
			if ( item == null )
				continue;
			item.SetClean();
		}
	}
}
