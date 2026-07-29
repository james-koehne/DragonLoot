#if UNITY_EDITOR
using System;
using UnityEditor;

/// <summary>
/// EditorPrefs-backed settings for Milanote → Cursor sync. Auth stays local; never commit cookies.
/// </summary>
public static class MilanoteSyncSettings
{
	const string Prefix = "DragonLoot.MilanoteSync.";

	const string KeyCookies = Prefix + "Cookies";
	const string KeyHeadersJson = Prefix + "HeadersJson";
	const string KeyBoardId = Prefix + "BoardId";
	const string KeyOutputRelativePath = Prefix + "OutputRelativePath";
	const string KeyLastSyncUtc = Prefix + "LastSyncUtc";

	public const string DefaultOutputRelativePath = ".cursor/milanote";
	public const string AppBaseUrl = "https://app.milanote.com";

	public static string Cookies
	{
		get => EditorPrefs.GetString( KeyCookies, string.Empty );
		set => EditorPrefs.SetString( KeyCookies, value ?? string.Empty );
	}

	/// <summary>
	/// Optional extra request headers as JSON object, e.g. {"User-Agent":"...","X-Milanote-Session":"..."}.
	/// Cookie header should use Cookies field instead.
	/// </summary>
	public static string HeadersJson
	{
		get => EditorPrefs.GetString( KeyHeadersJson, string.Empty );
		set => EditorPrefs.SetString( KeyHeadersJson, value ?? string.Empty );
	}

	public static string BoardId
	{
		get => EditorPrefs.GetString( KeyBoardId, string.Empty );
		set => EditorPrefs.SetString( KeyBoardId, NormalizeBoardId( value ) );
	}

	public static string OutputRelativePath
	{
		get
		{
			string path = EditorPrefs.GetString( KeyOutputRelativePath, DefaultOutputRelativePath );
			return string.IsNullOrWhiteSpace( path ) ? DefaultOutputRelativePath : path.Trim().Replace( '\\', '/' );
		}
		set
		{
			string path = string.IsNullOrWhiteSpace( value ) ? DefaultOutputRelativePath : value.Trim().Replace( '\\', '/' );
			EditorPrefs.SetString( KeyOutputRelativePath, path );
		}
	}

	public static string LastSyncUtc
	{
		get => EditorPrefs.GetString( KeyLastSyncUtc, string.Empty );
		set => EditorPrefs.SetString( KeyLastSyncUtc, value ?? string.Empty );
	}

	public static bool HasSession =>
		!string.IsNullOrWhiteSpace( Cookies );

	public static void ClearSession()
	{
		EditorPrefs.DeleteKey( KeyCookies );
		EditorPrefs.DeleteKey( KeyHeadersJson );
	}

	/// <summary>
	/// Accepts a raw board id or a Milanote URL such as
	/// https://app.milanote.com/1WJLXe1m2VuF8j/dragon-loot
	/// and returns just the board id (1WJLXe1m2VuF8j).
	/// </summary>
	public static string NormalizeBoardId( string boardIdOrUrl )
	{
		if ( string.IsNullOrWhiteSpace( boardIdOrUrl ) )
			return string.Empty;

		string value = boardIdOrUrl.Trim();

		if ( value.StartsWith( "http://", StringComparison.OrdinalIgnoreCase )
		     || value.StartsWith( "https://", StringComparison.OrdinalIgnoreCase ) )
		{
			if ( !Uri.TryCreate( value, UriKind.Absolute, out Uri uri ) )
				return value;

			string path = uri.AbsolutePath.Trim( '/' );
			if ( string.IsNullOrEmpty( path ) )
				return string.Empty;

			string[] segments = path.Split( '/' );
			if ( segments.Length == 0 )
				return string.Empty;

			// Legacy: /board/<id>  or current: /<id>/<slug>
			if ( string.Equals( segments[0], "board", StringComparison.OrdinalIgnoreCase ) )
				return segments.Length > 1 ? segments[1] : string.Empty;

			return segments[0];
		}

		// Paste may include a trailing slug: 1WJLXe1m2VuF8j/dragon-loot
		int slash = value.IndexOf( '/' );
		if ( slash >= 0 )
			value = value.Substring( 0, slash );

		return value.Trim();
	}

	public static string BuildBoardUrl( string boardIdOrUrl )
	{
		string id = NormalizeBoardId( boardIdOrUrl );
		if ( string.IsNullOrEmpty( id ) )
			return AppBaseUrl;
		return AppBaseUrl + "/" + id;
	}
}
#endif
