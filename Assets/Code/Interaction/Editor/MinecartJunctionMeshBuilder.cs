#if UNITY_EDITOR
using System.Collections.Generic;

using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds junction visuals: through-rails, quadratic Bezier corner turns, and a
/// shaped sleeper strip following all rail centerlines.
/// </summary>
public static class MinecartJunctionMeshBuilder
{
	public const string JunctionChildPrefix = "Junction_";
	public const string VisualChildName = "JunctionVisual";
	public const string ColliderChildName = "JunctionCollider";

	const string GeneratedFolder = "Assets/Generated/MinecartJunctions";
	const int ThroughSamples = 8;
	const float EntryMergeEpsilon = 0.08f;

	static readonly List<Vector3> Verts = new List<Vector3>( 512 );
	static readonly List<Vector3> Normals = new List<Vector3>( 512 );
	static readonly List<Vector2> Uvs = new List<Vector2>( 512 );
	static readonly List<int> RailTris = new List<int>( 1024 );
	static readonly List<int> SleeperTris = new List<int>( 512 );
	static readonly List<Vector3> ColliderVerts = new List<Vector3>( 64 );
	static readonly List<int> ColliderTris = new List<int>( 128 );
	static readonly List<JunctionEntry> Entries = new List<JunctionEntry>( 8 );
	static readonly List<JunctionEntry> SortedEntries = new List<JunctionEntry>( 8 );
	static readonly List<List<Vector3>> SleeperPaths = new List<List<Vector3>>( 8 );
	static readonly List<Vector3> WorldPolylineBuffer = new List<Vector3>( 16 );

	static MinecartJunctionGraph ActiveGraph;
	static MinecartJunction ActiveJunction;
	static Vector3 ActiveJunctionWorld;

	struct JunctionEntry
	{
		public MinecartTrack track;
		public float distance;
		public int portIndex;
		public Vector3 localPos;
		public Vector3 inwardTangent;
		public Vector3 up;
		public float angle;
	}

	public static void Rebuild( MinecartJunctionGraph graph )
	{
		if ( graph == null )
			return;

		EnsureFolder( "Assets/Generated" );
		EnsureFolder( GeneratedFolder );

		ActiveGraph = graph;
		graph.EditorClearDebugBake();

		IReadOnlyList<MinecartJunction> junctions = graph.Junctions;
		int count = junctions != null ? junctions.Count : 0;
		HashSet<string> keep = new HashSet<string>();

		for ( int i = 0; i < count; i++ )
		{
			MinecartJunction junction = junctions[ i ];
			if ( junction == null || junction.ports == null || junction.ports.Count < 2 )
				continue;

			string childName = JunctionChildPrefix + i;
			keep.Add( childName );
			RebuildJunction( graph, i, junction, childName );
		}

		DestroyOrphanJunctionChildren( graph.transform, keep );
		EditorUtility.SetDirty( graph );
		ActiveGraph = null;
	}

	static void RebuildJunction( MinecartJunctionGraph graph, int index, MinecartJunction junction, string childName )
	{
		Transform root = graph.transform.Find( childName );
		GameObject go = root != null ? root.gameObject : null;
		if ( go == null )
		{
			go = new GameObject( childName );
			Undo.RegisterCreatedObjectUndo( go, "Create Junction Mesh" );
			go.transform.SetParent( graph.transform, false );
		}

		go.transform.position = junction.worldPosition;
		go.transform.rotation = Quaternion.identity;
		go.transform.localScale = Vector3.one;

		junction.resolvedKind = MinecartJunctionGraph.ResolveKind( junction.kind, junction.ports );
		if ( junction.ridePaths == null )
			junction.ridePaths = new List<MinecartJunctionRidePath>( 8 );
		else
			junction.ridePaths.Clear();

		MinecartTrack template = ResolveTemplateTrack( junction );
		Material railMat = template != null && template.RailMaterial != null
			? template.RailMaterial
			: MinecartTrackMeshBuilder.EnsureRailMaterial();
		Material sleeperMat = template != null && template.SleeperMaterial != null
			? template.SleeperMaterial
			: MinecartTrackMeshBuilder.EnsureSleeperMaterial();

		float gauge = template != null ? template.RailGauge : 0.9f;
		float railWidth = template != null ? template.RailWidth : 0.06f;
		float railHeight = template != null ? template.RailHeight : 0.08f;
		Vector3 sleeperSize = template != null ? template.SleeperSize : new Vector3( 1.2f, 0.08f, 0.18f );
		float gap = graph.JunctionMeshGap;
		float halfSleeper = graph.SleeperHalfWidthOverride > 0.01f
			? graph.SleeperHalfWidthOverride
			: sleeperSize.x * 0.5f;

		Mesh visualMesh = BuildVisualMesh( graph, junction, gap, gauge, railWidth, railHeight, sleeperSize, halfSleeper );
		Mesh visualAsset = SaveMeshAsset( graph, index, "_Visual", visualMesh, Verts, Normals, Uvs, RailTris, SleeperTris, twoSubmeshes: true );
		ApplyVisual( go, visualAsset, railMat, sleeperMat );

		Mesh colliderMesh = BuildColliderFromSleeper( sleeperSize.y, halfSleeper );
		Mesh colliderAsset = SaveColliderMeshAsset( graph, index, colliderMesh );
		ApplyCollider( go, colliderAsset );

		EditorUtility.SetDirty( go );
	}

