using UnityEngine;

/// <summary>
/// Scene markers for the active Level. Assigned by greybox builder / hand-authored in the scene.
/// </summary>
public class LevelSceneMarkers : MonoBehaviour
{
	public static LevelSceneMarkers Instance;

	public Transform playerSpawn;

	[Tooltip( "Optional directional sun driven by EnvironmentDefinition." )]
	public Light sunLight;

	[Header( "Interaction (Block 002 greybox)" )]
	public bool spawnGreyboxInteractables = true;

	void Awake()
	{
		Instance = this;
	}

	void OnDestroy()
	{
		if ( Instance == this )
			Instance = null;
	}

}
