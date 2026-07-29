#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

[Serializable]
public sealed class SyncManifest
{
	[JsonProperty( "features" )]
	public Dictionary<string, SyncManifestEntry> Features = new Dictionary<string, SyncManifestEntry>();

	[JsonProperty( "nextOrdinal" )]
	public int NextOrdinal = 1;

	public const string FileName = ".sync-manifest.json";

	public static string GetPath( string outputRootAbsolute )
	{
		return Path.Combine( outputRootAbsolute, FileName );
	}

	public static SyncManifest Load( string outputRootAbsolute )
	{
		string path = GetPath( outputRootAbsolute );
		if ( !File.Exists( path ) )
			return new SyncManifest();

		try
		{
			string json = File.ReadAllText( path, Encoding.UTF8 );
			SyncManifest manifest = JsonConvert.DeserializeObject<SyncManifest>( json );
			if ( manifest == null )
				return new SyncManifest();
			if ( manifest.Features == null )
				manifest.Features = new Dictionary<string, SyncManifestEntry>();
			if ( manifest.NextOrdinal < 1 )
				manifest.NextOrdinal = 1;
			return manifest;
		}
		catch
		{
			return new SyncManifest();
		}
	}

	public void Save( string outputRootAbsolute )
	{
		string path = GetPath( outputRootAbsolute );
		string directory = Path.GetDirectoryName( path );
		if ( !string.IsNullOrEmpty( directory ) && !Directory.Exists( directory ) )
			Directory.CreateDirectory( directory );

		string json = JsonConvert.SerializeObject( this, Formatting.Indented );
		File.WriteAllText( path, json.Replace( "\r\n", "\n" ) + "\n", new UTF8Encoding( encoderShouldEmitUTF8Identifier: false ) );
	}

	public SyncManifestEntry GetOrCreate( string milanoteId, string featureName )
	{
		if ( Features.TryGetValue( milanoteId, out SyncManifestEntry existing ) )
			return existing;

		int ordinal = NextOrdinal++;
		var entry = new SyncManifestEntry
		{
			Ordinal = ordinal,
			FileName = BuildFileName( ordinal, featureName ),
			ContentHash = string.Empty
		};
		Features[milanoteId] = entry;
		return entry;
	}

	public static string BuildFileName( int ordinal, string featureName )
	{
		string sanitized = SanitizeFileName( featureName );
		return ordinal.ToString( "000" ) + " " + sanitized + ".md";
	}

	public static string SanitizeFileName( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			return "Untitled";

		char[] invalid = Path.GetInvalidFileNameChars();
		var sb = new StringBuilder( name.Length );
		foreach ( char c in name.Trim() )
		{
			bool bad = false;
			for ( int i = 0; i < invalid.Length; i++ )
			{
				if ( c == invalid[i] )
				{
					bad = true;
					break;
				}
			}

			if ( bad || c == '/' || c == '\\' || c == ':' )
				sb.Append( '-' );
			else
				sb.Append( c );
		}

		string result = sb.ToString();
		while ( result.Contains( "  " ) )
			result = result.Replace( "  ", " " );
		result = result.Trim( ' ', '.', '-' );
		return string.IsNullOrEmpty( result ) ? "Untitled" : result;
	}

	public static string HashContent( string fingerprint )
	{
		using ( var sha = SHA256.Create() )
		{
			byte[] bytes = Encoding.UTF8.GetBytes( fingerprint ?? string.Empty );
			byte[] hash = sha.ComputeHash( bytes );
			var sb = new StringBuilder( hash.Length * 2 );
			for ( int i = 0; i < hash.Length; i++ )
				sb.Append( hash[i].ToString( "x2" ) );
			return sb.ToString();
		}
	}
}

[Serializable]
public sealed class SyncManifestEntry
{
	[JsonProperty( "ordinal" )]
	public int Ordinal;

	[JsonProperty( "fileName" )]
	public string FileName;

	[JsonProperty( "contentHash" )]
	public string ContentHash;
}
#endif
