#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// Loads generated Feature markdown under .cursor/milanote into an in-memory catalog.
/// </summary>
public static class FeatureMarkdownReader
{
	public static ProjectTasksCatalog Load( string outputRootAbsolute )
	{
		var catalog = new ProjectTasksCatalog
		{
			OutputRootAbsolute = outputRootAbsolute,
			LoadedAtUtc = DateTime.UtcNow
		};

		if ( string.IsNullOrWhiteSpace( outputRootAbsolute ) || !Directory.Exists( outputRootAbsolute ) )
		{
			catalog.LoadWarnings.Add( "Milanote output folder not found: " + outputRootAbsolute );
			return catalog;
		}

		string featuresDir = Path.Combine( outputRootAbsolute, "FEATURES" );
		if ( !Directory.Exists( featuresDir ) )
		{
			catalog.LoadWarnings.Add( "FEATURES folder not found under " + outputRootAbsolute );
			return catalog;
		}

		var files = new List<string>();
		CollectMarkdownFiles( featuresDir, files );

		var byCategory = new Dictionary<string, ImportedCategory>( StringComparer.OrdinalIgnoreCase );

		for ( int i = 0; i < files.Count; i++ )
		{
			string path = files[i];
			try
			{
				ImportedFeature feature = ParseFile( path, outputRootAbsolute );
				if ( feature == null || string.IsNullOrWhiteSpace( feature.Name ) )
					continue;

				catalog.AllFeatures.Add( feature );

				string categoryName = string.IsNullOrWhiteSpace( feature.Category )
					? "Uncategorized"
					: feature.Category.Trim();

				ImportedCategory category;
				if ( !byCategory.TryGetValue( categoryName, out category ) )
				{
					category = new ImportedCategory { Name = categoryName };
					byCategory[categoryName] = category;
					catalog.Categories.Add( category );
				}

				category.Features.Add( feature );
			}
			catch ( Exception ex )
			{
				catalog.LoadWarnings.Add( "Failed to parse " + path + ": " + ex.Message );
			}
		}

		catalog.Categories.Sort( ( a, b ) =>
		{
			long scoreA = a.Features.Count > 0 ? a.Features[0].CategorySortScore : 0;
			long scoreB = b.Features.Count > 0 ? b.Features[0].CategorySortScore : 0;
			int indexA = a.Features.Count > 0 ? a.Features[0].CategorySortIndex : -1;
			int indexB = b.Features.Count > 0 ? b.Features[0].CategorySortIndex : -1;
			int cmp = KeywordHierarchyParser.CompareSortOrder(
				scoreA, indexA, a.Name, scoreB, indexB, b.Name );
			if ( cmp != 0 )
				return cmp;
			return string.Compare( a.Name, b.Name, StringComparison.OrdinalIgnoreCase );
		} );
		for ( int i = 0; i < catalog.Categories.Count; i++ )
		{
			catalog.Categories[i].Features.Sort( ( a, b ) =>
				KeywordHierarchyParser.CompareSortOrder(
					a.SortScore, a.SortIndex, a.Name,
					b.SortScore, b.SortIndex, b.Name ) );
		}

		catalog.AllFeatures.Sort( ( a, b ) =>
		{
			int cat = KeywordHierarchyParser.CompareSortOrder(
				a.CategorySortScore, a.CategorySortIndex, a.Category,
				b.CategorySortScore, b.CategorySortIndex, b.Category );
			if ( cat != 0 )
				return cat;
			return KeywordHierarchyParser.CompareSortOrder(
				a.SortScore, a.SortIndex, a.Name,
				b.SortScore, b.SortIndex, b.Name );
		} );

		return catalog;
	}

	static void CollectMarkdownFiles( string featuresDir, List<string> files )
	{
		string[] rootFiles = Directory.GetFiles( featuresDir, "*.md", SearchOption.TopDirectoryOnly );
		for ( int i = 0; i < rootFiles.Length; i++ )
			files.Add( rootFiles[i] );

		string completeDir = Path.Combine( featuresDir, IncrementalFileWriter.CompleteFolderName );
		if ( Directory.Exists( completeDir ) )
		{
			string[] completeFiles = Directory.GetFiles( completeDir, "*.md", SearchOption.TopDirectoryOnly );
			for ( int i = 0; i < completeFiles.Length; i++ )
				files.Add( completeFiles[i] );
		}
	}

