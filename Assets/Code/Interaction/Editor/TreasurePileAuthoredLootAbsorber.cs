#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Edit-mode: parents overlapping curated props (artifacts, chests, keys) under a pile's _AuthoredLoot.
/// Debounced — not every frame.
/// </summary>
[InitializeOnLoad]
static class TreasurePileAuthoredLootAbsorber
{
	const double ScanIntervalSeconds = 0.15;
	static double _nextScanTime;
	static bool _dirty;
	static bool _scanning;

	static TreasurePileAuthoredLootAbsorber()
	{
		EditorApplication.hierarchyChanged += MarkDirty;
		Undo.undoRedoPerformed += MarkDirty;
		ObjectChangeEvents.changesPublished += OnObjectChanges;
		EditorApplication.update += OnUpdate;
		EditorSceneManager.sceneOpened += OnSceneOpened;
	}

	static void OnSceneOpened( Scene scene, OpenSceneMode mode )
	{
		MarkDirty();
	}

	static void OnObjectChanges( ref ObjectChangeEventStream stream )
	{
		MarkDirty();
	}

	static void MarkDirty()
	{
		if ( Application.isPlaying || _scanning )
			return;
		_dirty = true;
	}

	static void OnUpdate()
	{
		if ( Application.isPlaying || !_dirty )
			return;
		if ( EditorApplication.isPlayingOrWillChangePlaymode )
			return;
		if ( EditorApplication.timeSinceStartup < _nextScanTime )
			return;

		_dirty = false;
		_nextScanTime = EditorApplication.timeSinceStartup + ScanIntervalSeconds;
		ScanOpenScenes();
	}

	static void ScanOpenScenes()
	{
		_scanning = true;
		try
		{
			for ( int s = 0; s < SceneManager.sceneCount; s++ )
			{
				Scene scene = SceneManager.GetSceneAt( s );
				if ( !scene.IsValid() || !scene.isLoaded )
					continue;

				GameObject[] roots = scene.GetRootGameObjects();
				for ( int r = 0; r < roots.Length; r++ )
					ScanHierarchyForPiles( roots[ r ].transform );
			}

			AbsorbLooseItems();
		}
		finally
		{
			_scanning = false;
		}
	}

	static void ScanHierarchyForPiles( Transform root )
	{
		if ( root == null )
			return;

		TreasurePileVisual pile = root.GetComponent<TreasurePileVisual>();
		if ( pile != null )
			EnsureAuthoredChildrenMarked( pile );

		for ( int i = 0; i < root.childCount; i++ )
			ScanHierarchyForPiles( root.GetChild( i ) );
	}

	static void EnsureAuthoredChildrenMarked( TreasurePileVisual pile )
	{
		Transform authoredRoot = pile.FindAuthoredLootRoot();
		if ( authoredRoot == null )
			return;

		TreasureItem[] items = authoredRoot.GetComponentsInChildren<TreasureItem>( true );
		for ( int i = 0; i < items.Length; i++ )
		{
			TreasureItem item = items[ i ];
			if ( item == null || !TreasurePileAuthoredItem.IsCuratable( item.Definition ) )
				continue;

			TreasurePileAuthoredItem marker = item.GetComponent<TreasurePileAuthoredItem>();
			if ( marker == null )
			{
				marker = Undo.AddComponent<TreasurePileAuthoredItem>( item.gameObject );
				marker.BindItem( item );
				EditorUtility.SetDirty( item.gameObject );
			}
			else
				marker.BindItem( item );
		}
	}

