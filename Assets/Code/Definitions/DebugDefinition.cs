using UnityEngine;

[CreateAssetMenu( fileName = "DebugDefinition", menuName = "Definitions/DebugDefinition" )]
public class DebugDefinition : ScriptableObject
{
	public bool debugLogging = false;
	public bool debugBackend = false;

	[Tooltip( "Editor only: on play, place the player at the Scene view camera instead of LevelSceneMarkers.playerSpawn." )]
	public bool spawnAtSceneCamera = false;
}
