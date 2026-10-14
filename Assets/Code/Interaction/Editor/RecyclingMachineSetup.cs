#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;

/// <summary>
/// Seeds Recycling Machine definition/recipe assets and places the existing prefab.
/// Prefab hierarchy is authored via MCP / hand edit — this does not rebuild it.
/// </summary>
static class RecyclingMachineSetup
{
	const string PrefabFolder = "Assets/Prefabs/Interaction/Recycling";
	const string PrefabPath = PrefabFolder + "/RecyclingMachine.prefab";
	const string LevelPath = "Assets/Scenes/Level.unity";
	const string DefFolder = "Assets/Definitions";
	const string RecipeFolder = "Assets/Definitions/Recycling";
	const string JunkFolder = "Assets/Definitions/Treasure/Junk";
	const string VisualFolder = "Assets/Addressables/Treasure/Junk";

	const string MachineDefPath = DefFolder + "/RecyclingMachineDefinition.asset";
	const string CatalogPath = RecipeFolder + "/RecyclingRecipeCatalog.asset";
	const string UpgradePath = DefFolder + "/Upgrades/Upgrade_RecyclingMachine.asset";
	const string UpgradeCatalogPath = DefFolder + "/UpgradeCatalogDefinition.asset";

	const string WoodPath = "Assets/Definitions/Treasure/Resources/wood_01.asset";
	const string MetalPath = "Assets/Definitions/Treasure/Resources/metal_bar_01.asset";
	const string Crate01Path = "Assets/Definitions/Treasure/Chests/Crate_01.asset";
	const string Crate02Path = "Assets/Definitions/Treasure/Chests/Crate_02.asset";
	const string BarrelPath = "Assets/Definitions/Treasure/Chests/Barrel_01.asset";

	static readonly Vector3 LevelSpawnPosition = new Vector3( -11f, 0f, 3f );

	[MenuItem( DragonLootMenus.StationsRecyclingCreate )]
	static void MenuCreate()
	{
		EnsureDefinitionAssets();
		GameObject prefab = LoadPrefab();
		PlaceInActiveScene( prefab );
	}

	[MenuItem( DragonLootMenus.StationsRecyclingPlace )]
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

	[MenuItem( DragonLootMenus.DefinitionsEnsureRecyclingMachine )]
	static void MenuEnsureDefinitions()
	{
		EnsureDefinitionAssets();
		if ( LoadPrefab() == null )
			Debug.LogWarning( "RecyclingMachineSetup: prefab missing at " + PrefabPath + " — author via MCP." );
		AssetDatabase.SaveAssets();
		Debug.Log( "Recycling machine definitions ensured." );
	}

