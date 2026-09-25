#if UNITY_EDITOR
using UnityEditor;

using UnityEngine;

/// <summary>
/// Ensures Tutorial_BuildMode exists and is registered on <see cref="TutorialCatalogDefinition"/>.
/// </summary>
public static class TutorialBuildModeBootstrap
{
	const string TutorialsFolder = "Assets/Definitions/Tutorials";
	const string BuildModePath = "Assets/Definitions/Tutorials/Tutorial_BuildMode.asset";
	const string CatalogPath = "Assets/Definitions/TutorialCatalogDefinition.asset";
	const string BuildModeId = "tut_build_mode";

	[InitializeOnLoadMethod]
	static void QueueEnsure()
	{
		EditorApplication.delayCall += EnsureBuildModeTutorial;
	}

	[MenuItem( DragonLootMenus.DefinitionsEnsureBuildModeTutorial )]
	static void MenuEnsure()
	{
		EnsureBuildModeTutorial();
		Debug.Log( "Tutorial_BuildMode ensured and registered on TutorialCatalogDefinition." );
	}

	public static void EnsureBuildModeTutorial()
	{
		AddressableEditorUtil.EnsureFolder( TutorialsFolder );

		TutorialDefinition tutorial = AssetDatabase.LoadAssetAtPath<TutorialDefinition>( BuildModePath );
		if ( tutorial == null && System.IO.File.Exists( BuildModePath ) )
		{
			AssetDatabase.ImportAsset( BuildModePath );
			tutorial = AssetDatabase.LoadAssetAtPath<TutorialDefinition>( BuildModePath );
		}

		if ( tutorial == null )
		{
			tutorial = ScriptableObject.CreateInstance<TutorialDefinition>();
			tutorial.name = "Tutorial_BuildMode";
			ApplyDefaults( tutorial );
			AssetDatabase.CreateAsset( tutorial, BuildModePath );
			AssetDatabase.SaveAssets();
		}

		TutorialCatalogDefinition catalog = AssetDatabase.LoadAssetAtPath<TutorialCatalogDefinition>( CatalogPath );
		if ( catalog == null )
			return;

		if ( catalog.tutorials == null )
			catalog.tutorials = new System.Collections.Generic.List<TutorialDefinition>();

		for ( int i = 0; i < catalog.tutorials.Count; i++ )
		{
			TutorialDefinition entry = catalog.tutorials[ i ];
			if ( entry != null && entry.id == BuildModeId )
				return;
			if ( entry == tutorial )
				return;
		}

		catalog.tutorials.Add( tutorial );
		EditorUtility.SetDirty( catalog );
		AssetDatabase.SaveAssets();
	}

	static void ApplyDefaults( TutorialDefinition tutorial )
	{
		tutorial.id = BuildModeId;
		tutorial.title = "Building";
		tutorial.tags = new[] { "build", "hammer" };
		tutorial.trigger = TutorialTriggerType.InteractWorldHammer;
		tutorial.body = "Press {BuildModeToggle} to enter build mode. Nearby structures appear as ghosts. Aim at a ghost and hold {ContextualInteract} to build it.";
		tutorial.tasks = new[]
		{
			new TutorialTask
			{
				id = "enter_build_mode",
				label = "Enter build mode",
				keybindHint = "[{BuildModeToggle}]",
				completeTrigger = TutorialTaskCompleteType.EnterBuildMode
			},
			new TutorialTask
			{
				id = "complete_buildable",
				label = "Build a structure",
				keybindHint = "Hold [{ContextualInteract}]",
				completeTrigger = TutorialTaskCompleteType.CompleteBuildable
			}
		};
	}
}
#endif
