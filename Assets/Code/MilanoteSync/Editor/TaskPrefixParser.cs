#if UNITY_EDITOR
using System;
using System.Collections.Generic;

public enum TaskSectionKind
{
	Tasks,
	Requirements,
	Notes,
	Bugs,
	Review,
	AiContext
}

/// <summary>
/// Maps Milanote task text prefixes / Bug-Review parentId trees into Markdown sections.
/// </summary>
public static class TaskPrefixParser
{
	const string RequirementPrefix = "Requirement:";
	const string NotePrefix = "Note:";
	const string BugPrefix = "Bug:";
	const string BugsPrefix = "Bugs:";
	const string ReviewPrefix = "Review:";
	const string AiPrefix = "AI:";

	public static void Categorise( ProjectFeature feature, IReadOnlyList<ProjectTaskItem> rawItems )
	{
		if ( feature == null || rawItems == null )
			return;

		if ( HasAnyParentId( rawItems ) )
			CategoriseByParentId( feature, rawItems );
		else
			CategoriseSequential( feature, rawItems );
	}

	/// <summary>
	/// API path: Bug/Review tagged items include themselves and only descendants
	/// whose parentId chain reaches that tagged item.
	/// </summary>
	static void CategoriseByParentId( ProjectFeature feature, IReadOnlyList<ProjectTaskItem> rawItems )
	{
		var byId = new Dictionary<string, ProjectTaskItem>( StringComparer.Ordinal );
		for ( int i = 0; i < rawItems.Count; i++ )
		{
			ProjectTaskItem item = rawItems[i];
			if ( item == null || string.IsNullOrEmpty( item.Id ) )
				continue;
			byId[item.Id] = item;
		}

		var sectionRootKind = new Dictionary<string, TaskSectionKind>( StringComparer.Ordinal );
		for ( int i = 0; i < rawItems.Count; i++ )
		{
			ProjectTaskItem item = rawItems[i];
			if ( item == null || string.IsNullOrWhiteSpace( item.Text ) )
				continue;

			TaskSectionKind rootKind;
			if ( TryClassifySectionRoot( item.Text.Trim(), out rootKind ) )
				sectionRootKind[item.Id] = rootKind;
		}

		for ( int i = 0; i < rawItems.Count; i++ )
		{
			ProjectTaskItem source = rawItems[i];
			if ( source == null || string.IsNullOrWhiteSpace( source.Text ) )
				continue;

			string raw = source.Text.Trim();
			TaskSectionKind kind;
			string text;

			TaskSectionKind sectionKind;
			bool isSectionRoot;
			if ( TryFindBugOrReviewSection( source, byId, sectionRootKind, out sectionKind, out isSectionRoot ) )
			{
				kind = sectionKind;
				text = FormatSectionItemText( raw, sectionKind, isSectionRoot );
			}
			else if ( !TryClassifyFixedPrefix( raw, out kind, out text ) )
			{
				kind = TaskSectionKind.Tasks;
				text = raw;
			}

			if ( string.IsNullOrWhiteSpace( text ) )
				continue;

			AddToSection( feature, kind, new ProjectTaskItem
			{
				Id = source.Id,
				ParentId = source.ParentId,
				Text = text.Trim(),
				IsComplete = source.IsComplete
			} );
		}
	}

