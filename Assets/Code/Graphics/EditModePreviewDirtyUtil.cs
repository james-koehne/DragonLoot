#if UNITY_EDITOR
using System.Reflection;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Shared helpers so edit-mode previews can mutate Lights/transforms without leaving the scene dirty.
/// </summary>
public static class EditModePreviewDirtyUtil
{
	static MethodInfo _clearSceneDirtinessMethod;

	public static void BeginMutation( GameObject go, out Scene scene, out bool wasSceneDirty )
	{
		scene = go != null ? go.scene : default;
		wasSceneDirty = scene.IsValid() && scene.isDirty;
	}

	public static void EndMutation( Scene scene, bool wasSceneDirty, params Object[] objects )
	{
		ClearObjectDirty( objects );
		ClearSceneDirtinessIfIntroduced( scene, wasSceneDirty );
	}

	public static void ClearObjectDirty( params Object[] objects )
	{
		if ( objects == null )
			return;

		for ( int i = 0; i < objects.Length; i++ )
		{
			if ( objects[ i ] != null )
				EditorUtility.ClearDirty( objects[ i ] );
		}
	}

	public static void ClearSceneDirtinessIfIntroduced( Scene scene, bool wasSceneDirty )
	{
		if ( !scene.IsValid() || wasSceneDirty || !scene.isDirty )
			return;

		ClearSceneDirtiness( scene );
	}

	public static void ClearSceneDirtiness( Scene scene )
	{
		if ( !scene.IsValid() || !scene.isDirty )
			return;

		if ( _clearSceneDirtinessMethod == null )
		{
			_clearSceneDirtinessMethod = typeof( EditorSceneManager ).GetMethod(
				"ClearSceneDirtiness",
				BindingFlags.NonPublic | BindingFlags.Static );
		}

		if ( _clearSceneDirtinessMethod != null )
			_clearSceneDirtinessMethod.Invoke( null, new object[] { scene } );
	}
}
#endif
