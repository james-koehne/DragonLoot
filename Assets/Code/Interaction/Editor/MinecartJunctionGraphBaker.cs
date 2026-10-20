#if UNITY_EDITOR
using System.Collections.Generic;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds <see cref="MinecartJunctionGraph"/> from overlapping track samples and endpoints.
/// Mid-path hits are peak-picked; clusters merge transitively so Y joins become one Tee.
/// </summary>
public static class MinecartJunctionGraphBaker
{
	const float PortDistanceMergeFloor = 1.0f;
	static bool Baking;

	struct Candidate
	{
		public Vector3 worldPosition;
		public MinecartTrack trackA;
		public float distanceA;
		public MinecartTrack trackB;
		public float distanceB;
		public bool fromEndpoint;
	}

	struct Cluster
	{
		public Vector3 sum;
		public int count;
		public List<PortKey> ports;
	}

	struct PortKey
	{
		public MinecartTrack track;
		public float distance;
	}

	struct SampleHit
	{
		public float distA;
		public float distB;
		public Vector3 worldPosition;
		public float separationSqr;
		public bool inRange;
	}

	[MenuItem( DragonLootMenus.MinecartCreateJunctionGraph )]
	[MenuItem( DragonLootMenus.GameObjectMinecartJunctionGraph )]
	public static void CreateJunctionGraph()
	{
		Scene scene = SceneManager.GetActiveScene();
		if ( !scene.IsValid() || !scene.isLoaded )
			return;

		MinecartJunctionGraph existing = Object.FindAnyObjectByType<MinecartJunctionGraph>();
		if ( existing != null )
		{
			Selection.activeGameObject = existing.gameObject;
			Bake( existing );
			return;
		}

		GameObject go = new GameObject( "MinecartJunctionGraph" );
		MinecartJunctionGraph graph = go.AddComponent<MinecartJunctionGraph>();
		Undo.RegisterCreatedObjectUndo( go, "Create Minecart Junction Graph" );
		Selection.activeGameObject = go;
		Bake( graph );
	}

	[MenuItem( DragonLootMenus.MinecartRebuildJunctions )]
	public static void RebuildJunctionsMenu()
	{
		MinecartJunctionGraph graph = Object.FindAnyObjectByType<MinecartJunctionGraph>();
		if ( graph == null )
		{
			CreateJunctionGraph();
			return;
		}

		Bake( graph );
	}

	public static void RebuildActiveSceneIfPresent()
	{
		if ( Baking )
			return;

		MinecartJunctionGraph graph = Object.FindAnyObjectByType<MinecartJunctionGraph>();
		if ( graph == null )
			return;

		Bake( graph );
	}

	public static void Bake( MinecartJunctionGraph graph )
	{
		if ( graph == null || Baking )
			return;

		Baking = true;
		try
		{
			BakeInternal( graph );
		}
		finally
		{
			Baking = false;
		}
	}