	/// <summary>
	/// Markdown export path (no parentId): sequential openers for Bug/Review.
	/// </summary>
	static void CategoriseSequential( ProjectFeature feature, IReadOnlyList<ProjectTaskItem> rawItems )
	{
		TaskSectionKind sectionMode = TaskSectionKind.Tasks;

		for ( int i = 0; i < rawItems.Count; i++ )
		{
			ProjectTaskItem source = rawItems[i];
			if ( source == null || string.IsNullOrWhiteSpace( source.Text ) )
				continue;

			string raw = source.Text.Trim();

			if ( IsExactOpener( raw, "Review" ) || IsEmptyPrefixedOpener( raw, ReviewPrefix ) )
			{
				sectionMode = TaskSectionKind.Review;
				continue;
			}

			if ( IsExactOpener( raw, "Bugs" ) || IsEmptyPrefixedOpener( raw, BugsPrefix ) )
			{
				sectionMode = TaskSectionKind.Bugs;
				continue;
			}

			TaskSectionKind kind;
			string text;
			if ( TryStripPrefix( raw, ReviewPrefix, out text ) )
			{
				kind = TaskSectionKind.Review;
				sectionMode = TaskSectionKind.Review;
			}
			else if ( TryStripPrefix( raw, BugsPrefix, out text ) )
			{
				kind = TaskSectionKind.Bugs;
				sectionMode = TaskSectionKind.Bugs;
			}
			else if ( TryStripPrefix( raw, BugPrefix, out text ) )
			{
				kind = TaskSectionKind.Bugs;
				sectionMode = TaskSectionKind.Bugs;
			}
			else if ( TryClassifyFixedPrefix( raw, out kind, out text ) )
			{
				sectionMode = TaskSectionKind.Tasks;
			}
			else
			{
				kind = sectionMode;
				text = raw;
			}

			if ( string.IsNullOrWhiteSpace( text ) )
				continue;

			AddToSection( feature, kind, new ProjectTaskItem
			{
				Id = source.Id,
				ParentId = source.ParentId,
				Text = text.Trim(),
				IsComplete = source.IsComplete
			} );
		}
	}

	/// <summary>
	/// Classifies a single line without section-opener state (used for diagnostics).
	/// </summary>
	public static void Parse( string rawText, out TaskSectionKind kind, out string text )
	{
		kind = TaskSectionKind.Tasks;
		text = rawText != null ? rawText.Trim() : string.Empty;

		TaskSectionKind rootKind;
		if ( TryClassifySectionRoot( text, out rootKind ) )
		{
			kind = rootKind;
			text = FormatSectionItemText( text, rootKind, isSectionRoot: true );
			return;
		}

		if ( !TryClassifyFixedPrefix( text, out kind, out text ) )
		{
			kind = TaskSectionKind.Tasks;
			text = rawText != null ? rawText.Trim() : string.Empty;
		}
	}

	static bool HasAnyParentId( IReadOnlyList<ProjectTaskItem> rawItems )
	{
		for ( int i = 0; i < rawItems.Count; i++ )
		{
			if ( rawItems[i] != null && !string.IsNullOrEmpty( rawItems[i].ParentId ) )
				return true;
		}

		return false;
	}

	static bool TryClassifySectionRoot( string raw, out TaskSectionKind kind )
	{
		kind = TaskSectionKind.Tasks;
		if ( string.IsNullOrEmpty( raw ) )
			return false;

		if ( IsExactOpener( raw, "Review" ) || IsEmptyPrefixedOpener( raw, ReviewPrefix )
		     || raw.StartsWith( ReviewPrefix, StringComparison.OrdinalIgnoreCase ) )
		{
			kind = TaskSectionKind.Review;
			return true;
		}

		if ( IsExactOpener( raw, "Bugs" ) || IsEmptyPrefixedOpener( raw, BugsPrefix )
		     || raw.StartsWith( BugsPrefix, StringComparison.OrdinalIgnoreCase )
		     || raw.StartsWith( BugPrefix, StringComparison.OrdinalIgnoreCase ) )
		{
			kind = TaskSectionKind.Bugs;
			return true;
		}

		return false;
	}

