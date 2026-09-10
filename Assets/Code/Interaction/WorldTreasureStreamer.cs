using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Distance-streams world clutter: stacks and loose coins live as data records in a spatial
/// grid, with a capped pool of GameObjects only inside the interaction radius.
/// Gems/artifacts keep pose-park records and wake via <see cref="TreasureItemFactory.SpawnSync"/>.
/// </summary>
public class WorldTreasureStreamer : MonoBehaviour
{
	public struct CellCoord : System.IEquatable<CellCoord>
	{
		public int X;
		public int Z;

		public CellCoord( int x, int z )
		{
			X = x;
			Z = z;
		}

		public bool Equals( CellCoord other )
		{
			return X == other.X && Z == other.Z;
		}

		public override bool Equals( object obj )
		{
			return obj is CellCoord other && Equals( other );
		}

		public override int GetHashCode()
		{
			unchecked
			{
				return ( X * 73856093 ) ^ ( Z * 19349663 );
			}
		}
	}

	struct CoinStackRecord
	{
		public Vector3 Position;
		public Quaternion Rotation;
		public float VariationSeed;
		public List<TreasureDefinition> Slots;
		public GroundCoinStack Resident;
		public bool PendingExtract;
		public CellCoord Cell;
	}

	struct BarStackRecord
	{
		public Vector3 Position;
		public Quaternion Rotation;
		public TreasureDefinition Definition;
		public int Count;
		public GroundGoldBarStack Resident;
		public bool PendingExtract;
		public CellCoord Cell;
	}

	struct LooseRecord
	{
		public TreasureDefinition Definition;
		public Vector3 Position;
		public Quaternion Rotation;
		public TreasurePileVisual OriginPile;
		public bool SurfaceMode;
		public TreasureItem Resident;
		public bool PendingExtract;
		public CellCoord Cell;
	}

	class GridCell
	{
		public readonly List<int> CoinStacks = new List<int>( 8 );
		public readonly List<int> BarStacks = new List<int>( 4 );
		public readonly List<int> Loose = new List<int>( 8 );
	}

	static WorldTreasureStreamer _instance;

	readonly Dictionary<CellCoord, GridCell> _cells = new Dictionary<CellCoord, GridCell>( 256 );
	readonly List<CoinStackRecord> _coinStacks = new List<CoinStackRecord>( 256 );
	readonly List<BarStackRecord> _barStacks = new List<BarStackRecord>( 64 );
	readonly List<LooseRecord> _loose = new List<LooseRecord>( 256 );
	readonly List<int> _freeCoinRecords = new List<int>( 64 );
	readonly List<int> _freeBarRecords = new List<int>( 16 );
	readonly List<int> _freeLooseRecords = new List<int>( 64 );
	readonly List<GroundCoinStack> _coinPool = new List<GroundCoinStack>( 64 );
	readonly List<GroundGoldBarStack> _barPool = new List<GroundGoldBarStack>( 16 );
	readonly List<int> _residentCoin = new List<int>( 64 );
	readonly List<int> _residentBar = new List<int>( 16 );
	readonly List<int> _residentLoose = new List<int>( 64 );
	readonly List<TreasureItem> _trackedLive = new List<TreasureItem>( 128 );
	readonly List<TreasureDefinition> _slotScratch = new List<TreasureDefinition>( 64 );
	readonly List<GridCell> _cellScratch = new List<GridCell>( 32 );
	readonly HashSet<TreasureItem> _trackedSet = new HashSet<TreasureItem>();

	GoldPileLootStreamSettings _settings;
	Transform _poolRoot;
	int _createdCoinHosts;
	int _createdBarHosts;
	int _wakeBudget;
	int _evictBudget;
	int _extractBudget;
	int _propSpawnBudget;
	int _cellsTicked;
	int _wakesThisTick;
	int _evictsThisTick;
	int _extractsThisTick;

	public static int HiddenCoinStackCount => _instance != null ? CountHiddenCoinStacks() : 0;
	public static int HiddenBarStackCount => _instance != null ? CountHiddenBarStacks() : 0;
	public static int HiddenLooseCount => _instance != null ? CountHiddenLoose() : 0;
	public static int ResidentCoinStackCount => _instance != null ? _instance._residentCoin.Count : 0;
	public static int ResidentBarStackCount => _instance != null ? _instance._residentBar.Count : 0;
	public static int ResidentLooseCount => _instance != null ? _instance._residentLoose.Count : 0;
	public static int CoinPoolFreeCount => _instance != null ? _instance._coinPool.Count : 0;
	public static int BarPoolFreeCount => _instance != null ? _instance._barPool.Count : 0;
	public static int CellsTicked => _instance != null ? _instance._cellsTicked : 0;
	public static int WakesLastTick => _instance != null ? _instance._wakesThisTick : 0;
	public static int EvictsLastTick => _instance != null ? _instance._evictsThisTick : 0;
	public static int ExtractsLastTick => _instance != null ? _instance._extractsThisTick : 0;
	public static int ParkedPropCount => HiddenLooseCount;

	public static void EnsureExists()
	{
		if ( _instance != null )
			return;
		if ( !Application.isPlaying )
			return;

		GameObject go = new GameObject( "WorldTreasureStreamer" );
		_instance = go.AddComponent<WorldTreasureStreamer>();
		Object.DontDestroyOnLoad( go );
	}

	public static GoldPileLootStreamSettings ResolveSettings()
	{
		EnsureExists();
		if ( _instance == null )
			return null;

		_instance.EnsureSettings();
		return _instance._settings;
	}

	public static void NotifyLoose( TreasureItem item )
	{
		if ( item == null || !Application.isPlaying )
			return;

		EnsureExists();
		if ( _instance == null )
			return;

		_instance._trackedSet.Add( item );
		if ( !_instance._trackedLive.Contains( item ) )
			_instance._trackedLive.Add( item );
	}

	public static void NotifyOwned( TreasureItem item )
	{
		if ( item == null || _instance == null )
			return;

		_instance._trackedSet.Remove( item );
		_instance._trackedLive.Remove( item );
		_instance.DetachLooseResident( item );
	}

	public static void CancelParkForItem( TreasureItem item )
	{
		NotifyOwned( item );
	}

	public static void CancelParkMatching( TreasureDefinition definition, Vector3 nearWorld, float radius )
	{
		if ( _instance == null || definition == null )
			return;

		float radiusSq = radius * radius;
		for ( int i = 0; i < _instance._loose.Count; i++ )
		{
			LooseRecord record = _instance._loose[ i ];
			if ( record.Definition != definition || record.Resident != null )
				continue;
			if ( ( record.Position - nearWorld ).sqrMagnitude > radiusSq )
				continue;
			_instance.FreeLooseRecord( i );
		}
	}

	public static int CountParkedCategory( TreasureCategory category )
	{
		if ( _instance == null )
			return 0;

		int n = 0;
		for ( int i = 0; i < _instance._loose.Count; i++ )
		{
			LooseRecord record = _instance._loose[ i ];
			if ( record.Definition == null || record.Resident != null )
				continue;
			if ( record.Definition.category == category )
				n++;
		}

		return n;
	}

	public static int CountParkedGems()
	{
		return CountParkedCategory( TreasureCategory.Gem );
	}

	public static int CountParkedArtifacts()
	{
		if ( _instance == null )
			return 0;

		int n = 0;
		for ( int i = 0; i < _instance._loose.Count; i++ )
		{
			LooseRecord record = _instance._loose[ i ];
			if ( record.Definition == null || record.Resident != null )
				continue;
			if ( GoldPileLootStreamSettings.IsArtifactStreamBucket( record.Definition.category ) )
				n++;
		}

		return n;
	}

