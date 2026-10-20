#if UNITY_EDITOR
using System.Collections.Generic;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Seeds Coin Sorting Station definition assets and places the existing prefab.
/// Prefab hierarchy is authored via MCP / hand edit — this does not rebuild it.
/// </summary>
public static class CoinSortingStationSetup
{
	const string PrefabFolder = "Assets/Prefabs/Interaction";
	const string PrefabPath = PrefabFolder + "/CoinSortingStation.prefab";
	const string LevelPath = "Assets/Scenes/Level.unity";

	static readonly Vector3 LevelSpawnPosition = new Vector3( -8f, 0f, 3f );

	[MenuItem( DragonLootMenus.StationsCoinSortingCreate )]
	static void MenuCreate()
	{
		EnsureDefinitionAssets();
		GameObject prefab = LoadPrefab();
		PlaceInActiveScene( prefab );
	}

	/// <summary>Batchmode entry: ensures definition assets and places existing prefab into Level.</summary>
	public static void BatchEnsureInstalled()
	{
		try
		{
			EnsureDefinitionAssets();
			GameObject prefab = LoadPrefab();
			if ( prefab == null )
				throw new System.InvalidOperationException( "Missing prefab at " + PrefabPath );

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

	/// <summary>Batchmode entry: verifies the CoinSortingStation prefab exists (no hierarchy rewrite).</summary>
	public static void BatchConfigurePrefab()
	{
		if ( LoadPrefab() == null )
			Debug.LogError( "CoinSortingStationSetup.BatchConfigurePrefab: missing prefab at " + PrefabPath );
		else
			Debug.Log( "CoinSortingStationSetup.BatchConfigurePrefab: prefab present at " + PrefabPath );
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
			definition.manualCoinsPerSecond = 4f;
			definition.automaticCoinsPerSecond = 2f;
			definition.level3ManualCoinsPerSecond = 10f;
			definition.level3AutomaticCoinsPerSecond = 5f;
			definition.crankHoldGrace = 0.2f;
			definition.fullStackLateralOffset = 0.35f;
			definition.autoPullFromNearbyStorage = true;
			definition.storagePullSources = CoinSortingStoragePullSources.StationMixedStorage;
			definition.storagePullRadius = 3f;
			definition.storagePullInterval = 0.35f;
			definition.storagePullFlightDuration = 0.32f;
			AssetDatabase.CreateAsset( definition, defPath );
		}
		else
		{
			definition.automaticCoinsPerSecond = Mathf.Max( 0.01f, definition.automaticCoinsPerSecond );
			definition.level3AutomaticCoinsPerSecond = Mathf.Max( 0.01f, definition.level3AutomaticCoinsPerSecond );
			if ( definition.storagePullSources == ( CoinSortingStoragePullSources.GroundStacks | CoinSortingStoragePullSources.MixedStorage ) )
				definition.storagePullSources = CoinSortingStoragePullSources.StationMixedStorage;
			EditorUtility.SetDirty( definition );
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
		upgrade.description = "Levels: 1 automatic sorting (crank boosts speed), 3 faster processing, 4 larger hopper.";
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

	[MenuItem( DragonLootMenus.StationsCoinSortingPlace )]
	static void MenuPlaceInLevel()
	{
		EnsureDefinitionAssets();
		GameObject prefab = LoadPrefab();
		if ( prefab == null )
			return;

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
			FindObjectsInactive.Include );
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

	static GameObject LoadPrefab()
	{
		GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>( PrefabPath );
		if ( prefab == null )
			Debug.LogWarning( "CoinSortingStationSetup: missing prefab at " + PrefabPath + " — author via MCP." );
		return prefab;
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