	static void BakeInternal( MinecartJunctionGraph graph )
	{
		Undo.RecordObject( graph, "Bake Minecart Junctions" );

		List<MinecartTrack> tracks = CollectTracks();
		List<Candidate> candidates = new List<Candidate>( 64 );
		float detect = graph.DetectRadius;
		float step = graph.SampleStep;
		float merge = Mathf.Max( graph.ClusterMergeRadius, detect * 2.5f );
		float portMerge = Mathf.Max( PortDistanceMergeFloor, graph.JunctionJoinRadius * 0.5f );

		for ( int i = 0; i < tracks.Count; i++ )
		{
			MinecartTrack a = tracks[ i ];
			CollectSelfMidPathOverlaps( a, detect, step, graph.MinSelfSeparation, candidates );
			CollectSelfEndpointOverlaps( a, detect, graph.MinSelfSeparation, candidates );

			for ( int j = i + 1; j < tracks.Count; j++ )
			{
				MinecartTrack b = tracks[ j ];
				CollectMidPathOverlaps( a, b, detect, step, candidates );
				CollectEndpointOverlaps( a, b, detect, candidates );
				CollectEndpointOverlaps( b, a, detect, candidates );
			}
		}

		SuppressMidPathNearEndpoints( candidates, merge );

		List<Cluster> clusters = ClusterCandidates( candidates, merge, portMerge );
		MergeClustersTransitive( clusters, merge, portMerge );

		List<MinecartJunction> junctions = new List<MinecartJunction>( clusters.Count );
		List<MinecartTrackJunctionIndex> index = new List<MinecartTrackJunctionIndex>( clusters.Count * 2 );

		for ( int c = 0; c < clusters.Count; c++ )
		{
			Cluster cluster = clusters[ c ];
			if ( cluster.count <= 0 || cluster.ports == null || cluster.ports.Count < 2 )
				continue;

			CollapseSameTrackPorts( cluster.ports, portMerge, cluster.sum / Mathf.Max( 1, cluster.count ) );
			List<MinecartJunctionPort> ports = BuildPorts( cluster.ports );
			if ( ports.Count < 2 )
				continue;

			Vector3 center = cluster.sum / cluster.count;
			MinecartJunction preserved = graph.EditorFindPreservedJunction( center );
			MinecartJunctionKind preservedKind = preserved != null ? preserved.kind : MinecartJunctionKind.Auto;
			MinecartJunctionKind resolved = MinecartJunctionGraph.ResolveKind( preservedKind, ports );
			MinecartJunction junction = new MinecartJunction
			{
				worldPosition = center,
				kind = preservedKind,
				resolvedKind = resolved,
				ports = ports,
				cornerOptions = MinecartJunctionGraph.CloneCornerOptions( preserved != null ? preserved.cornerOptions : null ),
				throughOptions = MinecartJunctionGraph.CloneThroughOptions( preserved != null ? preserved.throughOptions : null ),
				ridePaths = new List<MinecartJunctionRidePath>( 8 )
			};
			int junctionIndex = junctions.Count;
			junctions.Add( junction );

			for ( int p = 0; p < ports.Count; p++ )
			{
				MinecartJunctionPort port = ports[ p ];
				index.Add( new MinecartTrackJunctionIndex
				{
					track = port.track,
					distance = port.distance,
					junctionIndex = junctionIndex
				} );
			}
		}

		graph.EditorApplyBake( junctions, index );
		EditorUtility.SetDirty( graph );

		MinecartTrackMeshBuilder.RebuildAllWithJunctionGaps();
		MinecartJunctionMeshBuilder.Rebuild( graph );
		graph.RefreshBuildVisuals();

		if ( !Application.isPlaying )
			EditorSceneManager.MarkSceneDirty( graph.gameObject.scene );

		Debug.Log( $"[MinecartJunctionGraph] Baked {junctions.Count} junction(s) from {tracks.Count} track(s).", graph );
	}

	static List<MinecartTrack> CollectTracks()
	{
		MinecartTrack[] found = Object.FindObjectsByType<MinecartTrack>( FindObjectsInactive.Exclude );
		List<MinecartTrack> tracks = new List<MinecartTrack>( found.Length );
		for ( int i = 0; i < found.Length; i++ )
		{
			MinecartTrack track = found[ i ];
			if ( track != null && track.IsUsable )
				tracks.Add( track );
		}

		return tracks;
	}

