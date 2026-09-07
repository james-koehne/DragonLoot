using System.Collections;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Typed coin display: accepts one coin <see cref="TreasureDefinition"/> and snaps matching
/// carried coins into a generated horizontal slot grid (Displayed state).
/// Coins are stackable, so they pile vertically in each slot. Pickup takes from the top down.
/// Aim at an existing pile to stack onto it; aim at the table body fills the shortest pile
/// (left-to-right, top-left wins ties).
/// Hold-F whole-stack place lands on the aimed pile when stacking, otherwise the shortest
/// pile, then peels excess coins from every player-placed pile on this table and flips them
/// into shorter columns.
/// Each display levels independently; placing on another table does not interrupt this one.
/// Setup: collider on root, child DisplayArea, assign accepted treasure + grid settings.
/// </summary>
public class CoinDisplayTableInteractable : TypedDisplayTableInteractable
{
	static readonly List<CoinDisplayTableInteractable> All = new List<CoinDisplayTableInteractable>( 32 );

	struct LevelMove
	{
		public int FromSlot;
		public int ToSlot;
	}

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

	[Tooltip( "0 = pick a random seed at start. Non-zero seeds Perlin layout and empty-slot pass for this table." )]
	[SerializeField]
	int fillSeed;

	[Header( "Auto Level" )]
	[Tooltip( "After a hold-F whole stack lands, peel excess coins off that pile into shorter columns." )]
	[SerializeField]
	bool autoLevelAfterWholeStackPlace = true;

	[Tooltip( "Delay between starting each leveling coin flight." )]
	[SerializeField]
	[Min( 0f )]
	float levelCoinStagger = 0.06f;

	[Tooltip( "Pause after the incoming stack lands before redistribution begins." )]
	[SerializeField]
	[Min( 0f )]
	float levelStartDelay = 0.12f;

	[Tooltip( "Minimum flip arc height when a coin flies between display columns." )]
	[SerializeField]
	[Min( 0.05f )]
	float levelCrossSlotArcHeight = 0.28f;

	readonly List<LevelMove> _levelMoves = new List<LevelMove>( 128 );
	readonly HashSet<int> _levelSourceSlots = new HashSet<int>();

	Coroutine _levelRoutine;
	bool _levelPending;
	bool _levelStartDelayApplied;

	public override TreasureOwnerKind OwnerKind => TreasureOwnerKind.CoinTable;

	protected override TreasureCategory RequiredCategory => TreasureCategory.Coin;

	protected override string DefaultInteractionName => "Coin Display";

	protected override bool UsesLowestPilePlacement => true;

	public TreasureDefinition AcceptedCoin => AcceptedTreasure;

	public IReadOnlyList<TreasureItem> DisplayedCoins => DisplayedItems;

	public bool IsAutoLeveling => _levelRoutine != null;

	public static IReadOnlyList<CoinDisplayTableInteractable> ActiveTables => All;

	public static int CountAllDisplayedCoins()
	{
		int total = 0;
		for ( int i = 0; i < All.Count; i++ )
		{
			CoinDisplayTableInteractable table = All[ i ];
			if ( table == null || !table.isActiveAndEnabled )
				continue;
			total += table.CurrentCount;
		}
		return total;
	}

	void OnEnable()
	{
		for ( int i = 0; i < All.Count; i++ )
		{
			if ( All[ i ] == this )
				return;
		}
		All.Add( this );
	}

	void OnDisable()
	{
		All.Remove( this );
	}

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

	/// <summary>
	/// Append a whole carried stack into one slot, then peel excess off player-placed piles when enabled.
	/// </summary>
	public int TryAppendWholeStackWithAutoLevel( int slotIndex, IReadOnlyList<TreasureDefinition> definitions )
	{
		int added = TryAppendSlotDefinitions( slotIndex, definitions );
		if ( added > 0 && autoLevelAfterWholeStackPlace )
			BeginAutoLevelStacks( slotIndex );
		return added;
	}

