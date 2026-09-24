using System;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Editor-baked topology of overlapping <see cref="MinecartTrack"/> paths.
/// Carts query this at runtime for look-based branch choice near junctions.
/// Create via Dragon Loot → Minecart → Create Junction Graph.
/// </summary>
[DisallowMultipleComponent]
public class MinecartJunctionGraph : MonoBehaviour
{
	static readonly List<MinecartJunctionGraph> All = new List<MinecartJunctionGraph>( 2 );

	const float DeadEndEpsilon = 0.05f;

	[Header( "Bake" )]
	[Tooltip( "Max world distance between rail samples/endpoints to form a junction." )]
	[SerializeField]
	[Min( 0.05f )]
	float detectRadius = 0.75f;

	[Tooltip( "Along-spline sample spacing used when searching for mid-path overlaps." )]
	[SerializeField]
	[Min( 0.1f )]
	float sampleStep = 0.4f;

	[Tooltip( "Merge nearby detections into one junction node." )]
	[SerializeField]
	[Min( 0.05f )]
	float clusterMergeRadius = 1.0f;

	[Tooltip( "Half-length along each port cut from track visual/collider and filled by the junction mesh." )]
	[SerializeField]
	[Min( 0.05f )]
	float junctionMeshGap = 1.0f;

	[Header( "Baked" )]
	[SerializeField]
	List<MinecartJunction> junctions = new List<MinecartJunction>( 8 );

	[SerializeField]
	List<MinecartTrackJunctionIndex> trackIndex = new List<MinecartTrackJunctionIndex>( 16 );

	[Header( "Gizmos" )]
	[SerializeField]
	bool drawGizmosAlways = true;

	[SerializeField]
	float gizmoPortLength = 1.25f;

	public static IReadOnlyList<MinecartJunctionGraph> ActiveGraphs => All;

	public float DetectRadius => Mathf.Max( 0.05f, detectRadius );

	public float SampleStep => Mathf.Max( 0.1f, sampleStep );

	public float ClusterMergeRadius => Mathf.Max( 0.05f, clusterMergeRadius );

	public float JunctionMeshGap => Mathf.Max( 0.05f, junctionMeshGap );

	public IReadOnlyList<MinecartJunction> Junctions => junctions;

	public int JunctionCount => junctions != null ? junctions.Count : 0;

	public static MinecartJunctionGraph FindActive()
	{
		EnsureRegistryPopulated();
		for ( int i = 0; i < All.Count; i++ )
		{
			MinecartJunctionGraph graph = All[ i ];
			if ( graph != null && graph.isActiveAndEnabled )
				return graph;
		}

		return null;
	}

	void OnEnable()
	{
		if ( !All.Contains( this ) )
			All.Add( this );
	}

	void OnDisable()
	{
		All.Remove( this );
	}

	void OnValidate()
	{
		detectRadius = Mathf.Max( 0.05f, detectRadius );
		sampleStep = Mathf.Max( 0.1f, sampleStep );
		clusterMergeRadius = Mathf.Max( 0.05f, clusterMergeRadius );
		junctionMeshGap = Mathf.Max( 0.05f, junctionMeshGap );
		gizmoPortLength = Mathf.Max( 0.1f, gizmoPortLength );
	}

	static void EnsureRegistryPopulated()
	{
		if ( All.Count > 0 )
			return;

		MinecartJunctionGraph[] found = FindObjectsByType<MinecartJunctionGraph>( FindObjectsInactive.Exclude, FindObjectsSortMode.None );
		for ( int i = 0; i < found.Length; i++ )
		{
			MinecartJunctionGraph candidate = found[ i ];
			if ( candidate != null && !All.Contains( candidate ) )
				All.Add( candidate );
		}
	}

	public MinecartJunction GetJunction( int index )
	{
		if ( junctions == null || index < 0 || index >= junctions.Count )
			return null;

		return junctions[ index ];
	}

