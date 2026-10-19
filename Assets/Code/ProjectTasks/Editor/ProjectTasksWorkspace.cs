#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Initialize and maintain the Markdown Project Tasks directory layout.
/// Never overwrites existing Markdown file bodies.
/// </summary>
public static class ProjectTasksWorkspace
{
	public sealed class InitResult
	{
		public bool Success;
		public string Message = string.Empty;
		public readonly List<string> Created = new List<string>();
		public readonly List<string> SkippedExisting = new List<string>();
	}

	public static string InboxDir( string root ) =>
		Path.Combine( root, ProjectTasksTemplates.InboxFolderName );

	public static string FeaturesDir( string root ) =>
		Path.Combine( root, ProjectTasksTemplates.FeaturesFolderName );

	public static string ArchiveDir( string root ) =>
		Path.Combine( root, ProjectTasksTemplates.ArchiveFolderName );

	public static string AiInstructionsPath( string root ) =>
		Path.Combine( root, ProjectTasksTemplates.AiInstructionsFileName );

	public static string ProjectPath( string root ) =>
		Path.Combine( root, ProjectTasksTemplates.ProjectFileName );

	public static bool IsInitialized( string rootAbsolute )
	{
		string root = ProjectTasksSettings.NormalizeAbsolutePath( rootAbsolute );
		if ( string.IsNullOrEmpty( root ) || !Directory.Exists( root ) )
			return false;
		if ( !Directory.Exists( InboxDir( root ) ) )
			return false;
		if ( !Directory.Exists( FeaturesDir( root ) ) )
			return false;
		if ( !Directory.Exists( ArchiveDir( root ) ) )
			return false;
		if ( !File.Exists( AiInstructionsPath( root ) ) )
			return false;
		if ( !File.Exists( ProjectPath( root ) ) )
			return false;
		return true;
	}

	public static InitResult Initialize( string rootAbsolute )
	{
		var result = new InitResult();
		string root = ProjectTasksSettings.NormalizeAbsolutePath( rootAbsolute );
		if ( string.IsNullOrEmpty( root ) )
		{
			result.Success = false;
			result.Message = "Root path is empty.";
			return result;
		}

		try
		{
			EnsureDirectory( root, result );
			EnsureDirectory( InboxDir( root ), result );
			EnsureDirectory( FeaturesDir( root ), result );
			EnsureDirectory( ArchiveDir( root ), result );

			WriteTemplateIfMissing( AiInstructionsPath( root ), ProjectTasksTemplates.AiInstructionsMarkdown, result );
			WriteTemplateIfMissing( ProjectPath( root ), ProjectTasksTemplates.ProjectMarkdown, result );

			ProjectTasksSettings.RootPath = root;
			result.Success = true;
			result.Message = "Workspace ready at " + root
			                + " (created " + result.Created.Count
			                + ", skipped existing " + result.SkippedExisting.Count + ").";
		}
		catch ( Exception ex )
		{
			result.Success = false;
			result.Message = "Initialize failed: " + ex.Message;
		}

		return result;
	}

	public static bool EnsureFeatureStub( string rootAbsolute, string featureName, out string absolutePath, out string message )
	{
		absolutePath = null;
		message = string.Empty;
		string root = ProjectTasksSettings.NormalizeAbsolutePath( rootAbsolute );
		if ( string.IsNullOrEmpty( root ) )
		{
			message = "Root path is empty.";
			return false;
		}

		string sanitized = ProjectTasksMarkdown.SanitizeFeatureFileName( featureName );
		if ( string.Equals( sanitized, ProjectTasksMarkdown.AutoFeatureName, StringComparison.OrdinalIgnoreCase ) )
		{
			message = "Cannot create a feature named Auto.";
			return false;
		}

		string featuresDir = FeaturesDir( root );
		if ( !Directory.Exists( featuresDir ) )
			Directory.CreateDirectory( featuresDir );

		absolutePath = Path.Combine( featuresDir, ProjectTasksMarkdown.FeatureFileName( sanitized ) );
		if ( File.Exists( absolutePath ) )
		{
			message = "Feature already exists: " + sanitized;
			return true;
		}

		ProjectTasksMarkdown.WriteUtf8( absolutePath, ProjectTasksTemplates.FeatureStubMarkdown( sanitized ) );
		message = "Created feature: " + sanitized;
		return true;
	}

