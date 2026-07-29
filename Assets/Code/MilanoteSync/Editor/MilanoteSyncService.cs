#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Shared Milanote sync entry points for Milanote Sync and Project Tasks windows.
/// EditorPrefs / Application.dataPath must be read on the Unity main thread via
/// <see cref="CaptureContext"/> before kicking work onto a background thread.
/// </summary>
public static class MilanoteSyncService
{
	public const string RawPullFileName = ".raw-pull.json";

	public sealed class SyncResult
	{
		public bool Success;
		public string Message;
		public SyncWriteResult WriteResult;
		public TimeSpan Duration;
		public ProjectBoard Board;
		/// <summary>Set on the main thread after success (EditorPrefs is main-thread only).</summary>
		public string LastSyncUtc;
	}

	/// <summary>Main-thread snapshot of settings needed for background sync/import.</summary>
	public sealed class SyncContext
	{
		public string Cookies;
		public string HeadersJson;
		public string BoardId;
		public string ProjectRoot;
		public string OutputRoot;
	}

	/// <summary>Call from the Unity main thread only.</summary>
	public static SyncContext CaptureContext()
	{
		string projectRoot = Directory.GetParent( Application.dataPath ).FullName;
		return new SyncContext
		{
			Cookies = MilanoteSyncSettings.Cookies,
			HeadersJson = MilanoteSyncSettings.HeadersJson,
			BoardId = MilanoteSyncSettings.NormalizeBoardId( MilanoteSyncSettings.BoardId ),
			ProjectRoot = projectRoot,
			OutputRoot = IncrementalFileWriter.ResolveOutputRootAbsolute(
				projectRoot,
				MilanoteSyncSettings.OutputRelativePath )
		};
	}

	public static string ResolveOutputRoot()
	{
		string projectRoot = Directory.GetParent( Application.dataPath ).FullName;
		return IncrementalFileWriter.ResolveOutputRootAbsolute(
			projectRoot,
			MilanoteSyncSettings.OutputRelativePath );
	}

	/// <summary>
	/// Prefer <see cref="PullAndGenerateAsync(SyncContext, CancellationToken)"/> after
	/// <see cref="CaptureContext"/> on the main thread. This overload captures context
	/// immediately and must also be entered from the main thread.
	/// </summary>
	public static Task<SyncResult> PullAndGenerateAsync( CancellationToken cancellationToken = default )
	{
		return PullAndGenerateAsync( CaptureContext(), cancellationToken );
	}

	public static async Task<SyncResult> PullAndGenerateAsync(
		SyncContext context,
		CancellationToken cancellationToken = default )
	{
		var sw = Stopwatch.StartNew();
		var result = new SyncResult();

		try
		{
			if ( context == null )
			{
				result.Success = false;
				result.Message = "Sync context was null.";
				return result;
			}

			string cookies = context.Cookies;
			string headersJson = context.HeadersJson;
			string boardId = context.BoardId;
			string outputRoot = context.OutputRoot;
			string projectRoot = context.ProjectRoot;

			if ( string.IsNullOrWhiteSpace( cookies ) )
			{
				result.Success = false;
				result.Message = "Cookies are not configured. Open Tools → Milanote Sync.";
				return result;
			}

			if ( string.IsNullOrEmpty( boardId ) )
			{
				result.Success = false;
				result.Message = "Board ID is not configured. Open Tools → Milanote Sync.";
				return result;
			}

			DateTime syncUtc = DateTime.UtcNow;
			string lastSync = syncUtc.ToString( "yyyy-MM-dd HH:mm:ss" ) + "Z";
			using ( var client = new MilanoteApiClient( cookies, headersJson ) )
			{
				ProjectBoard board = await HierarchyParser.ParseAsync( client, boardId, cancellationToken )
					.ConfigureAwait( false );

				SaveRawPull( outputRoot, client.LastRawJson, boardId, syncUtc );

				string exportPath = FindLatestMarkdownExport( projectRoot );
				board = PreferRicherMarkdownExport( board, exportPath, result );

				SyncWriteResult writeResult = IncrementalFileWriter.Write( board, outputRoot, syncUtc );

				result.Success = true;
				result.Board = board;
				result.WriteResult = writeResult;
				result.LastSyncUtc = lastSync;
				if ( string.IsNullOrEmpty( result.Message ) )
				{
					result.Message = "Sync complete. Wrote " + writeResult.FeaturesWritten
						+ ", skipped " + writeResult.FeaturesSkipped
						+ ", archived " + writeResult.FeaturesArchived + ".";
				}
			}
		}
		catch ( OperationCanceledException )
		{
			result.Success = false;
			result.Message = "Cancelled.";
		}
		catch ( Exception ex )
		{
			Debug.LogException( ex );
			result.Success = false;
			result.Message = ex.Message;
		}
		finally
		{
			sw.Stop();
			result.Duration = sw.Elapsed;
		}

		return result;
	}

	public static SyncResult ImportMarkdownExport( string markdownPath )
	{
		return ImportMarkdownExport( CaptureContext(), markdownPath );
	}

