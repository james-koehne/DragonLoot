#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds Cleaning Station prefab and places one in Level when that scene is open.
/// </summary>
[InitializeOnLoad]
static class CleaningStationSetup
{
	const string PrefabFolder = "Assets/Addressables/Tables";
	const string PrefabPath = PrefabFolder + "/CleaningStation.prefab";
	const string LevelPath = "Assets/Scenes/Level.unity";

	static readonly Vector3 LevelSpawnPosition = new Vector3( 6f, 0f, 4f );

	static CleaningStationSetup()
	{
		EditorApplication.delayCall += EnsureInstalled;
	}

	[MenuItem( DragonLootMenus.StationsCleaningCreate )]
	static void MenuCreate()
	{
		ArtifactMaterialInstaller.TryInstall( forceAssignPrefabs: true );
		GameObject prefab = EnsurePrefab();
		PlaceInActiveScene( prefab );
	}

	[MenuItem( DragonLootMenus.StationsCleaningPlace )]
	static void MenuPlaceInLevel()
	{
		GameObject prefab = EnsurePrefab();
		Scene levelScene = EditorSceneManager.OpenScene( LevelPath, OpenSceneMode.Single );
		EnsureStationInScene( prefab, levelScene, save: true );
	}

	static void EnsureInstalled()
	{
		if ( EditorApplication.isPlayingOrWillChangePlaymode )
			return;

		ArtifactMaterialInstaller.TryInstall( forceAssignPrefabs: false );
		GameObject prefab = EnsurePrefab();
		if ( prefab == null )
			return;

		for ( int i = 0; i < SceneManager.sceneCount; i++ )
		{
			Scene open = SceneManager.GetSceneAt( i );
			if ( open.path == LevelPath && open.isLoaded )
			{
				EnsureStationInScene( prefab, open, save: true );
				return;
			}
		}
	}

	static GameObject EnsurePrefab()
	{
		EnsureFolder( "Assets/Addressables" );
		EnsureFolder( PrefabFolder );

		GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>( PrefabPath );
		if ( existing != null )
		{
			RegisterAddressable( PrefabPath, "Tables/CleaningStation" );
			return existing;
		}

		GameObject root = new GameObject( "CleaningStation" );

		BoxCollider placeCollider = root.AddComponent<BoxCollider>();
		placeCollider.center = new Vector3( 0f, 0.35f, 0f );
		placeCollider.size = new Vector3( 2.4f, 0.7f, 0.9f );

		CleaningStationInteractable station = root.AddComponent<CleaningStationInteractable>();

		GameObject belt = GameObject.CreatePrimitive( PrimitiveType.Cube );
		belt.name = "BeltVisual";
		belt.transform.SetParent( root.transform, false );
		belt.transform.localPosition = new Vector3( 0f, 0.25f, 0f );
		belt.transform.localScale = new Vector3( 2.2f, 0.15f, 0.7f );
		Object.DestroyImmediate( belt.GetComponent<Collider>() );

		GameObject machine = GameObject.CreatePrimitive( PrimitiveType.Cube );
		machine.name = "MachineVisual";
		machine.transform.SetParent( root.transform, false );
		machine.transform.localPosition = new Vector3( 0f, 0.7f, 0f );
		machine.transform.localScale = new Vector3( 0.7f, 0.8f, 0.85f );
		Object.DestroyImmediate( machine.GetComponent<Collider>() );

		Transform intake = CreateSocket( root.transform, "IntakeSocket", new Vector3( -0.9f, 0.45f, 0f ) );
		Transform cleaner = CreateSocket( root.transform, "CleanerSocket", new Vector3( 0f, 0.45f, 0f ) );
		Transform exit = CreateSocket( root.transform, "ExitSocket", new Vector3( 0.9f, 0.45f, 0f ) );

		GameObject intakeVolumeGo = new GameObject( "IntakeVolume" );
		intakeVolumeGo.transform.SetParent( root.transform, false );
		intakeVolumeGo.transform.localPosition = new Vector3( -0.9f, 0.45f, 0f );
		BoxCollider intakeVolume = intakeVolumeGo.AddComponent<BoxCollider>();
		intakeVolume.isTrigger = true;
		intakeVolume.size = new Vector3( 0.7f, 0.6f, 0.7f );

		SerializedObject so = new SerializedObject( station );
		SerializedProperty intakeProp = so.FindProperty( "intakeSocket" );
		SerializedProperty cleanerProp = so.FindProperty( "cleanerSocket" );
		SerializedProperty exitProp = so.FindProperty( "exitSocket" );
		SerializedProperty volumeProp = so.FindProperty( "intakeVolume" );
		if ( intakeProp != null )
			intakeProp.objectReferenceValue = intake;
		if ( cleanerProp != null )
			cleanerProp.objectReferenceValue = cleaner;
		if ( exitProp != null )
			exitProp.objectReferenceValue = exit;
		if ( volumeProp != null )
			volumeProp.objectReferenceValue = intakeVolume;
		so.ApplyModifiedPropertiesWithoutUndo();

		GameObject prefab = PrefabUtility.SaveAsPrefabAsset( root, PrefabPath );
		Object.DestroyImmediate( root );
		RegisterAddressable( PrefabPath, "Tables/CleaningStation" );
		return prefab;
	}

