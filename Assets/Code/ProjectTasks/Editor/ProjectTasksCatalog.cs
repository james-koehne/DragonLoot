#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Lightweight in-memory catalog of Inbox / Features / Archive Markdown for the command centre UI.
/// </summary>
public sealed class ProjectTasksCatalog
{
	public string RootAbsolute = string.Empty;
	public DateTime LoadedAtUtc = DateTime.UtcNow;
	public readonly List<string> Warnings = new List<string>();
	public readonly List<ProjectTasksInboxEntry> Inbox = new List<ProjectTasksInboxEntry>();
	public readonly List<ProjectTasksFeatureEntry> Features = new List<ProjectTasksFeatureEntry>();
	public readonly List<ProjectTasksVerificationEntry> Verification = new List<ProjectTasksVerificationEntry>();
	public int ArchiveCount;
	public int ColoursAssigned;
	public int PendingVerificationCount;
	public int CompletedVerificationCount;

	public static ProjectTasksCatalog Load( string rootAbsolute )
	{
		var catalog = new ProjectTasksCatalog
		{
			RootAbsolute = rootAbsolute ?? string.Empty,
			LoadedAtUtc = DateTime.UtcNow
		};

		if ( string.IsNullOrWhiteSpace( rootAbsolute ) )
		{
			catalog.Warnings.Add( "Project Tasks root is not configured." );
			return catalog;
		}

		if ( !Directory.Exists( rootAbsolute ) )
		{
			catalog.Warnings.Add( "Root folder not found: " + rootAbsolute );
			return catalog;
		}

		LoadInbox( catalog );
		LoadFeatures( catalog );
		LoadArchiveCount( catalog );
		return catalog;
	}

	static void LoadInbox( ProjectTasksCatalog catalog )
	{
		string inboxDir = ProjectTasksWorkspace.InboxDir( catalog.RootAbsolute );
		List<string> files = ProjectTasksWorkspace.ListMarkdownFiles( inboxDir );
		for ( int i = 0; i < files.Count; i++ )
		{
			string path = files[i];
			try
			{
				string markdown = ProjectTasksMarkdown.ReadAllTextOrEmpty( path );
				string fileName = Path.GetFileNameWithoutExtension( path );
				var entry = new ProjectTasksInboxEntry
				{
					AbsolutePath = path,
					TaskId = ProjectTasksMarkdown.ExtractTitle( markdown, fileName ),
					Feature = ProjectTasksMarkdown.ExtractMetaValue( markdown, "Feature" ),
					Status = ProjectTasksMarkdown.ExtractMetaValue( markdown, "Status" ),
					Created = ProjectTasksMarkdown.ExtractMetaValue( markdown, "Created" ),
					Description = ProjectTasksMarkdown.ExtractSection( markdown, "Description" ),
					Markdown = markdown
				};
				if ( string.IsNullOrWhiteSpace( entry.Feature ) )
					entry.Feature = ProjectTasksMarkdown.AutoFeatureName;
				catalog.Inbox.Add( entry );
			}
			catch ( Exception ex )
			{
				catalog.Warnings.Add( "Failed to read inbox file " + path + ": " + ex.Message );
			}
		}
	}

	static void LoadFeatures( ProjectTasksCatalog catalog )
	{
		string featuresDir = ProjectTasksWorkspace.FeaturesDir( catalog.RootAbsolute );
		List<string> files = ProjectTasksWorkspace.ListMarkdownFiles( featuresDir );
		for ( int i = 0; i < files.Count; i++ )
		{
			string path = files[i];
			try
			{
				string markdown = ProjectTasksMarkdown.ReadAllTextOrEmpty( path );
				string fileName = Path.GetFileNameWithoutExtension( path );
				string name = ProjectTasksMarkdown.ExtractTitle( markdown, fileName );
				string updatedMarkdown;
				bool wroteColour;
				Color colour = ProjectTasksMarkdown.EnsureFeatureColour( path, name, markdown, out updatedMarkdown, out wroteColour );
				if ( wroteColour )
					catalog.ColoursAssigned++;

				List<ProjectTasksVerificationParseItem> verificationItems =
					ProjectTasksMarkdown.ParseVerificationItems( updatedMarkdown );

				var entry = new ProjectTasksFeatureEntry
				{
					AbsolutePath = path,
					Name = name,
					FileName = Path.GetFileName( path ),
					Summary = ProjectTasksMarkdown.ExtractSection( updatedMarkdown, "Summary" ),
					OutstandingTasksMarkdown = ProjectTasksMarkdown.ExtractSection( updatedMarkdown, "Outstanding Tasks" ),
					OutstandingTaskCount = ProjectTasksMarkdown.CountOutstandingTaskHeadings( updatedMarkdown ),
					Colour = colour,
					ColourHex = ProjectTasksMarkdown.ColourToHex( colour ),
					Markdown = updatedMarkdown
				};

				for ( int v = 0; v < verificationItems.Count; v++ )
				{
					ProjectTasksVerificationParseItem parsed = verificationItems[v];
					var verification = new ProjectTasksVerificationEntry
					{
						FeatureName = name,
						FeatureAbsolutePath = path,
						Text = parsed.Text,
						IsComplete = parsed.IsComplete,
						ItemIndexInFeature = v,
						Colour = colour
					};
					entry.Verification.Add( verification );
					catalog.Verification.Add( verification );
					if ( parsed.IsComplete )
						catalog.CompletedVerificationCount++;
					else
						catalog.PendingVerificationCount++;
				}

				catalog.Features.Add( entry );
			}
			catch ( Exception ex )
			{
				catalog.Warnings.Add( "Failed to read feature file " + path + ": " + ex.Message );
			}
		}

		catalog.Features.Sort( ( a, b ) => string.Compare( a.Name, b.Name, StringComparison.OrdinalIgnoreCase ) );
		catalog.Verification.Sort( CompareVerificationEntries );
	}

