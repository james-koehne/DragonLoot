using System.Collections.Generic;

using UnityEngine;

public class DebugCoinSortingStationSection : DebugOverlaySection
{
	static int s_stationIndex;
	static string s_stationSearch = string.Empty;
	static readonly List<CoinSortingStation> s_stationList = new List<CoinSortingStation>( 8 );

	public string Title => "Coin Sorting Station";

	public void Draw()
	{
		PlayerController player = DebugOverlay.GetPlayer();
		if ( player != null )
			UpgradeSystem.Ensure( player );

		UpgradeSystem system = UpgradeSystem.Instance;
		if ( system != null )
		{
			DrawUpgradeToggle( system, UpgradeDefinition.CoinSorterAutomaticId, "Automatic" );
			DrawSpeedButtons( system );
			DrawUpgradeToggle( system, UpgradeDefinition.CoinSorterHopperId, "Larger Hopper" );
		}
		else
		{
			GUILayout.Label( "No upgrade system" );
		}

		GUILayout.Space( 6f );
		IReadOnlyList<CoinSortingStation> stations = CoinSortingStation.ActiveStations;
		GUILayout.Label( $"Stations: {stations.Count}" );
		if ( stations.Count == 0 )
		{
			GUILayout.Label( "No active CoinSortingStation in scene." );
			return;
		}

		s_stationList.Clear();
		for ( int i = 0; i < stations.Count; i++ )
		{
			if ( stations[ i ] != null )
				s_stationList.Add( stations[ i ] );
		}

		if ( s_stationList.Count == 0 )
		{
			GUILayout.Label( "No active CoinSortingStation in scene." );
			return;
		}

		if ( s_stationIndex >= s_stationList.Count )
			s_stationIndex = 0;

		DebugOverlayGui.DrawFilteredPicker(
			s_stationList,
			station => station.name,
			ref s_stationIndex,
			ref s_stationSearch,
			"No stations" );

		if ( s_stationIndex < 0 || s_stationIndex >= s_stationList.Count )
			return;

		CoinSortingStation station = s_stationList[ s_stationIndex ];
		GUILayout.Label( $"Auto: {station.HasAutomatic}  Speed L{station.SpeedLevel}  Hopper+: {station.HasLargerHopper}" );
		GUILayout.Label( $"Hopper stack: {station.BufferedCount} / {station.HopperCapacity}" );
		GUILayout.Label( $"Processing: {station.IsProcessing}  Crank: {station.IsCrankActive}" );
		GUILayout.Label( $"Reserve: {station.ReserveSeconds:0.00}s  Fill: {station.ReserveNormalized:0.00}" );
		GUILayout.Label( $"Charging: {station.IsCranking}  Discharging: {station.IsReserveDischarging}" );

		if ( GUILayout.Button( "Force process one" ) )
			station.DebugForceProcessOne();
	}

	static void DrawUpgradeToggle( UpgradeSystem system, string id, string label )
	{
		int level = system.GetUpgradeLevel( id );
		GUILayout.BeginHorizontal();
		GUILayout.Label( $"{label}: {( level >= 1 ? "ON" : "OFF" )}" );
		if ( GUILayout.Button( "On" ) )
			system.SetUpgradeLevel( id, 1 );
		if ( GUILayout.Button( "Off" ) )
			system.ResetUpgrade( id );
		GUILayout.EndHorizontal();
	}

	static void DrawSpeedButtons( UpgradeSystem system )
	{
		int level = system.GetUpgradeLevel( UpgradeDefinition.CoinSorterSpeedId );
		GUILayout.Label( $"Speed: L{level} (0 base / 1 +25% / 2 +50%)" );
		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "L0" ) )
			system.ResetUpgrade( UpgradeDefinition.CoinSorterSpeedId );
		if ( GUILayout.Button( "L1 +25%" ) )
			system.SetUpgradeLevel( UpgradeDefinition.CoinSorterSpeedId, 1 );
		if ( GUILayout.Button( "L2 +50%" ) )
			system.SetUpgradeLevel( UpgradeDefinition.CoinSorterSpeedId, 2 );
		GUILayout.EndHorizontal();
	}
}
