using UnityEngine;

public class DebugArtifactCleaningSection : DebugOverlaySection
{
	static CleaningStationInteractable[] s_stations;
	static float s_stationsCacheTime = -999f;

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
			string heldName = "?";
			if ( item.Definition != null && !string.IsNullOrEmpty( item.Definition.displayName ) )
				heldName = item.Definition.displayName;
			else if ( item.Definition != null )
				heldName = item.Definition.name;
			GUILayout.Label( "Held: " + heldName );
			GUILayout.Label( $"Requires cleaning: {item.RequiresCleaning}" );
			GUILayout.Label( $"Clean: {item.CleanProgress * 100f:0}%" );
			GUILayout.Label( $"IsClean: {item.IsClean}" );

			GUILayout.BeginHorizontal();
			if ( GUILayout.Button( "Set Dirty" ) )
			{
				item.SetDirty();
				DebugOverlay.SetLastAction( "Held item set dirty" );
			}
			if ( GUILayout.Button( "Set Clean" ) )
			{
				item.SetClean();
				DebugOverlay.SetLastAction( "Held item set clean" );
			}
			GUILayout.EndHorizontal();
		}

		if ( player.Cleaning != null )
			GUILayout.Label( $"Manual progress: {player.Cleaning.CleaningProgress * 100f:0}%" );

		CleaningStationInteractable[] stations = DebugOverlayGui.CachedFind( ref s_stations, ref s_stationsCacheTime );
		GUILayout.Label( $"Stations: {( stations != null ? stations.Length : 0 )}" );
		if ( stations != null )
		{
			for ( int i = 0; i < stations.Length; i++ )
			{
				CleaningStationInteractable station = stations[ i ];
				if ( station == null )
					continue;
				string occ = "(empty)";
				if ( station.Occupant != null )
				{
					TreasureDefinition occDef = station.Occupant.Definition;
					if ( occDef != null && !string.IsNullOrEmpty( occDef.displayName ) )
						occ = occDef.displayName;
					else if ( occDef != null )
						occ = occDef.name;
					else
						occ = "?";
				}
				GUILayout.Label( $"  [{i}] unlocked={station.IsUnlocked} occupied={station.IsOccupied} pickup={station.AllowsPickup} item={occ}" );
			}
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
				DebugOverlay.SetLastAction( nextEnabled ? "Cleaning enabled" : "Cleaning disabled (all cleaned)" );
			}

			GUILayout.Label( $"Manual duration: {def.manualCleanDurationSeconds:0.00}s" );
			GUILayout.Label( $"Belt travel: {def.stationBeltTravelSeconds:0.00}s" );
			bool nextUnlocked = GUILayout.Toggle( def.stationUnlocked, "Station unlocked" );
			if ( nextUnlocked != def.stationUnlocked )
			{
				def.stationUnlocked = nextUnlocked;
				DebugOverlay.SetLastAction( nextUnlocked ? "Cleaning station unlocked" : "Cleaning station locked" );
			}
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
