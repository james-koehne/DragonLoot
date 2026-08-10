using System.Collections;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Tracks same-type gem pyramid clusters. Clusters do not own items.
/// </summary>
public static class GemPyramidRegistry
{
	static readonly List<GemPyramidCluster> All = new List<GemPyramidCluster>( 32 );
	static readonly HashSet<TreasureItem> TuckInFlight = new HashSet<TreasureItem>();
	static bool _suppressJoin;

	public static IReadOnlyList<GemPyramidCluster> ActiveClusters => All;

	public static bool AreSameGemType( TreasureDefinition a, TreasureDefinition b )
	{
		if ( a == b )
			return true;
		if ( a == null || b == null )
			return false;
		if ( !string.IsNullOrEmpty( a.id ) && a.id == b.id )
			return true;
		return false;
	}

	public static GemPyramidCluster FindClusterContaining( TreasureItem item )
	{
		if ( item == null )
			return null;

		for ( int i = 0; i < All.Count; i++ )
		{
			GemPyramidCluster cluster = All[ i ];
			if ( cluster != null && cluster.Contains( item ) )
				return cluster;
		}

		return null;
	}

	public static GemPyramidCluster FindNearestCompatible(
		TreasureDefinition definition,
		Vector3 worldPos,
		TreasureSurfaceDefinition surfaceDef )
	{
		if ( definition == null || definition.category != TreasureCategory.Gem )
			return null;

		float bestDist = float.MaxValue;
		GemPyramidCluster best = null;
		for ( int i = 0; i < All.Count; i++ )
		{
			GemPyramidCluster cluster = All[ i ];
			if ( cluster == null || cluster.Definition == null )
				continue;
			if ( !AreSameGemType( definition, cluster.Definition ) )
				continue;
			if ( cluster.Count <= 0 )
				continue;

			if ( !cluster.IsInJoinRange( worldPos, surfaceDef ) )
				continue;

			float dist = cluster.GetDistanceXZ( worldPos );
			if ( dist >= bestDist )
				continue;

			bestDist = dist;
			best = cluster;
		}

		return best;
	}

	public static GemPyramidCluster CreateCluster( TreasureDefinition definition, Vector3 contact )
	{
		GemPyramidCluster cluster = new GemPyramidCluster( definition, contact );
		All.Add( cluster );
		return cluster;
	}

	public static void DestroyCluster( GemPyramidCluster cluster )
	{
		if ( cluster == null )
			return;
		All.Remove( cluster );
	}

	/// <summary>
	/// On place/settle: join same-type pyramid. Returns true if the gem tucked into /
	/// joined a multi-gem cluster (caller may skip other settle handlers).
	/// </summary>
	public static bool TryJoin( TreasureItem gem, bool animate )
	{
		if ( _suppressJoin || gem == null || gem.Definition == null )
			return false;
		if ( gem.Definition.category != TreasureCategory.Gem )
			return false;
		if ( gem.IsInFlight || TuckInFlight.Contains( gem ) )
			return false;

		TreasureSurfaceDefinition surfaceDef = ResolveSurfaceDef();
		if ( surfaceDef == null )
			return false;

		GemPyramidCluster existing = FindClusterContaining( gem );
		if ( existing != null )
			return existing.Count > 1;

		Vector3 pos = gem.transform.position;
		GemPyramidCluster nearby = FindNearestCompatible( gem.Definition, pos, surfaceDef );
		if ( nearby == null )
		{
			nearby = CreateCluster( gem.Definition, pos );
			nearby.AddOrUpdate( gem, surfaceDef, animateJoining: false );
			return false;
		}

		if ( nearby.IsFull( surfaceDef ) )
		{
			nearby = CreateCluster( gem.Definition, pos );
			nearby.AddOrUpdate( gem, surfaceDef, animateJoining: false );
			return false;
		}

		int before = nearby.Count;
		nearby.AddOrUpdate( gem, surfaceDef, animateJoining: animate );
		return nearby.Count > before || nearby.Count > 1;
	}

