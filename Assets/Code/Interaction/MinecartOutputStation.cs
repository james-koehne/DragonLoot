using UnityEngine;

/// <summary>
/// Drop-off station: docked carts unload item-by-item into mixed storage, then idle.
/// </summary>
public class MinecartOutputStation : MinecartStationBase
{
	void Reset()
	{
		SetInteractionName( "Send cart" );
	}

	protected override bool TryTransferOnce( MinecartInteractable cart )
	{
		MixedDisplayTableInteractable table = Storage;
		if ( table == null || cart == null || cart.ItemCount <= 0 )
			return false;

		if ( !cart.TryExtractOneItem( out TreasureItem item ) || item == null )
			return false;

		if ( !table.TryAcceptWorldItem( item ) )
		{
			if ( !cart.TryAcceptWorldItem( item ) )
				item.EnterPhysics( item.transform.position, item.transform.rotation );
			return false;
		}

		return true;
	}

	protected override bool ShouldLeaveAfterTransfer( MinecartInteractable cart )
	{
		return cart == null || cart.ItemCount <= 0;
	}

	protected override void OnForcedOrIdleLeave( MinecartInteractable cart, MinecartAutoController auto )
	{
		if ( auto != null )
			auto.LeaveDock( null );
	}
}
