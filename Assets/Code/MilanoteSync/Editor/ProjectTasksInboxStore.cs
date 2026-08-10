#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

/// <summary>
/// Local scratchpad for ideas to add to Milanote later. Survives sync and Clear Generated Data.
/// </summary>
[Serializable]
public sealed class ProjectTasksInboxStore
{
	public const string FileName = ".project-tasks-inbox.json";

	[JsonProperty( "items" )]
	public List<ProjectTasksInboxItem> Items = new List<ProjectTasksInboxItem>();

	public static string GetPath( string outputRootAbsolute )
	{
		return Path.Combine( outputRootAbsolute, FileName );
	}

	public static ProjectTasksInboxStore Load( string outputRootAbsolute )
	{
		string path = GetPath( outputRootAbsolute );
		if ( !File.Exists( path ) )
			return new ProjectTasksInboxStore();

		try
		{
			string json = File.ReadAllText( path, Encoding.UTF8 );
			ProjectTasksInboxStore store = JsonConvert.DeserializeObject<ProjectTasksInboxStore>( json );
			if ( store == null )
				return new ProjectTasksInboxStore();
			if ( store.Items == null )
				store.Items = new List<ProjectTasksInboxItem>();
			return store;
		}
		catch
		{
			return new ProjectTasksInboxStore();
		}
	}

	public void Save( string outputRootAbsolute )
	{
		string path = GetPath( outputRootAbsolute );
		string directory = Path.GetDirectoryName( path );
		if ( !string.IsNullOrEmpty( directory ) && !Directory.Exists( directory ) )
			Directory.CreateDirectory( directory );

		if ( Items == null )
			Items = new List<ProjectTasksInboxItem>();

		string json = JsonConvert.SerializeObject( this, Formatting.Indented );
		File.WriteAllText(
			path,
			json.Replace( "\r\n", "\n" ) + "\n",
			new UTF8Encoding( encoderShouldEmitUTF8Identifier: false ) );
	}

	public ProjectTasksInboxItem Add( string text )
	{
		if ( Items == null )
			Items = new List<ProjectTasksInboxItem>();

		string trimmed = text != null ? text.Trim() : string.Empty;
		if ( string.IsNullOrEmpty( trimmed ) )
			return null;

		var item = new ProjectTasksInboxItem
		{
			Id = Guid.NewGuid().ToString( "N" ),
			Text = trimmed,
			Done = false,
			CreatedUtc = DateTime.UtcNow.ToString( "o" )
		};
		Items.Add( item );
		return item;
	}

	public bool Remove( string id )
	{
		if ( Items == null || string.IsNullOrEmpty( id ) )
			return false;

		for ( int i = 0; i < Items.Count; i++ )
		{
			ProjectTasksInboxItem item = Items[i];
			if ( item == null || !string.Equals( item.Id, id, StringComparison.Ordinal ) )
				continue;
			Items.RemoveAt( i );
			return true;
		}

		return false;
	}

	public bool Move( int fromIndex, int toIndex )
	{
		if ( Items == null || Items.Count == 0 )
			return false;
		if ( fromIndex < 0 || fromIndex >= Items.Count )
			return false;
		if ( toIndex < 0 || toIndex >= Items.Count )
			return false;
		if ( fromIndex == toIndex )
			return false;

		ProjectTasksInboxItem item = Items[fromIndex];
		Items.RemoveAt( fromIndex );
		Items.Insert( toIndex, item );
		return true;
	}

	public int ClearDone()
	{
		if ( Items == null || Items.Count == 0 )
			return 0;

		int removed = 0;
		for ( int i = Items.Count - 1; i >= 0; i-- )
		{
			ProjectTasksInboxItem item = Items[i];
			if ( item == null || !item.Done )
				continue;
			Items.RemoveAt( i );
			removed++;
		}

		return removed;
	}

	public int CountPending()
	{
		if ( Items == null )
			return 0;

		int count = 0;
		for ( int i = 0; i < Items.Count; i++ )
		{
			ProjectTasksInboxItem item = Items[i];
			if ( item != null && !item.Done )
				count++;
		}

		return count;
	}

	public List<ProjectTasksInboxItem> CollectPending()
	{
		var list = new List<ProjectTasksInboxItem>();
		if ( Items == null )
			return list;

		for ( int i = 0; i < Items.Count; i++ )
		{
			ProjectTasksInboxItem item = Items[i];
			if ( item != null && !item.Done )
				list.Add( item );
		}

		return list;
	}
}

[Serializable]
public sealed class ProjectTasksInboxItem
{
	[JsonProperty( "id" )]
	public string Id;

	[JsonProperty( "text" )]
	public string Text = string.Empty;

	[JsonProperty( "done" )]
	public bool Done;

	[JsonProperty( "createdUtc" )]
	public string CreatedUtc = string.Empty;
}
#endif
