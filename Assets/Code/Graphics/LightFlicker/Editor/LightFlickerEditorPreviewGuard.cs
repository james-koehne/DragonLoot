#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps edit-mode flicker preview from persisting authored Light values to scenes and prefabs.
/// </summary>
[InitializeOnLoad]
static class LightFlickerEditorPreviewGuard
{
	static LightFlickerEditorPreviewGuard()
	{
		EditorSceneManager.sceneSaving += OnSceneSaving;
		PrefabStage.prefabSaving += OnPrefabSaving;
		LightFlickerDefinition.PresetsChanged += OnPresetsChanged;
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

		LightFlicker[] flickers = prefabRoot.GetComponentsInChildren<LightFlicker>( true );
		for ( int i = 0; i < flickers.Length; i++ )
		{
			if ( flickers[ i ] != null )
				flickers[ i ].RestoreAuthoredState();
		}
	}

	static void OnPresetsChanged()
	{
		RefreshAllEditPreviews();
	}

	static void OnPlayModeStateChanged( PlayModeStateChange change )
	{
		if ( change != PlayModeStateChange.ExitingEditMode )
			return;

		RestoreAllAuthoredState();
	}

	public static void RefreshAllEditPreviews()
	{
		if ( Application.isPlaying )
			return;

		LightFlicker[] flickers = Resources.FindObjectsOfTypeAll<LightFlicker>();
		for ( int i = 0; i < flickers.Length; i++ )
		{
			LightFlicker flicker = flickers[ i ];
			if ( flicker == null || !IsEditableSceneObject( flicker.gameObject ) )
				continue;

			if ( !flicker.PlayInEditMode || !flicker.isActiveAndEnabled )
				continue;

			flicker.RefreshEditorPreview();
		}
	}

	static void RestoreAllAuthoredState()
	{
		LightFlicker[] flickers = Resources.FindObjectsOfTypeAll<LightFlicker>();
		for ( int i = 0; i < flickers.Length; i++ )
		{
			LightFlicker flicker = flickers[ i ];
			if ( flicker == null || !IsEditableSceneObject( flicker.gameObject ) )
				continue;

			flicker.RestoreAuthoredState();
		}
	}

	static void RestoreAuthoredStateInScene( Scene scene )
	{
		if ( !scene.IsValid() )
			return;

		GameObject[] roots = scene.GetRootGameObjects();
		for ( int r = 0; r < roots.Length; r++ )
		{
			LightFlicker[] flickers = roots[ r ].GetComponentsInChildren<LightFlicker>( true );
			for ( int i = 0; i < flickers.Length; i++ )
			{
				if ( flickers[ i ] != null )
					flickers[ i ].RestoreAuthoredState();
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
#endif
