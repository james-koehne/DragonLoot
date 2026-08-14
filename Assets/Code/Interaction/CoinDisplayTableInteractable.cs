using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Typed coin display: accepts one coin <see cref="TreasureDefinition"/> and snaps matching
/// carried coins into a generated horizontal slot grid (Displayed state).
/// Coins are stackable, so they pile vertically in each slot. Pickup takes from the top down.
/// Setup: collider on root, child DisplayArea, assign accepted treasure + grid settings.
/// </summary>
public class CoinDisplayTableInteractable : TypedDisplayTableInteractable
{
	public override TreasureOwnerKind OwnerKind => TreasureOwnerKind.CoinTable;

	protected override TreasureCategory RequiredCategory => TreasureCategory.Coin;

	protected override string DefaultInteractionName => "Coin Display";

	public TreasureDefinition AcceptedCoin => AcceptedTreasure;

	public IReadOnlyList<TreasureItem> DisplayedCoins => DisplayedItems;

	/// <summary>
	/// Hold-F whole-stack place: every carried coin must match this table's accepted definition.
	/// </summary>
	public bool CanAcceptWholeCarriedCoinStack( IReadOnlyList<TreasureDefinition> definitions )
	{
		if ( definitions == null || definitions.Count == 0 || AcceptedCoin == null )
			return false;

		if ( !PlayerCarry.AreCoinDefinitionsUniform( definitions, out TreasureDefinition uniform ) )
			return false;

		return uniform == AcceptedCoin;
	}

	protected override void Reset()
	{
		base.Reset();
		// Coins sit closer together than gems on the display plane.
		slotSpacing = 0.12f;
		rows = 4;
		columns = 6;
	}

	protected override Quaternion GetSlotLocalRotation()
	{
		// Keep coins flat on the table surface (yaw from table facing).
		return Quaternion.identity;
	}

	protected override void PublishChanged()
	{
		EventBus.Publish( new CoinDisplayTableChangedEvent
		{
			Table = this,
			Count = CurrentCount,
			Capacity = Capacity
		} );
	}

	protected override void PublishCompleted()
	{
		EventBus.Publish( new CoinDisplayTableCompletedEvent
		{
			Table = this,
			AcceptedCoin = AcceptedTreasure
		} );
	}
}