	/// <summary>
	/// Sample along A; emit one candidate per continuous in-range run at the local separation minimum.
	/// </summary>
	static void CollectMidPathOverlaps( MinecartTrack a, MinecartTrack b, float detectRadius, float sampleStep, List<Candidate> candidates )
	{
		float lengthA = a.Length;
		int samples = Mathf.Max( 2, Mathf.CeilToInt( lengthA / sampleStep ) + 1 );
		float detectSqr = detectRadius * detectRadius;

		SampleHit[] hits = new SampleHit[ samples ];
		for ( int i = 0; i < samples; i++ )
		{
			float distA = lengthA * i / ( samples - 1 );
			SampleHit hit = new SampleHit { distA = distA, inRange = false, separationSqr = float.MaxValue };
			Vector3 posA;
			Vector3 tanA;
			Vector3 upA;
			if ( a.Evaluate( distA, out posA, out tanA, out upA ) )
			{
				float distB;
				Vector3 nearestB;
				if ( b.TryGetNearestPoint( posA, out distB, out nearestB ) )
				{
					float sepSqr = ( nearestB - posA ).sqrMagnitude;
					hit.distB = distB;
					hit.worldPosition = ( posA + nearestB ) * 0.5f;
					hit.separationSqr = sepSqr;
					hit.inRange = sepSqr <= detectSqr;
				}
			}

			hits[ i ] = hit;
		}

		EmitPeakCandidates( a, b, hits, candidates, fromEndpoint: false );
	}

	static void EmitPeakCandidates( MinecartTrack a, MinecartTrack b, SampleHit[] hits, List<Candidate> candidates, bool fromEndpoint )
	{
		int i = 0;
		while ( i < hits.Length )
		{
			if ( !hits[ i ].inRange )
			{
				i++;
				continue;
			}

			int runStart = i;
			int best = i;
			float bestSep = hits[ i ].separationSqr;
			i++;
			while ( i < hits.Length && hits[ i ].inRange )
			{
				if ( hits[ i ].separationSqr < bestSep )
				{
					bestSep = hits[ i ].separationSqr;
					best = i;
				}

				i++;
			}

			SampleHit peak = hits[ best ];
			candidates.Add( new Candidate
			{
				worldPosition = peak.worldPosition,
				trackA = a,
				distanceA = peak.distA,
				trackB = b,
				distanceB = peak.distB,
				fromEndpoint = fromEndpoint
			} );

			// Skip empty; loop continues at i (first out-of-range or end).
			_ = runStart;
		}
	}

	static void CollectEndpointOverlaps( MinecartTrack from, MinecartTrack other, float detectRadius, List<Candidate> candidates )
	{
		if ( from.IsClosed )
			return;

		TryEndpoint( from, 0f, other, detectRadius, candidates );
		TryEndpoint( from, from.Length, other, detectRadius, candidates );
	}

	static void CollectSelfMidPathOverlaps( MinecartTrack track, float detectRadius, float sampleStep, float minSeparation, List<Candidate> candidates )
	{
		if ( track == null || !track.IsUsable )
			return;

		float length = track.Length;
		if ( length < minSeparation * 2f )
			return;

		int samples = Mathf.Max( 2, Mathf.CeilToInt( length / sampleStep ) + 1 );
		float detectSqr = detectRadius * detectRadius;
		SampleHit[] hits = new SampleHit[ samples ];

		for ( int i = 0; i < samples; i++ )
		{
			float distA = length * i / ( samples - 1 );
			SampleHit hit = new SampleHit { distA = distA, inRange = false, separationSqr = float.MaxValue };
			Vector3 posA;
			Vector3 tanA;
			Vector3 upA;
			if ( track.Evaluate( distA, out posA, out tanA, out upA ) )
			{
				float distB;
				Vector3 nearestB;
				if ( TryGetNearestSelfPoint( track, distA, posA, detectSqr, minSeparation, out distB, out nearestB )
				     && ShouldKeepSelfPair( track, distA, distB ) )
				{
					float sepSqr = ( nearestB - posA ).sqrMagnitude;
					hit.distB = distB;
					hit.worldPosition = ( posA + nearestB ) * 0.5f;
					hit.separationSqr = sepSqr;
					hit.inRange = sepSqr <= detectSqr;
				}
			}

			hits[ i ] = hit;
		}

		EmitPeakCandidates( track, track, hits, candidates, fromEndpoint: false );
	}