	static bool TryFindBugOrReviewSection(
		ProjectTaskItem source,
		Dictionary<string, ProjectTaskItem> byId,
		Dictionary<string, TaskSectionKind> sectionRootKind,
		out TaskSectionKind kind,
		out bool isSectionRoot )
	{
		kind = TaskSectionKind.Tasks;
		isSectionRoot = false;
		if ( source == null || string.IsNullOrEmpty( source.Id ) )
			return false;

		TaskSectionKind rootKind;
		if ( sectionRootKind.TryGetValue( source.Id, out rootKind ) )
		{
			kind = rootKind;
			isSectionRoot = true;
			return true;
		}

		string parentId = source.ParentId;
		var guard = new HashSet<string>( StringComparer.Ordinal );
		while ( !string.IsNullOrEmpty( parentId ) && guard.Add( parentId ) )
		{
			if ( sectionRootKind.TryGetValue( parentId, out rootKind ) )
			{
				kind = rootKind;
				isSectionRoot = false;
				return true;
			}

			ProjectTaskItem parent;
			if ( !byId.TryGetValue( parentId, out parent ) )
				return false;
			parentId = parent.ParentId;
		}

		return false;
	}

	static string FormatSectionItemText( string raw, TaskSectionKind kind, bool isSectionRoot )
	{
		if ( string.IsNullOrEmpty( raw ) )
			return string.Empty;

		string text = raw.Trim();
		string stripped;

		if ( kind == TaskSectionKind.Review )
		{
			if ( IsExactOpener( text, "Review" ) )
				return isSectionRoot ? "Review" : text;
			if ( TryStripPrefix( text, ReviewPrefix, out stripped ) )
				return stripped;
			return text;
		}

		if ( kind == TaskSectionKind.Bugs )
		{
			if ( IsExactOpener( text, "Bugs" ) )
				return isSectionRoot ? string.Empty : text;
			if ( TryStripPrefix( text, BugsPrefix, out stripped ) )
				return stripped;
			if ( TryStripPrefix( text, BugPrefix, out stripped ) )
				return stripped;
			return text;
		}

		return text;
	}

	static bool TryClassifyFixedPrefix( string raw, out TaskSectionKind kind, out string text )
	{
		kind = TaskSectionKind.Tasks;
		text = raw;

		string stripped;
		if ( TryStripPrefix( raw, RequirementPrefix, out stripped ) )
		{
			kind = TaskSectionKind.Requirements;
			text = stripped;
			return true;
		}

		if ( TryStripPrefix( raw, NotePrefix, out stripped ) )
		{
			kind = TaskSectionKind.Notes;
			text = stripped;
			return true;
		}

		if ( TryStripPrefix( raw, AiPrefix, out stripped ) )
		{
			kind = TaskSectionKind.AiContext;
			text = stripped;
			return true;
		}

		return false;
	}

	static void AddToSection( ProjectFeature feature, TaskSectionKind kind, ProjectTaskItem item )
	{
		switch ( kind )
		{
			case TaskSectionKind.Requirements:
				feature.Requirements.Add( item );
				break;
			case TaskSectionKind.Notes:
				feature.Notes.Add( item );
				break;
			case TaskSectionKind.Bugs:
				feature.Bugs.Add( item );
				break;
			case TaskSectionKind.Review:
				feature.Review.Add( item );
				break;
			case TaskSectionKind.AiContext:
				feature.AiContext.Add( item );
				break;
			default:
				feature.Tasks.Add( item );
				break;
		}
	}

	static bool IsExactOpener( string text, string name )
	{
		return string.Equals( text, name, StringComparison.OrdinalIgnoreCase );
	}

	static bool IsEmptyPrefixedOpener( string text, string prefix )
	{
		string remainder;
		if ( !TryStripPrefix( text, prefix, out remainder ) )
			return false;
		return string.IsNullOrWhiteSpace( remainder );
	}

	static bool TryStripPrefix( string text, string prefix, out string remainder )
	{
		remainder = text;
		if ( string.IsNullOrEmpty( text ) || string.IsNullOrEmpty( prefix ) )
			return false;
		if ( !text.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) )
			return false;
		remainder = text.Substring( prefix.Length ).Trim();
		return true;
	}
}
#endif
