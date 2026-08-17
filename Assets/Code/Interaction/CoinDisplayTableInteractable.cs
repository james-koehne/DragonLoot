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
	[Header( "Start Fill" )]
	[Tooltip( "When enabled, each slot is pre-filled with a random stack of the accepted coin on play." )]
	[SerializeField]
	bool fillSlotsOnStart;

	[Tooltip( "Inclusive minimum coins placed in each slot." )]
	[SerializeField]
	[Min( 0 )]
	int minCoinsPerSlot = 1;

	[Tooltip( "Inclusive maximum coins placed in each slot. Clamped by max stack per slot when that is set." )]
	[SerializeField]
	[Min( 0 )]
	int maxCoinsPerSlot = 6;

	[Tooltip( "0 = non-deterministic. Non-zero seeds the per-slot amounts for this table." )]
	[SerializeField]
	int fillSeed;

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

	protected override void OnValidate()
	{
		base.OnValidate();
		minCoinsPerSlot = Mathf.Max( 0, minCoinsPerSlot );
		maxCoinsPerSlot = Mathf.Max( minCoinsPerSlot, maxCoinsPerSlot );
	}

	void Start()
	{
		TryFillSlotsOnStart();
	}

	void TryFillSlotsOnStart()
	{
		if ( !fillSlotsOnStart || AcceptedCoin == null || Slots == null )
			return;

		int min = Mathf.Max( 0, minCoinsPerSlot );
		int max = Mathf.Max( min, maxCoinsPerSlot );
		System.Random rng = fillSeed != 0 ? new System.Random( fillSeed ) : null;

		int totalAdded = 0;
		for ( int i = 0; i < Slots.Length; i++ )
		{
			int amount = rng != null
				? rng.Next( min, max + 1 )
				: UnityEngine.Random.Range( min, max + 1 );
			totalAdded += SpawnDisplayedStack( i, AcceptedCoin, amount );
		}

		if ( totalAdded > 0 )
			FinishStartFill();
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
