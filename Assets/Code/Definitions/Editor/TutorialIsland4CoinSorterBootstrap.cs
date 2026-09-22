#if UNITY_EDITOR
using UnityEditor;

using UnityEngine;

/// <summary>
/// Ensures Tutorial_Island4CoinSorter exists and is registered on <see cref="TutorialCatalogDefinition"/>.
/// </summary>
public static class TutorialIsland4CoinSorterBootstrap
{
	const string TutorialsFolder = "Assets/Definitions/Tutorials";
	const string AssetPath = "Assets/Definitions/Tutorials/Tutorial_Island4CoinSorter.asset";
	const string CatalogPath = "Assets/Definitions/TutorialCatalogDefinition.asset";
	const string TutorialId = "tut_island4_coin_sorter";

	[InitializeOnLoadMethod]
	static void QueueEnsure()
	{
		EditorApplication.delayCall += EnsureIsland4CoinSorterTutorial;
	}

	[MenuItem( DragonLootMenus.DefinitionsEnsureIsland4CoinSorterTutorial )]
	static void MenuEnsureIsland4CoinSorterTutorial()
	{
		EnsureIsland4CoinSorterTutorial();
		Debug.Log( "Tutorial_Island4CoinSorter ensured and registered on TutorialCatalogDefinition." );
	}

	public static void EnsureIsland4CoinSorterTutorial()
	{
		AddressableEditorUtil.EnsureFolder( TutorialsFolder );

		TutorialDefinition tutorial = AssetDatabase.LoadAssetAtPath<TutorialDefinition>( AssetPath );
		if ( tutorial == null && System.IO.File.Exists( AssetPath ) )
		{
			AssetDatabase.ImportAsset( AssetPath );
			tutorial = AssetDatabase.LoadAssetAtPath<TutorialDefinition>( AssetPath );
		}

		if ( tutorial == null )
		{
			tutorial = ScriptableObject.CreateInstance<TutorialDefinition>();
			tutorial.name = "Tutorial_Island4CoinSorter";
			ApplyDefaults( tutorial );
			AssetDatabase.CreateAsset( tutorial, AssetPath );
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
			if ( entry != null && entry.id == TutorialId )
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
		tutorial.id = TutorialId;
		tutorial.title = "Coin Sorter";
		tutorial.tags = new[] { "coins", "sorter", "island4" };
		tutorial.trigger = TutorialTriggerType.ManyCoins;
		tutorial.minWorldCoinsToShow = 50;
		tutorial.minCarriedCoinsToShow = 30;
		tutorial.minDisplayCoinsToShow = 30;
		tutorial.sorterCoinsToComplete = 10;
		tutorial.prerequisiteVolumeId = "volume_island_4";
		tutorial.showDelaySeconds = 0.75f;
		tutorial.body = "Got a lot of mixed coins? Use the coin sorter on this island — dump them into the hopper, then hold the crank to sort them into typed stacks.";
		tutorial.tasks = new[]
		{
			new TutorialTask
			{
				id = "look_sorter",
				label = "Look at the coin sorter",
				completeTrigger = TutorialTaskCompleteType.AimCoinSorter
			},
			new TutorialTask
			{
				id = "load_hopper",
				label = "Add coins into the hopper",
				keybindHint = "[{WholeStackPlace}] or [{Interact}]",
				completeTrigger = TutorialTaskCompleteType.LoadCoinSorterHopper
			},
			new TutorialTask
			{
				id = "sort_coins",
				label = "Sort coins with the crank",
				keybindHint = "Hold [{ContextualInteract}]",
				completeTrigger = TutorialTaskCompleteType.UseCoinSorter,
				requiredCount = 10
			}
		};
	}
}
#endif
