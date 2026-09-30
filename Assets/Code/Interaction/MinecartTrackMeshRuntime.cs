using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Builds rail + sleeper meshes for <see cref="MinecartTrack"/> using the current junction gap mask.
/// Used at runtime / edit preview (instance meshes) and by the editor baker (saved assets).
/// </summary>
public static class MinecartTrackMeshRuntime
{
	static readonly List<Vector3> Verts = new List<Vector3>( 512 );
	static readonly List<Vector3> Normals = new List<Vector3>( 512 );
	static readonly List<Vector2> Uvs = new List<Vector2>( 512 );
	static readonly List<int> RailTris = new List<int>( 1024 );
	static readonly List<int> SleeperTris = new List<int>( 1024 );
	static Transform MeshRoot;

	/// <summary>
	/// Rebuilds the track visual as a non-asset instance mesh from the live gap mask, then walk collider.
	/// Does not modify the serialized baked mesh asset.
	/// </summary>
	public static void RebuildInstanceVisual( MinecartTrack track )
	{
		if ( track == null || !track.IsUsable )
			return;

		Mesh mesh = BuildMesh( track );
		if ( mesh == null )
			return;

		track.ApplyInstanceVisualMesh( mesh );
		track.RebuildWalkCollider();
	}

	/// <summary>
	/// Restores the serialized baked mesh on TrackVisual (full-gap authored asset) and rebuilds walk collider.
	/// </summary>
	public static void RestoreBakedVisual( MinecartTrack track )
	{
		if ( track == null )
			return;

		track.RestoreBakedVisualMesh();
		track.RebuildWalkCollider();
	}

	public static Mesh BuildMesh( MinecartTrack track )
	{
		if ( track == null || !track.IsUsable )
			return null;

		Verts.Clear();
		Normals.Clear();
		Uvs.Clear();
		RailTris.Clear();
		SleeperTris.Clear();
		MeshRoot = track.transform;

		float length = track.Length;
		float step = track.SampleStep;
		int baseSamples = Mathf.Max( 2, Mathf.CeilToInt( length / step ) + 1 );
		if ( track.IsClosed )
			baseSamples = Mathf.Max( 3, Mathf.RoundToInt( length / step ) );

		List<float> distances = new List<float>( baseSamples + 8 );
		for ( int i = 0; i < baseSamples; i++ )
		{
			float t = track.IsClosed
				? ( length * i ) / baseSamples
				: ( length * i ) / ( baseSamples - 1 );
			distances.Add( t );
		}

		List<float> cutCaps = new List<float>( 4 );
		MinecartJunctionGraph.CollectTrackMeshCutDistancesFor( track, cutCaps );
		for ( int i = 0; i < cutCaps.Count; i++ )
			InsertUniqueDistance( track, distances, cutCaps[ i ] );

		distances.Sort();

		int samples = distances.Count;
		Vector3[] positions = new Vector3[ samples ];
		Vector3[] ups = new Vector3[ samples ];
		Vector3[] rights = new Vector3[ samples ];
		bool[] inGap = new bool[ samples ];
		for ( int i = 0; i < samples; i++ )
		{
			float t = distances[ i ];
			Vector3 pos;
			Vector3 tan;
			Vector3 up;
			track.Evaluate( t, out pos, out tan, out up );
			positions[ i ] = pos;
			ups[ i ] = up;
			rights[ i ] = Vector3.Cross( up, tan ).normalized;
			bool gap = MinecartJunctionGraph.IsTrackDistanceInMeshGap( track, t );
			if ( gap && IsNearAnyDistance( track, t, cutCaps, 0.002f ) )
				gap = false;
			inGap[ i ] = gap;
		}

		AppendRails( track, positions, ups, rights, samples, inGap );
		AppendSleepers( track, length );

		Mesh mesh = new Mesh();
		mesh.name = track.gameObject.name + "_Track";
		mesh.SetVertices( Verts );
		mesh.SetNormals( Normals );
		mesh.SetUVs( 0, Uvs );
		mesh.subMeshCount = 2;
		mesh.SetTriangles( RailTris, 0 );
		mesh.SetTriangles( SleeperTris, 1 );
		mesh.RecalculateBounds();
		mesh.RecalculateNormals();
		MeshRoot = null;
		return mesh;
	}

