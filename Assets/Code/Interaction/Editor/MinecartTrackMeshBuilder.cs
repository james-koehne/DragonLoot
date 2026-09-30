#if UNITY_EDITOR
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
		MinecartJunctionGraph.BeginForceAllGaps();
		try
		{
			RebuildInternal( track );
		}
		finally
		{
			MinecartJunctionGraph.EndForceAllGaps();
			Rebuilding = false;
		}

		MinecartJunctionGraph graph = MinecartJunctionGraph.FindActive();
		if ( graph != null )
			graph.RefreshBuildVisuals();
	}

	public static void RebuildAllWithJunctionGaps()
	{
		MinecartJunctionGraph.BeginForceAllGaps();
		try
		{
			MinecartTrack[] found = Object.FindObjectsByType<MinecartTrack>( FindObjectsInactive.Exclude, FindObjectsSortMode.None );
			for ( int i = 0; i < found.Length; i++ )
			{
				MinecartTrack track = found[ i ];
				if ( track == null || !track.IsUsable )
					continue;

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
		}
		finally
		{
			MinecartJunctionGraph.EndForceAllGaps();
		}

		MinecartJunctionGraph graph = MinecartJunctionGraph.FindActive();
		if ( graph != null )
			graph.RefreshBuildVisuals();
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

		Mesh meshData = MinecartTrackMeshRuntime.BuildMesh( track );
		if ( meshData == null )
			return;

		Mesh asset = SaveMeshAsset( track, meshData );
		ApplyVisual( track, asset, railMat, sleeperMat );
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
			asset.vertices = meshData.vertices;
			asset.normals = meshData.normals;
			asset.uv = meshData.uv;
			asset.subMeshCount = meshData.subMeshCount;
			for ( int s = 0; s < meshData.subMeshCount; s++ )
				asset.SetTriangles( meshData.GetTriangles( s ), s );
			asset.RecalculateBounds();
			asset.RecalculateNormals();
			EditorUtility.SetDirty( asset );
			Object.DestroyImmediate( meshData );
		}

		track.EditorSetBakedMesh( asset );
		track.RestoreBakedVisualMesh();
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
