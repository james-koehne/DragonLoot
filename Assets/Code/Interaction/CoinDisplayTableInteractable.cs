using System.Collections;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Typed coin display: accepts matching carried coins into a generated horizontal slot grid
/// (Displayed state). Default mode uses one accepted coin type for every slot.
/// Mixed-column mode assigns a required coin type per column (every row in that column shares it).
/// Coins are stackable, so they pile vertically in each slot. Pickup takes from the top down.
/// Aim at an existing pile to stack onto it; aim at the table body fills the shortest matching
/// pile (left-to-right, top-left wins ties).
/// Hold-R whole-stack place lands on the aimed pile when stacking, otherwise the shortest
/// matching pile. Auto-level waits until the table is idle (no new coins for a short delay),
/// then peels excess coins into shorter columns of the same coin type. Adding more coins
/// cancels an in-progress level and restarts the idle timer.
/// Each display levels independently; placing on another table does not interrupt this one.
/// Setup: collider on root, child DisplayArea, assign accepted treasure + grid settings
/// (or enable mixed column requirements and assign one coin per column).
/// </summary>
public class CoinDisplayTableInteractable : TypedDisplayTableInteractable
{
	static readonly List<CoinDisplayTableInteractable> All = new List<CoinDisplayTableInteractable>( 32 );

	struct LevelMove
	{
		public int FromSlot;
		public int ToSlot;
	}

	[Header( "Mixed Column Requirements" )]
	[Tooltip( "When enabled, each column uses Column Required Coins instead of the shared Accepted Treasure." )]
	[SerializeField]
	bool useMixedColumnRequirements;

	[Tooltip( "Required coin per column, left to right. Size matches Columns. Every row in a column shares that coin." )]
	[SerializeField]
	TreasureDefinition[] columnRequiredCoins;

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
	[Tooltip( "After a hold-R whole stack lands, peel excess coins off that pile into shorter columns." )]
	[SerializeField]
	bool autoLevelAfterWholeStackPlace = true;

	[Tooltip( "When enabled, every column can donate coins and leveling also runs after single-coin place or pickup — not only after a stack is added." )]
	[SerializeField]
	bool autoLevelAllStacks;

	[Tooltip( "Wait this long with no new coins added before auto-level starts. Adding coins during the wait or during leveling cancels and restarts the wait." )]
	[SerializeField]
	[Min( 0f )]
	float autoLevelIdleDelay = 0.5f;

	[Tooltip( "Multiplier for leveling flight and stagger speed. 2 = twice as fast as the original timings." )]
	[SerializeField]
	[Min( 0.1f )]
	float autoLevelSpeed = 2f;

	[Tooltip( "Delay between starting each leveling coin flight." )]
	[SerializeField]
	[Min( 0f )]
	float levelCoinStagger = 0.06f;

	[Tooltip( "Extra pause after the idle delay before the first leveling coin flies." )]
	[SerializeField]
	[Min( 0f )]
	float levelStartDelay = 0.12f;

	[Tooltip( "Minimum flip arc height when a coin flies between display columns." )]
	[SerializeField]
	[Min( 0.05f )]
	float levelCrossSlotArcHeight = 0.28f;

	readonly List<LevelMove> _levelMoves = new List<LevelMove>( 128 );
	readonly HashSet<int> _levelSourceSlots = new HashSet<int>();
	readonly List<int> _levelTypeGroup = new List<int>( 32 );
	readonly HashSet<TreasureDefinition> _seenLevelTypes = new HashSet<TreasureDefinition>();

	Coroutine _levelRoutine;
	Coroutine _levelIdleRoutine;
	bool _levelPending;
	bool _levelStartDelayApplied;
	TreasureItem _levelFlightCoin;
	int _levelFlightFromSlot = -1;
	bool _levelFlightSeated;

	public override TreasureOwnerKind OwnerKind => TreasureOwnerKind.CoinTable;

	protected override TreasureCategory RequiredCategory => TreasureCategory.Coin;

	protected override string DefaultInteractionName => "Coin Display";