	static void CollectSelfEndpointOverlaps( MinecartTrack track, float detectRadius, float minSeparation, List<Candidate> candidates )
	{
		if ( track == null || !track.IsUsable || track.IsClosed )
			return;

		float length = track.Length;
		if ( length < minSeparation * 2f )
			return;

		TrySelfEndpoint( track, 0f, detectRadius, minSeparation, candidates );
		TrySelfEndpoint( track, length, detectRadius, minSeparation, candidates );
	}

	static void TrySelfEndpoint( MinecartTrack track, float fromDistance, float detectRadius, float minSeparation, List<Candidate> candidates )
	{
		Vector3 pos;
		Vector3 tan;
		Vector3 up;
		if ( !track.Evaluate( fromDistance, out pos, out tan, out up ) )
			return;

		float otherDistance;
		Vector3 nearest;
		float detectSqr = detectRadius * detectRadius;
		if ( !TryGetNearestSelfPoint( track, fromDistance, pos, detectSqr, minSeparation, out otherDistance, out nearest ) )
			return;

		candidates.Add( new Candidate
		{
			worldPosition = ( pos + nearest ) * 0.5f,
			trackA = track,
			distanceA = fromDistance,
			trackB = track,
			distanceB = otherDistance,
			fromEndpoint = true
		} );
	}

	static bool TryGetNearestSelfPoint( MinecartTrack track, float fromDistance, Vector3 fromPos, float detectSqr, float minSeparation, out float otherDistance, out Vector3 nearest )
	{
		otherDistance = 0f;
		nearest = fromPos;
		float length = track.Length;
		float step = Mathf.Max( 0.15f, length / 64f );
		int samples = Mathf.Max( 2, Mathf.CeilToInt( length / step ) + 1 );
		float bestSqr = detectSqr;
		bool found = false;

		for ( int i = 0; i < samples; i++ )
		{
			float dist = length * i / ( samples - 1 );
			if ( Mathf.Abs( track.SignedAlong( fromDistance, dist ) ) < minSeparation )
				continue;

			Vector3 pos;
			Vector3 tan;
			Vector3 up;
			if ( !track.Evaluate( dist, out pos, out tan, out up ) )
				continue;

			float sqr = ( pos - fromPos ).sqrMagnitude;
			if ( sqr > bestSqr )
				continue;

			bestSqr = sqr;
			otherDistance = dist;
			nearest = pos;
			found = true;
		}

		return found;
	}

	static bool ShouldKeepSelfPair( MinecartTrack track, float distA, float distB )
	{
		if ( track.IsClosed )
		{
			float sep = track.SignedAlong( distA, distB );
			return sep > 0f;
		}

		return distA < distB - 0.001f;
	}

	static void TryEndpoint( MinecartTrack from, float fromDistance, MinecartTrack other, float detectRadius, List<Candidate> candidates )
	{
		Vector3 pos;
		Vector3 tan;
		Vector3 up;
		if ( !from.Evaluate( fromDistance, out pos, out tan, out up ) )
			return;

		float otherDistance;
		Vector3 nearest;
		if ( !other.TryGetNearestPoint( pos, out otherDistance, out nearest ) )
			return;

		float detectSqr = detectRadius * detectRadius;
		if ( ( nearest - pos ).sqrMagnitude > detectSqr )
			return;

		candidates.Add( new Candidate
		{
			worldPosition = ( pos + nearest ) * 0.5f,
			trackA = from,
			distanceA = fromDistance,
			trackB = other,
			distanceB = otherDistance,
			fromEndpoint = true
		} );
	}

