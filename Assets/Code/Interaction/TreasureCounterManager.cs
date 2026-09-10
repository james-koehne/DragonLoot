using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Event-driven section progress: Total / Collected / Sorted per <see cref="TreasureDefinition.id"/>.
/// </summary>
public class TreasureCounterManager : MonoBehaviour
{
	const string DefaultSectionId = "Section 1";

	static TreasureCounterManager _instance;

	readonly Dictionary<string, TreasureCounterEntry> _entries = new Dictionary<string, TreasureCounterEntry>();
	readonly List<TreasureCounterEntry> _ordered = new List<TreasureCounterEntry>();

	string _sectionId = DefaultSectionId;
	bool _sectionCompleted;

	public static TreasureCounterManager Instance => _instance;

	public string SectionId => _sectionId;
	public IReadOnlyList<TreasureCounterEntry> Entries => _ordered;
	public int TotalCategories { get; private set; }
	public int CategoriesCompleted { get; private set; }
	public int OverallCompletionPercent { get; private set; }
	public bool IsSectionCompleted => _sectionCompleted;

	public static TreasureCounterManager EnsureExists()
	{
		if ( _instance != null )
			return _instance;

		GameObject go = new GameObject( "TreasureCounterManager" );
		_instance = go.AddComponent<TreasureCounterManager>();
		return _instance;
	}

	void Awake()
	{
		if ( _instance != null && _instance != this )
		{
			Destroy( gameObject );
			return;
		}

		_instance = this;
	}

	void OnDestroy()
	{
		if ( _instance == this )
			_instance = null;
	}

	/// <summary>
	/// Rebuilds Totals for the active section from loaded level interactables.
	/// </summary>
	public void BindSection( string sectionId )
	{
		_sectionId = string.IsNullOrEmpty( sectionId ) ? DefaultSectionId : sectionId;
		_sectionCompleted = false;
		_entries.Clear();
		_ordered.Clear();

		ForEachInLoadedScenes<TreasurePileInteractable>( SeedPile );
		ForEachInLoadedScenes<CoinStackInteractable>( SeedCoinStack );
		ForEachInLoadedScenes<GemDisplayTableInteractable>( SeedGemTable );
		ForEachInLoadedScenes<CoinDisplayTableInteractable>( SeedCoinTable );
		ForEachInLoadedScenes<MixedDisplayTableInteractable>( SeedMixedTable );

		RefreshSectionAggregates();
		PublishAllChanged();
		TryPublishSectionCompleted();
	}

	public void NotifyCollected( TreasureDefinition definition, int amount = 1 )
	{
		if ( definition == null || amount <= 0 )
			return;

		TreasureCounterEntry entry = GetOrCreateEntry( definition );
		entry.Collected = Mathf.Min( entry.Total > 0 ? entry.Total : int.MaxValue, entry.Collected + amount );

		EventBus.Publish( new TreasureCollectedEvent
		{
			Treasure = definition,
			Amount = amount,
			Collected = entry.Collected
		} );

		PublishEntryChanged( entry );
	}

	public void NotifySortedDelta( TreasureDefinition definition, int delta )
	{
		if ( definition == null || delta == 0 )
			return;

		TreasureCounterEntry entry = GetOrCreateEntry( definition );
		int previousSorted = entry.Sorted;
		entry.Sorted = Mathf.Max( 0, entry.Sorted + delta );
		if ( entry.Total > 0 )
			entry.Sorted = Mathf.Min( entry.Sorted, entry.Total );

		bool wasCompleted = entry.Completed;
		EvaluateCompletion( entry, definition );

		EventBus.Publish( new TreasureSortedEvent
		{
			Treasure = definition,
			Delta = entry.Sorted - previousSorted,
			Sorted = entry.Sorted
		} );

		PublishEntryChanged( entry );

		if ( !wasCompleted && entry.Completed )
		{
			EventBus.Publish( new CategoryCompletedEvent
			{
				Id = entry.Id,
				DisplayName = entry.DisplayName,
				Treasure = definition
			} );
		}
		else if ( wasCompleted && !entry.Completed )
		{
			_sectionCompleted = false;
			EventBus.Publish( new CategoryReopenedEvent
			{
				Id = entry.Id,
				DisplayName = entry.DisplayName,
				Treasure = definition
			} );
		}

		RefreshSectionAggregates();
		TryPublishSectionCompleted();
	}