	static void EnsureDefinitionAssets()
	{
		EnsureFolder( DefFolder );
		EnsureFolder( DefFolder + "/Upgrades" );
		EnsureFolder( RecipeFolder );
		EnsureFolder( JunkFolder );
		EnsureFolder( VisualFolder );
		EnsureFolder( PrefabFolder );

		ConvertContainerToJunk( Crate01Path );
		ConvertContainerToJunk( Crate02Path );
		ConvertContainerToJunk( BarrelPath );

		TreasureDefinition wood = AssetDatabase.LoadAssetAtPath<TreasureDefinition>( WoodPath );
		TreasureDefinition metal = AssetDatabase.LoadAssetAtPath<TreasureDefinition>( MetalPath );
		TreasureDefinition crate01 = AssetDatabase.LoadAssetAtPath<TreasureDefinition>( Crate01Path );
		TreasureDefinition crate02 = AssetDatabase.LoadAssetAtPath<TreasureDefinition>( Crate02Path );
		TreasureDefinition barrel = AssetDatabase.LoadAssetAtPath<TreasureDefinition>( BarrelPath );
		TreasureDefinition furniture = EnsureJunkTreasure( "Furniture_01", "Broken Furniture", PrimitiveType.Cube, new Vector3( 0.7f, 0.55f, 0.45f ), new Color( 0.55f, 0.35f, 0.18f ) );
		TreasureDefinition scrap = EnsureJunkTreasure( "Scrap_01", "Scrap Metal", PrimitiveType.Cylinder, new Vector3( 0.35f, 0.15f, 0.35f ), new Color( 0.55f, 0.58f, 0.62f ) );

		RecyclingRecipeDefinition recipeCrate01 = EnsureRecipe( "Recipe_Crate_01", crate01, wood, 2, null, 0, chewWood: true, chewMetal: false );
		RecyclingRecipeDefinition recipeCrate02 = EnsureRecipe( "Recipe_Crate_02", crate02, wood, 2, null, 0, chewWood: true, chewMetal: false );
		RecyclingRecipeDefinition recipeBarrel = EnsureRecipe( "Recipe_Barrel_01", barrel, wood, 1, metal, 1, chewWood: true, chewMetal: true );
		RecyclingRecipeDefinition recipeFurniture = EnsureRecipe( "Recipe_Furniture_01", furniture, wood, 1, metal, 1, chewWood: true, chewMetal: true );
		RecyclingRecipeDefinition recipeScrap = EnsureRecipe( "Recipe_Scrap_01", scrap, null, 0, metal, 2, chewWood: false, chewMetal: true );

		RecyclingRecipeCatalog catalog = AssetDatabase.LoadAssetAtPath<RecyclingRecipeCatalog>( CatalogPath );
		if ( catalog == null )
		{
			catalog = ScriptableObject.CreateInstance<RecyclingRecipeCatalog>();
			catalog.name = "RecyclingRecipeCatalog";
			AssetDatabase.CreateAsset( catalog, CatalogPath );
		}

		catalog.recipes = new List<RecyclingRecipeDefinition>
		{
			recipeCrate01,
			recipeCrate02,
			recipeBarrel,
			recipeFurniture,
			recipeScrap
		};
		catalog.InvalidateCache();
		EditorUtility.SetDirty( catalog );

		RecyclingMachineDefinition machineDef = AssetDatabase.LoadAssetAtPath<RecyclingMachineDefinition>( MachineDefPath );
		if ( machineDef == null )
		{
			machineDef = ScriptableObject.CreateInstance<RecyclingMachineDefinition>();
			machineDef.name = "RecyclingMachineDefinition";
			AssetDatabase.CreateAsset( machineDef, MachineDefPath );
		}

		machineDef.upgradeId = RecyclingMachineDefinition.DefaultUpgradeId;
		machineDef.baseHopperCapacity = 4;
		machineDef.level4HopperCapacity = 8;
		machineDef.level5HopperCapacity = 12;
		machineDef.chargePerPull = 0.2f;
		machineDef.baseRuntimeSeconds = 4f;
		machineDef.level5RuntimeSeconds = 8f;
		machineDef.baseItemsPerCycle = 1;
		machineDef.level3ItemsPerCycle = 2;
		machineDef.baseChewInterval = 0.55f;
		machineDef.level3ChewInterval = 0.32f;
		machineDef.baseYieldMultiplier = 1f;
		machineDef.level4YieldMultiplier = 1.5f;
		machineDef.level5YieldMultiplier = 2f;
		EditorUtility.SetDirty( machineDef );

		UpgradeDefinition upgrade = AssetDatabase.LoadAssetAtPath<UpgradeDefinition>( UpgradePath );
		if ( upgrade == null )
		{
			upgrade = ScriptableObject.CreateInstance<UpgradeDefinition>();
			upgrade.name = "Upgrade_RecyclingMachine";
			AssetDatabase.CreateAsset( upgrade, UpgradePath );
		}

		upgrade.id = RecyclingMachineDefinition.DefaultUpgradeId;
		upgrade.displayName = "Recycling Machine";
		upgrade.description = "Levels: faster shred, more items/cycle, better yield, larger hopper, longer runtime.";
		upgrade.maxLevel = 5;
		upgrade.enabled = true;
		EditorUtility.SetDirty( upgrade );

		UpgradeCatalogDefinition upgradeCatalog = AssetDatabase.LoadAssetAtPath<UpgradeCatalogDefinition>( UpgradeCatalogPath );
		if ( upgradeCatalog == null )
		{
			upgradeCatalog = ScriptableObject.CreateInstance<UpgradeCatalogDefinition>();
			upgradeCatalog.name = "UpgradeCatalogDefinition";
			AssetDatabase.CreateAsset( upgradeCatalog, UpgradeCatalogPath );
		}

		if ( upgradeCatalog.upgrades == null )
			upgradeCatalog.upgrades = new List<UpgradeDefinition>();
		if ( !upgradeCatalog.upgrades.Contains( upgrade ) )
			upgradeCatalog.upgrades.Add( upgrade );
		EditorUtility.SetDirty( upgradeCatalog );

		EnsureBuildableDefinition();

		AddressableEditorUtil.TryRegister( MachineDefPath, "RecyclingMachineDefinition", "Definition" );
		AddressableEditorUtil.TryRegister( CatalogPath, "RecyclingRecipeCatalog", "Definition" );
		AddressableEditorUtil.TryRegister( UpgradeCatalogPath, "UpgradeCatalogDefinition", "Definition" );
		AssetDatabase.SaveAssets();
	}

