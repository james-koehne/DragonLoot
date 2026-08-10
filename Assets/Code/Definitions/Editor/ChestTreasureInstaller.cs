#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;

using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Creates chest / key TreasureDefinitions, ChestDefinitions, and wires them into pile contents.
/// Idempotent; runs on editor load and via menu.
/// </summary>
[InitializeOnLoad]
public static class ChestTreasureInstaller
{
	const string KeysFolder = "Assets/Definitions/Treasure/Keys";
	const string ChestsFolder = "Assets/Definitions/Treasure/Chests";
	const string ChestDefsFolder = "Assets/Definitions/Treasure/Chests/ChestDefs";
	const string PrefabFolder = "Assets/Addressables/Treasure/Chests";
	const string DisplayCasePrefabPath = "Assets/Addressables/Treasure/Chests/SkeletonKeyDisplayCase.prefab";

	static readonly string[] PileDefinitionPaths =
	{
		"Assets/Definitions/Treasure/Pile/TreasurePileDefinition.asset",
		"Assets/Definitions/Treasure/Pile/TreasurePileLargeDefinition.asset",
		"Assets/Definitions/Treasure/Pile/TreasurePileGiganticDefinition.asset",
	};

	const string GoldCoinPath = "Assets/Definitions/Treasure/Coins/GoldCoin.asset";
	const string RubyGemPath = "Assets/Definitions/Treasure/Gems/RubyGem.asset";

	static ChestTreasureInstaller()
	{
		EditorApplication.delayCall += EnsureInstalled;
	}

	[MenuItem( DragonLootMenus.TreasureInstallChests )]
	static void MenuInstall()
	{
		EnsureInstalled();
	}

	static void EnsureInstalled()
	{
		EnsureFolder( "Assets/Definitions" );
		EnsureFolder( "Assets/Definitions/Treasure" );
		EnsureFolder( KeysFolder );
		EnsureFolder( ChestsFolder );
		EnsureFolder( ChestDefsFolder );
		EnsureFolder( "Assets/Addressables" );
		EnsureFolder( "Assets/Addressables/Treasure" );
		EnsureFolder( PrefabFolder );

		TreasureDefinition goldCoin = AssetDatabase.LoadAssetAtPath<TreasureDefinition>( GoldCoinPath );
		TreasureDefinition rubyGem = AssetDatabase.LoadAssetAtPath<TreasureDefinition>( RubyGemPath );

		TreasureDefinition ironKey = EnsureKeyDefinition(
			KeysFolder + "/IronKey.asset",
			"IronKey",
			"Iron Key",
			KeyType.Iron,
			isSkeleton: false );
		TreasureDefinition goldKey = EnsureKeyDefinition(
			KeysFolder + "/GoldKey.asset",
			"GoldKey",
			"Gold Key",
			KeyType.Gold,
			isSkeleton: false );
		TreasureDefinition skeletonKey = EnsureKeyDefinition(
			KeysFolder + "/SkeletonKey.asset",
			"SkeletonKey",
			"Skeleton Key",
			KeyType.Bone,
			isSkeleton: true );

		ChestDefinition ironChestDef = EnsureChestDefinition(
			ChestDefsFolder + "/IronChestDefinition.asset",
			"IronChest",
			"Iron Chest",
			"Wood",
			KeyType.Iron,
			8f,
			goldCoin,
			rubyGem );

		ChestDefinition goldChestDef = EnsureChestDefinition(
			ChestDefsFolder + "/GoldChestDefinition.asset",
			"GoldChest",
			"Gold Chest",
			"Ornate",
			KeyType.Gold,
			12f,
			goldCoin,
			rubyGem );

		TreasureDefinition ironChest = EnsureChestTreasure(
			ChestsFolder + "/IronChest.asset",
			"IronChest",
			"Iron Chest",
			ironChestDef );
		TreasureDefinition goldChest = EnsureChestTreasure(
			ChestsFolder + "/GoldChest.asset",
			"GoldChest",
			"Gold Chest",
			goldChestDef );

		WirePiles( ironKey, goldKey, ironChest, goldChest );
		EnsureDisplayCasePrefab( skeletonKey );

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
	}

