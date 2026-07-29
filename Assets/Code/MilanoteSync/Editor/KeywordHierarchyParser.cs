#if UNITY_EDITOR
using System;
using System.Collections.Generic;

/// <summary>
/// Builds Category → Feature → Tasks from ordered todo items using
/// Category: / Feature: keywords (used for markdown export; API uses parentId in HierarchyParser).
/// Indentation is ignored — document order only.
/// </summary>
public static class KeywordHierarchyParser
{
	public const string CategoryPrefix = "Category:";
	public const string FeaturePrefix = "Feature:";
	public const string BugsName = "Bugs";
	public const string UncategorizedName = "Uncategorized";

	public sealed class Item
	{
		public string Id;
		public string Text;
		public bool IsComplete;
	}

	public static void ParseInto( ProjectBoard project, IReadOnlyList<Item> items )
	{
		if ( project == null )
			throw new ArgumentNullException( nameof( project ) );
		if ( items == null || items.Count == 0 )
			return;

		ProjectCategory currentCategory = null;
		ProjectFeature currentFeature = null;
		var currentRawTasks = new List<ProjectTaskItem>();
		var bugsRawByFeature = new Dictionary<ProjectFeature, List<ProjectTaskItem>>();

		Action flushCurrentFeature = () =>
		{
			if ( currentFeature == null )
				return;
			TaskPrefixParser.Categorise( currentFeature, currentRawTasks );
			currentFeature.Status = FeatureStatusDeriver.Derive( currentFeature );
			currentFeature = null;
			currentRawTasks = new List<ProjectTaskItem>();
		};

		for ( int i = 0; i < items.Count; i++ )
		{
			Item item = items[i];
			if ( item == null || string.IsNullOrWhiteSpace( item.Text ) )
				continue;

			string text = item.Text.Trim();
			string id = string.IsNullOrEmpty( item.Id ) ? "item-" + i : item.Id;

			string categoryName;
			if ( TryStripPrefix( text, CategoryPrefix, out categoryName ) )
			{
				flushCurrentFeature();
				currentCategory = GetOrCreateCategory( project, "cat-" + SanitizeId( categoryName ), categoryName );
				continue;
			}

			string featureName;
			if ( TryStripPrefix( text, FeaturePrefix, out featureName ) )
			{
				flushCurrentFeature();
				if ( currentCategory == null )
				{
					currentCategory = GetOrCreateCategory( project, "uncategorized", UncategorizedName );
					project.Warnings.Add(
						"Feature '" + featureName + "' appeared before any Category:; using Uncategorized." );
				}

				currentFeature = GetOrCreateNamedFeature( currentCategory, id, featureName, item.IsComplete );
				currentRawTasks = new List<ProjectTaskItem>();
				continue;
			}

			var taskItem = new ProjectTaskItem
			{
				Id = id,
				Text = text,
				IsComplete = item.IsComplete
			};

			if ( currentFeature != null )
			{
				currentRawTasks.Add( taskItem );
				continue;
			}

			if ( currentCategory != null )
			{
				ProjectFeature bugs = GetOrCreateBugsFeature( currentCategory );
				GetBugsList( bugsRawByFeature, bugs ).Add( taskItem );
				continue;
			}

			ProjectCategory overallBugsCat = GetOrCreateCategory( project, "cat-bugs", BugsName );
			ProjectFeature overallBugs = GetOrCreateBugsFeature( overallBugsCat );
			GetBugsList( bugsRawByFeature, overallBugs ).Add( taskItem );
		}

		flushCurrentFeature();

		foreach ( KeyValuePair<ProjectFeature, List<ProjectTaskItem>> pair in bugsRawByFeature )
		{
			TaskPrefixParser.Categorise( pair.Key, pair.Value );
			pair.Key.Status = FeatureStatusDeriver.Derive( pair.Key );
		}
	}

	static List<ProjectTaskItem> GetBugsList(
		Dictionary<ProjectFeature, List<ProjectTaskItem>> map,
		ProjectFeature bugsFeature )
	{
		List<ProjectTaskItem> list;
		if ( !map.TryGetValue( bugsFeature, out list ) || list == null )
		{
			list = new List<ProjectTaskItem>();
			map[bugsFeature] = list;
		}

		return list;
	}

