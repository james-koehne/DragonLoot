#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>One flattened game-text spreadsheet row.</summary>
public sealed class GameTextRow
{
	public string Category = string.Empty;
	public string Id = string.Empty;
	public string Field = string.Empty;
	public string Text = string.Empty;
	public string AssetPath = string.Empty;
}

/// <summary>RFC4180 CSV read/write for <see cref="GameTextRow"/>.</summary>
public static class GameTextCsv
{
	public const string Header = "Category,Id,Field,Text,AssetPath";

	public static string Write( IReadOnlyList<GameTextRow> rows )
	{
		StringBuilder sb = new StringBuilder( rows.Count * 64 + 64 );
		sb.AppendLine( Header );
		for ( int i = 0; i < rows.Count; i++ )
		{
			GameTextRow row = rows[i];
			sb.Append( Escape( row.Category ) );
			sb.Append( ',' );
			sb.Append( Escape( row.Id ) );
			sb.Append( ',' );
			sb.Append( Escape( row.Field ) );
			sb.Append( ',' );
			sb.Append( Escape( row.Text ) );
			sb.Append( ',' );
			sb.Append( Escape( row.AssetPath ) );
			sb.AppendLine();
		}

		return sb.ToString();
	}

	public static void WriteFile( string path, IReadOnlyList<GameTextRow> rows )
	{
		File.WriteAllText( path, Write( rows ), new UTF8Encoding( encoderShouldEmitUTF8Identifier: true ) );
	}

	public static List<GameTextRow> ReadFile( string path, out int parseErrors )
	{
		parseErrors = 0;
		string content = File.ReadAllText( path, Encoding.UTF8 );
		if ( !string.IsNullOrEmpty( content ) && content[0] == '\uFEFF' )
			content = content.Substring( 1 );

		List<string[]> records = ParseRecords( content );
		List<GameTextRow> rows = new List<GameTextRow>( Math.Max( 0, records.Count - 1 ) );
		if ( records.Count == 0 )
			return rows;

		int start = 0;
		if ( IsHeader( records[0] ) )
			start = 1;

		for ( int i = start; i < records.Count; i++ )
		{
			string[] cols = records[i];
			if ( cols.Length == 1 && string.IsNullOrWhiteSpace( cols[0] ) )
				continue;

			if ( cols.Length < 4 )
			{
				parseErrors++;
				continue;
			}

			GameTextRow row = new GameTextRow
			{
				Category = cols[0].Trim(),
				Id = cols[1],
				Field = cols[2].Trim(),
				Text = cols[3],
				AssetPath = cols.Length > 4 ? cols[4] : string.Empty
			};

			if ( string.IsNullOrEmpty( row.Category ) || string.IsNullOrEmpty( row.Id ) || string.IsNullOrEmpty( row.Field ) )
			{
				parseErrors++;
				continue;
			}

			rows.Add( row );
		}

		return rows;
	}

	static bool IsHeader( string[] cols )
	{
		if ( cols.Length < 4 )
			return false;
		return string.Equals( cols[0].Trim(), "Category", StringComparison.OrdinalIgnoreCase )
			&& string.Equals( cols[1].Trim(), "Id", StringComparison.OrdinalIgnoreCase )
			&& string.Equals( cols[2].Trim(), "Field", StringComparison.OrdinalIgnoreCase )
			&& string.Equals( cols[3].Trim(), "Text", StringComparison.OrdinalIgnoreCase );
	}

	static string Escape( string value )
	{
		if ( value == null )
			value = string.Empty;

		bool needsQuotes = false;
		for ( int i = 0; i < value.Length; i++ )
		{
			char c = value[i];
			if ( c == '"' || c == ',' || c == '\n' || c == '\r' )
			{
				needsQuotes = true;
				break;
			}
		}

		if ( !needsQuotes )
			return value;

		StringBuilder sb = new StringBuilder( value.Length + 8 );
		sb.Append( '"' );
		for ( int i = 0; i < value.Length; i++ )
		{
			char c = value[i];
			if ( c == '"' )
				sb.Append( '"' );
			sb.Append( c );
		}

		sb.Append( '"' );
		return sb.ToString();
	}

	static List<string[]> ParseRecords( string content )
	{
		List<string[]> records = new List<string[]>();
		List<string> fields = new List<string>( 5 );
		StringBuilder field = new StringBuilder();
		bool inQuotes = false;
		int i = 0;
		while ( i < content.Length )
		{
			char c = content[i];
			if ( inQuotes )
			{
				if ( c == '"' )
				{
					if ( i + 1 < content.Length && content[i + 1] == '"' )
					{
						field.Append( '"' );
						i += 2;
						continue;
					}

					inQuotes = false;
					i++;
					continue;
				}

				field.Append( c );
				i++;
				continue;
			}

			if ( c == '"' )
			{
				inQuotes = true;
				i++;
				continue;
			}

			if ( c == ',' )
			{
				fields.Add( field.ToString() );
				field.Length = 0;
				i++;
				continue;
			}

			if ( c == '\r' )
			{
				i++;
				if ( i < content.Length && content[i] == '\n' )
					i++;
				fields.Add( field.ToString() );
				field.Length = 0;
				records.Add( fields.ToArray() );
				fields.Clear();
				continue;
			}

			if ( c == '\n' )
			{
				i++;
				fields.Add( field.ToString() );
				field.Length = 0;
				records.Add( fields.ToArray() );
				fields.Clear();
				continue;
			}

			field.Append( c );
			i++;
		}

		if ( field.Length > 0 || fields.Count > 0 )
		{
			fields.Add( field.ToString() );
			records.Add( fields.ToArray() );
		}

		return records;
	}
}
#endif
