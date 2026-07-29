#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// Generates importer-owned Feature / CURRENT / INDEX markdown.
/// Cursor-owned sections are merged separately by <see cref="FeatureMarkdownMerge"/>.
/// </summary>
public static class MarkdownGenerator
{
	public static string GenerateFeature( ProjectFeature feature, DateTime syncUtc )
	{
		if ( feature == null )
			throw new ArgumentNullException( nameof( feature ) );

		var sb = new StringBuilder();
		sb.Append( "# " ).Append( SanitizeInline( feature.Name ) ).Append( '\n' );
		sb.Append( '\n' );

		sb.Append( "Category\n" );
		sb.Append( '\n' );
		sb.Append( SanitizeInline( feature.CategoryName ) ).Append( '\n' );
		sb.Append( '\n' );

		sb.Append( "Status\n" );
		sb.Append( '\n' );
		sb.Append( FeatureStatusDeriver.ToDisplayString( feature.Status ) ).Append( '\n' );

		AppendSection( sb, "Requirements", feature.Requirements );
		AppendSection( sb, "Tasks", feature.Tasks );
		AppendSection( sb, "Notes", feature.Notes );
		AppendSection( sb, "Bugs", feature.Bugs );
		AppendSection( sb, "Review", feature.Review );
		AppendSection( sb, "AI Context", feature.AiContext );

		sb.Append( '\n' );
		sb.Append( "## Metadata\n" );
		sb.Append( '\n' );
		sb.Append( "Milanote ID:\n" );
		sb.Append( feature.Id ?? string.Empty ).Append( '\n' );
		sb.Append( '\n' );
		sb.Append( "Last Sync:\n" );
		sb.Append( syncUtc.ToString( "yyyy-MM-dd", CultureInfo.InvariantCulture ) ).Append( '\n' );
		sb.Append( '\n' );
		sb.Append( "Category Sort Score:\n" );
		sb.Append( feature.CategorySortScore.ToString( CultureInfo.InvariantCulture ) ).Append( '\n' );
		sb.Append( '\n' );
		sb.Append( "Category Sort Index:\n" );
		sb.Append( feature.CategorySortIndex.ToString( CultureInfo.InvariantCulture ) ).Append( '\n' );
		sb.Append( '\n' );
		sb.Append( "Sort Score:\n" );
		sb.Append( feature.SortScore.ToString( CultureInfo.InvariantCulture ) ).Append( '\n' );
		sb.Append( '\n' );
		sb.Append( "Sort Index:\n" );
		sb.Append( feature.SortIndex.ToString( CultureInfo.InvariantCulture ) ).Append( '\n' );

		if ( !string.IsNullOrWhiteSpace( feature.Priority ) )
		{
			sb.Append( '\n' );
			sb.Append( "Priority:\n" );
			sb.Append( SanitizeInline( feature.Priority ) ).Append( '\n' );
		}

		if ( !string.IsNullOrWhiteSpace( feature.Owner ) )
		{
			sb.Append( '\n' );
			sb.Append( "Owner:\n" );
			sb.Append( SanitizeInline( feature.Owner ) ).Append( '\n' );
		}

		if ( feature.Labels != null && feature.Labels.Count > 0 )
		{
			sb.Append( '\n' );
			sb.Append( "Labels:\n" );
			for ( int i = 0; i < feature.Labels.Count; i++ )
			{
				if ( !string.IsNullOrWhiteSpace( feature.Labels[i] ) )
					sb.Append( "- " ).Append( SanitizeInline( feature.Labels[i] ) ).Append( '\n' );
			}
		}

		return NormalizeNewlines( sb.ToString() );
	}

	public static string GenerateCurrent( ProjectBoard board, Func<ProjectFeature, string> relativeLinkForFeature )
	{
		if ( board == null )
			throw new ArgumentNullException( nameof( board ) );

		var ready = new List<ProjectFeature>();
		var inProgress = new List<ProjectFeature>();

		foreach ( ProjectCategory category in board.Categories )
		{
			foreach ( ProjectFeature feature in category.Features )
			{
				if ( feature.Status == FeatureStatus.Ready )
					ready.Add( feature );
				else if ( feature.Status == FeatureStatus.InProgress )
					inProgress.Add( feature );
			}
		}

		// Preserve Milanote board order (already sorted on the ProjectBoard).
		var sb = new StringBuilder();
		sb.Append( "# Current Work\n" );
		sb.Append( '\n' );
		sb.Append( "Active features from Milanote. Edit tasks in Milanote only.\n" );
		sb.Append( '\n' );

		sb.Append( "## In Progress\n" );
		sb.Append( '\n' );
		AppendFeatureLinks( sb, inProgress, relativeLinkForFeature );

		sb.Append( "## Ready\n" );
		sb.Append( '\n' );
		AppendFeatureLinks( sb, ready, relativeLinkForFeature );

		return NormalizeNewlines( sb.ToString() );
	}

