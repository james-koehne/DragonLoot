using System;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// World coin sorter: hopper stack intake → timed process (bottom-first) → per-type output stacks.
/// Levels via <see cref="UpgradeSystem"/> / <see cref="CoinSortingStationDefinition"/>.
/// </summary>
public class CoinSortingStation : MonoBehaviour
{
	[Serializable]
	public struct ChuteBinding
	{
		public TreasureDefinition coin;
		public Transform chute;
	}

	static readonly List<CoinSortingStation> All = new List<CoinSortingStation>( 8 );
	static readonly List<TreasureDefinition> ConsumeScratch = new List<TreasureDefinition>( 64 );
	static readonly List<TreasureItem> CarryDumpScratch = new List<TreasureItem>( 64 );

	[SerializeField]
	CoinSortingHopper hopper;

	[SerializeField]
	CoinSortingCrankInteractable crank;

	[SerializeField]
	List<ChuteBinding> chutes = new List<ChuteBinding>();

	[Tooltip( "Optional override; null resolves CoinSortingStationDefinition via Addressables." )]
	[SerializeField]
	CoinSortingStationDefinition definitionOverride;

	[Tooltip( "World offset from hopper collider top for the intake stack contact." )]
	[SerializeField]
	Vector3 hopperStackOffset = new Vector3( 0f, 0.02f, 0f );

	readonly Dictionary<TreasureDefinition, GroundCoinStack> _activeOutputByType =
		new Dictionary<TreasureDefinition, GroundCoinStack>();
	readonly Dictionary<TreasureDefinition, int> _fullStackIndexByType =
		new Dictionary<TreasureDefinition, int>();
	readonly HashSet<TreasureDefinition> _missingChuteLogged = new HashSet<TreasureDefinition>();

	CoinSortingStationDefinition _definition;
	GroundCoinStack _hopperStack;
	float _processAccumulator;
	float _crankActiveUntil;
	bool _defaultLevelEnsured;

	public static IReadOnlyList<CoinSortingStation> ActiveStations => All;

	public int BufferedCount => _hopperStack != null ? _hopperStack.Count : 0;

	public int StationLevel => ResolveStationLevel();

	public int HopperCapacity
	{
		get
		{
			CoinSortingStationDefinition def = ResolveDefinition();
			if ( def == null )
				return 50;
			return def.ResolveHopperCapacity( StationLevel );
		}
	}

	public bool IsHopperFull => BufferedCount >= HopperCapacity;

	public bool HasRoomFor( int count )
	{
		if ( count <= 0 )
			return true;
		return BufferedCount + count <= HopperCapacity;
	}

	public int RemainingCapacity => Mathf.Max( 0, HopperCapacity - BufferedCount );

	public bool IsCrankActive => Time.time <= _crankActiveUntil;

	public bool IsProcessing
	{
		get
		{
			int level = StationLevel;
			if ( level < 1 || BufferedCount <= 0 )
				return false;

			CoinSortingStationDefinition def = ResolveDefinition();
			if ( def == null )
				return false;

			if ( def.IsAutomatic( level ) )
				return true;

			return def.RequiresCrank( level ) && IsCrankActive;
		}
	}

	public CoinSortingHopper Hopper => hopper;

	public CoinSortingCrankInteractable Crank => crank;

	public GroundCoinStack HopperStack => EnsureHopperStack();

	void OnEnable()
	{
		if ( !All.Contains( this ) )
			All.Add( this );
	}

	void OnDisable()
	{
		All.Remove( this );
	}

	void Awake()
	{
		ResolveDefinition();
		EnsureChildRefs();
		EnsureBodyPlacementCollider();
		if ( hopper != null )
			hopper.BindStation( this );
		if ( crank != null )
			crank.BindStation( this );
	}

	void Start()
	{
		EnsureDefaultUpgradeLevel();
		EnsureHopperStack();
	}

	void Update()
	{
		EnsureDefaultUpgradeLevel();
		SyncHopperStackCapacity();
		TickProcess( Time.deltaTime );
	}

	void EnsureChildRefs()
	{
		if ( hopper == null )
			hopper = GetComponentInChildren<CoinSortingHopper>( true );
		if ( crank == null )
			crank = GetComponentInChildren<CoinSortingCrankInteractable>( true );
	}