	public static ImportedFeature ParseFile( string absolutePath, string outputRootAbsolute )
	{
		string text = File.ReadAllText( absolutePath, Encoding.UTF8 );
		text = text.Replace( "\r\n", "\n" ).Replace( '\r', '\n' );

		var feature = new ImportedFeature
		{
			AbsolutePath = absolutePath,
			RelativePath = MakeRelative( outputRootAbsolute, absolutePath ),
			IsCompleteFolder = absolutePath.IndexOf(
				Path.DirectorySeparatorChar + IncrementalFileWriter.CompleteFolderName + Path.DirectorySeparatorChar,
				StringComparison.OrdinalIgnoreCase ) >= 0
				|| absolutePath.IndexOf(
					"/" + IncrementalFileWriter.CompleteFolderName + "/",
					StringComparison.OrdinalIgnoreCase ) >= 0
		};

		string[] lines = text.Split( '\n' );
		if ( lines.Length == 0 )
			return feature;

		// Title
		for ( int i = 0; i < lines.Length; i++ )
		{
			string line = lines[i].Trim();
			if ( line.StartsWith( "# ", StringComparison.Ordinal ) )
			{
				feature.Name = line.Substring( 2 ).Trim();
				break;
			}
		}

		feature.Category = ReadLabeledBlock( lines, "Category" );
		feature.Status = ReadLabeledBlock( lines, "Status" );

		ParseBulletSection( lines, "Requirements", feature.Requirements, feature.AllTasks, "Requirements" );
		ParseBulletSection( lines, "Tasks", feature.Tasks, feature.AllTasks, "Tasks" );
		ParseBulletSection( lines, "Notes", feature.Notes, feature.AllTasks, "Notes" );
		ParseBulletSection( lines, "Bugs", feature.Bugs, feature.AllTasks, "Bugs" );
		if ( feature.Bugs.Count == 0 )
			ParseBulletSection( lines, "Known Issues", feature.Bugs, feature.AllTasks, "Bugs" );
		ParseBulletSection( lines, "Review", feature.Review, allTasks: null, "Review" );
		ParseBulletSection( lines, "AI Context", feature.AiContext, feature.AllTasks, "AI Context" );

		Dictionary<string, string> cursorSections = FeatureMarkdownMerge.ExtractCursorSections( text );
		feature.CursorImplementation = GetSectionBody( cursorSections, "Cursor Implementation" );
		feature.FilesModified = GetSectionBody( cursorSections, "Files Modified" );
		feature.TestingInstructions = GetSectionBody( cursorSections, "Testing Instructions" );
		feature.CursorNotes = GetSectionBody( cursorSections, "Cursor Notes" );
		feature.DeveloperVerificationMarkdown = GetSectionBody( cursorSections, "Developer Verification" );

		feature.FeatureId = ReadMetadataValue( lines, "Milanote ID:" );
		feature.LastSync = ReadMetadataValue( lines, "Last Sync:" );
		feature.CategorySortScore = ReadMetadataLong( lines, "Category Sort Score:" );
		feature.CategorySortIndex = ReadMetadataInt( lines, "Category Sort Index:", -1 );
		feature.SortScore = ReadMetadataLong( lines, "Sort Score:" );
		feature.SortIndex = ReadMetadataInt( lines, "Sort Index:", -1 );

		if ( string.IsNullOrWhiteSpace( feature.FeatureId ) )
			feature.FeatureId = "path:" + feature.RelativePath.Replace( '\\', '/' );

		return feature;
	}

