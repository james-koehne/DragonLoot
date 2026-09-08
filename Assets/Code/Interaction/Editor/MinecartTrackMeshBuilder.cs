#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;

using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes a simple two-submesh rail + sleeper mesh for <see cref="MinecartTrack"/>.
/// </summary>
public static class MinecartTrackMeshBuilder
{
	const string GeneratedFolder = "Assets/Generated/MinecartTracks";
	const string MaterialsFolder = "Assets/Materials/Minecart";
	const string RailMatPath = MaterialsFolder + "/M_MinecartRail.mat";
	const string SleeperMatPath = MaterialsFolder + "/M_MinecartSleeper.mat";
	const string VisualName = MinecartTrack.VisualChildName;

	static readonly List<Vector3> Verts = new List<Vector3>( 512 );
	static readonly List<Vector3> Normals = new List<Vector3>( 512 );
	static readonly List<Vector2> Uvs = new List<Vector2>( 512 );
	static readonly List<int> RailTris = new List<int>( 1024 );
	static readonly List<int> SleeperTris = new List<int>( 1024 );
	static Transform MeshRoot;

	public static Material EnsureRailMaterial()
	{
		return EnsureLitMaterial( RailMatPath, new Color( 0.18f, 0.18f, 0.2f ), 0.65f, 0.45f );
	}

	public static Material EnsureSleeperMaterial()
	{
		return EnsureLitMaterial( SleeperMatPath, new Color( 0.35f, 0.22f, 0.12f ), 0.15f, 0.55f );
	}

	static bool Rebuilding;

	public static void Rebuild( MinecartTrack track )
	{
		if ( track == null || !track.IsUsable || Rebuilding )
			return;

		Rebuilding = true;
		try
		{
			RebuildInternal( track );
		}
		finally
		{
			Rebuilding = false;
		}
	}

	static void RebuildInternal( MinecartTrack track )
	{
		if ( track == null || !track.IsUsable )
			return;

		EnsureFolder( "Assets/Generated" );
		EnsureFolder( GeneratedFolder );

		Material railMat = track.RailMaterial != null ? track.RailMaterial : EnsureRailMaterial();
		Material sleeperMat = track.SleeperMaterial != null ? track.SleeperMaterial : EnsureSleeperMaterial();
		if ( track.RailMaterial == null || track.SleeperMaterial == null )
		{
			Undo.RecordObject( track, "Assign Minecart Track Materials" );
			track.EditorSetMaterials( railMat, sleeperMat );
			EditorUtility.SetDirty( track );
		}

		BuildMesh( track, out Mesh meshData );
		Mesh asset = SaveMeshAsset( track, meshData );
		ApplyVisual( track, asset, railMat, sleeperMat );
	}

	static void BuildMesh( MinecartTrack track, out Mesh mesh )
	{
		Verts.Clear();
		Normals.Clear();
		Uvs.Clear();
		RailTris.Clear();
		SleeperTris.Clear();
		MeshRoot = track.transform;

		float length = track.Length;
		float step = track.SampleStep;
		int samples = Mathf.Max( 2, Mathf.CeilToInt( length / step ) + 1 );
		if ( track.IsClosed )
			samples = Mathf.Max( 3, Mathf.RoundToInt( length / step ) );

		Vector3[] positions = new Vector3[ samples ];
		Vector3[] tangents = new Vector3[ samples ];
		Vector3[] ups = new Vector3[ samples ];
		Vector3[] rights = new Vector3[ samples ];
		for ( int i = 0; i < samples; i++ )
		{
			float t = track.IsClosed
				? ( length * i ) / samples
				: ( length * i ) / ( samples - 1 );
			Vector3 pos;
			Vector3 tan;
			Vector3 up;
			track.Evaluate( t, out pos, out tan, out up );
			positions[ i ] = pos;
			tangents[ i ] = tan;
			ups[ i ] = up;
			rights[ i ] = Vector3.Cross( up, tan ).normalized;
		}

		AppendRails( track, positions, ups, rights, samples );
		AppendSleepers( track, length );

		mesh = new Mesh();
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
	}

	static void AppendRails( MinecartTrack track, Vector3[] positions, Vector3[] ups, Vector3[] rights, int samples )
	{
		float gauge = track.RailGauge * 0.5f;
		float halfW = track.RailWidth * 0.5f;
		float railH = track.RailHeight;
		float sleeperTop = track.SleeperSize.y;
		int rings = track.IsClosed ? samples : samples;
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

	static Mesh SaveMeshAsset( MinecartTrack track, Mesh meshData )
	{
		string safeName = track.gameObject.name;
		foreach ( char c in Path.GetInvalidFileNameChars() )
			safeName = safeName.Replace( c, '_' );

		string path = GeneratedFolder + "/" + safeName + "_" + track.GetInstanceID().ToString( "X" ) + ".asset";
		Mesh existing = track.BakedMesh;
		if ( existing != null )
		{
			string existingPath = AssetDatabase.GetAssetPath( existing );
			if ( !string.IsNullOrEmpty( existingPath ) )
				path = existingPath;
		}

		Mesh asset = AssetDatabase.LoadAssetAtPath<Mesh>( path );
		if ( asset == null )
		{
			AssetDatabase.CreateAsset( meshData, path );
			asset = meshData;
		}
		else
		{
			asset.Clear();
			asset.SetVertices( Verts );
			asset.SetNormals( Normals );
			asset.SetUVs( 0, Uvs );
			asset.subMeshCount = 2;
			asset.SetTriangles( RailTris, 0 );
			asset.SetTriangles( SleeperTris, 1 );
			asset.RecalculateBounds();
			asset.RecalculateNormals();
			EditorUtility.SetDirty( asset );
			Object.DestroyImmediate( meshData );
		}

		track.EditorSetBakedMesh( asset );
		EditorUtility.SetDirty( track );
		return asset;
	}

	static void ApplyVisual( MinecartTrack track, Mesh mesh, Material railMat, Material sleeperMat )
	{
		Transform visual = track.transform.Find( VisualName );
		GameObject go = visual != null ? visual.gameObject : null;
		if ( go == null )
		{
			go = new GameObject( VisualName );
			Undo.RegisterCreatedObjectUndo( go, "Create Track Visual" );
			go.transform.SetParent( track.transform, false );
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

		track.RebuildWalkCollider();
		EditorUtility.SetDirty( go );
	}

	static Material EnsureLitMaterial( string path, Color color, float metallic, float smoothness )
	{
		Material existing = AssetDatabase.LoadAssetAtPath<Material>( path );
		if ( existing != null )
			return existing;

		EnsureFolder( "Assets/Materials" );
		EnsureFolder( MaterialsFolder );

		Shader shader = Shader.Find( "Universal Render Pipeline/Lit" );
		if ( shader == null )
			shader = Shader.Find( "HDRP/Lit" );

		if ( shader == null )
			shader = Shader.Find( "Standard" );

		Material mat = new Material( shader );
		mat.color = color;
		if ( mat.HasProperty( "_BaseColor" ) )
			mat.SetColor( "_BaseColor", color );
		if ( mat.HasProperty( "_Metallic" ) )
			mat.SetFloat( "_Metallic", metallic );
		if ( mat.HasProperty( "_Smoothness" ) )
			mat.SetFloat( "_Smoothness", smoothness );

		AssetDatabase.CreateAsset( mat, path );
		return mat;
	}

	static void EnsureFolder( string path )
	{
		AddressableEditorUtil.EnsureFolder( path );
	}
}
#endif