	static void EnsureBuildableDefinition()
	{
		const string buildableFolder = "Assets/Definitions/Buildables";
		const string buildablePath = buildableFolder + "/Buildable_Recycling_Machine.asset";
		EnsureFolder( buildableFolder );

		BuildableDefinition buildable = AssetDatabase.LoadAssetAtPath<BuildableDefinition>( buildablePath );
		if ( buildable == null )
		{
			buildable = ScriptableObject.CreateInstance<BuildableDefinition>();
			AssetDatabase.CreateAsset( buildable, buildablePath );
		}

		buildable.id = "recycling_machine";
		buildable.displayName = "Recycling Machine";
		buildable.description = "Shred junk into wood and metal with a pull-chain starter.";
		buildable.cost = 0;
		EditorUtility.SetDirty( buildable );
	}

	static void ConvertContainerToJunk( string assetPath )
	{
		TreasureDefinition def = AssetDatabase.LoadAssetAtPath<TreasureDefinition>( assetPath );
		if ( def == null )
			return;

		def.category = TreasureCategory.Junk;
		def.chestDefinition = null;
		EditorUtility.SetDirty( def );
	}

	static TreasureDefinition EnsureJunkTreasure( string id, string displayName, PrimitiveType primitive, Vector3 scale, Color color )
	{
		string defPath = JunkFolder + "/" + id + ".asset";
		string visualPath = VisualFolder + "/" + id + "Visual.prefab";

		EnsureJunkVisualPrefab( visualPath, primitive, scale, color );

		TreasureDefinition def = AssetDatabase.LoadAssetAtPath<TreasureDefinition>( defPath );
		if ( def == null )
		{
			def = ScriptableObject.CreateInstance<TreasureDefinition>();
			AssetDatabase.CreateAsset( def, defPath );
		}

		def.id = id;
		def.displayName = displayName;
		def.category = TreasureCategory.Junk;
		def.value = 25;
		def.exclusiveCarry = false;
		def.usesHeavyThrow = true;
		def.chestDefinition = null;
		def.canStack = false;
		def.worldScale = Vector3.one;
		def.heldScale = Vector3.one * 0.4f;
		def.pickupRadius = 0.5f;
		def.rigidbodyMass = 1.2f;

		string guid = AssetDatabase.AssetPathToGUID( visualPath );
		if ( !string.IsNullOrEmpty( guid ) )
		{
			def.prefab = new AssetReferenceGameObject( guid );
			AddressableEditorUtil.TryRegister( visualPath, visualPath );
		}

		EditorUtility.SetDirty( def );
		AddressableEditorUtil.TryRegister( defPath, id, "Definition" );
		return def;
	}

