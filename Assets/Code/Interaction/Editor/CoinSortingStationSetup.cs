#if UNITY_EDITOR
using System.Collections.Generic;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds a placeholder Coin Sorting Station prefab and ensures one exists in Level.
/// </summary>
[InitializeOnLoad]
static class CoinSortingStationSetup
{
	const string PrefabFolder = "Assets/Prefabs/Interaction";
	const string PrefabPath = PrefabFolder + "/CoinSortingStation.prefab";
	const string LevelPath = "Assets/Scenes/Level.unity";
	const string GoldCoinPath = "Assets/Definitions/Treasure/Coins/GoldCoin.asset";
	const string SilverCoinPath = "Assets/Definitions/Treasure/Coins/SilverCoin.asset";
	const string CopperCoinPath = "Assets/Definitions/Treasure/Coins/CopperCoin.asset";

	static readonly Vector3 LevelSpawnPosition = new Vector3( -8f, 0f, 3f );

	static CoinSortingStationSetup()
	{
		EditorApplication.delayCall += EnsureInstalled;
	}

	[MenuItem( DragonLootMenus.StationsCoinSortingCreate )]
	static void MenuCreate()
	{
		GameObject prefab = EnsurePrefab();
		PlaceInActiveScene( prefab );
	}

	/// <summary>Batchmode entry: creates definition assets, prefab, and places into Level.</summary>
	public static void BatchEnsureInstalled()
	{
		try
		{
			EnsureDefinitionAssets();
			GameObject prefab = EnsurePrefab();
			Scene levelScene = EditorSceneManager.OpenScene( LevelPath, OpenSceneMode.Single );
			EnsureStationInScene( prefab, levelScene, save: true );
			AssetDatabase.SaveAssets();
			Debug.Log( "CoinSortingStationSetup.BatchEnsureInstalled completed." );
		}
		catch ( System.Exception ex )
		{
			Debug.LogError( "CoinSortingStationSetup.BatchEnsureInstalled failed: " + ex );
			EditorApplication.Exit( 1 );
			return;
		}

		EditorApplication.Exit( 0 );
	}

	static void EnsureDefinitionAssets()
	{
		EnsureFolder( "Assets/Definitions" );
		EnsureFolder( "Assets/Definitions/Upgrades" );

		const string defPath = "Assets/Definitions/CoinSortingStationDefinition.asset";
		CoinSortingStationDefinition definition = AssetDatabase.LoadAssetAtPath<CoinSortingStationDefinition>( defPath );
		if ( definition == null )
		{
			definition = ScriptableObject.CreateInstance<CoinSortingStationDefinition>();
			definition.name = "CoinSortingStationDefinition";
			definition.upgradeId = CoinSortingStationDefinition.DefaultUpgradeId;
			definition.baseHopperCapacity = 50;
			definition.level4HopperCapacity = 150;
			definition.baseCoinsPerSecond = 4f;
			definition.level3CoinsPerSecond = 10f;
			definition.crankHoldGrace = 0.35f;
			definition.fullStackLateralOffset = 0.35f;
			AssetDatabase.CreateAsset( definition, defPath );
		}

		const string upgradePath = "Assets/Definitions/Upgrades/Upgrade_CoinSortingStation.asset";
		UpgradeDefinition upgrade = AssetDatabase.LoadAssetAtPath<UpgradeDefinition>( upgradePath );
		if ( upgrade == null )
		{
			upgrade = ScriptableObject.CreateInstance<UpgradeDefinition>();
			upgrade.name = "Upgrade_CoinSortingStation";
			AssetDatabase.CreateAsset( upgrade, upgradePath );
		}

		upgrade.id = CoinSortingStationDefinition.DefaultUpgradeId;
		upgrade.displayName = "Coin Sorting Station";
		upgrade.description = "Levels: 1 manual crank, 2 automatic, 3 faster processing, 4 larger hopper.";
		upgrade.maxLevel = 4;
		upgrade.enabled = true;
		EditorUtility.SetDirty( upgrade );

		const string catalogPath = "Assets/Definitions/UpgradeCatalogDefinition.asset";
		UpgradeCatalogDefinition catalog = AssetDatabase.LoadAssetAtPath<UpgradeCatalogDefinition>( catalogPath );
		if ( catalog == null )
		{
			catalog = ScriptableObject.CreateInstance<UpgradeCatalogDefinition>();
			catalog.name = "UpgradeCatalogDefinition";
			AssetDatabase.CreateAsset( catalog, catalogPath );
		}

		if ( catalog.upgrades == null )
			catalog.upgrades = new List<UpgradeDefinition>();
		if ( !catalog.upgrades.Contains( upgrade ) )
			catalog.upgrades.Add( upgrade );
		EditorUtility.SetDirty( catalog );

		RegisterAddressable( defPath, "CoinSortingStationDefinition" );
		RegisterAddressable( catalogPath, "UpgradeCatalogDefinition" );
		AssetDatabase.SaveAssets();
	}

