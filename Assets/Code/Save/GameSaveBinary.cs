using System;

/// <summary>Base64 helpers for compact binary blobs inside JSON save slots.</summary>
public static class GameSaveBinary
{
	public static string EncodeBytes( byte[] data )
	{
		if ( data == null || data.Length == 0 )
			return string.Empty;
		return Convert.ToBase64String( data );
	}

	public static byte[] DecodeBytes( string base64 )
	{
		if ( string.IsNullOrEmpty( base64 ) )
			return Array.Empty<byte>();
		return Convert.FromBase64String( base64 );
	}

	public static string EncodeU16( ushort[] data )
	{
		if ( data == null || data.Length == 0 )
			return string.Empty;

		byte[] bytes = new byte[ data.Length * 2 ];
		Buffer.BlockCopy( data, 0, bytes, 0, bytes.Length );
		return Convert.ToBase64String( bytes );
	}

	public static ushort[] DecodeU16( string base64 )
	{
		if ( string.IsNullOrEmpty( base64 ) )
			return Array.Empty<ushort>();

		byte[] bytes = Convert.FromBase64String( base64 );
		if ( ( bytes.Length & 1 ) != 0 )
			return Array.Empty<ushort>();

		ushort[] data = new ushort[ bytes.Length / 2 ];
		Buffer.BlockCopy( bytes, 0, data, 0, bytes.Length );
		return data;
	}
}
