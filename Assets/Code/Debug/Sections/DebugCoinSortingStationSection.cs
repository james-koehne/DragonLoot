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
		string upgradeId = CoinSortingStationDefinition.DefaultUpgradeId;
		CoinSortingStationDefinition def = null;
		def = RuntimeDefinition.Resolve( ref def );
		if ( def != null )
			upgradeId = def.ResolveUpgradeId();

		GUILayout.Label( $"Upgrade: {upgradeId}" );
		if ( system != null )
		{
			int level = system.GetUpgradeLevel( upgradeId );
			int max = system.GetMaxLevel( upgradeId );
			GUILayout.Label( $"Level: {level} / {max}" );

			GUILayout.BeginHorizontal();
			if ( GUILayout.Button( "L1" ) )
				system.SetUpgradeLevel( upgradeId, 1 );
			if ( GUILayout.Button( "L2" ) )
				system.SetUpgradeLevel( upgradeId, 2 );
			if ( GUILayout.Button( "L3" ) )
				system.SetUpgradeLevel( upgradeId, 3 );
			if ( GUILayout.Button( "L4" ) )
				system.SetUpgradeLevel( upgradeId, 4 );
			GUILayout.EndHorizontal();
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
		GUILayout.Label( $"Station level: {station.StationLevel}" );
		GUILayout.Label( $"Hopper stack: {station.BufferedCount} / {station.HopperCapacity}" );
		GUILayout.Label( $"Processing: {station.IsProcessing}  Crank: {station.IsCrankActive}" );
		GUILayout.Label( $"Reserve: {station.ReserveSeconds:0.00}s  Fill: {station.ReserveNormalized:0.00}" );
		GUILayout.Label( $"Charging: {station.IsCranking}  Discharging: {station.IsReserveDischarging}" );

		if ( GUILayout.Button( "Force process one" ) )
			station.DebugForceProcessOne();
	}
}