	static MinecartTrack ResolveTemplateTrack( MinecartJunction junction )
	{
		if ( junction == null || junction.ports == null )
			return null;

		for ( int i = 0; i < junction.ports.Count; i++ )
		{
			MinecartTrack track = junction.ports[ i ].track;
			if ( track != null && track.IsUsable )
				return track;
		}

		return null;
	}

	static Mesh BuildVisualMesh( MinecartJunctionGraph graph, MinecartJunction junction, float gap, float gauge, float railWidth, float railHeight, Vector3 sleeperSize, float sleeperHalfWidth )
	{
		Verts.Clear();
		Normals.Clear();
		Uvs.Clear();
		RailTris.Clear();
		SleeperTris.Clear();
		Entries.Clear();
		SleeperPaths.Clear();
		ActiveJunctionWorld = junction.worldPosition;
		ActiveJunction = junction;

		CollectEntries( graph, junction, gap );
		if ( Entries.Count < 2 )
		{
			ActiveJunction = null;
			return EmptyVisualMesh();
		}

		AppendThroughRails( graph, junction, gap, gauge, railWidth, railHeight, sleeperSize );
		if ( graph.DrawTurnCurves )
			AppendTurnCurves( graph, gauge, railWidth, railHeight, sleeperSize );
		if ( graph.DrawShapedSleeper )
			AppendShapedSleeper( sleeperSize.y, sleeperHalfWidth );

		ActiveJunction = null;

		Mesh mesh = new Mesh();
		mesh.name = "Junction_Visual";
		mesh.SetVertices( Verts );
		mesh.SetNormals( Normals );
		mesh.SetUVs( 0, Uvs );
		mesh.subMeshCount = 2;
		mesh.SetTriangles( RailTris, 0 );
		mesh.SetTriangles( SleeperTris, 1 );
		mesh.RecalculateBounds();
		mesh.RecalculateNormals();
		return mesh;
	}

	static Mesh EmptyVisualMesh()
	{
		Mesh mesh = new Mesh();
		mesh.name = "Junction_Visual_Empty";
		mesh.subMeshCount = 2;
		mesh.SetTriangles( new int[ 0 ], 0 );
		mesh.SetTriangles( new int[ 0 ], 1 );
		return mesh;
	}

	static void CollectEntries( MinecartJunctionGraph graph, MinecartJunction junction, float gap )
	{
		Vector3 center = junction.worldPosition;
		float entryOffset = gap + graph.JoinOverlap;
		Color[] palette =
		{
			new Color( 1f, 0.85f, 0.2f ),
			new Color( 0.3f, 0.85f, 1f ),
			new Color( 1f, 0.4f, 0.7f ),
			new Color( 0.5f, 1f, 0.45f )
		};

		for ( int p = 0; p < junction.ports.Count; p++ )
		{
			MinecartJunctionPort port = junction.ports[ p ];
			MinecartTrack track = port.track;
			if ( track == null || !track.IsUsable )
				continue;

			bool canNeg = MinecartJunctionGraph.IsValidExitDirection( port, -1 );
			bool canPos = MinecartJunctionGraph.IsValidExitDirection( port, 1 );
			if ( canNeg )
				TryAddEntry( track, port.distance - entryOffset, p, center, palette[ p % palette.Length ] );
			if ( canPos )
				TryAddEntry( track, port.distance + entryOffset, p, center, palette[ p % palette.Length ] );
		}
	}