	public static bool TryTakeParkedForReclaim(
		bool gems,
		out TreasureDefinition definition,
		out Vector3 position,
		out TreasurePileVisual origin )
	{
		definition = null;
		position = Vector3.zero;
		origin = null;
		if ( _instance == null )
			return false;

		int best = -1;
		float bestScore = float.MinValue;
		for ( int i = 0; i < _instance._loose.Count; i++ )
		{
			LooseRecord record = _instance._loose[ i ];
			if ( record.Definition == null || record.Resident != null )
				continue;

			bool isGem = record.Definition.category == TreasureCategory.Gem;
			bool isArt = GoldPileLootStreamSettings.IsArtifactStreamBucket( record.Definition.category );
			if ( gems && !isGem )
				continue;
			if ( !gems && !isArt )
				continue;

			float score = record.OriginPile != null ? 1f : 0f;
			if ( score > bestScore )
			{
				bestScore = score;
				best = i;
			}
		}

		if ( best < 0 )
			return false;

		LooseRecord chosen = _instance._loose[ best ];
		_instance.FreeLooseRecord( best );
		definition = chosen.Definition;
		position = chosen.Position;
		origin = chosen.OriginPile;
		return true;
	}

	public static GroundCoinStack AcquireCoinStack( Vector3 position, Quaternion rotation )
	{
		EnsureExists();
		if ( _instance == null )
			return GroundCoinStack.CreateUnpooled( position, rotation );

		return _instance.RentCoinStack( position, rotation );
	}

	public static GroundGoldBarStack AcquireBarStack( Vector3 position, Quaternion rotation )
	{
		EnsureExists();
		if ( _instance == null )
			return GroundGoldBarStack.CreateUnpooled( position, rotation );

		return _instance.RentBarStack( position, rotation );
	}

	public static void SpawnOrRecordCoinStack(
		Vector3 position,
		Quaternion rotation,
		IReadOnlyList<TreasureDefinition> slots,
		string objectName )
	{
		if ( slots == null || slots.Count <= 0 )
			return;

		EnsureExists();
		if ( _instance == null )
		{
			GroundCoinStack fallback = GroundCoinStack.CreateUnpooled( position, rotation );
			fallback.RestoreStreamingSlots( slots, 0f );
			if ( !string.IsNullOrEmpty( objectName ) )
				fallback.name = objectName;
			return;
		}

		_instance.EnsureSettings();
		float distSqr = 0f;
		if ( TryGetFocus( out Vector3 focus ) )
			distSqr = PlanarDistanceSqr( focus, position );

		if ( _instance.IsStackInRange( distSqr, currentlyResident: false, slots.Count ) )
		{
			GroundCoinStack stack = _instance.RentCoinStack( position, rotation );
			stack.RestoreStreamingSlots( slots, 0f );
			if ( !string.IsNullOrEmpty( objectName ) )
				stack.name = objectName;
			return;
		}

		_instance.AddHiddenCoinStack( position, rotation, slots, 0f );
	}

	public static void ReleaseEmptyCoinHost( GroundCoinStack stack )
	{
		if ( stack == null )
			return;

		EnsureExists();
		if ( _instance == null )
		{
			Object.Destroy( stack.gameObject );
			return;
		}

		_instance.ClearCoinResident( stack );
		_instance.ReturnCoinHost( stack );
	}

	public static void ReleaseEmptyBarHost( GroundGoldBarStack stack )
	{
		if ( stack == null )
			return;

		EnsureExists();
		if ( _instance == null )
		{
			Object.Destroy( stack.gameObject );
			return;
		}

		_instance.ClearBarResident( stack );
		_instance.ReturnBarHost( stack );
	}

	public static void NotifyCoinStackPinned( GroundCoinStack stack )
	{
		if ( stack == null || _instance == null )
			return;

		_instance.ForgetCoinStack( stack );
	}

	public static void NotifyCoinStackEnabled( GroundCoinStack stack )
	{
		if ( stack == null || _instance == null )
			return;
		if ( stack.IsStreamPinned )
			return;

		_instance.TrackCoinStack( stack );
	}

	public static void NotifyCoinStackDisabled( GroundCoinStack stack )
	{
		if ( stack == null || _instance == null )
			return;

		_instance.ClearCoinResident( stack );
	}

	public static void NotifyBarStackEnabled( GroundGoldBarStack stack )
	{
		if ( stack == null || _instance == null )
			return;

		_instance.TrackBarStack( stack );
	}

	public static void NotifyBarStackDisabled( GroundGoldBarStack stack )
	{
		if ( stack == null || _instance == null )
			return;

		_instance.ClearBarResident( stack );
	}

	public static bool TryWakeNearestCoinStack(
		Vector3 worldPos,
		float radius,
		float liveBestSq,
		out GroundCoinStack stack,
		out float bestSq )
	{
		stack = null;
		bestSq = liveBestSq;
		if ( _instance == null || radius <= 0f )
			return false;

		int best = _instance.FindHiddenCoinStack( worldPos, radius, liveBestSq );
		if ( best < 0 )
			return false;

		CoinStackRecord record = _instance._coinStacks[ best ];
		bestSq = PlanarDistanceSqr( worldPos, record.Position );
		stack = _instance.WakeCoinStack( best, force: true );
		return stack != null;
	}

	public static bool TryWakeNearestCoinStackAlongRay(
		Ray ray,
		float radius,
		float maxDistance,
		float liveBestPerpSq,
		out GroundCoinStack stack,
		out float bestPerpSq )
	{
		stack = null;
		bestPerpSq = liveBestPerpSq;
		if ( _instance == null )
			return false;

		int best = _instance.FindHiddenCoinStackAlongRay( ray, radius, maxDistance, liveBestPerpSq, out bestPerpSq );
		if ( best < 0 )
			return false;

		stack = _instance.WakeCoinStack( best, force: true );
		return stack != null;
	}

	public static bool TryWakeNearestBarStack(
		Vector3 worldPos,
		float radius,
		float liveBestSq,
		TreasureDefinition required,
		out GroundGoldBarStack stack,
		out float bestSq )
	{
		stack = null;
		bestSq = liveBestSq;
		if ( _instance == null || radius <= 0f )
			return false;

		int best = _instance.FindHiddenBarStack( worldPos, radius, liveBestSq, required );
		if ( best < 0 )
			return false;

		BarStackRecord record = _instance._barStacks[ best ];
		bestSq = PlanarDistanceSqr( worldPos, record.Position );
		stack = _instance.WakeBarStack( best, force: true );
		return stack != null;
	}

	public static bool TryWakeNearestBarStackAlongRay(
		Ray ray,
		float radius,
		float maxDistance,
		float liveBestPerpSq,
		TreasureDefinition required,
		out GroundGoldBarStack stack,
		out float bestPerpSq )
	{
		stack = null;
		bestPerpSq = liveBestPerpSq;
		if ( _instance == null )
			return false;

		int best = _instance.FindHiddenBarStackAlongRay( ray, radius, maxDistance, liveBestPerpSq, required, out bestPerpSq );
		if ( best < 0 )
			return false;

		stack = _instance.WakeBarStack( best, force: true );
		return stack != null;
	}