	/// <summary>
	/// Finds the nearest junction on <paramref name="track"/> whose along-track distance
	/// is within <paramref name="approachRadius"/> of <paramref name="distanceAlongTrack"/>.
	/// </summary>
	public bool TryFindApproachJunction( MinecartTrack track, float distanceAlongTrack, float approachRadius, out int junctionIndex, out float junctionDistance )
	{
		junctionIndex = -1;
		junctionDistance = 0f;
		if ( track == null || !track.IsUsable || approachRadius < 0f )
			return false;

		if ( trackIndex == null || trackIndex.Count == 0 )
			return false;

		float bestAbs = approachRadius;
		bool found = false;
		for ( int i = 0; i < trackIndex.Count; i++ )
		{
			MinecartTrackJunctionIndex entry = trackIndex[ i ];
			if ( entry.track != track )
				continue;

			float sep = Mathf.Abs( track.SignedAlong( distanceAlongTrack, entry.distance ) );
			if ( sep > bestAbs )
				continue;

			bestAbs = sep;
			junctionIndex = entry.junctionIndex;
			junctionDistance = entry.distance;
			found = true;
		}

		return found;
	}

	/// <summary>
	/// True when the cart has left the approach zone of a previously committed junction.
	/// </summary>
	public bool IsOutsideApproach( MinecartTrack track, float distanceAlongTrack, int junctionIndex, float approachRadius )
	{
		if ( track == null || junctionIndex < 0 || approachRadius < 0f )
			return true;

		float junctionDistance;
		if ( !TryGetPortDistance( track, junctionIndex, out junctionDistance ) )
			return true;

		float sep = Mathf.Abs( track.SignedAlong( distanceAlongTrack, junctionDistance ) );
		return sep > approachRadius;
	}

	public bool TryGetPortDistance( MinecartTrack track, int junctionIndex, out float distance )
	{
		distance = 0f;
		MinecartJunction junction = GetJunction( junctionIndex );
		if ( junction == null || junction.ports == null || track == null )
			return false;

		for ( int i = 0; i < junction.ports.Count; i++ )
		{
			MinecartJunctionPort port = junction.ports[ i ];
			if ( port.track != track )
				continue;

			distance = port.distance;
			return true;
		}

		return false;
	}

	public bool TryGetPort( MinecartTrack track, int junctionIndex, out MinecartJunctionPort port )
	{
		port = default;
		MinecartJunction junction = GetJunction( junctionIndex );
		if ( junction == null || junction.ports == null || track == null )
			return false;

		for ( int i = 0; i < junction.ports.Count; i++ )
		{
			MinecartJunctionPort candidate = junction.ports[ i ];
			if ( candidate.track != track )
				continue;

			port = candidate;
			return true;
		}

		return false;
	}

	/// <summary>
	/// Builds valid leave-options at a junction, excluding reverse of current travel on the arrival track.
	/// </summary>
	public int BuildExits( int junctionIndex, MinecartTrack arrivalTrack, int travelSign, List<MinecartJunctionExit> results )
	{
		if ( results == null )
			return 0;

		results.Clear();
		MinecartJunction junction = GetJunction( junctionIndex );
		if ( junction == null || junction.ports == null )
			return 0;

		int sign = travelSign >= 0 ? 1 : -1;
		for ( int i = 0; i < junction.ports.Count; i++ )
		{
			MinecartJunctionPort port = junction.ports[ i ];
			if ( port.track == null || !port.track.IsUsable )
				continue;

			TryAddExit( results, port, 1, arrivalTrack, sign );
			TryAddExit( results, port, -1, arrivalTrack, sign );
		}

		return results.Count;
	}

	static void TryAddExit( List<MinecartJunctionExit> results, MinecartJunctionPort port, int exitSign, MinecartTrack arrivalTrack, int arrivalTravelSign )
	{
		if ( port.track == arrivalTrack && exitSign == -arrivalTravelSign )
			return;

		if ( !IsValidExitDirection( port, exitSign ) )
			return;

		Vector3 tangent = port.tangent;
		if ( tangent.sqrMagnitude < 0.0001f )
			return;

		results.Add( new MinecartJunctionExit
		{
			track = port.track,
			distance = port.distance,
			travelSign = exitSign,
			worldTangent = tangent * exitSign
		} );
	}

