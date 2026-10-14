using System;
using System.IO;
using System.Text;

using UnityEngine;

public static class GameSavePaths
{
	public const string QuickSaveSlotId = "quicksave";
	const string SavesFolderName = "Saves";
	const string Extension = ".json";

	public static string SavesDirectory
	{
		get
		{
			string path = Path.Combine( Application.persistentDataPath, SavesFolderName );
			if ( !Directory.Exists( path ) )
				Directory.CreateDirectory( path );
			return path;
		}
	}

	public static string GetSlotFilePath( string slotId )
	{
		string safe = SanitizeSlotId( slotId );
		return Path.Combine( SavesDirectory, safe + Extension );
	}

	public static string SanitizeSlotId( string slotId )
	{
		if ( string.IsNullOrWhiteSpace( slotId ) )
			return "slot";

		StringBuilder sb = new StringBuilder( slotId.Length );
		for ( int i = 0; i < slotId.Length; i++ )
		{
			char c = slotId[ i ];
			if ( char.IsLetterOrDigit( c ) || c == '_' || c == '-' )
				sb.Append( c );
			else if ( char.IsWhiteSpace( c ) )
				sb.Append( '_' );
		}

		string result = sb.ToString();
		if ( string.IsNullOrEmpty( result ) )
			return "slot";
		return result;
	}

	public static string SlotIdFromFileName( string fileName )
	{
		if ( string.IsNullOrEmpty( fileName ) )
			return string.Empty;
		if ( fileName.EndsWith( Extension, StringComparison.OrdinalIgnoreCase ) )
			return fileName.Substring( 0, fileName.Length - Extension.Length );
		return fileName;
	}
}