	public static int CountHiddenGoldBarsInBounds( Bounds bounds )
	{
		if ( _instance == null )
			return 0;

		int n = 0;
		for ( int i = 0; i < _instance._barStacks.Count; i++ )
		{
			BarStackRecord record = _instance._barStacks[ i ];
			if ( record.Definition == null || record.Resident != null )
				continue;
			if ( !bounds.Contains( record.Position ) )
				continue;
			n += Mathf.Max( 0, record.Count );
		}

		return n;
	}

	void Awake()
	{
		if ( _instance != null && _instance != this )
		{
			Destroy( gameObject );
			return;
		}

		_instance = this;
		EnsureSettings();
		EnsurePoolRoot();
	}

	void LateUpdate()
	{
		EnsureSettings();
		_wakesThisTick = 0;
		_evictsThisTick = 0;
		_extractsThisTick = 0;
		_cellsTicked = 0;
		_wakeBudget = _settings != null ? Mathf.Max( 1, _settings.streamWakeBudget ) : 8;
		_evictBudget = _settings != null ? Mathf.Max( 1, _settings.streamEvictBudget ) : 16;
		_extractBudget = _settings != null ? Mathf.Max( 1, _settings.streamExtractBudget ) : 8;
		_propSpawnBudget = 3;

		if ( _settings != null )
			TreasureItemFactory.SetCoinVisualPoolCapacity( Mathf.Max( 8, _settings.coinVisualPoolSize ) );

		if ( !TryGetFocus( out Vector3 focus ) )
			return;

		TickWakeCells( focus );
		TickEvictResidents( focus );
		TickExtract();
		TickParkLive( focus );
	}

	void TickWakeCells( Vector3 focus )
	{
		float chunk = ResolveChunkSize();
		float radius = ResolveStackExitDistance();
		int span = Mathf.CeilToInt( radius / chunk ) + 1;
		int cx = Mathf.FloorToInt( focus.x / chunk );
		int cz = Mathf.FloorToInt( focus.z / chunk );
		float radiusSq = radius * radius;

		for ( int z = cz - span; z <= cz + span; z++ )
		{
			for ( int x = cx - span; x <= cx + span; x++ )
			{
				float cellX = ( x + 0.5f ) * chunk;
				float cellZ = ( z + 0.5f ) * chunk;
				float dx = cellX - focus.x;
				float dz = cellZ - focus.z;
				if ( dx * dx + dz * dz > ( radius + chunk ) * ( radius + chunk ) )
					continue;

				CellCoord coord = new CellCoord( x, z );
				if ( !_cells.TryGetValue( coord, out GridCell cell ) )
					continue;

				_cellsTicked++;
				WakeCell( cell, focus, radiusSq );
				if ( _wakeBudget <= 0 && _propSpawnBudget <= 0 )
					return;
			}
		}
	}

	void WakeCell( GridCell cell, Vector3 focus, float radiusSq )
	{
		for ( int i = 0; i < cell.CoinStacks.Count && _wakeBudget > 0; i++ )
		{
			int index = cell.CoinStacks[ i ];
			CoinStackRecord record = _coinStacks[ index ];
			if ( record.Slots == null || record.Resident != null )
				continue;
			if ( PlanarDistanceSqr( focus, record.Position ) > radiusSq )
				continue;
			if ( !IsStackInRange( PlanarDistanceSqr( focus, record.Position ), currentlyResident: false, CoinCountOf( record ) ) )
				continue;

			WakeCoinStack( index, force: false );
		}

		for ( int i = 0; i < cell.BarStacks.Count && _wakeBudget > 0; i++ )
		{
			int index = cell.BarStacks[ i ];
			BarStackRecord record = _barStacks[ index ];
			if ( record.Definition == null || record.Count <= 0 || record.Resident != null )
				continue;
			if ( !IsStackInRange( PlanarDistanceSqr( focus, record.Position ), currentlyResident: false ) )
				continue;

			WakeBarStack( index, force: false );
		}

		for ( int i = 0; i < cell.Loose.Count && ( _wakeBudget > 0 || _propSpawnBudget > 0 ); i++ )
		{
			int index = cell.Loose[ i ];
			LooseRecord record = _loose[ index ];
			if ( record.Definition == null || record.Resident != null )
				continue;
			if ( !IsLooseInRange( record.Definition.category, PlanarDistanceSqr( focus, record.Position ), currentlyResident: false ) )
				continue;

			WakeLoose( index );
		}
	}

	void TickEvictResidents( Vector3 focus )
	{
		for ( int i = _residentCoin.Count - 1; i >= 0 && _evictBudget > 0; i-- )
		{
			int index = _residentCoin[ i ];
			if ( index < 0 || index >= _coinStacks.Count )
			{
				_residentCoin.RemoveAt( i );
				continue;
			}

			CoinStackRecord record = _coinStacks[ index ];
			GroundCoinStack stack = record.Resident;
			if ( stack == null )
			{
				_residentCoin.RemoveAt( i );
				continue;
			}

			if ( !stack.CanWorldStream )
				continue;
			if ( IsStackInRange( PlanarDistanceSqr( focus, stack.ContactPosition ), currentlyResident: true, stack.Count ) )
			{
				if ( record.PendingExtract )
				{
					record.PendingExtract = false;
					_coinStacks[ index ] = record;
					stack.SetStreamingHidden( false );
				}

				continue;
			}

			stack.SetStreamingHidden( true );
			record.PendingExtract = true;
			record.Position = stack.ContactPosition;
			record.Rotation = stack.transform.rotation;
			_coinStacks[ index ] = record;
			_evictBudget--;
			_evictsThisTick++;
		}

		for ( int i = _residentBar.Count - 1; i >= 0 && _evictBudget > 0; i-- )
		{
			int index = _residentBar[ i ];
			if ( index < 0 || index >= _barStacks.Count )
			{
				_residentBar.RemoveAt( i );
				continue;
			}

			BarStackRecord record = _barStacks[ index ];
			GroundGoldBarStack stack = record.Resident;
			if ( stack == null )
			{
				_residentBar.RemoveAt( i );
				continue;
			}

			if ( !stack.CanWorldStream )
				continue;
			if ( IsStackInRange( PlanarDistanceSqr( focus, stack.ContactPosition ), currentlyResident: true ) )
			{
				if ( record.PendingExtract )
				{
					record.PendingExtract = false;
					_barStacks[ index ] = record;
					stack.SetStreamingHidden( false );
				}

				continue;
			}

			stack.SetStreamingHidden( true );
			record.PendingExtract = true;
			record.Position = stack.ContactPosition;
			record.Rotation = stack.transform.rotation;
			_barStacks[ index ] = record;
			_evictBudget--;
			_evictsThisTick++;
		}

		for ( int i = _residentLoose.Count - 1; i >= 0 && _evictBudget > 0; i-- )
		{
			int index = _residentLoose[ i ];
			if ( index < 0 || index >= _loose.Count )
			{
				_residentLoose.RemoveAt( i );
				continue;
			}

			LooseRecord record = _loose[ index ];
			TreasureItem item = record.Resident;
			if ( item == null )
			{
				_residentLoose.RemoveAt( i );
				continue;
			}

			if ( !CanParkLoose( item ) )
				continue;
			if ( IsLooseInRange( item.Definition.category, PlanarDistanceSqr( focus, item.transform.position ), currentlyResident: true ) )
			{
				if ( record.PendingExtract )
				{
					record.PendingExtract = false;
					item.SetStreamingHidden( false );
					_loose[ index ] = record;
				}

				continue;
			}

			item.SetStreamingHidden( true );
			record.PendingExtract = true;
			record.Position = item.transform.position;
			record.Rotation = item.transform.rotation;
			_loose[ index ] = record;
			_evictBudget--;
			_evictsThisTick++;
		}
	}