	static bool IsValidExitDirection( MinecartJunctionPort port, int exitSign )
	{
		MinecartTrack track = port.track;
		if ( track == null || !track.IsUsable )
			return false;

		if ( track.IsClosed )
			return true;

		float length = track.Length;
		if ( exitSign > 0 && port.distance >= length - DeadEndEpsilon )
			return false;

		if ( exitSign < 0 && port.distance <= DeadEndEpsilon )
			return false;

		return true;
	}

	public void EditorApplyBake( List<MinecartJunction> bakedJunctions, List<MinecartTrackJunctionIndex> bakedIndex )
	{
		junctions = bakedJunctions != null ? bakedJunctions : new List<MinecartJunction>( 0 );
		trackIndex = bakedIndex != null ? bakedIndex : new List<MinecartTrackJunctionIndex>( 0 );
	}

	public void CollectGapDistances( MinecartTrack track, List<float> distances )
	{
		if ( distances == null )
			return;

		distances.Clear();
		if ( track == null || trackIndex == null )
			return;

		for ( int i = 0; i < trackIndex.Count; i++ )
		{
			MinecartTrackJunctionIndex entry = trackIndex[ i ];
			if ( entry.track != track )
				continue;

			distances.Add( entry.distance );
		}
	}

	public bool IsDistanceInMeshGap( MinecartTrack track, float distance )
	{
		if ( track == null || trackIndex == null || trackIndex.Count == 0 )
			return false;

		float gap = JunctionMeshGap;
		for ( int i = 0; i < trackIndex.Count; i++ )
		{
			MinecartTrackJunctionIndex entry = trackIndex[ i ];
			if ( entry.track != track )
				continue;

			if ( Mathf.Abs( track.SignedAlong( distance, entry.distance ) ) <= gap )
				return true;
		}

		return false;
	}

	public static bool IsTrackDistanceInMeshGap( MinecartTrack track, float distance )
	{
		MinecartJunctionGraph graph = FindActive();
		if ( graph == null )
			return false;

		return graph.IsDistanceInMeshGap( track, distance );
	}

	void OnDrawGizmos()
	{
		if ( drawGizmosAlways )
			DrawJunctionGizmos();
	}

	void OnDrawGizmosSelected()
	{
		if ( !drawGizmosAlways )
			DrawJunctionGizmos();
	}

	void DrawJunctionGizmos()
	{
		if ( junctions == null )
			return;

		float len = Mathf.Max( 0.1f, gizmoPortLength );
		for ( int i = 0; i < junctions.Count; i++ )
		{
			MinecartJunction junction = junctions[ i ];
			if ( junction == null )
				continue;

			Gizmos.color = new Color( 1f, 0.55f, 0.1f, 0.9f );
			Gizmos.DrawWireSphere( junction.worldPosition, 0.35f );
			Gizmos.DrawSphere( junction.worldPosition, 0.12f );

			if ( junction.ports == null )
				continue;

			for ( int p = 0; p < junction.ports.Count; p++ )
			{
				MinecartJunctionPort port = junction.ports[ p ];
				Vector3 tan = port.tangent;
				if ( tan.sqrMagnitude < 0.0001f )
					continue;

				tan.Normalize();
				Gizmos.color = new Color( 0.2f, 0.85f, 1f, 0.95f );
				Gizmos.DrawLine( junction.worldPosition, junction.worldPosition + tan * len );
				Gizmos.color = new Color( 1f, 0.3f, 0.55f, 0.85f );
				Gizmos.DrawLine( junction.worldPosition, junction.worldPosition - tan * len );
			}
		}
	}
}

[Serializable]
public class MinecartJunction
{
	public Vector3 worldPosition;
	public List<MinecartJunctionPort> ports = new List<MinecartJunctionPort>( 4 );
}

[Serializable]
public struct MinecartJunctionPort
{
	public MinecartTrack track;
	public float distance;
	public Vector3 tangent;
}

[Serializable]
public struct MinecartTrackJunctionIndex
{
	public MinecartTrack track;
	public float distance;
	public int junctionIndex;
}

public struct MinecartJunctionExit
{
	public MinecartTrack track;
	public float distance;
	public int travelSign;
	public Vector3 worldTangent;
}