	public bool TryGetEntry( string id, out TreasureCounterEntry entry )
	{
		return _entries.TryGetValue( id, out entry );
	}

	void SeedPile( TreasurePileInteractable pile )
	{
		if ( pile == null )
			return;

		TreasurePileDefinition def = pile.PileDefinition;
		if ( def != null )
		{
			SeedPileEntries( def.coinContents );
			SeedPileEntries( def.treasureContents );
			SeedAuthoredPileExtras( pile );
			if ( ( def.coinContents != null && def.coinContents.Length > 0 )
				|| ( def.treasureContents != null && def.treasureContents.Length > 0 )
				|| ( pile.PileVisual != null && pile.PileVisual.CountAuthoredItems() > 0 ) )
				return;
		}

		if ( pile.Treasure == null )
			return;

		int amount = pile.TotalCount;
		if ( amount <= 0 )
			amount = pile.RemainingCount;
		if ( amount <= 0 )
			return;

		TreasureCounterEntry legacy = GetOrCreateEntry( pile.Treasure );
		legacy.Total += amount;
	}

	void SeedAuthoredPileExtras( TreasurePileInteractable pile )
	{
		TreasurePileVisual visual = pile != null ? pile.PileVisual : null;
		if ( visual == null )
			visual = pile.GetComponent<TreasurePileVisual>();
		if ( visual == null )
			return;

		Transform root = visual.FindAuthoredLootRoot();
		if ( root == null )
			return;

		TreasurePileAuthoredItem[] items = root.GetComponentsInChildren<TreasurePileAuthoredItem>( true );
		for ( int i = 0; i < items.Length; i++ )
		{
			TreasurePileAuthoredItem authored = items[ i ];
			if ( authored == null || authored.Definition == null )
				continue;
			if ( !TreasurePileAuthoredItem.IsCuratable( authored.Definition ) )
				continue;

			TreasureCounterEntry counter = GetOrCreateEntry( authored.Definition );
			counter.Total += 1;
		}
	}

	void SeedPileEntries( TreasurePileEntry[] entries )
	{
		if ( entries == null )
			return;

		for ( int i = 0; i < entries.Length; i++ )
		{
			TreasurePileEntry entry = entries[ i ];
			if ( entry.treasure == null || entry.count <= 0 )
				continue;

			TreasureCounterEntry counter = GetOrCreateEntry( entry.treasure );
			counter.Total += entry.count;
		}
	}

	void SeedCoinStack( CoinStackInteractable stack )
	{
		if ( stack == null || stack.Treasure == null )
			return;

		int amount = stack.CoinCount;
		if ( amount <= 0 )
			amount = stack.RemainingCount;
		if ( amount <= 0 )
			return;

		TreasureCounterEntry entry = GetOrCreateEntry( stack.Treasure );
		entry.Total += amount;
		entry.Sorted += amount;
		entry.Collected = Mathf.Max( entry.Collected, entry.Sorted );
		EvaluateCompletion( entry, stack.Treasure );
	}

	void SeedGemTable( GemDisplayTableInteractable table )
	{
		SeedTypedDisplayTable( table != null ? table.AcceptedGem : null, table != null ? table.CurrentCount : 0 );
	}

	void SeedCoinTable( CoinDisplayTableInteractable table )
	{
		if ( table == null )
			return;

		if ( !table.UsesMixedColumnRequirements )
		{
			SeedTypedDisplayTable( table.AcceptedCoin, table.CurrentCount );
			return;
		}

		int slotCount = table.SlotCount;
		for ( int i = 0; i < slotCount; i++ )
		{
			TreasureDefinition required = table.GetRequiredTreasure( i );
			int amount = table.GetSlotCount( i );
			if ( required == null || amount <= 0 )
				continue;
			SeedTypedDisplayTable( required, amount );
		}
	}