	static void TryAddEntry( MinecartTrack track, float distance, int portIndex, Vector3 junctionWorld, Color color )
	{
		float wrapped = track.WrapDistance( distance );
		if ( !track.IsClosed )
		{
			if ( wrapped <= 0.001f && distance < 0f )
				wrapped = 0f;
			if ( wrapped >= track.Length - 0.001f && distance > track.Length )
				wrapped = track.Length;
		}

		Vector3 pos;
		Vector3 tan;
		Vector3 up;
		if ( !track.Evaluate( wrapped, out pos, out tan, out up ) )
			return;

		if ( tan.sqrMagnitude < 0.0001f )
			return;

		tan.Normalize();
		Vector3 toCenter = junctionWorld - pos;
		toCenter.y = 0f;
		Vector3 flatTan = tan;
		flatTan.y = 0f;
		if ( flatTan.sqrMagnitude < 0.0001f )
			flatTan = tan;

		flatTan.Normalize();
		Vector3 inward = flatTan;
		if ( Vector3.Dot( flatTan, toCenter.sqrMagnitude > 0.0001f ? toCenter.normalized : flatTan ) < 0f )
			inward = -flatTan;

		if ( up.sqrMagnitude < 0.0001f )
			up = Vector3.up;
		else
			up.Normalize();

		for ( int i = 0; i < Entries.Count; i++ )
		{
			JunctionEntry existing = Entries[ i ];
			if ( existing.track != track )
				continue;

			if ( Mathf.Abs( track.SignedAlong( existing.distance, wrapped ) ) <= EntryMergeEpsilon )
				return;
		}

		Vector3 local = pos - junctionWorld;
		float angle = Mathf.Atan2( local.x, local.z );
		Entries.Add( new JunctionEntry
		{
			track = track,
			distance = wrapped,
			portIndex = portIndex,
			localPos = local,
			inwardTangent = inward,
			up = up,
			angle = angle
		} );

		if ( ActiveGraph != null )
			ActiveGraph.EditorAddDebugEntry( pos, inward, color );
	}

	static void AppendThroughRails( MinecartJunctionGraph graph, MinecartJunction junction, float gap, float gauge, float railWidth, float railHeight, Vector3 sleeperSize )
	{
		HashSet<MinecartTrack> done = new HashSet<MinecartTrack>();
		for ( int p = 0; p < junction.ports.Count; p++ )
		{
			MinecartJunctionPort port = junction.ports[ p ];
			MinecartTrack track = port.track;
			if ( track == null || !track.IsUsable || done.Contains( track ) )
				continue;

			done.Add( track );
			bool canNeg = MinecartJunctionGraph.IsValidExitDirection( port, -1 );
			bool canPos = MinecartJunctionGraph.IsValidExitDirection( port, 1 );
			// Tee stubs only have one valid direction — no through-rail into nothing.
			if ( !canNeg || !canPos )
				continue;

			float span = gap + graph.JoinOverlap;
			float from = port.distance - span;
			float to = port.distance + span;
			if ( !track.IsClosed )
			{
				from = Mathf.Max( 0f, from );
				to = Mathf.Min( track.Length, to );
			}

			if ( to - from < 0.05f && !track.IsClosed )
				continue;

			AppendTrackSegmentRails( track, from, to, junction.worldPosition, gauge, railWidth, railHeight, sleeperSize );
		}
	}

