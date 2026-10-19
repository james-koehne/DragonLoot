using System;
using System.Collections.Generic;

using UnityEngine;

public enum AbilityInitialUnlockOverride
{
	Unset = 0,
	ForceLocked = 1,
	ForceUnlocked = 2
}

[Serializable]
public class AbilityInitialUnlockOverrideEntry
{
	public string abilityId;
	public AbilityInitialUnlockOverride overrideState = AbilityInitialUnlockOverride.Unset;
}

[CreateAssetMenu( fileName = "DebugDefinition", menuName = "Definitions/DebugDefinition" )]
public class DebugDefinition : ScriptableObject
{
	public bool debugLogging = false;
	public bool debugBackend = false;

	[Tooltip( "Editor only: on play, place the player at the Scene view camera instead of LevelSceneMarkers.playerSpawn." )]
	public bool spawnAtSceneCamera = false;

	[Tooltip( "Debug spawn id used on play/respawn. Empty = LevelSceneMarkers.playerSpawn (intro). Use a DebugSpawnPoint id, or volume_main_cave / volume_workshop / volume_artifact_museum / volume_coin_hall." )]
	public string debugSpawnId;

	[Tooltip( "Skip spawning coins, gems, and artifacts for all treasure piles in play mode." )]
	public bool disableTreasureSpawning = false;

	[Tooltip( "Skip contextual tutorial popups from gameplay triggers. Debug overlay force-show and pause replay still work. Honored in player builds." )]
	public bool disableTutorials = false;

	[Tooltip( "Per-ability bootstrap unlock overrides. When set, replaces AbilityDefinition.startsUnlocked if the profile has no entry." )]
	public List<AbilityInitialUnlockOverrideEntry> abilityInitialUnlockOverrides = new List<AbilityInitialUnlockOverrideEntry>();

	public static bool TreasureSpawningDisabled
	{
		get
		{
			if ( GameMode.Instance == null )
				return false;
			DebugDefinition def = GameMode.Instance.DebugDefinition;
			if ( def == null )
				return false;
			return def.disableTreasureSpawning;
		}
	}

	public static bool TutorialsDisabled
	{
		get
		{
			if ( GameMode.Instance == null )
				return false;
			DebugDefinition def = GameMode.Instance.DebugDefinition;
			if ( def == null )
				return false;
			return def.disableTutorials;
		}
	}

	public static DebugDefinition GetRuntime()
	{
		if ( GameMode.Instance == null )
			return null;
		return GameMode.Instance.DebugDefinition;
	}

	/// <summary>
	/// Returns true when a non-Unset override is configured for <paramref name="abilityId"/>.
	/// <paramref name="unlocked"/> is true for ForceUnlocked, false for ForceLocked.
	/// </summary>
	public static bool TryGetAbilityInitialUnlockOverride( string abilityId, out bool unlocked )
	{
		unlocked = false;
		DebugDefinition def = GetRuntime();
		if ( def == null )
			return false;
		return def.TryGetAbilityInitialUnlockOverrideInstance( abilityId, out unlocked );
	}

	public bool TryGetAbilityInitialUnlockOverrideInstance( string abilityId, out bool unlocked )
	{
		unlocked = false;
		if ( string.IsNullOrEmpty( abilityId ) || abilityInitialUnlockOverrides == null )
			return false;

		for ( int i = 0; i < abilityInitialUnlockOverrides.Count; i++ )
		{
			AbilityInitialUnlockOverrideEntry entry = abilityInitialUnlockOverrides[ i ];
			if ( entry == null || entry.abilityId != abilityId )
				continue;
			if ( entry.overrideState == AbilityInitialUnlockOverride.Unset )
				return false;
			unlocked = entry.overrideState == AbilityInitialUnlockOverride.ForceUnlocked;
			return true;
		}

		return false;
	}

	public AbilityInitialUnlockOverride GetAbilityInitialUnlockOverride( string abilityId )
	{
		if ( string.IsNullOrEmpty( abilityId ) || abilityInitialUnlockOverrides == null )
			return AbilityInitialUnlockOverride.Unset;

		for ( int i = 0; i < abilityInitialUnlockOverrides.Count; i++ )
		{
			AbilityInitialUnlockOverrideEntry entry = abilityInitialUnlockOverrides[ i ];
			if ( entry == null || entry.abilityId != abilityId )
				continue;
			return entry.overrideState;
		}

		return AbilityInitialUnlockOverride.Unset;
	}

	public void SetAbilityInitialUnlockOverride( string abilityId, AbilityInitialUnlockOverride state )
	{
		if ( string.IsNullOrEmpty( abilityId ) )
			return;

		if ( abilityInitialUnlockOverrides == null )
			abilityInitialUnlockOverrides = new List<AbilityInitialUnlockOverrideEntry>();

		for ( int i = 0; i < abilityInitialUnlockOverrides.Count; i++ )
		{
			AbilityInitialUnlockOverrideEntry entry = abilityInitialUnlockOverrides[ i ];
			if ( entry == null || entry.abilityId != abilityId )
				continue;

			if ( state == AbilityInitialUnlockOverride.Unset )
			{
				abilityInitialUnlockOverrides.RemoveAt( i );
				return;
			}

			entry.overrideState = state;
			return;
		}

		if ( state == AbilityInitialUnlockOverride.Unset )
			return;

		abilityInitialUnlockOverrides.Add( new AbilityInitialUnlockOverrideEntry
		{
			abilityId = abilityId,
			overrideState = state
		} );
	}

	public void CopyAbilityInitialUnlockOverridesFrom( DebugDefinition source )
	{
		if ( abilityInitialUnlockOverrides == null )
			abilityInitialUnlockOverrides = new List<AbilityInitialUnlockOverrideEntry>();
		else
			abilityInitialUnlockOverrides.Clear();

		if ( source == null || source.abilityInitialUnlockOverrides == null )
			return;

		for ( int i = 0; i < source.abilityInitialUnlockOverrides.Count; i++ )
		{
			AbilityInitialUnlockOverrideEntry entry = source.abilityInitialUnlockOverrides[ i ];
			if ( entry == null || string.IsNullOrEmpty( entry.abilityId ) )
				continue;
			if ( entry.overrideState == AbilityInitialUnlockOverride.Unset )
				continue;
			abilityInitialUnlockOverrides.Add( new AbilityInitialUnlockOverrideEntry
			{
				abilityId = entry.abilityId,
				overrideState = entry.overrideState
			} );
		}
	}
}