	void TickExtract()
	{
		for ( int i = 0; i < _coinStacks.Count && _extractBudget > 0; i++ )
		{
			CoinStackRecord record = _coinStacks[ i ];
			if ( !record.PendingExtract || record.Resident == null )
				continue;
			if ( !record.Resident.CanWorldStream )
			{
				record.PendingExtract = false;
				record.Resident.SetStreamingHidden( false );
				_coinStacks[ i ] = record;
				continue;
			}

			ExtractCoinStack( i );
		}

		for ( int i = 0; i < _barStacks.Count && _extractBudget > 0; i++ )
		{
			BarStackRecord record = _barStacks[ i ];
			if ( !record.PendingExtract || record.Resident == null )
				continue;
			if ( !record.Resident.CanWorldStream )
			{
				record.PendingExtract = false;
				record.Resident.SetStreamingHidden( false );
				_barStacks[ i ] = record;
				continue;
			}

			ExtractBarStack( i );
		}

		for ( int i = 0; i < _loose.Count && _extractBudget > 0; i++ )
		{
			LooseRecord record = _loose[ i ];
			if ( !record.PendingExtract || record.Resident == null )
				continue;
			if ( !CanParkLoose( record.Resident ) )
			{
				record.PendingExtract = false;
				record.Resident.SetStreamingHidden( false );
				_loose[ i ] = record;
				continue;
			}

			ExtractLoose( i );
		}
	}

	void TickParkLive( Vector3 focus )
	{
		for ( int i = _trackedLive.Count - 1; i >= 0 && _evictBudget > 0; i-- )
		{
			TreasureItem item = _trackedLive[ i ];
			if ( item == null )
			{
				_trackedLive.RemoveAt( i );
				continue;
			}

			if ( !_trackedSet.Contains( item ) )
			{
				_trackedLive.RemoveAt( i );
				continue;
			}

			if ( !CanParkLoose( item ) )
				continue;
			if ( IndexOfLooseResident( item ) >= 0 )
				continue;
			if ( IsLooseInRange( item.Definition.category, PlanarDistanceSqr( focus, item.transform.position ), currentlyResident: true ) )
				continue;

			item.SetStreamingHidden( true );
			int index = AddLooseResident( item );
			LooseRecord record = _loose[ index ];
			record.PendingExtract = true;
			_loose[ index ] = record;
			_evictBudget--;
			_evictsThisTick++;
		}
	}

	GroundCoinStack RentCoinStack( Vector3 position, Quaternion rotation )
	{
		EnsurePoolRoot();
		GroundCoinStack stack = null;
		if ( _coinPool.Count > 0 )
		{
			int last = _coinPool.Count - 1;
			stack = _coinPool[ last ];
			_coinPool.RemoveAt( last );
		}
		else if ( _createdCoinHosts < ResolveStackPoolCap() )
		{
			stack = GroundCoinStack.CreateUnpooled( position, rotation );
			_createdCoinHosts++;
			stack.ActivateFromPool( position, rotation );
			return stack;
		}
		else
		{
			stack = StealFarthestCoinHost( position );
		}

		if ( stack == null )
		{
			stack = GroundCoinStack.CreateUnpooled( position, rotation );
			_createdCoinHosts++;
		}

		stack.ActivateFromPool( position, rotation );
		return stack;
	}

	GroundGoldBarStack RentBarStack( Vector3 position, Quaternion rotation )
	{
		EnsurePoolRoot();
		GroundGoldBarStack stack = null;
		if ( _barPool.Count > 0 )
		{
			int last = _barPool.Count - 1;
			stack = _barPool[ last ];
			_barPool.RemoveAt( last );
		}
		else if ( _createdBarHosts < Mathf.Max( 4, ResolveStackPoolCap() / 4 ) )
		{
			stack = GroundGoldBarStack.CreateUnpooled( position, rotation );
			_createdBarHosts++;
			stack.ActivateFromPool( position, rotation );
			return stack;
		}
		else
		{
			stack = StealFarthestBarHost( position );
		}

		if ( stack == null )
		{
			stack = GroundGoldBarStack.CreateUnpooled( position, rotation );
			_createdBarHosts++;
		}

		stack.ActivateFromPool( position, rotation );
		return stack;
	}

	GroundCoinStack StealFarthestCoinHost( Vector3 keepNear )
	{
		int bestResident = -1;
		float bestSq = -1f;
		for ( int i = 0; i < _residentCoin.Count; i++ )
		{
			int index = _residentCoin[ i ];
			CoinStackRecord record = _coinStacks[ index ];
			GroundCoinStack stack = record.Resident;
			if ( stack == null || !stack.CanWorldStream )
				continue;

			float sq = PlanarDistanceSqr( keepNear, stack.ContactPosition );
			if ( sq <= bestSq )
				continue;

			bestSq = sq;
			bestResident = index;
		}

		if ( bestResident < 0 )
			return null;

		ExtractCoinStack( bestResident );
		if ( _coinPool.Count <= 0 )
			return null;

		int last = _coinPool.Count - 1;
		GroundCoinStack stolen = _coinPool[ last ];
		_coinPool.RemoveAt( last );
		return stolen;
	}

	GroundGoldBarStack StealFarthestBarHost( Vector3 keepNear )
	{
		int bestResident = -1;
		float bestSq = -1f;
		for ( int i = 0; i < _residentBar.Count; i++ )
		{
			int index = _residentBar[ i ];
			BarStackRecord record = _barStacks[ index ];
			GroundGoldBarStack stack = record.Resident;
			if ( stack == null || !stack.CanWorldStream )
				continue;

			float sq = PlanarDistanceSqr( keepNear, stack.ContactPosition );
			if ( sq <= bestSq )
				continue;

			bestSq = sq;
			bestResident = index;
		}

		if ( bestResident < 0 )
			return null;

		ExtractBarStack( bestResident );
		if ( _barPool.Count <= 0 )
			return null;

		int last = _barPool.Count - 1;
		GroundGoldBarStack stolen = _barPool[ last ];
		_barPool.RemoveAt( last );
		return stolen;
	}

	void TrackCoinStack( GroundCoinStack stack )
	{
		int existing = IndexOfCoinResident( stack );
		if ( existing >= 0 )
		{
			CoinStackRecord record = _coinStacks[ existing ];
			MoveCoinRecord( existing, stack.ContactPosition );
			record = _coinStacks[ existing ];
			record.Resident = stack;
			record.PendingExtract = false;
			record.Rotation = stack.transform.rotation;
			record.VariationSeed = stack.VariationSeed;
			_coinStacks[ existing ] = record;
			return;
		}

		int index = AllocCoinRecord();
		CoinStackRecord created = _coinStacks[ index ];
		created.Position = stack.ContactPosition;
		created.Rotation = stack.transform.rotation;
		created.VariationSeed = stack.VariationSeed;
		created.Resident = stack;
		created.PendingExtract = false;
		if ( created.Slots == null )
			created.Slots = new List<TreasureDefinition>( 16 );
		else
			created.Slots.Clear();
		created.Cell = default;
		_coinStacks[ index ] = created;
		InsertCoinCell( index, stack.ContactPosition );
		if ( !_residentCoin.Contains( index ) )
			_residentCoin.Add( index );
	}