	public static string CaptureInboxTask( string rootAbsolute, string description, string featureName, out string absolutePath, out string message )
	{
		absolutePath = null;
		message = string.Empty;
		string root = ProjectTasksSettings.NormalizeAbsolutePath( rootAbsolute );
		if ( string.IsNullOrEmpty( root ) )
		{
			message = "Configure a Project Tasks root folder first.";
			return null;
		}

		string trimmed = description != null ? description.Trim() : string.Empty;
		if ( string.IsNullOrEmpty( trimmed ) )
		{
			message = "Task description is empty.";
			return null;
		}

		string feature = string.IsNullOrWhiteSpace( featureName )
			? ProjectTasksMarkdown.AutoFeatureName
			: featureName.Trim();

		if ( !string.Equals( feature, ProjectTasksMarkdown.AutoFeatureName, StringComparison.OrdinalIgnoreCase ) )
		{
			string featurePath;
			string featureMessage;
			if ( !EnsureFeatureStub( root, feature, out featurePath, out featureMessage ) )
			{
				message = featureMessage;
				return null;
			}
		}

		string inboxDir = InboxDir( root );
		if ( !Directory.Exists( inboxDir ) )
			Directory.CreateDirectory( inboxDir );

		string taskId = ProjectTasksMarkdown.NewTaskId();
		absolutePath = Path.Combine( inboxDir, taskId + ".md" );
		if ( File.Exists( absolutePath ) )
		{
			taskId = ProjectTasksMarkdown.NewTaskId();
			absolutePath = Path.Combine( inboxDir, taskId + ".md" );
		}

		string markdown = ProjectTasksMarkdown.BuildInboxMarkdown( taskId, feature, trimmed, DateTime.UtcNow );
		ProjectTasksMarkdown.WriteUtf8( absolutePath, markdown );
		message = "Captured " + taskId + " → Inbox (" + feature + ").";
		return taskId;
	}

	public static List<string> ListMarkdownFiles( string directoryAbsolute )
	{
		var list = new List<string>();
		if ( string.IsNullOrWhiteSpace( directoryAbsolute ) || !Directory.Exists( directoryAbsolute ) )
			return list;

		string[] files = Directory.GetFiles( directoryAbsolute, "*.md", SearchOption.TopDirectoryOnly );
		Array.Sort( files, StringComparer.OrdinalIgnoreCase );
		for ( int i = 0; i < files.Length; i++ )
			list.Add( files[i] );
		return list;
	}

	public static void RevealInFinder( string absolutePath )
	{
		if ( string.IsNullOrWhiteSpace( absolutePath ) )
			return;
		if ( File.Exists( absolutePath ) || Directory.Exists( absolutePath ) )
			UnityEditor.EditorUtility.RevealInFinder( absolutePath );
	}

	public static void OpenFile( string absolutePath )
	{
		if ( string.IsNullOrWhiteSpace( absolutePath ) || !File.Exists( absolutePath ) )
			return;
		UnityEditor.EditorUtility.OpenWithDefaultApp( absolutePath );
	}

	static void EnsureDirectory( string path, InitResult result )
	{
		if ( Directory.Exists( path ) )
		{
			result.SkippedExisting.Add( path );
			return;
		}

		Directory.CreateDirectory( path );
		result.Created.Add( path );
	}

	static void WriteTemplateIfMissing( string path, string contents, InitResult result )
	{
		if ( File.Exists( path ) )
		{
			result.SkippedExisting.Add( path );
			return;
		}

		ProjectTasksMarkdown.WriteUtf8( path, contents );
		result.Created.Add( path );
	}
}
#endif