	public static SyncResult ImportMarkdownExport( SyncContext context, string markdownPath )
	{
		var sw = Stopwatch.StartNew();
		var result = new SyncResult();
		try
		{
			string outputRoot = context != null ? context.OutputRoot : ResolveOutputRoot();
			DateTime syncUtc = DateTime.UtcNow;
			ProjectBoard board = MarkdownExportParser.ParseFile( markdownPath );
			SyncWriteResult writeResult = IncrementalFileWriter.Write( board, outputRoot, syncUtc );
			result.Success = true;
			result.Board = board;
			result.WriteResult = writeResult;
			result.LastSyncUtc = syncUtc.ToString( "yyyy-MM-dd HH:mm:ss" ) + "Z";
			result.Message = "Import complete. Wrote " + writeResult.FeaturesWritten
				+ ", skipped " + writeResult.FeaturesSkipped + ".";
		}
		catch ( Exception ex )
		{
			Debug.LogException( ex );
			result.Success = false;
			result.Message = ex.Message;
		}
		finally
		{
			sw.Stop();
			result.Duration = sw.Elapsed;
		}

		return result;
	}

	/// <summary>
	/// Import the newest .cursor/dragon-loot*.md export without calling the API.
	/// Call <see cref="CaptureContext"/> on the main thread first when running in the background.
	/// </summary>
	public static SyncResult ImportLatestMarkdownExport()
	{
		return ImportLatestMarkdownExport( CaptureContext() );
	}

	public static SyncResult ImportLatestMarkdownExport( SyncContext context )
	{
		if ( context == null )
		{
			return new SyncResult
			{
				Success = false,
				Message = "Sync context was null."
			};
		}

		string exportPath = FindLatestMarkdownExport( context.ProjectRoot );
		if ( string.IsNullOrEmpty( exportPath ) )
		{
			return new SyncResult
			{
				Success = false,
				Message = "No .cursor/dragon-loot*.md export found."
			};
		}

		SyncResult result = ImportMarkdownExport( context, exportPath );
		if ( result.Success )
			result.Message = "Imported " + Path.GetFileName( exportPath ) + ". " + result.Message;
		return result;
	}

	public static void ApplyLastSyncUtc( SyncResult result )
	{
		if ( result == null || string.IsNullOrEmpty( result.LastSyncUtc ) )
			return;
		MilanoteSyncSettings.LastSyncUtc = result.LastSyncUtc;
	}

	static ProjectBoard PreferRicherMarkdownExport( ProjectBoard apiBoard, string exportPath, SyncResult result )
	{
		// API is source of truth. Only fall back to the markdown export when the API
		// returned tasks but none used Category:/Feature: keywords (same format as the export).
		if ( string.IsNullOrEmpty( exportPath ) || !File.Exists( exportPath ) )
			return apiBoard;

		int apiKeywordScore = ScoreKeywordHierarchy( apiBoard );
		if ( apiKeywordScore > 0 )
			return apiBoard;

		if ( CountFeatures( apiBoard ) > 0
		     && HasOnlyBugsCategory( apiBoard )
		     && CountRawTasks( apiBoard ) > 0 )
		{
			ProjectBoard fromExport = MarkdownExportParser.ParseFile( exportPath );
			if ( ScoreKeywordHierarchy( fromExport ) > 0 )
			{
				result.Message = "API tasks lacked Category:/Feature: prefixes; "
					+ "used markdown export '" + Path.GetFileName( exportPath )
					+ "' as format reference fallback. Re-check Milanote todo text after Pull.";
				return fromExport;
			}
		}

		if ( CountFeatures( apiBoard ) == 0 )
		{
			ProjectBoard fromExport = MarkdownExportParser.ParseFile( exportPath );
			if ( CountFeatures( fromExport ) > 0 )
			{
				result.Message = "API returned no features; used markdown export '"
					+ Path.GetFileName( exportPath ) + "'.";
				return fromExport;
			}
		}

		return apiBoard;
	}

	static bool HasOnlyBugsCategory( ProjectBoard board )
	{
		if ( board == null || board.Categories.Count == 0 )
			return false;
		for ( int i = 0; i < board.Categories.Count; i++ )
		{
			if ( !string.Equals(
				    board.Categories[i].Name,
				    KeywordHierarchyParser.BugsName,
				    StringComparison.OrdinalIgnoreCase ) )
				return false;
		}

		return true;
	}

	static int CountRawTasks( ProjectBoard board )
	{
		if ( board == null )
			return 0;
		int count = 0;
		for ( int c = 0; c < board.Categories.Count; c++ )
		{
			ProjectCategory category = board.Categories[c];
			for ( int f = 0; f < category.Features.Count; f++ )
			{
				ProjectFeature feature = category.Features[f];
				count += feature.Tasks.Count
				         + feature.Requirements.Count
				         + feature.Notes.Count
				         + feature.Bugs.Count
				         + feature.Review.Count
				         + feature.AiContext.Count;
			}
		}

		return count;
	}

