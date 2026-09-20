#if UNITY_EDITOR
using System.Collections.Generic;

using UnityEditor;

using UnityEngine;

/// <summary>
/// Ensures the dig / pickup speed upgrade exists and is registered on <see cref="UpgradeCatalogDefinition"/>.
/// </summary>
public static class UpgradeDigPickupSpeedBootstrap
{
	const string UpgradesFolder = "Assets/Definitions/Upgrades";
	const string UpgradePath = UpgradesFolder + "/Upgrade_DigPickupSpeed.asset";
	const string CatalogPath = "Assets/Definitions/UpgradeCatalogDefinition.asset";

	[InitializeOnLoadMethod]
	static void QueueEnsure()
	{
		EditorApplication.delayCall += EnsureUpgrade;
	}

	[MenuItem( DragonLootMenus.DefinitionsEnsureDigPickupSpeedUpgrade )]
	static void MenuEnsureUpgrade()
	{
		EnsureUpgrade();
		Debug.Log( "Upgrade_DigPickupSpeed ensured and registered on UpgradeCatalogDefinition." );
	}

	static void EnsureUpgrade()
	{
		AddressableEditorUtil.EnsureFolder( UpgradesFolder );

		UpgradeDefinition upgrade = AssetDatabase.LoadAssetAtPath<UpgradeDefinition>( UpgradePath );
		if ( upgrade == null )
		{
			upgrade = ScriptableObject.CreateInstance<UpgradeDefinition>();
			upgrade.name = "Upgrade_DigPickupSpeed";
			upgrade.id = UpgradeDefinition.DigPickupSpeedId;
			upgrade.displayName = "Swift Hands";
			upgrade.description = "Dig and pick up treasure 1.5x faster.";
			upgrade.maxLevel = 1;
			upgrade.enabled = true;
			upgrade.digPickupSpeedMultiplier = PlayerDigPickupSpeed.DefaultMultiplier;
			AssetDatabase.CreateAsset( upgrade, UpgradePath );
			AssetDatabase.SaveAssets();
		}
		else
		{
			bool dirty = false;
			if ( upgrade.id != UpgradeDefinition.DigPickupSpeedId )
			{
				upgrade.id = UpgradeDefinition.DigPickupSpeedId;
				dirty = true;
			}
			if ( string.IsNullOrEmpty( upgrade.displayName ) )
			{
				upgrade.displayName = "Swift Hands";
				dirty = true;
			}
			if ( upgrade.maxLevel < 1 )
			{
				upgrade.maxLevel = 1;
				dirty = true;
			}
			if ( Mathf.Approximately( upgrade.digPickupSpeedMultiplier, 1f ) )
			{
				upgrade.digPickupSpeedMultiplier = PlayerDigPickupSpeed.DefaultMultiplier;
				dirty = true;
			}

			if ( dirty )
			{
				EditorUtility.SetDirty( upgrade );
				AssetDatabase.SaveAssets();
			}
		}

		UpgradeCatalogDefinition catalog = AssetDatabase.LoadAssetAtPath<UpgradeCatalogDefinition>( CatalogPath );
		if ( catalog == null )
			return;

		if ( catalog.upgrades == null )
			catalog.upgrades = new List<UpgradeDefinition>();

		for ( int i = 0; i < catalog.upgrades.Count; i++ )
		{
			UpgradeDefinition entry = catalog.upgrades[ i ];
			if ( entry == upgrade || ( entry != null && entry.id == UpgradeDefinition.DigPickupSpeedId ) )
				return;
		}

		catalog.upgrades.Add( upgrade );
		EditorUtility.SetDirty( catalog );
		AssetDatabase.SaveAssets();
	}
}
#endif
