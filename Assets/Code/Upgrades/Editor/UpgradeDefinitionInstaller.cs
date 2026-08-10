#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

[InitializeOnLoad]
static class UpgradeDefinitionInstaller
{
	const string UpgradesFolder = "Assets/Definitions/Upgrades";
	const string CatalogPath = "Assets/Definitions/UpgradeCatalogDefinition.asset";
	const string AlphaPath = UpgradesFolder + "/Upgrade_PlaceholderAlpha.asset";
	const string BetaPath = UpgradesFolder + "/Upgrade_PlaceholderBeta.asset";
	const string CoinSortingPath = UpgradesFolder + "/Upgrade_CoinSortingStation.asset";

	static UpgradeDefinitionInstaller()
	{
		EditorApplication.delayCall += EnsureInstalled;
	}

	static void EnsureInstalled()
	{
		EnsureFolder( "Assets/Definitions" );
		EnsureFolder( UpgradesFolder );

		UpgradeDefinition alpha = EnsureUpgrade(
			AlphaPath,
			"placeholder_upgrade_alpha",
			"Placeholder Upgrade Alpha",
			"Debug placeholder upgrade used to verify unlock and level progression.",
			3 );
		UpgradeDefinition beta = EnsureUpgrade(
			BetaPath,
			"placeholder_upgrade_beta",
			"Placeholder Upgrade Beta",
			"Second debug upgrade left locked by default for unlock testing.",
			5 );
		UpgradeDefinition coinSorting = EnsureUpgrade(
			CoinSortingPath,
			CoinSortingStationDefinition.DefaultUpgradeId,
			"Coin Sorting Station",
			"Levels: 1 manual crank, 2 automatic, 3 faster processing, 4 larger hopper.",
			4 );

		UpgradeCatalogDefinition catalog = EnsureCatalog( alpha, beta, coinSorting );
		RegisterDefinitionAddressable( CatalogPath, catalog.name );
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

	static UpgradeDefinition EnsureUpgrade(
		string path,
		string id,
		string displayName,
		string description,
		int maxLevel )
	{
		UpgradeDefinition definition = AssetDatabase.LoadAssetAtPath<UpgradeDefinition>( path );
		if ( definition == null )
		{
			definition = ScriptableObject.CreateInstance<UpgradeDefinition>();
			definition.name = System.IO.Path.GetFileNameWithoutExtension( path );
			AssetDatabase.CreateAsset( definition, path );
		}

		bool dirty = false;
		if ( definition.id != id )
		{
			definition.id = id;
			dirty = true;
		}
		if ( definition.displayName != displayName )
		{
			definition.displayName = displayName;
			dirty = true;
		}
		if ( definition.description != description )
		{
			definition.description = description;
			dirty = true;
		}
		if ( definition.maxLevel != maxLevel )
		{
			definition.maxLevel = maxLevel;
			dirty = true;
		}
		if ( !definition.enabled )
		{
			definition.enabled = true;
			dirty = true;
		}

		if ( dirty )
			EditorUtility.SetDirty( definition );

		AssetDatabase.SaveAssets();
		return definition;
	}

	static UpgradeCatalogDefinition EnsureCatalog( params UpgradeDefinition[] upgrades )
	{
		UpgradeCatalogDefinition catalog = AssetDatabase.LoadAssetAtPath<UpgradeCatalogDefinition>( CatalogPath );
		if ( catalog == null )
		{
			catalog = ScriptableObject.CreateInstance<UpgradeCatalogDefinition>();
			catalog.name = "UpgradeCatalogDefinition";
			AssetDatabase.CreateAsset( catalog, CatalogPath );
		}

		if ( catalog.upgrades == null )
			catalog.upgrades = new System.Collections.Generic.List<UpgradeDefinition>();

		bool dirty = false;
		for ( int i = 0; i < upgrades.Length; i++ )
		{
			UpgradeDefinition upgrade = upgrades[ i ];
			if ( upgrade == null )
				continue;

			if ( !catalog.upgrades.Contains( upgrade ) )
			{
				catalog.upgrades.Add( upgrade );
				dirty = true;
			}
		}

		if ( dirty )
			EditorUtility.SetDirty( catalog );

		AssetDatabase.SaveAssets();
		return catalog;
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