	/// <summary>
	/// Body visuals often have no collider (setup strips the primitive one). Restore a raycast
	/// collider so aiming anywhere on the station (except the crank) can deposit into the hopper.
	/// </summary>
	void EnsureBodyPlacementCollider()
	{
		Transform body = transform.Find( "Body" );
		if ( body == null )
			return;

		if ( body.GetComponent<Collider>() != null )
			return;

		BoxCollider box = body.gameObject.AddComponent<BoxCollider>();
		box.isTrigger = false;

		Renderer renderer = body.GetComponent<Renderer>();
		if ( renderer != null )
		{
			Bounds local = renderer.localBounds;
			box.center = local.center;
			box.size = local.size;
		}
		else
		{
			box.center = Vector3.zero;
			box.size = Vector3.one;
		}
	}

	/// <summary>
	/// Maps a ray hit on this station to the hopper placement target, ignoring the crank.
	/// </summary>
	public static CoinSortingHopper ResolveHopperPlacementFromCollider( Collider collider )
	{
		if ( collider == null )
			return null;

		if ( collider.GetComponentInParent<CoinSortingCrankInteractable>() != null )
			return null;

		CoinSortingStation station = collider.GetComponentInParent<CoinSortingStation>();
		if ( station == null )
			return null;

		return station.hopper != null ? station.hopper : station.GetComponentInChildren<CoinSortingHopper>( true );
	}

	CoinSortingStationDefinition ResolveDefinition()
	{
		if ( definitionOverride != null )
		{
			_definition = definitionOverride;
			return _definition;
		}

		return RuntimeDefinition.Resolve( ref _definition );
	}

	void EnsureDefaultUpgradeLevel()
	{
		if ( _defaultLevelEnsured )
			return;

		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;
		if ( player == null )
			return;

		UpgradeSystem system = UpgradeSystem.Ensure( player );
		if ( system == null )
			return;

		CoinSortingStationDefinition def = ResolveDefinition();
		string id = def != null
			? def.ResolveUpgradeId()
			: CoinSortingStationDefinition.DefaultUpgradeId;

		if ( !system.TryGetDefinition( id, out _ ) )
		{
			_defaultLevelEnsured = true;
			return;
		}

		if ( !system.IsUnlocked( id ) || system.GetUpgradeLevel( id ) < 1 )
			system.SetUpgradeLevel( id, 1 );

		_defaultLevelEnsured = true;
	}

	int ResolveStationLevel()
	{
		EnsureDefaultUpgradeLevel();
		UpgradeSystem system = UpgradeSystem.Instance;
		if ( system == null )
			return 1;

		CoinSortingStationDefinition def = ResolveDefinition();
		string id = def != null
			? def.ResolveUpgradeId()
			: CoinSortingStationDefinition.DefaultUpgradeId;

		int level = system.GetUpgradeLevel( id );
		return level < 1 ? 1 : level;
	}

	public void NotifyCrankPulse()
	{
		CoinSortingStationDefinition def = ResolveDefinition();
		float grace = def != null ? def.crankHoldGrace : 0.35f;
		_crankActiveUntil = Time.time + Mathf.Max( 0.05f, grace );
	}

	GroundCoinStack EnsureHopperStack()
	{
		if ( _hopperStack != null )
			return _hopperStack;

		Transform anchor = hopper != null ? hopper.transform : transform;
		Vector3 contact = ResolveHopperStackContact();
		Quaternion rot = TreasureOrientation.FlattenUpright( anchor.rotation );
		_hopperStack = GroundCoinStack.CreateAt( contact, rot );
		_hopperStack.name = "HopperCoinStack";
		_hopperStack.transform.SetParent( anchor, true );
		_hopperStack.ConfigureAsMachineBuffer( HopperCapacity, this );
		return _hopperStack;
	}

	Vector3 ResolveHopperStackContact()
	{
		if ( hopper != null )
		{
			Collider col = hopper.GetComponent<Collider>();
			if ( col != null )
			{
				Bounds bounds = col.bounds;
				return new Vector3( bounds.center.x, bounds.max.y, bounds.center.z ) + hopperStackOffset;
			}

			return hopper.transform.position + hopperStackOffset;
		}

		return transform.position + Vector3.up + hopperStackOffset;
	}

	void SyncHopperStackCapacity()
	{
		if ( _hopperStack == null )
			return;
		_hopperStack.SetMaxCountOverride( HopperCapacity );
	}