	static void AppendTrackSegmentRails( MinecartTrack track, float fromDist, float toDist, Vector3 junctionWorld, float gauge, float railWidth, float railHeight, Vector3 sleeperSize )
	{
		float length = toDist - fromDist;
		if ( track.IsClosed && length < 0f )
			length += track.Length;

		int samples = Mathf.Max( 2, ThroughSamples );
		float halfW = railWidth * 0.5f;
		float halfH = railHeight * 0.5f;
		float halfGauge = gauge * 0.5f;
		float sleeperTop = sleeperSize.y;

		Vector3[] centers = new Vector3[ samples ];
		Vector3[] rights = new Vector3[ samples ];
		Vector3[] ups = new Vector3[ samples ];
		List<Vector3> sleeperPath = new List<Vector3>( samples );
		WorldPolylineBuffer.Clear();

		for ( int i = 0; i < samples; i++ )
		{
			float t = samples == 1 ? 0f : ( float )i / ( samples - 1 );
			float dist = fromDist + length * t;
			if ( track.IsClosed )
				dist = track.WrapDistance( dist );

			Vector3 pos;
			Vector3 tan;
			Vector3 up;
			if ( !track.Evaluate( dist, out pos, out tan, out up ) )
				return;

			if ( tan.sqrMagnitude < 0.0001f )
				tan = Vector3.forward;
			else
				tan.Normalize();

			if ( up.sqrMagnitude < 0.0001f )
				up = Vector3.up;
			else
				up.Normalize();

			Vector3 right = Vector3.Cross( up, tan );
			if ( right.sqrMagnitude < 0.0001f )
				right = Vector3.right;
			else
				right.Normalize();

			centers[ i ] = pos - junctionWorld;
			rights[ i ] = right;
			ups[ i ] = up;
			sleeperPath.Add( centers[ i ] );
			WorldPolylineBuffer.Add( pos );
		}

		SleeperPaths.Add( sleeperPath );
		if ( ActiveGraph != null )
			ActiveGraph.EditorAddDebugPolyline( false, WorldPolylineBuffer );

		int leftStart = Verts.Count;
		for ( int i = 0; i < samples; i++ )
		{
			Vector3 center = centers[ i ] + rights[ i ] * -halfGauge + ups[ i ] * ( sleeperTop + halfH );
			AddRailRing( center, rights[ i ], ups[ i ], halfW, halfH );
		}

		int rightStart = Verts.Count;
		for ( int i = 0; i < samples; i++ )
		{
			Vector3 center = centers[ i ] + rights[ i ] * halfGauge + ups[ i ] * ( sleeperTop + halfH );
			AddRailRing( center, rights[ i ], ups[ i ], halfW, halfH );
		}

		for ( int i = 0; i < samples - 1; i++ )
		{
			ConnectRailRings( leftStart + i * 4, leftStart + ( i + 1 ) * 4 );
			ConnectRailRings( rightStart + i * 4, rightStart + ( i + 1 ) * 4 );
		}
	}

	static void AppendTurnCurves( MinecartJunctionGraph graph, float gauge, float railWidth, float railHeight, Vector3 sleeperSize )
	{
		SortedEntries.Clear();
		SortedEntries.AddRange( Entries );
		SortedEntries.Sort( ( a, b ) => a.angle.CompareTo( b.angle ) );

		int count = SortedEntries.Count;
		if ( count < 2 )
			return;

		for ( int i = 0; i < count; i++ )
		{
			JunctionEntry a = SortedEntries[ i ];
			JunctionEntry b = SortedEntries[ ( i + 1 ) % count ];
			if ( a.track == b.track )
				continue;

			AppendBezierDualRail( graph, a, b, gauge, railWidth, railHeight, sleeperSize );
		}
	}

