using UnityEngine;

public class DebugLoggingSection : DebugOverlaySection
{
	public string Title => "Logging";

	public void Draw()
	{
		if ( GameMode.Instance == null )
		{
			GUILayout.Label( "No GameMode" );
			return;
		}

		DebugDefinition def = GameMode.Instance.DebugDefinition;
		if ( def == null )
		{
			GUILayout.Label( "No DebugDefinition" );
			return;
		}

		def.debugLogging = GUILayout.Toggle( def.debugLogging, "Debug Logging" );
		def.debugBackend = GUILayout.Toggle( def.debugBackend, "Debug Backend" );
#if UNITY_EDITOR
		def.spawnAtSceneCamera = GUILayout.Toggle( def.spawnAtSceneCamera, "Spawn At Scene Camera" );
#endif
	}
}