	static TreasureDefinition EnsureKeyDefinition(
		string path,
		string id,
		string displayName,
		KeyType keyType,
		bool isSkeleton )
	{
		TreasureDefinition def = AssetDatabase.LoadAssetAtPath<TreasureDefinition>( path );
		if ( def == null )
		{
			def = ScriptableObject.CreateInstance<TreasureDefinition>();
			AssetDatabase.CreateAsset( def, path );
		}

		def.id = id;
		def.displayName = displayName;
		def.category = TreasureCategory.Key;
		def.variant = isSkeleton ? "Skeleton" : keyType.ToString();
		def.value = isSkeleton ? 200 : 40;
		def.weight = 1;
		def.exclusiveCarry = true;
		def.usesHeavyThrow = false;
		def.canStack = false;
		def.keyType = keyType;
		def.isSkeletonKey = isSkeleton;
		def.collideWithPlayerOnPile = false;
		def.worldScale = Vector3.one * 0.22f;
		def.heldScale = Vector3.one * 0.18f;
		def.rigidbodyMass = 0.1f;
		def.drag = 0.6f;
		def.angularDrag = 0.6f;
		def.pickupRadius = 0.35f;
		def.coinThickness = 0.08f;
		def.EnsurePhysicsDefaults();

		EditorUtility.SetDirty( def );
		RegisterDefinitionAddressable( path, def.name );
		return def;
	}

	static ChestDefinition EnsureChestDefinition(
		string path,
		string id,
		string displayName,
		string chestType,
		KeyType keyType,
		float lockpickDuration,
		TreasureDefinition goldCoin,
		TreasureDefinition rubyGem )
	{
		ChestDefinition def = AssetDatabase.LoadAssetAtPath<ChestDefinition>( path );
		if ( def == null )
		{
			def = ScriptableObject.CreateInstance<ChestDefinition>();
			AssetDatabase.CreateAsset( def, path );
		}

		def.id = id;
		def.displayName = displayName;
		def.chestType = chestType;
		def.keyType = keyType;
		def.startsLocked = true;
		def.lockpickDuration = lockpickDuration;

		var contents = new List<ChestContentEntry>();
		if ( goldCoin != null )
			contents.Add( new ChestContentEntry { treasure = goldCoin, count = 8 } );
		if ( rubyGem != null )
			contents.Add( new ChestContentEntry { treasure = rubyGem, count = 1 } );
		def.contents = contents.ToArray();

		EditorUtility.SetDirty( def );
		return def;
	}

	static TreasureDefinition EnsureChestTreasure(
		string path,
		string id,
		string displayName,
		ChestDefinition chestDefinition )
	{
		TreasureDefinition def = AssetDatabase.LoadAssetAtPath<TreasureDefinition>( path );
		if ( def == null )
		{
			def = ScriptableObject.CreateInstance<TreasureDefinition>();
			AssetDatabase.CreateAsset( def, path );
		}

		def.id = id;
		def.displayName = displayName;
		def.category = TreasureCategory.Chest;
		def.variant = chestDefinition != null ? chestDefinition.chestType : "Chest";
		def.value = 150;
		def.weight = 10;
		def.exclusiveCarry = true;
		def.usesHeavyThrow = true;
		def.canStack = false;
		def.collideWithPlayerOnPile = true;
		def.cartGridSize = new Vector2Int( 2, 2 );
		def.chestDefinition = chestDefinition;
		def.worldScale = Vector3.one * 0.55f;
		def.heldScale = Vector3.one * 0.35f;
		def.rigidbodyMass = 2f;
		def.drag = 0.8f;
		def.angularDrag = 0.8f;
		def.pickupRadius = 0.6f;
		def.coinThickness = 0.4f;
		def.EnsurePhysicsDefaults();

		EditorUtility.SetDirty( def );
		RegisterDefinitionAddressable( path, def.name );
		return def;
	}

	static void WirePiles(
		TreasureDefinition ironKey,
		TreasureDefinition goldKey,
		TreasureDefinition ironChest,
		TreasureDefinition goldChest )
	{
		for ( int i = 0; i < PileDefinitionPaths.Length; i++ )
		{
			TreasurePileDefinition pile = AssetDatabase.LoadAssetAtPath<TreasurePileDefinition>( PileDefinitionPaths[ i ] );
			if ( pile == null )
				continue;

			var kept = new List<TreasurePileEntry>();
			TreasurePileEntry[] existing = pile.treasureContents;
			if ( existing != null )
			{
				for ( int e = 0; e < existing.Length; e++ )
				{
					TreasureDefinition treasure = existing[ e ].treasure;
					if ( treasure == null )
						continue;
					if ( treasure.category == TreasureCategory.Key || treasure.category == TreasureCategory.Chest )
						continue;
					kept.Add( existing[ e ] );
				}
			}

			int scale = i == 0 ? 1 : ( i == 1 ? 2 : 3 );
			AddEntry( kept, ironKey, Mathf.Max( 2, 3 * scale ) );
			AddEntry( kept, goldKey, Mathf.Max( 1, 2 * scale ) );
			AddEntry( kept, ironChest, Mathf.Max( 1, scale ) );
			AddEntry( kept, goldChest, Mathf.Max( 1, scale - 1 ) );

			pile.treasureContents = kept.ToArray();
			EditorUtility.SetDirty( pile );
		}
	}

