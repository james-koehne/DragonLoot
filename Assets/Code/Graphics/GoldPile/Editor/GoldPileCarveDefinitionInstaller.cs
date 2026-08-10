#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

[InitializeOnLoad]
static class GoldPileCarveDefinitionInstaller
{
	const string DefinitionAssetPath = "Assets/Definitions/GoldPileCarveDefinition.asset";

	static GoldPileCarveDefinitionInstaller()
	{
		EditorApplication.delayCall += EnsureInstalled;
	}

	[MenuItem( DragonLootMenus.GraphicsGoldPileCarveInstall, priority = 210 )]
	static void InstallFromMenu()
	{
		GoldPileCarveDefinition definition = EnsureDefinitionAsset();
		if ( definition != null )
		{
			Selection.activeObject = definition;
			EditorGUIUtility.PingObject( definition );
		}
	}

	static void EnsureInstalled()
	{
		EnsureDefinitionAsset();
	}

	static GoldPileCarveDefinition EnsureDefinitionAsset()
	{
		GoldPileCarveDefinition definition = AssetDatabase.LoadAssetAtPath<GoldPileCarveDefinition>( DefinitionAssetPath );
		if ( definition == null )
		{
			if ( !AssetDatabase.IsValidFolder( "Assets/Definitions" ) )
				AssetDatabase.CreateFolder( "Assets", "Definitions" );

			definition = ScriptableObject.CreateInstance<GoldPileCarveDefinition>();
			definition.name = "GoldPileCarveDefinition";
			// Migrate tuned values formerly on PlayerControllerDefinition.
			definition.carveRadius = 0.1f;
			definition.carveMinRadiusFractionOfPile = 0.06f;
			definition.carveBlurPadCells = 1;
			definition.carveBlurPasses = 1;
			definition.carveBlurStrength = 0.1f;
			definition.carveFalloffSharpness = 3.5f;
			definition.Validate();
			AssetDatabase.CreateAsset( definition, DefinitionAssetPath );
			AssetDatabase.SaveAssets();
		}

		RegisterDefinitionAddressable( DefinitionAssetPath );
		return definition;
	}

	static void RegisterDefinitionAddressable( string assetPath )
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

		entry.SetAddress( "GoldPileCarveDefinition" );
		if ( !entry.labels.Contains( "Definition" ) )
			entry.SetLabel( "Definition", true, true );
		settings.SetDirty( AddressableAssetSettings.ModificationEvent.EntryModified, entry, true );
	}
}
#endif
