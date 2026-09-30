#if UNITY_EDITOR
using UnityEditor;

using UnityEngine;

/// <summary>
/// Creates world and held hammer prefabs for build mode.
/// </summary>
public static class WorldHammerPrefabMenu
{
	const string PrefabFolder = "Assets/Prefabs/Build";
	public const string WorldHammerPath = "Assets/Prefabs/Build/WorldHammer.prefab";
	public const string HeldHammerPath = "Assets/Prefabs/Build/HeldHammer.prefab";

	[MenuItem( DragonLootMenus.BuildCreateWorldHammerPrefab )]
	static void CreateWorldHammerPrefab()
	{
		EnsureWorldHammer();
		Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>( WorldHammerPath );
		Debug.Log( "Created WorldHammer prefab at " + WorldHammerPath );
	}

	[MenuItem( DragonLootMenus.BuildCreateHeldHammerPrefab )]
	static void CreateHeldHammerPrefabMenu()
	{
		EnsureHeldHammer();
		Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>( HeldHammerPath );
		Debug.Log( "Created HeldHammer prefab at " + HeldHammerPath );
	}

	public static GameObject EnsureWorldHammer()
	{
		AddressableEditorUtil.EnsureFolder( PrefabFolder );

		GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>( WorldHammerPath );
		if ( existing != null )
		{
			AddressableEditorUtil.TryRegister( WorldHammerPath, WorldHammerPath );
			return existing;
		}

		GameObject root = new GameObject( "WorldHammer" );
		try
		{
			BuildHammerVisual( root.transform );
			root.AddComponent<WorldHammerInteractable>();

			CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
			capsule.center = new Vector3( 0f, 0.17f, 0f );
			capsule.radius = 0.2f;
			capsule.height = 0.6f;

			GameObject prefab = PrefabUtility.SaveAsPrefabAsset( root, WorldHammerPath );
			AddressableEditorUtil.TryRegister( WorldHammerPath, WorldHammerPath );
			return prefab;
		}
		finally
		{
			Object.DestroyImmediate( root );
		}
	}

	/// <summary>
	/// Viewmodel hammer: same visual as WorldHammer, no interactable / colliders.
	/// Prefers cloning WorldHammer when present so art stays in sync.
	/// </summary>
	public static GameObject EnsureHeldHammer()
	{
		AddressableEditorUtil.EnsureFolder( PrefabFolder );

		GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>( HeldHammerPath );
		if ( existing != null )
		{
			AddressableEditorUtil.TryRegister( HeldHammerPath, HeldHammerPath );
			return existing;
		}

		GameObject world = AssetDatabase.LoadAssetAtPath<GameObject>( WorldHammerPath );
		if ( world != null )
		{
			GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab( world );
			try
			{
				instance.name = "HeldHammer";
				StripWorldComponents( instance );
				GameObject prefab = PrefabUtility.SaveAsPrefabAsset( instance, HeldHammerPath );
				AddressableEditorUtil.TryRegister( HeldHammerPath, HeldHammerPath );
				return prefab;
			}
			finally
			{
				Object.DestroyImmediate( instance );
			}
		}

		GameObject root = new GameObject( "HeldHammer" );
		try
		{
			BuildHammerVisual( root.transform );
			GameObject prefab = PrefabUtility.SaveAsPrefabAsset( root, HeldHammerPath );
			AddressableEditorUtil.TryRegister( HeldHammerPath, HeldHammerPath );
			return prefab;
		}
		finally
		{
			Object.DestroyImmediate( root );
		}
	}

	static void StripWorldComponents( GameObject root )
	{
		WorldHammerInteractable interactable = root.GetComponent<WorldHammerInteractable>();
		if ( interactable != null )
			Object.DestroyImmediate( interactable );

		Collider[] colliders = root.GetComponentsInChildren<Collider>( true );
		for ( int i = 0; i < colliders.Length; i++ )
		{
			if ( colliders[ i ] != null )
				Object.DestroyImmediate( colliders[ i ] );
		}
	}

	static void BuildHammerVisual( Transform parent )
	{
		GameObject handle = GameObject.CreatePrimitive( PrimitiveType.Cylinder );
		handle.name = "Handle";
		handle.transform.SetParent( parent, false );
		handle.transform.localPosition = new Vector3( 0f, 0.35f, 0f );
		handle.transform.localScale = new Vector3( 0.06f, 0.35f, 0.06f );
		Object.DestroyImmediate( handle.GetComponent<Collider>() );

		GameObject head = GameObject.CreatePrimitive( PrimitiveType.Cube );
		head.name = "Head";
		head.transform.SetParent( parent, false );
		head.transform.localPosition = new Vector3( 0f, 0.72f, 0f );
		head.transform.localScale = new Vector3( 0.28f, 0.12f, 0.12f );
		Object.DestroyImmediate( head.GetComponent<Collider>() );
	}
}
#endif
