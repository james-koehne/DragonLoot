#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public sealed class SyncWriteResult
{
	public int FeaturesWritten;
	public int FeaturesSkipped;
	public int FeaturesArchived;
	public int FeaturesRenamed;
	public int FeaturesCompleted;
	public readonly List<string> Messages = new List<string>();
	public readonly List<string> Warnings = new List<string>();
}

/// <summary>
/// Writes Markdown under the output root with stable IDs, incremental skips, and archive-on-delete.
/// Complete (ticked) features go under FEATURES/_complete/.
/// </summary>
public static class IncrementalFileWriter
{
	public const string CompleteFolderName = "_complete";
	public const string ArchiveFolderName = "_archive";

	public static SyncWriteResult Write( ProjectBoard board, string outputRootAbsolute, DateTime syncUtc )
	{
		if ( board == null )
			throw new ArgumentNullException( nameof( board ) );
		if ( string.IsNullOrWhiteSpace( outputRootAbsolute ) )
			throw new ArgumentException( "Output root is empty.", nameof( outputRootAbsolute ) );

		var result = new SyncWriteResult();
		foreach ( string warning in board.Warnings )
			result.Warnings.Add( warning );

		string featuresDir = Path.Combine( outputRootAbsolute, "FEATURES" );
		string completeDir = Path.Combine( featuresDir, CompleteFolderName );
		string archiveDir = Path.Combine( featuresDir, ArchiveFolderName );
		EnsureDirectory( outputRootAbsolute );
		EnsureDirectory( featuresDir );
		EnsureDirectory( completeDir );
		EnsureDirectory( archiveDir );

		SyncManifest manifest = SyncManifest.Load( outputRootAbsolute );
		var seenIds = new HashSet<string>( StringComparer.Ordinal );
		var featureEntries = new Dictionary<string, SyncManifestEntry>( StringComparer.Ordinal );

		foreach ( ProjectCategory category in board.Categories )
		{
			foreach ( ProjectFeature feature in category.Features )
			{
				if ( string.IsNullOrEmpty( feature.Id ) )
				{
					result.Warnings.Add( "Feature without Milanote ID skipped: " + feature.Name );
					continue;
				}

				seenIds.Add( feature.Id );

				SyncManifestEntry entry = manifest.GetOrCreate( feature.Id, feature.Name );
				string desiredFileName = SyncManifest.BuildFileName( entry.Ordinal, feature.Name );
				bool isComplete = feature.Status == FeatureStatus.Done;
				string targetDir = isComplete ? completeDir : featuresDir;
				string relativeFolder = isComplete ? "FEATURES/" + CompleteFolderName : "FEATURES";

				ResolveAndRenameFile(
					entry,
					desiredFileName,
					featuresDir,
					completeDir,
					targetDir,
					result );

				string fingerprint = MarkdownGenerator.BuildContentFingerprint( feature );
				string hash = SyncManifest.HashContent( fingerprint );
				string absolutePath = Path.Combine( targetDir, entry.FileName );
				string existingMarkdown = File.Exists( absolutePath )
					? File.ReadAllText( absolutePath, Encoding.UTF8 )
					: null;

				bool milanoteUnchanged = string.Equals( entry.ContentHash, hash, StringComparison.Ordinal );
				bool cursorSectionsPresent = FeatureMarkdownMerge.HasAllCursorSections( existingMarkdown );
				if ( milanoteUnchanged && cursorSectionsPresent && File.Exists( absolutePath ) )
				{
					result.FeaturesSkipped++;
					featureEntries[feature.Id] = entry;
					continue;
				}

				string generated = MarkdownGenerator.GenerateFeature( feature, syncUtc );
				string merged = FeatureMarkdownMerge.Merge( generated, existingMarkdown );
				WriteTextIfChanged( absolutePath, merged );
				entry.ContentHash = hash;
				result.FeaturesWritten++;
				if ( isComplete )
					result.FeaturesCompleted++;
				result.Messages.Add( "Wrote: " + relativeFolder + "/" + entry.FileName );
				featureEntries[feature.Id] = entry;
			}
		}

		ArchiveMissingFeatures( manifest, seenIds, featuresDir, completeDir, archiveDir, result );

		Func<ProjectFeature, string> linkResolver = feature =>
		{
			string folder = feature.Status == FeatureStatus.Done
				? "FEATURES/" + CompleteFolderName
				: "FEATURES";
			if ( featureEntries.TryGetValue( feature.Id, out SyncManifestEntry entry ) )
				return folder + "/" + entry.FileName.Replace( " ", "%20" );
			return folder + "/" + SyncManifest.BuildFileName( 0, feature.Name ).Replace( " ", "%20" );
		};

		Func<ProjectFeature, string> displayName = feature =>
		{
			string label;
			if ( featureEntries.TryGetValue( feature.Id, out SyncManifestEntry entry ) )
				label = Path.GetFileNameWithoutExtension( entry.FileName );
			else
				label = feature.Name;

			if ( feature.Status == FeatureStatus.Done )
				return label + " (Complete)";
			return label;
		};

		string currentMd = MarkdownGenerator.GenerateCurrent( board, linkResolver );
		string indexMd = MarkdownGenerator.GenerateIndex( board, displayName );
		WriteTextIfChanged( Path.Combine( outputRootAbsolute, "CURRENT.md" ), currentMd );
		WriteTextIfChanged( Path.Combine( outputRootAbsolute, "INDEX.md" ), indexMd );
		result.Messages.Add( "Updated CURRENT.md and INDEX.md" );

		manifest.Save( outputRootAbsolute );
		return result;
	}

