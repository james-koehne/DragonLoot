#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[CanEditMultipleObjects]
[CustomEditor( typeof( MagicalLanternMotion ) )]
public class MagicalLanternMotionEditor : Editor
{
	public override void OnInspectorGUI()
	{
		EditorGUILayout.HelpBox(
			"Drop this on the lantern that should float, not a static post. " +
			"It glides on a heading with a capped turn rate, so every path is a curve — never a straight cut or reverse. " +
			"Disable the component (or turn off Play In Edit Mode) before repositioning.",
			MessageType.Info );

		DrawDefaultInspector();

		EditorGUILayout.Space( 6f );
		if ( GUILayout.Button( "Recapture Rest Pose" ) )
		{
			for ( int i = 0; i < targets.Length; i++ )
			{
				MagicalLanternMotion motion = targets[ i ] as MagicalLanternMotion;
				if ( motion == null )
					continue;

				Undo.RecordObject( motion, "Recapture Rest Pose" );
				Undo.RecordObject( motion.transform, "Recapture Rest Pose" );
				motion.RecaptureRestPose();
				EditorUtility.SetDirty( motion );
			}
		}
	}
}

[InitializeOnLoad]
static class MagicalLanternMotionEditorPreviewGuard
{
	static MagicalLanternMotionEditorPreviewGuard()
	{
		EditorSceneManager.sceneSaving += OnSceneSaving;
		PrefabStage.prefabSaving += OnPrefabSaving;
		EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
	}

	static void OnSceneSaving( Scene scene, string path )
	{
		if ( !scene.IsValid() || !scene.isLoaded )
			return;

		RestoreAuthoredStateInScene( scene );
	}

	static void OnPrefabSaving( GameObject prefabRoot )
	{
		if ( prefabRoot == null )
			return;

		MagicalLanternMotion[] motions = prefabRoot.GetComponentsInChildren<MagicalLanternMotion>( true );
		for ( int i = 0; i < motions.Length; i++ )
		{
			if ( motions[ i ] != null )
				motions[ i ].RestoreAuthoredPose();
		}
	}

	static void OnPlayModeStateChanged( PlayModeStateChange change )
	{
		if ( change != PlayModeStateChange.ExitingEditMode )
			return;

		RestoreAllAuthoredState();
	}

	static void RestoreAllAuthoredState()
	{
		MagicalLanternMotion[] motions = Resources.FindObjectsOfTypeAll<MagicalLanternMotion>();
		for ( int i = 0; i < motions.Length; i++ )
		{
			MagicalLanternMotion motion = motions[ i ];
			if ( motion == null || !IsEditableSceneObject( motion.gameObject ) )
				continue;

			motion.RestoreAuthoredPose();
		}
	}

	static void RestoreAuthoredStateInScene( Scene scene )
	{
		if ( !scene.IsValid() )
			return;

		GameObject[] roots = scene.GetRootGameObjects();
		for ( int r = 0; r < roots.Length; r++ )
		{
			MagicalLanternMotion[] motions = roots[ r ].GetComponentsInChildren<MagicalLanternMotion>( true );
			for ( int i = 0; i < motions.Length; i++ )
			{
				if ( motions[ i ] != null )
					motions[ i ].RestoreAuthoredPose();
			}
		}
	}

	static bool IsEditableSceneObject( GameObject go )
	{
		if ( go == null )
			return false;

		if ( EditorUtility.IsPersistent( go ) )
			return false;

		if ( ( go.hideFlags & HideFlags.NotEditable ) != 0 )
			return false;

		Scene scene = go.scene;
		if ( !scene.IsValid() || !scene.isLoaded )
			return false;

		if ( string.IsNullOrEmpty( scene.path ) && PrefabStageUtility.GetCurrentPrefabStage() == null )
			return false;

		return true;
	}
}

static class MagicalLanternMotionMenu
{
	[MenuItem( DragonLootMenus.GraphicsMagicalLanternAddToSelection, priority = 240 )]
	static void AddToSelection()
	{
		GameObject[] selection = Selection.gameObjects;
		if ( selection == null || selection.Length == 0 )
			return;

		int added = 0;
		for ( int i = 0; i < selection.Length; i++ )
		{
			GameObject go = selection[ i ];
			if ( go == null )
				continue;

			if ( go.GetComponent<MagicalLanternMotion>() != null )
				continue;

			Undo.AddComponent<MagicalLanternMotion>( go );
			added++;
		}

		Debug.Log( "MagicalLanternMotion: added to " + added + " object(s)." );
	}

	[MenuItem( DragonLootMenus.GraphicsMagicalLanternAddToSelection, validate = true )]
	static bool ValidateAddToSelection()
	{
		GameObject[] selection = Selection.gameObjects;
		return selection != null && selection.Length > 0;
	}
}
#endif
