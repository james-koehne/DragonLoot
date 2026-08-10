#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

/// <summary>
/// Unity-managed development metadata. Survives Milanote sync; never written into importer sections.
/// </summary>
[Serializable]
public sealed class ProjectTasksLocalStore
{
	public const string FileName = ".unity-dev-metadata.json";

	[JsonProperty( "features" )]
	public Dictionary<string, LocalFeatureMetadata> Features = new Dictionary<string, LocalFeatureMetadata>();

	public static string GetPath( string outputRootAbsolute )
	{
		return Path.Combine( outputRootAbsolute, FileName );
	}

	public static ProjectTasksLocalStore Load( string outputRootAbsolute )
	{
		string path = GetPath( outputRootAbsolute );
		if ( !File.Exists( path ) )
			return new ProjectTasksLocalStore();

		try
		{
			string json = File.ReadAllText( path, Encoding.UTF8 );
			ProjectTasksLocalStore store = JsonConvert.DeserializeObject<ProjectTasksLocalStore>( json );
			if ( store == null )
				return new ProjectTasksLocalStore();
			if ( store.Features == null )
				store.Features = new Dictionary<string, LocalFeatureMetadata>();
			return store;
		}
		catch
		{
			return new ProjectTasksLocalStore();
		}
	}

	public void Save( string outputRootAbsolute )
	{
		string path = GetPath( outputRootAbsolute );
		string directory = Path.GetDirectoryName( path );
		if ( !string.IsNullOrEmpty( directory ) && !Directory.Exists( directory ) )
			Directory.CreateDirectory( directory );

		string json = JsonConvert.SerializeObject( this, Formatting.Indented );
		File.WriteAllText(
			path,
			json.Replace( "\r\n", "\n" ) + "\n",
			new UTF8Encoding( encoderShouldEmitUTF8Identifier: false ) );
	}

	public LocalFeatureMetadata GetOrCreate( string featureId )
	{
		if ( string.IsNullOrEmpty( featureId ) )
			featureId = "unknown";

		LocalFeatureMetadata meta;
		if ( Features.TryGetValue( featureId, out meta ) && meta != null )
			return meta;

		meta = new LocalFeatureMetadata { FeatureId = featureId };
		Features[featureId] = meta;
		return meta;
	}

	/// <summary>
	/// Milanote-complete items are treated as already tested: tasks → Implemented,
	/// features that are Complete / fully ticked → Verified (does not override Failed).
	/// </summary>
	public bool ApplyMilanoteCompletionAssumptions( ImportedFeature feature )
	{
		if ( feature == null || string.IsNullOrEmpty( feature.FeatureId ) )
			return false;

		LocalFeatureMetadata local = GetOrCreate( feature.FeatureId );
		bool changed = false;

		for ( int i = 0; i < feature.AllTasks.Count; i++ )
		{
			ImportedTask task = feature.AllTasks[i];
			if ( task == null || !task.IsComplete )
				continue;

			LocalTaskMetadata taskMeta = local.GetOrCreateTask( task.Key );
			if ( taskMeta.WorkStatus != LocalTaskWorkStatus.Implemented )
			{
				taskMeta.WorkStatus = LocalTaskWorkStatus.Implemented;
				changed = true;
			}
		}

		bool milanoteFeatureComplete =
			feature.IsCompleteFolder
			|| string.Equals( feature.Status, "Complete", StringComparison.OrdinalIgnoreCase )
			|| string.Equals( feature.Status, "Done", StringComparison.OrdinalIgnoreCase )
			|| ( feature.TotalTaskCount > 0 && feature.CompletedTaskCount == feature.TotalTaskCount );

		if ( milanoteFeatureComplete
		     && local.VerificationStatus != LocalVerificationStatus.Verified
		     && local.VerificationStatus != LocalVerificationStatus.Failed )
		{
			local.VerificationStatus = LocalVerificationStatus.Verified;
			if ( string.IsNullOrEmpty( local.VerificationDate ) )
				local.VerificationDate = DateTime.UtcNow.ToString( "yyyy-MM-dd" );
			if ( string.IsNullOrWhiteSpace( local.VerificationComments ) )
				local.VerificationComments = "Assumed tested (complete in Milanote).";
			changed = true;
		}

		return changed;
	}