	public bool IsHopperStack( GroundCoinStack stack )
	{
		return stack != null && stack == _hopperStack;
	}

	/// <summary>Enqueue a single coin definition if capacity allows (appends onto hopper stack).</summary>
	public bool TryEnqueue( TreasureDefinition definition )
	{
		if ( !GroundCoinStack.IsGroundStackableCoin( definition ) )
			return false;
		if ( IsHopperFull )
			return false;

		GroundCoinStack stack = EnsureHopperStack();
		return stack.TryAppendDefinition( definition );
	}

	/// <summary>Enqueue as many defs as capacity allows; returns number accepted.</summary>
	public int TryEnqueueRange( IReadOnlyList<TreasureDefinition> definitions )
	{
		if ( definitions == null || definitions.Count == 0 )
			return 0;

		int accepted = 0;
		for ( int i = 0; i < definitions.Count; i++ )
		{
			if ( !TryEnqueue( definitions[ i ] ) )
				break;
			accepted++;
		}

		return accepted;
	}

	/// <summary>
	/// Dump every stackable coin from <paramref name="carry"/> onto the hopper stack in one go.
	/// Non-coins and capacity overflow stay in carry. Accepted coins append immediately (no flight).
	/// </summary>
	public bool TryDumpCarryIntoHopper( PlayerCarry carry )
	{
		if ( carry == null || carry.Count <= 0 )
			return false;

		CarryDumpScratch.Clear();
		carry.CopyCarriedItemsInOrder( CarryDumpScratch );
		if ( CarryDumpScratch.Count == 0 )
			return false;

		GroundCoinStack stack = EnsureHopperStack();
		bool any = false;

		for ( int i = 0; i < CarryDumpScratch.Count; i++ )
		{
			if ( IsHopperFull || stack.IsFull )
				break;

			TreasureItem member = CarryDumpScratch[ i ];
			if ( member == null || !GroundCoinStack.IsGroundStackableCoin( member ) )
				continue;

			TreasureDefinition definition = member.Definition;
			if ( !carry.TryDetachItem( member ) )
				continue;

			TreasureItemFactory.Despawn( member );
			if ( !stack.TryAppendDefinition( definition ) )
				break;

			any = true;
		}

		CarryDumpScratch.Clear();
		return any;
	}

	public bool TryAbsorbLooseCoin( TreasureItem item )
	{
		if ( item == null || !GroundCoinStack.IsGroundStackableCoin( item ) )
			return false;
		if ( !item.IsWorldLoose || item.IsInFlight )
			return false;
		if ( IsHopperFull )
			return false;

		GroundCoinStack stack = EnsureHopperStack();
		return stack.TryAbsorbLooseImmediate( item );
	}

	public bool TryAbsorbStack( GroundCoinStack stack )
	{
		if ( stack == null || IsHopperStack( stack ) || IsHopperFull )
			return false;

		ConsumeScratch.Clear();
		if ( !stack.TryConsumeAllDefinitions( ConsumeScratch ) )
		{
			ConsumeScratch.Clear();
			return false;
		}

		int accepted = TryEnqueueRange( ConsumeScratch );
		ConsumeScratch.Clear();
		return accepted > 0;
	}

	/// <summary>
	/// Absorbs a stack only when the entire slot count fits; otherwise leaves the stack alone.
	/// </summary>
	public bool TryAbsorbStackIfFits( GroundCoinStack stack )
	{
		if ( stack == null || stack.Count <= 0 || IsHopperStack( stack ) )
			return false;
		if ( !HasRoomFor( stack.Count ) )
			return false;
		return TryAbsorbStack( stack );
	}

	void TickProcess( float dt )
	{
		if ( dt <= 0f || !IsProcessing )
		{
			if ( !IsProcessing )
				_processAccumulator = 0f;
			return;
		}

		CoinSortingStationDefinition def = ResolveDefinition();
		float rate = def != null
			? def.ResolveCoinsPerSecond( StationLevel )
			: 4f;

		_processAccumulator += rate * dt;
		GroundCoinStack hopperStack = EnsureHopperStack();
		while ( _processAccumulator >= 1f && hopperStack != null && hopperStack.Count > 0 )
		{
			if ( !hopperStack.TryPeekBottomDefinition( out TreasureDefinition next ) )
				break;

			if ( !EmitOne( next ) )
				break;

			if ( !hopperStack.TryConsumeBottomDefinition( out _ ) )
				break;

			_processAccumulator -= 1f;
		}

		if ( BufferedCount == 0 )
			_processAccumulator = 0f;
	}