	static void AppendBezierDualRail( MinecartJunctionGraph graph, JunctionEntry a, JunctionEntry b, float gauge, float railWidth, float railHeight, Vector3 sleeperSize )
	{
		Vector3 start = a.localPos;
		Vector3 end = b.localPos;
		float chord = Vector3.Distance( start, end );
		if ( chord < 0.05f )
			return;

		Vector3 flatA = new Vector3( start.x, 0f, start.z );
		Vector3 flatB = new Vector3( end.x, 0f, end.z );
		float avgRadius = ( flatA.magnitude + flatB.magnitude ) * 0.5f;
		Vector3 bisector = flatA.normalized + flatB.normalized;
		if ( bisector.sqrMagnitude < 0.0001f )
			return;

		Vector3 control = bisector.normalized * ( avgRadius * graph.CornerControlScale );
		control.y = ( start.y + end.y ) * 0.5f;

		if ( ActiveGraph != null )
			ActiveGraph.EditorAddDebugCorner( ActiveJunctionWorld + start, ActiveJunctionWorld + control, ActiveJunctionWorld + end );

		int samples = graph.CurveSamples;
		float halfW = railWidth * 0.5f;
		float halfH = railHeight * 0.5f;
		float halfGauge = gauge * 0.5f;
		float sleeperTop = sleeperSize.y;

		Vector3[] centers = new Vector3[ samples ];
		Vector3[] rights = new Vector3[ samples ];
		Vector3[] ups = new Vector3[ samples ];
		List<Vector3> sleeperPath = new List<Vector3>( samples );
		WorldPolylineBuffer.Clear();
		Vector3 prevRight = Vector3.zero;

		for ( int i = 0; i < samples; i++ )
		{
			float t = samples == 1 ? 0f : ( float )i / ( samples - 1 );
			Vector3 pos = EvalQuadratic( start, control, end, t );
			Vector3 tan = EvalQuadraticDerivative( start, control, end, t );
			if ( tan.sqrMagnitude < 0.0001f )
				tan = end - start;

			tan.Normalize();
			Vector3 up = Vector3.up;
			Vector3 right = Vector3.Cross( up, tan );
			if ( right.sqrMagnitude < 0.0001f )
				right = Vector3.right;
			else
				right.Normalize();

			if ( i > 0 && Vector3.Dot( right, prevRight ) < 0f )
				right = -right;

			centers[ i ] = pos;
			rights[ i ] = right;
			ups[ i ] = up;
			prevRight = right;
			sleeperPath.Add( pos );
			WorldPolylineBuffer.Add( ActiveJunctionWorld + pos );
		}

		SleeperPaths.Add( sleeperPath );
		if ( ActiveGraph != null )
			ActiveGraph.EditorAddDebugPolyline( true, WorldPolylineBuffer );

		BakeRidePath( a, b, WorldPolylineBuffer );
		BakeRidePath( b, a, ReverseWorldPolyline( WorldPolylineBuffer ) );

		int leftStart = Verts.Count;
		for ( int i = 0; i < samples; i++ )
		{
			Vector3 center = centers[ i ] + rights[ i ] * -halfGauge + ups[ i ] * ( sleeperTop + halfH );
			AddRailRing( center, rights[ i ], ups[ i ], halfW, halfH );
		}

		int rightStart = Verts.Count;
		for ( int i = 0; i < samples; i++ )
		{
			Vector3 center = centers[ i ] + rights[ i ] * halfGauge + ups[ i ] * ( sleeperTop + halfH );
			AddRailRing( center, rights[ i ], ups[ i ], halfW, halfH );
		}

		for ( int i = 0; i < samples - 1; i++ )
		{
			ConnectRailRings( leftStart + i * 4, leftStart + ( i + 1 ) * 4 );
			ConnectRailRings( rightStart + i * 4, rightStart + ( i + 1 ) * 4 );
		}
	}

	static List<Vector3> ReverseWorldPolyline( List<Vector3> source )
	{
		List<Vector3> reversed = new List<Vector3>( source.Count );
		for ( int i = source.Count - 1; i >= 0; i-- )
			reversed.Add( source[ i ] );

		return reversed;
	}

	static void BakeRidePath( JunctionEntry from, JunctionEntry to, List<Vector3> worldPoints )
	{
		if ( ActiveJunction == null || worldPoints == null || worldPoints.Count < 2 )
			return;

		int intoSign = IntoTravelSign( from );
		int outSign = -IntoTravelSign( to );
		MinecartJunctionRidePath path = new MinecartJunctionRidePath
		{
			fromTrack = from.track,
			fromDistance = from.distance,
			intoTravelSign = intoSign,
			toTrack = to.track,
			toDistance = to.distance,
			outTravelSign = outSign,
			worldPoints = new List<Vector3>( worldPoints ),
			length = MinecartJunctionRidePath.MeasureLength( worldPoints )
		};
		ActiveJunction.ridePaths.Add( path );
	}

	static int IntoTravelSign( JunctionEntry entry )
	{
		Vector3 pos;
		Vector3 tan;
		Vector3 up;
		if ( entry.track == null || !entry.track.Evaluate( entry.distance, out pos, out tan, out up ) )
			return 1;

		tan.y = 0f;
		if ( tan.sqrMagnitude < 0.0001f )
			return 1;

		tan.Normalize();
		Vector3 inward = entry.inwardTangent;
		inward.y = 0f;
		if ( inward.sqrMagnitude < 0.0001f )
			return 1;

		return Vector3.Dot( tan, inward.normalized ) >= 0f ? 1 : -1;
	}

	static Vector3 EvalQuadratic( Vector3 a, Vector3 b, Vector3 c, float t )
	{
		float u = 1f - t;
		return u * u * a + 2f * u * t * b + t * t * c;
	}

	static Vector3 EvalQuadraticDerivative( Vector3 a, Vector3 b, Vector3 c, float t )
	{
		return 2f * ( 1f - t ) * ( b - a ) + 2f * t * ( c - b );
	}

