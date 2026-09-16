#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ensures FairyHelper prefab juice children (Glow particles, InteractFeedbacks, SpeechBubble) exist after script/prefab imports.
/// </summary>
public static class FairyHelperJuicePostprocessor
{
	static bool _ranThisDomain;

	[InitializeOnLoadMethod]
	static void QueuePatch()
	{
		if ( _ranThisDomain )
			return;
		_ranThisDomain = true;
		EditorApplication.delayCall += RunPatch;
	}

	static void RunPatch()
	{
		try
		{
			FairyHelperPrefabBuilder.EnsureJuiceOnPrefab();
		}
		catch ( System.Exception ex )
		{
			Debug.LogWarning( "FairyHelperJuicePostprocessor: " + ex.Message );
		}
	}
}
#endif
