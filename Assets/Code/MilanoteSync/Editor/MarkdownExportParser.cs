#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

/// <summary>
/// Parses a Milanote board markdown export into Category:/Feature: keyword hierarchy.
/// Only checklist content under headings containing "Current Phase" is imported.
/// </summary>
public static class MarkdownExportParser
{
	public const string CurrentPhaseMarker = "Current Phase";

	static readonly Regex HeadingRegex = new Regex(
		@"^#{1,6}\s+(?<title>.+?)\s*$",
		RegexOptions.Compiled );

	static readonly Regex ChecklistRegex = new Regex(
		@"^(?<indent>[ \t]*)[-*+]\s+\[(?<check>[ xX])\]\s+(?<text>.+?)\s*$",
		RegexOptions.Compiled );

	public static ProjectBoard ParseFile( string absolutePath )
	{
		if ( string.IsNullOrWhiteSpace( absolutePath ) || !File.Exists( absolutePath ) )
			throw new FileNotFoundException( "Markdown export not found.", absolutePath );

		string text = File.ReadAllText( absolutePath );
		return Parse( text, Path.GetFileNameWithoutExtension( absolutePath ), requireCurrentPhase: true );
	}

	public static ProjectBoard Parse( string markdown, string rootTitle )
	{
		return Parse( markdown, rootTitle, requireCurrentPhase: true );
	}

	/// <param name="requireCurrentPhase">
	/// When true (Milanote export), only checklist content under "Current Phase" headings is imported.
	/// When false (embedded document fallback), all checklist items are imported.
	/// </param>
	public static ProjectBoard Parse( string markdown, string rootTitle, bool requireCurrentPhase )
	{
		var project = new ProjectBoard
		{
			RootBoardId = "markdown-export",
			RootTitle = string.IsNullOrWhiteSpace( rootTitle ) ? "Markdown Export" : rootTitle.Trim()
		};

		if ( string.IsNullOrEmpty( markdown ) )
		{
			project.Warnings.Add( "Markdown export was empty." );
			return project;
		}

		string[] lines = markdown.Replace( "\r\n", "\n" ).Replace( '\r', '\n' ).Split( '\n' );
		var items = new List<KeywordHierarchyParser.Item>();
		int itemIndex = 0;
		bool inCurrentPhase = !requireCurrentPhase;
		int currentPhaseHeadings = 0;
		int skippedChecklists = 0;

		for ( int i = 0; i < lines.Length; i++ )
		{
			string line = lines[i];

			Match heading = HeadingRegex.Match( line );
			if ( heading.Success )
			{
				string title = heading.Groups["title"].Value.Trim();
				if ( string.IsNullOrEmpty( title ) )
					continue;

				if ( requireCurrentPhase )
				{
					inCurrentPhase = title.IndexOf( CurrentPhaseMarker, StringComparison.OrdinalIgnoreCase ) >= 0;
					if ( !inCurrentPhase )
						continue;

					currentPhaseHeadings++;
					itemIndex++;
					items.Add( new KeywordHierarchyParser.Item
					{
						Id = "md-heading-" + itemIndex.ToString( "0000" ),
						Text = KeywordHierarchyParser.CategoryPrefix + " " + title,
						IsComplete = false
					} );
					continue;
				}

				// Embedded / unconstrained parse: Phase headings become categories when present.
				if ( title.StartsWith( "Phase", StringComparison.OrdinalIgnoreCase )
				     || title.IndexOf( CurrentPhaseMarker, StringComparison.OrdinalIgnoreCase ) >= 0 )
				{
					itemIndex++;
					items.Add( new KeywordHierarchyParser.Item
					{
						Id = "md-heading-" + itemIndex.ToString( "0000" ),
						Text = KeywordHierarchyParser.CategoryPrefix + " " + title,
						IsComplete = false
					} );
				}

				continue;
			}

			Match checklist = ChecklistRegex.Match( line );
			if ( !checklist.Success )
				continue;

			if ( !inCurrentPhase )
			{
				skippedChecklists++;
				continue;
			}

			string text = checklist.Groups["text"].Value.Trim();
			if ( string.IsNullOrEmpty( text ) )
				continue;

			bool isComplete = !string.IsNullOrWhiteSpace( checklist.Groups["check"].Value )
			                  && checklist.Groups["check"].Value.Trim().ToLowerInvariant() == "x";
			itemIndex++;

			items.Add( new KeywordHierarchyParser.Item
			{
				Id = "md-" + itemIndex.ToString( "0000" ),
				Text = text,
				IsComplete = isComplete
			} );
		}

		if ( requireCurrentPhase && currentPhaseHeadings == 0 )
		{
			project.Warnings.Add(
				"Markdown export has no headings containing '" + CurrentPhaseMarker
				+ "'. Skipped " + skippedChecklists + " checklist item(s). No export todos imported." );
			return project;
		}

		int keywordCategories = CountKeywordPrefix( items, KeywordHierarchyParser.CategoryPrefix );
		int keywordFeatures = CountKeywordPrefix( items, KeywordHierarchyParser.FeaturePrefix );
		if ( requireCurrentPhase )
		{
			project.Warnings.Add(
				"Markdown Current Phase headings=" + currentPhaseHeadings
				+ ", checklist items=" + ( items.Count - currentPhaseHeadings )
				+ ", skipped outside Current Phase=" + skippedChecklists
				+ ", Category:=" + keywordCategories
				+ ", Feature:=" + keywordFeatures + "." );
		}
		else
		{
			project.Warnings.Add(
				"Markdown checklist items=" + items.Count
				+ ", Category:=" + keywordCategories
				+ ", Feature:=" + keywordFeatures + "." );
		}

		KeywordHierarchyParser.ParseInto( project, items );

		if ( CountFeatures( project ) == 0 )
			project.Warnings.Add(
				"Markdown export contained no Category:/Feature: hierarchy"
				+ ( requireCurrentPhase ? " under Current Phase" : "" )
				+ ". Prefix checklist items with 'Category:' and 'Feature:'." );

		return project;
	}

	public static int CountKeywordPrefix( IReadOnlyList<KeywordHierarchyParser.Item> items, string prefix )
	{
		int count = 0;
		if ( items == null )
			return 0;
		for ( int i = 0; i < items.Count; i++ )
		{
			if ( items[i] == null || string.IsNullOrEmpty( items[i].Text ) )
				continue;
			if ( items[i].Text.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) )
				count++;
		}

		return count;
	}

	static int CountFeatures( ProjectBoard project )
	{
		int count = 0;
		for ( int i = 0; i < project.Categories.Count; i++ )
			count += project.Categories[i].Features.Count;
		return count;
	}
}
#endif