	static Transform CreateSocket( Transform parent, string name, Vector3 localPos )
	{
		GameObject go = new GameObject( name );
		go.transform.SetParent( parent, false );
		go.transform.localPosition = localPos;
		go.transform.localRotation = Quaternion.identity;
		return go.transform;
	}

	static void PlaceInActiveScene( GameObject prefab )
	{
		if ( prefab == null )
			return;

		Scene scene = SceneManager.GetActiveScene();
		if ( !scene.IsValid() || !scene.isLoaded )
			return;

		EnsureStationInScene( prefab, scene, save: true );
	}

	static void EnsureStationInScene( GameObject prefab, Scene scene, bool save )
	{
		if ( prefab == null || !scene.IsValid() )
			return;

		CleaningStationInteractable[] existing = Object.FindObjectsByType<CleaningStationInteractable>(
			FindObjectsInactive.Include,
			FindObjectsSortMode.None );
		for ( int i = 0; i < existing.Length; i++ )
		{
			if ( existing[ i ] != null && existing[ i ].gameObject.scene == scene )
				return;
		}

		GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab( prefab, scene );
		instance.name = "CleaningStation";
		instance.transform.position = LevelSpawnPosition;
		instance.transform.rotation = Quaternion.identity;
		Undo.RegisterCreatedObjectUndo( instance, "Place Cleaning Station" );
		EditorSceneManager.MarkSceneDirty( scene );
		if ( save )
			EditorSceneManager.SaveScene( scene );
	}

	static void RegisterAddressable( string assetPath, string address )
	{
		AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
		if ( settings == null )
			return;

		string guid = AssetDatabase.AssetPathToGUID( assetPath );
		if ( string.IsNullOrEmpty( guid ) )
			return;

		AddressableAssetEntry entry = settings.FindAssetEntry( guid );
		if ( entry == null )
			entry = settings.CreateOrMoveEntry( guid, settings.DefaultGroup, readOnly: false, postEvent: false );

		entry.SetAddress( address );
		settings.SetDirty( AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true );
	}

	static void EnsureFolder( string path )
	{
		if ( AssetDatabase.IsValidFolder( path ) )
			return;

		string parent = System.IO.Path.GetDirectoryName( path )?.Replace( '\\', '/' );
		string leaf = System.IO.Path.GetFileName( path );
		if ( string.IsNullOrEmpty( parent ) || string.IsNullOrEmpty( leaf ) )
			return;
		if ( !AssetDatabase.IsValidFolder( parent ) )
			EnsureFolder( parent );
		AssetDatabase.CreateFolder( parent, leaf );
	}
}
#endif