	protected override bool UsesLowestPilePlacement => true;

	public override bool UsesPerSlotRequirements => useMixedColumnRequirements;

	public override bool AllowsVerticalStack => true;

	public TreasureDefinition AcceptedCoin => AcceptedTreasure;

	public IReadOnlyList<TreasureItem> DisplayedCoins => DisplayedItems;

	public bool IsAutoLeveling => _levelRoutine != null || _levelIdleRoutine != null;

	public bool UsesMixedColumnRequirements => useMixedColumnRequirements;

	public static IReadOnlyList<CoinDisplayTableInteractable> ActiveTables => All;

	public override TreasureDefinition GetRequiredTreasure( int slotIndex )
	{
		if ( !useMixedColumnRequirements )
			return acceptedTreasure;

		int column = SlotIndexToColumn( slotIndex );
		return GetRequiredCoinForColumn( column );
	}

	public TreasureDefinition GetRequiredCoinForColumn( int column )
	{
		if ( !useMixedColumnRequirements )
			return acceptedTreasure;

		if ( columnRequiredCoins == null || column < 0 || column >= columnRequiredCoins.Length )
			return null;

		return columnRequiredCoins[ column ];
	}

	int SlotIndexToColumn( int slotIndex )
	{
		if ( slotIndex < 0 || columns <= 0 )
			return -1;
		return slotIndex % columns;
	}

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
	/// Hold ContextualInteract whole-stack place: Active (right hand) must match this table.
	/// Deposits Active plus contiguous same-type coins from the left-hand held bottom only.
	/// </summary>
	public bool CanAcceptActiveConnectedCoinStack( TreasureDefinition activeDefinition )
	{
		return Accepts( activeDefinition );
	}

	/// <summary>
	/// Append a whole carried stack into one slot, then peel excess off player-placed piles when enabled.
	/// </summary>
	public int TryAppendWholeStackWithAutoLevel( int slotIndex, IReadOnlyList<TreasureDefinition> definitions )
	{
		int added = TryAppendSlotDefinitions( slotIndex, definitions, respectPerSlotMax: false );
		if ( added > 0 && ( autoLevelAfterWholeStackPlace || autoLevelAllStacks ) )
			BeginAutoLevelStacks( slotIndex );
		return added;
	}

	protected override void OnDisplaySlotChanged( int slotIndex )
	{
		if ( !autoLevelAllStacks )
			return;

		BeginAutoLevelStacks( slotIndex );
	}

	protected override void OnDestroy()
	{
		All.Remove( this );
		StopAutoLevelIdleWait();
		InterruptAutoLevelSorting( restoreFlightCoin: false );
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
		autoLevelIdleDelay = Mathf.Max( 0f, autoLevelIdleDelay );
		autoLevelSpeed = Mathf.Max( 0.1f, autoLevelSpeed );
		levelCoinStagger = Mathf.Max( 0f, levelCoinStagger );
		levelStartDelay = Mathf.Max( 0f, levelStartDelay );
		levelCrossSlotArcHeight = Mathf.Max( 0.05f, levelCrossSlotArcHeight );
		SyncColumnRequiredCoins();
	}

	void SyncColumnRequiredCoins()
	{
		int count = Mathf.Max( 1, columns );
		if ( columnRequiredCoins != null && columnRequiredCoins.Length == count )
			return;

		TreasureDefinition[] next = new TreasureDefinition[ count ];
		int copy = columnRequiredCoins != null ? Mathf.Min( count, columnRequiredCoins.Length ) : 0;
		for ( int i = 0; i < copy; i++ )
			next[ i ] = columnRequiredCoins[ i ];

		for ( int i = copy; i < count; i++ )
			next[ i ] = acceptedTreasure;

		columnRequiredCoins = next;
	}

	protected override bool HasConfiguredAcceptance()
	{
		if ( !useMixedColumnRequirements )
			return acceptedTreasure != null;

		if ( columnRequiredCoins == null )
			return false;

		for ( int i = 0; i < columnRequiredCoins.Length; i++ )
		{
			if ( columnRequiredCoins[ i ] != null )
				return true;
		}

		return false;
	}