	/// <summary>
	/// Preview / place end pose: next lattice slot if a same-type cluster is in range.
	/// </summary>
	public static bool TryGetJoinPose(
		TreasureItem gem,
		Vector3 proposedPos,
		out Vector3 joinPos,
		out Quaternion joinRot )
	{
		joinPos = proposedPos;
		joinRot = gem != null ? gem.transform.rotation : Quaternion.identity;
		if ( gem == null || gem.Definition == null || gem.Definition.category != TreasureCategory.Gem )
			return false;

		TreasureSurfaceDefinition surfaceDef = ResolveSurfaceDef();
		if ( surfaceDef == null )
			return false;

		GemPyramidCluster nearby = FindNearestCompatible( gem.Definition, proposedPos, surfaceDef );
		if ( nearby == null || nearby.IsFull( surfaceDef ) )
			return false;

		return nearby.TryPreviewNextPose( gem, surfaceDef, out joinPos, out joinRot );
	}

	public static void NotifyRemoved( TreasureItem gem )
	{
		if ( gem == null )
			return;

		TuckInFlight.Remove( gem );
		GemPyramidCluster cluster = FindClusterContaining( gem );
		if ( cluster == null )
			return;

		TreasureSurfaceDefinition surfaceDef = ResolveSurfaceDef();
		// Leave holes — do not collapse / shift the remaining pyramid.
		cluster.Remove( gem, surfaceDef, animateCollapse: false );
	}

	public static bool IsTucking( TreasureItem gem )
	{
		return gem != null && TuckInFlight.Contains( gem );
	}

	public static void ApplySettledPoseSuppressed( TreasureItem gem, Vector3 pos, Quaternion rot )
	{
		_suppressJoin = true;
		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		// Avoid snapToSeat — that flattens stacked pyramid gems onto the ground.
		gem.EnterSurface( pos, rot, Vector3.zero, snapToSeat: false );
		if ( world != null && world.Simulator != null )
			world.Simulator.ForceSleep( gem );
		gem.transform.SetPositionAndRotation( pos, rot );
		gem.SyncRigidbodyToTransform();
		_suppressJoin = false;
	}

	public static void TuckGemToPose(
		TreasureItem gem,
		Vector3 endPos,
		Quaternion endRot,
		TreasureSurfaceDefinition surfaceDef )
	{
		if ( gem == null )
			return;

		Vector3 start = gem.transform.position;
		if ( ( start - endPos ).sqrMagnitude < 0.0004f )
		{
			ApplySettledPoseSuppressed( gem, endPos, endRot );
			return;
		}

		if ( TuckInFlight.Contains( gem ) )
			return;

		TuckInFlight.Add( gem );

		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		if ( world != null && world.Simulator != null )
			world.Simulator.Unregister( gem );

		gem.BeginFlight();
		TreasureMotionHost.Run( TuckRoutine( gem, endPos, endRot, surfaceDef ) );
	}

	static IEnumerator TuckRoutine(
		TreasureItem gem,
		Vector3 endPos,
		Quaternion endRot,
		TreasureSurfaceDefinition surfaceDef )
	{
		float duration = surfaceDef != null ? surfaceDef.gemPyramidTuckDuration : 0.28f;
		float hop = surfaceDef != null ? surfaceDef.gemPyramidTuckHopHeight : 0.12f;
		duration = Mathf.Max( 0.05f, duration );

		List<TreasureItem> list = new List<TreasureItem>( 1 ) { gem };
		Vector3[] ends = { endPos };
		Quaternion[] rots = { endRot };

		yield return CoinFlipMotion.AnimateWorldFlips(
			list,
			ends,
			rots,
			duration,
			arcHeight: hop * 0.35f,
			spins: 0f,
			useHopThenArc: true,
			hopHeight: hop,
			riseFraction: 0.32f,
			secondaryArcHeight: hop * 0.25f );

		TuckInFlight.Remove( gem );
		if ( gem == null )
			yield break;

		gem.EndFlight();
		if ( gem.State == TreasureItemState.Held )
			yield break;

		_suppressJoin = true;
		TreasureSurfaceWorld settleWorld = TreasureSurfaceWorld.Instance;
		gem.EnterSurface( endPos, endRot, Vector3.zero, snapToSeat: false );
		if ( settleWorld != null && settleWorld.Simulator != null )
			settleWorld.Simulator.ForceSleep( gem );
		gem.transform.SetPositionAndRotation( endPos, endRot );
		gem.SyncRigidbodyToTransform();
		_suppressJoin = false;
	}

	static TreasureSurfaceDefinition ResolveSurfaceDef()
	{
		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		return world != null ? world.Definition : null;
	}
}
