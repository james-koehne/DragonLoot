#if UNITY_EDITOR
using UnityEditor;

using UnityEngine;

/// <summary>
/// Ensures Tutorial_Driving exists and is registered on <see cref="TutorialCatalogDefinition"/>.
/// </summary>
public static class TutorialDrivingBootstrap
{
	const string TutorialsFolder = "Assets/Definitions/Tutorials";
	const string DrivingPath = "Assets/Definitions/Tutorials/Tutorial_Driving.asset";
	const string CatalogPath = "Assets/Definitions/TutorialCatalogDefinition.asset";
	const string DrivingId = "tut_driving";

	[InitializeOnLoadMethod]
	static void QueueEnsure()
	{
		EditorApplication.delayCall += EnsureDrivingTutorial;
	}

	[MenuItem( DragonLootMenus.DefinitionsEnsureDrivingTutorial )]
	static void MenuEnsureDrivingTutorial()
	{
		EnsureDrivingTutorial();
		Debug.Log( "Tutorial_Driving ensured and registered on TutorialCatalogDefinition." );
	}

	static void EnsureDrivingTutorial()
	{
		AddressableEditorUtil.EnsureFolder( TutorialsFolder );

		TutorialDefinition driving = AssetDatabase.LoadAssetAtPath<TutorialDefinition>( DrivingPath );
		if ( driving == null && System.IO.File.Exists( DrivingPath ) )
		{
			AssetDatabase.ImportAsset( DrivingPath );
			driving = AssetDatabase.LoadAssetAtPath<TutorialDefinition>( DrivingPath );
		}

		if ( driving == null )
		{
			driving = ScriptableObject.CreateInstance<TutorialDefinition>();
			driving.name = "Tutorial_Driving";
			ApplyDrivingDefaults( driving );
			AssetDatabase.CreateAsset( driving, DrivingPath );
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
			if ( entry != null && entry.id == DrivingId )
				return;
			if ( entry == driving )
				return;
		}

		catalog.tutorials.Add( driving );
		EditorUtility.SetDirty( catalog );
		AssetDatabase.SaveAssets();
	}

	static void ApplyDrivingDefaults( TutorialDefinition driving )
	{
		driving.id = DrivingId;
		driving.title = "Driving";
		driving.tags = new[] { "minecart", "movement", "drive" };
		driving.trigger = TutorialTriggerType.DriveMinecart;
		driving.prerequisiteTutorialIds = new[] { "tut_movement" };
		driving.body = "Look at the side of a drive cart and interact to sit. Move forward to speed up the way you're looking. Move back to brake, then reverse. Interact or jump to hop out.";
		driving.tasks = new[]
		{
			new TutorialTask
			{
				id = "sit_drive",
				label = "Sit in a drive cart",
				keybindHint = "[{ContextualInteract}] look at the side",
				completeTrigger = TutorialTaskCompleteType.EnterDriveMinecart
			},
			new TutorialTask
			{
				id = "accelerate",
				label = "Speed up",
				keybindHint = "[WASD] forward",
				completeTrigger = TutorialTaskCompleteType.DriveMinecartAccelerate
			},
			new TutorialTask
			{
				id = "brake_reverse",
				label = "Brake or reverse",
				keybindHint = "[WASD] back",
				completeTrigger = TutorialTaskCompleteType.DriveMinecartBrake
			},
			new TutorialTask
			{
				id = "hop_out",
				label = "Hop out",
				keybindHint = "[{ContextualInteract}] or [{Jump}]",
				completeTrigger = TutorialTaskCompleteType.ExitDriveMinecart
			}
		};
	}
}
#endif