	public static string GenerateIndex( ProjectBoard board, Func<ProjectFeature, string> displayNameForFeature )
	{
		if ( board == null )
			throw new ArgumentNullException( nameof( board ) );

		var sb = new StringBuilder();
		sb.Append( "# Feature Index\n" );
		sb.Append( '\n' );

		// Preserve Milanote category / feature order from the board.
		for ( int c = 0; c < board.Categories.Count; c++ )
		{
			ProjectCategory category = board.Categories[c];
			sb.Append( SanitizeInline( category.Name ) ).Append( '\n' );
			sb.Append( '\n' );

			if ( category.Features.Count == 0 )
			{
				sb.Append( "- _(none)_\n" );
			}
			else
			{
				for ( int i = 0; i < category.Features.Count; i++ )
				{
					string label = displayNameForFeature != null
						? displayNameForFeature( category.Features[i] )
						: category.Features[i].Name;
					sb.Append( "- " ).Append( SanitizeInline( label ) ).Append( '\n' );
				}
			}

			sb.Append( '\n' );
		}

		return NormalizeNewlines( sb.ToString().TrimEnd( '\n' ) + "\n" );
	}

	/// <summary>
	/// Stable hash input for incremental writes (excludes Last Sync date).
	/// Format version forces rewrite when markdown bullet shape changes (e.g. [x]/[ ]).
	/// </summary>
	public static string BuildContentFingerprint( ProjectFeature feature )
	{
		var sb = new StringBuilder();
		sb.Append( "fmt=checklist-v1" ).Append( '\n' );
		sb.Append( feature.Name ?? "" ).Append( '\n' );
		sb.Append( feature.CategoryName ?? "" ).Append( '\n' );
		sb.Append( feature.IsComplete ? "1" : "0" ).Append( '\n' );
		sb.Append( FeatureStatusDeriver.ToDisplayString( feature.Status ) ).Append( '\n' );
		sb.Append( feature.CategorySortScore ).Append( '|' ).Append( feature.CategorySortIndex ).Append( '\n' );
		sb.Append( feature.SortScore ).Append( '|' ).Append( feature.SortIndex ).Append( '\n' );
		AppendFingerprintList( sb, "R", feature.Requirements );
		AppendFingerprintList( sb, "T", feature.Tasks );
		AppendFingerprintList( sb, "N", feature.Notes );
		AppendFingerprintList( sb, "B", feature.Bugs );
		AppendFingerprintList( sb, "V", feature.Review );
		AppendFingerprintList( sb, "A", feature.AiContext );
		sb.Append( feature.Priority ?? "" ).Append( '\n' );
		sb.Append( feature.Owner ?? "" ).Append( '\n' );
		if ( feature.Labels != null )
		{
			for ( int i = 0; i < feature.Labels.Count; i++ )
				sb.Append( feature.Labels[i] ?? "" ).Append( '\n' );
		}

		return sb.ToString();
	}

	static void AppendFingerprintList( StringBuilder sb, string tag, List<ProjectTaskItem> items )
	{
		sb.Append( tag ).Append( '\n' );
		if ( items == null )
			return;
		for ( int i = 0; i < items.Count; i++ )
		{
			ProjectTaskItem item = items[i];
			sb.Append( item.IsComplete ? '1' : '0' ).Append( '|' ).Append( item.Text ?? "" ).Append( '\n' );
		}
	}

	static void AppendSection( StringBuilder sb, string heading, List<ProjectTaskItem> items )
	{
		if ( items == null || items.Count == 0 )
			return;

		sb.Append( '\n' );
		sb.Append( "## " ).Append( heading ).Append( '\n' );
		sb.Append( '\n' );
		for ( int i = 0; i < items.Count; i++ )
		{
			sb.Append( items[i].IsComplete ? "- [x] " : "- [ ] " )
				.Append( SanitizeInline( items[i].Text ) )
				.Append( '\n' );
		}
	}

	static void AppendFeatureLinks(
		StringBuilder sb,
		List<ProjectFeature> features,
		Func<ProjectFeature, string> relativeLinkForFeature )
	{
		if ( features.Count == 0 )
		{
			sb.Append( "- _(none)_\n" );
			sb.Append( '\n' );
			return;
		}

		for ( int i = 0; i < features.Count; i++ )
		{
			ProjectFeature feature = features[i];
			string link = relativeLinkForFeature != null
				? relativeLinkForFeature( feature )
				: feature.Name;
			sb.Append( "- [" )
				.Append( SanitizeInline( feature.Name ) )
				.Append( "](" )
				.Append( link )
				.Append( ") (" )
				.Append( SanitizeInline( feature.CategoryName ) )
				.Append( ")\n" );
		}

		sb.Append( '\n' );
	}

	static int CompareFeatures( ProjectFeature a, ProjectFeature b )
	{
		int cmp = KeywordHierarchyParser.CompareSortOrder(
			a.CategorySortScore, a.CategorySortIndex, a.CategoryName,
			b.CategorySortScore, b.CategorySortIndex, b.CategoryName );
		if ( cmp != 0 )
			return cmp;
		return KeywordHierarchyParser.CompareSortOrder(
			a.SortScore, a.SortIndex, a.Id,
			b.SortScore, b.SortIndex, b.Id );
	}

	static string SanitizeInline( string value )
	{
		if ( string.IsNullOrEmpty( value ) )
			return string.Empty;
		return value.Replace( "\r\n", " " ).Replace( '\n', ' ' ).Replace( '\r', ' ' ).Trim();
	}

	static string NormalizeNewlines( string text )
	{
		return text.Replace( "\r\n", "\n" ).Replace( '\r', '\n' );
	}
}
#endif
