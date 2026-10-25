using UnityEngine;

/// <summary>
/// Drop-off station: docked carts unload accepted items into mixed storage, then continue
/// to the next matching output or return home with undroppable cargo.
/// </summary>
public class MinecartOutputStation : MinecartStationBase
{
	static readonly Color OutputRoleTint = new Color( 0.12f, 0.72f, 0.78f, 1f );

	[SerializeField]
	[Tooltip( "When any entries are set, only these definitions are accepted (categories ignored)." )]
	TreasureDefinition[] acceptedDefinitions;

	[SerializeField]
	[Tooltip( "Used only when acceptedDefinitions is empty. Empty = accept all categories." )]
	TreasureCategory[] acceptedCategories;

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

	/// <summary>
	/// Definitions win when set; otherwise categories; empty both = accept all.
	/// </summary>
	public bool AcceptsDefinition( TreasureDefinition def )
	{
		if ( def == null )
			return false;

		if ( acceptedDefinitions != null && acceptedDefinitions.Length > 0 )
		{
			for ( int i = 0; i < acceptedDefinitions.Length; i++ )
			{
				if ( acceptedDefinitions[ i ] == def )
					return true;
			}

			return false;
		}

		if ( acceptedCategories != null && acceptedCategories.Length > 0 )
		{
			TreasureCategory category = def.category;
			for ( int i = 0; i < acceptedCategories.Length; i++ )
			{
				if ( acceptedCategories[ i ] == category )
					return true;
			}

			return false;
		}

		return true;
	}

	public bool AcceptsAnyCargoOn( MinecartInteractable cart )
	{
		return cart != null && cart.HasCargoMatching( AcceptsDefinition );
	}

	protected override bool TryTransferOnce( MinecartInteractable cart )
	{
		MixedDisplayTableInteractable table = Storage;
		if ( table == null || cart == null || cart.ItemCount <= 0 )
			return false;

		if ( !cart.TryExtractOneMatchingItem( CanUnloadDefinition, out TreasureItem item ) || item == null )
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
		return cart == null || !HasUnloadableAcceptedCargo( cart );
	}

	protected override bool CanTransferProgress( MinecartInteractable cart )
	{
		return HasUnloadableAcceptedCargo( cart );
	}

	protected override void OnForcedOrIdleLeave( MinecartInteractable cart, MinecartAutoController auto )
	{
		if ( auto == null )
			return;

		MinecartInteractable cargo = cart != null ? cart.ResolveCargoCart() : null;
		if ( cargo == null )
			cargo = cart;

		MinecartInteractable routeCart = auto.Cart != null ? auto.Cart : cart;

		if ( !MinecartStationBase.AutomationEnabled || cargo == null || cargo.ItemCount <= 0 )
		{
			auto.LeaveDock( null );
			return;
		}

		MinecartOutputStation next = MinecartAutoController.FindBestOutputForCart( routeCart, exclude: this );
		if ( next != null )
		{
			auto.LeaveDock( next );
			return;
		}

		MinecartInputStation home = auto.HomeInput;
		if ( home == null )
			home = MinecartAutoController.FindNearestInput( routeCart );

		auto.LeaveDock( home );
	}

	bool CanUnloadDefinition( TreasureDefinition def )
	{
		if ( !AcceptsDefinition( def ) )
			return false;

		MixedDisplayTableInteractable table = Storage;
		return table != null && table.CanAcceptWorldItem( def );
	}

	bool HasUnloadableAcceptedCargo( MinecartInteractable cart )
	{
		return cart != null && cart.HasCargoMatching( CanUnloadDefinition );
	}

	public void EditorSetAcceptedCategories( TreasureCategory[] categories )
	{
		acceptedCategories = categories;
	}

	public void EditorSetAcceptedDefinitions( TreasureDefinition[] definitions )
	{
		acceptedDefinitions = definitions;
	}
}
