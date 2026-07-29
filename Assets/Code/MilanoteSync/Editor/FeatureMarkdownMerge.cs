#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Merges importer-owned Feature markdown with preserved Cursor-owned sections.
/// Importer never overwrites Cursor Implementation / Files Modified / Testing /
/// Cursor Notes / Developer Verification content.
/// </summary>
public static class FeatureMarkdownMerge
{
	public static readonly string[] CursorSectionHeadings =
	{
		"Cursor Implementation",
		"Files Modified",
		"Testing Instructions",
		"Cursor Notes",
		"Developer Verification",
	};

	public static string Merge( string generatedMilanoteMarkdown, string existingFileMarkdown )
	{
		string generated = Normalize( generatedMilanoteMarkdown );
		Dictionary<string, string> preserved = ExtractCursorSections( existingFileMarkdown );

		var sb = new StringBuilder();
		sb.Append( StripTrailingCursorSections( generated ).TrimEnd() );
		sb.Append( '\n' );

		for ( int i = 0; i < CursorSectionHeadings.Length; i++ )
		{
			string heading = CursorSectionHeadings[i];
			sb.Append( '\n' );
			string block;
			if ( preserved.TryGetValue( heading, out block ) && !string.IsNullOrWhiteSpace( block ) )
				sb.Append( block.TrimEnd() );
			else
				sb.Append( BuildDefaultSection( heading ).TrimEnd() );
			sb.Append( '\n' );
		}

		return Normalize( sb.ToString() );
	}

	public static bool HasAllCursorSections( string markdown )
	{
		if ( string.IsNullOrEmpty( markdown ) )
			return false;

		string normalized = Normalize( markdown );
		for ( int i = 0; i < CursorSectionHeadings.Length; i++ )
		{
			if ( IndexOfHeading( normalized, CursorSectionHeadings[i] ) < 0 )
				return false;
		}

		return true;
	}

	public static Dictionary<string, string> ExtractCursorSections( string markdown )
	{
		var result = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		if ( string.IsNullOrEmpty( markdown ) )
			return result;

		string normalized = Normalize( markdown );
		for ( int i = 0; i < CursorSectionHeadings.Length; i++ )
		{
			string heading = CursorSectionHeadings[i];
			int start = IndexOfHeading( normalized, heading );
			if ( start < 0 )
				continue;

			int end = FindNextSectionStart( normalized, start + 1 );
			string block = end >= 0
				? normalized.Substring( start, end - start )
				: normalized.Substring( start );
			result[heading] = block.TrimEnd() + "\n";
		}

		return result;
	}

	static string BuildDefaultSection( string heading )
	{
		if ( string.Equals( heading, "Developer Verification", StringComparison.OrdinalIgnoreCase ) )
		{
			return "## Developer Verification\n"
				+ "\n"
				+ "Status\n"
				+ "\n"
				+ "☐ Not Tested\n"
				+ "\n"
				+ "☐ Testing\n"
				+ "\n"
				+ "☐ Verified\n"
				+ "\n"
				+ "Date\n"
				+ "\n"
				+ "\n"
				+ "Comments\n"
				+ "\n";
		}

		return "## " + heading + "\n\n";
	}

	static string StripTrailingCursorSections( string markdown )
	{
		if ( string.IsNullOrEmpty( markdown ) )
			return string.Empty;

		string normalized = Normalize( markdown );
		int earliest = -1;
		for ( int i = 0; i < CursorSectionHeadings.Length; i++ )
		{
			int idx = IndexOfHeading( normalized, CursorSectionHeadings[i] );
			if ( idx < 0 )
				continue;
			if ( earliest < 0 || idx < earliest )
				earliest = idx;
		}

		if ( earliest < 0 )
			return normalized;

		return normalized.Substring( 0, earliest ).TrimEnd() + "\n";
	}

	static int IndexOfHeading( string markdown, string heading )
	{
		string marker = "## " + heading;
		int index = 0;
		while ( index >= 0 && index < markdown.Length )
		{
			index = markdown.IndexOf( marker, index, StringComparison.OrdinalIgnoreCase );
			if ( index < 0 )
				return -1;

			bool atLineStart = index == 0 || markdown[index - 1] == '\n';
			int after = index + marker.Length;
			bool atHeadingEnd = after >= markdown.Length
				|| markdown[after] == '\n'
				|| markdown[after] == '\r'
				|| char.IsWhiteSpace( markdown[after] );

			if ( atLineStart && atHeadingEnd )
				return index;

			index = after;
		}

		return -1;
	}

	static int FindNextSectionStart( string markdown, int searchFrom )
	{
		int best = -1;
		int index = searchFrom;
		while ( index < markdown.Length )
		{
			int next = markdown.IndexOf( "\n## ", index, StringComparison.Ordinal );
			if ( next < 0 )
				break;

			int headingStart = next + 1;
			if ( best < 0 || headingStart < best )
				best = headingStart;
			break;
		}

		return best;
	}

	static string Normalize( string text )
	{
		if ( text == null )
			return string.Empty;
		return text.Replace( "\r\n", "\n" ).Replace( '\r', '\n' );
	}
}
#endif