	void Start()
	{
		TryFillSlotsOnStart();
	}

	void TryFillSlotsOnStart()
	{
		if ( !fillSlotsOnStart || Slots == null || !HasConfiguredAcceptance() )
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
			if ( GetRequiredTreasure( i ) == null )
			{
				amounts[ i ] = 0;
				continue;
			}

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
		{
			TreasureDefinition required = GetRequiredTreasure( i );
			if ( required == null )
				continue;
			totalAdded += SpawnDisplayedStack( i, required, amounts[ i ] );
		}

		if ( totalAdded > 0 )
			FinishStartFill();
	}

	void BeginAutoLevelStacks( int placedSlot )
	{
		if ( !isActiveAndEnabled || Slots == null || DisplaySlotCapacity <= 0 )
			return;

		// New coins cancel in-progress sorting and restart the idle wait.
		InterruptAutoLevelSorting( restoreFlightCoin: true );

		if ( autoLevelAllStacks )
		{
			MarkAllSlotsAsLevelSources();
		}
		else
		{
			if ( placedSlot < 0 || placedSlot >= DisplaySlotCapacity )
				return;

			_levelSourceSlots.Add( placedSlot );
		}

		if ( _levelSourceSlots.Count <= 0 )
			return;

		StopAutoLevelIdleWait();
		_levelIdleRoutine = StartCoroutine( AutoLevelIdleThenStartRoutine() );
	}

	IEnumerator AutoLevelIdleThenStartRoutine()
	{
		float delay = Mathf.Max( 0f, autoLevelIdleDelay );
		if ( delay > 0f )
			yield return new WaitForSeconds( delay );

		_levelIdleRoutine = null;
		if ( !isActiveAndEnabled || _levelSourceSlots.Count <= 0 )
			yield break;

		if ( autoLevelAllStacks )
			MarkAllSlotsAsLevelSources();

		if ( _levelSourceSlots.Count <= 0 )
			yield break;

		_levelStartDelayApplied = false;
		_levelPending = false;
		_levelRoutine = StartCoroutine( AutoLevelStacksRoutine() );
	}

	void StopAutoLevelIdleWait()
	{
		if ( _levelIdleRoutine == null )
			return;

		StopCoroutine( _levelIdleRoutine );
		_levelIdleRoutine = null;
	}

	void InterruptAutoLevelSorting( bool restoreFlightCoin )
	{
		if ( _levelRoutine != null )
		{
			StopCoroutine( _levelRoutine );
			_levelRoutine = null;
		}

		_levelPending = false;
		_levelStartDelayApplied = false;
		_levelMoves.Clear();

		if ( restoreFlightCoin )
			RestoreInterruptedLevelFlightCoin();
		else
			ClearLevelFlightTracking();
	}

	void RestoreInterruptedLevelFlightCoin()
	{
		TreasureItem coin = _levelFlightCoin;
		int fromSlot = _levelFlightFromSlot;
		bool seated = _levelFlightSeated;
		ClearLevelFlightTracking();

		if ( seated || coin == null )
			return;

		if ( fromSlot < 0 || fromSlot >= DisplaySlotCapacity )
		{
			coin.EnterPhysics( coin.transform.position, coin.transform.rotation );
			return;
		}

		TryPushSlotItem( fromSlot, coin );
		GetSlotWorldPose( fromSlot, Mathf.Max( 0, GetSlotCount( fromSlot ) - 1 ), coin, out Vector3 pos, out Quaternion rot );
		coin.EnterDisplayed( this, pos, rot );
		RefreshSlotVisual( fromSlot, animate: false );
	}

	void ClearLevelFlightTracking()
	{
		_levelFlightCoin = null;
		_levelFlightFromSlot = -1;
		_levelFlightSeated = false;
	}

