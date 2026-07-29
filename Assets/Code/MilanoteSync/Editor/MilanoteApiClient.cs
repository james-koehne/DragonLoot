#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public sealed class MilanoteApiException : Exception
{
	public int StatusCode { get; }

	public MilanoteApiException( string message, int statusCode = 0 )
		: base( message )
	{
		StatusCode = statusCode;
	}
}

/// <summary>
/// Native C# client for Milanote unofficial read endpoints (no Python dependency).
/// </summary>
public sealed class MilanoteApiClient : IDisposable
{
	public const string HomeUrl = "https://app.milanote.com/api/users/me";
	public const string BoardBaseUrl = "https://app.milanote.com/api/boards/";

	readonly HttpClient _http;
	readonly bool _ownsHttp;

	/// <summary>Raw response body from the most recent successful JSON GET.</summary>
	public string LastRawJson { get; private set; }

	public MilanoteApiClient( string cookies, string headersJson )
		: this( CreateHttpClient( cookies, headersJson ), ownsHttp: true )
	{
	}

	public MilanoteApiClient( HttpClient http, bool ownsHttp = false )
	{
		_http = http ?? throw new ArgumentNullException( nameof( http ) );
		_ownsHttp = ownsHttp;
	}

	public static HttpClient CreateHttpClient( string cookies, string headersJson )
	{
		if ( string.IsNullOrWhiteSpace( cookies ) )
			throw new MilanoteApiException( "Cookies are required. Paste session cookies from a logged-in Milanote browser request." );

		var handler = new HttpClientHandler
		{
			UseCookies = false
		};
		var http = new HttpClient( handler );
		http.Timeout = TimeSpan.FromSeconds( 60 );
		http.DefaultRequestHeaders.TryAddWithoutValidation( "Cookie", cookies.Trim() );
		http.DefaultRequestHeaders.TryAddWithoutValidation( "Accept", "application/json" );

		ApplyExtraHeaders( http, headersJson );
		return http;
	}

	static void ApplyExtraHeaders( HttpClient http, string headersJson )
	{
		if ( string.IsNullOrWhiteSpace( headersJson ) )
			return;

		Dictionary<string, string> headers;
		try
		{
			headers = JsonConvert.DeserializeObject<Dictionary<string, string>>( headersJson );
		}
		catch ( JsonException ex )
		{
			throw new MilanoteApiException( "Headers JSON is invalid: " + ex.Message );
		}

		if ( headers == null )
			return;

		foreach ( KeyValuePair<string, string> pair in headers )
		{
			if ( string.IsNullOrWhiteSpace( pair.Key ) || pair.Value == null )
				continue;

			string key = pair.Key.Trim();
			if ( string.Equals( key, "Cookie", StringComparison.OrdinalIgnoreCase ) )
				continue;

			http.DefaultRequestHeaders.Remove( key );
			http.DefaultRequestHeaders.TryAddWithoutValidation( key, pair.Value );
		}
	}

	public async Task<MilanoteBoardResponse> GetMeAsync( CancellationToken cancellationToken = default )
	{
		return await GetJsonAsync( HomeUrl, cancellationToken ).ConfigureAwait( false );
	}

	public async Task<MilanoteBoardResponse> GetBoardByIdAsync( string boardId, CancellationToken cancellationToken = default )
	{
		if ( string.IsNullOrWhiteSpace( boardId ) )
			throw new MilanoteApiException( "Board ID is empty." );

		string url = BoardBaseUrl + boardId.Trim() + "?loadAncestors=false";
		MilanoteBoardResponse response = await GetJsonAsync( url, cancellationToken ).ConfigureAwait( false );
		ValidateBoardResponse( response, boardId.Trim() );
		return response;
	}

	public async Task<string> TestConnectionAsync( CancellationToken cancellationToken = default )
	{
		MilanoteBoardResponse response = await GetMeAsync( cancellationToken ).ConfigureAwait( false );
		if ( response == null || response.Elements == null || response.Elements.Count == 0 )
			return "Connected (no home elements returned).";

		foreach ( KeyValuePair<string, JObject> pair in response.Elements )
		{
			MilanoteParsedElement element = MilanoteDtoParser.ParseElement( pair.Value );
			if ( element != null && !string.IsNullOrEmpty( element.Title ) )
				return "Connected. Home element: " + element.Title + " (" + element.Id + ")";
			if ( element != null && !string.IsNullOrEmpty( element.Id ) )
				return "Connected. Home element id: " + element.Id;
		}

		return "Connected.";
	}

	async Task<MilanoteBoardResponse> GetJsonAsync( string url, CancellationToken cancellationToken )
	{
		HttpResponseMessage response;
		try
		{
			response = await _http.GetAsync( url, cancellationToken ).ConfigureAwait( false );
		}
		catch ( TaskCanceledException ) when ( !cancellationToken.IsCancellationRequested )
		{
			throw new MilanoteApiException( "Request timed out contacting Milanote." );
		}
		catch ( HttpRequestException ex )
		{
			throw new MilanoteApiException( "HTTP error: " + ex.Message );
		}

		string body = await response.Content.ReadAsStringAsync().ConfigureAwait( false );
		if ( !response.IsSuccessStatusCode )
		{
			string snippet = body;
			if ( snippet != null && snippet.Length > 240 )
				snippet = snippet.Substring( 0, 240 ) + "...";
			throw new MilanoteApiException(
				"Milanote returned HTTP " + (int)response.StatusCode + ": " + snippet,
				(int)response.StatusCode );
		}

		try
		{
			MilanoteBoardResponse parsed = JsonConvert.DeserializeObject<MilanoteBoardResponse>( body );
			LastRawJson = body;
			return parsed;
		}
		catch ( JsonException ex )
		{
			throw new MilanoteApiException( "Failed to parse Milanote JSON: " + ex.Message );
		}
	}

	static void ValidateBoardResponse( MilanoteBoardResponse response, string boardId )
	{
		if ( response == null )
			throw new MilanoteApiException( "Empty board response." );

		if ( response.Errors != null && response.Errors.TryGetValue( boardId, out JObject errorObj ) )
		{
			string code = null;
			JToken codeToken = errorObj.SelectToken( "error.code" );
			if ( codeToken != null )
				code = codeToken.Value<string>();
			if ( string.Equals( code, "BOARD_NOT_FOUND", StringComparison.OrdinalIgnoreCase ) )
				throw new MilanoteApiException( "Board not found: " + boardId );
			throw new MilanoteApiException( "Milanote board error for " + boardId + ": " + code );
		}

		if ( response.Elements == null || !response.Elements.TryGetValue( boardId, out JObject boardJson ) )
			throw new MilanoteApiException( "Board element missing from response: " + boardId );

		string elementType = boardJson.Value<string>( "elementType" );
		if ( string.Equals( elementType, "SKELETON", StringComparison.OrdinalIgnoreCase ) )
			throw new MilanoteApiException( "Not authorized (SKELETON). Check cookies/headers and try again." );
	}

	public void Dispose()
	{
		if ( _ownsHttp )
			_http.Dispose();
	}
}
#endif
