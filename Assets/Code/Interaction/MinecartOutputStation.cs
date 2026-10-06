using UnityEngine;

/// <summary>
/// Drop-off station: docked carts unload item-by-item into mixed storage, then idle.
/// </summary>
public class MinecartOutputStation : MinecartStationBase
{
	static readonly Color OutputRoleTint = new Color( 0.12f, 0.72f, 0.78f, 1f );

	protected override Color RoleTint => OutputRoleTint;

	protected override string DefaultInteractionName => "Dismiss cart";

	void Reset()
	{
		SetInteractionName( DefaultInteractionName );
	}

	public override void RequestImmediateSend()
	{
		if ( !HasActiveInputStation() )
			return;

		base.RequestImmediateSend();
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
		if ( !HasActiveInputStation() )
			return;

		if ( auto != null )
			auto.LeaveDock( null );
	}
}
