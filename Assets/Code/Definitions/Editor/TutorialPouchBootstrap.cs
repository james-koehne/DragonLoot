#if UNITY_EDITOR
using UnityEditor;

using UnityEngine;

/// <summary>
/// Ensures Tutorial_Pouches exists and is registered on <see cref="TutorialCatalogDefinition"/>.
/// </summary>
public static class TutorialPouchBootstrap
{
	const string TutorialsFolder = "Assets/Definitions/Tutorials";
	const string PouchesPath = "Assets/Definitions/Tutorials/Tutorial_Pouches.asset";
	const string CatalogPath = "Assets/Definitions/TutorialCatalogDefinition.asset";
	const string PouchesId = "tut_pouches";

	[InitializeOnLoadMethod]
	static void QueueEnsure()
	{
		EditorApplication.delayCall += EnsurePouchesTutorial;
	}

	[MenuItem( DragonLootMenus.DefinitionsEnsurePouchesTutorial )]
	static void MenuEnsurePouchesTutorial()
	{
		EnsurePouchesTutorial();
		Debug.Log( "Tutorial_Pouches ensured and registered on TutorialCatalogDefinition." );
	}

	public static void EnsurePouchesTutorial()
	{
		AddressableEditorUtil.EnsureFolder( TutorialsFolder );

		TutorialDefinition pouches = AssetDatabase.LoadAssetAtPath<TutorialDefinition>( PouchesPath );
		if ( pouches == null && System.IO.File.Exists( PouchesPath ) )
		{
			AssetDatabase.ImportAsset( PouchesPath );
			pouches = AssetDatabase.LoadAssetAtPath<TutorialDefinition>( PouchesPath );
		}

		if ( pouches == null )
		{
			pouches = ScriptableObject.CreateInstance<TutorialDefinition>();
			pouches.name = "Tutorial_Pouches";
			ApplyPouchesDefaults( pouches );
			AssetDatabase.CreateAsset( pouches, PouchesPath );
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
			if ( entry != null && entry.id == PouchesId )
				return;
			if ( entry == pouches )
				return;
		}

		catalog.tutorials.Add( pouches );
		EditorUtility.SetDirty( catalog );
		AssetDatabase.SaveAssets();
	}

	static void ApplyPouchesDefaults( TutorialDefinition pouches )
	{
		pouches.id = PouchesId;
		pouches.title = "Pouches";
		pouches.tags = new[] { "pouches", "gems" };
		pouches.trigger = TutorialTriggerType.UnselectedHoldingCategory;
		pouches.holdingCategory = TreasureCategory.Gem;
		pouches.prerequisiteVolumeId = "volume_island_3";
		pouches.body = "Gems go in their own pouch. Switch to the gem pouch to hold them.";
		pouches.tasks = new[]
		{
			new TutorialTask
			{
				id = "switch_gem_pouch",
				label = "Switch to the gem pouch",
				keybindHint = "[{CategorySlot2}] or [{CyclePouch}]",
				completeTrigger = TutorialTaskCompleteType.SelectHoldingCategory
			}
		};
	}
}
#endif
