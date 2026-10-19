#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Task ID generation and Markdown write helpers for Inbox / Features / Archive.
/// Never silently overwrites existing file bodies for templates or stubs.
/// </summary>
public static class ProjectTasksMarkdown
{
	public const string AutoFeatureName = "Auto";
	public const string ColourMetaKey = "Colour";

	static readonly Regex InvalidFileNameChars = new Regex( @"[<>:""/\\|?*\x00-\x1F]", RegexOptions.Compiled );
	static readonly Regex ColourMetaLine = new Regex(
		@"^[-\*]\s*Colou?r\s*:\s*(#[0-9A-Fa-f]{6})\s*$",
		RegexOptions.Multiline | RegexOptions.Compiled );

	public static string NewTaskId()
	{
		string date = DateTime.UtcNow.ToString( "yyyyMMdd", CultureInfo.InvariantCulture );
		string hex = Guid.NewGuid().ToString( "N" ).Substring( 0, 8 );
		return "TASK-" + date + "-" + hex;
	}

	public static string SanitizeFeatureFileName( string featureName )
	{
		if ( string.IsNullOrWhiteSpace( featureName ) )
			return "Feature";

		string name = featureName.Trim();
		name = InvalidFileNameChars.Replace( name, "" );
		name = name.Trim( '.', ' ' );
		if ( string.IsNullOrEmpty( name ) )
			return "Feature";
		return name;
	}

	public static string FeatureFileName( string featureName )
	{
		return SanitizeFeatureFileName( featureName ) + ".md";
	}