	/// <summary>
	/// Stub endpoint joins win: drop mid-path smear candidates near an accepted endpoint join.
	/// </summary>
	static void SuppressMidPathNearEndpoints( List<Candidate> candidates, float alongRadius )
	{
		if ( candidates == null || candidates.Count == 0 )
			return;

		float along = Mathf.Max( 0.5f, alongRadius );
		for ( int i = candidates.Count - 1; i >= 0; i-- )
		{
			Candidate mid = candidates[ i ];
			if ( mid.fromEndpoint )
				continue;

			bool nearEndpoint = false;
			for ( int j = 0; j < candidates.Count; j++ )
			{
				Candidate end = candidates[ j ];
				if ( !end.fromEndpoint )
					continue;

				if ( !SameUnorderedTrackPair( mid.trackA, mid.trackB, end.trackA, end.trackB ) )
					continue;

				if ( AlongClose( mid.trackA, mid.distanceA, end.trackA, end.distanceA, along )
				     || AlongClose( mid.trackA, mid.distanceA, end.trackB, end.distanceB, along )
				     || AlongClose( mid.trackB, mid.distanceB, end.trackA, end.distanceA, along )
				     || AlongClose( mid.trackB, mid.distanceB, end.trackB, end.distanceB, along ) )
				{
					nearEndpoint = true;
					break;
				}
			}

			if ( nearEndpoint )
				candidates.RemoveAt( i );
		}
	}

	static bool SameUnorderedTrackPair( MinecartTrack a0, MinecartTrack b0, MinecartTrack a1, MinecartTrack b1 )
	{
		return ( a0 == a1 && b0 == b1 ) || ( a0 == b1 && b0 == a1 );
	}

	static bool AlongClose( MinecartTrack track, float dist, MinecartTrack otherTrack, float otherDist, float radius )
	{
		if ( track == null || track != otherTrack )
			return false;

		return Mathf.Abs( track.SignedAlong( dist, otherDist ) ) <= radius;
	}

	static List<Cluster> ClusterCandidates( List<Candidate> candidates, float mergeRadius, float portMerge )
	{
		List<Cluster> clusters = new List<Cluster>( candidates.Count );
		float mergeSqr = mergeRadius * mergeRadius;

		for ( int i = 0; i < candidates.Count; i++ )
		{
			Candidate candidate = candidates[ i ];
			int best = -1;
			float bestSqr = mergeSqr;
			for ( int c = 0; c < clusters.Count; c++ )
			{
				Cluster cluster = clusters[ c ];
				Vector3 center = cluster.sum / Mathf.Max( 1, cluster.count );
				float sqr = ( center - candidate.worldPosition ).sqrMagnitude;
				if ( sqr >= bestSqr )
					continue;

				bestSqr = sqr;
				best = c;
			}

			if ( best < 0 )
			{
				Cluster created = new Cluster
				{
					sum = candidate.worldPosition,
					count = 1,
					ports = new List<PortKey>( 4 )
				};
				AddPort( created.ports, candidate.trackA, candidate.distanceA, portMerge );
				AddPort( created.ports, candidate.trackB, candidate.distanceB, portMerge );
				clusters.Add( created );
				continue;
			}

			Cluster existing = clusters[ best ];
			existing.sum += candidate.worldPosition;
			existing.count++;
			AddPort( existing.ports, candidate.trackA, candidate.distanceA, portMerge );
			AddPort( existing.ports, candidate.trackB, candidate.distanceB, portMerge );
			clusters[ best ] = existing;
		}

		return clusters;
	}

	static void MergeClustersTransitive( List<Cluster> clusters, float mergeRadius, float portMerge )
	{
		if ( clusters == null || clusters.Count < 2 )
			return;

		float mergeSqr = mergeRadius * mergeRadius;
		bool merged = true;
		while ( merged )
		{
			merged = false;
			for ( int i = 0; i < clusters.Count; i++ )
			{
				Cluster a = clusters[ i ];
				if ( a.count <= 0 )
					continue;

				Vector3 centerA = a.sum / a.count;
				for ( int j = i + 1; j < clusters.Count; j++ )
				{
					Cluster b = clusters[ j ];
					if ( b.count <= 0 )
						continue;

					Vector3 centerB = b.sum / b.count;
					bool close = ( centerA - centerB ).sqrMagnitude <= mergeSqr;
					if ( !close )
						close = ClustersShareClosePort( a, b, portMerge );

					if ( !close )
						continue;

					a.sum += b.sum;
					a.count += b.count;
					for ( int p = 0; p < b.ports.Count; p++ )
						AddPort( a.ports, b.ports[ p ].track, b.ports[ p ].distance, portMerge );

					clusters[ i ] = a;
					clusters.RemoveAt( j );
					merged = true;
					break;
				}

				if ( merged )
					break;
			}
		}
	}

