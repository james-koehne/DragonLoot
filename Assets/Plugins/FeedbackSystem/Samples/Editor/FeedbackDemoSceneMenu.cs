#if UNITY_EDITOR
using System.IO;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class FeedbackDemoSceneMenu
{
	const string ScenePath = "Assets/Plugins/FeedbackSystem/Samples/FeedbackSystemDemo.unity";

	[MenuItem( "FeedbackSystem/Open Demo Scene" )]
	public static void OpenDemoScene()
	{
		if ( !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() )
			return;

		string directory = Path.GetDirectoryName( ScenePath );
		if ( !string.IsNullOrEmpty( directory ) && !Directory.Exists( directory ) )
			Directory.CreateDirectory( directory );

		Scene scene;
		if ( File.Exists( ScenePath ) )
			scene = EditorSceneManager.OpenScene( ScenePath, OpenSceneMode.Single );
		else
			scene = EditorSceneManager.NewScene( NewSceneSetup.DefaultGameObjects, NewSceneMode.Single );

			if ( !HasDemoController( scene ) )
			{
				GameObject demo = FindNamedRoot( scene, "Feedback Demo" );
				if ( demo == null )
					demo = new GameObject( "Feedback Demo" );
				demo.AddComponent<FeedbackDemoController>();
			}

		EditorSceneManager.SaveScene( scene, ScenePath );
		AssetDatabase.Refresh();
	}

	static bool HasDemoController( Scene scene )
	{
		GameObject[] roots = scene.GetRootGameObjects();
		for ( int i = 0; i < roots.Length; i++ )
		{
			if ( roots[i].GetComponentInChildren<FeedbackDemoController>( true ) != null )
				return true;
		}

		return false;
	}

	static GameObject FindNamedRoot( Scene scene, string name )
	{
		GameObject[] roots = scene.GetRootGameObjects();
		for ( int i = 0; i < roots.Length; i++ )
		{
			if ( roots[i].name == name )
				return roots[i];
		}

		return null;
	}
}
#endif