	static void EnsureJunkVisualPrefab( string path, PrimitiveType primitive, Vector3 scale, Color color )
	{
		if ( File.Exists( path ) )
			return;

		GameObject root = new GameObject( Path.GetFileNameWithoutExtension( path ) );
		GameObject visual = GameObject.CreatePrimitive( primitive );
		visual.name = "Mesh";
		visual.transform.SetParent( root.transform, false );
		visual.transform.localScale = scale;
		Object.DestroyImmediate( visual.GetComponent<Collider>() );

		Renderer renderer = visual.GetComponent<Renderer>();
		if ( renderer != null )
		{
			Material mat = new Material( Shader.Find( "Universal Render Pipeline/Lit" ) );
			if ( mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader" )
				mat = new Material( Shader.Find( "Standard" ) );
			mat.color = color;
			renderer.sharedMaterial = mat;
		}

		BoxCollider col = root.AddComponent<BoxCollider>();
		col.size = scale;
		Rigidbody body = root.AddComponent<Rigidbody>();
		body.isKinematic = true;
		body.useGravity = false;
		root.AddComponent<TreasureItem>();
		root.AddComponent<TreasureItemInteractable>();

		EnsureFolder( Path.GetDirectoryName( path ).Replace( '\\', '/' ) );
		PrefabUtility.SaveAsPrefabAsset( root, path );
		Object.DestroyImmediate( root );
	}

	static RecyclingRecipeDefinition EnsureRecipe(
		string name,
		TreasureDefinition input,
		TreasureDefinition wood,
		int woodCount,
		TreasureDefinition metal,
		int metalCount,
		bool chewWood,
		bool chewMetal )
	{
		string path = RecipeFolder + "/" + name + ".asset";
		RecyclingRecipeDefinition recipe = AssetDatabase.LoadAssetAtPath<RecyclingRecipeDefinition>( path );
		if ( recipe == null )
		{
			recipe = ScriptableObject.CreateInstance<RecyclingRecipeDefinition>();
			AssetDatabase.CreateAsset( recipe, path );
		}

		recipe.input = input;
		recipe.chewWood = chewWood;
		recipe.chewMetal = chewMetal;

		List<RecyclingOutputEntry> outputs = new List<RecyclingOutputEntry>();
		if ( wood != null && woodCount > 0 )
			outputs.Add( new RecyclingOutputEntry { resource = wood, count = woodCount } );
		if ( metal != null && metalCount > 0 )
			outputs.Add( new RecyclingOutputEntry { resource = metal, count = metalCount } );
		recipe.outputs = outputs.ToArray();
		EditorUtility.SetDirty( recipe );
		return recipe;
	}

	static GameObject LoadPrefab()
	{
		GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>( PrefabPath );
		if ( prefab == null )
			Debug.LogWarning( "RecyclingMachineSetup: missing prefab at " + PrefabPath + " — author via MCP." );
		return prefab;
	}

	static void PlaceInActiveScene( GameObject prefab )
	{
		if ( prefab == null )
			return;

		Scene scene = SceneManager.GetActiveScene();
		EnsureStationInScene( prefab, scene, save: false );
	}

	static void EnsureStationInScene( GameObject prefab, Scene scene, bool save )
	{
		if ( prefab == null || !scene.IsValid() )
			return;

		RecyclingMachine[] existing = Object.FindObjectsByType<RecyclingMachine>( FindObjectsInactive.Include, FindObjectsSortMode.None );
		for ( int i = 0; i < existing.Length; i++ )
		{
			if ( existing[ i ] != null && existing[ i ].gameObject.scene == scene )
				return;
		}

		GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab( prefab, scene );
		instance.transform.position = LevelSpawnPosition;
		Undo.RegisterCreatedObjectUndo( instance, "Place Recycling Machine" );
		if ( save )
			EditorSceneManager.MarkSceneDirty( scene );
	}

	static void EnsureFolder( string path )
	{
		if ( AssetDatabase.IsValidFolder( path ) )
			return;

		string[] parts = path.Split( '/' );
		string current = parts[ 0 ];
		for ( int i = 1; i < parts.Length; i++ )
		{
			string next = current + "/" + parts[ i ];
			if ( !AssetDatabase.IsValidFolder( next ) )
				AssetDatabase.CreateFolder( current, parts[ i ] );
			current = next;
		}
	}
}
#endif
