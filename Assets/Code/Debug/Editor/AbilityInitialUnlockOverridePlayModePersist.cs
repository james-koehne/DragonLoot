#if UNITY_EDITOR
using System.Text;

using UnityEditor;
using UnityEngine;

/// <summary>
/// Play Mode edits to <see cref="DebugDefinition.abilityInitialUnlockOverrides"/> revert on exit.
/// Capture them while exiting Play Mode and write back onto the authored asset in edit mode.
/// </summary>
public static class AbilityInitialUnlockOverridePlayModePersist
{
	const string PrefsKey = "DragonLoot.AbilityInitialUnlockOverrides";
	const string PrefsHasKey = "DragonLoot.AbilityInitialUnlockOverrides.Has";

	[InitializeOnLoadMethod]
	static void Init()
	{
		EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
		EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
	}

	static void OnPlayModeStateChanged( PlayModeStateChange state )
	{
		if ( state == PlayModeStateChange.ExitingPlayMode )
			CaptureFromRuntime();
		else if ( state == PlayModeStateChange.EnteredEditMode )
			FlushToAsset();
	}

	static void CaptureFromRuntime()
	{
		DebugDefinition runtime = null;
		if ( GameMode.Instance != null )
			runtime = GameMode.Instance.DebugDefinition;
		if ( runtime == null )
			return;

		EditorPrefs.SetBool( PrefsHasKey, true );
		EditorPrefs.SetString( PrefsKey, Serialize( runtime ) );
	}

	static void FlushToAsset()
	{
		if ( !EditorPrefs.GetBool( PrefsHasKey, false ) )
			return;

		string serialized = EditorPrefs.GetString( PrefsKey, string.Empty );
		EditorPrefs.DeleteKey( PrefsHasKey );
		EditorPrefs.DeleteKey( PrefsKey );

		string[] guids = AssetDatabase.FindAssets( "t:DebugDefinition" );
		if ( guids == null || guids.Length == 0 )
			return;

		for ( int i = 0; i < guids.Length; i++ )
		{
			string path = AssetDatabase.GUIDToAssetPath( guids[ i ] );
			DebugDefinition asset = AssetDatabase.LoadAssetAtPath<DebugDefinition>( path );
			if ( asset == null )
				continue;

			ApplySerialized( asset, serialized );
			EditorUtility.SetDirty( asset );
		}

		AssetDatabase.SaveAssets();
	}

	static string Serialize( DebugDefinition def )
	{
		var sb = new StringBuilder();
		if ( def.abilityInitialUnlockOverrides == null )
			return string.Empty;

		for ( int i = 0; i < def.abilityInitialUnlockOverrides.Count; i++ )
		{
			AbilityInitialUnlockOverrideEntry entry = def.abilityInitialUnlockOverrides[ i ];
			if ( entry == null || string.IsNullOrEmpty( entry.abilityId ) )
				continue;
			if ( entry.overrideState == AbilityInitialUnlockOverride.Unset )
				continue;
			if ( sb.Length > 0 )
				sb.Append( '|' );
			sb.Append( entry.abilityId );
			sb.Append( '=' );
			sb.Append( (int)entry.overrideState );
		}

		return sb.ToString();
	}

	static void ApplySerialized( DebugDefinition asset, string serialized )
	{
		if ( asset.abilityInitialUnlockOverrides == null )
			asset.abilityInitialUnlockOverrides = new System.Collections.Generic.List<AbilityInitialUnlockOverrideEntry>();
		else
			asset.abilityInitialUnlockOverrides.Clear();

		string[] parts = serialized.Split( '|' );
		for ( int i = 0; i < parts.Length; i++ )
		{
			string part = parts[ i ];
			if ( string.IsNullOrEmpty( part ) )
				continue;
			int eq = part.IndexOf( '=' );
			if ( eq <= 0 || eq >= part.Length - 1 )
				continue;
			string id = part.Substring( 0, eq );
			if ( !int.TryParse( part.Substring( eq + 1 ), out int stateInt ) )
				continue;
			if ( stateInt == (int)AbilityInitialUnlockOverride.Unset )
				continue;
			asset.abilityInitialUnlockOverrides.Add( new AbilityInitialUnlockOverrideEntry
			{
				abilityId = id,
				overrideState = (AbilityInitialUnlockOverride)stateInt
			} );
		}
	}
}
#endif
