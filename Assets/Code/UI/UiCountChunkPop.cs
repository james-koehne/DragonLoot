using System.Collections.Generic;
using System.Text;

using UnityEngine;

/// <summary>
/// Punches color/size on the task line that contains an increased <c>current/required</c> count.
/// </summary>
public sealed class UiCountChunkPop
{
	public const float Duration = 0.18f;
	const float SizePunch = 0.2f;
	static readonly Color PunchColor = new Color( 1f, 0.92f, 0.55f, 1f );

	public struct Token
	{
		public int Start;
		public int Length;
		public int Current;
		public int Required;
	}

	readonly List<Token> _tokens = new List<Token>( 8 );
	readonly List<int> _indices = new List<int>( 4 );
	readonly List<Token> _lineSpans = new List<Token>( 4 );
	readonly List<Token> _previousScratch = new List<Token>( 8 );
	readonly List<Token> _nextScratch = new List<Token>( 8 );
	readonly StringBuilder _builder = new StringBuilder( 256 );
	string _baseText = string.Empty;
	float _elapsed;

	public bool Active { get; private set; }

	public bool Begin( string previousText, string nextText )
	{
		Stop();
		CollectIncreased( previousText, nextText, _previousScratch, _nextScratch, _indices );
		if ( _indices.Count == 0 )
			return false;

		_tokens.Clear();
		for ( int i = 0; i < _nextScratch.Count; i++ )
			_tokens.Add( _nextScratch[ i ] );

		_baseText = nextText ?? string.Empty;
		BuildLineSpans();
		if ( _lineSpans.Count == 0 )
		{
			Stop();
			return false;
		}

		_elapsed = 0.02f;
		Active = true;
		return true;
	}

	public string CurrentDisplay( int baseFontSize, Color restColor )
	{
		if ( !Active )
			return _baseText;
		return Apply( Amount( _elapsed ), baseFontSize, restColor );
	}

	public bool Tick( float deltaTime, int baseFontSize, Color restColor, out string display )
	{
		display = _baseText;
		if ( !Active )
			return false;

		_elapsed += Mathf.Max( 0f, deltaTime );
		if ( _elapsed >= Duration )
		{
			Stop();
			return false;
		}

		display = Apply( Amount( _elapsed ), baseFontSize, restColor );
		return true;
	}

	public void Stop()
	{
		Active = false;
		_elapsed = 0f;
		_indices.Clear();
		_tokens.Clear();
		_lineSpans.Clear();
	}

	static float Amount( float elapsed )
	{
		if ( elapsed <= 0f || elapsed >= Duration )
			return 0f;
		float t = elapsed / Duration;
		if ( t < 0.28f )
			return t / 0.28f;
		return 1f - ( ( t - 0.28f ) / 0.72f );
	}

	static void CollectIncreased( string previous, string next, List<Token> previousTokens, List<Token> nextTokens, List<int> into )
	{
		into.Clear();
		Collect( previous, previousTokens );
		Collect( next, nextTokens );
		int count = previousTokens.Count < nextTokens.Count ? previousTokens.Count : nextTokens.Count;
		for ( int i = 0; i < count; i++ )
		{
			Token previousToken = previousTokens[ i ];
			Token nextToken = nextTokens[ i ];
			if ( nextToken.Current <= previousToken.Current )
				continue;
			if ( nextToken.Required > 0 && nextToken.Current >= nextToken.Required )
				continue;
			into.Add( i );
		}
	}

	static void Collect( string text, List<Token> into )
	{
		into.Clear();
		if ( string.IsNullOrEmpty( text ) )
			return;

		int i = 0;
		while ( i < text.Length )
		{
			if ( text[ i ] == '<' )
			{
				int close = text.IndexOf( '>', i + 1 );
				if ( close < 0 )
					break;
				i = close + 1;
				continue;
			}

			if ( !char.IsDigit( text[ i ] ) )
			{
				i++;
				continue;
			}

			int start = i;
			while ( i < text.Length && char.IsDigit( text[ i ] ) )
				i++;
			if ( i >= text.Length || text[ i ] != '/' )
				continue;
			int slash = i;
			i++;
			if ( i >= text.Length || !char.IsDigit( text[ i ] ) )
				continue;
			while ( i < text.Length && char.IsDigit( text[ i ] ) )
				i++;

			int current;
			int required;
			if ( !int.TryParse( text.Substring( start, slash - start ), out current ) )
				continue;
			if ( !int.TryParse( text.Substring( slash + 1, i - slash - 1 ), out required ) )
				continue;

			Token token = new Token();
			token.Start = start;
			token.Length = i - start;
			token.Current = current;
			token.Required = required;
			into.Add( token );
		}
	}

	void BuildLineSpans()
	{
		_lineSpans.Clear();
		for ( int i = 0; i < _indices.Count; i++ )
		{
			int index = _indices[ i ];
			if ( index < 0 || index >= _tokens.Count )
				continue;

			int start;
			int length;
			GetLineSpan( _baseText, _tokens[ index ].Start, out start, out length );
			if ( length <= 0 )
				continue;

			bool duplicate = false;
			for ( int s = 0; s < _lineSpans.Count; s++ )
			{
				if ( _lineSpans[ s ].Start != start )
					continue;
				duplicate = true;
				break;
			}
			if ( duplicate )
				continue;

			Token span = new Token();
			span.Start = start;
			span.Length = length;
			_lineSpans.Add( span );
		}

		_lineSpans.Sort( CompareStartDescending );
	}

	static int CompareStartDescending( Token a, Token b )
	{
		return b.Start.CompareTo( a.Start );
	}

	static void GetLineSpan( string text, int index, out int start, out int length )
	{
		start = 0;
		length = 0;
		if ( string.IsNullOrEmpty( text ) || index < 0 || index >= text.Length )
			return;

		start = 0;
		if ( index > 0 )
		{
			int newline = text.LastIndexOf( '\n', index - 1 );
			start = newline < 0 ? 0 : newline + 1;
		}

		int end = text.IndexOf( '\n', index );
		if ( end < 0 )
			end = text.Length;
		length = end - start;
	}

	string Apply( float amount, int baseFontSize, Color restColor )
	{
		if ( string.IsNullOrEmpty( _baseText ) || _lineSpans.Count == 0 || amount <= 0.001f )
			return _baseText;

		_builder.Length = 0;
		_builder.Append( _baseText );
		for ( int i = 0; i < _lineSpans.Count; i++ )
		{
			Token span = _lineSpans[ i ];
			if ( span.Start < 0 || span.Length <= 0 || span.Start + span.Length > _builder.Length )
				continue;
			string line = _builder.ToString( span.Start, span.Length );
			_builder.Remove( span.Start, span.Length );
			_builder.Insert( span.Start, Wrap( line, amount, baseFontSize, restColor ) );
		}

		return _builder.ToString();
	}

	static string Wrap( string line, float amount, int baseFontSize, Color restColor )
	{
		int size = Mathf.Max( 1, Mathf.RoundToInt( baseFontSize * ( 1f + SizePunch * amount ) ) );
		Color color = Color.Lerp( restColor, PunchColor, amount );
		return "<size=" + size + "><color=#" + ColorUtility.ToHtmlStringRGB( color ) + ">" + line + "</color></size>";
	}
}
