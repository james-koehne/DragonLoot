#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;

/// <summary>
/// EditorPrefs-backed settings for the Markdown Project Tasks workspace.
/// Root may be any absolute folder (including outside the Unity project / inside an Obsidian vault).
/// </summary>
public static class ProjectTasksSettings
{
	const string Prefix = "DragonLoot.ProjectTasks.";
	const string KeyRootPath = Prefix + "RootPath";

	public static string RootPath
	{
		get => EditorPrefs.GetString( KeyRootPath, string.Empty );
		set
		{
			string path = value ?? string.Empty;
			path = path.Trim();
			if ( !string.IsNullOrEmpty( path ) )
				path = Path.GetFullPath( path );
			EditorPrefs.SetString( KeyRootPath, path );
		}
	}

	public static bool HasRootPath => !string.IsNullOrWhiteSpace( RootPath );

	public static bool IsRootConfiguredAndExists()
	{
		if ( !HasRootPath )
			return false;
		return Directory.Exists( RootPath );
	}

	public static string NormalizeAbsolutePath( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return string.Empty;
		try
		{
			return Path.GetFullPath( path.Trim() );
		}
		catch ( Exception )
		{
			return path.Trim();
		}
	}
}
#endif
