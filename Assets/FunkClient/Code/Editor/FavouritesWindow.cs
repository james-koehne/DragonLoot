using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class FavouritesWindow : EditorWindow
{
	[System.Serializable]
	private class FileUsage
	{
		public string guid;
		public long lastOpened; // store as ticks
	}

	[System.Serializable]
	private class FileUsageList
	{
		public List<FileUsage> list;
	}

	private static Dictionary<string, FileUsage> usageData = new Dictionary<string, FileUsage>();
	private const string SaveKey = "RecentlyOpenedFiles_Data";

	private Vector2 scrollPos;

	[MenuItem( "Window/Recently Opened" )]
	public static void ShowWindow()
	{
		GetWindow<FavouritesWindow>( "Recently Opened" );
		LoadUsageData();
	}

	private void OnEnable()
	{
		LoadUsageData();
	}

	private void OnGUI()
	{
		using ( new EditorGUILayout.HorizontalScope() )
		{
			if ( GUILayout.Button( "Clear Data", GUILayout.Width( 100 ) ) )
			{
				usageData.Clear();
				SaveUsageData();
				RepaintAll();
			}

			GUILayout.FlexibleSpace();
		}

		GUILayout.Space( 4 );

		scrollPos = EditorGUILayout.BeginScrollView( scrollPos );

		// Order by last opened time (newest first)
		foreach ( var guid in usageData.Values
					 .OrderByDescending( f => f.lastOpened )
					 .Select( f => f.guid ) )
		{
			string path = AssetDatabase.GUIDToAssetPath( guid );
			if ( string.IsNullOrEmpty( path ) )
				continue;

			UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>( path );
			if ( asset == null )
				continue;

			Rect rowRect = GUILayoutUtility.GetRect( 0, 22, GUILayout.ExpandWidth( true ) );

			// Hover highlight
			if ( rowRect.Contains( Event.current.mousePosition ) && Event.current.type == EventType.Repaint )
			{
				var bg = EditorGUIUtility.isProSkin ? new Color( 1, 1, 1, 0.06f ) : new Color( 0, 0, 0, 0.06f );
				EditorGUI.DrawRect( rowRect, bg );
			}

			// Draw icon + name
			GUIContent content = EditorGUIUtility.ObjectContent( asset, asset.GetType() );
			content.text = asset.name;
			GUI.Label( rowRect, content );

			// Handle clicks
			if ( Event.current.type == EventType.MouseDown && rowRect.Contains( Event.current.mousePosition ) )
			{
				if ( Event.current.clickCount == 2 )
				{
					AssetDatabase.OpenAsset( asset );
				}
				else
				{
					Selection.activeObject = asset;
					EditorGUIUtility.PingObject( asset );
				}

				Event.current.Use();
			}
		}

		EditorGUILayout.EndScrollView();
	}

	// --- Tracking actual opens ---
	[UnityEditor.Callbacks.OnOpenAsset]
	private static bool OnOpenAsset( int instanceID, int line )
	{
		string path = AssetDatabase.GetAssetPath( instanceID );
		if ( string.IsNullOrEmpty( path ) )
			return false;

		string guid = AssetDatabase.AssetPathToGUID( path );
		if ( string.IsNullOrEmpty( guid ) )
			return false;

		OnAssetOpened( guid );
		return false;
	}

	private static void OnAssetOpened( string guid )
	{
		if ( !usageData.TryGetValue( guid, out var usage ) )
		{
			usage = new FileUsage { guid = guid };
			usageData[ guid ] = usage;
		}

		// Update to current timestamp
		usage.lastOpened = System.DateTime.UtcNow.Ticks;

		SaveUsageData();
		RepaintAll();
	}

	[InitializeOnLoadMethod]
	private static void Init()
	{
		EditorApplication.projectWindowItemOnGUI += TrackSelection;
	}

	private static void TrackSelection( string guid, Rect rect )
	{
		if ( Event.current != null && Event.current.type == EventType.MouseDown && rect.Contains( Event.current.mousePosition ) )
		{
			OnAssetOpened( guid );
		}
	}

	// --- Persistence ---
	private static void SaveUsageData()
	{
		var list = new FileUsageList { list = usageData.Values.ToList() };
		string json = JsonUtility.ToJson( list );
		EditorPrefs.SetString( SaveKey, json );
	}

	private static void LoadUsageData()
	{
		string json = EditorPrefs.GetString( SaveKey, "" );
		if ( !string.IsNullOrEmpty( json ) )
		{
			var data = JsonUtility.FromJson<FileUsageList>( json );
			usageData = ( data != null && data.list != null )
				? data.list.ToDictionary( f => f.guid, f => f )
				: new Dictionary<string, FileUsage>();
		}
		else
		{
			usageData = new Dictionary<string, FileUsage>();
		}
	}

	private static void RepaintAll()
	{
		foreach ( var w in Resources.FindObjectsOfTypeAll<FavouritesWindow>() )
		{
			w.Repaint();
		}
	}
}
