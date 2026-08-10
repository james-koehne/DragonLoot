using UnityEngine;

public class DebugCoinSortingStationSection : DebugOverlaySection
{
	static int s_stationIndex;

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
		var stations = CoinSortingStation.ActiveStations;
		GUILayout.Label( $"Stations: {stations.Count}" );
		if ( stations.Count == 0 )
		{
			GUILayout.Label( "No active CoinSortingStation in scene." );
			return;
		}

		if ( s_stationIndex >= stations.Count )
			s_stationIndex = 0;

		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "◀", GUILayout.Width( 28f ) ) )
			s_stationIndex = ( s_stationIndex + stations.Count - 1 ) % stations.Count;
		GUILayout.Label( $"#{s_stationIndex + 1}: {stations[ s_stationIndex ].name}" );
		if ( GUILayout.Button( "▶", GUILayout.Width( 28f ) ) )
			s_stationIndex = ( s_stationIndex + 1 ) % stations.Count;
		GUILayout.EndHorizontal();

		CoinSortingStation station = stations[ s_stationIndex ];
		GUILayout.Label( $"Station level: {station.StationLevel}" );
		GUILayout.Label( $"Hopper stack: {station.BufferedCount} / {station.HopperCapacity}" );
		GUILayout.Label( $"Processing: {station.IsProcessing}  Crank: {station.IsCrankActive}" );

		if ( GUILayout.Button( "Force process one" ) )
			station.DebugForceProcessOne();
	}
}