	static string GetSectionBody( Dictionary<string, string> sections, string heading )
	{
		string block;
		if ( !sections.TryGetValue( heading, out block ) || string.IsNullOrEmpty( block ) )
			return string.Empty;

		string normalized = block.Replace( "\r\n", "\n" );
		string marker = "## " + heading;
		int idx = normalized.IndexOf( marker, StringComparison.OrdinalIgnoreCase );
		if ( idx < 0 )
			return normalized.Trim();

		string body = normalized.Substring( idx + marker.Length ).TrimStart( '\n', '\r' );
		return body.TrimEnd() + ( string.IsNullOrEmpty( body ) ? string.Empty : "\n" );
	}

	static void ParseBulletSection(
		string[] lines,
		string heading,
		List<string> target,
		List<ImportedTask> allTasks,
		string sectionName )
	{
		int start = IndexOfHeading( lines, heading );
		if ( start < 0 )
			return;

		for ( int i = start + 1; i < lines.Length; i++ )
		{
			string line = lines[i];
			string trimmed = line.Trim();
			if ( trimmed.StartsWith( "## ", StringComparison.Ordinal ) )
				break;
			if ( !trimmed.StartsWith( "- ", StringComparison.Ordinal ) )
				continue;

			string text = trimmed.Substring( 2 ).Trim();
			bool isComplete = false;
			if ( text.Length >= 3 && text[0] == '[' && text[2] == ']' )
			{
				char mark = text[1];
				isComplete = mark == 'x' || mark == 'X';
				if ( isComplete || mark == ' ' )
					text = text.Substring( 3 ).Trim();
			}

			if ( string.IsNullOrEmpty( text ) )
				continue;

			target.Add( text );
			if ( allTasks != null )
			{
				allTasks.Add( new ImportedTask
				{
					Section = sectionName,
					Text = text,
					Key = sectionName + "|" + text,
					IsComplete = isComplete
				} );
			}
		}
	}

	static string ReadLabeledBlock( string[] lines, string label )
	{
		for ( int i = 0; i < lines.Length; i++ )
		{
			if ( !string.Equals( lines[i].Trim(), label, StringComparison.OrdinalIgnoreCase ) )
				continue;

			for ( int j = i + 1; j < lines.Length; j++ )
			{
				string value = lines[j].Trim();
				if ( string.IsNullOrEmpty( value ) )
					continue;
				if ( value.StartsWith( "#", StringComparison.Ordinal ) )
					break;
				return value;
			}
		}

		return string.Empty;
	}

	static string ReadMetadataValue( string[] lines, string label )
	{
		for ( int i = 0; i < lines.Length; i++ )
		{
			if ( !string.Equals( lines[i].Trim(), label, StringComparison.OrdinalIgnoreCase ) )
				continue;

			for ( int j = i + 1; j < lines.Length; j++ )
			{
				string value = lines[j].Trim();
				if ( string.IsNullOrEmpty( value ) )
					continue;
				if ( value.StartsWith( "#", StringComparison.Ordinal ) )
					break;
				return value;
			}
		}

		return string.Empty;
	}

	static long ReadMetadataLong( string[] lines, string label )
	{
		string value = ReadMetadataValue( lines, label );
		if ( long.TryParse( value, out long parsed ) )
			return parsed;
		return 0;
	}

	static int ReadMetadataInt( string[] lines, string label, int fallback )
	{
		string value = ReadMetadataValue( lines, label );
		if ( int.TryParse( value, out int parsed ) )
			return parsed;
		return fallback;
	}

	static int IndexOfHeading( string[] lines, string heading )
	{
		string marker = "## " + heading;
		for ( int i = 0; i < lines.Length; i++ )
		{
			if ( string.Equals( lines[i].Trim(), marker, StringComparison.OrdinalIgnoreCase ) )
				return i;
		}

		return -1;
	}

	static string MakeRelative( string root, string absolute )
	{
		try
		{
			string fullRoot = Path.GetFullPath( root )
				.TrimEnd( Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar );
			string fullPath = Path.GetFullPath( absolute );
			if ( fullPath.StartsWith( fullRoot, StringComparison.OrdinalIgnoreCase ) )
			{
				return fullPath.Substring( fullRoot.Length )
					.TrimStart( Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar )
					.Replace( '\\', '/' );
			}
		}
		catch
		{
			// fall through
		}

		return Path.GetFileName( absolute );
	}
}
#endif
