using System.Collections.Generic;
using System.IO;

using UnityEngine;

public static class HoverOutlineTargetUtility
{
	static readonly List<Renderer> Buffer = new List<Renderer>( 8 );
	// #region agent log
	static float _agentOrphanLogAt;
	// #endregion

	/// <summary>
	/// Focus eligible for hover outline without requiring pickup capacity (works while hands are full).
	/// </summary>
	public static bool CanOutlineFocus( InteractableBase interactable, PlayerController player )
	{
		if ( interactable == null || !interactable.IsAvailable || player == null )
			return false;

		TreasureItemInteractable itemInteractable = interactable as TreasureItemInteractable;
		if ( itemInteractable != null )
			return CanOutlineTreasureItem( itemInteractable.Item );

		ChestInteractable chest = interactable as ChestInteractable;
		if ( chest != null )
			return CanOutlineChest( chest );

		GroundCoinStack groundStack = interactable as GroundCoinStack;
		if ( groundStack != null )
			return groundStack.Count > 0;

		CoinStackInteractable coinStack = interactable as CoinStackInteractable;
		if ( coinStack != null )
			return coinStack.isActiveAndEnabled;

		CoinSortingCrankInteractable crank = interactable as CoinSortingCrankInteractable;
		if ( crank != null )
			return crank.CanInteract( player );

		CoinSortingStationMoveInteractable move = interactable as CoinSortingStationMoveInteractable;
		if ( move != null )
			return move.CanInteract( player );

		CoinSortingIndicatorInteractable indicator = interactable as CoinSortingIndicatorInteractable;
		if ( indicator != null )
			return indicator.CanOutlineFocus( player );

		DoorInteractable door = interactable as DoorInteractable;
		if ( door != null )
			return door.CanOutlineFocus( player );

		MinecartInteractable cart = interactable as MinecartInteractable;
		if ( cart != null )
			return cart.CanInteract( player );

		MinecartCallPost callPost = interactable as MinecartCallPost;
		if ( callPost != null )
			return callPost.CanInteract( player );

		MinecartStationAutomationButton stationButton = interactable as MinecartStationAutomationButton;
		if ( stationButton != null )
			return stationButton.IsAvailable;

		FairyInteractable fairy = interactable as FairyInteractable;
		if ( fairy != null )
			return fairy.CanInteract( player );

		CoffeeMachineInteractable coffee = interactable as CoffeeMachineInteractable;
		if ( coffee != null )
			return coffee.CanInteract( player );

		return false;
	}

	public static bool CanOutlineChest( ChestInteractable chest )
	{
		if ( chest == null )
			return false;

		TreasureItem item = chest.Item;
		if ( item == null || item.Definition == null )
			return false;

		item.TryRepairPickupState();

		if ( item.State == TreasureItemState.Held
			|| item.State == TreasureItemState.Stacked
			|| item.IsReclaiming )
			return false;

		TreasurePileVisual origin = item.OriginPile;
		if ( origin != null && origin.IsTreasureBuried( item ) )
			return false;

		TreasurePileVisual ownerPile = item.PileOwner;
		if ( ownerPile != null && ownerPile.IsTreasureBuried( item ) )
			return false;

		return true;
	}

	public static bool CanOutlineTreasureItem( TreasureItem item )
	{
		if ( item == null || item.Definition == null )
			return false;

		item.TryRepairPickupState();

		if ( item.State == TreasureItemState.Held
			|| item.State == TreasureItemState.Stacked
			|| item.IsReclaiming )
		{
			// #region agent log
			if ( item.State == TreasureItemState.Stacked && Time.unscaledTime - _agentOrphanLogAt > 0.5f )
			{
				GroundCoinStack stackOwner = item.Owner as GroundCoinStack;
				bool badOwner = stackOwner == null
					|| !stackOwner.isActiveAndEnabled
					|| stackOwner.IsReturningToPool
					|| stackOwner.Count <= 0;
				if ( badOwner )
				{
					_agentOrphanLogAt = Time.unscaledTime;
					try
					{
						long ts = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
						string line = "{\"sessionId\":\"82c704\",\"hypothesisId\":\"H3\",\"location\":\"HoverOutlineTargetUtility.CanOutlineTreasureItem\",\"message\":\"orphan_stacked_coin\",\"data\":{\"itemInFlight\":"
							+ ( item.IsInFlight ? "true" : "false" )
							+ ",\"worldLoose\":" + ( item.IsWorldLoose ? "true" : "false" )
							+ ",\"ownerNull\":" + ( stackOwner == null ? "true" : "false" )
							+ ",\"ownerActive\":" + ( stackOwner != null && stackOwner.isActiveAndEnabled ? "true" : "false" )
							+ ",\"ownerReturning\":" + ( stackOwner != null && stackOwner.IsReturningToPool ? "true" : "false" )
							+ ",\"ownerCount\":" + ( stackOwner != null ? stackOwner.Count : -1 )
							+ "},\"timestamp\":" + ts + "}\n";
						File.AppendAllText( @"c:\GameDevelopment\FunkGames\DragonLoot\DragonLoot\.cursor\debug-82c704.log", line );
					}
					catch
					{
					}
				}
			}
			// #endregion
			return false;
		}

		if ( item.State == TreasureItemState.InPile )
			return false;

		if ( TreasureItemInteractable.IsDisplayedPickupLocked( item ) )
			return false;

		return true;
	}