	static void InsertUniqueDistance( MinecartTrack track, List<float> distances, float distance )
	{
		const float eps = 0.001f;
		for ( int i = 0; i < distances.Count; i++ )
		{
			if ( Mathf.Abs( track.SignedAlong( distances[ i ], distance ) ) <= eps )
				return;
		}

		distances.Add( distance );
	}

	static bool IsNearAnyDistance( MinecartTrack track, float distance, List<float> candidates, float eps )
	{
		if ( candidates == null )
			return false;

		for ( int i = 0; i < candidates.Count; i++ )
		{
			if ( Mathf.Abs( track.SignedAlong( distance, candidates[ i ] ) ) <= eps )
				return true;
		}

		return false;
	}

	static void AppendRails( MinecartTrack track, Vector3[] positions, Vector3[] ups, Vector3[] rights, int samples, bool[] inGap )
	{
		float gauge = track.RailGauge * 0.5f;
		float halfW = track.RailWidth * 0.5f;
		float railH = track.RailHeight;
		float sleeperTop = track.SleeperSize.y;
		int rings = samples;
		int segs = track.IsClosed ? samples : samples - 1;

		int leftStart = Verts.Count;
		for ( int i = 0; i < rings; i++ )
		{
			Vector3 center = positions[ i ] + rights[ i ] * -gauge + ups[ i ] * ( sleeperTop + railH * 0.5f );
			AddRailRing( center, rights[ i ], ups[ i ], halfW, railH * 0.5f );
		}

		int rightStart = Verts.Count;
		for ( int i = 0; i < rings; i++ )
		{
			Vector3 center = positions[ i ] + rights[ i ] * gauge + ups[ i ] * ( sleeperTop + railH * 0.5f );
			AddRailRing( center, rights[ i ], ups[ i ], halfW, railH * 0.5f );
		}

		for ( int i = 0; i < segs; i++ )
		{
			int a = i;
			int b = ( i + 1 ) % rings;
			if ( inGap != null && inGap.Length > a && inGap.Length > b && ( inGap[ a ] || inGap[ b ] ) )
				continue;

			ConnectRailRings( leftStart + a * 4, leftStart + b * 4 );
			ConnectRailRings( rightStart + a * 4, rightStart + b * 4 );
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

	static void AppendSleepers( MinecartTrack track, float length )
	{
		float spacing = track.SleeperSpacing;
		Vector3 size = track.SleeperSize;
		int count = Mathf.Max( 1, Mathf.FloorToInt( length / spacing ) );
		if ( track.IsClosed )
			count = Mathf.Max( 1, Mathf.RoundToInt( length / spacing ) );

		for ( int i = 0; i < count; i++ )
		{
			float dist = track.IsClosed
				? ( length * i ) / count
				: Mathf.Min( length, i * spacing );
			if ( MinecartJunctionGraph.IsTrackDistanceInMeshGap( track, dist ) )
				continue;

			Vector3 pos;
			Vector3 tan;
			Vector3 up;
			if ( !track.Evaluate( dist, out pos, out tan, out up ) )
				continue;

			Vector3 right = Vector3.Cross( up, tan ).normalized;
			Vector3 center = pos + up * ( size.y * 0.5f );
			AddBox( center, right, up, tan, size, SleeperTris );
		}
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
		if ( MeshRoot != null )
		{
			pos = MeshRoot.InverseTransformPoint( pos );
			normal = MeshRoot.InverseTransformDirection( normal );
		}

		Verts.Add( pos );
		Normals.Add( normal );
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
}