	void SeedMixedTable( MixedDisplayTableInteractable table )
	{
		if ( table == null )
			return;

		IReadOnlyList<TreasureItem> items = table.DisplayedItems;
		for ( int i = 0; i < items.Count; i++ )
		{
			TreasureItem item = items[ i ];
			if ( item == null || item.Definition == null )
				continue;

			SeedTypedDisplayTable( item.Definition, 1 );
		}
	}

	void SeedTypedDisplayTable( TreasureDefinition definition, int amount )
	{
		if ( definition == null || amount <= 0 )
			return;

		TreasureCounterEntry entry = GetOrCreateEntry( definition );
		entry.Sorted += amount;
		entry.Collected = Mathf.Max( entry.Collected, entry.Sorted );
		if ( entry.Total > 0 )
			entry.Sorted = Mathf.Min( entry.Sorted, entry.Total );
		EvaluateCompletion( entry, definition );
	}

	TreasureCounterEntry GetOrCreateEntry( TreasureDefinition definition )
	{
		string id = ResolveId( definition );
		if ( _entries.TryGetValue( id, out TreasureCounterEntry existing ) )
			return existing;

		TreasureCounterEntry entry = new TreasureCounterEntry
		{
			Id = id,
			DisplayName = string.IsNullOrEmpty( definition.displayName ) ? id : definition.displayName,
			Icon = definition.icon,
			Family = definition.category,
			Definition = definition
		};
		_entries[ id ] = entry;
		_ordered.Add( entry );
		return entry;
	}

	static string ResolveId( TreasureDefinition definition )
	{
		if ( definition == null )
			return string.Empty;

		if ( !string.IsNullOrEmpty( definition.id ) )
			return definition.id;

		return definition.name;
	}

	void EvaluateCompletion( TreasureCounterEntry entry, TreasureDefinition definition )
	{
		if ( entry == null )
			return;

		bool complete = entry.Total > 0 && entry.Sorted >= entry.Total;
		entry.Completed = complete;
	}

	void RefreshSectionAggregates()
	{
		int total = 0;
		int completed = 0;
		int sortedSum = 0;
		int totalSum = 0;

		for ( int i = 0; i < _ordered.Count; i++ )
		{
			TreasureCounterEntry entry = _ordered[ i ];
			if ( entry.Total <= 0 )
				continue;

			total++;
			totalSum += entry.Total;
			sortedSum += entry.Sorted;
			if ( entry.Completed )
				completed++;
		}

		TotalCategories = total;
		CategoriesCompleted = completed;
		OverallCompletionPercent = totalSum > 0
			? Mathf.Clamp( Mathf.RoundToInt( ( sortedSum / (float)totalSum ) * 100f ), 0, 100 )
			: 0;
	}

	void TryPublishSectionCompleted()
	{
		if ( _sectionCompleted || TotalCategories <= 0 || CategoriesCompleted < TotalCategories )
			return;

		_sectionCompleted = true;
		EventBus.Publish( new SectionCompletedEvent
		{
			SectionId = _sectionId,
			CategoriesCompleted = CategoriesCompleted,
			TotalCategories = TotalCategories,
			OverallCompletionPercent = OverallCompletionPercent
		} );
	}

	void PublishEntryChanged( TreasureCounterEntry entry )
	{
		if ( entry == null )
			return;

		EventBus.Publish( new TreasureCounterChangedEvent { Entry = entry } );
	}

	void PublishAllChanged()
	{
		for ( int i = 0; i < _ordered.Count; i++ )
			PublishEntryChanged( _ordered[ i ] );
	}

	static void ForEachInLoadedScenes<T>( Action<T> action ) where T : Component
	{
		if ( action == null )
			return;

		for ( int s = 0; s < SceneManager.sceneCount; s++ )
		{
			Scene scene = SceneManager.GetSceneAt( s );
			if ( !scene.isLoaded )
				continue;

			GameObject[] roots = scene.GetRootGameObjects();
			for ( int r = 0; r < roots.Length; r++ )
			{
				T[] components = roots[ r ].GetComponentsInChildren<T>( true );
				for ( int i = 0; i < components.Length; i++ )
				{
					if ( components[ i ] != null )
						action( components[ i ] );
				}
			}
		}
	}
}
