#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

[InitializeOnLoad]
static class StylizedLightingDefinitionInstaller
{
	const string DefinitionAssetPath = "Assets/Definitions/StylizedLightingDefinition.asset";

	static StylizedLightingDefinitionInstaller()
	{
		EditorApplication.delayCall += EnsureInstalled;
	}

	[MenuItem( DragonLootMenus.GraphicsStylizedLightingInstall, priority = 200 )]
	static void InstallFromMenu()
	{
		StylizedLightingDefinition definition = EnsureDefinitionAsset();
		if ( definition != null )
		{
			definition.ApplyToGlobals();
			Selection.activeObject = definition;
			EditorGUIUtility.PingObject( definition );
		}
	}

	static void EnsureInstalled()
	{
		StylizedLightingDefinition.ApplyDefaultGlobals();
		StylizedLightingDefinition definition = EnsureDefinitionAsset();
		if ( definition != null )
			definition.ApplyToGlobals();
	}

	static StylizedLightingDefinition EnsureDefinitionAsset()
	{
		StylizedLightingDefinition definition = AssetDatabase.LoadAssetAtPath<StylizedLightingDefinition>( DefinitionAssetPath );
		if ( definition == null )
		{
			if ( !AssetDatabase.IsValidFolder( "Assets/Definitions" ) )
				AssetDatabase.CreateFolder( "Assets", "Definitions" );

			definition = ScriptableObject.CreateInstance<StylizedLightingDefinition>();
			definition.name = "StylizedLightingDefinition";
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

		entry.SetAddress( "StylizedLightingDefinition" );
		if ( !entry.labels.Contains( "Definition" ) )
			entry.SetLabel( "Definition", true, true );
		settings.SetDirty( AddressableAssetSettings.ModificationEvent.EntryModified, entry, true );
	}
}
#endif
