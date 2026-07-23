using System;
using System.Security.Cryptography;
using System.Text;

using Newtonsoft.Json;

using UnityEngine;

#if STEAM_BUILD
using Steamworks;
#endif

[Serializable]
public class ProfileSnapshot
{
	public int version;
	public long timestamp;
	public string deviceId;
	public string checksum;
	public string data;
}

public static class ProfileChecksum
{
	public static string Compute( string data, int version )
	{
		string input = $"{version}:{data}";

		using ( SHA256 sha = SHA256.Create() )
		{
			byte[] bytes = Encoding.UTF8.GetBytes( input );
			byte[] hash = sha.ComputeHash( bytes );
			return BytesToHex( hash );
		}
	}

	private static string BytesToHex( byte[] bytes )
	{
		StringBuilder sb = new StringBuilder( bytes.Length * 2 );
		for ( int i = 0; i < bytes.Length; i++ )
			sb.Append( bytes[ i ].ToString( "x2" ) );

		return sb.ToString();
	}

	public static bool IsValid( ProfileSnapshot snapshot )
	{
		if ( snapshot == null )
			return false;
		string expected = ProfileChecksum.Compute( snapshot.data, snapshot.version );
		return snapshot.checksum == expected;
	}
}

#if STEAM_BUILD

public static class SteamCloud
{
	private const string FILE_NAME = "profile_snapshot.json";

	public static bool DEBUG_LOG_JSON = false;

	public static bool IsAvailable()
	{
		return SteamManager.Instance != null && SteamManager.Instance.Initialized && SteamRemoteStorage.IsCloudEnabledForApp();
	}

	public static void UploadProfile<T>()
	{
		if ( !IsAvailable() )
			return;

		ProfileSnapshot snapshot;
		string json;
		try
		{
			snapshot = ProfileData.ExportSnapshot<T>();
			json = JsonConvert.SerializeObject( snapshot, Formatting.None );
		}
		catch ( Exception ex )
		{
			Debug.LogError( $"SteamCloud: Export/serialize failed: {ex.Message}" );
			return;
		}

		LogJson( "UPLOAD", json );

		byte[] bytes = Encoding.UTF8.GetBytes( json );
		if ( !SteamRemoteStorage.FileWrite( FILE_NAME, bytes, bytes.Length ) )
			Debug.LogError( "SteamCloud: FileWrite failed" );
	}

	public static ProfileSnapshot DownloadProfile()
	{
		if ( !IsAvailable() )
			return null;
		if ( !SteamRemoteStorage.FileExists( FILE_NAME ) )
			return null;

		int size = SteamRemoteStorage.GetFileSize( FILE_NAME );
		if ( size <= 0 )
			return null;
		byte[] bytes = new byte[ size ];
		int read = SteamRemoteStorage.FileRead( FILE_NAME, bytes, size );
		if ( read != size )
		{
			Debug.LogWarning( $"SteamCloud: FileRead returned {read}, expected {size}" );
			return null;
		}

		try
		{
			string json = Encoding.UTF8.GetString( bytes );
			LogJson( "DOWNLOAD", json );
			return JsonConvert.DeserializeObject<ProfileSnapshot>( json );
		}
		catch ( Exception ex )
		{
			Debug.LogWarning( $"SteamCloud: Corrupt or invalid cloud data: {ex.Message}" );
			return null;
		}
	}

	public static void SyncWithSteam<T>()
	{
		ProfileSnapshot local = ProfileData.ExportSnapshot<T>();
		ProfileSnapshot remote = DownloadProfile();

		if ( remote == null )
		{
			Debug.Log( "SteamCloud: No remote profile > uploading" );
			UploadProfile<T>();
			return;
		}

		if ( !ProfileChecksum.IsValid( remote ) )
		{
			Debug.LogWarning( "SteamCloud: Remote checksum invalid > overwriting" );
			UploadProfile<T>();
			return;
		}

		if ( remote.checksum == local.checksum )
		{
			Debug.Log( "SteamCloud: Profiles identical > no sync needed" );
			return;
		}

		if ( remote.timestamp > local.timestamp )
		{
			Debug.Log( "SteamCloud: Remote newer > importing" );
			ProfileData.ImportSnapshot<T>( remote );
		}
		else
		{
			Debug.Log( "SteamCloud: Local newer > uploading" );
			UploadProfile<T>();
		}
	}

	private static void LogJson( string label, string json )
	{
		if ( !DEBUG_LOG_JSON )
			return;

		try
		{
			string pretty = JsonConvert.SerializeObject(
				JsonConvert.DeserializeObject( json ),
				Formatting.Indented
			);
			Debug.Log( $"[SteamCloud DEBUG] {label}\n{pretty}" );
		}
		catch ( Exception )
		{
			Debug.Log( $"[SteamCloud DEBUG] {label}\n{json}" );
		}
	}
}

#endif
