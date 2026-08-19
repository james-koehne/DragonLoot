#if UNITY_EDITOR
using System.Collections.Generic;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Edit-mode: parents overlapping curated props under a pile's _AuthoredLoot.
/// Only tests objects that actually changed — never scans every renderer in the scene.
/// </summary>
[InitializeOnLoad]
static class TreasurePileAuthoredLootAbsorber
{
	const double ScanIntervalSeconds = 0.35;
	static readonly HashSet<int> PendingIds = new HashSet<int>();
	static readonly Dictionary<string, TreasureDefinition> DefinitionByVisualName =
		new Dictionary<string, TreasureDefinition>();
	static double _nextScanTime;
	static bool _scanning;

	static TreasurePileAuthoredLootAbsorber()
	{
		Undo.undoRedoPerformed += QueueSelection;
		ObjectChangeEvents.changesPublished += OnObjectChanges;
		EditorApplication.update += OnUpdate;
		Selection.selectionChanged += QueueSelection;
		EditorSceneManager.sceneOpened += OnSceneOpened;
	}

	static void OnSceneOpened( Scene scene, OpenSceneMode mode )
	{
		QueueSelection();
	}

	static void QueueSelection()
	{
		if ( Application.isPlaying || _scanning )
			return;

		Object[] selected = Selection.gameObjects;
		for ( int i = 0; i < selected.Length; i++ )
		{
			if ( selected[ i ] != null )
				PendingIds.Add( selected[ i ].GetInstanceID() );
		}
	}

	static void OnObjectChanges( ref ObjectChangeEventStream stream )
	{
		if ( Application.isPlaying || _scanning )
			return;

		for ( int i = 0; i < stream.length; i++ )
		{
			switch ( stream.GetEventType( i ) )
			{
				case ObjectChangeKind.ChangeGameObjectParent:
				{
					stream.GetChangeGameObjectParentEvent( i, out ChangeGameObjectParentEventArgs args );
					PendingIds.Add( args.instanceId );
					break;
				}
				case ObjectChangeKind.CreateGameObjectHierarchy:
				{
					stream.GetCreateGameObjectHierarchyEvent( i, out CreateGameObjectHierarchyEventArgs args );
					PendingIds.Add( args.instanceId );
					break;
				}
				case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
				{
					stream.GetChangeGameObjectOrComponentPropertiesEvent(
						i,
						out ChangeGameObjectOrComponentPropertiesEventArgs args );
					Object changed = EditorUtility.InstanceIDToObject( args.instanceId );
					if ( changed is Transform || changed is GameObject || changed is TreasureItem
						|| changed is TreasurePileAuthoredItem )
						PendingIds.Add( args.instanceId );
					break;
				}
			}
		}
	}

	static void OnUpdate()
	{
		if ( Application.isPlaying || PendingIds.Count == 0 )
			return;
		if ( EditorApplication.isPlayingOrWillChangePlaymode )
			return;
		if ( EditorApplication.timeSinceStartup < _nextScanTime )
			return;

		_nextScanTime = EditorApplication.timeSinceStartup + ScanIntervalSeconds;
		ProcessPending();
	}

	static void ProcessPending()
	{
		_scanning = true;
		try
		{
			TreasurePileVisual[] piles = Object.FindObjectsByType<TreasurePileVisual>(
				FindObjectsInactive.Exclude,
				FindObjectsSortMode.None );
			if ( piles == null || piles.Length == 0 )
			{
				PendingIds.Clear();
				return;
			}

			int[] ids = new int[ PendingIds.Count ];
			PendingIds.CopyTo( ids );
			PendingIds.Clear();

			for ( int i = 0; i < ids.Length; i++ )
			{
				Object obj = EditorUtility.InstanceIDToObject( ids[ i ] );
				GameObject go = obj as GameObject;
				if ( go == null )
				{
					Component component = obj as Component;
					if ( component != null )
						go = component.gameObject;
				}

				if ( go == null )
					continue;

				ProcessCandidate( go, piles );
			}

			for ( int p = 0; p < piles.Length; p++ )
			{
				if ( piles[ p ] != null )
					EnsureAuthoredChildrenMarked( piles[ p ] );
			}
		}
		finally
		{
			_scanning = false;
		}
	}

	static void ProcessCandidate( GameObject go, TreasurePileVisual[] piles )
	{
		if ( go == null || !IsAbsorbCandidate( go ) )
			return;
		if ( IsBlockedByNonPileOwner( go ) )
			return;

		Bounds worldBounds = GetWorldBounds( go );
		TryAbsorbGameObject( go, piles, worldBounds );
	}

	static bool IsAbsorbCandidate( GameObject go )
	{
		if ( go == null )
			return false;
		if ( go.GetComponent<TreasurePileAuthoredItem>() != null )
			return true;
		if ( go.GetComponent<TreasureItem>() != null )
			return true;

		string name = StripCloneSuffix( go.name );
		if ( name.EndsWith( "Visual" ) )
			return true;
		return false;
	}