	public static IReadOnlyList<Renderer> CollectFromFocus( IInteractable focus )
	{
		Buffer.Clear();
		if ( focus == null )
			return Buffer;

		TreasureItemInteractable itemInteractable = focus as TreasureItemInteractable;
		if ( itemInteractable != null )
		{
			TreasureItem item = itemInteractable.Item;
			ITreasureDisplayStackOwner display = item != null ? item.Owner as ITreasureDisplayStackOwner : null;
			if ( display != null
				&& item.Definition != null
				&& item.Definition.category == TreasureCategory.Coin )
			{
				display.AppendSlotOutlineRenderers( item, Buffer );
				if ( Buffer.Count > 0 )
					return Buffer;
			}

			return CollectRenderers( item );
		}

		ChestInteractable chest = focus as ChestInteractable;
		if ( chest != null )
			return CollectRenderers( chest.Item );

		GroundCoinStack groundStack = focus as GroundCoinStack;
		if ( groundStack != null )
			return CollectFromBehaviour( groundStack );

		CoinStackInteractable coinStack = focus as CoinStackInteractable;
		if ( coinStack != null )
			return CollectFromBehaviour( coinStack );

		CoinSortingCrankInteractable crank = focus as CoinSortingCrankInteractable;
		if ( crank != null )
			return CollectFromBehaviour( crank );

		CoinSortingStationMoveInteractable move = focus as CoinSortingStationMoveInteractable;
		if ( move != null )
		{
			CoinSortingStation station = move.Station;
			if ( station != null )
			{
				AppendStationMoveRenderers( station );
				return Buffer;
			}

			return CollectFromBehaviour( move );
		}

		CoinSortingIndicatorInteractable indicator = focus as CoinSortingIndicatorInteractable;
		if ( indicator != null )
			return CollectFromBehaviour( indicator );

		DoorInteractable door = focus as DoorInteractable;
		if ( door != null )
			return CollectFromBehaviour( door );

		MinecartInteractable cart = focus as MinecartInteractable;
		if ( cart != null )
		{
			Buffer.Clear();
			cart.AppendOutlineRenderers( Buffer );
			return Buffer;
		}

		MinecartCallPost callPost = focus as MinecartCallPost;
		if ( callPost != null )
			return CollectFromBehaviour( callPost );

		MinecartStationAutomationButton stationButton = focus as MinecartStationAutomationButton;
		if ( stationButton != null )
			return CollectFromBehaviour( stationButton );

		FairyInteractable fairy = focus as FairyInteractable;
		if ( fairy != null )
			return CollectFromBehaviour( fairy );

		CoffeeMachineInteractable coffee = focus as CoffeeMachineInteractable;
		if ( coffee != null )
			return CollectFromBehaviour( coffee );

		return Buffer;
	}

