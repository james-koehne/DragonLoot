#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Raw Milanote board API response shapes. Elements are id-keyed dictionaries.
/// </summary>
public sealed class MilanoteBoardResponse
{
	[JsonProperty( "elements" )]
	public Dictionary<string, JObject> Elements;

	[JsonProperty( "comments" )]
	public Dictionary<string, JObject> Comments;

	[JsonProperty( "errors" )]
	public Dictionary<string, JObject> Errors;
}

public sealed class MilanoteParsedElement
{
	public string Id;
	public string ElementType;
	public string Title;
	public string TextContent;
	public bool IsComplete;
	public string ParentId;
	public string Section;
	public long PositionScore;
	public int PositionIndex = -1;
	/// <summary>Explicit indent from API when present; otherwise -1 (compute from parent chain).</summary>
	public int ExplicitIndent = -1;
	public JObject Raw;
}

public static class MilanoteDtoParser
{
	public static MilanoteParsedElement ParseElement( JObject data )
	{
		if ( data == null )
			return null;

		var element = new MilanoteParsedElement
		{
			Id = data.Value<string>( "id" ),
			ElementType = data.Value<string>( "elementType" ),
			Raw = data
		};

		JToken location = data["location"];
		if ( location != null && location.Type != JTokenType.Null )
		{
			element.ParentId = location.Value<string>( "parentId" );
			element.Section = location.Value<string>( "section" );
			ReadPosition( location, element );
		}

		JToken content = data["content"];
		if ( content != null && content.Type != JTokenType.Null )
		{
			element.Title = content.Value<string>( "title" );
			if ( string.IsNullOrEmpty( element.Title ) )
				element.Title = content.Value<string>( "originalTitle" );

			element.IsComplete = ReadIsComplete( content );
			element.TextContent = ExtractTextContent( content["textContent"] );
			element.ExplicitIndent = ReadExplicitIndent( content );
		}

		return element;
	}

	static bool ReadIsComplete( JToken content )
	{
		if ( content == null || content.Type == JTokenType.Null )
			return false;

		JToken token = content["isComplete"];
		if ( token == null || token.Type == JTokenType.Null )
			token = content["complete"];
		if ( token == null || token.Type == JTokenType.Null )
			return false;

		if ( token.Type == JTokenType.Boolean )
			return token.Value<bool>();
		if ( token.Type == JTokenType.Integer )
			return token.Value<int>() != 0;
		if ( token.Type == JTokenType.Float )
			return token.Value<double>() != 0d;

		string text = token.ToString().Trim();
		return string.Equals( text, "true", StringComparison.OrdinalIgnoreCase )
		       || text == "1";
	}

	static int ReadExplicitIndent( JToken content )
	{
		string[] keys = { "indent", "indentation", "indentLevel", "indentationLevel", "level", "depth", "nestingLevel" };
		for ( int i = 0; i < keys.Length; i++ )
		{
			JToken token = content[keys[i]];
			if ( token == null || token.Type == JTokenType.Null )
				continue;
			if ( token.Type == JTokenType.Integer )
				return token.Value<int>();
			if ( int.TryParse( token.ToString(), out int value ) && value >= 0 )
				return value;
		}

		return -1;
	}

	public static string ExtractTextContent( JToken textContent )
	{
		if ( textContent == null || textContent.Type == JTokenType.Null )
			return null;

		if ( textContent.Type == JTokenType.String )
			return textContent.Value<string>();

		string direct = ReadDirectTextField( textContent );
		if ( !string.IsNullOrEmpty( direct ) )
			return direct;

		JToken blocks = textContent["blocks"];
		if ( blocks != null )
		{
			string fromBlocks = ExtractTextFromBlocks( blocks );
			if ( !string.IsNullOrEmpty( fromBlocks ) )
				return fromBlocks;
		}

		// ProseMirror / nested document trees
		JToken[] docRoots = { textContent["document"], textContent["doc"], textContent["content"] };
		for ( int i = 0; i < docRoots.Length; i++ )
		{
			if ( docRoots[i] == null || docRoots[i].Type == JTokenType.Null )
				continue;
			string fromDoc = CollectNestedText( docRoots[i] );
			if ( !string.IsNullOrEmpty( fromDoc ) )
				return fromDoc;
		}

		string recursive = CollectNestedText( textContent );
		if ( !string.IsNullOrEmpty( recursive ) )
			return recursive;

		return null;
	}

