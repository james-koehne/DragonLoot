using UnityEngine;

/// <summary>
/// Fills from mixed storage into auto cargo carts. Auto-calls a free cart when storage has items.
/// On leave, routes loaded carts to the best output. Autocart fleet is owned by <see cref="MinecartStationBase"/>.
/// </summary>
public class MinecartInputStation : MinecartStationBase
{
	static readonly Color InputRoleTint = new Color( 0.95f, 0.62f, 0.12f, 1f );

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

		if ( TryBeginCall( cart ) )
			RememberHome( cart );
	}

	protected override bool TryBeginCall( MinecartInteractable cart )
	{
		if ( !base.TryBeginCall( cart ) )
			return false;

		RememberHome( cart );
		return true;
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
			if ( !table.TryAcceptWorldItem( item ) )
				item.EnterPhysics( item.transform.position, item.transform.rotation );
			return false;
		}

		cart.NotifyCargoPlaced();
		RememberHome( cart );
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

		RememberHome( cart );

		MinecartInteractable cargo = cart != null ? cart.ResolveCargoCart() : null;
		if ( cargo == null )
			cargo = cart;

		if ( cargo != null && cargo.ItemCount > 0 && MinecartStationBase.AutomationEnabled )
		{
			MinecartOutputStation output = MinecartAutoController.FindBestOutputForCart( auto.Cart != null ? auto.Cart : cart );
			if ( output != null )
			{
				auto.LeaveDock( output );
				return;
			}
		}

		// No accepting output (or empty cart): undock and idle here with remaining cargo.
		auto.LeaveDock( null );
	}

	void RememberHome( MinecartInteractable cart )
	{
		if ( cart == null )
			return;

		MinecartAutoController auto = cart.ResolveAutoController();
		if ( auto != null )
			auto.SetHomeInput( this );
	}
}