	void TrackBarStack( GroundGoldBarStack stack )
	{
		int existing = IndexOfBarResident( stack );
		if ( existing >= 0 )
		{
			MoveBarRecord( existing, stack.ContactPosition );
			BarStackRecord record = _barStacks[ existing ];
			record.Resident = stack;
			record.PendingExtract = false;
			record.Rotation = stack.transform.rotation;
			record.Definition = stack.StackDefinition;
			record.Count = stack.Count;
			_barStacks[ existing ] = record;
			return;
		}

		int index = AllocBarRecord();
		BarStackRecord created = _barStacks[ index ];
		created.Position = stack.ContactPosition;
		created.Rotation = stack.transform.rotation;
		created.Definition = stack.StackDefinition;
		created.Count = stack.Count;
		created.Resident = stack;
		created.PendingExtract = false;
		created.Cell = default;
		_barStacks[ index ] = created;
		InsertBarCell( index, stack.ContactPosition );
		if ( !_residentBar.Contains( index ) )
			_residentBar.Add( index );
	}

	void AddHiddenCoinStack(
		Vector3 position,
		Quaternion rotation,
		IReadOnlyList<TreasureDefinition> slots,
		float variationSeed )
	{
		int index = AllocCoinRecord();
		CoinStackRecord record = _coinStacks[ index ];
		record.Position = position;
		record.Rotation = rotation;
		record.VariationSeed = variationSeed;
		record.Resident = null;
		record.PendingExtract = false;
		if ( record.Slots == null )
			record.Slots = new List<TreasureDefinition>( slots.Count );
		else
			record.Slots.Clear();
		for ( int i = 0; i < slots.Count; i++ )
			record.Slots.Add( slots[ i ] );
		record.Cell = default;
		_coinStacks[ index ] = record;
		InsertCoinCell( index, position );
	}

	GroundCoinStack WakeCoinStack( int index, bool force )
	{
		if ( index < 0 || index >= _coinStacks.Count )
			return null;

		CoinStackRecord record = _coinStacks[ index ];
		if ( record.Resident != null )
		{
			if ( record.PendingExtract )
			{
				record.PendingExtract = false;
				record.Resident.SetStreamingHidden( false );
				_coinStacks[ index ] = record;
			}

			return record.Resident;
		}

		if ( record.Slots == null || record.Slots.Count <= 0 )
			return null;
		if ( !force && _wakeBudget <= 0 )
			return null;

		GroundCoinStack stack = RentCoinStack( record.Position, record.Rotation );
		AttachCoinResident( index, stack );
		stack.RestoreStreamingSlots( record.Slots, record.VariationSeed );
		if ( !force )
			_wakeBudget--;
		_wakesThisTick++;
		return stack;
	}

	GroundGoldBarStack WakeBarStack( int index, bool force )
	{
		if ( index < 0 || index >= _barStacks.Count )
			return null;

		BarStackRecord record = _barStacks[ index ];
		if ( record.Resident != null )
		{
			if ( record.PendingExtract )
			{
				record.PendingExtract = false;
				record.Resident.SetStreamingHidden( false );
				_barStacks[ index ] = record;
			}

			return record.Resident;
		}

		if ( record.Definition == null || record.Count <= 0 )
			return null;
		if ( !force && _wakeBudget <= 0 )
			return null;

		GroundGoldBarStack stack = RentBarStack( record.Position, record.Rotation );
		AttachBarResident( index, stack );
		stack.RestoreStreamingBars( record.Definition, record.Count );
		if ( !force )
			_wakeBudget--;
		_wakesThisTick++;
		return stack;
	}

	void WakeLoose( int index )
	{
		if ( index < 0 || index >= _loose.Count )
			return;

		LooseRecord record = _loose[ index ];
		if ( record.Resident != null || record.Definition == null )
			return;

		bool isCoin = record.Definition.category == TreasureCategory.Coin;
		if ( isCoin )
		{
			if ( _wakeBudget <= 0 )
				return;
		}
		else if ( _propSpawnBudget <= 0 )
			return;

		TreasureItem item;
		if ( isCoin )
		{
			item = TreasureItemFactory.RentVisualCoin( record.Definition, record.Position, record.Rotation );
			if ( item == null )
				item = TreasureItemFactory.SpawnFallback( record.Definition, record.Position, record.Rotation, null );
		}
		else
			item = TreasureItemFactory.SpawnSync( record.Definition, record.Position, record.Rotation, null );

		if ( item == null )
			return;

		if ( record.OriginPile != null )
			item.SetOriginPile( record.OriginPile );

		if ( record.SurfaceMode )
			item.EnterSurface( record.Position, record.Rotation, Vector3.zero );
		else
			item.EnterSettledPhysics( record.Position, record.Rotation );

		item.SetStreamingHidden( false );
		record.Resident = item;
		record.PendingExtract = false;
		_loose[ index ] = record;
		if ( !_residentLoose.Contains( index ) )
			_residentLoose.Add( index );
		NotifyLoose( item );
		if ( isCoin )
			_wakeBudget--;
		else
		{
			_propSpawnBudget--;
			LooseTreasureManager.NotifyParkedChanged();
		}

		_wakesThisTick++;
	}

	void ExtractCoinStack( int index )
	{
		CoinStackRecord record = _coinStacks[ index ];
		GroundCoinStack stack = record.Resident;
		if ( stack == null )
		{
			record.PendingExtract = false;
			_coinStacks[ index ] = record;
			return;
		}

		_slotScratch.Clear();
		stack.CaptureStreamingSlots( _slotScratch, out float seed, out Vector3 pos, out Quaternion rot );
		if ( _slotScratch.Count <= 0 )
		{
			record.Resident = null;
			record.PendingExtract = false;
			_coinStacks[ index ] = record;
			_residentCoin.Remove( index );
			ReturnCoinHost( stack );
			FreeCoinRecord( index );
			_extractBudget--;
			_extractsThisTick++;
			return;
		}

		if ( record.Slots == null )
			record.Slots = new List<TreasureDefinition>( _slotScratch.Count );
		else
			record.Slots.Clear();
		for ( int i = 0; i < _slotScratch.Count; i++ )
			record.Slots.Add( _slotScratch[ i ] );

		record.Position = pos;
		record.Rotation = rot;
		record.VariationSeed = seed;
		record.Resident = null;
		record.PendingExtract = false;
		_coinStacks[ index ] = record;
		_residentCoin.Remove( index );
		MoveCoinRecord( index, pos );
		ReturnCoinHost( stack );
		_extractBudget--;
		_extractsThisTick++;
	}

	void ExtractBarStack( int index )
	{
		BarStackRecord record = _barStacks[ index ];
		GroundGoldBarStack stack = record.Resident;
		if ( stack == null )
		{
			record.PendingExtract = false;
			_barStacks[ index ] = record;
			return;
		}

		stack.CaptureStreamingBars( out TreasureDefinition definition, out int count, out Vector3 pos, out Quaternion rot );
		record.Definition = definition;
		record.Count = count;
		record.Position = pos;
		record.Rotation = rot;
		record.Resident = null;
		record.PendingExtract = false;
		_barStacks[ index ] = record;
		_residentBar.Remove( index );
		MoveBarRecord( index, pos );
		ReturnBarHost( stack );
		_extractBudget--;
		_extractsThisTick++;
	}

