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

	[Header( "Visual Tweaks" )]
	[Tooltip( "How far entries/through rails extend past the track cut so they tuck under existing rails." )]
	[SerializeField]
	[Min( 0f )]
	float joinOverlap = 0.22f;

	[Tooltip( "Bezier control distance as a fraction of average entry radius from the junction center." )]
	[SerializeField]
	[Range( 0.1f, 1.5f )]
	float cornerControlScale = 0.85f;

	[Tooltip( "Sample rings per corner turn curve." )]
	[SerializeField]
	[Min( 4 )]
	int curveSamples = 12;

	[Tooltip( "Half-width of the shaped sleeper strip under junction rails. 0 = use track sleeperSize.x * 0.5." )]
	[SerializeField]
	[Min( 0f )]
	float sleeperHalfWidth = 0f;

	[SerializeField]
	bool drawTurnCurves = true;

	[SerializeField]
	bool drawShapedSleeper = true;

	[Header( "Baked" )]
	[SerializeField]
	List<MinecartJunction> junctions = new List<MinecartJunction>( 8 );

	[SerializeField]
	List<MinecartTrackJunctionIndex> trackIndex = new List<MinecartTrackJunctionIndex>( 16 );

	[Header( "Gizmos" )]
	[SerializeField]
	bool drawGizmosAlways = true;

	[SerializeField]
	bool drawDebugGizmos = true;

	[SerializeField]
	float gizmoPortLength = 1.25f;

	[SerializeField]
	List<MinecartJunctionDebugEntry> debugEntries = new List<MinecartJunctionDebugEntry>( 8 );

	[SerializeField]
	List<MinecartJunctionDebugCorner> debugCorners = new List<MinecartJunctionDebugCorner>( 4 );

	[SerializeField]
	List<MinecartJunctionDebugPolyline> debugThroughs = new List<MinecartJunctionDebugPolyline>( 4 );

	[SerializeField]
	List<MinecartJunctionDebugPolyline> debugTurns = new List<MinecartJunctionDebugPolyline>( 4 );

	public static IReadOnlyList<MinecartJunctionGraph> ActiveGraphs => All;

	public float DetectRadius => Mathf.Max( 0.05f, detectRadius );

	public float SampleStep => Mathf.Max( 0.1f, sampleStep );

	public float ClusterMergeRadius => Mathf.Max( 0.05f, clusterMergeRadius );

	public float JunctionMeshGap => Mathf.Max( 0.05f, junctionMeshGap );

	public float JoinOverlap => Mathf.Max( 0f, joinOverlap );

	public float CornerControlScale => Mathf.Clamp( cornerControlScale, 0.1f, 1.5f );

	public int CurveSamples => Mathf.Max( 4, curveSamples );

	public float SleeperHalfWidthOverride => Mathf.Max( 0f, sleeperHalfWidth );

	public bool DrawTurnCurves => drawTurnCurves;

	public bool DrawShapedSleeper => drawShapedSleeper;

	public bool DrawDebugGizmos => drawDebugGizmos;

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
		joinOverlap = Mathf.Max( 0f, joinOverlap );
		cornerControlScale = Mathf.Clamp( cornerControlScale, 0.1f, 1.5f );
		curveSamples = Mathf.Max( 4, curveSamples );
		sleeperHalfWidth = Mathf.Max( 0f, sleeperHalfWidth );
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

	public static bool IsValidExitDirection( MinecartJunctionPort port, int exitSign )
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

	public static MinecartJunctionKind ResolveKind( MinecartJunctionKind preferred, List<MinecartJunctionPort> ports )
	{
		if ( preferred == MinecartJunctionKind.Cross || preferred == MinecartJunctionKind.Tee )
			return preferred;

		if ( ports == null || ports.Count == 0 )
			return MinecartJunctionKind.Cross;

		int stubs = 0;
		for ( int i = 0; i < ports.Count; i++ )
		{
			MinecartJunctionPort port = ports[ i ];
			bool canPos = IsValidExitDirection( port, 1 );
			bool canNeg = IsValidExitDirection( port, -1 );
			if ( canPos && canNeg )
				continue;

			if ( canPos || canNeg )
				stubs++;
		}

		return stubs > 0 ? MinecartJunctionKind.Tee : MinecartJunctionKind.Cross;
	}

	public bool TryFindRidePath( int junctionIndex, MinecartTrack fromTrack, int intoTravelSign, MinecartTrack toTrack, int outTravelSign, out MinecartJunctionRidePath path )
	{
		path = null;
		MinecartJunction junction = GetJunction( junctionIndex );
		if ( junction == null || junction.ridePaths == null || fromTrack == null || toTrack == null )
			return false;

		int intoSign = intoTravelSign >= 0 ? 1 : -1;
		int outSign = outTravelSign >= 0 ? 1 : -1;
		for ( int i = 0; i < junction.ridePaths.Count; i++ )
		{
			MinecartJunctionRidePath candidate = junction.ridePaths[ i ];
			if ( candidate == null || candidate.fromTrack != fromTrack || candidate.toTrack != toTrack )
				continue;

			if ( candidate.intoTravelSign != intoSign || candidate.outTravelSign != outSign )
				continue;

			if ( candidate.worldPoints == null || candidate.worldPoints.Count < 2 )
				continue;

			path = candidate;
			return true;
		}

		return false;
	}

	public void EditorApplyBake( List<MinecartJunction> bakedJunctions, List<MinecartTrackJunctionIndex> bakedIndex )
	{
		junctions = bakedJunctions != null ? bakedJunctions : new List<MinecartJunction>( 0 );
		trackIndex = bakedIndex != null ? bakedIndex : new List<MinecartTrackJunctionIndex>( 0 );
	}

	public MinecartJunctionKind EditorFindPreservedKind( Vector3 worldPosition )
	{
		if ( junctions == null )
			return MinecartJunctionKind.Auto;

		float merge = ClusterMergeRadius;
		float best = merge * merge;
		MinecartJunctionKind kind = MinecartJunctionKind.Auto;
		for ( int i = 0; i < junctions.Count; i++ )
		{
			MinecartJunction junction = junctions[ i ];
			if ( junction == null )
				continue;

			float sqr = ( junction.worldPosition - worldPosition ).sqrMagnitude;
			if ( sqr > best )
				continue;

			best = sqr;
			kind = junction.kind;
		}

		return kind;
	}

	public void EditorClearDebugBake()
	{
		if ( debugEntries == null )
			debugEntries = new List<MinecartJunctionDebugEntry>( 8 );
		else
			debugEntries.Clear();

		if ( debugCorners == null )
			debugCorners = new List<MinecartJunctionDebugCorner>( 4 );
		else
			debugCorners.Clear();

		if ( debugThroughs == null )
			debugThroughs = new List<MinecartJunctionDebugPolyline>( 4 );
		else
			debugThroughs.Clear();

		if ( debugTurns == null )
			debugTurns = new List<MinecartJunctionDebugPolyline>( 4 );
		else
			debugTurns.Clear();
	}

	public void EditorAddDebugEntry( Vector3 worldPos, Vector3 inwardTangent, Color color )
	{
		if ( debugEntries == null )
			debugEntries = new List<MinecartJunctionDebugEntry>( 8 );

		debugEntries.Add( new MinecartJunctionDebugEntry
		{
			worldPos = worldPos,
			inwardTangent = inwardTangent,
			color = color
		} );
	}

	public void EditorAddDebugCorner( Vector3 start, Vector3 control, Vector3 end )
	{
		if ( debugCorners == null )
			debugCorners = new List<MinecartJunctionDebugCorner>( 4 );

		debugCorners.Add( new MinecartJunctionDebugCorner
		{
			start = start,
			control = control,
			end = end
		} );
	}

	public void EditorAddDebugPolyline( bool turn, List<Vector3> worldPoints )
	{
		if ( worldPoints == null || worldPoints.Count < 2 )
			return;

		MinecartJunctionDebugPolyline line = new MinecartJunctionDebugPolyline
		{
			points = new List<Vector3>( worldPoints )
		};
		if ( turn )
		{
			if ( debugTurns == null )
				debugTurns = new List<MinecartJunctionDebugPolyline>( 4 );
			debugTurns.Add( line );
		}
		else
		{
			if ( debugThroughs == null )
				debugThroughs = new List<MinecartJunctionDebugPolyline>( 4 );
			debugThroughs.Add( line );
		}
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
		if ( drawDebugGizmos && drawGizmosAlways )
			DrawDebugBakeGizmos();
	}

	void OnDrawGizmosSelected()
	{
		if ( !drawGizmosAlways )
			DrawJunctionGizmos();
		if ( drawDebugGizmos )
			DrawDebugBakeGizmos();
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

	void DrawDebugBakeGizmos()
	{
		if ( debugEntries != null )
		{
			for ( int i = 0; i < debugEntries.Count; i++ )
			{
				MinecartJunctionDebugEntry entry = debugEntries[ i ];
				Gizmos.color = entry.color.a > 0.01f ? entry.color : Color.yellow;
				Gizmos.DrawSphere( entry.worldPos, 0.12f );
				Vector3 inward = entry.inwardTangent;
				if ( inward.sqrMagnitude > 0.0001f )
					Gizmos.DrawRay( entry.worldPos, inward.normalized * 0.6f );
			}
		}

		if ( debugCorners != null )
		{
			Gizmos.color = new Color( 1f, 0.9f, 0.1f, 1f );
			for ( int i = 0; i < debugCorners.Count; i++ )
			{
				MinecartJunctionDebugCorner corner = debugCorners[ i ];
				Gizmos.DrawLine( corner.start, corner.control );
				Gizmos.DrawLine( corner.control, corner.end );
				Gizmos.DrawWireSphere( corner.control, 0.1f );
				Gizmos.DrawWireSphere( corner.start, 0.08f );
				Gizmos.DrawWireSphere( corner.end, 0.08f );
			}
		}

		DrawDebugPolylines( debugThroughs, new Color( 0.2f, 1f, 0.4f, 1f ) );
		DrawDebugPolylines( debugTurns, new Color( 1f, 0.35f, 0.9f, 1f ) );
	}

	static void DrawDebugPolylines( List<MinecartJunctionDebugPolyline> lines, Color color )
	{
		if ( lines == null )
			return;

		Gizmos.color = color;
		for ( int i = 0; i < lines.Count; i++ )
		{
			MinecartJunctionDebugPolyline line = lines[ i ];
			if ( line == null || line.points == null || line.points.Count < 2 )
				continue;

			for ( int p = 0; p < line.points.Count - 1; p++ )
				Gizmos.DrawLine( line.points[ p ], line.points[ p + 1 ] );
		}
	}
}

