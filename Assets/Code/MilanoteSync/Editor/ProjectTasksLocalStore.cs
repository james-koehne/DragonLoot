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