	static int CompareVerificationEntries( ProjectTasksVerificationEntry a, ProjectTasksVerificationEntry b )
	{
		if ( a == null && b == null )
			return 0;
		if ( a == null )
			return 1;
		if ( b == null )
			return -1;
		int complete = a.IsComplete.CompareTo( b.IsComplete );
		if ( complete != 0 )
			return complete;
		int feature = string.Compare( a.FeatureName, b.FeatureName, StringComparison.OrdinalIgnoreCase );
		if ( feature != 0 )
			return feature;
		return a.ItemIndexInFeature.CompareTo( b.ItemIndexInFeature );
	}

	public ProjectTasksFeatureEntry FindFeatureByName( string featureName )
	{
		if ( string.IsNullOrWhiteSpace( featureName ) || Features == null )
			return null;

		for ( int i = 0; i < Features.Count; i++ )
		{
			ProjectTasksFeatureEntry feature = Features[i];
			if ( feature != null
			     && string.Equals( feature.Name, featureName.Trim(), StringComparison.OrdinalIgnoreCase ) )
				return feature;
		}

		return null;
	}

	static void LoadArchiveCount( ProjectTasksCatalog catalog )
	{
		string archiveDir = ProjectTasksWorkspace.ArchiveDir( catalog.RootAbsolute );
		catalog.ArchiveCount = ProjectTasksWorkspace.ListMarkdownFiles( archiveDir ).Count;
	}
}

public sealed class ProjectTasksInboxEntry
{
	public string AbsolutePath = string.Empty;
	public string TaskId = string.Empty;
	public string Feature = string.Empty;
	public string Status = string.Empty;
	public string Created = string.Empty;
	public string Description = string.Empty;
	public string Markdown = string.Empty;

	public string Preview
	{
		get
		{
			if ( string.IsNullOrWhiteSpace( Description ) )
				return "(no description)";
			string oneLine = Description.Replace( '\n', ' ' ).Replace( '\r', ' ' ).Trim();
			if ( oneLine.Length <= 120 )
				return oneLine;
			return oneLine.Substring( 0, 117 ) + "...";
		}
	}
}

public sealed class ProjectTasksFeatureEntry
{
	public string AbsolutePath = string.Empty;
	public string Name = string.Empty;
	public string FileName = string.Empty;
	public string Summary = string.Empty;
	public string OutstandingTasksMarkdown = string.Empty;
	public int OutstandingTaskCount;
	public Color Colour = Color.white;
	public string ColourHex = string.Empty;
	public string Markdown = string.Empty;
	public readonly List<ProjectTasksVerificationEntry> Verification = new List<ProjectTasksVerificationEntry>();

	public int PendingVerificationCount
	{
		get
		{
			int count = 0;
			for ( int i = 0; i < Verification.Count; i++ )
			{
				if ( Verification[i] != null && !Verification[i].IsComplete )
					count++;
			}

			return count;
		}
	}

	public string SummaryPreview
	{
		get
		{
			if ( string.IsNullOrWhiteSpace( Summary ) )
				return "(no summary)";
			string oneLine = Summary.Replace( '\n', ' ' ).Replace( '\r', ' ' ).Trim();
			if ( oneLine.Length <= 160 )
				return oneLine;
			return oneLine.Substring( 0, 157 ) + "...";
		}
	}
}

public sealed class ProjectTasksVerificationEntry
{
	public string FeatureName = string.Empty;
	public string FeatureAbsolutePath = string.Empty;
	public string Text = string.Empty;
	public bool IsComplete;
	public int ItemIndexInFeature;
	public Color Colour = Color.white;
}
#endif
