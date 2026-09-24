#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor helpers for treasure-surface path debug hierarchy.
/// </summary>
public static class TreasureSurfacePathDebugMenu
{
	[MenuItem( DragonLootMenus.TreasureSurfaceCreatePathDebug, priority = 30 )]
	[MenuItem( DragonLootMenus.GameObjectTreasureSurfacePathDebug, priority = 20 )]
	public static void CreateInActiveScene()
	{
		TreasureSurfacePathDebug existing = FindInOpenScenes();
		if ( existing != null )
		{
			Selection.activeGameObject = existing.gameObject;
			EditorGUIUtility.PingObject( existing.gameObject );
			return;
		}

		GameObject root = new GameObject( "TreasureSurfacePathDebug" );
		Undo.RegisterCreatedObjectUndo( root, "Create Treasure Surface Path Debug" );

		GameObject startGo = new GameObject( "PathStart" );
		Undo.RegisterCreatedObjectUndo( startGo, "Create Path Start" );
		startGo.transform.SetParent( root.transform, false );
		startGo.transform.localPosition = new Vector3( -2f, 0f, 0f );

		GameObject endGo = new GameObject( "PathEnd" );
		Undo.RegisterCreatedObjectUndo( endGo, "Create Path End" );
		endGo.transform.SetParent( root.transform, false );
		endGo.transform.localPosition = new Vector3( 2f, 0f, 0f );

		LineRenderer line = Undo.AddComponent<LineRenderer>( root );
		line.useWorldSpace = true;
		line.loop = false;
		line.widthMultiplier = 0.08f;
		line.numCapVertices = 2;
		line.numCornerVertices = 2;
		line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		line.receiveShadows = false;
		line.positionCount = 0;
		Shader shader = Shader.Find( "Sprites/Default" );
		if ( shader != null )
			line.sharedMaterial = new Material( shader );

		TreasureSurfacePathDebug debug = Undo.AddComponent<TreasureSurfacePathDebug>( root );
		SerializedObject so = new SerializedObject( debug );
		so.FindProperty( "start" ).objectReferenceValue = startGo.transform;
		so.FindProperty( "end" ).objectReferenceValue = endGo.transform;
		so.FindProperty( "lineRenderer" ).objectReferenceValue = line;
		SerializedProperty settings = so.FindProperty( "settings" );
		if ( settings != null )
		{
			TreasureSurfacePathSettings defaults = TreasureSurfacePathSettings.Default;
			settings.FindPropertyRelative( "edgeMargin" ).floatValue = defaults.edgeMargin;
			settings.FindPropertyRelative( "edgePenalty" ).floatValue = defaults.edgePenalty;
			settings.FindPropertyRelative( "maxStepHeight" ).floatValue = defaults.maxStepHeight;
			settings.FindPropertyRelative( "searchPadding" ).floatValue = defaults.searchPadding;
			settings.FindPropertyRelative( "allowDiagonal" ).boolValue = defaults.allowDiagonal;
		}

		so.ApplyModifiedProperties();

		Selection.activeGameObject = root;
		EditorGUIUtility.PingObject( root );
	}

	static TreasureSurfacePathDebug FindInOpenScenes()
	{
		for ( int s = 0; s < SceneManager.sceneCount; s++ )
		{
			Scene scene = SceneManager.GetSceneAt( s );
			if ( !scene.IsValid() || !scene.isLoaded )
				continue;

			GameObject[] roots = scene.GetRootGameObjects();
			for ( int r = 0; r < roots.Length; r++ )
			{
				TreasureSurfacePathDebug found =
					roots[ r ].GetComponentInChildren<TreasureSurfacePathDebug>( true );
				if ( found != null )
					return found;
			}
		}

		return null;
	}
}
#endif