	public static bool TryStripPrefix( string text, string prefix, out string remainder )
	{
		remainder = null;
		if ( string.IsNullOrEmpty( text ) || string.IsNullOrEmpty( prefix ) )
			return false;
		if ( !text.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) )
			return false;

		remainder = text.Substring( prefix.Length ).Trim();
		if ( string.IsNullOrEmpty( remainder ) )
			remainder = prefix.TrimEnd( ':' ).Trim();
		return true;
	}

	public static ProjectCategory GetOrCreateCategory(
		ProjectBoard project,
		string id,
		string name,
		long sortScore = 0,
		int sortIndex = -1 )
	{
		for ( int i = 0; i < project.Categories.Count; i++ )
		{
			if ( string.Equals( project.Categories[i].Name, name, StringComparison.OrdinalIgnoreCase ) )
				return project.Categories[i];
		}

		var category = new ProjectCategory
		{
			Id = id,
			Name = name,
			SortScore = sortScore,
			SortIndex = sortIndex
		};
		project.Categories.Add( category );
		return category;
	}

	public static ProjectFeature GetOrCreateNamedFeature(
		ProjectCategory category,
		string id,
		string name,
		bool isComplete,
		long sortScore = 0,
		int sortIndex = -1 )
	{
		for ( int i = 0; i < category.Features.Count; i++ )
		{
			if ( string.Equals( category.Features[i].Name, name, StringComparison.OrdinalIgnoreCase ) )
			{
				if ( isComplete )
					category.Features[i].IsComplete = true;
				return category.Features[i];
			}
		}

		var feature = new ProjectFeature
		{
			Id = id,
			Name = name,
			CategoryName = category.Name,
			IsComplete = isComplete,
			SortScore = sortScore,
			SortIndex = sortIndex,
			CategorySortScore = category.SortScore,
			CategorySortIndex = category.SortIndex
		};
		category.Features.Add( feature );
		return feature;
	}

	public static ProjectFeature GetOrCreateBugsFeature( ProjectCategory category )
	{
		return GetOrCreateNamedFeature(
			category,
			"bugs-" + SanitizeId( category.Name ),
			BugsName,
			false,
			long.MaxValue,
			int.MaxValue );
	}

	public static int CompareSortOrder( long scoreA, int indexA, string idA, long scoreB, int indexB, string idB )
	{
		int cmp = scoreA.CompareTo( scoreB );
		if ( cmp != 0 )
			return cmp;
		cmp = indexA.CompareTo( indexB );
		if ( cmp != 0 )
			return cmp;
		return string.CompareOrdinal( idA ?? string.Empty, idB ?? string.Empty );
	}

	public static void SortBoard( ProjectBoard project )
	{
		if ( project == null )
			return;

		project.Categories.Sort( ( a, b ) =>
			CompareSortOrder( a.SortScore, a.SortIndex, a.Id, b.SortScore, b.SortIndex, b.Id ) );

		for ( int i = 0; i < project.Categories.Count; i++ )
		{
			ProjectCategory category = project.Categories[i];
			category.Features.Sort( ( a, b ) =>
				CompareSortOrder( a.SortScore, a.SortIndex, a.Id, b.SortScore, b.SortIndex, b.Id ) );
		}
	}

	static string SanitizeId( string text )
	{
		if ( string.IsNullOrEmpty( text ) )
			return "item";

		var chars = new char[Math.Min( text.Length, 48 )];
		int n = 0;
		for ( int i = 0; i < text.Length && n < chars.Length; i++ )
		{
			char c = text[i];
			if ( char.IsLetterOrDigit( c ) )
				chars[n++] = char.ToLowerInvariant( c );
			else if ( c == ' ' || c == '-' || c == '_' )
				chars[n++] = '-';
		}

		string result = new string( chars, 0, n ).Trim( '-' );
		return string.IsNullOrEmpty( result ) ? "item" : result;
	}
}
#endif
