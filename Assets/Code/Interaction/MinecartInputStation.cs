using UnityEngine;

/// <summary>
/// Fills from mixed storage into auto cargo carts. Auto-calls a free cart when storage has items.
/// On leave, sends loaded carts to the linked output station.
/// </summary>
public class MinecartInputStation : MinecartStationBase
{
	static readonly Color InputRoleTint = new Color( 0.95f, 0.62f, 0.12f, 1f );

	[SerializeField]
	MinecartOutputStation linkedOutput;

	protected override bool ShowsCallVisualization => true;

	protected override Color RoleTint => InputRoleTint;

	protected override string DefaultInteractionName => "Send loaded cart";

	void Reset()
	{
		SetInteractionName( DefaultInteractionName );
	}

	protected override void Update()
	{
		base.Update();
		TryAutoCall();
	}

	void TryAutoCall()
	{
		if ( !MinecartStationBase.AutomationEnabled )
			return;

		if ( HasDockedCart || HasInboundCart )
			return;

		MixedDisplayTableInteractable table = Storage;
		if ( table == null || table.ItemCount <= 0 )
			return;

		MinecartInteractable cart = FindNearestAvailableCart();
		if ( cart == null )
			return;

		TryBeginCall( cart );
	}

	protected override bool TryTransferOnce( MinecartInteractable cart )
	{
		MixedDisplayTableInteractable table = Storage;
		if ( table == null || cart == null || table.ItemCount <= 0 )
			return false;

		if ( !table.TryExtractOneItem( out TreasureItem item ) || item == null )
			return false;

		if ( !cart.TryAcceptWorldItem( item ) )
		{
			// Put it back if the cart cannot take it.
			if ( !table.TryAcceptWorldItem( item ) )
				item.EnterPhysics( item.transform.position, item.transform.rotation );
			return false;
		}

		cart.NotifyCargoPlaced();
		return true;
	}

	protected override bool ShouldLeaveAfterTransfer( MinecartInteractable cart )
	{
		if ( cart == null )
			return true;

		MixedDisplayTableInteractable table = Storage;
		if ( table == null || table.ItemCount <= 0 )
			return true;

		if ( cart.IsCargoFull )
			return true;

		if ( table.DisplayedItems != null && table.DisplayedItems.Count > 0 )
		{
			TreasureItem next = table.DisplayedItems[ table.DisplayedItems.Count - 1 ];
			if ( next != null && next.Definition != null && !cart.CanAcceptDefinitionForTransfer( next.Definition ) )
				return true;
		}

		return false;
	}

	protected override bool CanTransferProgress( MinecartInteractable cart )
	{
		MixedDisplayTableInteractable table = Storage;
		if ( table == null || cart == null || table.ItemCount <= 0 )
			return false;

		if ( cart.IsCargoFull )
			return false;

		if ( table.DisplayedItems == null || table.DisplayedItems.Count <= 0 )
			return false;

		TreasureItem next = table.DisplayedItems[ table.DisplayedItems.Count - 1 ];
		return next != null && next.Definition != null && cart.CanAcceptDefinitionForTransfer( next.Definition );
	}

	protected override void OnForcedOrIdleLeave( MinecartInteractable cart, MinecartAutoController auto )
	{
		if ( auto == null )
			return;

		MinecartOutputStation output = linkedOutput;
		if ( output != null && cart != null && cart.ItemCount > 0 && MinecartStationBase.AutomationEnabled )
			auto.LeaveDock( output );
		else
			auto.LeaveDock( null );
	}

	public void EditorSetLinkedOutput( MinecartOutputStation value )
	{
		linkedOutput = value;
	}
}
