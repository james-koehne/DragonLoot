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
	public const string JunctionChildPrefix = MinecartJunctionGraph.JunctionChildPrefix;
	public const string VisualChildName = "JunctionVisual";
	public const string ColliderChildName = "JunctionCollider";

	const string GeneratedFolder = "Assets/Generated/MinecartJunctions";
	const int ThroughSamples = 8;
	const float EntryMergeEpsilon = 0.08f;
	const float EntryAngleMergeDegrees = 10f;
	const float EntryWorldMerge = 0.15f;
	const float NearStraightCornerDegrees = 135f;
	const float OppositeInwardDot = -0.35f;
	const float ColinearStubDot = 0.82f;
	const int MaxThroughCross = 2;
	const int MaxThroughTee = 2;

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
		public float meetDistance;
		public int portIndex;
		public Vector3 localPos;
		public Vector3 inwardTangent;
		public Vector3 up;
		public float angle;
		public bool isStubArm;
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
		float joinRadius = graph.JunctionEntryOffset;
		float halfSleeper = graph.SleeperHalfWidthOverride > 0.01f
			? graph.SleeperHalfWidthOverride
			: sleeperSize.x * 0.5f;

		Mesh visualMesh = BuildVisualMesh( graph, junction, joinRadius, gauge, railWidth, railHeight, sleeperSize, halfSleeper );
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

	static Mesh BuildVisualMesh( MinecartJunctionGraph graph, MinecartJunction junction, float joinRadius, float gauge, float railWidth, float railHeight, Vector3 sleeperSize, float sleeperHalfWidth )
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

		CollectEntries( graph, junction, joinRadius );
		DedupeEntries();
		if ( Entries.Count < 2 )
		{
			ActiveJunction = null;
			return EmptyVisualMesh();
		}

		AppendThroughPaths( graph, junction, gauge, railWidth, railHeight, sleeperSize );
		if ( graph.DrawTurnCurves )
			AppendTurnCurves( graph, junction, gauge, railWidth, railHeight, sleeperSize );
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

	static void CollectEntries( MinecartJunctionGraph graph, MinecartJunction junction, float unusedJoinRadius )
	{
		Vector3 center = junction.worldPosition;
		float cutRadius = graph.JunctionJoinRadius;
		float meetSlop = graph.JoinMeetSlop;
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
			bool stub = !( canNeg && canPos );
			// alongSign: direction away from the port along the track for the meet tuck.
			if ( canNeg )
				TryAddEntry( track, port.distance - cutRadius, -1, meetSlop, p, stub, center, palette[ p % palette.Length ] );
			if ( canPos )
				TryAddEntry( track, port.distance + cutRadius, 1, meetSlop, p, stub, center, palette[ p % palette.Length ] );
		}
	}

	static void TryAddEntry( MinecartTrack track, float cutDistance, int alongSign, float meetSlop, int portIndex, bool isStubArm, Vector3 junctionWorld, Color color )
	{
		float meetDistance = cutDistance + alongSign * meetSlop;
		float wrappedCut = track.WrapDistance( cutDistance );
		float wrappedMeet = track.WrapDistance( meetDistance );
		if ( !track.IsClosed )
		{
			if ( wrappedCut <= 0.001f && cutDistance < 0f )
				wrappedCut = 0f;
			if ( wrappedCut >= track.Length - 0.001f && cutDistance > track.Length )
				wrappedCut = track.Length;
			wrappedMeet = Mathf.Clamp( meetDistance, 0f, track.Length );
		}

		Vector3 pos;
		Vector3 tan;
		Vector3 up;
		if ( !track.Evaluate( wrappedMeet, out pos, out tan, out up ) )
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

			if ( Mathf.Abs( track.SignedAlong( existing.distance, wrappedCut ) ) <= EntryMergeEpsilon )
				return;
		}

		Vector3 local = pos - junctionWorld;
		float angle = Mathf.Atan2( local.x, local.z );
		Entries.Add( new JunctionEntry
		{
			track = track,
			distance = wrappedCut,
			meetDistance = wrappedMeet,
			portIndex = portIndex,
			localPos = local,
			inwardTangent = inward,
			up = up,
			angle = angle,
			isStubArm = isStubArm
		} );

		if ( ActiveGraph != null )
			ActiveGraph.EditorAddDebugEntry( pos, inward, color );
	}

	static void DedupeEntries()
	{
		if ( Entries.Count < 2 )
			return;

		Entries.Sort( ( a, b ) => a.angle.CompareTo( b.angle ) );
		float angleMerge = EntryAngleMergeDegrees * Mathf.Deg2Rad;
		List<JunctionEntry> kept = new List<JunctionEntry>( Entries.Count );
		for ( int i = 0; i < Entries.Count; i++ )
		{
			JunctionEntry candidate = Entries[ i ];
			bool merged = false;
			for ( int k = 0; k < kept.Count; k++ )
			{
				JunctionEntry existing = kept[ k ];
				float ang = Mathf.Abs( Mathf.DeltaAngle( existing.angle * Mathf.Rad2Deg, candidate.angle * Mathf.Rad2Deg ) ) * Mathf.Deg2Rad;
				float worldDist = Vector3.Distance( existing.localPos, candidate.localPos );
				if ( ang > angleMerge && worldDist > EntryWorldMerge )
					continue;

				// Keep the farther entry (closer to the join plane / track cut).
				float existingRad = new Vector3( existing.localPos.x, 0f, existing.localPos.z ).magnitude;
				float candidateRad = new Vector3( candidate.localPos.x, 0f, candidate.localPos.z ).magnitude;
				if ( candidateRad > existingRad )
					kept[ k ] = candidate;

				merged = true;
				break;
			}

			if ( !merged )
				kept.Add( candidate );
		}

		Entries.Clear();
		Entries.AddRange( kept );
	}

	static void AppendThroughPaths( MinecartJunctionGraph graph, MinecartJunction junction, float gauge, float railWidth, float railHeight, Vector3 sleeperSize )
	{
		List<ThroughCandidate> candidates = new List<ThroughCandidate>( 4 );
		CollectSameTrackThroughCandidates( graph, junction, candidates );
		CollectOppositeStubThroughCandidates( candidates );

		SyncThroughOptions( junction, candidates );

		MinecartJunctionKind kind = junction.resolvedKind;
		int maxThrough = kind == MinecartJunctionKind.Tee ? MaxThroughTee : MaxThroughCross;
		int meshed = 0;
		for ( int i = 0; i < candidates.Count; i++ )
		{
			ThroughCandidate c = candidates[ i ];
			MinecartJunctionCornerOption opt = FindThroughOption( junction, c );
			bool enabled = opt == null || opt.enabled;
			if ( !enabled || !c.meshable )
				continue;

			if ( meshed >= maxThrough )
			{
				if ( ActiveGraph != null )
					ActiveGraph.EditorAddDebugSkippedCorner( ActiveJunctionWorld + c.start, ActiveJunctionWorld + ( c.start + c.end ) * 0.5f, ActiveJunctionWorld + c.end );
				continue;
			}

			if ( c.sameTrack )
				AppendTrackSegmentRails( c.track, c.fromDist, c.toDist, junction.worldPosition, gauge, railWidth, railHeight, sleeperSize );
			else
				AppendCurvedThroughDualRail( graph, c.entryA, c.entryB, c.start, c.end, gauge, railWidth, railHeight, sleeperSize );

			meshed++;
		}
	}

	/// <summary>
	/// Cross-track through / end-to-end join: always a tangent Bezier fillet (never a kinked chord).
	/// </summary>
	static void AppendCurvedThroughDualRail( MinecartJunctionGraph graph, JunctionEntry a, JunctionEntry b, Vector3 start, Vector3 end, float gauge, float railWidth, float railHeight, Vector3 sleeperSize )
	{
		float minRadius = gauge * 0.5f + railWidth;
		Vector3 control = ComputeCornerControl( a, b, start, end, graph.CornerControlScale, minRadius );
		AppendBezierDualRail( graph, a, b, start, control, end, gauge, railWidth, railHeight, sleeperSize );
	}

	static void CollectSameTrackThroughCandidates( MinecartJunctionGraph graph, MinecartJunction junction, List<ThroughCandidate> into )
	{
		float pad = Mathf.Max( graph.JoinMeetSlop, 0.05f );
		HashSet<string> donePorts = new HashSet<string>();

		for ( int i = 0; i < Entries.Count; i++ )
		{
			JunctionEntry a = Entries[ i ];
			if ( a.track == null || !a.track.IsUsable )
				continue;

			string portKey = ThroughPortKey( a.track, a.portIndex );
			if ( donePorts.Contains( portKey ) )
				continue;

			int best = -1;
			float bestSep = 0.15f;
			for ( int j = 0; j < Entries.Count; j++ )
			{
				if ( j == i )
					continue;

				JunctionEntry b = Entries[ j ];
				// Continuous through only pairs opposite arms of the SAME port — not self-junction branch ports.
				if ( b.track != a.track || b.portIndex != a.portIndex )
					continue;

				float sep = Mathf.Abs( a.track.SignedAlong( a.meetDistance, b.meetDistance ) );
				if ( sep <= bestSep )
					continue;

				Vector3 pA = Flatten( a.localPos );
				Vector3 pB = Flatten( b.localPos );
				if ( pA.sqrMagnitude > 0.0001f && pB.sqrMagnitude > 0.0001f
				     && Vector3.Dot( pA.normalized, pB.normalized ) > OppositeInwardDot )
					continue;

				bestSep = sep;
				best = j;
			}

			if ( best < 0 )
				continue;

			donePorts.Add( portKey );
			JunctionEntry other = Entries[ best ];

			float fromAlong = PushMeetOutwardAlongTrack( a.track, a.meetDistance, junction.worldPosition, pad );
			float toAlong = PushMeetOutwardAlongTrack( a.track, other.meetDistance, junction.worldPosition, pad );
			float spanSigned = a.track.SignedAlong( fromAlong, toAlong );
			if ( Mathf.Abs( spanSigned ) < 0.05f )
				continue;

			JunctionEntry entryFrom = a;
			JunctionEntry entryTo = other;
			if ( spanSigned < 0f )
			{
				float tmp = fromAlong;
				fromAlong = toAlong;
				toAlong = tmp;
				entryFrom = other;
				entryTo = a;
			}

			Vector3 fromPos;
			Vector3 toPos;
			Vector3 tan;
			Vector3 up;
			if ( !a.track.Evaluate( a.track.WrapDistance( fromAlong ), out fromPos, out tan, out up ) )
				continue;
			if ( !a.track.Evaluate( a.track.WrapDistance( toAlong ), out toPos, out tan, out up ) )
				continue;

			into.Add( new ThroughCandidate
			{
				sameTrack = true,
				track = a.track,
				entryA = entryFrom,
				entryB = entryTo,
				start = fromPos - junction.worldPosition,
				end = toPos - junction.worldPosition,
				fromDist = fromAlong,
				toDist = toAlong,
				angleA = entryFrom.angle,
				angleB = entryTo.angle,
				meshable = true,
				label = $"Through {a.track.name}"
			} );
		}

		// Fallback: each bidirectional port with no opposite entry pair gets its own cut fill.
		float span = graph.JunctionJoinRadius + graph.JoinMeetSlop + pad;
		for ( int p = 0; p < junction.ports.Count; p++ )
		{
			MinecartJunctionPort port = junction.ports[ p ];
			MinecartTrack track = port.track;
			if ( track == null || !track.IsUsable )
				continue;

			string portKey = ThroughPortKey( track, p );
			if ( donePorts.Contains( portKey ) )
				continue;

			bool canNeg = MinecartJunctionGraph.IsValidExitDirection( port, -1 );
			bool canPos = MinecartJunctionGraph.IsValidExitDirection( port, 1 );
			if ( !canNeg || !canPos )
				continue;

			donePorts.Add( portKey );
			float from = port.distance - span;
			float to = port.distance + span;
			if ( !track.IsClosed )
			{
				from = Mathf.Max( 0f, from );
				to = Mathf.Min( track.Length, to );
			}

			if ( to - from < 0.05f && !track.IsClosed )
				continue;

			Vector3 startPos;
			Vector3 endPos;
			Vector3 tan;
			Vector3 up;
			if ( !track.Evaluate( track.WrapDistance( from ), out startPos, out tan, out up ) )
				continue;
			if ( !track.Evaluate( track.WrapDistance( to ), out endPos, out tan, out up ) )
				continue;

			into.Add( new ThroughCandidate
			{
				sameTrack = true,
				track = track,
				entryA = default( JunctionEntry ),
				entryB = default( JunctionEntry ),
				start = startPos - junction.worldPosition,
				end = endPos - junction.worldPosition,
				fromDist = from,
				toDist = to,
				angleA = Mathf.Atan2( ( startPos - junction.worldPosition ).x, ( startPos - junction.worldPosition ).z ),
				angleB = Mathf.Atan2( ( endPos - junction.worldPosition ).x, ( endPos - junction.worldPosition ).z ),
				meshable = true,
				label = $"Through {track.name}"
			} );
		}
	}

	static string ThroughPortKey( MinecartTrack track, int portIndex )
	{
		int id = track != null ? track.GetInstanceID() : 0;
		return id.ToString() + ":" + portIndex.ToString();
	}

	static float PushMeetOutwardAlongTrack( MinecartTrack track, float meetDistance, Vector3 junctionWorld, float pad )
	{
		Vector3 pos;
		Vector3 tan;
		Vector3 up;
		if ( !track.Evaluate( meetDistance, out pos, out tan, out up ) )
			return meetDistance;

		tan.y = 0f;
		if ( tan.sqrMagnitude < 0.0001f )
			return meetDistance;

		tan.Normalize();
		Vector3 away = pos - junctionWorld;
		away.y = 0f;
		float alongSign = Vector3.Dot( tan, away.sqrMagnitude > 0.0001f ? away.normalized : tan ) >= 0f ? 1f : -1f;
		float pushed = meetDistance + alongSign * pad;
		if ( !track.IsClosed )
			return Mathf.Clamp( pushed, 0f, track.Length );

		return track.WrapDistance( pushed );
	}

	static void CollectOppositeStubThroughCandidates( List<ThroughCandidate> into )
	{
		float pad = ActiveGraph != null ? Mathf.Max( ActiveGraph.JoinMeetSlop, 0.05f ) : 0.05f;
		int count = Entries.Count;
		for ( int i = 0; i < count; i++ )
		{
			for ( int j = i + 1; j < count; j++ )
			{
				JunctionEntry a = Entries[ i ];
				JunctionEntry b = Entries[ j ];
				if ( a.track == null || b.track == null )
					continue;

				if ( a.track == b.track )
					continue;

				if ( !AreOppositeEntries( a, b ) )
					continue;

				float chord = Vector3.Distance( a.localPos, b.localPos );
				if ( chord < 0.15f )
					continue;

				bool duplicate = false;
				for ( int t = 0; t < into.Count; t++ )
				{
					ThroughCandidate existing = into[ t ];
					if ( !existing.sameTrack )
						continue;

					Vector3 midExisting = ( existing.start + existing.end ) * 0.5f;
					Vector3 midNew = ( a.localPos + b.localPos ) * 0.5f;
					if ( Vector3.Distance( midExisting, midNew ) < 0.35f )
					{
						duplicate = true;
						break;
					}
				}

				if ( duplicate )
					continue;

				Vector3 inA = Flatten( a.inwardTangent );
				Vector3 inB = Flatten( b.inwardTangent );
				Vector3 start = a.localPos;
				Vector3 end = b.localPos;
				if ( inA.sqrMagnitude > 0.0001f )
					start -= inA.normalized * pad;
				if ( inB.sqrMagnitude > 0.0001f )
					end -= inB.normalized * pad;

				string nameA = a.track.name;
				string nameB = b.track.name;
				into.Add( new ThroughCandidate
				{
					sameTrack = false,
					track = null,
					entryA = a,
					entryB = b,
					start = start,
					end = end,
					fromDist = 0f,
					toDist = 0f,
					angleA = a.angle,
					angleB = b.angle,
					meshable = true,
					label = $"Through {nameA} ↔ {nameB}"
				} );
			}
		}
	}

	static bool AreOppositeEntries( JunctionEntry a, JunctionEntry b )
	{
		Vector3 inA = Flatten( a.inwardTangent );
		Vector3 inB = Flatten( b.inwardTangent );
		Vector3 pA = Flatten( a.localPos );
		Vector3 pB = Flatten( b.localPos );

		bool tangentsOppose = inA.sqrMagnitude > 0.0001f && inB.sqrMagnitude > 0.0001f
			&& Vector3.Dot( inA.normalized, inB.normalized ) <= OppositeInwardDot;
		bool positionsOppose = pA.sqrMagnitude > 0.0001f && pB.sqrMagnitude > 0.0001f
			&& Vector3.Dot( pA.normalized, pB.normalized ) <= OppositeInwardDot;

		if ( tangentsOppose && positionsOppose )
			return true;

		if ( positionsOppose && pA.sqrMagnitude > 0.0001f && pB.sqrMagnitude > 0.0001f )
			return Vector3.Angle( pA, pB ) >= NearStraightCornerDegrees;

		return false;
	}

	/// <summary>
	/// True when two opposite stubs form a nearly straight splice (inward tangents align with the chord).
	/// Kept for diagnostics; through joins always mesh as Bezier fillets regardless.
	/// </summary>
	static bool AreColinearStubJoin( JunctionEntry a, JunctionEntry b )
	{
		Vector3 inA = Flatten( a.inwardTangent );
		Vector3 inB = Flatten( b.inwardTangent );
		Vector3 chord = Flatten( b.localPos - a.localPos );
		if ( inA.sqrMagnitude < 0.0001f || inB.sqrMagnitude < 0.0001f || chord.sqrMagnitude < 0.0001f )
			return false;

		inA.Normalize();
		inB.Normalize();
		chord.Normalize();

		float alignA = Vector3.Dot( inA, chord );
		float alignB = Vector3.Dot( inB, -chord );
		return alignA >= ColinearStubDot && alignB >= ColinearStubDot;
	}

	static bool IsStraightThroughPair( JunctionEntry a, JunctionEntry b )
	{
		// Any opposite cross-track stub pair is owned by through (curved fillet), not corners.
		return a.track != b.track && AreOppositeEntries( a, b );
	}

	static Vector3 Flatten( Vector3 v )
	{
		v.y = 0f;
		return v;
	}

	struct ThroughCandidate
	{
		public bool sameTrack;
		public MinecartTrack track;
		public JunctionEntry entryA;
		public JunctionEntry entryB;
		public Vector3 start;
		public Vector3 end;
		public float fromDist;
		public float toDist;
		public float angleA;
		public float angleB;
		public bool meshable;
		public string label;
	}

	static void SyncThroughOptions( MinecartJunction junction, List<ThroughCandidate> candidates )
	{
		if ( junction.throughOptions == null )
			junction.throughOptions = new List<MinecartJunctionCornerOption>( candidates.Count );

		List<MinecartJunctionCornerOption> previous = junction.throughOptions;
		List<MinecartJunctionCornerOption> next = new List<MinecartJunctionCornerOption>( candidates.Count );
		for ( int i = 0; i < candidates.Count; i++ )
		{
			ThroughCandidate c = candidates[ i ];
			MinecartTrack trackA = c.sameTrack ? c.track : c.entryA.track;
			MinecartTrack trackB = c.sameTrack ? c.track : c.entryB.track;
			MinecartJunctionCornerOption match = FindPathOption( previous, trackA, trackB, c.angleA, c.angleB );

			next.Add( new MinecartJunctionCornerOption
			{
				label = c.label,
				trackA = trackA,
				trackB = trackB,
				angleA = c.angleA,
				angleB = c.angleB,
				cornerAngleDegrees = 180f,
				enabled = match != null ? match.enabled : c.meshable
			} );
		}

		junction.throughOptions = next;
	}

	static MinecartJunctionCornerOption FindThroughOption( MinecartJunction junction, ThroughCandidate c )
	{
		if ( junction.throughOptions == null )
			return null;

		MinecartTrack trackA = c.sameTrack ? c.track : c.entryA.track;
		MinecartTrack trackB = c.sameTrack ? c.track : c.entryB.track;
		return FindPathOption( junction.throughOptions, trackA, trackB, c.angleA, c.angleB );
	}

	static MinecartJunctionCornerOption FindPathOption( List<MinecartJunctionCornerOption> options, MinecartTrack trackA, MinecartTrack trackB, float angleA, float angleB )
	{
		if ( options == null )
			return null;

		for ( int i = 0; i < options.Count; i++ )
		{
			MinecartJunctionCornerOption o = options[ i ];
			if ( o == null )
				continue;

			if ( !SameTrackPair( o.trackA, o.trackB, trackA, trackB ) )
				continue;

			if ( !AnglesMatch( o.angleA, o.angleB, angleA, angleB ) )
				continue;

			return o;
		}

		return null;
	}

	static void AppendTrackSegmentRails( MinecartTrack track, float fromDist, float toDist, Vector3 junctionWorld, float gauge, float railWidth, float railHeight, Vector3 sleeperSize )
	{
		float length = toDist - fromDist;
		if ( track.IsClosed && length < 0f )
			length += track.Length;

		int samples = Mathf.Max( 2, ThroughSamples );
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

		EmitDualRailMesh( centers, rights, ups, samples, gauge, railWidth, railHeight, sleeperSize.y );
	}

	static void EmitDualRailMesh( Vector3[] centers, Vector3[] rights, Vector3[] ups, int samples, float gauge, float railWidth, float railHeight, float sleeperTop )
	{
		float halfW = railWidth * 0.5f;
		float halfH = railHeight * 0.5f;
		float halfGauge = gauge * 0.5f;

		int leftStart = Verts.Count;
		for ( int i = 0; i < samples; i++ )
		{
			float offset = halfGauge;
			if ( samples >= 3 && i > 0 && i < samples - 1 )
			{
				float radius = EstimatePolylineRadius( centers[ i - 1 ], centers[ i ], centers[ i + 1 ] );
				if ( radius < halfGauge * 1.05f )
					offset = Mathf.Max( railWidth, radius * 0.9f );
			}

			Vector3 center = centers[ i ] + rights[ i ] * -offset + ups[ i ] * ( sleeperTop + halfH );
			AddRailRing( center, rights[ i ], ups[ i ], halfW, halfH );
		}

		int rightStart = Verts.Count;
		for ( int i = 0; i < samples; i++ )
		{
			float offset = halfGauge;
			if ( samples >= 3 && i > 0 && i < samples - 1 )
			{
				float radius = EstimatePolylineRadius( centers[ i - 1 ], centers[ i ], centers[ i + 1 ] );
				if ( radius < halfGauge * 1.05f )
					offset = Mathf.Max( railWidth, radius * 0.9f );
			}

			Vector3 center = centers[ i ] + rights[ i ] * offset + ups[ i ] * ( sleeperTop + halfH );
			AddRailRing( center, rights[ i ], ups[ i ], halfW, halfH );
		}

		for ( int i = 0; i < samples - 1; i++ )
		{
			ConnectRailRings( leftStart + i * 4, leftStart + ( i + 1 ) * 4 );
			ConnectRailRings( rightStart + i * 4, rightStart + ( i + 1 ) * 4 );
		}
	}

	static float EstimatePolylineRadius( Vector3 a, Vector3 b, Vector3 c )
	{
		Vector3 ab = Flatten( b - a );
		Vector3 bc = Flatten( c - b );
		if ( ab.sqrMagnitude < 0.0001f || bc.sqrMagnitude < 0.0001f )
			return 999f;

		float turn = Vector3.Angle( ab, bc ) * Mathf.Deg2Rad;
		if ( turn < 0.001f )
			return 999f;

		float seg = ( ab.magnitude + bc.magnitude ) * 0.5f;
		return seg / turn;
	}

	static void AppendTurnCurves( MinecartJunctionGraph graph, MinecartJunction junction, float gauge, float railWidth, float railHeight, Vector3 sleeperSize )
	{
		SortedEntries.Clear();
		SortedEntries.AddRange( Entries );
		SortedEntries.Sort( ( a, b ) => a.angle.CompareTo( b.angle ) );

		int count = SortedEntries.Count;
		if ( count < 2 )
			return;

		float minChord = Mathf.Max( 0.2f, graph.JunctionEntryOffset * 0.35f );
		float minRadius = gauge * 0.5f + railWidth;
		List<CornerCandidate> candidates = new List<CornerCandidate>( 8 );

		// Adjacent pairs around the ring (turn corners). Same-track allowed only across different ports (self-junction).
		for ( int i = 0; i < count; i++ )
		{
			JunctionEntry a = SortedEntries[ i ];
			JunctionEntry b = SortedEntries[ ( i + 1 ) % count ];
			if ( a.track == b.track && a.portIndex == b.portIndex )
				continue;

			// Colinear opposite stubs are straight through fills; bent end-to-end pairs become corners.
			if ( IsStraightThroughPair( a, b ) )
				continue;

			TryBuildCandidate( graph, a, b, minChord, minRadius, candidates );
		}

		// Also pair every distinct-port same-track entry pair (self-junction arms may not be ring-adjacent).
		for ( int i = 0; i < count; i++ )
		{
			for ( int j = i + 1; j < count; j++ )
			{
				JunctionEntry a = SortedEntries[ i ];
				JunctionEntry b = SortedEntries[ j ];
				if ( a.track != b.track || a.portIndex == b.portIndex )
					continue;

				TryBuildCandidate( graph, a, b, minChord, minRadius, candidates );
			}
		}

		// Tee: also stub ↔ each through arm in case ring adjacency missed one.
		if ( junction.resolvedKind == MinecartJunctionKind.Tee )
		{
			for ( int i = 0; i < count; i++ )
			{
				if ( !SortedEntries[ i ].isStubArm )
					continue;

				for ( int j = 0; j < count; j++ )
				{
					if ( SortedEntries[ j ].isStubArm )
						continue;

					if ( SortedEntries[ j ].track == SortedEntries[ i ].track
					     && SortedEntries[ j ].portIndex == SortedEntries[ i ].portIndex )
						continue;

					if ( IsStraightThroughPair( SortedEntries[ i ], SortedEntries[ j ] ) )
						continue;

					TryBuildCandidate( graph, SortedEntries[ i ], SortedEntries[ j ], minChord, minRadius, candidates );
				}
			}
		}

		// Two-stub end-to-end: ring adjacency covers the pair, but also force any bent stub↔stub pair.
		for ( int i = 0; i < count; i++ )
		{
			if ( !SortedEntries[ i ].isStubArm )
				continue;

			for ( int j = i + 1; j < count; j++ )
			{
				if ( !SortedEntries[ j ].isStubArm )
					continue;

				if ( SortedEntries[ i ].track == SortedEntries[ j ].track )
					continue;

				if ( IsStraightThroughPair( SortedEntries[ i ], SortedEntries[ j ] ) )
					continue;

				TryBuildCandidate( graph, SortedEntries[ i ], SortedEntries[ j ], minChord, minRadius, candidates );
			}
		}

		SyncCornerOptions( junction, candidates );

		for ( int i = 0; i < candidates.Count; i++ )
		{
			CornerCandidate c = candidates[ i ];
			MinecartJunctionCornerOption opt = FindCornerOption( junction, c );
			bool enabled = opt == null || opt.enabled;
			if ( !enabled || !c.meshable )
			{
				if ( ActiveGraph != null )
					ActiveGraph.EditorAddDebugSkippedCorner( ActiveJunctionWorld + c.start, ActiveJunctionWorld + c.control, ActiveJunctionWorld + c.end );
				continue;
			}

			AppendBezierDualRail( graph, c.entryA, c.entryB, c.start, c.control, c.end, gauge, railWidth, railHeight, sleeperSize );
		}
	}

	struct CornerCandidate
	{
		public JunctionEntry entryA;
		public JunctionEntry entryB;
		public Vector3 start;
		public Vector3 end;
		public Vector3 control;
		public float cornerAngleDegrees;
		public float chord;
		public bool meshable;
		public string label;
	}

	static void TryBuildCandidate( MinecartJunctionGraph graph, JunctionEntry a, JunctionEntry b, float minChord, float minRadius, List<CornerCandidate> into )
	{
		// Same port arms are through fills, not corners.
		if ( a.track == b.track && a.portIndex == b.portIndex )
			return;

		for ( int i = 0; i < into.Count; i++ )
		{
			CornerCandidate existing = into[ i ];
			if ( SameTrackPair( existing.entryA.track, existing.entryB.track, a.track, b.track )
			     && AnglesMatch( existing.entryA.angle, existing.entryB.angle, a.angle, b.angle ) )
				return;
		}

		Vector3 start = a.localPos;
		Vector3 end = b.localPos;
		float chord = Vector3.Distance( start, end );
		Vector3 flatA = Flatten( start );
		Vector3 flatB = Flatten( end );
		float cornerAngle = 0f;
		if ( flatA.sqrMagnitude > 0.0001f && flatB.sqrMagnitude > 0.0001f )
			cornerAngle = Vector3.Angle( flatA, flatB );

		Vector3 control = ComputeCornerControl( a, b, start, end, graph.CornerControlScale, minRadius );
		bool hasControl = control.sqrMagnitude > 0.0001f || ( start.sqrMagnitude > 0.0001f && end.sqrMagnitude > 0.0001f );

		bool tooShort = chord < minChord;
		bool tooSharp = cornerAngle > 0.01f && cornerAngle < graph.MinCornerAngleDegrees;
		bool meshable = !tooShort && !tooSharp && hasControl;

		string trackAName = a.track != null ? a.track.name : "?";
		string trackBName = b.track != null ? b.track.name : "?";
		string label = $"{trackAName} ↔ {trackBName} ({cornerAngle:0}°)";
		if ( tooShort )
			label += " [short]";
		else if ( tooSharp )
			label += " [sharp]";

		into.Add( new CornerCandidate
		{
			entryA = a,
			entryB = b,
			start = start,
			end = end,
			control = control,
			cornerAngleDegrees = cornerAngle,
			chord = chord,
			meshable = meshable,
			label = label
		} );
	}

	/// <summary>
	/// Quadratic Bezier control from inward-tangent ray intersection (fillet), not a radial bisector.
	/// Radial bisectors pinch dual rails into a point on tight bends.
	/// </summary>
	static Vector3 ComputeCornerControl( JunctionEntry a, JunctionEntry b, Vector3 start, Vector3 end, float scale, float minRadius )
	{
		Vector3 inA = Flatten( a.inwardTangent );
		Vector3 inB = Flatten( b.inwardTangent );
		if ( inA.sqrMagnitude < 0.0001f )
			inA = -Flatten( start );
		if ( inB.sqrMagnitude < 0.0001f )
			inB = -Flatten( end );

		if ( inA.sqrMagnitude < 0.0001f )
			inA = Vector3.forward;
		if ( inB.sqrMagnitude < 0.0001f )
			inB = Vector3.forward;

		inA.Normalize();
		inB.Normalize();

		float chord = Vector3.Distance( start, end );
		float handle = Mathf.Max( minRadius * 1.5f, chord * Mathf.Clamp( scale, 0.2f, 1.2f ) * 0.55f );
		float y = ( start.y + end.y ) * 0.5f;

		Vector3 hit;
		if ( TryIntersectRaysXZ( start, inA, end, inB, out hit ) )
		{
			Vector3 fromStart = Flatten( hit - start );
			Vector3 fromEnd = Flatten( hit - end );
			bool inside = Vector3.Dot( fromStart, inA ) > 0.01f && Vector3.Dot( fromEnd, inB ) > 0.01f;
			if ( inside )
			{
				float maxDist = Mathf.Max( handle, chord * 1.25f );
				if ( fromStart.magnitude > maxDist )
					hit = start + inA * maxDist;
				hit.y = y;
				return EnsureMinCornerRadius( start, hit, end, minRadius );
			}
		}

		Vector3 fallback = ( start + inA * handle + end + inB * handle ) * 0.5f;
		fallback.y = y;
		return EnsureMinCornerRadius( start, fallback, end, minRadius );
	}

	static Vector3 EnsureMinCornerRadius( Vector3 start, Vector3 control, Vector3 end, float minRadius )
	{
		// Approximate curvature at mid Bezier sample; push control away from chord if too tight.
		Vector3 mid = EvalQuadratic( start, control, end, 0.5f );
		Vector3 chordMid = ( start + end ) * 0.5f;
		Vector3 lateral = Flatten( mid - chordMid );
		float lateralMag = lateral.magnitude;
		if ( lateralMag >= minRadius * 0.85f )
			return control;

		Vector3 pushDir = lateralMag > 0.0001f ? lateral.normalized : Flatten( control - chordMid );
		if ( pushDir.sqrMagnitude < 0.0001f )
			pushDir = Vector3.Cross( Vector3.up, Flatten( end - start ) ).normalized;

		if ( pushDir.sqrMagnitude < 0.0001f )
			return control;

		Vector3 pushed = chordMid + pushDir * ( minRadius * 1.1f );
		pushed.y = control.y;
		return pushed;
	}

	static bool TryIntersectRaysXZ( Vector3 p0, Vector3 d0, Vector3 p1, Vector3 d1, out Vector3 hit )
	{
		hit = Vector3.zero;
		float ax = p0.x;
		float az = p0.z;
		float bx = d0.x;
		float bz = d0.z;
		float cx = p1.x;
		float cz = p1.z;
		float dx = d1.x;
		float dz = d1.z;
		float denom = bx * dz - bz * dx;
		if ( Mathf.Abs( denom ) < 0.00001f )
			return false;

		float t = ( ( cx - ax ) * dz - ( cz - az ) * dx ) / denom;
		hit = new Vector3( ax + bx * t, ( p0.y + p1.y ) * 0.5f, az + bz * t );
		return true;
	}

	static bool SameTrackPair( MinecartTrack a0, MinecartTrack b0, MinecartTrack a1, MinecartTrack b1 )
	{
		return ( a0 == a1 && b0 == b1 ) || ( a0 == b1 && b0 == a1 );
	}

	static bool AnglesMatch( float a0, float b0, float a1, float b1 )
	{
		const float eps = 8f;
		bool direct = Mathf.Abs( Mathf.DeltaAngle( a0 * Mathf.Rad2Deg, a1 * Mathf.Rad2Deg ) ) < eps
			&& Mathf.Abs( Mathf.DeltaAngle( b0 * Mathf.Rad2Deg, b1 * Mathf.Rad2Deg ) ) < eps;
		bool swapped = Mathf.Abs( Mathf.DeltaAngle( a0 * Mathf.Rad2Deg, b1 * Mathf.Rad2Deg ) ) < eps
			&& Mathf.Abs( Mathf.DeltaAngle( b0 * Mathf.Rad2Deg, a1 * Mathf.Rad2Deg ) ) < eps;
		return direct || swapped;
	}

	static void SyncCornerOptions( MinecartJunction junction, List<CornerCandidate> candidates )
	{
		if ( junction.cornerOptions == null )
			junction.cornerOptions = new List<MinecartJunctionCornerOption>( candidates.Count );

		List<MinecartJunctionCornerOption> previous = junction.cornerOptions;
		List<MinecartJunctionCornerOption> next = new List<MinecartJunctionCornerOption>( candidates.Count );
		for ( int i = 0; i < candidates.Count; i++ )
		{
			CornerCandidate c = candidates[ i ];
			MinecartJunctionCornerOption match = FindPathOption( previous, c.entryA.track, c.entryB.track, c.entryA.angle, c.entryB.angle );

			bool defaultEnabled = c.meshable
				&& c.cornerAngleDegrees >= ( ActiveGraph != null ? ActiveGraph.MinCornerAngleDegrees : 35f );

			next.Add( new MinecartJunctionCornerOption
			{
				label = c.label,
				trackA = c.entryA.track,
				trackB = c.entryB.track,
				angleA = c.entryA.angle,
				angleB = c.entryB.angle,
				cornerAngleDegrees = c.cornerAngleDegrees,
				enabled = match != null ? match.enabled : defaultEnabled
			} );
		}

		junction.cornerOptions = next;
	}

	static MinecartJunctionCornerOption FindCornerOption( MinecartJunction junction, CornerCandidate c )
	{
		return FindPathOption( junction.cornerOptions, c.entryA.track, c.entryB.track, c.entryA.angle, c.entryB.angle );
	}

	static void AppendBezierDualRail( MinecartJunctionGraph graph, JunctionEntry a, JunctionEntry b, Vector3 start, Vector3 control, Vector3 end, float gauge, float railWidth, float railHeight, Vector3 sleeperSize )
	{
		if ( ActiveGraph != null )
			ActiveGraph.EditorAddDebugCorner( ActiveJunctionWorld + start, ActiveJunctionWorld + control, ActiveJunctionWorld + end );

		int samples = graph.CurveSamples;
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
		EmitDualRailMesh( centers, rights, ups, samples, gauge, railWidth, railHeight, sleeperTop );
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
