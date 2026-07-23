using UnityEngine;

[CreateAssetMenu( fileName = "DebugDefinition", menuName = "Definitions/DebugDefinition" )]
public class DebugDefinition : ScriptableObject
{
	public bool debugLogging = false;
	public bool debugBackend = false;
}