	public static string BuildInboxMarkdown( string taskId, string featureName, string description, DateTime createdUtc )
	{
		string feature = string.IsNullOrWhiteSpace( featureName ) ? AutoFeatureName : featureName.Trim();
		string body = description != null ? description.Trim() : string.Empty;
		string created = createdUtc.ToUniversalTime().ToString( "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture );

		var sb = new StringBuilder();
		sb.Append( "# " ).Append( taskId ).Append( '\n' );
		sb.Append( '\n' );
		sb.Append( "- Status: Inbox\n" );
		sb.Append( "- Feature: " ).Append( feature ).Append( '\n' );
		sb.Append( "- Created: " ).Append( created ).Append( '\n' );
		sb.Append( '\n' );
		sb.Append( "## Description\n" );
		sb.Append( '\n' );
		sb.Append( body ).Append( '\n' );
		sb.Append( '\n' );
		sb.Append( "## Capture Notes\n" );
		sb.Append( '\n' );
		return NormalizeNewlines( sb.ToString() );
	}

	public static bool WriteNewFileIfMissing( string absolutePath, string contents )
	{
		if ( string.IsNullOrWhiteSpace( absolutePath ) )
			return false;
		if ( File.Exists( absolutePath ) )
			return false;

		string directory = Path.GetDirectoryName( absolutePath );
		if ( !string.IsNullOrEmpty( directory ) && !Directory.Exists( directory ) )
			Directory.CreateDirectory( directory );

		WriteUtf8( absolutePath, contents );
		return true;
	}

	public static void WriteUtf8( string absolutePath, string contents )
	{
		string directory = Path.GetDirectoryName( absolutePath );
		if ( !string.IsNullOrEmpty( directory ) && !Directory.Exists( directory ) )
			Directory.CreateDirectory( directory );

		string normalized = NormalizeNewlines( contents ?? string.Empty );
		File.WriteAllText( absolutePath, normalized, new UTF8Encoding( encoderShouldEmitUTF8Identifier: false ) );
	}

	public static string NormalizeNewlines( string text )
	{
		if ( text == null )
			return string.Empty;
		string normalized = text.Replace( "\r\n", "\n" ).Replace( '\r', '\n' );
		if ( !normalized.EndsWith( "\n", StringComparison.Ordinal ) )
			normalized += "\n";
		return normalized;
	}

	public static string ReadAllTextOrEmpty( string absolutePath )
	{
		if ( string.IsNullOrWhiteSpace( absolutePath ) || !File.Exists( absolutePath ) )
			return string.Empty;
		try
		{
			return File.ReadAllText( absolutePath, Encoding.UTF8 );
		}
		catch
		{
			return string.Empty;
		}
	}

	public static string ExtractMetaValue( string markdown, string key )
	{
		if ( string.IsNullOrEmpty( markdown ) || string.IsNullOrEmpty( key ) )
			return string.Empty;

		string pattern = @"^[-\*]\s*" + Regex.Escape( key ) + @"\s*:\s*(.*)\s*$";
		Match match = Regex.Match( markdown, pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase );
		if ( !match.Success )
			return string.Empty;
		return match.Groups[1].Value.Trim();
	}

	public static string ExtractSection( string markdown, string heading )
	{
		if ( string.IsNullOrEmpty( markdown ) || string.IsNullOrEmpty( heading ) )
			return string.Empty;

		string pattern = @"^##\s+" + Regex.Escape( heading ) + @"\s*$";
		Match start = Regex.Match( markdown, pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase );
		if ( !start.Success )
			return string.Empty;

		int contentStart = start.Index + start.Length;
		Match next = Regex.Match( markdown.Substring( contentStart ), @"^##\s+", RegexOptions.Multiline );
		string body = next.Success
			? markdown.Substring( contentStart, next.Index )
			: markdown.Substring( contentStart );
		return body.Trim();
	}

	public static string ExtractTitle( string markdown, string fallback )
	{
		if ( !string.IsNullOrEmpty( markdown ) )
		{
			Match match = Regex.Match( markdown, @"^#\s+(.+)\s*$", RegexOptions.Multiline );
			if ( match.Success )
				return match.Groups[1].Value.Trim();
		}

		return fallback ?? string.Empty;
	}

	public static int CountOutstandingTaskHeadings( string markdown )
	{
		string section = ExtractSection( markdown, "Outstanding Tasks" );
		if ( string.IsNullOrEmpty( section ) )
			return 0;

		MatchCollection matches = Regex.Matches( section, @"^###\s+TASK-", RegexOptions.Multiline );
		return matches.Count;
	}

	public const string VerificationSectionHeading = "Verification";

	static readonly Regex VerificationChecklistLine = new Regex(
		@"^(\s*[-*]\s+)\[([ xX])\](\s*)(.*)$",
		RegexOptions.Multiline | RegexOptions.Compiled );

	public static List<ProjectTasksVerificationParseItem> ParseVerificationItems( string markdown )
	{
		var items = new List<ProjectTasksVerificationParseItem>();
		int sectionStart;
		int contentStart;
		int contentEnd;
		if ( !TryGetSectionBounds( markdown, VerificationSectionHeading, out sectionStart, out contentStart, out contentEnd ) )
			return items;

		string sectionBody = markdown.Substring( contentStart, contentEnd - contentStart );
		MatchCollection matches = VerificationChecklistLine.Matches( sectionBody );
		for ( int i = 0; i < matches.Count; i++ )
		{
			Match match = matches[i];
			string marker = match.Groups[2].Value;
			bool complete = string.Equals( marker, "x", StringComparison.OrdinalIgnoreCase );
			string text = match.Groups[4].Value.Trim();
			if ( string.IsNullOrEmpty( text ) )
				continue;

			items.Add( new ProjectTasksVerificationParseItem
			{
				Text = text,
				IsComplete = complete,
				AbsoluteMatchIndex = contentStart + match.Index,
				MatchLength = match.Length
			} );
		}

		return items;
	}

	public static bool TrySetVerificationItemChecked( string absolutePath, int itemIndex, bool isComplete, out string message )
	{
		message = string.Empty;
		if ( string.IsNullOrWhiteSpace( absolutePath ) || !File.Exists( absolutePath ) )
		{
			message = "Feature file not found.";
			return false;
		}

		string markdown = ReadAllTextOrEmpty( absolutePath );
		List<ProjectTasksVerificationParseItem> items = ParseVerificationItems( markdown );
		if ( itemIndex < 0 || itemIndex >= items.Count )
		{
			message = "Verification item index out of range.";
			return false;
		}

		ProjectTasksVerificationParseItem item = items[itemIndex];
		Match match = VerificationChecklistLine.Match( markdown.Substring( item.AbsoluteMatchIndex, item.MatchLength ) );
		if ( !match.Success )
		{
			message = "Could not rewrite verification checkbox.";
			return false;
		}

		string prefix = match.Groups[1].Value;
		string spacer = match.Groups[3].Value;
		string text = match.Groups[4].Value;
		string marker = isComplete ? "x" : " ";
		string replacement = prefix + "[" + marker + "]" + spacer + text;

		string updated = markdown.Substring( 0, item.AbsoluteMatchIndex )
		                 + replacement
		                 + markdown.Substring( item.AbsoluteMatchIndex + item.MatchLength );
		WriteUtf8( absolutePath, updated );
		message = isComplete ? "Marked verified." : "Marked pending.";
		return true;
	}

	public static int ClearCompletedVerificationItems( string absolutePath, out string message )
	{
		message = string.Empty;
		if ( string.IsNullOrWhiteSpace( absolutePath ) || !File.Exists( absolutePath ) )
		{
			message = "Feature file not found.";
			return 0;
		}

		string markdown = ReadAllTextOrEmpty( absolutePath );
		int sectionStart;
		int contentStart;
		int contentEnd;
		if ( !TryGetSectionBounds( markdown, VerificationSectionHeading, out sectionStart, out contentStart, out contentEnd ) )
		{
			message = "No Verification section.";
			return 0;
		}

		string sectionBody = markdown.Substring( contentStart, contentEnd - contentStart );
		var keptLines = new List<string>();
		int removed = 0;
		string[] lines = sectionBody.Replace( "\r\n", "\n" ).Replace( '\r', '\n' ).Split( '\n' );
		for ( int i = 0; i < lines.Length; i++ )
		{
			string line = lines[i];
			Match match = VerificationChecklistLine.Match( line );
			if ( match.Success && string.Equals( match.Groups[2].Value, "x", StringComparison.OrdinalIgnoreCase ) )
			{
				removed++;
				continue;
			}

			keptLines.Add( line );
		}

		if ( removed == 0 )
		{
			message = "No completed verification items to clear.";
			return 0;
		}

		while ( keptLines.Count > 0 && string.IsNullOrWhiteSpace( keptLines[0] ) )
			keptLines.RemoveAt( 0 );
		while ( keptLines.Count > 0 && string.IsNullOrWhiteSpace( keptLines[keptLines.Count - 1] ) )
			keptLines.RemoveAt( keptLines.Count - 1 );

		string newBody = keptLines.Count == 0 ? "\n" : "\n" + string.Join( "\n", keptLines ) + "\n";
		string updated = markdown.Substring( 0, contentStart ) + newBody + markdown.Substring( contentEnd );
		WriteUtf8( absolutePath, updated );
		message = "Cleared " + removed + " completed verification item(s).";
		return removed;
	}

	static bool TryGetSectionBounds( string markdown, string heading, out int sectionStart, out int contentStart, out int contentEnd )
	{
		sectionStart = -1;
		contentStart = -1;
		contentEnd = -1;
		if ( string.IsNullOrEmpty( markdown ) || string.IsNullOrEmpty( heading ) )
			return false;

		string pattern = @"^##\s+" + Regex.Escape( heading ) + @"\s*$";
		Match start = Regex.Match( markdown, pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase );
		if ( !start.Success )
			return false;

		sectionStart = start.Index;
		contentStart = start.Index + start.Length;
		Match next = Regex.Match( markdown.Substring( contentStart ), @"^##\s+", RegexOptions.Multiline );
		contentEnd = next.Success ? contentStart + next.Index : markdown.Length;
		return true;
	}

	public static Color ColorFromFeatureName( string featureName )
	{
		string key = string.IsNullOrWhiteSpace( featureName ) ? "Feature" : featureName.Trim();
		unchecked
		{
			int hash = 216613626;
			for ( int i = 0; i < key.Length; i++ )
			{
				hash ^= char.ToLowerInvariant( key[i] );
				hash *= 16777619;
			}

			float hue = ( ( hash & 0x7fffffff ) % 10000 ) / 10000f;
			// Spread hues; keep saturation/value readable on dark Editor UI.
			return Color.HSVToRGB( hue, 0.58f, 0.88f );
		}
	}

	public static string ColourToHex( Color color )
	{
		int r = Mathf.Clamp( Mathf.RoundToInt( color.r * 255f ), 0, 255 );
		int g = Mathf.Clamp( Mathf.RoundToInt( color.g * 255f ), 0, 255 );
		int b = Mathf.Clamp( Mathf.RoundToInt( color.b * 255f ), 0, 255 );
		return string.Format( CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", r, g, b );
	}

	public static bool TryParseColourHex( string hex, out Color color )
	{
		color = Color.white;
		if ( string.IsNullOrWhiteSpace( hex ) )
			return false;

		string value = hex.Trim();
		if ( value.StartsWith( "#", StringComparison.Ordinal ) )
			value = value.Substring( 1 );
		if ( value.Length != 6 )
			return false;

		byte r;
		byte g;
		byte b;
		if ( !byte.TryParse( value.Substring( 0, 2 ), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r )
		     || !byte.TryParse( value.Substring( 2, 2 ), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g )
		     || !byte.TryParse( value.Substring( 4, 2 ), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b ) )
			return false;

		color = new Color( r / 255f, g / 255f, b / 255f, 1f );
		return true;
	}

	public static string ExtractColourHex( string markdown )
	{
		if ( string.IsNullOrEmpty( markdown ) )
			return string.Empty;

		Match match = ColourMetaLine.Match( markdown );
		if ( match.Success )
			return match.Groups[1].Value;

		string meta = ExtractMetaValue( markdown, "Colour" );
		if ( string.IsNullOrEmpty( meta ) )
			meta = ExtractMetaValue( markdown, "Color" );
		return meta;
	}

	/// <summary>
	/// Ensures a Colour meta line exists on the feature file. Generates a stable colour from the name when missing.
	/// Returns the resolved colour. Writes the file only when Colour was absent.
	/// </summary>
	public static Color EnsureFeatureColour( string absolutePath, string featureName, string markdown, out string updatedMarkdown, out bool wroteFile )
	{
		updatedMarkdown = markdown ?? string.Empty;
		wroteFile = false;

		string existingHex = ExtractColourHex( updatedMarkdown );
		Color color;
		if ( TryParseColourHex( existingHex, out color ) )
			return color;

		color = ColorFromFeatureName( featureName );
		string hex = ColourToHex( color );
		updatedMarkdown = InsertColourMeta( updatedMarkdown, featureName, hex );

		if ( !string.IsNullOrWhiteSpace( absolutePath ) )
		{
			WriteUtf8( absolutePath, updatedMarkdown );
			wroteFile = true;
		}

		return color;
	}

	static string InsertColourMeta( string markdown, string featureName, string hex )
	{
		string colourLine = "- Colour: " + hex;
		if ( string.IsNullOrWhiteSpace( markdown ) )
		{
			string name = string.IsNullOrWhiteSpace( featureName ) ? "Feature" : featureName.Trim();
			return NormalizeNewlines( "# " + name + "\n\n" + colourLine + "\n" );
		}

		string text = markdown.Replace( "\r\n", "\n" ).Replace( '\r', '\n' );
		Match title = Regex.Match( text, @"^#\s+.+\s*$", RegexOptions.Multiline );
		if ( !title.Success )
			return NormalizeNewlines( colourLine + "\n\n" + text );

		int insertAt = title.Index + title.Length;
		string before = text.Substring( 0, insertAt ).TrimEnd( '\n', '\r' );
		string after = text.Substring( insertAt ).TrimStart( '\n', '\r' );
		return NormalizeNewlines( before + "\n\n" + colourLine + "\n\n" + after );
	}
}

public sealed class ProjectTasksVerificationParseItem
{
	public string Text = string.Empty;
	public bool IsComplete;
	public int AbsoluteMatchIndex;
	public int MatchLength;
}
#endif
