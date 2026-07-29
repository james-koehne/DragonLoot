#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public sealed class MilanoteSyncWindow : EditorWindow
{
	Vector2 _logScroll;
	string _statusMessage = "Idle.";
	MessageType _statusType = MessageType.Info;
	string _logText = string.Empty;
	bool _busy;
	bool _showAdvancedHeaders;
	CancellationTokenSource _cts;

	string _cookies;
	string _headersJson;
	string _boardId;
	string _outputRelativePath;

	[MenuItem( "Tools/MilanoteSync/Settings" )]
	public static void Open()
	{
		MilanoteSyncWindow window = GetWindow<MilanoteSyncWindow>( "Milanote Sync" );
		window.minSize = new Vector2( 420f, 520f );
		window.Show();
	}

	void OnEnable()
	{
		LoadFromSettings();
	}

	void OnDisable()
	{
		CancelInFlight();
		SaveToSettings();
	}

	void LoadFromSettings()
	{
		_cookies = MilanoteSyncSettings.Cookies;
		_headersJson = MilanoteSyncSettings.HeadersJson;
		_boardId = MilanoteSyncSettings.BoardId;
		_outputRelativePath = MilanoteSyncSettings.OutputRelativePath;
	}

	void SaveToSettings()
	{
		MilanoteSyncSettings.Cookies = _cookies;
		MilanoteSyncSettings.HeadersJson = _headersJson;
		MilanoteSyncSettings.BoardId = MilanoteSyncSettings.NormalizeBoardId( _boardId );
		_boardId = MilanoteSyncSettings.BoardId;
		MilanoteSyncSettings.OutputRelativePath = _outputRelativePath;
	}

	void OnGUI()
	{
		EditorGUI.BeginDisabledGroup( _busy );

		DrawConnectionSection();
		EditorGUILayout.Space( 8f );
		DrawBoardSection();
		EditorGUILayout.Space( 8f );
		DrawSyncSection();
		EditorGUILayout.Space( 8f );
		DrawOutputSection();
		EditorGUILayout.Space( 8f );
		DrawStatusRulesSection();

		EditorGUI.EndDisabledGroup();

		EditorGUILayout.Space( 8f );
		EditorGUILayout.HelpBox( _statusMessage, _statusType );

		EditorGUILayout.LabelField( "Log", EditorStyles.boldLabel );
		_logScroll = EditorGUILayout.BeginScrollView( _logScroll, GUILayout.MinHeight( 140f ) );
		EditorGUILayout.TextArea( _logText, GUILayout.ExpandHeight( true ) );
		EditorGUILayout.EndScrollView();
	}

	void DrawConnectionSection()
	{
		EditorGUILayout.LabelField( "Connection", EditorStyles.boldLabel );
		EditorGUILayout.HelpBox(
			"Log into Milanote in a browser, open DevTools → Network, reload, find the `me` request, "
			+ "Copy as cURL, then paste Cookie (and optional headers) here.",
			MessageType.None );

		EditorGUILayout.LabelField( "Cookies" );
		_cookies = EditorGUILayout.TextArea( _cookies ?? string.Empty, GUILayout.MinHeight( 56f ) );

		_showAdvancedHeaders = EditorGUILayout.Foldout( _showAdvancedHeaders, "Extra Headers (JSON object)", true );
		if ( _showAdvancedHeaders )
		{
			EditorGUILayout.HelpBox(
				"Optional. Example: {\"User-Agent\":\"Mozilla/5.0\",\"Accept-Language\":\"en-US\"}",
				MessageType.None );
			_headersJson = EditorGUILayout.TextArea( _headersJson ?? string.Empty, GUILayout.MinHeight( 48f ) );
		}

		EditorGUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Test Connection", GUILayout.Height( 28f ) ) )
			StartTestConnection();
		if ( GUILayout.Button( "Clear Session", GUILayout.Height( 28f ) ) )
		{
			_cookies = string.Empty;
			_headersJson = string.Empty;
			MilanoteSyncSettings.ClearSession();
			SetStatus( "Session cleared.", MessageType.Info );
		}

		EditorGUILayout.EndHorizontal();
	}

	void DrawBoardSection()
	{
		EditorGUILayout.LabelField( "Board", EditorStyles.boldLabel );
		_boardId = EditorGUILayout.TextField( "Board ID or URL", _boardId ?? string.Empty );
		EditorGUILayout.HelpBox(
			"Paste a board id or full URL, e.g.\n"
			+ "https://app.milanote.com/1WJLXe1m2VuF8j/dragon-loot\n"
			+ "Hierarchy uses keyword prefixes on todo items:\n"
			+ "  Category: Name\n"
			+ "  Feature: Name\n"
			+ "  other items → tasks (or Bugs if outside a Feature).",
			MessageType.None );

		EditorGUI.BeginDisabledGroup( string.IsNullOrWhiteSpace( _boardId ) );
		if ( GUILayout.Button( "Open Board In Browser" ) )
			Application.OpenURL( MilanoteSyncSettings.BuildBoardUrl( _boardId ) );
		EditorGUI.EndDisabledGroup();
	}

	void DrawSyncSection()
	{
		EditorGUILayout.LabelField( "Sync", EditorStyles.boldLabel );

		string lastSync = MilanoteSyncSettings.LastSyncUtc;
		EditorGUILayout.LabelField( "Last Sync (UTC)", string.IsNullOrEmpty( lastSync ) ? "Never" : lastSync );

		EditorGUILayout.BeginHorizontal();
		GUI.backgroundColor = new Color( 0.55f, 0.85f, 0.55f );
		if ( GUILayout.Button( _busy ? "Working…" : "Pull & Generate", GUILayout.Height( 32f ) ) )
			StartPullAndGenerate();
		GUI.backgroundColor = Color.white;

		if ( GUILayout.Button( "Cancel", GUILayout.Height( 32f ), GUILayout.Width( 80f ) ) )
			CancelInFlight();
		EditorGUILayout.EndHorizontal();

		if ( GUILayout.Button( "Import Markdown Export…", GUILayout.Height( 28f ) ) )
			StartImportMarkdownExport();

		if ( GUILayout.Button( "Import Latest dragon-loot*.md Export", GUILayout.Height( 28f ) ) )
			ImportLatestExport();

		GUI.backgroundColor = new Color( 0.9f, 0.45f, 0.45f );
		if ( GUILayout.Button( "Clear Generated Data…", GUILayout.Height( 28f ) ) )
			ClearGeneratedData();
		GUI.backgroundColor = Color.white;

		EditorGUILayout.HelpBox(
			"Author Milanote todos with 'Category:' and 'Feature:' prefixes.\n"
			+ "Example: Category: Player → Feature: Throwing → task lines.\n"
			+ "Only TASK_LIST / export headings containing 'Current Phase' are imported.\n"
			+ "Section tags: Requirement: / Note: / Bug: / Bugs / Review: / Review / AI:.\n"
			+ "Bug/Review include themselves + Milanote child tasks only (parentId), not following siblings.\n"
			+ "Loose items under a Category go to that Category's Bugs feature.\n"
			+ "Use 'Import Latest dragon-loot*.md Export' after exporting from Milanote.\n"
			+ "Clear Generated Data wipes FEATURES, CURRENT, INDEX, manifest, raw pull, and local Unity metadata "
			+ "(keeps cookies/board settings).",
			MessageType.None );
	}

	void DrawOutputSection()
	{
		EditorGUILayout.LabelField( "Output", EditorStyles.boldLabel );
		_outputRelativePath = EditorGUILayout.TextField( "Relative Path", _outputRelativePath ?? string.Empty );

		string absolute = IncrementalFileWriter.ResolveOutputRootAbsolute(
			Directory.GetParent( Application.dataPath ).FullName,
			_outputRelativePath );
		EditorGUILayout.SelectableLabel( absolute, EditorStyles.textField, GUILayout.Height( 18f ) );

		EditorGUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Open Folder" ) )
		{
			EnsureOutputExists( absolute );
			EditorUtility.RevealInFinder( absolute );
		}

		if ( GUILayout.Button( "Refresh AssetDatabase" ) )
		{
			AssetDatabase.Refresh();
			SetStatus( "AssetDatabase refreshed.", MessageType.Info );
		}

		EditorGUILayout.EndHorizontal();
	}

	void DrawStatusRulesSection()
	{
		EditorGUILayout.LabelField( "Status Rules", EditorStyles.boldLabel );
		EditorGUILayout.HelpBox(
			"Hierarchy: Category: / Feature: keywords (not indentation).\n"
			+ "Ready — no tasks, or all incomplete\n"
			+ "In Progress — mix of complete and incomplete child tasks\n"
			+ "Complete — feature checklist item is ticked (or all child tasks done)\n"
			+ "Complete features are written to FEATURES/_complete/\n\n"
			+ "CURRENT.md lists Ready and In Progress only.\n"
			+ "Task prefixes: Requirement: / Note: / Bug: / Bugs / Review: / Review / AI:\n"
			+ "Import filter: todo list / heading titles must contain 'Current Phase'.",
			MessageType.Info );
	}

	void ClearGeneratedData()
	{
		bool confirmed = EditorUtility.DisplayDialog(
			"Clear Generated Data",
			"Delete all generated Milanote markdown and local Project Tasks metadata?\n\n"
			+ "This removes FEATURES/, CURRENT.md, INDEX.md, .sync-manifest.json, "
			+ ".raw-pull.json, and .unity-dev-metadata.json.\n\n"
			+ "Cookies and Board ID are kept.",
			"Clear",
			"Cancel" );
		if ( !confirmed )
			return;

		MilanoteSyncService.SyncResult result = MilanoteSyncService.ClearGeneratedData();
		AppendLog( result.Message );
		SetStatus( result.Message, result.Success ? MessageType.Info : MessageType.Error );
		AssetDatabase.Refresh();
		ProjectTasksWindow.RefreshIfOpen();
	}

	void StartTestConnection()
	{
		SaveToSettings();
		if ( string.IsNullOrWhiteSpace( _cookies ) )
		{
			SetStatus( "Paste cookies before testing.", MessageType.Warning );
			return;
		}

		RunBusy( "Testing connection…", async token =>
		{
			using ( var client = new MilanoteApiClient( _cookies, _headersJson ) )
			{
				string message = await client.TestConnectionAsync( token ).ConfigureAwait( false );
				EnqueueUi( () =>
				{
					AppendLog( message );
					SetStatus( message, MessageType.Info );
				} );
			}
		} );
	}

	void StartPullAndGenerate()
	{
		SaveToSettings();

		if ( string.IsNullOrWhiteSpace( _cookies ) )
		{
			SetStatus( "Paste cookies before syncing.", MessageType.Warning );
			return;
		}

		string boardId = MilanoteSyncSettings.NormalizeBoardId( _boardId );
		if ( string.IsNullOrEmpty( boardId ) )
		{
			SetStatus( "Set a Board ID before syncing.", MessageType.Warning );
			return;
		}

		_boardId = boardId;
		SaveToSettings();

		MilanoteSyncService.SyncContext syncContext = MilanoteSyncService.CaptureContext();
		RunBusy( "Pulling Milanote board…", async token =>
		{
			MilanoteSyncService.SyncResult result = await MilanoteSyncService.PullAndGenerateAsync( syncContext, token )
				.ConfigureAwait( false );

			EnqueueUi( () =>
			{
				MilanoteSyncService.ApplyLastSyncUtc( result );
				AppendLog( result.Message );
				if ( result.Board != null )
				{
					AppendLog(
						"Parsed " + CountFeatures( result.Board ) + " features across "
						+ result.Board.Categories.Count + " categories." );
					for ( int i = 0; i < result.Board.Warnings.Count; i++ )
						AppendLog( "Warning: " + result.Board.Warnings[i] );
				}

				if ( result.WriteResult != null )
				{
					AppendLog( FormatWriteResult( result.WriteResult ) );
					for ( int i = 0; i < result.WriteResult.Warnings.Count; i++ )
						AppendLog( "Warning: " + result.WriteResult.Warnings[i] );
				}

				AssetDatabase.Refresh();
				ProjectTasksWindow.RefreshIfOpen();
				SetStatus(
					result.Message,
					result.Success
						? ( result.WriteResult != null && result.WriteResult.Warnings.Count > 0
							? MessageType.Warning
							: MessageType.Info )
						: MessageType.Error );
			} );
		} );
	}

	void ImportLatestExport()
	{
		SaveToSettings();
		MilanoteSyncService.SyncContext syncContext = MilanoteSyncService.CaptureContext();
		RunBusy( "Importing latest markdown export…", async token =>
		{
			await Task.Yield();
			token.ThrowIfCancellationRequested();

			MilanoteSyncService.SyncResult result = MilanoteSyncService.ImportLatestMarkdownExport( syncContext );

			EnqueueUi( () =>
			{
				MilanoteSyncService.ApplyLastSyncUtc( result );
				AppendLog( result.Message );
				if ( result.Board != null )
				{
					AppendLog(
						"Parsed " + CountFeatures( result.Board ) + " features across "
						+ result.Board.Categories.Count + " categories." );
					for ( int c = 0; c < result.Board.Categories.Count; c++ )
					{
						ProjectCategory category = result.Board.Categories[c];
						AppendLog( "  Category: " + category.Name );
						for ( int f = 0; f < category.Features.Count; f++ )
							AppendLog( "    Feature: " + category.Features[f].Name
								+ " (" + category.Features[f].Status + ")" );
					}

					for ( int i = 0; i < result.Board.Warnings.Count; i++ )
						AppendLog( "Warning: " + result.Board.Warnings[i] );
				}

				if ( result.WriteResult != null )
					AppendLog( FormatWriteResult( result.WriteResult ) );

				AssetDatabase.Refresh();
				ProjectTasksWindow.RefreshIfOpen();
				SetStatus(
					result.Message,
					result.Success ? MessageType.Info : MessageType.Error );
			} );
		} );
	}

	void StartImportMarkdownExport()
	{
		SaveToSettings();

		string projectRoot = Directory.GetParent( Application.dataPath ).FullName;
		string defaultDir = Path.Combine( projectRoot, ".cursor" );
		string picked = EditorUtility.OpenFilePanel(
			"Import Milanote Markdown Export",
			Directory.Exists( defaultDir ) ? defaultDir : projectRoot,
			"md" );

		if ( string.IsNullOrEmpty( picked ) )
			return;

		string outputRoot = IncrementalFileWriter.ResolveOutputRootAbsolute( projectRoot, _outputRelativePath );

		RunBusy( "Importing markdown export…", async token =>
		{
			await Task.Yield();
			token.ThrowIfCancellationRequested();

			DateTime syncUtc = DateTime.UtcNow;
			ProjectBoard board = MarkdownExportParser.ParseFile( picked );
			SyncWriteResult writeResult = IncrementalFileWriter.Write( board, outputRoot, syncUtc );

			EnqueueUi( () =>
			{
				MilanoteSyncSettings.LastSyncUtc = syncUtc.ToString( "yyyy-MM-dd HH:mm:ss" ) + "Z";
				AppendLog( "Imported: " + picked );
				AppendLog(
					"Parsed " + CountFeatures( board ) + " features across "
					+ board.Categories.Count + " categories." );
				AppendLog( FormatWriteResult( writeResult ) );
				foreach ( string warning in writeResult.Warnings )
					AppendLog( "Warning: " + warning );

				AssetDatabase.Refresh();
				ProjectTasksWindow.RefreshIfOpen();
				SetStatus(
					"Import complete. Wrote " + writeResult.FeaturesWritten
					+ ", skipped " + writeResult.FeaturesSkipped + ".",
					writeResult.Warnings.Count > 0 ? MessageType.Warning : MessageType.Info );
			} );
		} );
	}

	static int CountFeatures( ProjectBoard board )
	{
		int count = 0;
		for ( int i = 0; i < board.Categories.Count; i++ )
			count += board.Categories[i].Features.Count;
		return count;
	}

	static string FormatWriteResult( SyncWriteResult result )
	{
		var sb = new StringBuilder();
		sb.AppendLine( "Write summary:" );
		sb.AppendLine( "  written=" + result.FeaturesWritten );
		sb.AppendLine( "  skipped=" + result.FeaturesSkipped );
		sb.AppendLine( "  renamed=" + result.FeaturesRenamed );
		sb.AppendLine( "  archived=" + result.FeaturesArchived );
		for ( int i = 0; i < result.Messages.Count; i++ )
			sb.AppendLine( "  " + result.Messages[i] );
		return sb.ToString().TrimEnd();
	}

	void RunBusy( string status, Func<CancellationToken, Task> work )
	{
		if ( _busy )
		{
			SetStatus( "Already running.", MessageType.Warning );
			return;
		}

		CancelInFlight();
		_cts = new CancellationTokenSource();
		CancellationToken token = _cts.Token;
		_busy = true;
		SetStatus( status, MessageType.Info );
		Repaint();

		Task.Run( async () =>
		{
			try
			{
				await work( token ).ConfigureAwait( false );
			}
			catch ( OperationCanceledException )
			{
				EnqueueUi( () => SetStatus( "Cancelled.", MessageType.Warning ) );
			}
			catch ( Exception ex )
			{
				Debug.LogException( ex );
				EnqueueUi( () =>
				{
					AppendLog( "Error: " + ex.Message );
					SetStatus( ex.Message, MessageType.Error );
				} );
			}
			finally
			{
				EnqueueUi( () =>
				{
					_busy = false;
					Repaint();
				} );
			}
		} );
	}

	void CancelInFlight()
	{
		if ( _cts == null )
			return;
		try
		{
			_cts.Cancel();
		}
		catch
		{
			// ignored
		}

		_cts.Dispose();
		_cts = null;
	}

	static void EnqueueUi( Action action )
	{
		EditorApplication.delayCall += () =>
		{
			try
			{
				action();
			}
			catch ( Exception ex )
			{
				Debug.LogException( ex );
			}
		};
	}

	void SetStatus( string message, MessageType type )
	{
		_statusMessage = message ?? string.Empty;
		_statusType = type;
		Repaint();
	}

	void AppendLog( string line )
	{
		string stamp = DateTime.Now.ToString( "HH:mm:ss" );
		if ( string.IsNullOrEmpty( _logText ) )
			_logText = "[" + stamp + "] " + line;
		else
			_logText = _logText + "\n[" + stamp + "] " + line;
		Repaint();
	}

	static void EnsureOutputExists( string absolute )
	{
		if ( !Directory.Exists( absolute ) )
			Directory.CreateDirectory( absolute );
	}
}
#endif
