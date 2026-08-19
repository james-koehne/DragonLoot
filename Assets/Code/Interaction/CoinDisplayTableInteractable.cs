using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Typed coin display: accepts one coin <see cref="TreasureDefinition"/> and snaps matching
/// carried coins into a generated horizontal slot grid (Displayed state).
/// Coins are stackable, so they pile vertically in each slot. Pickup takes from the top down.
/// Placement ignores the aimed pile: targeting the table or any stack fills the shortest
/// pile, searching left-to-right from the top-left (ties keep the earlier slot).
/// Setup: collider on root, child DisplayArea, assign accepted treasure + grid settings.
/// </summary>
public class CoinDisplayTableInteractable : TypedDisplayTableInteractable
{
	[Header( "Start Fill" )]
	[Tooltip( "When enabled, slots are pre-filled on play using Perlin noise amounts with optional random gaps." )]
	[SerializeField]
	bool fillSlotsOnStart;

	[Tooltip( "Inclusive minimum coins placed in each slot (Perlin low end)." )]
	[SerializeField]
	[Min( 0 )]
	int minCoinsPerSlot = 1;

	[Tooltip( "Inclusive maximum coins placed in each slot (Perlin high end). Clamped by max stack per slot when that is set." )]
	[SerializeField]
	[Min( 0 )]
	int maxCoinsPerSlot = 6;

	[Tooltip( "After Perlin amounts are assigned, each non-empty slot has this chance to be cleared." )]
	[SerializeField]
	[Range( 0f, 1f )]
	float emptySlotChance = 0.25f;

	[Tooltip( "0 = non-deterministic. Non-zero seeds Perlin layout and empty-slot pass for this table." )]
	[SerializeField]
	int fillSeed;

	public override TreasureOwnerKind OwnerKind => TreasureOwnerKind.CoinTable;

	protected override TreasureCategory RequiredCategory => TreasureCategory.Coin;

	protected override string DefaultInteractionName => "Coin Display";

	protected override bool UsesLowestPilePlacement => true;

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
		emptySlotChance = Mathf.Clamp01( emptySlotChance );
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
		float noiseOrigin = fillSeed != 0 ? fillSeed * 0.0137f : UnityEngine.Random.Range( 0f, 1000f );
		float clearChance = Mathf.Clamp01( emptySlotChance );
		int slotCount = Slots.Length;
		int[] amounts = new int[ slotCount ];

		for ( int i = 0; i < slotCount; i++ )
		{
			int row = i / columns;
			int col = i % columns;
			float noise = Mathf.PerlinNoise( col * 0.41f + noiseOrigin, row * 0.41f + noiseOrigin * 1.37f );
			amounts[ i ] = Mathf.RoundToInt( Mathf.Lerp( min, max, noise ) );
		}

		for ( int i = 0; i < slotCount; i++ )
		{
			if ( amounts[ i ] <= 0 || clearChance <= 0f )
				continue;
			if ( NextFillFloat( rng ) < clearChance )
				amounts[ i ] = 0;
		}

		int totalAdded = 0;
		for ( int i = 0; i < slotCount; i++ )
			totalAdded += SpawnDisplayedStack( i, AcceptedCoin, amounts[ i ] );

		if ( totalAdded > 0 )
			FinishStartFill();
	}

	static float NextFillFloat( System.Random rng )
	{
		if ( rng != null )
			return (float)rng.NextDouble();
		return UnityEngine.Random.value;
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
