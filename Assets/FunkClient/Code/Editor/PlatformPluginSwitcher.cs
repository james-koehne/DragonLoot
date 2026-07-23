using System;
using System.IO;

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

using UnityEngine;

public class PlatformPluginSwitcher : IPreprocessBuildWithReport
{
	public int callbackOrder => 0;

	public void OnPreprocessBuild( BuildReport report )
	{
#if UNITY_6000_0_OR_NEWER
		NamedBuildTarget buildTarget = NamedBuildTarget.FromBuildTargetGroup( EditorUserBuildSettings.selectedBuildTargetGroup );
		string defineSymbols = PlayerSettings.GetScriptingDefineSymbols( buildTarget );
#else
		string defineSymbols = PlayerSettings.GetScriptingDefineSymbolsForGroup( EditorUserBuildSettings.selectedBuildTargetGroup );
#endif

		bool isSteam = defineSymbols.Contains( "STEAM_BUILD" );
		bool isStove = defineSymbols.Contains( "STOVE_BUILD" );

		if ( isSteam && isStove )
			Debug.LogWarning( "[PlatformPluginSwitcher] Both STEAM_BUILD and STOVE_BUILD are defined. Use only one per build so store SDKs and conditional code do not overlap." );

		SetPluginsEnabled( "Assets/Plugins/Steamworks", isSteam );
		SetPluginsEnabled( "Assets/Plugins/Stove", isStove );

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
	}

	/// <summary>
	/// Walks <paramref name="folderPath"/> recursively (all subfolders) and toggles every <see cref="PluginImporter"/> found.
	/// </summary>
	private void SetPluginsEnabled( string folderPath, bool enabled )
	{
		folderPath = folderPath.Replace( '\\', '/' ).TrimEnd( '/' );

		string projectRoot = Directory.GetParent( Application.dataPath ).FullName;
		string absRoot = Path.GetFullPath( Path.Combine( projectRoot, folderPath ) );

		if ( !Directory.Exists( absRoot ) )
		{
			Debug.Log( $"[PlatformPluginSwitcher] Skip (folder missing): {folderPath}" );
			return;
		}

		Debug.Log( $"[PlatformPluginSwitcher] Scanning '{folderPath}' (editor + Win64 = {( enabled ? "enabled" : "disabled" )})" );

		foreach ( string absFile in Directory.EnumerateFiles( absRoot, "*", SearchOption.AllDirectories ) )
		{
			if ( absFile.EndsWith( ".meta", StringComparison.OrdinalIgnoreCase ) )
				continue;

			string assetPath = ToAssetPath( absFile );
			if ( string.IsNullOrEmpty( assetPath ) )
				continue;

			var importer = AssetImporter.GetAtPath( assetPath ) as PluginImporter;
			if ( importer == null )
				continue;

			importer.SetCompatibleWithAnyPlatform( false );
			importer.SetCompatibleWithEditor( enabled );

			importer.SetCompatibleWithPlatform( BuildTarget.StandaloneWindows64, enabled );

			importer.SaveAndReimport();

			Debug.Log( $"[PlatformPluginSwitcher] Set plugin '{assetPath}' -> Editor={( enabled ? "on" : "off" )}, StandaloneWindows64={( enabled ? "on" : "off" )}" );
		}
	}

	private static string ToAssetPath( string absoluteFilePath )
	{
		string fullFile = Path.GetFullPath( absoluteFilePath );
		string dataPath = Path.GetFullPath( Application.dataPath );

		if ( !fullFile.StartsWith( dataPath, StringComparison.OrdinalIgnoreCase ) )
			return null;

		return "Assets" + fullFile.Substring( dataPath.Length ).Replace( '\\', '/' );
	}
}