	static void ResolveAndRenameFile(
		SyncManifestEntry entry,
		string desiredFileName,
		string featuresDir,
		string completeDir,
		string targetDir,
		SyncWriteResult result )
	{
		string existingPath = FindExistingFeatureFile( entry.FileName, featuresDir, completeDir );
		string targetPath = Path.Combine( targetDir, desiredFileName );

		if ( !string.Equals( entry.FileName, desiredFileName, StringComparison.Ordinal ) )
			entry.FileName = desiredFileName;

		if ( string.IsNullOrEmpty( existingPath ) )
			return;

		if ( string.Equals( existingPath, targetPath, StringComparison.OrdinalIgnoreCase ) )
			return;

		EnsureDirectory( targetDir );
		if ( File.Exists( targetPath ) )
			File.Delete( targetPath );
		File.Move( existingPath, targetPath );
		result.FeaturesRenamed++;
		result.Messages.Add( "Moved: " + Path.GetFileName( existingPath ) + " → " + RelativeFeaturesPath( targetPath, featuresDir ) );
	}

	static string FindExistingFeatureFile( string fileName, string featuresDir, string completeDir )
	{
		if ( string.IsNullOrEmpty( fileName ) )
			return null;

		string activePath = Path.Combine( featuresDir, fileName );
		if ( File.Exists( activePath ) )
			return activePath;

		string completePath = Path.Combine( completeDir, fileName );
		if ( File.Exists( completePath ) )
			return completePath;

		return null;
	}

	static void ArchiveMissingFeatures(
		SyncManifest manifest,
		HashSet<string> seenIds,
		string featuresDir,
		string completeDir,
		string archiveDir,
		SyncWriteResult result )
	{
		var toArchive = new List<string>();
		foreach ( KeyValuePair<string, SyncManifestEntry> pair in manifest.Features )
		{
			if ( !seenIds.Contains( pair.Key ) )
				toArchive.Add( pair.Key );
		}

		for ( int i = 0; i < toArchive.Count; i++ )
		{
			string id = toArchive[i];
			SyncManifestEntry entry = manifest.Features[id];
			string sourcePath = FindExistingFeatureFile( entry.FileName, featuresDir, completeDir );
			if ( !string.IsNullOrEmpty( sourcePath ) )
			{
				string destPath = Path.Combine( archiveDir, entry.FileName );
				if ( File.Exists( destPath ) )
				{
					string unique = Path.GetFileNameWithoutExtension( entry.FileName )
						+ "-" + id
						+ Path.GetExtension( entry.FileName );
					destPath = Path.Combine( archiveDir, unique );
				}

				EnsureDirectory( archiveDir );
				File.Move( sourcePath, destPath );
				result.FeaturesArchived++;
				result.Messages.Add( "Archived: FEATURES/" + ArchiveFolderName + "/" + Path.GetFileName( destPath ) );
			}

			manifest.Features.Remove( id );
		}
	}

	static string RelativeFeaturesPath( string absolutePath, string featuresDir )
	{
		string fullFeatures = Path.GetFullPath( featuresDir ).TrimEnd( Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar );
		string fullPath = Path.GetFullPath( absolutePath );
		if ( fullPath.StartsWith( fullFeatures, StringComparison.OrdinalIgnoreCase ) )
		{
			string relative = fullPath.Substring( fullFeatures.Length ).TrimStart( Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar );
			return ( "FEATURES/" + relative ).Replace( '\\', '/' );
		}

		return Path.GetFileName( absolutePath );
	}

	public static string ResolveOutputRootAbsolute( string projectRoot, string relativePath )
	{
		string relative = string.IsNullOrWhiteSpace( relativePath )
			? MilanoteSyncSettings.DefaultOutputRelativePath
			: relativePath.Trim().Replace( '\\', '/' );
		return Path.GetFullPath( Path.Combine( projectRoot, relative ) );
	}

	static void EnsureDirectory( string path )
	{
		if ( !Directory.Exists( path ) )
			Directory.CreateDirectory( path );
	}

	static void WriteTextIfChanged( string path, string content )
	{
		string normalized = content.Replace( "\r\n", "\n" ).Replace( '\r', '\n' );
		if ( File.Exists( path ) )
		{
			string existing = File.ReadAllText( path, Encoding.UTF8 ).Replace( "\r\n", "\n" ).Replace( '\r', '\n' );
			if ( string.Equals( existing, normalized, StringComparison.Ordinal ) )
				return;
		}

		string directory = Path.GetDirectoryName( path );
		if ( !string.IsNullOrEmpty( directory ) && !Directory.Exists( directory ) )
			Directory.CreateDirectory( directory );

		File.WriteAllText( path, normalized, new UTF8Encoding( encoderShouldEmitUTF8Identifier: false ) );
	}
}
#endif
