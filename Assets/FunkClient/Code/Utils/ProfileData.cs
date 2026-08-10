using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;

#if UNITY_EDITOR && PARRELSYNC
using ParrelSync;
#endif

using UnityEngine;

public static class ProfileData
{
#if UNITY_WEBGL && !UNITY_EDITOR
    private static Dictionary<string, object> settingsCache = new Dictionary<string, object>();
    private static bool isReady = false;
    private const string FileKey = "profile_data";
#endif

	public static async Task Setup()
	{
		TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
#if UNITY_WEBGL && !UNITY_EDITOR
        IndexedDBHelper.Create(GameInstance.gameDef.gameBackendName + "_DB", success =>
        {
            if (success)
            {
                IndexedDBHelper.instance.ReadData(FileKey, (string json) =>
                {
                    if (!string.IsNullOrEmpty(json))
                    {
                        try
                        {
                            settingsCache = JsonConvert.DeserializeObject<Dictionary<string, object>>(json)
                                            ?? new Dictionary<string, object>();
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[WebGL] Error loading settings: {ex.Message}");
                            settingsCache = new Dictionary<string, object>();
                        }
                    }
                    else
                    {
                        settingsCache = new Dictionary<string, object>();
                    }

                    isReady = true;
                    tcs.SetResult(true);
                });
            }
            else
            {
                Debug.LogError("[WebGL] Failed to open IndexedDB");
                tcs.SetResult(false);
            }
        });
#else
		tcs.SetResult( true );
#endif
		await tcs.Task;
	}

#if UNITY_EDITOR
	[UnityEditor.MenuItem( "Funk/Clear Profile" )]
	public static void MenuItem_ClearProfile()
	{
		PlayerPrefs.DeleteAll();
		PlayerPrefs.Save();
	}
#endif

	private static string GetFullName( string name )
	{
		return $"Profile.Settings.{name}(" +
#if UNITY_EDITOR && PARRELSYNC
			"Editor" + ( ClonesManager.IsClone() ? "." + ClonesManager.GetCurrentProject().name : "" )
#elif UNITY_EDITOR
			"Editor.Default"
#else
			"Default"
#endif
			+ $".{GameInstance.gameDef.gameBackendName})";
	}

	public static T GetSetting<T>( string name, bool encrypted = false )
	{
		string fullName = GetFullName( name );

		//Debug.Log( "Debug GetSetting = " + fullName );

#if UNITY_WEBGL && !UNITY_EDITOR
        if (!isReady)
        {
            Debug.LogWarning("[WebGL] ProfileData not ready yet");
            return default;
        }

        if (!settingsCache.TryGetValue(fullName, out var rawValue))
            return default;

        try
        {
            return rawValue is T val ? val : JsonConvert.DeserializeObject<T>(rawValue.ToString());
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WebGL] Error deserializing {fullName}: {ex.Message}");
            return default;
        }
#else
		if ( !PlayerPrefs.HasKey( fullName ) )
			return default;

		Type t = typeof( T );
		switch ( Type.GetTypeCode( t ) )
		{
			case TypeCode.Int32:
				return (T)(object)PlayerPrefs.GetInt( fullName, -1 );
			case TypeCode.Boolean:
				return (T)(object)( PlayerPrefs.GetInt( fullName ) > 0 );
			case TypeCode.String:
				return (T)(object)PlayerPrefs.GetString( fullName );
		}

		string str = PlayerPrefs.GetString( fullName );
		if ( string.IsNullOrEmpty( str ) )
			return default;

