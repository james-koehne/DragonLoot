#if UNITY_EDITOR
using System;
using System.Collections.Generic;

public enum LocalTaskWorkStatus
{
	None = 0,
	Implemented = 1,
	Blocked = 2,
	NeedsReview = 3
}

public enum LocalVerificationStatus
{
	NotTested = 0,
	Testing = 1,
	Verified = 2,
	Failed = 3
}

/// <summary>
/// Importer-owned feature snapshot loaded from generated Markdown.
/// </summary>
public sealed class ImportedFeature
{
	public string FeatureId;
	public string Name;
	public string Category;
	public string Status;
	public string LastSync;
	public string AbsolutePath;
	public string RelativePath;
	public bool IsCompleteFolder;
	public long CategorySortScore;
	public int CategorySortIndex = -1;
	public long SortScore;
	public int SortIndex = -1;

	public readonly List<string> Requirements = new List<string>();
	public readonly List<string> Tasks = new List<string>();
	public readonly List<string> Notes = new List<string>();
	public readonly List<string> Bugs = new List<string>();
	public readonly List<string> Review = new List<string>();
	public readonly List<string> AiContext = new List<string>();

	public string CursorImplementation = string.Empty;
	public string FilesModified = string.Empty;
	public string TestingInstructions = string.Empty;
	public string CursorNotes = string.Empty;
	public string DeveloperVerificationMarkdown = string.Empty;

	public readonly List<ImportedTask> AllTasks = new List<ImportedTask>();

	public int CompletedTaskCount
	{
		get
		{
			int count = 0;
			for ( int i = 0; i < AllTasks.Count; i++ )
			{
				if ( AllTasks[i].IsComplete )
					count++;
			}

			return count;
		}
	}

	public int TotalTaskCount => AllTasks.Count;

	public string TaskProgressLabel => CompletedTaskCount + "/" + TotalTaskCount;

	public string MilanoteProgressLabel => "M:" + CompletedTaskCount + "/" + TotalTaskCount;

	public bool IsMilanoteComplete
	{
		get
		{
			if ( IsCompleteFolder )
				return true;
			if ( string.Equals( Status, "Complete", StringComparison.OrdinalIgnoreCase )
			     || string.Equals( Status, "Done", StringComparison.OrdinalIgnoreCase ) )
				return true;

			return TotalTaskCount > 0 && CompletedTaskCount == TotalTaskCount;
		}
	}
}

public sealed class ImportedTask
{
	public string Section;
	public string Text;
	public string Key;
	public bool IsComplete;
}

public sealed class ImportedCategory
{
	public string Name;
	public readonly List<ImportedFeature> Features = new List<ImportedFeature>();

	public int CompletedCount
	{
		get
		{
			int count = 0;
			for ( int i = 0; i < Features.Count; i++ )
			{
				if ( Features[i].IsMilanoteComplete )
					count++;
			}

			return count;
		}
	}
}

public sealed class ProjectTasksCatalog
{
	public readonly List<ImportedCategory> Categories = new List<ImportedCategory>();
	public readonly List<ImportedFeature> AllFeatures = new List<ImportedFeature>();
	public readonly List<string> LoadWarnings = new List<string>();
	public string OutputRootAbsolute;
	public DateTime LoadedAtUtc;
}
#endif