	protected override void OnDestroy()
	{
		All.Remove( this );
		_levelRoutine = null;
		_levelPending = false;
		_levelSourceSlots.Clear();
		base.OnDestroy();
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
		levelCoinStagger = Mathf.Max( 0f, levelCoinStagger );
		levelStartDelay = Mathf.Max( 0f, levelStartDelay );
		levelCrossSlotArcHeight = Mathf.Max( 0.05f, levelCrossSlotArcHeight );
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
		int seed = fillSeed != 0 ? fillSeed : UnityEngine.Random.Range( 1, int.MaxValue );
		System.Random rng = new System.Random( seed );
		float noiseOrigin = seed * 0.0137f;
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
			if ( (float)rng.NextDouble() < clearChance )
				amounts[ i ] = 0;
		}

		int totalAdded = 0;
		for ( int i = 0; i < slotCount; i++ )
			totalAdded += SpawnDisplayedStack( i, AcceptedCoin, amounts[ i ] );

		if ( totalAdded > 0 )
			FinishStartFill();
	}

	void BeginAutoLevelStacks( int placedSlot )
	{
		if ( !isActiveAndEnabled || Slots == null || DisplaySlotCapacity <= 0 )
			return;

		if ( placedSlot < 0 || placedSlot >= DisplaySlotCapacity )
			return;

		_levelSourceSlots.Add( placedSlot );

		if ( _levelRoutine != null )
		{
			_levelPending = true;
			return;
		}

		_levelStartDelayApplied = false;
		_levelRoutine = StartCoroutine( AutoLevelStacksRoutine() );
	}

	IEnumerator AutoLevelStacksRoutine()
	{
		while ( true )
		{
			_levelPending = false;
			BuildLevelMoves( _levelMoves, _levelSourceSlots );
			if ( _levelMoves.Count <= 0 )
			{
				if ( !_levelPending )
					break;
				continue;
			}

			if ( !_levelStartDelayApplied && levelStartDelay > 0f )
			{
				yield return new WaitForSeconds( levelStartDelay );
				_levelStartDelayApplied = true;
			}

			int moveIndex = 0;
			while ( moveIndex < _levelMoves.Count )
			{
				if ( _levelPending )
				{
					BuildLevelMoves( _levelMoves, _levelSourceSlots );
					moveIndex = 0;
					_levelPending = false;
					if ( _levelMoves.Count <= 0 )
						break;
					continue;
				}

				LevelMove move = _levelMoves[ moveIndex ];
				yield return AnimateLevelMoveRoutine( move.FromSlot, move.ToSlot );
				moveIndex++;

				if ( levelCoinStagger > 0f && moveIndex < _levelMoves.Count )
					yield return new WaitForSeconds( levelCoinStagger );
			}

			RefreshDisplayCountAndPublish();
			TryMarkCompleteIfNeeded();

			if ( !_levelPending )
				break;
		}

		_levelSourceSlots.Clear();
		_levelRoutine = null;
		_levelStartDelayApplied = false;
	}

	IEnumerator AnimateLevelMoveRoutine( int fromSlot, int toSlot )
	{
		if ( !TryPopSlotTopItem( fromSlot, out TreasureItem coin ) || coin == null )
			yield break;

		// Cylinder-bound columns hide per-coin meshes; flight must show the moving coin.
		coin.BeginFlight();
		coin.SetMeshVisible( true );
		RefreshSlotVisual( fromSlot, animate: true );

		int destStackIndex = GetSlotCount( toSlot );
		GetSlotWorldPose( toSlot, destStackIndex, coin, out Vector3 endPos, out _ );
		float arcHeight = ResolveCrossSlotArcHeight( coin.transform.position, endPos );

		yield return AnimateTreasureItemFlightToSlot(
			coin,
			toSlot,
			destStackIndex,
			requireReservedInSlot: false,
			arcHeightOverride: arcHeight );

		if ( coin == null )
			yield break;

		TryPushSlotItem( toSlot, coin );
		GetSlotWorldPose( toSlot, destStackIndex, coin, out endPos, out Quaternion endRot );
		coin.EnterDisplayed( this, endPos, endRot );
		RefreshSlotVisual( toSlot, animate: true );
		PlayTreasurePlaceFeedback( coin );
	}

	float ResolveCrossSlotArcHeight( Vector3 startPos, Vector3 endPos )
	{
		Vector3 flatStart = new Vector3( startPos.x, 0f, startPos.z );
		Vector3 flatEnd = new Vector3( endPos.x, 0f, endPos.z );
		float horizontal = Vector3.Distance( flatStart, flatEnd );
		return Mathf.Max( levelCrossSlotArcHeight, horizontal * 0.45f );
	}

	void BuildLevelMoves( List<LevelMove> moves, HashSet<int> sourceSlots )
	{
		int slotCount = DisplaySlotCapacity;
		if ( slotCount <= 0 || sourceSlots == null || sourceSlots.Count <= 0 )
		{
			moves.Clear();
			return;
		}

		int[] counts = new int[ slotCount ];
		int[] targets = new int[ slotCount ];
		BuildLevelMovesInto( moves, slotCount, sourceSlots, counts, targets );
	}

	void BuildLevelMovesInto(
		List<LevelMove> moves,
		int slotCount,
		HashSet<int> sourceSlots,
		int[] counts,
		int[] targets )
	{
		moves.Clear();
		if ( slotCount <= 0 || sourceSlots == null || sourceSlots.Count <= 0 )
			return;

		for ( int i = 0; i < slotCount; i++ )
			counts[ i ] = GetSlotCount( i );

		int total = 0;
		for ( int i = 0; i < slotCount; i++ )
			total += counts[ i ];

		if ( total <= 0 )
			return;

		int baseCount = total / slotCount;
		int remainder = total % slotCount;
		for ( int i = 0; i < slotCount; i++ )
			targets[ i ] = baseCount + ( i < remainder ? 1 : 0 );

		int maxPerSlot = MaxStackPerSlotLimit;
		if ( maxPerSlot > 0 )
		{
			for ( int i = 0; i < slotCount; i++ )
				targets[ i ] = Mathf.Min( targets[ i ], maxPerSlot );
		}

		while ( true )
		{
			int fromSlot = FindBestLevelSource( slotCount, sourceSlots, counts, targets );
			if ( fromSlot < 0 )
				break;

			int toSlot = FindBestLevelDestination( slotCount, fromSlot, counts, targets, maxPerSlot );
			if ( toSlot < 0 )
				break;

			moves.Add( new LevelMove { FromSlot = fromSlot, ToSlot = toSlot } );
			counts[ fromSlot ]--;
			counts[ toSlot ]++;
		}
	}

	static int FindBestLevelSource(
		int slotCount,
		HashSet<int> sourceSlots,
		int[] counts,
		int[] targets )
	{
		int fromSlot = -1;
		int bestExcess = 0;

		for ( int i = 0; i < slotCount; i++ )
		{
			if ( !sourceSlots.Contains( i ) )
				continue;

			int excess = counts[ i ] - targets[ i ];
			if ( excess <= 0 )
				continue;

			if ( excess > bestExcess || ( excess == bestExcess && ( fromSlot < 0 || i < fromSlot ) ) )
			{
				bestExcess = excess;
				fromSlot = i;
			}
		}

		return fromSlot;
	}

	static int FindBestLevelDestination(
		int slotCount,
		int sourceSlot,
		int[] counts,
		int[] targets,
		int maxPerSlot )
	{
		int toSlot = -1;
		int bestDeficit = 0;

		for ( int i = 0; i < slotCount; i++ )
		{
			if ( i == sourceSlot )
				continue;

			if ( maxPerSlot > 0 && counts[ i ] >= maxPerSlot )
				continue;

			int delta = counts[ i ] - targets[ i ];
			if ( delta >= 0 )
				continue;

			if ( delta < bestDeficit || ( delta == bestDeficit && ( toSlot < 0 || i < toSlot ) ) )
			{
				bestDeficit = delta;
				toSlot = i;
			}
		}

		return toSlot;
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