		try
		{
			return encrypted ? DeserializeFromBinaryBase64<T>( str ) : JsonConvert.DeserializeObject<T>( str );
		}
		catch ( Exception ex )
		{
			Debug.LogWarning( $"Error deserializing setting: {ex.Message}" );
			return default;
		}
#endif
	}

	public static void SaveSetting( string name, object value, bool encrypt = false )
	{
		string fullName = GetFullName( name );

#if UNITY_WEBGL && !UNITY_EDITOR
        if (!isReady)
        {
            Debug.LogWarning("[WebGL] ProfileData not ready yet");
            return;
        }

        if (value == null)
        {
            settingsCache.Remove(fullName);
        }
        else
        {
            settingsCache[fullName] = value;
        }

        string json = JsonConvert.SerializeObject(settingsCache, Formatting.Indented);
        IndexedDBHelper.instance.WriteData(FileKey, json, success =>
        {
            if (!success)
            {
                Debug.LogWarning("[WebGL] Failed to write to IndexedDB");
            }
        });
#else
		if ( value == null )
		{
			PlayerPrefs.DeleteKey( fullName );
			PlayerPrefs.Save();
			return;
		}

		switch ( value )
		{
			case int intValue:
				PlayerPrefs.SetInt( fullName, intValue );
				break;
			case bool boolValue:
				PlayerPrefs.SetInt( fullName, boolValue ? 1 : 0 );
				break;
			case string stringValue:
				PlayerPrefs.SetString( fullName, stringValue );
				break;
			default:
				try
				{
					string serialized = encrypt ? SerializeToBinaryBase64( value ) : JsonConvert.SerializeObject( value );

					PlayerPrefs.SetString( fullName, serialized );
				}
				catch ( Exception ex )
				{
					Debug.LogWarning( $"Error saving setting: {ex.Message}" );
					return;
				}
				break;
		}
		PlayerPrefs.Save();
#endif
	}

	private static string SerializeToBinaryBase64( object obj )
	{
		using MemoryStream ms = new MemoryStream();
		BinaryFormatter formatter = new BinaryFormatter();
		formatter.Serialize( ms, obj );

		byte[] data = ms.ToArray();
		byte[] key = GenerateKey( SystemInfo.deviceUniqueIdentifier );
		byte[] encrypted = EncryptDecrypt( data, key );

		byte[] IV = new byte[ 16 ];
		using ( var rng = new RNGCryptoServiceProvider() )
			rng.GetBytes( IV );

		byte[] result = new byte[ IV.Length + encrypted.Length ];
		Buffer.BlockCopy( IV, 0, result, 0, IV.Length );
		Buffer.BlockCopy( encrypted, 0, result, IV.Length, encrypted.Length );

		return Convert.ToBase64String( result );
	}

	private static T DeserializeFromBinaryBase64<T>( string base64 )
	{
		byte[] data = Convert.FromBase64String( base64 );

		if ( data.Length <= 16 )
			throw new Exception( "Invalid data" );

		int encryptedLength = data.Length - 16;
		byte[] encrypted = new byte[ encryptedLength ];
		Buffer.BlockCopy( data, 16, encrypted, 0, encryptedLength );

		byte[] key = GenerateKey( SystemInfo.deviceUniqueIdentifier );
		byte[] decrypted = EncryptDecrypt( encrypted, key );

		using MemoryStream ms = new MemoryStream( decrypted );
		BinaryFormatter formatter = new BinaryFormatter();
		return (T)formatter.Deserialize( ms );
	}

	private static byte[] GenerateKey( string userId, int keyLength = 8 )
	{
		using MD5 md5 = MD5.Create();
		byte[] hash = md5.ComputeHash( Encoding.UTF8.GetBytes( userId ) );
		byte[] key = new byte[ keyLength ];
		Array.Copy( hash, key, keyLength );
		return key;
	}

	private static byte[] EncryptDecrypt( byte[] data, byte[] key )
	{
		byte[] result = new byte[ data.Length ];
		for ( int i = 0; i < data.Length; i++ )
		{
			result[ i ] = (byte)( data[ i ] ^ key[ i % key.Length ] );
		}
		return result;
	}

	public static ProfileSnapshot ExportSnapshot<T>()
	{
		ProfileSnapshot snapshot = new ProfileSnapshot
		{
			version = 1,
			timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
			deviceId = SystemInfo.deviceUniqueIdentifier,
			data = ""
		};

		string storageKey = !string.IsNullOrEmpty( GameInstance.LocalGameDataStorageKey )
			? GameInstance.LocalGameDataStorageKey
			: GameInstance.gameInstanceDef.GameLocalDataName;

		string json = JsonConvert.SerializeObject( GetSetting<T>( storageKey ) );

		snapshot.data = json;
		snapshot.checksum = ProfileChecksum.Compute( snapshot.data, snapshot.version );

		return snapshot;
	}

	public static void ImportSnapshot<T>( ProfileSnapshot snapshot )
	{
		T deserializedObject = JsonConvert.DeserializeObject<T>( snapshot.data );

		string storageKey = !string.IsNullOrEmpty( GameInstance.LocalGameDataStorageKey )
			? GameInstance.LocalGameDataStorageKey
			: GameInstance.gameInstanceDef.GameLocalDataName;

		SaveSetting( storageKey, deserializedObject );

		if ( typeof( T ) == typeof( LocalGameData ) )
			GameInstance.LocalGameData = (LocalGameData)(object)deserializedObject;
	}
}
