#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

[InitializeOnLoad]
static class GoldBarStackSettingsInstaller
{
	const string DefinitionAssetPath = "Assets/Definitions/Treasure/GoldBarStackSettings.asset";

	static GoldBarStackSettingsInstaller()
	{
		EditorApplication.delayCall += EnsureInstalled;
	}

	[MenuItem( DragonLootMenus.TreasureInstallGoldBarStackSettings, priority = 45 )]
	static void InstallFromMenu()
	{
		GoldBarStackSettings definition = EnsureDefinitionAsset();
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

	static GoldBarStackSettings EnsureDefinitionAsset()
	{
		if ( !AssetDatabase.IsValidFolder( "Assets/Definitions" ) )
			AssetDatabase.CreateFolder( "Assets", "Definitions" );
		if ( !AssetDatabase.IsValidFolder( "Assets/Definitions/Treasure" ) )
			AssetDatabase.CreateFolder( "Assets/Definitions", "Treasure" );

		GoldBarStackSettings definition = AssetDatabase.LoadAssetAtPath<GoldBarStackSettings>( DefinitionAssetPath );
		if ( definition == null )
		{
			definition = ScriptableObject.CreateInstance<GoldBarStackSettings>();
			definition.name = "GoldBarStackSettings";
			AssetDatabase.CreateAsset( definition, DefinitionAssetPath );
			AssetDatabase.SaveAssets();
			Debug.Log( "Created " + DefinitionAssetPath );
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

		entry.SetAddress( "GoldBarStackSettings" );
		if ( !entry.labels.Contains( "Definition" ) )
			entry.SetLabel( "Definition", true, true );
		settings.SetDirty( AddressableAssetSettings.ModificationEvent.EntryModified, entry, true );
	}
}
#endif
