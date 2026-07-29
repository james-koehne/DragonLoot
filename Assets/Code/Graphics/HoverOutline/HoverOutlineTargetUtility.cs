using System.Collections.Generic;

using UnityEngine;

public static class HoverOutlineTargetUtility
{
	static readonly List<Renderer> Buffer = new List<Renderer>( 8 );

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

		GroundCoinStack groundStack = interactable as GroundCoinStack;
		if ( groundStack != null )
			return groundStack.Count > 0;

		CoinStackInteractable coinStack = interactable as CoinStackInteractable;
		if ( coinStack != null )
			return coinStack.isActiveAndEnabled;

		return false;
	}

	public static bool CanOutlineTreasureItem( TreasureItem item )
	{
		if ( item == null || item.Definition == null )
			return false;

		item.TryRepairPickupState();

		if ( item.State == TreasureItemState.Held
			|| item.State == TreasureItemState.Stacked
			|| item.IsReclaiming )
			return false;

		if ( item.State == TreasureItemState.InPile )
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
			return CollectRenderers( itemInteractable.Item );

		GroundCoinStack groundStack = focus as GroundCoinStack;
		if ( groundStack != null )
			return CollectFromBehaviour( groundStack );

		CoinStackInteractable coinStack = focus as CoinStackInteractable;
		if ( coinStack != null )
			return CollectFromBehaviour( coinStack );

		return Buffer;
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

	static void AppendRenderers( GameObject root )
	{
		if ( root == null )
			return;

		Renderer[] renderers = root.GetComponentsInChildren<Renderer>( true );
		for ( int i = 0; i < renderers.Length; i++ )
		{
			Renderer renderer = renderers[ i ];
			if ( renderer == null || !renderer.enabled )
				continue;

			if ( !( renderer is MeshRenderer ) && !( renderer is SkinnedMeshRenderer ) )
				continue;

			if ( renderer.sharedMaterial == null )
				continue;

			Buffer.Add( renderer );
		}
	}
}
