#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Export / import a flattened CSV of player-facing game text
/// (definitions + map labels / markers + fairy defaults).
/// </summary>
public sealed class GameTextSpreadsheetWindow : EditorWindow
{
	string _status = "Ready.";
	Vector2 _scroll;

	[MenuItem( DragonLootMenus.ToolsGameTextSpreadsheet )]
	public static void Open()
	{
		GameTextSpreadsheetWindow window = GetWindow<GameTextSpreadsheetWindow>();
		window.titleContent = new GUIContent( "Game Text" );
		window.minSize = new Vector2( 360f, 220f );
		window.Show();
	}

	void OnGUI()
	{
		_scroll = EditorGUILayout.BeginScrollView( _scroll );

		EditorGUILayout.LabelField( "Game Text Spreadsheet", EditorStyles.boldLabel );
		EditorGUILayout.HelpBox(
			"Exports and imports one flattened CSV (Category, Id, Field, Text, AssetPath).\n" +
			"Covers definition assets, map labels/markers, and fairy default lines.\n" +
			"Import overwrites matching Category+Id+Field only — does not create assets or array slots.\n" +
			"Hardcoded UI strings and localization columns are out of scope.",
			MessageType.Info );

		EditorGUILayout.Space( 8f );

		using ( new EditorGUILayout.HorizontalScope() )
		{
			if ( GUILayout.Button( "Export CSV…", GUILayout.Height( 32f ) ) )
				ExportCsv();

			if ( GUILayout.Button( "Import CSV…", GUILayout.Height( 32f ) ) )
				ImportCsv();
		}

		EditorGUILayout.Space( 8f );
		EditorGUILayout.LabelField( "Status", EditorStyles.boldLabel );
		EditorGUILayout.HelpBox( _status, MessageType.None );

		EditorGUILayout.EndScrollView();
	}

	void ExportCsv()
	{
		string path = EditorUtility.SaveFilePanel( "Export Game Text CSV", Application.dataPath, "GameText", "csv" );
		if ( string.IsNullOrEmpty( path ) )
			return;

		try
		{
			List<GameTextRow> rows = GameTextCollectors.CollectAll();
			GameTextCsv.WriteFile( path, rows );
			_status = "Exported " + rows.Count + " rows to:\n" + path;
			Debug.Log( "[GameText] Exported " + rows.Count + " rows to " + path );
		}
		catch ( System.Exception ex )
		{
			_status = "Export failed: " + ex.Message;
			Debug.LogException( ex );
		}
	}

	void ImportCsv()
	{
		string path = EditorUtility.OpenFilePanel( "Import Game Text CSV", Application.dataPath, "csv" );
		if ( string.IsNullOrEmpty( path ) )
			return;

		if ( !EditorUtility.DisplayDialog(
			    "Import Game Text",
			    "Overwrite matching text fields from this CSV?\n\n" + path +
			    "\n\nAssets are updated in place. Missing rows are skipped. Nothing new is created.",
			    "Import",
			    "Cancel" ) )
			return;

		try
		{
			int parseErrors;
			List<GameTextRow> rows = GameTextCsv.ReadFile( path, out parseErrors );
			GameTextApplicator.Result result = GameTextApplicator.Apply( rows, parseErrors );
			_status = "Import finished.\n" +
			          "Updated: " + result.Updated + "\n" +
			          "Unchanged: " + result.Unchanged + "\n" +
			          "Missing: " + result.Missing + "\n" +
			          "Parse errors: " + result.ParseErrors + "\n" +
			          "Source: " + path;
			Debug.Log( "[GameText] Import — updated=" + result.Updated
			           + " unchanged=" + result.Unchanged
			           + " missing=" + result.Missing
			           + " parseErrors=" + result.ParseErrors
			           + " from " + path );
		}
		catch ( System.Exception ex )
		{
			_status = "Import failed: " + ex.Message;
			Debug.LogException( ex );
		}
	}
}
#endif
