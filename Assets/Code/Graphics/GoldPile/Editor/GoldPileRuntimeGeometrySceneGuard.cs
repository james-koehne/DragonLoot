#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps procedural gold-pile meshes / collider tiles out of scene YAML.
/// sceneSaving marks runtime geometry DontSave; menu strip destroys already-embedded legacy data.
/// </summary>
[InitializeOnLoad]
static class GoldPileRuntimeGeometrySceneGuard
{
	const string GoldPileVisualName = "GoldPileVisual";
	const string GoldPileColliderPrefix = "GoldPileCollider_";
	const string ColliderRootName = "GoldPileColliders";
	const string ColliderTilePrefix = "ColliderTile_";
	const HideFlags RuntimeHideFlags = HideFlags.HideAndDontSave;

	static GoldPileRuntimeGeometrySceneGuard()
	{
		EditorSceneManager.sceneSaving += OnSceneSaving;
	}

	[MenuItem( DragonLootMenus.GraphicsGoldPileStripRuntimeGeometry, priority = 220 )]
	static void StripFromOpenScenesMenu()
	{
		int stripped = 0;
		for ( int i = 0; i < SceneManager.sceneCount; i++ )
		{
			Scene scene = SceneManager.GetSceneAt( i );
			if ( !scene.IsValid() || !scene.isLoaded )
				continue;

			stripped += ProcessScene( scene, destroy: true );
			EditorSceneManager.MarkSceneDirty( scene );
		}

		Debug.Log(
			"GoldPileRuntimeGeometrySceneGuard: stripped "
			+ stripped
			+ " persisted procedural mesh/collider object(s) from open scenes. Save scenes to rewrite YAML." );
	}

	static void OnSceneSaving( Scene scene, string path )
	{
		if ( !scene.IsValid() || !scene.isLoaded )
			return;

		// Mark DontSave so Unity omits them from YAML without blanking the live preview.
		ProcessScene( scene, destroy: false );
	}

	static int ProcessScene( Scene scene, bool destroy )
	{
		int stripped = 0;
		GameObject[] roots = scene.GetRootGameObjects();
		for ( int r = 0; r < roots.Length; r++ )
			stripped += ProcessHierarchy( roots[ r ].transform, destroy );
		return stripped;
	}

	static int ProcessHierarchy( Transform root, bool destroy )
	{
		int stripped = 0;
		if ( root == null )
			return 0;

		if ( destroy )
		{
			GoldPileTerrainMesh terrain = root.GetComponent<GoldPileTerrainMesh>();
			if ( terrain != null )
			{
				terrain.StripPersistedRuntimeGeometry();
				stripped++;
			}
		}

		TreasurePileVisual pileVisual = root.GetComponent<TreasurePileVisual>();
		if ( pileVisual != null )
			stripped += StripLegacyPileMeshColliders( root );

		for ( int i = root.childCount - 1; i >= 0; i-- )
		{
			Transform child = root.GetChild( i );
			if ( child == null )
				continue;

			string name = child.name;
			bool colliderHierarchy =
				name == ColliderRootName
				|| name == "~" + ColliderRootName
				|| name.StartsWith( ColliderTilePrefix );

			if ( name == TreasurePileVisual.LatentBakePreviewRootName )
			{
				stripped += ProcessLatentBakePreview( child, destroy );
				continue;
			}

			if ( colliderHierarchy )
			{
				stripped += ProcessColliderHierarchy( child, destroy );
				continue;
			}

			stripped += ProcessHierarchy( child, destroy );
		}

		stripped += ProcessMeshFilter( root, destroy );
		stripped += ProcessMeshCollider( root, destroy );
		return stripped;
	}

	static int StripLegacyPileMeshColliders( Transform root )
	{
		int stripped = 0;
		MeshCollider[] cols = root.GetComponentsInChildren<MeshCollider>( true );
		for ( int i = 0; i < cols.Length; i++ )
		{
			MeshCollider col = cols[ i ];
			if ( col == null )
				continue;

			Transform t = col.transform;
			if ( t.name.StartsWith( ColliderTilePrefix )
				|| t.name == ColliderRootName
				|| t.name == "~" + ColliderRootName )
				continue;

			Object.DestroyImmediate( col );
			stripped++;
		}

		return stripped;
	}

	static int ProcessLatentBakePreview( Transform root, bool destroy )
	{
		if ( root == null )
			return 0;

		if ( destroy )
		{
			Object.DestroyImmediate( root.gameObject );
			return 1;
		}

		SetHideFlagsRecursive( root, RuntimeHideFlags );
		return 1;
	}

	static int ProcessColliderHierarchy( Transform root, bool destroy )
	{
		int stripped = 0;
		MeshCollider[] cols = root.GetComponentsInChildren<MeshCollider>( true );
		for ( int i = 0; i < cols.Length; i++ )
		{
			MeshCollider col = cols[ i ];
			if ( col == null || col.sharedMesh == null )
				continue;

			Mesh mesh = col.sharedMesh;
			if ( !IsProceduralGoldPileMesh( mesh ) )
				continue;

			if ( destroy )
			{
				col.sharedMesh = null;
				if ( IsSceneEmbeddedMesh( mesh ) )
					Object.DestroyImmediate( mesh );
				stripped++;
			}
			else
			{
				mesh.hideFlags = RuntimeHideFlags;
				stripped++;
			}
		}

		if ( destroy )
		{
			Object.DestroyImmediate( root.gameObject );
			stripped++;
		}
		else
		{
			SetHideFlagsRecursive( root, RuntimeHideFlags );
			stripped++;
		}

		return stripped;
	}

	static int ProcessMeshFilter( Transform root, bool destroy )
	{
		MeshFilter filter = root.GetComponent<MeshFilter>();
		if ( filter == null || filter.sharedMesh == null )
			return 0;

		Mesh mesh = filter.sharedMesh;
		if ( !IsProceduralGoldPileMesh( mesh ) )
			return 0;

		if ( destroy )
		{
			filter.sharedMesh = null;
			if ( IsSceneEmbeddedMesh( mesh ) )
				Object.DestroyImmediate( mesh );
		}
		else
			mesh.hideFlags = RuntimeHideFlags;

		return 1;
	}

	static int ProcessMeshCollider( Transform root, bool destroy )
	{
		MeshCollider col = root.GetComponent<MeshCollider>();
		if ( col == null || col.sharedMesh == null )
			return 0;

		Mesh mesh = col.sharedMesh;
		if ( !IsProceduralGoldPileMesh( mesh ) )
			return 0;

		if ( destroy )
		{
			col.sharedMesh = null;
			if ( IsSceneEmbeddedMesh( mesh ) )
				Object.DestroyImmediate( mesh );
		}
		else
			mesh.hideFlags = RuntimeHideFlags;

		return 1;
	}

	static void SetHideFlagsRecursive( Transform root, HideFlags flags )
	{
		root.gameObject.hideFlags = flags;
		for ( int i = 0; i < root.childCount; i++ )
			SetHideFlagsRecursive( root.GetChild( i ), flags );
	}

	static bool IsProceduralGoldPileMesh( Mesh mesh )
	{
		if ( mesh == null )
			return false;

		string name = mesh.name;
		return name == GoldPileVisualName
			|| name.StartsWith( GoldPileColliderPrefix );
	}

	static bool IsSceneEmbeddedMesh( Mesh mesh )
	{
		if ( mesh == null )
			return false;

		string path = AssetDatabase.GetAssetPath( mesh );
		return string.IsNullOrEmpty( path );
	}
}
#endif