	void MarkAllSlotsAsLevelSources()
	{
		int slotCount = DisplaySlotCapacity;
		for ( int i = 0; i < slotCount; i++ )
		{
			if ( GetSettledSlotCount( i ) <= 0 )
				continue;
			if ( Slots != null && i < Slots.Length && SlotHasInFlightItem( Slots[ i ] ) )
				continue;
			_levelSourceSlots.Add( i );
		}
	}

	float ResolveAutoLevelSpeed()
	{
		return Mathf.Max( 0.1f, autoLevelSpeed );
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

			float speed = ResolveAutoLevelSpeed();
			float startDelay = levelStartDelay / speed;
			if ( !_levelStartDelayApplied && startDelay > 0f )
			{
				yield return new WaitForSeconds( startDelay );
				_levelStartDelayApplied = true;
			}

			int moveIndex = 0;
			while ( moveIndex < _levelMoves.Count )
			{
				if ( _levelPending )
				{
					if ( autoLevelAllStacks )
						MarkAllSlotsAsLevelSources();
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

				float stagger = levelCoinStagger / speed;
				if ( stagger > 0f && moveIndex < _levelMoves.Count )
					yield return new WaitForSeconds( stagger );
			}

			RefreshDisplayCountAndPublish();
			TryMarkCompleteIfNeeded();

			if ( !_levelPending )
				break;
		}

		_levelSourceSlots.Clear();
		_levelRoutine = null;
		_levelStartDelayApplied = false;
		ClearLevelFlightTracking();
	}

	IEnumerator AnimateLevelMoveRoutine( int fromSlot, int toSlot )
	{
		if ( !TryPopSlotTopItem( fromSlot, out TreasureItem coin ) || coin == null )
			yield break;

		_levelFlightCoin = coin;
		_levelFlightFromSlot = fromSlot;
		_levelFlightSeated = false;

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
			arcHeightOverride: arcHeight,
			speedScale: ResolveAutoLevelSpeed() );

		if ( coin == null || _levelFlightCoin != coin )
			yield break;

		TryPushSlotItem( toSlot, coin );
		GetSlotWorldPose( toSlot, destStackIndex, coin, out endPos, out Quaternion endRot );
		coin.EnterDisplayed( this, endPos, endRot );
		RefreshSlotVisual( toSlot, animate: true );
		PlayTreasurePlaceFeedback( coin );
		_levelFlightSeated = true;
		ClearLevelFlightTracking();
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
			counts[ i ] = GetSettledSlotCount( i );

		if ( !useMixedColumnRequirements )
		{
			BuildLevelMovesForGroup( moves, slotCount, sourceSlots, counts, targets, groupFilter: null );
			return;
		}