	void ExtractLoose( int index )
	{
		LooseRecord record = _loose[ index ];
		TreasureItem item = record.Resident;
		if ( item == null || item.Definition == null )
		{
			record.PendingExtract = false;
			_loose[ index ] = record;
			return;
		}

		record.Definition = item.Definition;
		record.Position = item.transform.position;
		record.Rotation = item.transform.rotation;
		record.OriginPile = item.OriginPile;
		record.SurfaceMode = item.State == TreasureItemState.SurfaceRolling;
		record.Resident = null;
		record.PendingExtract = false;
		_loose[ index ] = record;
		_residentLoose.Remove( index );
		_trackedSet.Remove( item );
		_trackedLive.Remove( item );
		LooseTreasureManager.Unregister( item );
		TreasureProximitySleep.Unregister( item );
		MoveLooseRecord( index, record.Position );
		TreasureItemFactory.Despawn( item );
		if ( record.Definition.category != TreasureCategory.Coin )
			LooseTreasureManager.NotifyParkedChanged();
		_extractBudget--;
		_extractsThisTick++;
	}

	void ReturnCoinHost( GroundCoinStack stack )
	{
		if ( stack == null )
			return;

		EnsurePoolRoot();
		stack.PrepareForPool();
		stack.transform.SetParent( _poolRoot, false );
		stack.gameObject.SetActive( false );
		if ( !_coinPool.Contains( stack ) )
			_coinPool.Add( stack );
	}

	void ReturnBarHost( GroundGoldBarStack stack )
	{
		if ( stack == null )
			return;

		EnsurePoolRoot();
		stack.PrepareForPool();
		stack.transform.SetParent( _poolRoot, false );
		stack.gameObject.SetActive( false );
		if ( !_barPool.Contains( stack ) )
			_barPool.Add( stack );
	}

	int AddLooseResident( TreasureItem item )
	{
		int existing = IndexOfLooseResident( item );
		if ( existing >= 0 )
			return existing;

		int index = AllocLooseRecord();
		LooseRecord record = _loose[ index ];
		record.Definition = item.Definition;
		record.Position = item.transform.position;
		record.Rotation = item.transform.rotation;
		record.OriginPile = item.OriginPile;
		record.SurfaceMode = item.State == TreasureItemState.SurfaceRolling;
		record.Resident = item;
		record.PendingExtract = false;
		record.Cell = default;
		_loose[ index ] = record;
		InsertLooseCell( index, record.Position );
		if ( !_residentLoose.Contains( index ) )
			_residentLoose.Add( index );
		return index;
	}

	void DetachLooseResident( TreasureItem item )
	{
		int index = IndexOfLooseResident( item );
		if ( index < 0 )
			return;

		// Pickup / auto-stack / reclaim: drop the park record so the streamer
		// cannot wake a duplicate at the old pose on the next tick.
		FreeLooseRecord( index );
	}

	void ForgetCoinStack( GroundCoinStack stack )
	{
		int index = IndexOfCoinResident( stack );
		if ( index < 0 )
			return;

		CoinStackRecord record = _coinStacks[ index ];
		record.Resident = null;
		record.PendingExtract = false;
		_coinStacks[ index ] = record;
		_residentCoin.Remove( index );
		FreeCoinRecord( index );
	}

	void ClearCoinResident( GroundCoinStack stack )
	{
		int index = IndexOfCoinResident( stack );
		if ( index < 0 )
			return;

		CoinStackRecord record = _coinStacks[ index ];
		if ( record.Resident != stack )
			return;

		if ( stack.Count <= 0 )
		{
			record.Resident = null;
			_coinStacks[ index ] = record;
			_residentCoin.Remove( index );
			FreeCoinRecord( index );
			return;
		}

		if ( stack.IsReturningToPool || record.PendingExtract )
			return;

		record.Resident = null;
		_coinStacks[ index ] = record;
		_residentCoin.Remove( index );
	}

	void ClearBarResident( GroundGoldBarStack stack )
	{
		int index = IndexOfBarResident( stack );
		if ( index < 0 )
			return;

		BarStackRecord record = _barStacks[ index ];
		if ( record.Resident != stack )
			return;

		if ( stack.IsReturningToPool || record.PendingExtract )
			return;

		if ( stack.Count <= 0 )
		{
			record.Resident = null;
			_barStacks[ index ] = record;
			_residentBar.Remove( index );
			FreeBarRecord( index );
			return;
		}

		record.Resident = null;
		_barStacks[ index ] = record;
		_residentBar.Remove( index );
	}

	int FindHiddenCoinStack( Vector3 worldPos, float radius, float liveBestSq )
	{
		float bestSq = Mathf.Min( liveBestSq, radius * radius );
		int best = -1;
		CollectCellsInRadius( worldPos, radius );
		for ( int c = 0; c < _cellScratch.Count; c++ )
		{
			GridCell cell = _cellScratch[ c ];
			for ( int i = 0; i < cell.CoinStacks.Count; i++ )
			{
				int index = cell.CoinStacks[ i ];
				CoinStackRecord record = _coinStacks[ index ];
				if ( record.Slots == null || record.Slots.Count <= 0 )
					continue;
				if ( record.Slots.Count >= GroundCoinStack.DefaultMaxHeight )
					continue;
				if ( record.Resident != null && !record.PendingExtract )
					continue;

				float sq = PlanarDistanceSqr( worldPos, record.Position );
				if ( sq >= bestSq )
					continue;

				bestSq = sq;
				best = index;
			}
		}

		return best;
	}

	int FindHiddenCoinStackAlongRay( Ray ray, float radius, float maxDistance, float liveBestPerpSq, out float bestPerpSq )
	{
		bestPerpSq = liveBestPerpSq;
		int best = -1;
		Vector3 origin = ray.origin;
		Vector3 dir = ray.direction;
		if ( dir.sqrMagnitude < 0.0001f )
			return -1;
		dir.Normalize();
		float maxDist = Mathf.Max( 0.01f, maxDistance );
		Vector3 mid = origin + dir * ( maxDist * 0.5f );
		CollectCellsInRadius( mid, maxDist * 0.5f + radius );
		for ( int c = 0; c < _cellScratch.Count; c++ )
		{
			GridCell cell = _cellScratch[ c ];
			for ( int i = 0; i < cell.CoinStacks.Count; i++ )
			{
				int index = cell.CoinStacks[ i ];
				CoinStackRecord record = _coinStacks[ index ];
				if ( record.Slots == null || record.Slots.Count <= 0 )
					continue;
				if ( record.Slots.Count >= GroundCoinStack.DefaultMaxHeight )
					continue;
				if ( record.Resident != null && !record.PendingExtract )
					continue;

				Vector3 contact = record.Position;
				Vector3 to = contact - origin;
				float along = Vector3.Dot( to, dir );
				if ( along < -radius || along > maxDist + radius )
					continue;

				Vector3 closest = origin + dir * Mathf.Clamp( along, 0f, maxDist );
				Vector3 delta = contact - closest;
				float xzSq = delta.x * delta.x + delta.z * delta.z;
				if ( xzSq >= bestPerpSq )
					continue;

				bestPerpSq = xzSq;
				best = index;
			}
		}

		return best;
	}

	int FindHiddenBarStack( Vector3 worldPos, float radius, float liveBestSq, TreasureDefinition required )
	{
		float bestSq = Mathf.Min( liveBestSq, radius * radius );
		int best = -1;
		CollectCellsInRadius( worldPos, radius );
		for ( int c = 0; c < _cellScratch.Count; c++ )
		{
			GridCell cell = _cellScratch[ c ];
			for ( int i = 0; i < cell.BarStacks.Count; i++ )
			{
				int index = cell.BarStacks[ i ];
				BarStackRecord record = _barStacks[ index ];
				if ( record.Definition == null || record.Count <= 0 )
					continue;
				if ( record.Resident != null && !record.PendingExtract )
					continue;
				if ( required != null && !GoldBarStack.AreSameType( record.Definition, required ) )
					continue;

				float sq = PlanarDistanceSqr( worldPos, record.Position );
				if ( sq >= bestSq )
					continue;

				bestSq = sq;
				best = index;
			}
		}

		return best;
	}

