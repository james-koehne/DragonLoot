#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

[InitializeOnLoad]
static class LightFlickerDefinitionInstaller
{
	const string DefinitionAssetPath = "Assets/Definitions/LightFlickerDefinition.asset";

	static LightFlickerDefinitionInstaller()
	{
		EditorApplication.delayCall += EnsureInstalled;
	}

	[MenuItem( DragonLootMenus.GraphicsLightFlickerInstall, priority = 210 )]
	static void InstallFromMenu()
	{
		LightFlickerDefinition definition = EnsureDefinitionAsset( forceDefaults: false );
		if ( definition != null )
		{
			Selection.activeObject = definition;
			EditorGUIUtility.PingObject( definition );
		}
	}

	[MenuItem( DragonLootMenus.GraphicsLightFlickerResetPresets, priority = 211 )]
	static void ResetPresetsFromMenu()
	{
		LightFlickerDefinition definition = EnsureDefinitionAsset( forceDefaults: true );
		if ( definition != null )
		{
			Selection.activeObject = definition;
			EditorGUIUtility.PingObject( definition );
			Debug.Log( "Reset LightFlickerDefinition presets to defaults." );
		}
	}

	static void EnsureInstalled()
	{
		EnsureDefinitionAsset( forceDefaults: false );
	}

	static LightFlickerDefinition EnsureDefinitionAsset( bool forceDefaults )
	{
		if ( !AssetDatabase.IsValidFolder( "Assets/Definitions" ) )
			AssetDatabase.CreateFolder( "Assets", "Definitions" );

		LightFlickerDefinition definition = AssetDatabase.LoadAssetAtPath<LightFlickerDefinition>( DefinitionAssetPath );
		if ( definition == null )
		{
			definition = ScriptableObject.CreateInstance<LightFlickerDefinition>();
			definition.name = "LightFlickerDefinition";
			definition.presets = LightFlickerDefinition.CreateDefaultPresets();
			AssetDatabase.CreateAsset( definition, DefinitionAssetPath );
			AssetDatabase.SaveAssets();
			Debug.Log( "Created " + DefinitionAssetPath );
		}
		else if ( forceDefaults || definition.presets == null || definition.presets.Length == 0 )
		{
			Undo.RecordObject( definition, "Reset Light Flicker Presets" );
			definition.presets = LightFlickerDefinition.CreateDefaultPresets();
			EditorUtility.SetDirty( definition );
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

		entry.SetAddress( "LightFlickerDefinition" );
		if ( !entry.labels.Contains( "Definition" ) )
			entry.SetLabel( "Definition", true, true );
		settings.SetDirty( AddressableAssetSettings.ModificationEvent.EntryModified, entry, true );
	}
}
#endif