	static void AppendStationMoveRenderers( CoinSortingStation station )
	{
		if ( station == null )
			return;

		Renderer[] renderers = station.GetComponentsInChildren<Renderer>( true );
		for ( int i = 0; i < renderers.Length; i++ )
		{
			Renderer renderer = renderers[ i ];
			if ( renderer == null || !renderer.enabled )
				continue;
			if ( !( renderer is MeshRenderer ) && !( renderer is SkinnedMeshRenderer ) )
				continue;
			if ( renderer.sharedMaterial == null )
				continue;
			if ( renderer.GetComponentInParent<CoinSortingCrankInteractable>() != null )
				continue;
			if ( renderer.GetComponentInParent<CoinSortingIndicatorInteractable>() != null )
				continue;
			if ( renderer.GetComponentInParent<CoinSortingEnergyGauge>() != null )
				continue;
			if ( renderer.GetComponentInParent<CoinSortingBooleanIndicator>() != null )
				continue;
			if ( renderer.GetComponentInParent<GroundCoinStack>() != null )
				continue;
			int collectable = PhysicsLayers.CollectableLayer;
			if ( collectable >= 0 && renderer.gameObject.layer == collectable )
				continue;

			Buffer.Add( renderer );
		}
	}

	public static IReadOnlyList<Renderer> CollectRenderers( TreasureItem item )
	{
		Buffer.Clear();
		if ( item == null )
			return Buffer;

		AppendRenderers( item.gameObject );
		return Buffer;
	}

	public static IReadOnlyList<Renderer> CollectFromBehaviour( Component component )
	{
		Buffer.Clear();
		if ( component == null )
			return Buffer;

		AppendRenderers( component.gameObject );
		return Buffer;
	}

	public static bool HasEnabledMeshRenderers( GameObject root )
	{
		if ( root == null )
			return false;

		Renderer[] renderers = root.GetComponentsInChildren<Renderer>( true );
		for ( int i = 0; i < renderers.Length; i++ )
		{
			if ( ShouldIncludeOutlineRenderer( renderers[ i ] ) )
				return true;
		}

		return false;
	}

	/// <summary>
	/// Quest outlines: gold piles contribute only the deformed terrain mesh, not seated treasure.
	/// </summary>
	public static void AppendQuestOutlineRenderers( GameObject root, List<Renderer> destination )
	{
		if ( root == null || destination == null )
			return;

		GoldPileTerrainMesh terrain = ResolvePileTerrain( root );
		if ( terrain != null )
		{
			Renderer pileRenderer = terrain.PileRenderer;
			if ( pileRenderer != null && pileRenderer.enabled && pileRenderer.sharedMaterial != null )
				destination.Add( pileRenderer );
			return;
		}

		AppendEnabledMeshRenderers( root, destination );
	}

	static GoldPileTerrainMesh ResolvePileTerrain( GameObject root )
	{
		if ( root == null )
			return null;

		TreasurePileVisual pile = root.GetComponent<TreasurePileVisual>();
		if ( pile != null )
		{
			if ( pile.TerrainMesh != null )
				return pile.TerrainMesh;

			GoldPileTerrainMesh onPile = pile.GetComponent<GoldPileTerrainMesh>();
			if ( onPile != null )
				return onPile;
		}

		return root.GetComponent<GoldPileTerrainMesh>();
	}

	/// <summary>
	/// Appends enabled mesh/skinned renderers under <paramref name="root"/> into <paramref name="destination"/>
	/// without clearing it (safe while building a multi-source outline list).
	/// Skips meshes under <see cref="DisplayCompleteEffect"/> so complete FX never outline with the display.
	/// </summary>
	public static void AppendEnabledMeshRenderers( GameObject root, List<Renderer> destination )
	{
		if ( root == null || destination == null )
			return;

		Renderer[] renderers = root.GetComponentsInChildren<Renderer>( true );
		for ( int i = 0; i < renderers.Length; i++ )
		{
			Renderer renderer = renderers[ i ];
			if ( !ShouldIncludeOutlineRenderer( renderer ) )
				continue;

			destination.Add( renderer );
		}
	}

	static void AppendRenderers( GameObject root )
	{
		if ( root == null )
			return;

		Renderer[] renderers = root.GetComponentsInChildren<Renderer>( true );
		for ( int i = 0; i < renderers.Length; i++ )
		{
			Renderer renderer = renderers[ i ];
			if ( !ShouldIncludeOutlineRenderer( renderer ) )
				continue;

			Buffer.Add( renderer );
		}
	}

	static bool ShouldIncludeOutlineRenderer( Renderer renderer )
	{
		if ( renderer == null || !renderer.enabled )
			return false;

		if ( !( renderer is MeshRenderer ) && !( renderer is SkinnedMeshRenderer ) )
			return false;

		if ( renderer.sharedMaterial == null )
			return false;

		if ( renderer.GetComponentInParent<DisplayCompleteEffect>( true ) != null )
			return false;

		return true;
	}
}
