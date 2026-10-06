using System.Collections.Generic;

using UnityEngine;

public class DebugMinecartsSection : DebugOverlaySection
{
	static string s_lastStatus = "";

	public string Title => "Minecarts";

	public void Draw()
	{
		IReadOnlyList<MinecartInteractable> carts = MinecartInteractable.ActiveCarts;
		GUILayout.Label( $"Active carts: {carts.Count}" );

		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Spawn auto cart" ) )
			SpawnNearPlayer( drive: false );
		if ( GUILayout.Button( "Spawn drive cart" ) )
			SpawnNearPlayer( drive: true );
		GUILayout.EndHorizontal();

		MinecartInteractable lead = ResolveNearbyLead();
		if ( lead == null )
		{
			GUILayout.Label( "No nearby consist lead" );
			if ( !string.IsNullOrEmpty( s_lastStatus ) )
				GUILayout.Label( s_lastStatus );
			return;
		}

		GUILayout.Label( lead.name );
		GUILayout.Label( lead.IsDriveCart ? "Kind: Drive" : "Kind: Cargo" );
		GUILayout.Label( $"Followers: {lead.FollowerCount}" );
		GUILayout.Label( $"Along: {lead.DistanceAlongTrack:0.00}  Speed: {lead.AlongTrackSpeed:0.00}" );
		GUILayout.Label( lead.DebugJunctionCommitLabel );

		MinecartJunctionGraph graph = MinecartJunctionGraph.FindActive();
		if ( graph == null )
			GUILayout.Label( "Junction graph: none" );
		else
			GUILayout.Label( $"Junction graph: {graph.JunctionCount} node(s)" );

		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Add cargo car" ) )
			AddCar( lead, drive: false );
		if ( GUILayout.Button( "Add drive car" ) )
			AddCar( lead, drive: true );
		GUILayout.EndHorizontal();

		if ( GUILayout.Button( "Remove last car" ) )
		{
			if ( lead.TryRemoveLastFollower() )
				s_lastStatus = "Removed last car";
			else
				s_lastStatus = "No follower to remove";
		}

		if ( !string.IsNullOrEmpty( s_lastStatus ) )
			GUILayout.Label( s_lastStatus );
	}

	static void SpawnNearPlayer( bool drive )
	{
		PlayerController player = DebugOverlay.GetPlayer();
		if ( player == null )
		{
			s_lastStatus = "No player";
			return;
		}

		MinecartInteractable spawned;
		if ( !MinecartConsistUtility.TrySpawnOnNearestTrack( player.transform.position, drive, out spawned ) )
		{
			s_lastStatus = "No nearby travel-ready track";
			return;
		}

		s_lastStatus = drive ? $"Spawned drive cart ({spawned.name})" : $"Spawned auto cart ({spawned.name})";
	}

	static void AddCar( MinecartInteractable lead, bool drive )
	{
		MinecartInteractable spawned;
		if ( MinecartConsistUtility.TryAddCar( lead, drive, out spawned ) )
			s_lastStatus = drive ? "Added drive car" : "Added cargo car";
		else
			s_lastStatus = drive ? "Failed to add drive car" : "Failed to add cargo car";
	}

	static MinecartInteractable ResolveNearbyLead()
	{
		PlayerController player = DebugOverlay.GetPlayer();
		Vector3 origin = player != null ? player.transform.position : Vector3.zero;
		MinecartInteractable best = null;
		float bestDist = float.MaxValue;
		IReadOnlyList<MinecartInteractable> carts = MinecartInteractable.ActiveCarts;
		for ( int i = 0; i < carts.Count; i++ )
		{
			MinecartInteractable cart = carts[ i ];
			if ( cart == null || !cart.IsConsistLead )
				continue;

			float dist = ( cart.transform.position - origin ).sqrMagnitude;
			if ( dist >= bestDist )
				continue;

			bestDist = dist;
			best = cart;
		}

		return best;
	}
}
