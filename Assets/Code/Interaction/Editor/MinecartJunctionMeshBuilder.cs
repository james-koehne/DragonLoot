#if UNITY_EDITOR
using System.Collections.Generic;

using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds visual rail/sleeper pad meshes and walk colliders for baked junctions.
/// </summary>
public static class MinecartJunctionMeshBuilder
{
	public const string JunctionChildPrefix = "Junction_";
	public const string VisualChildName = "JunctionVisual";
	public const string ColliderChildName = "JunctionCollider";

	const string GeneratedFolder = "Assets/Generated/MinecartJunctions";
	const int RailSamples = 5;

	static readonly List<Vector3> Verts = new List<Vector3>( 256 );
	static readonly List<Vector3> Normals = new List<Vector3>( 256 );
	static readonly List<Vector2> Uvs = new List<Vector2>( 256 );
	static readonly List<int> RailTris = new List<int>( 512 );
	static readonly List<int> SleeperTris = new List<int>( 256 );
	static readonly List<Vector3> ColliderVerts = new List<Vector3>( 64 );
	static readonly List<int> ColliderTris = new List<int>( 128 );

	public static void Rebuild( MinecartJunctionGraph graph )
	{
		if ( graph == null )
			return;

		EnsureFolder( "Assets/Generated" );
		EnsureFolder( GeneratedFolder );

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

		Mesh visualMesh = BuildVisualMesh( go.transform, junction, gap, gauge, railWidth, railHeight, sleeperSize );
		Mesh visualAsset = SaveMeshAsset( graph, index, "_Visual", visualMesh, Verts, Normals, Uvs, RailTris, SleeperTris, twoSubmeshes: true );
		ApplyVisual( go, visualAsset, railMat, sleeperMat );

		Mesh colliderMesh = BuildColliderMesh( go.transform, junction, gap, sleeperSize );
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

	static Mesh BuildVisualMesh( Transform root, MinecartJunction junction, float gap, float gauge, float railWidth, float railHeight, Vector3 sleeperSize )
	{
		Verts.Clear();
		Normals.Clear();
		Uvs.Clear();
		RailTris.Clear();
		SleeperTris.Clear();

		Vector3 up = Vector3.up;
		Vector3 padCenter = up * ( sleeperSize.y * 0.5f );
		float padExtent = Mathf.Max( gap * 2f, sleeperSize.x );
		AddBox( padCenter, Vector3.right, up, Vector3.forward, new Vector3( padExtent, sleeperSize.y, padExtent ), SleeperTris );

		for ( int p = 0; p < junction.ports.Count; p++ )
		{
			MinecartJunctionPort port = junction.ports[ p ];
			Vector3 tan = port.tangent;
			if ( tan.sqrMagnitude < 0.0001f )
				continue;

			tan.Normalize();
			Vector3 right = Vector3.Cross( up, tan );
			if ( right.sqrMagnitude < 0.0001f )
				right = Vector3.right;
			else
				right.Normalize();

			AppendRailStub( tan, right, up, gap, gauge, railWidth, railHeight, sleeperSize.y );
			AppendRailStub( -tan, -right, up, gap, gauge, railWidth, railHeight, sleeperSize.y );
		}

		Mesh mesh = new Mesh();
		mesh.name = root.name + "_Visual";
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

	static void AppendRailStub( Vector3 tan, Vector3 right, Vector3 up, float gap, float gauge, float railWidth, float railHeight, float sleeperTop )
	{
		float halfW = railWidth * 0.5f;
		float halfH = railHeight * 0.5f;
		float halfGauge = gauge * 0.5f;
		int samples = Mathf.Max( 2, RailSamples );

		int leftStart = Verts.Count;
		for ( int i = 0; i < samples; i++ )
		{
			float t = gap * i / ( samples - 1 );
			Vector3 along = tan * t;
			Vector3 center = along + right * -halfGauge + up * ( sleeperTop + halfH );
			AddRailRing( center, right, up, halfW, halfH );
		}

		int rightStart = Verts.Count;
		for ( int i = 0; i < samples; i++ )
		{
			float t = gap * i / ( samples - 1 );
			Vector3 along = tan * t;
			Vector3 center = along + right * halfGauge + up * ( sleeperTop + halfH );
			AddRailRing( center, right, up, halfW, halfH );
		}

		for ( int i = 0; i < samples - 1; i++ )
		{
			ConnectRailRings( leftStart + i * 4, leftStart + ( i + 1 ) * 4 );
			ConnectRailRings( rightStart + i * 4, rightStart + ( i + 1 ) * 4 );
		}
	}

	static Mesh BuildColliderMesh( Transform root, MinecartJunction junction, float gap, Vector3 sleeperSize )
	{
		ColliderVerts.Clear();
		ColliderTris.Clear();

		float halfW = Mathf.Max( sleeperSize.x * 0.5f, gap );
		float halfH = Mathf.Max( 0.04f, sleeperSize.y * 0.5f );
		float halfL = Mathf.Max( gap, sleeperSize.x * 0.5f );
		Vector3 center = Vector3.up * halfH;

		Vector3[] corners =
		{
			center + new Vector3( -halfW, -halfH, -halfL ),
			center + new Vector3( halfW, -halfH, -halfL ),
			center + new Vector3( halfW, halfH, -halfL ),
			center + new Vector3( -halfW, halfH, -halfL ),
			center + new Vector3( -halfW, -halfH, halfL ),
			center + new Vector3( halfW, -halfH, halfL ),
			center + new Vector3( halfW, halfH, halfL ),
			center + new Vector3( -halfW, halfH, halfL )
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
		mesh.name = root.name + "_Collider";
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
