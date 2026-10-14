using System;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Shared IMGUI helpers for <see cref="DebugOverlay"/> sections.
/// </summary>
public static class DebugOverlayGui
{
	const string AdvancedPrefsPrefix = "DragonLoot.Debug.Advanced.";
	const float DefaultCacheInterval = 1f;

	static readonly List<int> s_matchBuffer = new List<int>( 64 );

	public static void DrawSearchField( ref string search, string label = "Filter" )
	{
		if ( search == null )
			search = string.Empty;

		GUILayout.BeginHorizontal();
		GUILayout.Label( label, GUILayout.Width( 44f ) );
		string next = GUILayout.TextField( search );
		if ( next != search )
			search = next;
		if ( !string.IsNullOrEmpty( search ) && GUILayout.Button( "x", GUILayout.Width( 22f ) ) )
			search = string.Empty;
		GUILayout.EndHorizontal();
	}

	public static void DrawStatus( string status )
	{
		if ( string.IsNullOrEmpty( status ) )
			return;
		GUILayout.Label( status );
	}

	public static void DrawAmountPresets( ref int amount, int min, int max, params int[] presets )
	{
		DrawAmountPresets( "Amount", ref amount, min, max, presets );
	}

	public static void DrawAmountPresets( string label, ref int amount, int min, int max, params int[] presets )
	{
		GUILayout.Label( label + ": " + amount );
		if ( presets != null && presets.Length > 0 )
		{
			GUILayout.BeginHorizontal();
			for ( int i = 0; i < presets.Length; i++ )
			{
				int preset = presets[ i ];
				if ( GUILayout.Button( preset.ToString() ) )
					amount = Mathf.Clamp( preset, min, max );
			}
			GUILayout.EndHorizontal();
		}

		amount = Mathf.RoundToInt( GUILayout.HorizontalSlider( amount, min, max ) );
		amount = Mathf.Clamp( amount, min, max );
	}

	/// <summary>
	/// Search + ◀▶ picker over a list. Updates <paramref name="selectedIndex"/> to an index in <paramref name="items"/>.
	/// </summary>
	public static void DrawFilteredPicker<T>(
		IList<T> items,
		Func<T, string> getName,
		ref int selectedIndex,
		ref string search,
		string emptyLabel = "No items" )
	{
		if ( items == null || items.Count == 0 || getName == null )
		{
			GUILayout.Label( emptyLabel );
			return;
		}

		DrawSearchField( ref search );

		s_matchBuffer.Clear();
		string needle = search != null ? search.Trim() : string.Empty;
		bool hasNeedle = needle.Length > 0;
		for ( int i = 0; i < items.Count; i++ )
		{
			T item = items[ i ];
			if ( item == null )
				continue;
			string name = getName( item );
			if ( name == null )
				name = string.Empty;
			if ( hasNeedle && name.IndexOf( needle, StringComparison.OrdinalIgnoreCase ) < 0 )
				continue;
			s_matchBuffer.Add( i );
		}

		if ( s_matchBuffer.Count == 0 )
		{
			GUILayout.Label( "No matches" );
			return;
		}

		int matchPos = 0;
		for ( int i = 0; i < s_matchBuffer.Count; i++ )
		{
			if ( s_matchBuffer[ i ] == selectedIndex )
			{
				matchPos = i;
				break;
			}
		}

		if ( selectedIndex < 0 || selectedIndex >= items.Count ||
		     ( hasNeedle && ( selectedIndex >= items.Count || !MatchContains( selectedIndex ) ) ) )
		{
			matchPos = 0;
			selectedIndex = s_matchBuffer[ 0 ];
		}

		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "◀", GUILayout.Width( 28f ) ) )
		{
			matchPos = ( matchPos + s_matchBuffer.Count - 1 ) % s_matchBuffer.Count;
			selectedIndex = s_matchBuffer[ matchPos ];
		}

		string label = getName( items[ selectedIndex ] );
		if ( label == null )
			label = "(null)";
		GUILayout.Label( label + "  (" + ( matchPos + 1 ) + "/" + s_matchBuffer.Count + ")", GUILayout.MinWidth( 80f ) );

		if ( GUILayout.Button( "▶", GUILayout.Width( 28f ) ) )
		{
			matchPos = ( matchPos + 1 ) % s_matchBuffer.Count;
			selectedIndex = s_matchBuffer[ matchPos ];
		}
		GUILayout.EndHorizontal();
	}

	static bool MatchContains( int index )
	{
		for ( int i = 0; i < s_matchBuffer.Count; i++ )
		{
			if ( s_matchBuffer[ i ] == index )
				return true;
		}
		return false;
	}

	public static bool DrawAdvancedFoldout( string key, string label = "Advanced" )
	{
		if ( string.IsNullOrEmpty( key ) )
			key = label;

		string prefsKey = AdvancedPrefsPrefix + key;
		bool open = PlayerPrefs.GetInt( prefsKey, 0 ) != 0;
		string foldLabel = ( open ? "▼ " : "▶ " ) + label;
		bool next = GUILayout.Toggle( open, foldLabel );
		if ( next != open )
		{
			PlayerPrefs.SetInt( prefsKey, next ? 1 : 0 );
			PlayerPrefs.Save();
		}

		return next;
	}

	public static T[] CachedFind<T>( ref T[] cache, ref float lastTime, float interval = DefaultCacheInterval )
		where T : UnityEngine.Object
	{
		float now = Time.unscaledTime;
		if ( cache == null || now - lastTime >= interval )
		{
			cache = UnityEngine.Object.FindObjectsByType<T>( FindObjectsInactive.Exclude, FindObjectsSortMode.None );
			lastTime = now;
		}

		return cache;
	}
}