	static void EnsureAuthoredChildrenMarked( TreasurePileVisual pile )
	{
		Transform authoredRoot = pile.FindAuthoredLootRoot();
		if ( authoredRoot == null )
			return;

		for ( int i = 0; i < authoredRoot.childCount; i++ )
		{
			Transform child = authoredRoot.GetChild( i );
			if ( child == null )
				continue;
			if ( child.name == TreasurePileVisual.LatentBakePreviewRootName )
				continue;

			EnsureMarkerOnGameObject( child.gameObject );
		}
	}

	static string StripCloneSuffix( string name )
	{
		if ( string.IsNullOrEmpty( name ) )
			return string.Empty;

		int clone = name.IndexOf( "(Clone)" );
		if ( clone >= 0 )
			name = name.Substring( 0, clone ).Trim();
		return name;
	}

	static void TryAbsorbGameObject( GameObject go, TreasurePileVisual[] piles, Bounds worldBounds )
	{
		if ( go == null || piles == null )
			return;

		TreasurePileVisual parentPile = go.GetComponentInParent<TreasurePileVisual>();
		TreasurePileVisual bestPile = null;
		for ( int p = 0; p < piles.Length; p++ )
		{
			TreasurePileVisual pile = piles[ p ];
			if ( pile == null )
				continue;
			if ( pile.Heightfield == null || !pile.Heightfield.IsInitialized )
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
			return;

		AbsorbIntoPile( go, bestPile );
	}

	static void AbsorbIntoPile( GameObject go, TreasurePileVisual pile )
	{
		if ( go == null || pile == null )
			return;

		Transform authoredRoot = pile.EnsureAuthoredLootRoot();
		Transform itemTransform = go.transform;
		bool parented = false;

		if ( itemTransform.parent != authoredRoot )
		{
			if ( IsOwnedByNonTargetInteractable( go, pile ) )
				return;

			Undo.SetTransformParent( itemTransform, authoredRoot, "Absorb Authored Pile Loot" );
			parented = true;
		}

		bool marked = EnsureMarkerOnGameObject( go );
		if ( parented || marked )
		{
			EditorSceneManager.MarkSceneDirty( pile.gameObject.scene );
		}
	}

	static bool EnsureMarkerOnGameObject( GameObject go )
	{
		if ( go == null )
			return false;

		bool changed = false;
		TreasurePileAuthoredItem marker = go.GetComponent<TreasurePileAuthoredItem>();
		if ( marker == null )
		{
			marker = Undo.AddComponent<TreasurePileAuthoredItem>( go );
			changed = true;
		}

		TreasureItem item = go.GetComponent<TreasureItem>();
		if ( item != null )
			marker.BindItem( item );

		if ( marker.Definition == null )
		{
			TreasureDefinition def = FindDefinitionForVisual( go );
			if ( def != null )
			{
				marker.BindDefinition( def );
				changed = true;
			}
		}

		if ( changed )
			EditorUtility.SetDirty( go );

		return changed;
	}

	static TreasureDefinition FindDefinitionForVisual( GameObject go )
	{
		if ( go == null )
			return null;

		string name = StripCloneSuffix( go.name );
		if ( name.EndsWith( "Visual" ) )
			name = name.Substring( 0, name.Length - "Visual".Length );

		if ( string.IsNullOrEmpty( name ) )
			return null;

		if ( DefinitionByVisualName.TryGetValue( name, out TreasureDefinition cached ) )
			return cached;

		string[] guids = AssetDatabase.FindAssets( name + " t:TreasureDefinition" );
		TreasureDefinition resolved = null;
		for ( int i = 0; i < ( guids != null ? guids.Length : 0 ); i++ )
		{
			string path = AssetDatabase.GUIDToAssetPath( guids[ i ] );
			TreasureDefinition def = AssetDatabase.LoadAssetAtPath<TreasureDefinition>( path );
			if ( def == null )
				continue;
			if ( def.name == name || def.id == name )
			{
				resolved = def;
				break;
			}

			if ( resolved == null )
				resolved = def;
		}

		DefinitionByVisualName[ name ] = resolved;
		return resolved;
	}

	static bool IsBlockedByNonPileOwner( GameObject go )
	{
		if ( go == null )
			return true;

		InteractableBase[] interactables = go.GetComponentsInParent<InteractableBase>( true );
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

	static bool IsOwnedByNonTargetInteractable( GameObject go, TreasurePileVisual targetPile )
	{
		if ( go == null )
			return true;

		InteractableBase[] interactables = go.GetComponentsInParent<InteractableBase>( true );
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

	static Bounds GetWorldBounds( GameObject go )
	{
		TreasurePileAuthoredItem authored = go.GetComponent<TreasurePileAuthoredItem>();
		if ( authored != null )
			return authored.GetWorldBounds();

		return TreasureItem.GetCombinedRendererWorldBounds( go.transform, go.transform.position );
	}
}
#endif