	static void RegisterAddressable( string assetPath, string address )
	{
		UnityEditor.AddressableAssets.Settings.AddressableAssetSettings settings =
			UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
		if ( settings == null )
			return;

		string guid = AssetDatabase.AssetPathToGUID( assetPath );
		if ( string.IsNullOrEmpty( guid ) )
			return;

		UnityEditor.AddressableAssets.Settings.AddressableAssetEntry entry = settings.FindAssetEntry( guid );
		if ( entry == null )
			entry = settings.CreateOrMoveEntry( guid, settings.DefaultGroup );

		entry.SetAddress( address );
		if ( !entry.labels.Contains( "Definition" ) )
			entry.SetLabel( "Definition", true, true );
		settings.SetDirty(
			UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.ModificationEvent.EntryModified,
			entry,
			true );
	}

	static void EnsureInstalled()
	{
		GameObject prefab = EnsurePrefab();
		if ( prefab == null )
			return;

		// Only auto-place when Level is already open — avoid opening/saving on every editor load.
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

	[MenuItem( DragonLootMenus.StationsCoinSortingPlace )]
	static void MenuPlaceInLevel()
	{
		GameObject prefab = EnsurePrefab();
		Scene levelScene = default;
		bool openedTemp = false;

		for ( int i = 0; i < SceneManager.sceneCount; i++ )
		{
			Scene open = SceneManager.GetSceneAt( i );
			if ( open.path == LevelPath )
			{
				levelScene = open;
				break;
			}
		}

		if ( !levelScene.IsValid() || !levelScene.isLoaded )
		{
			levelScene = EditorSceneManager.OpenScene( LevelPath, OpenSceneMode.Additive );
			openedTemp = true;
		}

		EnsureStationInScene( prefab, levelScene, save: true );

		if ( openedTemp && levelScene.IsValid() )
			EditorSceneManager.CloseScene( levelScene, true );
	}

	static void EnsureStationInScene( GameObject prefab, Scene scene, bool save )
	{
		if ( prefab == null || !scene.IsValid() || !scene.isLoaded )
			return;

		CoinSortingStation[] existing = Object.FindObjectsByType<CoinSortingStation>(
			FindObjectsInactive.Include,
			FindObjectsSortMode.None );
		for ( int i = 0; i < existing.Length; i++ )
		{
			if ( existing[ i ] != null && existing[ i ].gameObject.scene == scene )
				return;
		}

		GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab( prefab, scene );
		instance.transform.position = LevelSpawnPosition;
		instance.transform.rotation = Quaternion.identity;
		Undo.RegisterCreatedObjectUndo( instance, "Place Coin Sorting Station" );
		EditorSceneManager.MarkSceneDirty( scene );
		if ( save )
			EditorSceneManager.SaveScene( scene );
	}

	static GameObject EnsurePrefab()
	{
		EnsureFolder( "Assets/Prefabs" );
		EnsureFolder( PrefabFolder );

		GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>( PrefabPath );
		if ( existing != null )
			return existing;

		GameObject root = BuildHierarchy();
		GameObject prefab = PrefabUtility.SaveAsPrefabAsset( root, PrefabPath );
		Object.DestroyImmediate( root );
		AssetDatabase.SaveAssets();
		return prefab;
	}

	static GameObject BuildHierarchy()
	{
		GameObject root = new GameObject( "CoinSortingStation" );
		CoinSortingStation station = root.AddComponent<CoinSortingStation>();

		// Body placeholder
		GameObject body = GameObject.CreatePrimitive( PrimitiveType.Cube );
		body.name = "Body";
		body.transform.SetParent( root.transform, false );
		body.transform.localPosition = new Vector3( 0f, 0.6f, 0f );
		body.transform.localScale = new Vector3( 1.6f, 1.2f, 1.2f );
		// Keep the body BoxCollider so placement rays can hit the whole station (maps to hopper).

		// Hopper (placement box on top)
		GameObject hopperGo = new GameObject( "Hopper" );
		hopperGo.transform.SetParent( root.transform, false );
		hopperGo.transform.localPosition = new Vector3( 0f, 1.35f, 0f );
		BoxCollider hopperCol = hopperGo.AddComponent<BoxCollider>();
		hopperCol.isTrigger = false;
		hopperCol.size = new Vector3( 1.2f, 0.6f, 1.0f );
		// No Rigidbody required; FixedUpdate overlap scan absorbs nearby coins.
		CoinSortingHopper hopper = hopperGo.AddComponent<CoinSortingHopper>();

		GameObject hopperVis = GameObject.CreatePrimitive( PrimitiveType.Cube );
		hopperVis.name = "HopperVisual";
		hopperVis.transform.SetParent( hopperGo.transform, false );
		hopperVis.transform.localScale = new Vector3( 1.2f, 0.15f, 1.0f );
		Object.DestroyImmediate( hopperVis.GetComponent<Collider>() );

		// Crank
		GameObject crankGo = new GameObject( "Crank" );
		crankGo.transform.SetParent( root.transform, false );
		crankGo.transform.localPosition = new Vector3( 0.95f, 0.7f, 0f );
		SphereCollider crankCol = crankGo.AddComponent<SphereCollider>();
		crankCol.radius = 0.25f;
		CoinSortingCrankInteractable crank = crankGo.AddComponent<CoinSortingCrankInteractable>();

		GameObject crankVis = GameObject.CreatePrimitive( PrimitiveType.Sphere );
		crankVis.name = "CrankVisual";
		crankVis.transform.SetParent( crankGo.transform, false );
		crankVis.transform.localScale = Vector3.one * 0.35f;
		Object.DestroyImmediate( crankVis.GetComponent<Collider>() );

		// Chutes
		Transform goldChute = CreateChute( root.transform, "Chute_Gold", new Vector3( -0.5f, 0.05f, -0.9f ) );
		Transform silverChute = CreateChute( root.transform, "Chute_Silver", new Vector3( 0f, 0.05f, -0.9f ) );
		Transform copperChute = CreateChute( root.transform, "Chute_Copper", new Vector3( 0.5f, 0.05f, -0.9f ) );

		TreasureDefinition gold = AssetDatabase.LoadAssetAtPath<TreasureDefinition>( GoldCoinPath );
		TreasureDefinition silver = AssetDatabase.LoadAssetAtPath<TreasureDefinition>( SilverCoinPath );
		TreasureDefinition copper = AssetDatabase.LoadAssetAtPath<TreasureDefinition>( CopperCoinPath );

		var bindings = new List<CoinSortingStation.ChuteBinding>
		{
			new CoinSortingStation.ChuteBinding { coin = gold, chute = goldChute },
			new CoinSortingStation.ChuteBinding { coin = silver, chute = silverChute },
			new CoinSortingStation.ChuteBinding { coin = copper, chute = copperChute },
		};

		station.EditorSetHopper( hopper );
		station.EditorSetCrank( crank );
		station.EditorSetChutes( bindings );
		hopper.BindStation( station );
		crank.BindStation( station );

		return root;
	}

	static Transform CreateChute( Transform parent, string name, Vector3 localPos )
	{
		GameObject chute = new GameObject( name );
		chute.transform.SetParent( parent, false );
		chute.transform.localPosition = localPos;
		chute.transform.localRotation = Quaternion.identity;

		GameObject marker = GameObject.CreatePrimitive( PrimitiveType.Cylinder );
		marker.name = "Marker";
		marker.transform.SetParent( chute.transform, false );
		marker.transform.localScale = new Vector3( 0.2f, 0.02f, 0.2f );
		Object.DestroyImmediate( marker.GetComponent<Collider>() );
		return chute.transform;
	}

	static void PlaceInActiveScene( GameObject prefab )
	{
		if ( prefab == null )
			return;

		Scene scene = SceneManager.GetActiveScene();
		GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab( prefab, scene );
		instance.transform.position = LevelSpawnPosition;
		Selection.activeGameObject = instance;
		Undo.RegisterCreatedObjectUndo( instance, "Create Coin Sorting Station" );
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