	int FindHiddenBarStackAlongRay(
		Ray ray,
		float radius,
		float maxDistance,
		float liveBestPerpSq,
		TreasureDefinition required,
		out float bestPerpSq )
	{
		bestPerpSq = liveBestPerpSq;
		int best = -1;
		Vector3 origin = ray.origin;
		Vector3 dir = ray.direction;
		if ( dir.sqrMagnitude < 0.0001f )
			return -1;
		dir.Normalize();
		float maxDist = Mathf.Max( 0.01f, maxDistance );
		Vector3 mid = origin + dir * ( maxDist * 0.5f );
		CollectCellsInRadius( mid, maxDist * 0.5f + radius );
		for ( int c = 0; c < _cellScratch.Count; c++ )
		{
			GridCell cell = _cellScratch[ c ];
			for ( int i = 0; i < cell.BarStacks.Count; i++ )
			{
				int index = cell.BarStacks[ i ];
				BarStackRecord record = _barStacks[ index ];
				if ( record.Definition == null || record.Count <= 0 )
					continue;
				if ( record.Resident != null && !record.PendingExtract )
					continue;
				if ( required != null && !GoldBarStack.AreSameType( record.Definition, required ) )
					continue;

				Vector3 contact = record.Position;
				Vector3 to = contact - origin;
				float along = Vector3.Dot( to, dir );
				if ( along < -radius || along > maxDist + radius )
					continue;

				Vector3 closest = origin + dir * Mathf.Clamp( along, 0f, maxDist );
				Vector3 delta = contact - closest;
				float xzSq = delta.x * delta.x + delta.z * delta.z;
				if ( xzSq >= bestPerpSq )
					continue;

				bestPerpSq = xzSq;
				best = index;
			}
		}

		return best;
	}

	void CollectCellsInRadius( Vector3 worldPos, float radius )
	{
		_cellScratch.Clear();
		float chunk = ResolveChunkSize();
		int span = Mathf.Max( 1, Mathf.CeilToInt( radius / chunk ) );
		int cx = Mathf.FloorToInt( worldPos.x / chunk );
		int cz = Mathf.FloorToInt( worldPos.z / chunk );
		for ( int z = cz - span; z <= cz + span; z++ )
		{
			for ( int x = cx - span; x <= cx + span; x++ )
			{
				if ( !_cells.TryGetValue( new CellCoord( x, z ), out GridCell cell ) )
					continue;
				_cellScratch.Add( cell );
			}
		}
	}

	void AttachCoinResident( int index, GroundCoinStack stack )
	{
		int tracked = IndexOfCoinResident( stack );
		if ( tracked >= 0 && tracked != index )
		{
			CoinStackRecord dup = _coinStacks[ tracked ];
			dup.Resident = null;
			dup.PendingExtract = false;
			_coinStacks[ tracked ] = dup;
			_residentCoin.Remove( tracked );
			FreeCoinRecord( tracked );
		}

		CoinStackRecord record = _coinStacks[ index ];
		record.Resident = stack;
		record.PendingExtract = false;
		_coinStacks[ index ] = record;
		if ( !_residentCoin.Contains( index ) )
			_residentCoin.Add( index );
	}

	void AttachBarResident( int index, GroundGoldBarStack stack )
	{
		int tracked = IndexOfBarResident( stack );
		if ( tracked >= 0 && tracked != index )
		{
			BarStackRecord dup = _barStacks[ tracked ];
			dup.Resident = null;
			dup.PendingExtract = false;
			_barStacks[ tracked ] = dup;
			_residentBar.Remove( tracked );
			FreeBarRecord( tracked );
		}

		BarStackRecord record = _barStacks[ index ];
		record.Resident = stack;
		record.PendingExtract = false;
		_barStacks[ index ] = record;
		if ( !_residentBar.Contains( index ) )
			_residentBar.Add( index );
	}

	int AllocCoinRecord()
	{
		if ( _freeCoinRecords.Count > 0 )
		{
			int last = _freeCoinRecords.Count - 1;
			int index = _freeCoinRecords[ last ];
			_freeCoinRecords.RemoveAt( last );
			return index;
		}

		_coinStacks.Add( new CoinStackRecord { Slots = new List<TreasureDefinition>( 16 ) } );
		return _coinStacks.Count - 1;
	}

	int AllocBarRecord()
	{
		if ( _freeBarRecords.Count > 0 )
		{
			int last = _freeBarRecords.Count - 1;
			int index = _freeBarRecords[ last ];
			_freeBarRecords.RemoveAt( last );
			return index;
		}

		_barStacks.Add( default );
		return _barStacks.Count - 1;
	}

	int AllocLooseRecord()
	{
		if ( _freeLooseRecords.Count > 0 )
		{
			int last = _freeLooseRecords.Count - 1;
			int index = _freeLooseRecords[ last ];
			_freeLooseRecords.RemoveAt( last );
			return index;
		}

		_loose.Add( default );
		return _loose.Count - 1;
	}

	void FreeCoinRecord( int index )
	{
		RemoveCoinCell( index );
		CoinStackRecord record = _coinStacks[ index ];
		if ( record.Slots != null )
			record.Slots.Clear();
		record.Resident = null;
		record.PendingExtract = false;
		_coinStacks[ index ] = record;
		_residentCoin.Remove( index );
		_freeCoinRecords.Add( index );
	}

	void FreeBarRecord( int index )
	{
		RemoveBarCell( index );
		BarStackRecord record = _barStacks[ index ];
		record.Definition = null;
		record.Count = 0;
		record.Resident = null;
		record.PendingExtract = false;
		_barStacks[ index ] = record;
		_residentBar.Remove( index );
		_freeBarRecords.Add( index );
	}

	void FreeLooseRecord( int index )
	{
		RemoveLooseCell( index );
		_loose[ index ] = default;
		_residentLoose.Remove( index );
		_freeLooseRecords.Add( index );
	}

	void InsertCoinCell( int index, Vector3 position )
	{
		CellCoord coord = CoordFromPosition( position );
		CoinStackRecord record = _coinStacks[ index ];
		record.Cell = coord;
		_coinStacks[ index ] = record;
		GetOrCreateCell( coord ).CoinStacks.Add( index );
	}

	void InsertBarCell( int index, Vector3 position )
	{
		CellCoord coord = CoordFromPosition( position );
		BarStackRecord record = _barStacks[ index ];
		record.Cell = coord;
		_barStacks[ index ] = record;
		GetOrCreateCell( coord ).BarStacks.Add( index );
	}

	void InsertLooseCell( int index, Vector3 position )
	{
		CellCoord coord = CoordFromPosition( position );
		LooseRecord record = _loose[ index ];
		record.Cell = coord;
		_loose[ index ] = record;
		GetOrCreateCell( coord ).Loose.Add( index );
	}

	void RemoveCoinCell( int index )
	{
		CoinStackRecord record = _coinStacks[ index ];
		if ( !_cells.TryGetValue( record.Cell, out GridCell cell ) )
			return;
		cell.CoinStacks.Remove( index );
	}

