using UnityEngine;

[CreateAssetMenu( fileName = "DebugDefinition", menuName = "Definitions/DebugDefinition" )]
public class DebugDefinition : ScriptableObject
{
	public bool debugLogging = false;
	public bool debugBackend = false;

	[Tooltip( "Editor only: on play, place the player at the Scene view camera instead of LevelSceneMarkers.playerSpawn." )]
	public bool spawnAtSceneCamera = false;

	[Tooltip( "Skip spawning coins, gems, and artifacts for all treasure piles in play mode." )]
	public bool disableTreasureSpawning = false;

	[Tooltip( "Skip contextual tutorial popups from gameplay triggers. Debug overlay force-show and pause replay still work." )]
	public bool disableTutorials = true;

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
}