		// Level each required coin type independently so copper never flies into silver columns.
		_seenLevelTypes.Clear();
		for ( int i = 0; i < slotCount; i++ )
		{
			TreasureDefinition required = GetRequiredTreasure( i );
			if ( required == null || !_seenLevelTypes.Add( required ) )
				continue;

			_levelTypeGroup.Clear();
			for ( int j = 0; j < slotCount; j++ )
			{
				if ( GetRequiredTreasure( j ) == required )
					_levelTypeGroup.Add( j );
			}

			BuildLevelMovesForGroup( moves, slotCount, sourceSlots, counts, targets, _levelTypeGroup );
		}
	}

	void BuildLevelMovesForGroup(
		List<LevelMove> moves,
		int slotCount,
		HashSet<int> sourceSlots,
		int[] counts,
		int[] targets,
		List<int> groupFilter )
	{
		int total = 0;
		int groupSize = 0;
		for ( int i = 0; i < slotCount; i++ )
		{
			if ( groupFilter != null && !groupFilter.Contains( i ) )
			{
				targets[ i ] = counts[ i ];
				continue;
			}

			total += counts[ i ];
			groupSize++;
		}

		if ( groupSize <= 0 || total <= 0 )
			return;

		int baseCount = total / groupSize;
		int remainder = total % groupSize;
		int groupOrdinal = 0;
		for ( int i = 0; i < slotCount; i++ )
		{
			if ( groupFilter != null && !groupFilter.Contains( i ) )
				continue;

			targets[ i ] = baseCount + ( groupOrdinal < remainder ? 1 : 0 );
			groupOrdinal++;
		}

		int maxPerSlot = MaxStackPerSlotLimit;
		if ( maxPerSlot > 0 )
		{
			for ( int i = 0; i < slotCount; i++ )
			{
				if ( groupFilter != null && !groupFilter.Contains( i ) )
					continue;
				targets[ i ] = Mathf.Min( targets[ i ], maxPerSlot );
			}
		}

		while ( true )
		{
			int fromSlot = FindBestLevelSource( slotCount, sourceSlots, counts, targets, groupFilter );
			if ( fromSlot < 0 )
				break;

			int toSlot = FindBestLevelDestination( slotCount, fromSlot, counts, targets, maxPerSlot, groupFilter );
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
		int[] targets,
		List<int> groupFilter )
	{
		int fromSlot = -1;
		int bestExcess = 0;

		for ( int i = 0; i < slotCount; i++ )
		{
			if ( !sourceSlots.Contains( i ) )
				continue;
			if ( groupFilter != null && !groupFilter.Contains( i ) )
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
		int maxPerSlot,
		List<int> groupFilter )
	{
		int toSlot = -1;
		int bestDeficit = 0;

		for ( int i = 0; i < slotCount; i++ )
		{
			if ( i == sourceSlot )
				continue;
			if ( groupFilter != null && !groupFilter.Contains( i ) )
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
			AcceptedCoin = useMixedColumnRequirements ? null : AcceptedTreasure
		} );
	}

	protected override string GetSlotGizmoLabel( int slotIndex )
	{
		if ( !useMixedColumnRequirements )
			return slotIndex.ToString();

		int column = SlotIndexToColumn( slotIndex );
		int row = columns > 0 ? slotIndex / columns : 0;
		TreasureDefinition required = GetRequiredCoinForColumn( column );
		string typeLabel = required == null
			? "?"
			: ( !string.IsNullOrEmpty( required.displayName ) ? required.displayName : required.name );

		// Label the front of each column with the coin type; deeper rows keep a light index.
		if ( row == 0 )
			return "C" + column + "\n" + typeLabel;

		return slotIndex.ToString();
	}

	protected override void DrawLayoutGizmos()
	{
		if ( !useMixedColumnRequirements )
		{
			base.DrawLayoutGizmos();
			return;
		}

		Transform area = displayArea != null ? displayArea : transform;
		if ( area == null )
			return;

		int count = SlotCount;
		float markerRadius = Mathf.Clamp( slotSpacing * 0.18f, 0.02f, 0.08f );

		DisplayTableSlotLayout.DrawLayoutGizmos(
			area,
			rows,
			columns,
			slotSpacing,
			margin,
			new Color( 0.35f, 1f, 0.55f, 0.15f ),
			new Color( 0.35f, 1f, 0.55f, 0.35f ) );

		for ( int i = 0; i < count; i++ )
		{
			TreasureDefinition required = GetRequiredTreasure( i );
			Vector3 world = area.TransformPoint(
				DisplayTableSlotLayout.GetSlotLocalPosition( i, rows, columns, slotSpacing, margin ) );
			Gizmos.color = ResolveSlotGizmoColor( required );
			Gizmos.DrawWireSphere( world, markerRadius );
			Gizmos.DrawLine( world, world + area.up * ( markerRadius * 2f ) );

#if UNITY_EDITOR
			int row = columns > 0 ? i / columns : 0;
			if ( row == 0 )
			{
				UnityEditor.Handles.color = Gizmos.color;
				UnityEditor.Handles.Label( world + area.up * 0.05f, GetSlotGizmoLabel( i ) );
			}
#endif
		}
	}

	static Color ResolveSlotGizmoColor( TreasureDefinition required )
	{
		if ( required == null )
			return new Color( 1f, 0.35f, 0.35f, 0.9f );

		int hash = required.GetInstanceID();
		float hue = Mathf.Abs( hash % 360 ) / 360f;
		return Color.HSVToRGB( hue, 0.65f, 1f );
	}
}