	static void AddEntry( List<TreasurePileEntry> list, TreasureDefinition treasure, int count )
	{
		if ( treasure == null || count <= 0 )
			return;

		list.Add( new TreasurePileEntry
		{
			treasure = treasure,
			count = count,
		} );
	}

	static void EnsureDisplayCasePrefab( TreasureDefinition skeletonKey )
	{
		GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>( DisplayCasePrefabPath );
		if ( root == null )
		{
			root = GameObject.CreatePrimitive( PrimitiveType.Cube );
			root.name = "SkeletonKeyDisplayCase";
			root.transform.localScale = new Vector3( 0.6f, 0.9f, 0.6f );

			Collider col = root.GetComponent<Collider>();
			if ( col != null )
				Object.DestroyImmediate( col );

			BoxCollider box = root.AddComponent<BoxCollider>();
			box.size = Vector3.one;
			box.isTrigger = false;

			SkeletonKeyDisplayCase interactable = root.AddComponent<SkeletonKeyDisplayCase>();
			interactable.Configure( skeletonKey, 3 );

			GameObject spawn = new GameObject( "SpawnPoint" );
			spawn.transform.SetParent( root.transform, false );
			spawn.transform.localPosition = new Vector3( 0f, 0.65f, 0f );

			SerializedObject so = new SerializedObject( interactable );
			so.FindProperty( "spawnPoint" ).objectReferenceValue = spawn.transform;
			so.FindProperty( "skeletonKeyDefinition" ).objectReferenceValue = skeletonKey;
			so.FindProperty( "chestsRequired" ).intValue = 3;
			so.ApplyModifiedPropertiesWithoutUndo();

			EnsureFolder( PrefabFolder );
			PrefabUtility.SaveAsPrefabAsset( root, DisplayCasePrefabPath );
			Object.DestroyImmediate( root );
		}
		else
		{
			SkeletonKeyDisplayCase interactable = root.GetComponent<SkeletonKeyDisplayCase>();
			if ( interactable == null )
				interactable = root.AddComponent<SkeletonKeyDisplayCase>();
			interactable.Configure( skeletonKey, 3 );
			EditorUtility.SetDirty( root );
		}

		RegisterPrefabAddressable( DisplayCasePrefabPath, "Treasure/SkeletonKeyDisplayCase" );
	}

	static void EnsureFolder( string path )
	{
		if ( AssetDatabase.IsValidFolder( path ) )
			return;

		string parent = Path.GetDirectoryName( path )?.Replace( '\\', '/' );
		string leaf = Path.GetFileName( path );
		if ( string.IsNullOrEmpty( parent ) || string.IsNullOrEmpty( leaf ) )
			return;

		if ( !AssetDatabase.IsValidFolder( parent ) )
			EnsureFolder( parent );

		AssetDatabase.CreateFolder( parent, leaf );
	}

	static void RegisterDefinitionAddressable( string assetPath, string address )
	{
		AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
		if ( settings == null )
			return;

		string guid = AssetDatabase.AssetPathToGUID( assetPath );
		if ( string.IsNullOrEmpty( guid ) )
			return;

		AddressableAssetEntry entry = settings.FindAssetEntry( guid );
		if ( entry == null )
			entry = settings.CreateOrMoveEntry( guid, settings.DefaultGroup );

		entry.SetAddress( address );
		if ( !entry.labels.Contains( "Definition" ) )
			entry.SetLabel( "Definition", true, true );
		settings.SetDirty( AddressableAssetSettings.ModificationEvent.EntryModified, entry, true );
	}

	static void RegisterPrefabAddressable( string assetPath, string address )
	{
		AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
		if ( settings == null )
			return;

		string guid = AssetDatabase.AssetPathToGUID( assetPath );
		if ( string.IsNullOrEmpty( guid ) )
			return;

		AddressableAssetEntry entry = settings.FindAssetEntry( guid );
		if ( entry == null )
			entry = settings.CreateOrMoveEntry( guid, settings.DefaultGroup );

		entry.SetAddress( address );
		settings.SetDirty( AddressableAssetSettings.ModificationEvent.EntryModified, entry, true );
	}
}
#endif
