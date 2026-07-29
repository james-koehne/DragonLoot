#if UNITY_EDITOR
using System;
using System.Collections.Generic;

public enum FeatureStatus
{
	Ready,
	InProgress,
	Done
}

public sealed class ProjectBoard
{
	public string RootBoardId;
	public string RootTitle;
	public readonly List<ProjectCategory> Categories = new List<ProjectCategory>();
	public readonly List<string> Warnings = new List<string>();
}

public sealed class ProjectCategory
{
	public string Id;
	public string Name;
	public long SortScore;
	public int SortIndex = -1;
	public readonly List<ProjectFeature> Features = new List<ProjectFeature>();
}

public sealed class ProjectFeature
{
	public string Id;
	public string Name;
	public string CategoryName;
	public FeatureStatus Status;
	/// <summary>True when the Milanote feature checklist item itself is ticked.</summary>
	public bool IsComplete;
	public long SortScore;
	public int SortIndex = -1;
	/// <summary>Parent category Milanote sort score (for stable cross-reload ordering).</summary>
	public long CategorySortScore;
	public int CategorySortIndex = -1;
	public readonly List<ProjectTaskItem> Tasks = new List<ProjectTaskItem>();
	public readonly List<ProjectTaskItem> Requirements = new List<ProjectTaskItem>();
	public readonly List<ProjectTaskItem> Notes = new List<ProjectTaskItem>();
	public readonly List<ProjectTaskItem> Bugs = new List<ProjectTaskItem>();
	public readonly List<ProjectTaskItem> Review = new List<ProjectTaskItem>();
	public readonly List<ProjectTaskItem> AiContext = new List<ProjectTaskItem>();

	// Future metadata hooks (omitted from Markdown until populated).
	public string Priority;
	public string Owner;
	public readonly List<string> Labels = new List<string>();
}

public sealed class ProjectTaskItem
{
	public string Id;
	/// <summary>Milanote location.parentId when known (API sync). Used for Bug/Review nesting.</summary>
	public string ParentId;
	public string Text;
	public bool IsComplete;
	public long SortScore;
	public int SortIndex = -1;
}

public static class FeatureStatusDeriver
{
	public static FeatureStatus Derive( ProjectFeature feature )
	{
		if ( feature == null )
			return FeatureStatus.Ready;

		if ( feature.IsComplete )
			return FeatureStatus.Done;

		return DeriveFromTasks( CollectAll( feature ) );
	}

	public static FeatureStatus DeriveFromTasks( IReadOnlyList<ProjectTaskItem> allTasks )
	{
		if ( allTasks == null || allTasks.Count == 0 )
			return FeatureStatus.Ready;

		int complete = 0;
		int incomplete = 0;
		for ( int i = 0; i < allTasks.Count; i++ )
		{
			if ( allTasks[i].IsComplete )
				complete++;
			else
				incomplete++;
		}

		if ( complete > 0 && incomplete == 0 )
			return FeatureStatus.Done;
		if ( complete > 0 && incomplete > 0 )
			return FeatureStatus.InProgress;
		return FeatureStatus.Ready;
	}

	/// <summary>Legacy helper — prefer <see cref="Derive(ProjectFeature)"/>.</summary>
	public static FeatureStatus Derive( IReadOnlyList<ProjectTaskItem> allTasks )
	{
		return DeriveFromTasks( allTasks );
	}

	public static string ToDisplayString( FeatureStatus status )
	{
		switch ( status )
		{
			case FeatureStatus.InProgress:
				return "In Progress";
			case FeatureStatus.Done:
				return "Complete";
			default:
				return "Ready";
		}
	}

	static List<ProjectTaskItem> CollectAll( ProjectFeature feature )
	{
		var all = new List<ProjectTaskItem>();
		all.AddRange( feature.Tasks );
		all.AddRange( feature.Requirements );
		all.AddRange( feature.Notes );
		all.AddRange( feature.Bugs );
		// Review is developer-owned and does not affect feature status derivation.
		all.AddRange( feature.AiContext );
		return all;
	}
}
#endif