	void RemoveBarCell( int index )
	{
		BarStackRecord record = _barStacks[ index ];
		if ( !_cells.TryGetValue( record.Cell, out GridCell cell ) )
			return;
		cell.BarStacks.Remove( index );
	}

	void RemoveLooseCell( int index )
	{
		LooseRecord record = _loose[ index ];
		if ( !_cells.TryGetValue( record.Cell, out GridCell cell ) )
			return;
		cell.Loose.Remove( index );
	}

	void MoveCoinRecord( int index, Vector3 position )
	{
		RemoveCoinCell( index );
		InsertCoinCell( index, position );
		CoinStackRecord record = _coinStacks[ index ];
		record.Position = position;
		_coinStacks[ index ] = record;
	}

	void MoveBarRecord( int index, Vector3 position )
	{
		RemoveBarCell( index );
		InsertBarCell( index, position );
		BarStackRecord record = _barStacks[ index ];
		record.Position = position;
		_barStacks[ index ] = record;
	}

	void MoveLooseRecord( int index, Vector3 position )
	{
		RemoveLooseCell( index );
		InsertLooseCell( index, position );
		LooseRecord record = _loose[ index ];
		record.Position = position;
		_loose[ index ] = record;
	}

	GridCell GetOrCreateCell( CellCoord coord )
	{
		if ( _cells.TryGetValue( coord, out GridCell cell ) )
			return cell;

		cell = new GridCell();
		_cells[ coord ] = cell;
		return cell;
	}

	CellCoord CoordFromPosition( Vector3 position )
	{
		float chunk = ResolveChunkSize();
		return new CellCoord( Mathf.FloorToInt( position.x / chunk ), Mathf.FloorToInt( position.z / chunk ) );
	}

	int IndexOfCoinResident( GroundCoinStack stack )
	{
		for ( int i = 0; i < _coinStacks.Count; i++ )
		{
			if ( _coinStacks[ i ].Resident == stack )
				return i;
		}

		return -1;
	}

	int IndexOfBarResident( GroundGoldBarStack stack )
	{
		for ( int i = 0; i < _barStacks.Count; i++ )
		{
			if ( _barStacks[ i ].Resident == stack )
				return i;
		}

		return -1;
	}

	int IndexOfLooseResident( TreasureItem item )
	{
		for ( int i = 0; i < _loose.Count; i++ )
		{
			if ( _loose[ i ].Resident == item )
				return i;
		}

		return -1;
	}

	bool CanParkLoose( TreasureItem item )
	{
		if ( item == null || item.Definition == null )
			return false;
		if ( item.Owner != null )
			return false;
		if ( item.IsReclaiming || item.IsInFlight )
			return false;
		if ( !item.IsWorldLoose )
			return false;
		if ( item.Owner is ITreasureDisplayStackOwner )
			return false;
		if ( GemPyramidRegistry.FindClusterContaining( item ) != null )
			return false;
		if ( !item.IsSettledForWorldStream )
			return false;

		TreasureCategory category = item.Definition.category;
		if ( category == TreasureCategory.Coin )
			return _settings == null || !_settings.coinNeverCull;
		if ( category == TreasureCategory.Gem )
			return _settings == null || !_settings.gemNeverCull;
		if ( GoldPileLootStreamSettings.IsArtifactStreamBucket( category ) )
			return _settings == null || !_settings.artifactNeverCull;
		return false;
	}

	bool IsStackInRange( float distanceMetersSqr, bool currentlyResident, int coinCount = 0 )
	{
		if ( _settings == null )
			return true;
		return _settings.IsStackInStreamRange( distanceMetersSqr, currentlyResident, coinCount );
	}

	static int CoinCountOf( CoinStackRecord record )
	{
		if ( record.Resident != null )
			return record.Resident.Count;
		if ( record.Slots != null )
			return record.Slots.Count;
		return 0;
	}

	bool IsLooseInRange( TreasureCategory category, float distanceMetersSqr, bool currentlyResident )
	{
		if ( _settings == null )
			return true;
		return _settings.IsPropInStreamRange( category, distanceMetersSqr, currentlyResident );
	}

	float ResolveChunkSize()
	{
		if ( _settings != null && _settings.chunkSize > 0.1f )
			return _settings.chunkSize;
		return 8f;
	}

	float ResolveStackExitDistance()
	{
		float max = _settings != null ? Mathf.Max( 0.1f, _settings.stackResidentDistance ) : 20f;
		float pad = _settings != null ? Mathf.Max( 0f, _settings.propStreamHysteresisMeters ) : 2f;
		float coin = _settings != null ? Mathf.Max( 0.1f, _settings.coinResidentDistance ) : max;
		float large = _settings != null ? Mathf.Max( 0.1f, _settings.largeStackResidentDistance ) : max;
		return Mathf.Max( max, Mathf.Max( coin, large ) ) + pad;
	}

	int ResolveStackPoolCap()
	{
		return _settings != null ? Mathf.Max( 8, _settings.stackInstancePoolSize ) : 64;
	}

	void EnsureSettings()
	{
		if ( _settings != null )
			return;

		_settings = RuntimeDefinition.Resolve( ref _settings );
#if UNITY_EDITOR
		if ( _settings == null )
		{
			_settings = UnityEditor.AssetDatabase.LoadAssetAtPath<GoldPileLootStreamSettings>(
				GoldPileLootStreamSettings.AssetPath );
			if ( _settings == null )
			{
				_settings = UnityEditor.AssetDatabase.LoadAssetAtPath<GoldPileLootStreamSettings>(
					GoldPileLootStreamSettings.LegacyAssetPath );
			}
		}
#endif
		if ( _settings == null )
			_settings = ScriptableObject.CreateInstance<GoldPileLootStreamSettings>();
	}

	void EnsurePoolRoot()
	{
		if ( _poolRoot != null )
			return;

		GameObject root = new GameObject( "WorldTreasurePool" );
		Object.DontDestroyOnLoad( root );
		root.SetActive( false );
		_poolRoot = root.transform;
	}

	static bool TryGetFocus( out Vector3 position )
	{
		if ( TreasureProximitySleep.TryGetPlayerPosition( out position ) )
			return true;

		Camera cam = Camera.main;
		if ( cam == null )
		{
			position = default;
			return false;
		}

		position = cam.transform.position;
		return true;
	}

	static float PlanarDistanceSqr( Vector3 a, Vector3 b )
	{
		float dx = a.x - b.x;
		float dz = a.z - b.z;
		return dx * dx + dz * dz;
	}

	static int CountHiddenCoinStacks()
	{
		int n = 0;
		for ( int i = 0; i < _instance._coinStacks.Count; i++ )
		{
			CoinStackRecord record = _instance._coinStacks[ i ];
			if ( record.Slots != null && record.Slots.Count > 0 && record.Resident == null )
				n++;
		}

		return n;
	}

	static int CountHiddenBarStacks()
	{
		int n = 0;
		for ( int i = 0; i < _instance._barStacks.Count; i++ )
		{
			BarStackRecord record = _instance._barStacks[ i ];
			if ( record.Definition != null && record.Count > 0 && record.Resident == null )
				n++;
		}

		return n;
	}

	static int CountHiddenLoose()
	{
		int n = 0;
		for ( int i = 0; i < _instance._loose.Count; i++ )
		{
			LooseRecord record = _instance._loose[ i ];
			if ( record.Definition != null && record.Resident == null )
				n++;
		}

		return n;
	}

	void OnDestroy()
	{
		if ( _instance == this )
			_instance = null;
	}
}