	public bool ApplyMilanoteCompletionAssumptions( ProjectTasksCatalog catalog )
	{
		if ( catalog == null || catalog.AllFeatures == null )
			return false;

		bool changed = false;
		for ( int i = 0; i < catalog.AllFeatures.Count; i++ )
		{
			if ( ApplyMilanoteCompletionAssumptions( catalog.AllFeatures[i] ) )
				changed = true;
		}

		return changed;
	}

	public static bool IsFeatureMilanoteComplete( ImportedFeature feature )
	{
		return feature != null && feature.IsMilanoteComplete;
	}

	public static bool IsLocallyDonePendingMilanote( ImportedTask task, LocalTaskMetadata taskMeta )
	{
		return task != null
		       && !task.IsComplete
		       && taskMeta != null
		       && taskMeta.WorkStatus == LocalTaskWorkStatus.Implemented;
	}

	public int CountPendingMilanote( ImportedFeature feature )
	{
		if ( feature == null )
			return 0;

		LocalFeatureMetadata local = GetOrCreate( feature.FeatureId );
		int count = 0;
		for ( int i = 0; i < feature.AllTasks.Count; i++ )
		{
			ImportedTask task = feature.AllTasks[i];
			LocalTaskMetadata taskMeta = local.GetOrCreateTask( task.Key );
			if ( IsLocallyDonePendingMilanote( task, taskMeta ) )
				count++;
		}

		return count;
	}

	public int CountLocalProgress( ImportedFeature feature )
	{
		if ( feature == null )
			return 0;

		LocalFeatureMetadata local = GetOrCreate( feature.FeatureId );
		int count = 0;
		for ( int i = 0; i < feature.AllTasks.Count; i++ )
		{
			ImportedTask task = feature.AllTasks[i];
			if ( task.IsComplete )
			{
				count++;
				continue;
			}

			LocalTaskMetadata taskMeta = local.GetOrCreateTask( task.Key );
			if ( taskMeta.WorkStatus == LocalTaskWorkStatus.Implemented )
				count++;
		}

		return count;
	}

	public int CountPendingMilanoteAll( ProjectTasksCatalog catalog )
	{
		if ( catalog == null || catalog.AllFeatures == null )
			return 0;

		int count = 0;
		for ( int i = 0; i < catalog.AllFeatures.Count; i++ )
			count += CountPendingMilanote( catalog.AllFeatures[i] );
		return count;
	}

	public ProjectTasksTint GetFeatureTint( ImportedFeature feature )
	{
		if ( feature == null )
			return ProjectTasksTint.None;

		if ( IsFeatureMilanoteComplete( feature ) )
			return ProjectTasksTint.Green;

		if ( CountPendingMilanote( feature ) > 0 )
			return ProjectTasksTint.Amber;

		return ProjectTasksTint.None;
	}

	public ProjectTasksTint GetCategoryTint( ImportedCategory category )
	{
		if ( category == null || category.Features == null || category.Features.Count == 0 )
			return ProjectTasksTint.None;

		bool allGreen = true;
		bool anyAmber = false;
		for ( int i = 0; i < category.Features.Count; i++ )
		{
			ProjectTasksTint tint = GetFeatureTint( category.Features[i] );
			if ( tint != ProjectTasksTint.Green )
				allGreen = false;
			if ( tint == ProjectTasksTint.Amber )
				anyAmber = true;
		}

		if ( allGreen )
			return ProjectTasksTint.Green;
		if ( anyAmber )
			return ProjectTasksTint.Amber;
		return ProjectTasksTint.None;
	}