	static int ScoreKeywordHierarchy( ProjectBoard board )
	{
		if ( board == null )
			return 0;

		int score = 0;
		for ( int i = 0; i < board.Categories.Count; i++ )
		{
			ProjectCategory category = board.Categories[i];
			if ( category == null )
				continue;

			bool categoryIsBugs = string.Equals(
				category.Name, KeywordHierarchyParser.BugsName, StringComparison.OrdinalIgnoreCase );
			if ( !categoryIsBugs )
				score += 100;

			for ( int f = 0; f < category.Features.Count; f++ )
			{
				ProjectFeature feature = category.Features[f];
				if ( feature == null )
					continue;

				bool featureIsBugs = string.Equals(
					feature.Name, KeywordHierarchyParser.BugsName, StringComparison.OrdinalIgnoreCase );
				if ( !featureIsBugs )
					score += 20;
			}
		}

		return score;
	}

	/// <summary>
	/// Deletes generated markdown and local Unity metadata. Keeps cookies / board ID / settings.
	/// </summary>
	public static SyncResult ClearGeneratedData()
	{
		var result = new SyncResult();
		try
		{
			string outputRoot = ResolveOutputRoot();
			if ( !Directory.Exists( outputRoot ) )
			{
				result.Success = true;
				result.Message = "Output folder already empty: " + outputRoot;
				return result;
			}

			int deleted = 0;
			deleted += DeletePath( Path.Combine( outputRoot, "FEATURES" ) );
			deleted += DeletePath( Path.Combine( outputRoot, "CURRENT.md" ) );
			deleted += DeletePath( Path.Combine( outputRoot, "INDEX.md" ) );
			deleted += DeletePath( Path.Combine( outputRoot, SyncManifest.FileName ) );
			deleted += DeletePath( Path.Combine( outputRoot, ProjectTasksLocalStore.FileName ) );
			deleted += DeletePath( Path.Combine( outputRoot, RawPullFileName ) );

			result.Success = true;
			result.Message = "Cleared generated data (" + deleted + " paths) under " + outputRoot
				+ ". Auth settings kept.";
		}
		catch ( Exception ex )
		{
			Debug.LogException( ex );
			result.Success = false;
			result.Message = ex.Message;
		}

		return result;
	}

	/// <summary>
	/// Writes the last Milanote API board JSON under the output root for debugging / re-parsing.
	/// Overwrites on each Pull &amp; Generate.
	/// </summary>
	static void SaveRawPull( string outputRoot, string rawJson, string boardId, DateTime syncUtc )
	{
		if ( string.IsNullOrEmpty( outputRoot ) || string.IsNullOrEmpty( rawJson ) )
			return;

		Directory.CreateDirectory( outputRoot );
		string path = Path.Combine( outputRoot, RawPullFileName );

		JToken payload;
		try
		{
			payload = JToken.Parse( rawJson );
		}
		catch ( JsonException )
		{
			payload = rawJson;
		}

		var envelope = new JObject
		{
			["pulledAtUtc"] = syncUtc.ToString( "yyyy-MM-dd HH:mm:ss" ) + "Z",
			["boardId"] = boardId ?? string.Empty,
			["response"] = payload
		};

		string json = envelope.ToString( Formatting.Indented );
		File.WriteAllText(
			path,
			json.Replace( "\r\n", "\n" ) + "\n",
			new UTF8Encoding( encoderShouldEmitUTF8Identifier: false ) );
	}

	static int DeletePath( string path )
	{
		if ( string.IsNullOrEmpty( path ) )
			return 0;

		if ( File.Exists( path ) )
		{
			File.Delete( path );
			return 1;
		}

		if ( Directory.Exists( path ) )
		{
			Directory.Delete( path, recursive: true );
			return 1;
		}

		return 0;
	}

	static int CountFeatures( ProjectBoard board )
	{
		if ( board == null )
			return 0;
		int count = 0;
		for ( int i = 0; i < board.Categories.Count; i++ )
			count += board.Categories[i].Features.Count;
		return count;
	}

	static string FindLatestMarkdownExport( string projectRoot )
	{
		string cursorDir = Path.Combine( projectRoot, ".cursor" );
		if ( !Directory.Exists( cursorDir ) )
			return null;

		string[] files = Directory.GetFiles( cursorDir, "dragon-loot*.md", SearchOption.TopDirectoryOnly );
		if ( files == null || files.Length == 0 )
			files = Directory.GetFiles( cursorDir, "*.md", SearchOption.TopDirectoryOnly );

		if ( files == null || files.Length == 0 )
			return null;

		string best = null;
		DateTime bestTime = DateTime.MinValue;
		for ( int i = 0; i < files.Length; i++ )
		{
			string name = Path.GetFileName( files[i] );
			if ( string.Equals( name, "CURRENT.md", StringComparison.OrdinalIgnoreCase )
			     || string.Equals( name, "INDEX.md", StringComparison.OrdinalIgnoreCase ) )
				continue;

			DateTime writeTime = File.GetLastWriteTimeUtc( files[i] );
			if ( best == null || writeTime > bestTime )
			{
				best = files[i];
				bestTime = writeTime;
			}
		}

		return best;
	}
}
#endif