	static void AppendShapedSleeper( float sleeperHeight, float halfWidth )
	{
		float halfH = Mathf.Max( 0.02f, sleeperHeight * 0.5f );
		for ( int p = 0; p < SleeperPaths.Count; p++ )
		{
			List<Vector3> path = SleeperPaths[ p ];
			if ( path == null || path.Count < 2 )
				continue;

			AppendSleeperRibbon( path, halfWidth, halfH );
		}
	}

	/// <summary>
	/// Continuous sleeper ribbon along a centerline (one connected strip, not discrete boxes).
	/// </summary>
	static void AppendSleeperRibbon( List<Vector3> path, float halfWidth, float halfH )
	{
		int count = path.Count;
		Vector3[] rights = new Vector3[ count ];
		Vector3 prevRight = Vector3.zero;
		for ( int i = 0; i < count; i++ )
		{
			Vector3 fwd;
			if ( i < count - 1 )
				fwd = path[ i + 1 ] - path[ i ];
			else
				fwd = path[ i ] - path[ i - 1 ];

			fwd.y = 0f;
			if ( fwd.sqrMagnitude < 0.000001f )
				fwd = i > 0 ? rights[ i - 1 ] : Vector3.forward;
			else
				fwd.Normalize();

			Vector3 right = Vector3.Cross( Vector3.up, fwd );
			if ( right.sqrMagnitude < 0.0001f )
				right = Vector3.right;
			else
				right.Normalize();

			if ( i > 0 && Vector3.Dot( right, prevRight ) < 0f )
				right = -right;

			rights[ i ] = right;
			prevRight = right;
		}

		int baseIndex = Verts.Count;
		for ( int i = 0; i < count; i++ )
		{
			Vector3 center = path[ i ];
			Vector3 right = rights[ i ];
			Vector3 up = Vector3.up;
			Vector3 left = center - right * halfWidth;
			Vector3 rightPos = center + right * halfWidth;

			// Bottom left, bottom right, top left, top right
			AddVert( left, -up, new Vector2( 0f, i ) );
			AddVert( rightPos, -up, new Vector2( 1f, i ) );
			AddVert( left + up * ( halfH * 2f ), up, new Vector2( 0f, i ) );
			AddVert( rightPos + up * ( halfH * 2f ), up, new Vector2( 1f, i ) );
		}

		for ( int i = 0; i < count - 1; i++ )
		{
			int a = baseIndex + i * 4;
			int b = baseIndex + ( i + 1 ) * 4;
			// Top
			SleeperTris.Add( a + 2 );
			SleeperTris.Add( b + 2 );
			SleeperTris.Add( a + 3 );
			SleeperTris.Add( a + 3 );
			SleeperTris.Add( b + 2 );
			SleeperTris.Add( b + 3 );
			// Bottom
			SleeperTris.Add( a + 0 );
			SleeperTris.Add( a + 1 );
			SleeperTris.Add( b + 0 );
			SleeperTris.Add( a + 1 );
			SleeperTris.Add( b + 1 );
			SleeperTris.Add( b + 0 );
			// Left side
			SleeperTris.Add( a + 0 );
			SleeperTris.Add( b + 0 );
			SleeperTris.Add( a + 2 );
			SleeperTris.Add( a + 2 );
			SleeperTris.Add( b + 0 );
			SleeperTris.Add( b + 2 );
			// Right side
			SleeperTris.Add( a + 1 );
			SleeperTris.Add( a + 3 );
			SleeperTris.Add( b + 1 );
			SleeperTris.Add( a + 3 );
			SleeperTris.Add( b + 3 );
			SleeperTris.Add( b + 1 );
		}
	}