	public List<PendingMilanoteItem> CollectPendingMilanote( ProjectTasksCatalog catalog )
	{
		var list = new List<PendingMilanoteItem>();
		if ( catalog == null || catalog.AllFeatures == null )
			return list;

		for ( int i = 0; i < catalog.AllFeatures.Count; i++ )
		{
			ImportedFeature feature = catalog.AllFeatures[i];
			LocalFeatureMetadata local = GetOrCreate( feature.FeatureId );
			for ( int t = 0; t < feature.AllTasks.Count; t++ )
			{
				ImportedTask task = feature.AllTasks[t];
				LocalTaskMetadata taskMeta = local.GetOrCreateTask( task.Key );
				if ( !IsLocallyDonePendingMilanote( task, taskMeta ) )
					continue;

				list.Add( new PendingMilanoteItem
				{
					Feature = feature,
					Task = task,
					TaskMeta = taskMeta
				} );
			}
		}

		return list;
	}

	public bool FeatureMatchesLocalFilter( ImportedFeature feature, string filter )
	{
		if ( feature == null || string.IsNullOrEmpty( filter )
		     || string.Equals( filter, "All", StringComparison.OrdinalIgnoreCase ) )
			return true;

		LocalFeatureMetadata local = GetOrCreate( feature.FeatureId );
		if ( string.Equals( filter, "Pending Milanote", StringComparison.OrdinalIgnoreCase ) )
			return CountPendingMilanote( feature ) > 0;

		for ( int i = 0; i < feature.AllTasks.Count; i++ )
		{
			ImportedTask task = feature.AllTasks[i];
			LocalTaskMetadata taskMeta = local.GetOrCreateTask( task.Key );
			if ( string.Equals( filter, "Blocked", StringComparison.OrdinalIgnoreCase )
			     && taskMeta.WorkStatus == LocalTaskWorkStatus.Blocked )
				return true;
			if ( string.Equals( filter, "Needs Review", StringComparison.OrdinalIgnoreCase )
			     && taskMeta.WorkStatus == LocalTaskWorkStatus.NeedsReview )
				return true;
			if ( string.Equals( filter, "Has Highlights", StringComparison.OrdinalIgnoreCase )
			     && taskMeta.Highlighted )
				return true;
		}

		return false;
	}
}

public enum ProjectTasksTint
{
	None = 0,
	Amber = 1,
	Green = 2
}

public sealed class PendingMilanoteItem
{
	public ImportedFeature Feature;
	public ImportedTask Task;
	public LocalTaskMetadata TaskMeta;
}

[Serializable]
public sealed class LocalFeatureMetadata
{
	[JsonProperty( "featureId" )]
	public string FeatureId;

	[JsonProperty( "priority" )]
	public string Priority = string.Empty;

	[JsonProperty( "assignedDeveloper" )]
	public string AssignedDeveloper = string.Empty;

	[JsonProperty( "developerNotes" )]
	public string DeveloperNotes = string.Empty;

	[JsonProperty( "scenePath" )]
	public string ScenePath = string.Empty;

	[JsonProperty( "spawnPoint" )]
	public string SpawnPoint = string.Empty;

	[JsonProperty( "verificationStatus" )]
	public LocalVerificationStatus VerificationStatus = LocalVerificationStatus.NotTested;

	[JsonProperty( "verificationComments" )]
	public string VerificationComments = string.Empty;

	[JsonProperty( "verificationDate" )]
	public string VerificationDate = string.Empty;

	[JsonProperty( "tasks" )]
	public Dictionary<string, LocalTaskMetadata> Tasks = new Dictionary<string, LocalTaskMetadata>();

	public LocalTaskMetadata GetOrCreateTask( string taskKey )
	{
		if ( string.IsNullOrEmpty( taskKey ) )
			taskKey = "task";

		if ( Tasks == null )
			Tasks = new Dictionary<string, LocalTaskMetadata>();

		LocalTaskMetadata task;
		if ( Tasks.TryGetValue( taskKey, out task ) && task != null )
			return task;

		task = new LocalTaskMetadata { TaskKey = taskKey };
		Tasks[taskKey] = task;
		return task;
	}
}

[Serializable]
public sealed class LocalTaskMetadata
{
	[JsonProperty( "taskKey" )]
	public string TaskKey;

	[JsonProperty( "highlighted" )]
	public bool Highlighted;

	[JsonProperty( "comment" )]
	public string Comment = string.Empty;

	[JsonProperty( "workStatus" )]
	public LocalTaskWorkStatus WorkStatus = LocalTaskWorkStatus.None;
}
#endif
