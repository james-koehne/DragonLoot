using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

public class SecureStorageManager
{
	private static readonly int KeySize = 32;  // 256-bit AES key
	private static readonly int IVSize = 16;   // 128-bit IV for AES

	private async static Task<string> GetDeviceSalt()
	{
		string deviceIdentifier = "";

		await MainThreadDispatcher.RunOnMainThreadAsync( () =>
		{
			deviceIdentifier = SystemInfo.deviceUniqueIdentifier;
		} );

		if ( string.IsNullOrEmpty( deviceIdentifier ) )
		{
			throw new Exception( "GenerateKey invalid" );
		}

		return deviceIdentifier;
	}

	// Securely derive an encryption key using PBKDF2
	private static async Task<byte[]> GenerateKey( string salt )
	{
		string deviceIdentifier = await GetDeviceSalt();

		using ( var pbkdf2 = new Rfc2898DeriveBytes( deviceIdentifier, Encoding.UTF8.GetBytes( salt ), 100000, HashAlgorithmName.SHA256 ) )
		{
			return pbkdf2.GetBytes( KeySize );
		}
	}

	// Encrypt and store securely
	public static async Task StoreSecureString( string key, string value )
	{
		await Task.Run( async () =>
		{
			byte[] salt = GenerateRandomBytes( 16 );
			byte[] encryptionKey = await GenerateKey( Convert.ToBase64String( salt ) );
			byte[] iv = GenerateRandomBytes( IVSize );

			string encryptedValue = EncryptString( value, encryptionKey, iv, out byte[] hmac );

			string storedData = Convert.ToBase64String( salt ) + ":" + Convert.ToBase64String( iv ) + ":" + Convert.ToBase64String( hmac ) + ":" + encryptedValue;

			await MainThreadDispatcher.RunOnMainThreadAsync( () =>
			{
				ProfileData.SaveSetting( key, storedData );
			} );
		} );
	}

	// Retrieve and decrypt securely
	public static async Task<string> RetrieveSecureString( string key )
	{
		return await Task.Run( async () =>
		{
			string storedData = "";

			await MainThreadDispatcher.RunOnMainThreadAsync( () =>
			{
				storedData = ProfileData.GetSetting<string>( key );
			} );

			if ( string.IsNullOrEmpty( storedData ) )
				return "";

			string[] parts = storedData.Split( ':' );
			if ( parts.Length != 4 )
				return "";

			byte[] salt = Convert.FromBase64String( parts[ 0 ] );
			byte[] iv = Convert.FromBase64String( parts[ 1 ] );
			byte[] storedHmac = Convert.FromBase64String( parts[ 2 ] );
			byte[] encryptionKey = await GenerateKey( Convert.ToBase64String( salt ) );

			string decryptedValue = DecryptString( parts[ 3 ], encryptionKey, iv, storedHmac );
			return decryptedValue;
		} );
	}

	public static void ClearSecureString( string key )
	{
		ProfileData.SaveSetting( key, "" );
	}

	// AES-CBC Encryption with HMAC authentication
	private static string EncryptString( string plainText, byte[] key, byte[] iv, out byte[] hmac )
	{
		byte[] cipherText;

		using ( Aes aesAlg = Aes.Create() )
		{
			aesAlg.Key = key;
			aesAlg.IV = iv;
			aesAlg.Mode = CipherMode.CBC;
			aesAlg.Padding = PaddingMode.PKCS7;

			using ( ICryptoTransform encryptor = aesAlg.CreateEncryptor( aesAlg.Key, aesAlg.IV ) )
			using ( var msEncrypt = new MemoryStream() )
			using ( var csEncrypt = new CryptoStream( msEncrypt, encryptor, CryptoStreamMode.Write ) )
			using ( var swEncrypt = new StreamWriter( csEncrypt ) )
			{
				swEncrypt.Write( plainText );
				swEncrypt.Flush();
				csEncrypt.FlushFinalBlock();
				cipherText = msEncrypt.ToArray();
			}
		}

		hmac = ComputeHMAC( cipherText, key ); // Generate HMAC for integrity
		return Convert.ToBase64String( cipherText );
	}

	// AES-CBC Decryption with HMAC verification
	private static string DecryptString( string cipherText, byte[] key, byte[] iv, byte[] expectedHmac )
	{
		byte[] cipherBytes = Convert.FromBase64String( cipherText );

		// Verify integrity using HMAC
		byte[] computedHmac = ComputeHMAC( cipherBytes, key );
		if ( !CompareBytes( computedHmac, expectedHmac ) )
		{
			Debug.LogError( "Data integrity check failed! Possible tampering detected." );
			return "";
		}

		using ( Aes aesAlg = Aes.Create() )
		{
			aesAlg.Key = key;
			aesAlg.IV = iv;
			aesAlg.Mode = CipherMode.CBC;
			aesAlg.Padding = PaddingMode.PKCS7;

			using ( ICryptoTransform decryptor = aesAlg.CreateDecryptor( aesAlg.Key, aesAlg.IV ) )
			using ( var msDecrypt = new MemoryStream( cipherBytes ) )
			using ( var csDecrypt = new CryptoStream( msDecrypt, decryptor, CryptoStreamMode.Read ) )
			using ( var srDecrypt = new StreamReader( csDecrypt ) )
			{
				return srDecrypt.ReadToEnd();
			}
		}
	}

	// Compute HMAC-SHA256 for authentication
	private static byte[] ComputeHMAC( byte[] data, byte[] key )
	{
		using ( var hmac = new HMACSHA256( key ) )
		{
			return hmac.ComputeHash( data );
		}
	}

	// Constant-time comparison to prevent timing attacks
	private static bool CompareBytes( byte[] a, byte[] b )
	{
		if ( a.Length != b.Length )
			return false;
		int result = 0;
		for ( int i = 0; i < a.Length; i++ )
		{
			result |= a[ i ] ^ b[ i ];
		}
		return result == 0;
	}

	// Generate a random byte array
	private static byte[] GenerateRandomBytes( int size )
	{
		byte[] bytes = new byte[ size ];
		using ( var rng = new RNGCryptoServiceProvider() )
		{
			rng.GetBytes( bytes );
		}
		return bytes;
	}
}