	static Mesh BuildColliderFromSleeper( float sleeperHeight, float halfWidth )
	{
		ColliderVerts.Clear();
		ColliderTris.Clear();

		// Reuse sleeper path footprint as a simple expanded box if no paths.
		float extent = halfWidth;
		for ( int p = 0; p < SleeperPaths.Count; p++ )
		{
			List<Vector3> path = SleeperPaths[ p ];
			if ( path == null )
				continue;

			for ( int i = 0; i < path.Count; i++ )
			{
				Vector3 pt = path[ i ];
				extent = Mathf.Max( extent, Mathf.Abs( pt.x ) + halfWidth, Mathf.Abs( pt.z ) + halfWidth );
			}
		}

		float halfH = Mathf.Max( 0.04f, sleeperHeight * 0.5f );
		Vector3 center = Vector3.up * halfH;
		Vector3[] corners =
		{
			center + new Vector3( -extent, -halfH, -extent ),
			center + new Vector3( extent, -halfH, -extent ),
			center + new Vector3( extent, halfH, -extent ),
			center + new Vector3( -extent, halfH, -extent ),
			center + new Vector3( -extent, -halfH, extent ),
			center + new Vector3( extent, -halfH, extent ),
			center + new Vector3( extent, halfH, extent ),
			center + new Vector3( -extent, halfH, extent )
		};

		for ( int i = 0; i < corners.Length; i++ )
			ColliderVerts.Add( corners[ i ] );

		AddColliderQuad( 0, 3, 2, 1 );
		AddColliderQuad( 4, 5, 6, 7 );
		AddColliderQuad( 0, 1, 5, 4 );
		AddColliderQuad( 1, 2, 6, 5 );
		AddColliderQuad( 2, 3, 7, 6 );
		AddColliderQuad( 3, 0, 4, 7 );

		Mesh mesh = new Mesh();
		mesh.name = "Junction_Collider";
		mesh.SetVertices( ColliderVerts );
		mesh.SetTriangles( ColliderTris, 0 );
		mesh.RecalculateBounds();
		mesh.RecalculateNormals();
		return mesh;
	}

	static void ApplyVisual( GameObject junctionRoot, Mesh mesh, Material railMat, Material sleeperMat )
	{
		Transform visual = junctionRoot.transform.Find( VisualChildName );
		GameObject go = visual != null ? visual.gameObject : null;
		if ( go == null )
		{
			go = new GameObject( VisualChildName );
			Undo.RegisterCreatedObjectUndo( go, "Create Junction Visual" );
			go.transform.SetParent( junctionRoot.transform, false );
		}

		MeshFilter filter = go.GetComponent<MeshFilter>();
		if ( filter == null )
			filter = go.AddComponent<MeshFilter>();

		MeshRenderer renderer = go.GetComponent<MeshRenderer>();
		if ( renderer == null )
			renderer = go.AddComponent<MeshRenderer>();

		MeshCollider collider = go.GetComponent<MeshCollider>();
		if ( collider != null )
			Undo.DestroyObjectImmediate( collider );

		filter.sharedMesh = mesh;
		renderer.sharedMaterials = new Material[] { railMat, sleeperMat };
		EditorUtility.SetDirty( go );
	}

	static void ApplyCollider( GameObject junctionRoot, Mesh mesh )
	{
		Transform child = junctionRoot.transform.Find( ColliderChildName );
		GameObject go = child != null ? child.gameObject : null;
		if ( go == null )
		{
			go = new GameObject( ColliderChildName );
			Undo.RegisterCreatedObjectUndo( go, "Create Junction Collider" );
			go.transform.SetParent( junctionRoot.transform, false );
		}

		MeshCollider collider = go.GetComponent<MeshCollider>();
		if ( collider == null )
			collider = go.AddComponent<MeshCollider>();

		collider.sharedMesh = mesh;
		collider.convex = false;
		EditorUtility.SetDirty( go );
	}

	static Mesh SaveMeshAsset( MinecartJunctionGraph graph, int index, string suffix, Mesh meshData, List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> railTris, List<int> sleeperTris, bool twoSubmeshes )
	{
		string path = GeneratedFolder + "/" + graph.gameObject.name + "_J" + index + suffix + ".asset";
		Mesh asset = AssetDatabase.LoadAssetAtPath<Mesh>( path );
		if ( asset == null )
		{
			AssetDatabase.CreateAsset( meshData, path );
			return meshData;
		}

		asset.Clear();
		asset.SetVertices( verts );
		asset.SetNormals( normals );
		asset.SetUVs( 0, uvs );
		if ( twoSubmeshes )
		{
			asset.subMeshCount = 2;
			asset.SetTriangles( railTris, 0 );
			asset.SetTriangles( sleeperTris, 1 );
		}
		else
		{
			asset.subMeshCount = 1;
			asset.SetTriangles( railTris, 0 );
		}

		asset.RecalculateBounds();
		asset.RecalculateNormals();
		EditorUtility.SetDirty( asset );
		Object.DestroyImmediate( meshData );
		return asset;
	}