	static void AbsorbLooseItems()
	{
		TreasureItem[] allItems = Object.FindObjectsByType<TreasureItem>(
			FindObjectsInactive.Exclude,
			FindObjectsSortMode.None );
		if ( allItems == null || allItems.Length == 0 )
			return;

		TreasurePileVisual[] piles = Object.FindObjectsByType<TreasurePileVisual>(
			FindObjectsInactive.Exclude,
			FindObjectsSortMode.None );
		if ( piles == null || piles.Length == 0 )
			return;

		for ( int p = 0; p < piles.Length; p++ )
		{
			TreasurePileVisual pile = piles[ p ];
			if ( pile == null )
				continue;
			pile.EnsureEditorPreview();
			if ( pile.Heightfield == null || !pile.Heightfield.IsInitialized )
				continue;
		}

		for ( int i = 0; i < allItems.Length; i++ )
		{
			TreasureItem item = allItems[ i ];
			if ( item == null || !TreasurePileAuthoredItem.IsCuratable( item.Definition ) )
				continue;
			if ( IsBlockedByNonPileOwner( item ) )
				continue;

			TreasurePileVisual parentPile = item.GetComponentInParent<TreasurePileVisual>();
			Bounds worldBounds = GetItemWorldBounds( item );

			TreasurePileVisual bestPile = null;
			for ( int p = 0; p < piles.Length; p++ )
			{
				TreasurePileVisual pile = piles[ p ];
				if ( pile == null || pile.Heightfield == null || !pile.Heightfield.IsInitialized )
					continue;

				if ( parentPile != null && parentPile != pile )
					continue;

				if ( !GoldPileTreasurePlacement.WorldAabbIntersectsSolidMound(
					pile.Heightfield,
					pile.transform,
					worldBounds ) )
					continue;

				bestPile = pile;
				break;
			}

			if ( bestPile == null )
				continue;

			AbsorbIntoPile( item, bestPile );
		}
	}

	static void AbsorbIntoPile( TreasureItem item, TreasurePileVisual pile )
	{
		if ( item == null || pile == null )
			return;

		Transform authoredRoot = pile.EnsureAuthoredLootRoot();
		Transform itemTransform = item.transform;

		if ( itemTransform.parent == authoredRoot )
		{
			EnsureMarker( item );
			return;
		}

		if ( IsOwnedByNonTargetInteractable( item, pile ) )
			return;

		Undo.SetTransformParent( itemTransform, authoredRoot, "Absorb Authored Pile Loot" );
		EnsureMarker( item );
		EditorUtility.SetDirty( item.gameObject );
		EditorUtility.SetDirty( pile.gameObject );
		EditorSceneManager.MarkSceneDirty( pile.gameObject.scene );
	}

	static void EnsureMarker( TreasureItem item )
	{
		TreasurePileAuthoredItem marker = item.GetComponent<TreasurePileAuthoredItem>();
		if ( marker == null )
			marker = Undo.AddComponent<TreasurePileAuthoredItem>( item.gameObject );
		marker.BindItem( item );
	}

	/// <summary>Tables, stacks, carry, etc. — never steal from those owners.</summary>
	static bool IsBlockedByNonPileOwner( TreasureItem item )
	{
		if ( item == null )
			return true;

		InteractableBase[] interactables = item.GetComponentsInParent<InteractableBase>( true );
		for ( int i = 0; i < interactables.Length; i++ )
		{
			InteractableBase interactable = interactables[ i ];
			if ( interactable == null )
				continue;
			if ( interactable is TreasureItemInteractable )
				continue;
			if ( interactable is TreasurePileInteractable )
				continue;
			return true;
		}

		return false;
	}

	static bool IsOwnedByNonTargetInteractable( TreasureItem item, TreasurePileVisual targetPile )
	{
		if ( item == null )
			return true;

		InteractableBase[] interactables = item.GetComponentsInParent<InteractableBase>( true );
		TreasurePileInteractable targetInteractable = targetPile != null
			? targetPile.GetComponent<TreasurePileInteractable>()
			: null;

		for ( int i = 0; i < interactables.Length; i++ )
		{
			InteractableBase interactable = interactables[ i ];
			if ( interactable == null )
				continue;
			if ( interactable is TreasureItemInteractable )
				continue;
			if ( interactable is TreasurePileInteractable pileInteractable )
			{
				if ( targetInteractable != null && pileInteractable == targetInteractable )
					continue;
				if ( targetPile != null && pileInteractable.PileVisual == targetPile )
					continue;
				return true;
			}

			return true;
		}

		return false;
	}

	static Bounds GetItemWorldBounds( TreasureItem item )
	{
		Collider col = item.GetComponent<Collider>();
		if ( col != null )
			return col.bounds;

		Renderer renderer = item.GetComponentInChildren<Renderer>();
		if ( renderer != null )
			return renderer.bounds;

		return new Bounds( item.transform.position, Vector3.one * 0.5f );
	}
}
#endif
