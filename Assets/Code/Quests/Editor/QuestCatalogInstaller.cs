#if UNITY_EDITOR
using System.Collections.Generic;

using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// Creates / rebuilds quest definition assets and catalog. Does not modify Level.unity.
/// </summary>
[InitializeOnLoad]
public static class QuestCatalogInstaller
{
	const string QuestsFolder = "Assets/Definitions/Quests";
	const string CatalogPath = "Assets/Definitions/QuestCatalogDefinition.asset";
	const string StingerPath = "Assets/Audio/SFX/Stingers/EpicRiser_TEMP_DELETE.wav";
	const string StartingQuestId = "quest_starting";

	static QuestCatalogInstaller()
	{
		EditorApplication.delayCall += () => EnsureCatalog( forceRebuildContent: false );
	}

	[MenuItem( DragonLootMenus.QuestsInstallCatalog, priority = 300 )]
	static void InstallCatalogFromMenu()
	{
		QuestCatalogDefinition catalog = EnsureCatalog( forceRebuildContent: true );
		if ( catalog != null )
		{
			Selection.activeObject = catalog;
			EditorGUIUtility.PingObject( catalog );
		}
	}

	[MenuItem( DragonLootMenus.QuestsWireLevel, priority = 301 )]
	static void WireLevelFromMenu()
	{
		EnsureCatalog( forceRebuildContent: false );
		Debug.Log(
			"Quest scene wiring is manual. Do not run an installer against Level.unity.\n" +
			"Set StartingAreaDoor.unlockQuestId = quest_starting.\n" +
			"Add QuestTarget.areaId = starting_area on starting-area stations.\n" +
			"Move coin_sorter onto CoinSortingStation (1). Add gold_bar_table on GoldBarDisplayTable.\n" +
			"Add volume_hallway_enter / volume_hallway_end and snap constellation / sorter volumes." );
	}

	public static void BatchInstall()
	{
		EnsureCatalog( forceRebuildContent: true );
		AssetDatabase.SaveAssets();
	}

	static QuestCatalogDefinition EnsureCatalog( bool forceRebuildContent )
	{
		EnsureFolder( "Assets/Definitions" );
		EnsureFolder( QuestsFolder );

		QuestDefinition coins = EnsureQuestAsset( QuestsFolder + "/Quest_CoinSorting.asset", "quest_coin_sorting", QuestCatalogFallback.PopulateCoinsSubquest, forceRebuildContent );
		QuestDefinition constellation = EnsureQuestAsset( QuestsFolder + "/Quest_Constellation.asset", "quest_constellation", QuestCatalogFallback.PopulateConstellationSubquest, forceRebuildContent );
		QuestDefinition artifacts = EnsureQuestAsset( QuestsFolder + "/Quest_Museum.asset", "quest_museum", QuestCatalogFallback.PopulateArtifactsSubquest, forceRebuildContent );
		QuestDefinition starting = EnsureQuestAsset(
			QuestsFolder + "/Quest_Starting.asset",
			StartingQuestId,
			quest => QuestCatalogFallback.PopulateStartingQuest( quest, coins, constellation, artifacts ),
			forceRebuildContent );
		QuestDefinition main = EnsureQuestAsset( QuestsFolder + "/Quest_Final.asset", "quest_final", QuestCatalogFallback.PopulateMainQuest, forceRebuildContent );

		starting.subquests = new[] { coins, constellation, artifacts };
		QuestCatalogFallback.AssignMainQuestStinger( main, AssetDatabase.LoadAssetAtPath<AudioClip>( StingerPath ) );
		EditorUtility.SetDirty( starting );
		EditorUtility.SetDirty( main );

		QuestCatalogDefinition catalog = AssetDatabase.LoadAssetAtPath<QuestCatalogDefinition>( CatalogPath );
		if ( catalog == null )
		{
			catalog = ScriptableObject.CreateInstance<QuestCatalogDefinition>();
			catalog.name = "QuestCatalogDefinition";
			AssetDatabase.CreateAsset( catalog, CatalogPath );
		}

		catalog.quests = new List<QuestDefinition> { starting, main };
		EditorUtility.SetDirty( catalog );
		AssetDatabase.SaveAssets();

		RegisterDefinitionAddressable( CatalogPath, "QuestCatalogDefinition" );
		return catalog;
	}

	delegate void QuestBuilder( QuestDefinition quest );

	static QuestDefinition EnsureQuestAsset( string path, string id, QuestBuilder builder, bool forceRebuild )
	{
		QuestDefinition quest = AssetDatabase.LoadAssetAtPath<QuestDefinition>( path );
		if ( quest == null )
		{
			quest = ScriptableObject.CreateInstance<QuestDefinition>();
			quest.name = System.IO.Path.GetFileNameWithoutExtension( path );
			AssetDatabase.CreateAsset( quest, path );
			forceRebuild = true;
		}

		if ( forceRebuild || string.IsNullOrEmpty( quest.id ) || quest.objectives == null || quest.objectives.Length == 0 )
		{
			Undo.RecordObject( quest, "Build Quest Content" );
			quest.id = id;
			builder( quest );
			EditorUtility.SetDirty( quest );
		}

		return quest;
	}

	static void EnsureFolder( string path )
	{
		if ( AssetDatabase.IsValidFolder( path ) )
			return;

		string parent = System.IO.Path.GetDirectoryName( path ).Replace( '\\', '/' );
		string name = System.IO.Path.GetFileName( path );
		if ( !AssetDatabase.IsValidFolder( parent ) )
			EnsureFolder( parent );
		AssetDatabase.CreateFolder( parent, name );
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
}
#endif