	static Mesh SaveColliderMeshAsset( MinecartJunctionGraph graph, int index, Mesh meshData )
	{
		string path = GeneratedFolder + "/" + graph.gameObject.name + "_J" + index + "_Collider.asset";
		Mesh asset = AssetDatabase.LoadAssetAtPath<Mesh>( path );
		if ( asset == null )
		{
			AssetDatabase.CreateAsset( meshData, path );
			return meshData;
		}

		asset.Clear();
		asset.SetVertices( ColliderVerts );
		asset.subMeshCount = 1;
		asset.SetTriangles( ColliderTris, 0 );
		asset.RecalculateBounds();
		asset.RecalculateNormals();
		EditorUtility.SetDirty( asset );
		Object.DestroyImmediate( meshData );
		return asset;
	}

	static void DestroyOrphanJunctionChildren( Transform root, HashSet<string> keep )
	{
		if ( root == null )
			return;

		for ( int i = root.childCount - 1; i >= 0; i-- )
		{
			Transform child = root.GetChild( i );
			if ( child == null || !child.name.StartsWith( JunctionChildPrefix ) )
				continue;

			if ( keep.Contains( child.name ) )
				continue;

			Undo.DestroyObjectImmediate( child.gameObject );
		}
	}

	static void AddRailRing( Vector3 center, Vector3 right, Vector3 up, float halfW, float halfH )
	{
		Vector3 r = right * halfW;
		Vector3 u = up * halfH;
		AddVert( center - r - u, -up, new Vector2( 0f, 0f ) );
		AddVert( center + r - u, -up, new Vector2( 1f, 0f ) );
		AddVert( center + r + u, up, new Vector2( 1f, 1f ) );
		AddVert( center - r + u, up, new Vector2( 0f, 1f ) );
	}

	static void ConnectRailRings( int a, int b )
	{
		AddQuad( a + 0, a + 1, b + 1, b + 0, RailTris );
		AddQuad( a + 1, a + 2, b + 2, b + 1, RailTris );
		AddQuad( a + 2, a + 3, b + 3, b + 2, RailTris );
		AddQuad( a + 3, a + 0, b + 0, b + 3, RailTris );
	}

	static void AddBox( Vector3 center, Vector3 right, Vector3 up, Vector3 fwd, Vector3 size, List<int> tris )
	{
		Vector3 r = right * ( size.x * 0.5f );
		Vector3 u = up * ( size.y * 0.5f );
		Vector3 f = fwd * ( size.z * 0.5f );
		int i = Verts.Count;
		AddVert( center - r - u - f, ( -right - up - fwd ).normalized, new Vector2( 0f, 0f ) );
		AddVert( center + r - u - f, ( right - up - fwd ).normalized, new Vector2( 1f, 0f ) );
		AddVert( center + r + u - f, ( right + up - fwd ).normalized, new Vector2( 1f, 1f ) );
		AddVert( center - r + u - f, ( -right + up - fwd ).normalized, new Vector2( 0f, 1f ) );
		AddVert( center - r - u + f, ( -right - up + fwd ).normalized, new Vector2( 0f, 0f ) );
		AddVert( center + r - u + f, ( right - up + fwd ).normalized, new Vector2( 1f, 0f ) );
		AddVert( center + r + u + f, ( right + up + fwd ).normalized, new Vector2( 1f, 1f ) );
		AddVert( center - r + u + f, ( -right + up + fwd ).normalized, new Vector2( 0f, 1f ) );

		AddQuad( i + 0, i + 3, i + 2, i + 1, tris );
		AddQuad( i + 4, i + 5, i + 6, i + 7, tris );
		AddQuad( i + 4, i + 7, i + 3, i + 0, tris );
		AddQuad( i + 1, i + 2, i + 6, i + 5, tris );
		AddQuad( i + 3, i + 7, i + 6, i + 2, tris );
		AddQuad( i + 4, i + 0, i + 1, i + 5, tris );
	}

	static void AddVert( Vector3 pos, Vector3 normal, Vector2 uv )
	{
		Verts.Add( pos );
		Normals.Add( normal.normalized );
		Uvs.Add( uv );
	}

	static void AddQuad( int a, int b, int c, int d, List<int> tris )
	{
		tris.Add( a );
		tris.Add( b );
		tris.Add( c );
		tris.Add( a );
		tris.Add( c );
		tris.Add( d );
	}

	static void AddColliderQuad( int a, int b, int c, int d )
	{
		ColliderTris.Add( a );
		ColliderTris.Add( b );
		ColliderTris.Add( c );
		ColliderTris.Add( a );
		ColliderTris.Add( c );
		ColliderTris.Add( d );
	}

	static void EnsureFolder( string path )
	{
		AddressableEditorUtil.EnsureFolder( path );
	}
}
#endif