	static string ReadDirectTextField( JToken token )
	{
		string[] keys = { "text", "plainText", "value", "string" };
		for ( int i = 0; i < keys.Length; i++ )
		{
			JToken field = token[keys[i]];
			if ( field != null && field.Type == JTokenType.String )
			{
				string value = field.Value<string>();
				if ( !string.IsNullOrEmpty( value ) )
					return value;
			}
		}

		return null;
	}

	static string ExtractTextFromBlocks( JToken blocks )
	{
		var parts = new List<string>();

		if ( blocks.Type == JTokenType.Array )
		{
			foreach ( JToken block in blocks )
				AppendBlockText( parts, block );
		}
		else if ( blocks.Type == JTokenType.Object )
		{
			foreach ( JProperty prop in blocks.Children<JProperty>() )
				AppendBlockText( parts, prop.Value );
		}

		return parts.Count == 0 ? null : string.Join( "", parts );
	}

	static void AppendBlockText( List<string> parts, JToken block )
	{
		if ( block == null )
			return;

		string direct = ReadDirectTextField( block );
		if ( !string.IsNullOrEmpty( direct ) )
		{
			parts.Add( direct );
			return;
		}

		JToken[] nested = { block["content"], block["children"], block["nodes"], block["leaves"] };
		for ( int i = 0; i < nested.Length; i++ )
		{
			if ( nested[i] == null || nested[i].Type != JTokenType.Array )
				continue;

			foreach ( JToken child in nested[i] )
			{
				string childText = ReadDirectTextField( child );
				if ( !string.IsNullOrEmpty( childText ) )
					parts.Add( childText );
				else
					AppendBlockText( parts, child );
			}
		}
	}

	static string CollectNestedText( JToken token )
	{
		var parts = new List<string>();
		CollectNestedTextRecursive( token, parts, 0 );
		return parts.Count == 0 ? null : string.Join( "", parts );
	}

	static void CollectNestedTextRecursive( JToken token, List<string> parts, int depth )
	{
		if ( token == null || depth > 10 )
			return;

		if ( token.Type == JTokenType.Object )
		{
			string direct = ReadDirectTextField( token );
			if ( !string.IsNullOrEmpty( direct ) )
				parts.Add( direct );

			foreach ( JProperty prop in token.Children<JProperty>() )
			{
				if ( string.Equals( prop.Name, "entityMap", StringComparison.OrdinalIgnoreCase ) )
					continue;
				CollectNestedTextRecursive( prop.Value, parts, depth + 1 );
			}
		}
		else if ( token.Type == JTokenType.Array )
		{
			foreach ( JToken child in token )
				CollectNestedTextRecursive( child, parts, depth + 1 );
		}
	}

	static void ReadPosition( JToken location, MilanoteParsedElement element )
	{
		// Preferred: location.position.{index,score} (current Milanote API).
		JToken position = location["position"];
		if ( position != null && position.Type != JTokenType.Null )
		{
			element.PositionIndex = ParseInt( position["index"], -1 );
			element.PositionScore = ParseLong( position["score"], -1 );
			return;
		}

		// Legacy flat fields.
		element.PositionIndex = ParseInt( location["positionIndex"], -1 );
		element.PositionScore = ParseLong( location["positionScore"], -1 );
	}

	static int ParseInt( JToken token, int fallback )
	{
		if ( token == null || token.Type == JTokenType.Null )
			return fallback;
		if ( token.Type == JTokenType.Integer )
			return token.Value<int>();
		if ( int.TryParse( token.ToString(), out int value ) )
			return value;
		return fallback;
	}

	static long ParseLong( JToken token, long fallback )
	{
		if ( token == null || token.Type == JTokenType.Null )
			return fallback;
		if ( token.Type == JTokenType.Integer )
			return token.Value<long>();
		if ( long.TryParse( token.ToString(), out long value ) )
			return value;
		return fallback;
	}
}
#endif
