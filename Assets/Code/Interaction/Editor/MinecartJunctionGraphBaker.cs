#if UNITY_EDITOR
using System.Collections.Generic;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds <see cref="MinecartJunctionGraph"/> from overlapping track samples and endpoints.
/// </summary>
public static class MinecartJunctionGraphBaker
{
	const float PortDistanceMerge = 0.35f;
	static bool Baking;

	struct Candidate
	{
		public Vector3 worldPosition;
		public MinecartTrack trackA;
		public float distanceA;
		public MinecartTrack trackB;
		public float distanceB;
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

	[MenuItem( DragonLootMenus.MinecartCreateJunctionGraph )]
	[MenuItem( DragonLootMenus.GameObjectMinecartJunctionGraph )]
	public static void CreateJunctionGraph()
	{
		Scene scene = SceneManager.GetActiveScene();
		if ( !scene.IsValid() || !scene.isLoaded )
			return;

		MinecartJunctionGraph existing = Object.FindFirstObjectByType<MinecartJunctionGraph>();
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
		MinecartJunctionGraph graph = Object.FindFirstObjectByType<MinecartJunctionGraph>();
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

		MinecartJunctionGraph graph = Object.FindFirstObjectByType<MinecartJunctionGraph>();
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
		float merge = graph.ClusterMergeRadius;

		for ( int i = 0; i < tracks.Count; i++ )
		{
			MinecartTrack a = tracks[ i ];
			for ( int j = i + 1; j < tracks.Count; j++ )
			{
				MinecartTrack b = tracks[ j ];
				CollectMidPathOverlaps( a, b, detect, step, candidates );
				CollectEndpointOverlaps( a, b, detect, candidates );
				CollectEndpointOverlaps( b, a, detect, candidates );
			}
		}

		List<Cluster> clusters = ClusterCandidates( candidates, merge );
		List<MinecartJunction> junctions = new List<MinecartJunction>( clusters.Count );
		List<MinecartTrackJunctionIndex> index = new List<MinecartTrackJunctionIndex>( clusters.Count * 2 );

		for ( int c = 0; c < clusters.Count; c++ )
		{
			Cluster cluster = clusters[ c ];
			if ( cluster.count <= 0 || cluster.ports == null || cluster.ports.Count < 2 )
				continue;

			List<MinecartJunctionPort> ports = BuildPorts( cluster.ports );
			if ( ports.Count < 2 )
				continue;

			Vector3 center = cluster.sum / cluster.count;
			MinecartJunction junction = new MinecartJunction
			{
				worldPosition = center,
				ports = ports
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

		if ( !Application.isPlaying )
			EditorSceneManager.MarkSceneDirty( graph.gameObject.scene );

		Debug.Log( $"[MinecartJunctionGraph] Baked {junctions.Count} junction(s) from {tracks.Count} track(s).", graph );
	}

	static List<MinecartTrack> CollectTracks()
	{
		MinecartTrack[] found = Object.FindObjectsByType<MinecartTrack>( FindObjectsInactive.Exclude, FindObjectsSortMode.None );
		List<MinecartTrack> tracks = new List<MinecartTrack>( found.Length );
		for ( int i = 0; i < found.Length; i++ )
		{
			MinecartTrack track = found[ i ];
			if ( track != null && track.IsUsable )
				tracks.Add( track );
		}

		return tracks;
	}

	static void CollectMidPathOverlaps( MinecartTrack a, MinecartTrack b, float detectRadius, float sampleStep, List<Candidate> candidates )
	{
		float lengthA = a.Length;
		int samples = Mathf.Max( 2, Mathf.CeilToInt( lengthA / sampleStep ) + 1 );
		float detectSqr = detectRadius * detectRadius;

		for ( int i = 0; i < samples; i++ )
		{
			float distA = lengthA * i / ( samples - 1 );
			Vector3 posA;
			Vector3 tanA;
			Vector3 upA;
			if ( !a.Evaluate( distA, out posA, out tanA, out upA ) )
				continue;

			float distB;
			Vector3 nearestB;
			if ( !b.TryGetNearestPoint( posA, out distB, out nearestB ) )
				continue;

			if ( ( nearestB - posA ).sqrMagnitude > detectSqr )
				continue;

			candidates.Add( new Candidate
			{
				worldPosition = ( posA + nearestB ) * 0.5f,
				trackA = a,
				distanceA = distA,
				trackB = b,
				distanceB = distB
			} );
		}
	}

	static void CollectEndpointOverlaps( MinecartTrack from, MinecartTrack other, float detectRadius, List<Candidate> candidates )
	{
		if ( from.IsClosed )
			return;

		TryEndpoint( from, 0f, other, detectRadius, candidates );
		TryEndpoint( from, from.Length, other, detectRadius, candidates );
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
			distanceB = otherDistance
		} );
	}

	static List<Cluster> ClusterCandidates( List<Candidate> candidates, float mergeRadius )
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
				AddPort( created.ports, candidate.trackA, candidate.distanceA );
				AddPort( created.ports, candidate.trackB, candidate.distanceB );
				clusters.Add( created );
				continue;
			}

			Cluster existing = clusters[ best ];
			existing.sum += candidate.worldPosition;
			existing.count++;
			AddPort( existing.ports, candidate.trackA, candidate.distanceA );
			AddPort( existing.ports, candidate.trackB, candidate.distanceB );
			clusters[ best ] = existing;
		}

		return clusters;
	}

	static void AddPort( List<PortKey> ports, MinecartTrack track, float distance )
	{
		if ( track == null || ports == null )
			return;

		for ( int i = 0; i < ports.Count; i++ )
		{
			PortKey existing = ports[ i ];
			if ( existing.track != track )
				continue;

			if ( Mathf.Abs( existing.distance - distance ) <= PortDistanceMerge )
				return;
		}

		ports.Add( new PortKey { track = track, distance = distance } );
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
