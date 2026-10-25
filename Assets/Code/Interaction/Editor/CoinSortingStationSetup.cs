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
			definition.baseHopperCapacity = 50;
			definition.largeHopperCapacity = 150;
			definition.manualCoinsPerSecond = 4f;
			definition.automaticCoinsPerSecond = 2f;
			definition.speedLevel1Multiplier = 1.25f;
			definition.speedLevel2Multiplier = 1.5f;
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
			definition.speedLevel1Multiplier = Mathf.Max( 1f, definition.speedLevel1Multiplier );
			definition.speedLevel2Multiplier = Mathf.Max( 1f, definition.speedLevel2Multiplier );
			if ( definition.storagePullSources == ( CoinSortingStoragePullSources.GroundStacks | CoinSortingStoragePullSources.MixedStorage ) )
				definition.storagePullSources = CoinSortingStoragePullSources.StationMixedStorage;
			EditorUtility.SetDirty( definition );
		}

		EnsureSplitCoinSorterUpgrades();

		const string catalogPath = "Assets/Definitions/UpgradeCatalogDefinition.asset";
		RegisterAddressable( defPath, "CoinSortingStationDefinition" );
		RegisterAddressable( catalogPath, "UpgradeCatalogDefinition" );
		AssetDatabase.SaveAssets();
	}

	static void EnsureSplitCoinSorterUpgrades()
	{
		const string folder = "Assets/Definitions/Upgrades";
		UpgradeDefinition automatic = EnsureUpgradeAsset(
			folder + "/Upgrade_CoinSorterAutomatic.asset",
			UpgradeDefinition.CoinSorterAutomaticId,
			"Automatic Sorting",
			"Sorts coins unattended. Crank boosts speed.",
			1,
			new[] { "Automatic Sorting" } );
		UpgradeDefinition speed = EnsureUpgradeAsset(
			folder + "/Upgrade_CoinSorterSpeed.asset",
			UpgradeDefinition.CoinSorterSpeedId,
			"Coin Sorter Speed",
			"Faster coin sorting.",
			2,
			new[] { "Coin Sorter +25% Speed", "Coin Sorter +50% Speed" } );
		UpgradeDefinition hopper = EnsureUpgradeAsset(
			folder + "/Upgrade_CoinSorterHopper.asset",
			UpgradeDefinition.CoinSorterHopperId,
			"Larger Hopper",
			"Holds more coins in the sorter hopper.",
			1,
			new[] { "Larger Hopper" } );

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

		RegisterUpgrade( catalog, automatic );
		RegisterUpgrade( catalog, speed );
		RegisterUpgrade( catalog, hopper );

		UpgradeDefinition legacy = AssetDatabase.LoadAssetAtPath<UpgradeDefinition>( folder + "/Upgrade_CoinSortingStation.asset" );
		if ( legacy != null )
		{
			for ( int i = catalog.upgrades.Count - 1; i >= 0; i-- )
			{
				UpgradeDefinition entry = catalog.upgrades[ i ];
				if ( entry == legacy || ( entry != null && entry.id == UpgradeDefinition.LegacyCoinSortingStationId ) )
					catalog.upgrades.RemoveAt( i );
			}

			legacy.enabled = false;
			EditorUtility.SetDirty( legacy );
		}

		EditorUtility.SetDirty( catalog );
	}

	static UpgradeDefinition EnsureUpgradeAsset( string path, string id, string displayName, string description, int maxLevel, string[] labels )
	{
		UpgradeDefinition upgrade = AssetDatabase.LoadAssetAtPath<UpgradeDefinition>( path );
		if ( upgrade == null )
		{
			upgrade = ScriptableObject.CreateInstance<UpgradeDefinition>();
			AssetDatabase.CreateAsset( upgrade, path );
		}

		upgrade.id = id;
		upgrade.displayName = displayName;
		upgrade.description = description;
		upgrade.maxLevel = maxLevel;
		upgrade.enabled = true;
		upgrade.levelUnlockLabels = labels;
		EditorUtility.SetDirty( upgrade );
		return upgrade;
	}

	static void RegisterUpgrade( UpgradeCatalogDefinition catalog, UpgradeDefinition upgrade )
	{
		if ( upgrade == null )
			return;
		for ( int i = 0; i < catalog.upgrades.Count; i++ )
		{
			UpgradeDefinition entry = catalog.upgrades[ i ];
			if ( entry == upgrade || ( entry != null && entry.id == upgrade.id ) )
				return;
		}

		catalog.upgrades.Add( upgrade );
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