	static bool ClustersShareClosePort( Cluster a, Cluster b, float portMerge )
	{
		if ( a.ports == null || b.ports == null )
			return false;

		for ( int i = 0; i < a.ports.Count; i++ )
		{
			PortKey pa = a.ports[ i ];
			if ( pa.track == null )
				continue;

			for ( int j = 0; j < b.ports.Count; j++ )
			{
				PortKey pb = b.ports[ j ];
				if ( pb.track != pa.track )
					continue;

				if ( Mathf.Abs( pa.track.SignedAlong( pa.distance, pb.distance ) ) <= portMerge * 2f )
					return true;
			}
		}

		return false;
	}

	static void AddPort( List<PortKey> ports, MinecartTrack track, float distance, float portMerge )
	{
		if ( track == null || ports == null )
			return;

		for ( int i = 0; i < ports.Count; i++ )
		{
			PortKey existing = ports[ i ];
			if ( existing.track != track )
				continue;

			if ( Mathf.Abs( track.SignedAlong( existing.distance, distance ) ) <= portMerge )
				return;
		}

		ports.Add( new PortKey { track = track, distance = distance } );
	}

	/// <summary>
	/// Final collapse: smear ports on the same rail merge to the sample closest to the cluster center.
	/// </summary>
	static void CollapseSameTrackPorts( List<PortKey> ports, float portMerge, Vector3 clusterCenter )
	{
		if ( ports == null || ports.Count < 2 )
			return;

		for ( int i = 0; i < ports.Count; i++ )
		{
			PortKey keep = ports[ i ];
			if ( keep.track == null )
				continue;

			float bestDist = keep.distance;
			float bestWorldSep = float.MaxValue;
			Vector3 keepPos;
			Vector3 tan;
			Vector3 up;
			if ( keep.track.Evaluate( keep.distance, out keepPos, out tan, out up ) )
				bestWorldSep = ( keepPos - clusterCenter ).sqrMagnitude;

			for ( int j = ports.Count - 1; j > i; j-- )
			{
				PortKey other = ports[ j ];
				if ( other.track != keep.track )
					continue;

				if ( Mathf.Abs( keep.track.SignedAlong( keep.distance, other.distance ) ) > portMerge )
					continue;

				Vector3 otherPos;
				if ( other.track.Evaluate( other.distance, out otherPos, out tan, out up ) )
				{
					float sep = ( otherPos - clusterCenter ).sqrMagnitude;
					if ( sep < bestWorldSep )
					{
						bestWorldSep = sep;
						bestDist = other.distance;
					}
				}

				ports.RemoveAt( j );
			}

			keep.distance = bestDist;
			ports[ i ] = keep;
		}
	}

	static List<MinecartJunctionPort> BuildPorts( List<PortKey> keys )
	{
		List<MinecartJunctionPort> ports = new List<MinecartJunctionPort>( keys.Count );
		for ( int i = 0; i < keys.Count; i++ )
		{
			PortKey key = keys[ i ];
			if ( key.track == null || !key.track.IsUsable )
				continue;

			Vector3 pos;
			Vector3 tan;
			Vector3 up;
			if ( !key.track.Evaluate( key.distance, out pos, out tan, out up ) )
				continue;

			if ( tan.sqrMagnitude < 0.0001f )
				continue;

			ports.Add( new MinecartJunctionPort
			{
				track = key.track,
				distance = key.track.WrapDistance( key.distance ),
				tangent = tan.normalized
			} );
		}

		return ports;
	}
}
#endif