public enum MinecartJunctionKind
{
	Auto = 0,
	Cross = 1,
	Tee = 2
}

[Serializable]
public class MinecartJunction
{
	public Vector3 worldPosition;

	[Tooltip( "Auto detects Cross (X) vs Tee (T) from ports. Override to force." )]
	public MinecartJunctionKind kind = MinecartJunctionKind.Auto;

	[Tooltip( "Resolved kind after bake (Auto → Cross or Tee)." )]
	public MinecartJunctionKind resolvedKind = MinecartJunctionKind.Cross;

	public List<MinecartJunctionPort> ports = new List<MinecartJunctionPort>( 4 );

	public List<MinecartJunctionRidePath> ridePaths = new List<MinecartJunctionRidePath>( 8 );
}

[Serializable]
public class MinecartJunctionRidePath
{
	public MinecartTrack fromTrack;
	public float fromDistance;
	public int intoTravelSign;
	public MinecartTrack toTrack;
	public float toDistance;
	public int outTravelSign;
	public List<Vector3> worldPoints = new List<Vector3>( 12 );
	public float length;

	public bool TryEvaluate( float distanceAlong, out Vector3 worldPos, out Vector3 worldTangent )
	{
		worldPos = Vector3.zero;
		worldTangent = Vector3.forward;
		if ( worldPoints == null || worldPoints.Count < 2 )
			return false;

		float target = Mathf.Clamp( distanceAlong, 0f, Mathf.Max( 0.0001f, length ) );
		float traveled = 0f;
		for ( int i = 0; i < worldPoints.Count - 1; i++ )
		{
			Vector3 a = worldPoints[ i ];
			Vector3 b = worldPoints[ i + 1 ];
			float seg = Vector3.Distance( a, b );
			if ( seg < 0.00001f )
				continue;

			if ( traveled + seg >= target - 0.00001f )
			{
				float t = Mathf.Clamp01( ( target - traveled ) / seg );
				worldPos = Vector3.Lerp( a, b, t );
				Vector3 tan = b - a;
				if ( tan.sqrMagnitude < 0.0001f )
					tan = Vector3.forward;
				else
					tan.Normalize();

				worldTangent = tan;
				return true;
			}

			traveled += seg;
		}

		worldPos = worldPoints[ worldPoints.Count - 1 ];
		Vector3 endTan = worldPoints[ worldPoints.Count - 1 ] - worldPoints[ worldPoints.Count - 2 ];
		if ( endTan.sqrMagnitude < 0.0001f )
			endTan = Vector3.forward;
		else
			endTan.Normalize();

		worldTangent = endTan;
		return true;
	}

	public static float MeasureLength( List<Vector3> points )
	{
		if ( points == null || points.Count < 2 )
			return 0f;

		float sum = 0f;
		for ( int i = 0; i < points.Count - 1; i++ )
			sum += Vector3.Distance( points[ i ], points[ i + 1 ] );

		return sum;
	}
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

[Serializable]
public class MinecartJunctionDebugEntry
{
	public Vector3 worldPos;
	public Vector3 inwardTangent;
	public Color color;
}

[Serializable]
public class MinecartJunctionDebugCorner
{
	public Vector3 start;
	public Vector3 control;
	public Vector3 end;
}

[Serializable]
public class MinecartJunctionDebugPolyline
{
	public List<Vector3> points = new List<Vector3>( 8 );
}