	/// <summary>Debug: force process one buffered coin if any.</summary>
	public bool DebugForceProcessOne()
	{
		GroundCoinStack hopperStack = EnsureHopperStack();
		if ( hopperStack == null || hopperStack.Count <= 0 )
			return false;

		if ( !hopperStack.TryPeekBottomDefinition( out TreasureDefinition next ) )
			return false;
		if ( !EmitOne( next ) )
			return false;

		return hopperStack.TryConsumeBottomDefinition( out _ );
	}

	bool EmitOne( TreasureDefinition definition )
	{
		if ( definition == null )
			return false;

		Transform chute = FindChute( definition );
		if ( chute == null )
		{
			if ( _missingChuteLogged.Add( definition ) )
				Debug.LogWarning(
					$"CoinSortingStation '{name}': no chute for coin '{definition.name}'. Add a ChuteBinding.",
					this );
			return false;
		}

		GroundCoinStack stack = GetOrCreateOutputStack( definition, chute );
		if ( stack == null )
			return false;

		if ( stack.IsFull || !stack.TryAppendDefinition( definition ) )
		{
			BumpFullStackIndex( definition );
			stack = GetOrCreateOutputStack( definition, chute );
			if ( stack == null || !stack.TryAppendDefinition( definition ) )
				return false;
		}

		_activeOutputByType[ definition ] = stack;
		return true;
	}

	Transform FindChute( TreasureDefinition definition )
	{
		if ( definition == null || chutes == null )
			return null;

		for ( int i = 0; i < chutes.Count; i++ )
		{
			ChuteBinding binding = chutes[ i ];
			if ( binding.coin == null || binding.chute == null )
				continue;
			if ( SameCoinType( binding.coin, definition ) )
				return binding.chute;
		}

		return null;
	}

	GroundCoinStack GetOrCreateOutputStack( TreasureDefinition definition, Transform chute )
	{
		if ( _activeOutputByType.TryGetValue( definition, out GroundCoinStack existing ) )
		{
			if ( existing != null && !existing.IsFull )
				return existing;

			if ( existing != null && existing.IsFull )
				BumpFullStackIndex( definition );
			else
				_activeOutputByType.Remove( definition );
		}

		int index = 0;
		if ( _fullStackIndexByType.TryGetValue( definition, out int stored ) )
			index = stored;

		CoinSortingStationDefinition def = ResolveDefinition();
		float lateral = def != null ? def.fullStackLateralOffset : 0.35f;
		Vector3 pos = chute.position + chute.right * ( lateral * index );
		Quaternion rot = TreasureOrientation.FlattenUpright( chute.rotation );

		GroundCoinStack nearest = GroundCoinStack.FindNearest( pos, lateral * 0.45f );
		if ( nearest != null
			&& !nearest.IsFull
			&& nearest.TryGetHomogeneousDefinition( out TreasureDefinition homo )
			&& SameCoinType( homo, definition ) )
		{
			_activeOutputByType[ definition ] = nearest;
			return nearest;
		}

		GroundCoinStack created = GroundCoinStack.CreateAt( pos, rot );
		_activeOutputByType[ definition ] = created;
		return created;
	}

	void BumpFullStackIndex( TreasureDefinition definition )
	{
		int index = 0;
		if ( _fullStackIndexByType.TryGetValue( definition, out int stored ) )
			index = stored;
		_fullStackIndexByType[ definition ] = index + 1;
		_activeOutputByType.Remove( definition );
	}

	static bool SameCoinType( TreasureDefinition a, TreasureDefinition b )
	{
		if ( a == b )
			return true;
		if ( a == null || b == null )
			return false;
		if ( !string.IsNullOrEmpty( a.id ) && a.id == b.id )
			return true;
		return false;
	}

#if UNITY_EDITOR
	public void EditorSetChutes( List<ChuteBinding> bindings )
	{
		chutes = bindings != null ? bindings : new List<ChuteBinding>();
	}

	public void EditorSetHopper( CoinSortingHopper value )
	{
		hopper = value;
	}

	public void EditorSetCrank( CoinSortingCrankInteractable value )
	{
		crank = value;
	}
#endif
}
