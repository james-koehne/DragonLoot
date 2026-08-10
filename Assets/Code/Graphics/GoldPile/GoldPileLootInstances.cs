using System.Collections.Generic;
using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Multi-type buried GPU-instanced loot covering the heightfield. Pickable by instance.
/// Draws through frustum/LOD chunk streaming over the Drawn visibility pool.
/// Coin seats are visual densify only (not 1:1 inventory); dig/pick economy uses _remaining.
/// </summary>
[DisallowMultipleComponent]
public class GoldPileLootInstances : MonoBehaviour, TreasureSparkleMaskRegistrar.IInstanceMaskSource
{
	const int BatchSize = 1023;

	struct Slot
	{
		public TreasureDefinition Definition;
		public int EntryIndex;
		public Vector3 LocalPos;
		public Quaternion LocalRot;
		public float Scale;
		public float EmbedDepth;
		public float PlaceSurfaceHeight;
		public float ProbeRadius;
		public bool FixedVolumePose;
		public bool CoinVisual;
		public bool Taken;
		public bool Drawn;
		public int BatchKey;
		public int CellX;
		public int CellZ;
		public int ChunkX;
		public int ChunkZ;
		public int MatrixIndex;
	}

	struct BatchGroup
	{
		public Mesh Mesh;
		public Material Material;
		public int SubmeshIndex;
		public Matrix4x4 PartLocal;
		public bool IsPrimaryPart;
		public bool AlwaysDraw;
		public bool SupportsInstancing;
		public List<int> SlotIndices;
		public Matrix4x4[] Matrices;
		public Matrix4x4[] DrawBatch;
		public Matrix4x4[] StreamMatrices;
		public int StreamCount;
		public Matrix4x4[][] ChunkStreamMatrices;
		public int[] ChunkStreamCounts;
	}

	[SerializeField]
	[Min( 1 )]
	int spatialCells = 16;

	[SerializeField]
	Mesh fallbackMesh;

	[SerializeField]
	Material fallbackMaterial;

	[SerializeField]
	[Tooltip( "Lean DragonLoot/Coin Pile material used for GPU-instanced mound draws." )]
	Material pileMaterial;

	[SerializeField]
	Material silverPileMaterial;

	[SerializeField]
	Material copperPileMaterial;

	[SerializeField]
	GoldPileLootStreamSettings streamSettings;

	[SerializeField]
	[Tooltip( "Logs slot/instance draw counts once after the initial BindAsync spawn." )]
	bool logInitialSpawnStats = true;

	public Mesh FallbackMesh => fallbackMesh;
	public Material FallbackMaterial => fallbackMaterial;
	public Material PileMaterial => pileMaterial;
	public GoldPileLootStreamSettings StreamSettings => streamSettings;
	public GoldPileChunkGrid ChunkGrid => _chunkGrid;
	public GoldPileChunkStreamer Streamer => _streamer;
	public int LastDrawnCount { get; private set; }
	public int LastCulledCount { get; private set; }
	public int DrawnPoolCount { get; private set; }
	public int CacheRebuildCount { get; private set; }
	public int LastDirtyChunkRebuildCount { get; private set; }
	public int SteadyVisibleBudget => _steadyVisibleBudget;
	public int MaxVisibleTotal => _maxVisibleTotal;
	public bool StreamingEnabled => _streamingEnabled;
	public bool IsReady => _ready;

	public void SetStreamingEnabled( bool enabled )
	{
		_streamingEnabled = enabled;
		_streamer.ForceRetick();
		InvalidateDrawCache();
	}

	public bool TryGetPrimaryDrawAssets( out Mesh mesh, out Material material )
	{
		mesh = null;
		material = null;
		if ( _batches == null )
		{
			mesh = fallbackMesh;
			material = fallbackMaterial;
			return mesh != null && material != null;
		}

		for ( int i = 0; i < _batches.Length; i++ )
		{
			BatchGroup group = _batches[ i ];
			if ( group.Mesh == null || group.Material == null )
				continue;
			mesh = group.Mesh;
			material = group.Material;
			return true;
		}

		mesh = fallbackMesh;
		material = fallbackMaterial;
		return mesh != null && material != null;
	}

	readonly List<AsyncOperationHandle<GameObject>> _visualHandles = new List<AsyncOperationHandle<GameObject>>();
	readonly Dictionary<string, VisualAssets> _visualsByKey = new Dictionary<string, VisualAssets>();
	readonly GoldPileChunkGrid _chunkGrid = new GoldPileChunkGrid();
	readonly GoldPileChunkStreamer _streamer = new GoldPileChunkStreamer();
	readonly HashSet<int> _pickPrioritySlots = new HashSet<int>();

	GoldPileHeightfield _heightfield;
	Transform _pileRoot;
	TreasurePileDefinition _definition;
	Slot[] _slots;
	BatchGroup[] _batches;
	List<int>[] _cellLists;
	List<int>[] _drawnCellLists;
	List<int>[] _chunkDrawnSlots;
	Dictionary<TreasureDefinition, int> _remaining;
	Dictionary<TreasureDefinition, int> _batchKeyByDef;
	int[] _visibleCountByEntry;
	List<int> _eligibleScratch;
	System.Comparison<int> _coverageComparison;
	bool[] _cellUsedScratch;
	bool _ready;
	int _coinUnitSerial;
	MaterialPropertyBlock _sparkleMaskPropertyBlock;
	bool _streamingEnabled = true;
	bool _drawCacheDirty = true;
	bool _drawCacheFullRebuild = true;
	float _cachedLod0Density = -1f;
	float _cachedLod1Density = -1f;
	float _cachedLod2Density = -1f;
	bool _cachedCategoryCull;
	float _densityLod0 = 1f;
	float _densityLod1 = 1f;
	float _densityLod2 = 1f;
	float _densityLod3 = 0f;
	float _pickRadius = 0.45f;
	float _buryDepth = 0.18f;
	float _initialRevealDepth = 0.06f;
	int _maxVisibleTotal = 400;
	int _steadyVisibleBudget = 320;
	float _placementMinSpacing = 0.35f;
	bool _enforcePlacementSpacing = true;
	float _placementJitter = 0.3f;
	float _placementRadiusFraction = 0.88f;
	float _placementScaleJitter = 0.1f;
	float _coinSurfaceHeightFraction = 0.12f;
	int _coinPullToCarveCount = 2;
	float _treasureRadialPower = 1.25f;
	float _treasureHeightBias = 0.75f;
	float _treasurePickupOutsideFraction = 0.4f;
	TreasurePileVisual _owner;
	readonly List<int> _pendingCoinWorldReleases = new List<int>( 32 );
	bool _coinReleasePassRunning;
	int _pileLootSeed = 1;
	Dictionary<TreasureDefinition, Bounds> _coinMeshBoundsByDef;
	Vector3 _lastDigLocal = Vector3.zero;
	bool _hasLastDig;
	int _bindSerial;
	Camera _cachedCamera;
	bool _pendingDensify;
	Vector3 _pendingDensifyWorld;
	float _pendingDensifyRadius;

	public int TotalRemaining
	{
		get
		{
			if ( _remaining == null )
				return 0;
			int n = 0;
			foreach ( KeyValuePair<TreasureDefinition, int> pair in _remaining )
				n += pair.Value;
			return n;
		}
	}

	/// <summary>Remaining coin-category inventory units (not GPU seat count).</summary>
	public int TotalRemainingCoins
	{
		get
		{
			if ( _remaining == null )
				return 0;
			int n = 0;
			foreach ( KeyValuePair<TreasureDefinition, int> pair in _remaining )
			{
				if ( pair.Key == null || pair.Key.category != TreasureCategory.Coin )
					continue;
				n += pair.Value;
			}
			return n;
		}
	}

	public async Task BindAsync(
		TreasurePileDefinition definition,
		GoldPileHeightfield heightfield,
		Transform pileRoot,
		int authoredLayoutSeed = 0 )
	{
		await BindAsync( null, definition, heightfield, pileRoot, authoredLayoutSeed );
	}

	public async Task BindAsync(
		TreasurePileVisual owner,
		TreasurePileDefinition definition,
		GoldPileHeightfield heightfield,
		Transform pileRoot,
		int authoredLayoutSeed = 0 )
	{
		int bindId = ++_bindSerial;
		_definition = definition;
		_heightfield = heightfield;
		_pileRoot = pileRoot != null ? pileRoot : transform;
		_owner = owner;
		_ready = false;
		_drawCacheDirty = true;
		_pendingCoinWorldReleases.Clear();
		_coinReleasePassRunning = false;
		ReleaseVisualHandles();
		_streamer.Clear();
		_chunkGrid.Release();

		if ( definition == null || heightfield == null )
			return;

		_pickRadius = definition.pickRadius;
		_buryDepth = definition.buryDepth;
		_initialRevealDepth = definition.initialRevealDepth;
		_maxVisibleTotal = Mathf.Max( 1, definition.maxVisibleTotal );
		_steadyVisibleBudget = definition.SteadyCoinVisibleBudget();
		_placementMinSpacing = Mathf.Max( 0.05f, definition.placementMinSpacing );
		_enforcePlacementSpacing = definition.enforcePlacementSpacing;
		_placementJitter = Mathf.Clamp( definition.placementJitter, 0f, 0.49f );
		_placementRadiusFraction = Mathf.Clamp( definition.placementRadiusFraction, 0.4f, 1f );
		_placementScaleJitter = Mathf.Clamp( definition.placementScaleJitter, 0f, 0.5f );
		_coinSurfaceHeightFraction = Mathf.Clamp( definition.coinSurfaceHeightFraction, 0f, 0.5f );
		_coinPullToCarveCount = Mathf.Max( 0, definition.coinPullToCarveCount );
		_treasureRadialPower = Mathf.Clamp( definition.treasureRadialPower, 0.25f, 3f );
		_treasureHeightBias = Mathf.Clamp( definition.treasureHeightBias, 0f, 3f );
		_treasurePickupOutsideFraction = Mathf.Clamp( definition.treasurePickupOutsideFraction, 0.05f, 0.95f );
		_pileLootSeed = WorldLootSeed.GetPileEffectiveSeed( _pileRoot, authoredLayoutSeed );
		_hasLastDig = false;
		_pendingDensify = false;

		EnsureStreamSettings();

		BuildRemaining( definition );
		await EnsureFallbackVisualsAsync( definition );
		if ( !IsBindTargetAlive( bindId ) )
			return;

		Dictionary<TreasureDefinition, VisualAssets> visuals = await ResolveVisualsAsync( definition );
		if ( !IsBindTargetAlive( bindId ) )
			return;

		int worldSeed = GoldPileChunkGrid.HashChunkSeed(
			_pileLootSeed,
			Mathf.RoundToInt( _pileRoot.position.x * 10f ),
			Mathf.RoundToInt( _pileRoot.position.z * 10f ) );
		_chunkGrid.Build( _heightfield, _pileRoot, streamSettings.chunkSize, worldSeed );
		_streamer.Bind( _chunkGrid, streamSettings );
		AllocateChunkDrawnLists();

		BuildSlots( definition, visuals );
		RebuildVisibility();
		if ( !IsBindTargetAlive( bindId ) )
			return;

		// Fill near-surface seats up to the steady draw target (not just one 48-seat pass).
		SeedSteadyNearSurfaceCoins();
		if ( !IsBindTargetAlive( bindId ) )
			return;

		_ready = _slots != null;

		if ( logInitialSpawnStats )
			LogInitialSpawnStats();
	}

	/// <summary>
	/// Promotes / spawns near-surface coin seats until the steady draw budget is filled.
	/// Bind-time only — digs use a throttled TopUp pass instead.
	/// </summary>
	void SeedSteadyNearSurfaceCoins()
	{
		if ( _definition == null || _heightfield == null )
			return;

		float topUpRadius = _heightfield.WorldSize * 0.55f;
		const int maxPasses = 64;
		const int spawnPerPass = 128;

		for ( int pass = 0; pass < maxPasses; pass++ )
		{
			int drawn = CountDrawnCoinSlots();
			if ( drawn >= _steadyVisibleBudget )
				break;

			int spawned = TopUpShallowCoinSeatsNear( Vector3.zero, topUpRadius, spawnPerPass );
			if ( spawned <= 0 )
				break;

			RebuildVisibility();
		}
	}

	bool IsBindTargetAlive( int bindId )
	{
		if ( this == null )
			return false;
		if ( bindId != _bindSerial )
			return false;
		if ( _pileRoot == null )
			return false;
		return true;
	}

	void EnsureStreamSettings()
	{
		if ( streamSettings != null )
			return;

#if UNITY_EDITOR
		streamSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<GoldPileLootStreamSettings>(
			GoldPileLootStreamSettings.AssetPath );
		if ( streamSettings == null )
		{
			streamSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<GoldPileLootStreamSettings>(
				GoldPileLootStreamSettings.LegacyAssetPath );
		}
#endif
		if ( streamSettings == null )
			streamSettings = ScriptableObject.CreateInstance<GoldPileLootStreamSettings>();
	}

	void AllocateChunkDrawnLists()
	{
		int count = Mathf.Max( 1, _chunkGrid.ChunkCount );
		_chunkDrawnSlots = new List<int>[ count ];
		for ( int i = 0; i < count; i++ )
			_chunkDrawnSlots[ i ] = new List<int>( 32 );
	}

	void LogInitialSpawnStats()
	{
		int drawn = 0;
		if ( _batches != null )
		{
			for ( int b = 0; b < _batches.Length; b++ )
			{
				BatchGroup group = _batches[ b ];
				if ( !group.IsPrimaryPart || group.SlotIndices == null )
					continue;
				drawn += group.SlotIndices.Count;
			}
		}

		int slotCount = _slots != null ? _slots.Length : 0;
		System.Text.StringBuilder sb = new System.Text.StringBuilder( 256 );
		sb.Append( "[GoldPileLootInstances] Initial spawn on '" ).Append( name ).Append( "': " );
		sb.Append( drawn ).Append( " instanced meshes in draw pool / " );
		sb.Append( slotCount ).Append( " slots (draw cap " ).Append( _maxVisibleTotal ).Append( ")" );
		sb.Append( "  chunks=" ).Append( _chunkGrid.ChunkCount );

		if ( _batches != null && _batches.Length > 0 )
		{
			sb.Append( "\n  By mesh batch:" );
			for ( int b = 0; b < _batches.Length; b++ )
			{
				BatchGroup group = _batches[ b ];
				int count = group.SlotIndices != null ? group.SlotIndices.Count : 0;
				string meshName = group.Mesh != null ? group.Mesh.name : "(null mesh)";
				sb.Append( "\n    - " ).Append( meshName ).Append( ": " ).Append( count );
			}
		}

		if ( _definition != null && _definition.coinContents != null && _visibleCountByEntry != null )
		{
			sb.Append( "\n  By treasure type:" );
			for ( int e = 0; e < _definition.coinContents.Length && e < _visibleCountByEntry.Length; e++ )
			{
				TreasurePileEntry entry = _definition.coinContents[ e ];
				if ( entry.treasure == null )
					continue;
				string typeName = !string.IsNullOrEmpty( entry.treasure.displayName )
					? entry.treasure.displayName
					: entry.treasure.name;
				sb.Append( "\n    - " ).Append( typeName ).Append( ": " )
					.Append( _visibleCountByEntry[ e ] )
					.Append( " drawn / " ).Append( entry.count ).Append( " inventory" );
			}
		}

		Debug.Log( sb.ToString(), this );
	}

	public void RefreshAfterCarve( Vector3 worldCenter, float radius )
	{
		if ( !_ready || _heightfield == null || _pileRoot == null )
			return;

		// Queue densify for next LateUpdate so carve/upload own the dig frame.
		_chunkGrid.MarkDirtyInRadius( worldCenter, radius );
		_lastDigLocal = _pileRoot.InverseTransformPoint( worldCenter );
		_hasLastDig = true;
		_pendingDensifyWorld = worldCenter;
		_pendingDensifyRadius = radius;
		_pendingDensify = true;
	}

	void RunPendingDensify()
	{
		if ( !_pendingDensify )
			return;

		_pendingDensify = false;
		RunDensifyAfterCarve( _pendingDensifyWorld, _pendingDensifyRadius );
	}

	void RunDensifyAfterCarve( Vector3 worldCenter, float radius )
	{
		if ( !_ready || _heightfield == null || _pileRoot == null || _slots == null )
			return;

		GoldPileEditTiming.Begin( "GoldPile.RefreshAfterCarve" );
		System.Diagnostics.Stopwatch phaseSw = GoldPileEditTiming.StartWatchIfEnabled();

		Vector3 local = _pileRoot.InverseTransformPoint( worldCenter );
		_lastDigLocal = local;
		_hasLastDig = true;

		// Coins/gems keep world pose; unsupported coins past release threshold pop to physics.
		int released = QueueUnsupportedCoinReleasesNear( local, radius );

		bool eligibilityChanged = released > 0;
		bool patchedDrawn = false;
		int scanned = 0;

		ForEachSlotIndexNearLocal( local, radius, slotIndex =>
		{
			Slot slot = _slots[ slotIndex ];
			if ( slot.Taken )
				return;

			scanned++;
			bool eligible = IsEligible( slot );
			if ( slot.Drawn != eligible )
				eligibilityChanged = true;
		} );

		if ( phaseSw != null )
		{
			phaseSw.Stop();
			GoldPileEditTiming.Record(
				"densify.eligibility",
				phaseSw.Elapsed.TotalMilliseconds,
				$"near={scanned} queuedRelease={released} slots={_slots.Length}" );
		}

		if ( eligibilityChanged )
			RebuildVisibility();
		else if ( patchedDrawn )
			_drawCacheDirty = true;

		// Throttled refill so digs keep near-surface density without hitching the carve frame.
		float topUpRadius = Mathf.Max( radius * 1.5f, _pickRadius * 4f );
		Vector3 topUpLocal = local;
		int topped = TopUpShallowCoinSeatsNear( topUpLocal, topUpRadius, maxSpawn: 48 );
		if ( topped > 0 )
			RebuildVisibility();

		GoldPileEditTiming.End();

		FlushPendingCoinWorldReleases();
	}

	/// <summary>
	/// Queues coin GPU seats whose outside fraction reached the gem release threshold.
	/// Seats keep pose until release; no stick/recycle/top-up.
	/// </summary>
	int QueueUnsupportedCoinReleasesNear( Vector3 localCenter, float radius )
	{
		if ( _slots == null || _heightfield == null )
			return 0;

		float radiusSq = radius * radius;
		int queued = 0;
		const int maxQueue = 48;

		for ( int i = 0; i < _slots.Length && queued < maxQueue; i++ )
		{
			Slot slot = _slots[ i ];
			if ( slot.Taken || slot.Definition == null )
				continue;
			if ( slot.Definition.category != TreasureCategory.Coin )
				continue;

			float dx = slot.LocalPos.x - localCenter.x;
			float dz = slot.LocalPos.z - localCenter.z;
			if ( dx * dx + dz * dz > radiusSq )
				continue;

			if ( !ShouldReleaseCoinSeat( slot ) )
				continue;

			if ( _pendingCoinWorldReleases.Contains( i ) )
				continue;

			_pendingCoinWorldReleases.Add( i );
			queued++;
		}

		return queued;
	}

	bool ShouldReleaseCoinSeat( Slot slot )
	{
		if ( _heightfield == null )
			return false;

		// Pivot left the mound volume (or column gone) — same oracle as placement, inverted.
		return !GoldPileTreasurePlacement.IsCoinPivotInside( _heightfield, slot.LocalPos );
	}

	void FlushPendingCoinWorldReleases()
	{
		if ( _coinReleasePassRunning || _pendingCoinWorldReleases.Count == 0 )
			return;

		_coinReleasePassRunning = true;
		_ = FlushPendingCoinWorldReleasesAsync();
	}

	async Task FlushPendingCoinWorldReleasesAsync()
	{
		try
		{
			while ( _pendingCoinWorldReleases.Count > 0 && _ready )
			{
				int slotIndex = _pendingCoinWorldReleases[ 0 ];
				_pendingCoinWorldReleases.RemoveAt( 0 );
				await ReleaseCoinSeatToWorldAsync( slotIndex );
			}
		}
		finally
		{
			_coinReleasePassRunning = false;
			if ( _pendingCoinWorldReleases.Count > 0 && _ready )
				FlushPendingCoinWorldReleases();
		}
	}

	async Task ReleaseCoinSeatToWorldAsync( int slotIndex )
	{
		if ( !_ready || _slots == null || slotIndex < 0 || slotIndex >= _slots.Length || _pileRoot == null )
			return;

		Slot slot = _slots[ slotIndex ];
		if ( slot.Taken || slot.Definition == null || slot.Definition.category != TreasureCategory.Coin )
			return;

		if ( !ShouldReleaseCoinSeat( slot ) )
			return;

		TreasureDefinition definition = slot.Definition;
		Vector3 worldPos = _pileRoot.TransformPoint( slot.LocalPos );
		Quaternion worldRot = _pileRoot.rotation * slot.LocalRot;

		if ( !TryTakeSlot( slotIndex, out TreasureDefinition taken ) || taken == null )
			return;

		definition = taken;

		TreasureItem item = await TreasureItemFactory.SpawnAsync( definition, worldPos, worldRot, null );
		if ( item == null )
			return;

		if ( this == null || !_ready )
		{
			TreasureItemFactory.Despawn( item );
			return;
		}

		item.ApplyWorldScale();
		if ( _owner != null )
			item.SetOriginPile( _owner );

		Vector3 velocity = Vector3.zero;
		if ( TreasureItem.UsesSurfaceSimulation( definition ) )
		{
			TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
			if ( world == null )
				world = TreasureSurfaceWorld.EnsureExists();

			if ( world != null
				&& world.Sampler != null
				&& world.Sampler.TrySample( worldPos, out TreasureSurfaceSample sample )
				&& sample.Valid )
			{
				TreasureSurfaceDefinition surfaceDef = world.Definition;
				float seatLift = TreasureSurfaceSeat.GetStableContactLift( item );
				float heightAbove = worldPos.y - ( sample.Height + seatLift );
				if ( heightAbove > 0.05f && surfaceDef != null )
					velocity.y = -Mathf.Min( surfaceDef.gravity * 0.15f, 2.5f );
			}

			item.EnterSurface( worldPos, worldRot, velocity, snapToSeat: false, fromRest: true );
		}
		else
			item.EnterPhysics( worldPos, worldRot, velocity );

		if ( _owner != null )
			_owner.NotifyPropReleasedToWorld( worldPos, coinInventoryAlreadyConsumed: true );
	}

	public void RefreshAll()
	{
		if ( !_ready )
			return;

		_chunkGrid.MarkAllDirty();
		RebuildVisibility();
	}

	public bool TryPickNearest(
		Vector3 worldPoint,
		float maxDistance,
		out int slotIndex,
		out TreasureDefinition definition,
		out Vector3 worldPos,
		out Quaternion worldRot )
	{
		slotIndex = -1;
		definition = null;
		worldPos = worldPoint;
		worldRot = Quaternion.identity;
		if ( !_ready || _pileRoot == null )
			return false;

		float maxDist = maxDistance > 0f ? maxDistance : _pickRadius;
		float bestSq = maxDist * maxDist;
		Vector3 local = _pileRoot.InverseTransformPoint( worldPoint );
		int cellX = LocalToCell( local.x );
		int cellZ = LocalToCell( local.z );

		RefreshStreamStateForPick();

		for ( int oz = -1; oz <= 1; oz++ )
		{
			for ( int ox = -1; ox <= 1; ox++ )
			{
				int cx = cellX + ox;
				int cz = cellZ + oz;
				if ( cx < 0 || cz < 0 || cx >= spatialCells || cz >= spatialCells )
					continue;

				List<int> list = _cellLists[ CellIndex( cx, cz ) ];
				for ( int i = 0; i < list.Count; i++ )
				{
					int idx = list[ i ];
					Slot slot = _slots[ idx ];
					if ( slot.Taken || !slot.Drawn )
						continue;
					if ( _heightfield != null
						&& !_heightfield.ExistsAtLocal( slot.LocalPos.x, slot.LocalPos.z ) )
						continue;
					if ( !PassesStreamFilter( idx, slot, checkExists: false ) )
						continue;
					if ( !IsPickable( slot ) )
						continue;

					float dx = slot.LocalPos.x - local.x;
					float dy = slot.LocalPos.y - local.y;
					float dz = slot.LocalPos.z - local.z;
					float sq = dx * dx + dy * dy + dz * dz;
					if ( sq > bestSq )
						continue;

					bestSq = sq;
					slotIndex = idx;
				}
			}
		}

		if ( slotIndex < 0 )
			return false;

		Slot best = _slots[ slotIndex ];
		definition = best.Definition;
		worldPos = _pileRoot.TransformPoint( best.LocalPos );
		worldRot = _pileRoot.rotation * best.LocalRot;
		return true;
	}

	public bool TryTakeSlot( int slotIndex, out TreasureDefinition definition )
	{
		definition = null;
		if ( !_ready || slotIndex < 0 || slotIndex >= _slots.Length )
			return false;

		Slot slot = _slots[ slotIndex ];
		if ( slot.Taken || slot.Definition == null )
			return false;

		if ( _remaining == null || !_remaining.TryGetValue( slot.Definition, out int left ) || left <= 0 )
			return false;

		_pickPrioritySlots.Remove( slotIndex );
		_remaining[ slot.Definition ] = left - 1;
		bool wasDrawn = slot.Drawn;

		slot.Taken = true;
		_slots[ slotIndex ] = slot;
		if ( wasDrawn )
			UnmarkDrawn( slotIndex );
		RemoveFromCell( slotIndex );
		definition = slot.Definition;
		return true;
	}

	/// <summary>
	/// Consumes one unit from internal inventory (any remaining type matching <paramref name="canTake"/>).
	/// Prefers marking an undrawn slot Taken so visible instances stay until explicitly picked.
	/// </summary>
	public bool TryConsumeFromInventory(
		System.Func<TreasureDefinition, bool> canTake,
		Vector3 preferredWorldPos,
		out TreasureDefinition definition,
		out Vector3 worldPos,
		out Quaternion worldRot )
	{
		definition = null;
		worldPos = preferredWorldPos;
		worldRot = Quaternion.identity;
		if ( !_ready || _remaining == null )
			return false;

		if ( !TryPickConsumableDefinition( canTake, out definition ) )
			return false;

		RememberDigWorld( preferredWorldPos );

		if ( definition.category == TreasureCategory.Coin )
		{
			if ( !_remaining.TryGetValue( definition, out int left ) || left <= 0 )
				return false;
			_remaining[ definition ] = left - 1;
			// Densify runs once after CarveForUnitsTaken → RefreshAfterCarve (possibly deferred).
			ResolveConsumePose( preferredWorldPos, out worldPos, out worldRot );
			return true;
		}

		if ( !TryMarkOneInventorySlotTaken( definition, out bool wasDrawn ) )
			return false;

		_ = wasDrawn;
		ResolveConsumePose( preferredWorldPos, out worldPos, out worldRot );
		return true;
	}

	/// <summary>
	/// Consumes up to <paramref name="count"/> inventory units with at most one visibility rebuild.
	/// </summary>
	public int TryConsumeManyFromInventory(
		System.Func<TreasureDefinition, bool> canTake,
		int count,
		Vector3 preferredWorldPos,
		List<TreasureDefinition> consumedDefs,
		out Vector3 worldPos,
		out Quaternion worldRot )
	{
		worldPos = preferredWorldPos;
		worldRot = Quaternion.identity;

		if ( count <= 0 || !_ready || _remaining == null || _slots == null )
			return 0;

		if ( !TryPickConsumableDefinition( canTake, out TreasureDefinition primary ) )
			return 0;

		if ( !_remaining.TryGetValue( primary, out int left ) || left <= 0 )
			return 0;

		RememberDigWorld( preferredWorldPos );

		int want = Mathf.Min( count, left );
		int taken = 0;

		if ( primary.category == TreasureCategory.Coin )
		{
			taken = want;
			_remaining[ primary ] = left - taken;
			if ( consumedDefs != null )
			{
				for ( int i = 0; i < taken; i++ )
					consumedDefs.Add( primary );
			}

			// Densify runs once after CarveForUnitsTaken → RefreshAfterCarve (possibly deferred).
			ResolveConsumePose( preferredWorldPos, out worldPos, out worldRot );
			return taken;
		}

		// Prefer undrawn slots in one pass (avoids N full-array FindMatchingUntakenSlot scans).
		for ( int i = 0; i < _slots.Length && taken < want; i++ )
		{
			Slot slot = _slots[ i ];
			if ( slot.Taken || slot.Definition != primary || slot.Drawn )
				continue;

			slot.Taken = true;
			_slots[ i ] = slot;
			RemoveFromCell( i );
			if ( consumedDefs != null )
				consumedDefs.Add( primary );
			taken++;
		}

		// Fall back to drawn slots only if inventory still needs more marks.
		for ( int i = 0; i < _slots.Length && taken < want; i++ )
		{
			Slot slot = _slots[ i ];
			if ( slot.Taken || slot.Definition != primary )
				continue;

			bool wasDrawn = slot.Drawn;
			slot.Taken = true;
			_slots[ i ] = slot;
			if ( wasDrawn )
				UnmarkDrawn( i );
			RemoveFromCell( i );
			if ( consumedDefs != null )
				consumedDefs.Add( primary );
			taken++;
		}

		// Inventory can exceed placed slots — still debit remaining for digs with no slot left.
		while ( taken < want )
		{
			if ( consumedDefs != null )
				consumedDefs.Add( primary );
			taken++;
		}

		_remaining[ primary ] = left - taken;

		if ( taken > 0 )
			ResolveConsumePose( preferredWorldPos, out worldPos, out worldRot );

		return taken;
	}

	bool TryPickConsumableDefinition( System.Func<TreasureDefinition, bool> canTake, out TreasureDefinition definition )
	{
		definition = null;
		List<TreasureDefinition> candidates = null;
		foreach ( KeyValuePair<TreasureDefinition, int> pair in _remaining )
		{
			if ( pair.Key == null || pair.Value <= 0 )
				continue;
			if ( canTake != null && !canTake( pair.Key ) )
				continue;

			if ( candidates == null )
				candidates = new List<TreasureDefinition>( 4 );
			candidates.Add( pair.Key );
		}

		if ( candidates == null || candidates.Count == 0 )
			return false;

		definition = candidates[ Random.Range( 0, candidates.Count ) ];
		return definition != null;
	}

	bool TryMarkOneInventorySlotTaken( TreasureDefinition definition, out bool wasDrawn )
	{
		wasDrawn = false;
		if ( definition == null || _remaining == null )
			return false;
		if ( !_remaining.TryGetValue( definition, out int left ) || left <= 0 )
			return false;

		_remaining[ definition ] = left - 1;

		int slotIndex = FindMatchingUntakenSlot( definition, preferUndrawn: true );
		if ( slotIndex < 0 )
			return true;

		Slot slot = _slots[ slotIndex ];
		wasDrawn = slot.Drawn;
		slot.Taken = true;
		_slots[ slotIndex ] = slot;
		if ( wasDrawn )
			UnmarkDrawn( slotIndex );
		RemoveFromCell( slotIndex );
		return true;
	}

	void ResolveConsumePose( Vector3 preferredWorldPos, out Vector3 worldPos, out Quaternion worldRot )
	{
		worldPos = preferredWorldPos;
		worldRot = Quaternion.identity;
		if ( _pileRoot == null )
			return;

		Vector3 local = _pileRoot.InverseTransformPoint( preferredWorldPos );
		if ( _heightfield != null )
		{
			float h = _heightfield.SampleNormalized( local.x, local.z ) * _heightfield.MaxHeight;
			worldPos = _pileRoot.TransformPoint( new Vector3( local.x, h + 0.05f, local.z ) );
		}

		worldRot = _pileRoot.rotation;
	}

	public bool TryPickFallback(
		out int slotIndex,
		out TreasureDefinition definition,
		out Vector3 worldPos,
		out Quaternion worldRot )
	{
		slotIndex = -1;
		definition = null;
		worldPos = transform.position;
		worldRot = Quaternion.identity;
		if ( !_ready )
			return false;

		if ( _remaining == null )
			return false;

		foreach ( KeyValuePair<TreasureDefinition, int> pair in _remaining )
		{
			if ( pair.Key == null || pair.Value <= 0 )
				continue;
			if ( pair.Key.category != TreasureCategory.Coin )
				continue;
			definition = pair.Key;
			if ( _heightfield != null && _pileRoot != null )
			{
				float half = _heightfield.WorldSize * 0.5f;
				float lx = Random.Range( -half * 0.4f, half * 0.4f );
				float lz = Random.Range( -half * 0.4f, half * 0.4f );
				float h = _heightfield.SampleNormalized( lx, lz ) * _heightfield.MaxHeight;
				worldPos = _pileRoot.TransformPoint( new Vector3( lx, h + 0.05f, lz ) );
			}
			return true;
		}

		return false;
	}

	public bool ConsumeFallbackDefinition( TreasureDefinition definition )
	{
		if ( !TryMarkOneInventorySlotTaken( definition, out bool wasDrawn ) )
			return false;

		if ( wasDrawn )
			RebuildVisibility();
		return true;
	}

	public void AddRemaining( TreasureDefinition definition )
	{
		if ( definition == null )
			return;

		if ( _remaining == null )
			_remaining = new Dictionary<TreasureDefinition, int>();

		if ( _remaining.ContainsKey( definition ) )
			_remaining[ definition ]++;
		else
			_remaining[ definition ] = 1;
	}

	public static bool UsesGpuInstances( TreasureDefinition definition )
	{
		if ( definition == null )
			return true;

		return definition.category == TreasureCategory.Coin;
	}

	/// <summary>
	/// Occupied volume bounds for cross-system unique seating.
	/// </summary>
	public void CollectVolumeBoundsOccupancy( List<Bounds> dst )
	{
		if ( dst == null || _slots == null )
			return;

		for ( int i = 0; i < _slots.Length; i++ )
		{
			Slot slot = _slots[ i ];
			if ( !slot.FixedVolumePose || slot.Taken )
				continue;
			Bounds bounds = GoldPileTreasurePlacement.LocalAabbFromPose(
				slot.LocalPos,
				slot.LocalRot,
				slot.Scale,
				GetCoinMesh( slot.Definition ) );
			dst.Add( bounds );
		}
	}

	public void CollectVolumePoseOccupancy( List<Vector3> dst )
	{
		if ( dst == null || _slots == null )
			return;

		for ( int i = 0; i < _slots.Length; i++ )
		{
			Slot slot = _slots[ i ];
			if ( !slot.FixedVolumePose || slot.Taken )
				continue;
			dst.Add( slot.LocalPos );
		}
	}

	int FindMatchingUntakenSlot( TreasureDefinition definition, bool preferUndrawn )
	{
		if ( definition == null || _slots == null )
			return -1;

		int fallback = -1;
		for ( int i = 0; i < _slots.Length; i++ )
		{
			Slot slot = _slots[ i ];
			if ( slot.Taken || slot.Definition != definition )
				continue;

			if ( preferUndrawn && !slot.Drawn )
				return i;

			if ( fallback < 0 )
				fallback = i;
		}

		return fallback;
	}

	public int GetRemaining( TreasureDefinition definition )
	{
		if ( definition == null || _remaining == null )
			return 0;
		return _remaining.TryGetValue( definition, out int n ) ? n : 0;
	}

	/// <summary>
	/// Returns a unit to pile inventory. Coins may stay hidden when local density is high;
	/// gems/artifacts always try to become a visible instance (search nearby if needed).
	/// </summary>
	public bool TryDeposit(
		TreasureDefinition definition,
		Vector3 preferredWorldPos,
		bool requireVisible,
		out Vector3 worldPos,
		out Quaternion worldRot,
		out bool becameVisible )
	{
		worldPos = preferredWorldPos;
		worldRot = Quaternion.identity;
		becameVisible = false;
		if ( !_ready || definition == null || _remaining == null || _pileRoot == null )
			return false;

		if ( _remaining.ContainsKey( definition ) )
			_remaining[ definition ]++;
		else
			_remaining[ definition ] = 1;

		Vector3 localPreferred = _pileRoot.InverseTransformPoint( preferredWorldPos );
		float spacingSq = _placementMinSpacing * _placementMinSpacing;
		bool densityHigh = IsTooCloseToDrawn( localPreferred, spacingSq )
			|| CountDrawnForDefinition( definition ) >= _maxVisibleTotal;

		bool wantVisible = requireVisible || !densityHigh;
		int slotIndex = FindNearestTakenSlot( definition, preferredWorldPos );

		if ( slotIndex < 0 && requireVisible )
			slotIndex = FindNearestTakenSlotSpiral( definition, preferredWorldPos );

		if ( requireVisible && slotIndex < 0 )
		{
			RollbackDepositIncrement( definition );
			return false;
		}

		if ( slotIndex >= 0 && wantVisible )
		{
			Slot slot = _slots[ slotIndex ];
			slot.Taken = false;
			if ( !slot.FixedVolumePose )
			{
				slot.EmbedDepth = Mathf.Min( slot.EmbedDepth, _initialRevealDepth * 0.5f );
				if ( requireVisible || slot.CoinVisual )
				{
					if ( GoldPileTreasurePlacement.TrySampleSolidCoinSurface(
						_heightfield,
						_pileLootSeed,
						slotIndex,
						localPreferred,
						Mathf.Max( _pickRadius * 3f, 1.25f ),
						_placementRadiusFraction,
						_coinSurfaceHeightFraction,
						slot.Scale * 0.5f,
						out Vector3 placeLocal,
						out float surface ) )
					{
						slot.LocalPos = placeLocal;
						slot.PlaceSurfaceHeight = surface;
					}
					else if ( TrySampleCoinFootprintFallback(
						slotIndex,
						slot.Scale * 0.5f,
						out Vector3 fallbackLocal,
						out surface ) )
					{
						slot.LocalPos = fallbackLocal;
						slot.PlaceSurfaceHeight = surface;
					}

					ConformSlot( slotIndex, ref slot );
				}
			}

			_slots[ slotIndex ] = slot;
			AssignCell( slotIndex );
		}

		RebuildVisibility();

		if ( slotIndex >= 0 )
		{
			if ( requireVisible )
				EnsureDepositedSlotVisible( slotIndex );

			Slot slot = _slots[ slotIndex ];
			becameVisible = !slot.Taken && slot.Drawn;
			worldPos = _pileRoot.TransformPoint( slot.LocalPos );
			worldRot = _pileRoot.rotation * slot.LocalRot;
		}
		else if ( _heightfield != null )
		{
			float h = _heightfield.SampleNormalized( localPreferred.x, localPreferred.z ) * _heightfield.MaxHeight;
			worldPos = _pileRoot.TransformPoint( new Vector3( localPreferred.x, h + 0.05f, localPreferred.z ) );
			worldRot = _pileRoot.rotation;
		}

		if ( requireVisible && !becameVisible )
		{
			// Buried gem seats stay latent until carve reveals them.
			if ( slotIndex >= 0 && _slots[ slotIndex ].FixedVolumePose )
				return true;

			RollbackVisibleDeposit( definition, slotIndex );
			return false;
		}

		return true;
	}

	void RollbackDepositIncrement( TreasureDefinition definition )
	{
		if ( definition == null || _remaining == null )
			return;

		if ( !_remaining.TryGetValue( definition, out int left ) )
			return;

		left--;
		if ( left <= 0 )
			_remaining.Remove( definition );
		else
			_remaining[ definition ] = left;
	}

	void RollbackVisibleDeposit( TreasureDefinition definition, int slotIndex )
	{
		RollbackDepositIncrement( definition );
		if ( slotIndex < 0 || _slots == null || slotIndex >= _slots.Length )
			return;

		_pickPrioritySlots.Remove( slotIndex );
		Slot slot = _slots[ slotIndex ];
		slot.Taken = true;
		slot.Drawn = false;
		_slots[ slotIndex ] = slot;
		RemoveFromCell( slotIndex );
		RebuildVisibility();
	}

	void EnsureDepositedSlotVisible( int slotIndex )
	{
		if ( !_ready || slotIndex < 0 || slotIndex >= _slots.Length )
			return;

		Slot slot = _slots[ slotIndex ];
		if ( slot.Taken )
			return;

		ForceSlotSurfaceVisible( slotIndex );
		AssignCell( slotIndex );
		slot = _slots[ slotIndex ];

		if ( slot.Drawn )
		{
			_pickPrioritySlots.Add( slotIndex );
			PatchDrawnSlotMatrix( slotIndex );
			InvalidateDrawCache();
			return;
		}

		if ( !IsEligible( slot ) )
		{
			slot.EmbedDepth = Mathf.Min( slot.EmbedDepth, _initialRevealDepth * 0.35f );
			ConformSlot( slotIndex, ref slot );
			_slots[ slotIndex ] = slot;
		}

		MarkDrawn( slotIndex );
		_pickPrioritySlots.Add( slotIndex );
		RebuildBatchMatrices();
		InvalidateDrawCache();
	}

	int GetDrawnCeiling( bool allowDigBuffer )
	{
		// Volume coins: only currently eligible (revealed) seats compete for the GPU draw cap.
		// Buried / not-yet-visible seats do not count. Dig buffer is unused.
		_ = allowDigBuffer;
		return _maxVisibleTotal;
	}

	int CountDrawnForDefinition( TreasureDefinition definition )
	{
		if ( _slots == null || definition == null )
			return 0;

		int n = 0;
		for ( int i = 0; i < _slots.Length; i++ )
		{
			if ( !_slots[ i ].Taken && _slots[ i ].Drawn && _slots[ i ].Definition == definition )
				n++;
		}

		return n;
	}

	int FindNearestTakenSlot( TreasureDefinition definition, Vector3 worldPos )
	{
		if ( _slots == null || _pileRoot == null || definition == null )
			return -1;

		Vector3 local = _pileRoot.InverseTransformPoint( worldPos );
		int best = -1;
		float bestDist = float.MaxValue;
		for ( int i = 0; i < _slots.Length; i++ )
		{
			Slot slot = _slots[ i ];
			if ( !slot.Taken || slot.Definition != definition )
				continue;

			float d = ( slot.LocalPos - local ).sqrMagnitude;
			if ( d >= bestDist )
				continue;

			bestDist = d;
			best = i;
		}

		return best;
	}

	int FindNearestTakenSlotSpiral( TreasureDefinition definition, Vector3 worldPos )
	{
		int direct = FindNearestTakenSlot( definition, worldPos );
		if ( direct >= 0 )
			return direct;

		if ( _pileRoot == null || _heightfield == null )
			return -1;

		Vector3 local = _pileRoot.InverseTransformPoint( worldPos );
		float step = Mathf.Max( 0.15f, _placementMinSpacing * 0.5f );
		for ( int ring = 1; ring <= 8; ring++ )
		{
			int samples = 6 * ring;
			for ( int s = 0; s < samples; s++ )
			{
				float ang = ( s / (float)samples ) * Mathf.PI * 2f;
				float r = step * ring;
				Vector3 probe = local + new Vector3( Mathf.Cos( ang ) * r, 0f, Mathf.Sin( ang ) * r );
				Vector3 worldProbe = _pileRoot.TransformPoint( probe );
				int idx = FindNearestTakenSlot( definition, worldProbe );
				if ( idx >= 0 )
					return idx;
			}
		}

		return -1;
	}

	Vector3 FindNearbySurfaceLocal( Vector3 preferredLocal, float spacingSq )
	{
		if ( _heightfield == null )
			return preferredLocal;

		if ( !IsTooCloseToDrawn( preferredLocal, spacingSq ) )
			return preferredLocal;

		float step = Mathf.Max( 0.12f, _placementMinSpacing * 0.45f );
		for ( int ring = 1; ring <= 10; ring++ )
		{
			int samples = 8 * ring;
			for ( int s = 0; s < samples; s++ )
			{
				float ang = ( s / (float)samples ) * Mathf.PI * 2f;
				float r = step * ring;
				Vector3 probe = preferredLocal + new Vector3( Mathf.Cos( ang ) * r, 0f, Mathf.Sin( ang ) * r );
				float half = _heightfield.WorldSize * 0.5f;
				if ( Mathf.Abs( probe.x ) > half || Mathf.Abs( probe.z ) > half )
					continue;
				if ( IsTooCloseToDrawn( probe, spacingSq ) )
					continue;
				return probe;
			}
		}

		return preferredLocal;
	}

	void AddToCell( int slotIndex )
	{
		if ( _slots == null || slotIndex < 0 || slotIndex >= _slots.Length || _cellLists == null )
			return;

		Slot slot = _slots[ slotIndex ];
		int cell = CellIndex( slot.CellX, slot.CellZ );
		if ( cell < 0 || cell >= _cellLists.Length )
			return;

		List<int> list = _cellLists[ cell ];
		if ( list != null && !list.Contains( slotIndex ) )
			list.Add( slotIndex );
	}

	void LateUpdate()
	{
		GoldPileEditTiming.TryFlushDeferredFromPriorFrame();
		RunPendingDensify();

		if ( !_ready || _batches == null )
		{
			LastDrawnCount = 0;
			LastCulledCount = 0;
			return;
		}

		GoldPileEditTiming.Begin( "GoldPile.RefreshStreamState" );
		bool streamChanged = RefreshStreamState();
		bool settingsChanged = StreamFilterSettingsChanged();
		if ( settingsChanged )
			_drawCacheFullRebuild = true;
		if ( streamChanged || settingsChanged )
			_drawCacheDirty = true;
		GoldPileEditTiming.End();

		if ( _drawCacheDirty )
		{
			GoldPileEditTiming.Begin( "GoldPile.RebuildStreamDrawCache" );
			RebuildStreamDrawCache();
			GoldPileEditTiming.End();
		}

		GoldPileEditTiming.Begin( "GoldPile.DrawFromStreamCache" );
		DrawFromStreamCache();
		GoldPileEditTiming.End();
	}

	void InvalidateDrawCache()
	{
		_drawCacheDirty = true;
		_drawCacheFullRebuild = true;
	}

	/// <summary>
	/// Detects live edits to density / category cull on the shared settings asset.
	/// Streamer Tick only dirties on LOD/frustum changes, so density tweaks need this.
	/// </summary>
	bool StreamFilterSettingsChanged()
	{
		if ( streamSettings == null )
			return false;

		float d0 = streamSettings.lod0Density;
		float d1 = streamSettings.lod1Density;
		float d2 = streamSettings.lod2Density;
		bool categoryCull = streamSettings.useCategoryDistanceCulling;
		if ( Mathf.Approximately( d0, _cachedLod0Density )
			&& Mathf.Approximately( d1, _cachedLod1Density )
			&& Mathf.Approximately( d2, _cachedLod2Density )
			&& categoryCull == _cachedCategoryCull )
			return false;

		_cachedLod0Density = d0;
		_cachedLod1Density = d1;
		_cachedLod2Density = d2;
		_cachedCategoryCull = categoryCull;
		return true;
	}

	void CacheDensityForRebuild()
	{
		if ( streamSettings == null )
		{
			_densityLod0 = 1f;
			_densityLod1 = 1f;
			_densityLod2 = 1f;
			_densityLod3 = 0f;
			return;
		}

		_densityLod0 = streamSettings.DensityForLod( 0 );
		_densityLod1 = streamSettings.DensityForLod( 1 );
		_densityLod2 = streamSettings.DensityForLod( 2 );
		_densityLod3 = streamSettings.DensityForLod( 3 );
	}

	float DensityForLodCached( int lod )
	{
		switch ( lod )
		{
			case 0: return _densityLod0;
			case 1: return _densityLod1;
			case 2: return _densityLod2;
			default: return _densityLod3;
		}
	}

	void RebuildStreamDrawCache()
	{
		_drawCacheDirty = false;
		CacheRebuildCount++;
		CacheDensityForRebuild();

		int chunkCount = _chunkGrid.ChunkCount;
		bool filter = _streamingEnabled && streamSettings != null && chunkCount > 0;
		bool incremental = filter
			&& !_drawCacheFullRebuild
			&& _streamer.DirtyChunkIndices != null
			&& _streamer.DirtyChunkIndices.Count > 0;

		if ( incremental )
		{
			for ( int b = 0; b < _batches.Length; b++ )
			{
				BatchGroup group = _batches[ b ];
				if ( group.AlwaysDraw || group.SlotIndices == null )
					continue;
				if ( group.ChunkStreamMatrices == null
					|| group.ChunkStreamMatrices.Length != chunkCount
					|| group.ChunkStreamCounts == null
					|| group.ChunkStreamCounts.Length != chunkCount )
				{
					incremental = false;
					break;
				}
			}
		}

		LastDirtyChunkRebuildCount = incremental ? _streamer.DirtyChunkIndices.Count : chunkCount;

		if ( incremental )
			RebuildStreamDrawCacheIncremental( chunkCount );
		else
			RebuildStreamDrawCacheFull( filter, chunkCount );

		_drawCacheFullRebuild = false;
		RecountDrawnPool();
	}

	void RecountDrawnPool()
	{
		DrawnPoolCount = 0;
		if ( _batches == null )
			return;

		for ( int b = 0; b < _batches.Length; b++ )
		{
			if ( !_batches[ b ].IsPrimaryPart || _batches[ b ].SlotIndices == null )
				continue;
			DrawnPoolCount += _batches[ b ].SlotIndices.Count;
		}
	}

	void RebuildStreamDrawCacheFull( bool filter, int chunkCount )
	{
		if ( chunkCount > 0 )
		{
			IReadOnlyList<GoldPileChunk> chunks = _chunkGrid.Chunks;
			for ( int i = 0; i < chunks.Count; i++ )
				chunks[ i ].LastDrawnCount = 0;
		}

		int culled = 0;

		for ( int b = 0; b < _batches.Length; b++ )
		{
			BatchGroup group = _batches[ b ];
			if ( group.SlotIndices == null )
				continue;

			int poolCount = group.SlotIndices.Count;
			if ( group.StreamMatrices == null || group.StreamMatrices.Length < poolCount )
				group.StreamMatrices = new Matrix4x4[ Mathf.Max( poolCount, 16 ) ];

			if ( filter && !group.AlwaysDraw )
				EnsureChunkStreamBuffers( ref group, chunkCount, poolCount );

			int fill = 0;
			if ( filter && !group.AlwaysDraw && group.ChunkStreamCounts != null )
			{
				for ( int c = 0; c < chunkCount; c++ )
					group.ChunkStreamCounts[ c ] = 0;
			}

			for ( int i = 0; i < poolCount; i++ )
			{
				int slotIndex = group.SlotIndices[ i ];
				Slot slot = _slots[ slotIndex ];
				if ( slot.Taken || !slot.Drawn )
				{
					culled++;
					continue;
				}

				if ( group.AlwaysDraw )
				{
					if ( _heightfield != null
						&& !_heightfield.ExistsAtLocal( slot.LocalPos.x, slot.LocalPos.z )
						&& !_pickPrioritySlots.Contains( slotIndex ) )
					{
						culled++;
						continue;
					}

					int alwaysMatrixIndex = ResolveMatrixIndex( slot, group, i );
					if ( group.Matrices == null
						|| alwaysMatrixIndex < 0
						|| alwaysMatrixIndex >= group.Matrices.Length )
					{
						culled++;
						continue;
					}

					group.StreamMatrices[ fill++ ] = group.Matrices[ alwaysMatrixIndex ];
					continue;
				}

				if ( filter && !PassesStreamFilter( slotIndex, slot, checkExists: true ) )
				{
					culled++;
					continue;
				}

				int matrixIndex = ResolveMatrixIndex( slot, group, i );
				if ( group.Matrices == null || matrixIndex < 0 || matrixIndex >= group.Matrices.Length )
				{
					culled++;
					continue;
				}

				Matrix4x4 matrix = group.Matrices[ matrixIndex ];

				if ( !filter )
				{
					group.StreamMatrices[ fill++ ] = matrix;
					continue;
				}

				AppendChunkStreamMatrix( ref group, slot, matrix, chunkCount );
			}

			if ( group.AlwaysDraw || !filter )
				group.StreamCount = fill;
			else
				ConcatChunkStreamsToFlat( ref group, chunkCount );

			_batches[ b ] = group;
		}

		LastCulledCount = culled;
		RecountChunkLastDrawnCounts( chunkCount );
	}

	void RebuildStreamDrawCacheIncremental( int chunkCount )
	{
		IReadOnlyList<int> dirty = _streamer.DirtyChunkIndices;
		IReadOnlyList<GoldPileChunk> chunks = _chunkGrid.Chunks;

		for ( int d = 0; d < dirty.Count; d++ )
		{
			int chunkIndex = dirty[ d ];
			if ( chunkIndex < 0 || chunkIndex >= chunkCount )
				continue;

			chunks[ chunkIndex ].LastDrawnCount = 0;

			for ( int b = 0; b < _batches.Length; b++ )
			{
				BatchGroup group = _batches[ b ];
				if ( group.AlwaysDraw || group.SlotIndices == null )
					continue;

				EnsureChunkStreamBuffers( ref group, chunkCount, group.SlotIndices.Count );
				if ( group.ChunkStreamCounts != null && chunkIndex < group.ChunkStreamCounts.Length )
					group.ChunkStreamCounts[ chunkIndex ] = 0;
				_batches[ b ] = group;
			}

			List<int> drawnSlots = _chunkDrawnSlots != null && chunkIndex < _chunkDrawnSlots.Length
				? _chunkDrawnSlots[ chunkIndex ]
				: null;
			if ( drawnSlots == null )
				continue;

			for ( int s = 0; s < drawnSlots.Count; s++ )
			{
				int slotIndex = drawnSlots[ s ];
				if ( slotIndex < 0 || slotIndex >= _slots.Length )
					continue;

				Slot slot = _slots[ slotIndex ];
				if ( slot.Taken || !slot.Drawn )
					continue;
				if ( !PassesStreamFilter( slotIndex, slot, checkExists: false ) )
					continue;
				if ( slot.BatchKey < 0 || slot.BatchKey >= _batches.Length )
					continue;

				List<int> sharedSlots = _batches[ slot.BatchKey ].SlotIndices;
				for ( int b = 0; b < _batches.Length; b++ )
				{
					BatchGroup group = _batches[ b ];
					if ( group.AlwaysDraw || group.SlotIndices != sharedSlots )
						continue;

					int matrixIndex = ResolveMatrixIndex( slot, group, -1 );
					if ( group.Matrices == null || matrixIndex < 0 || matrixIndex >= group.Matrices.Length )
						continue;

					AppendChunkStreamMatrix( ref group, slot, group.Matrices[ matrixIndex ], chunkCount );
					_batches[ b ] = group;
				}
			}
		}

		int culled = 0;
		for ( int b = 0; b < _batches.Length; b++ )
		{
			BatchGroup group = _batches[ b ];
			if ( group.AlwaysDraw || group.SlotIndices == null )
				continue;

			EnsureChunkStreamBuffers( ref group, chunkCount, group.SlotIndices.Count );
			ConcatChunkStreamsToFlat( ref group, chunkCount );
			_batches[ b ] = group;

			if ( group.IsPrimaryPart )
				culled += Mathf.Max( 0, group.SlotIndices.Count - group.StreamCount );
		}

		LastCulledCount = culled;
		RecountChunkLastDrawnCounts( chunkCount );
	}

	void RecountChunkLastDrawnCounts( int chunkCount )
	{
		if ( chunkCount <= 0 || _batches == null )
			return;

		IReadOnlyList<GoldPileChunk> chunks = _chunkGrid.Chunks;
		for ( int i = 0; i < chunks.Count; i++ )
			chunks[ i ].LastDrawnCount = 0;

		for ( int b = 0; b < _batches.Length; b++ )
		{
			BatchGroup group = _batches[ b ];
			if ( !group.IsPrimaryPart || group.AlwaysDraw || group.ChunkStreamCounts == null )
				continue;

			int n = Mathf.Min( chunkCount, group.ChunkStreamCounts.Length );
			for ( int c = 0; c < n; c++ )
				chunks[ c ].LastDrawnCount += group.ChunkStreamCounts[ c ];
		}
	}

	static int ResolveMatrixIndex( Slot slot, BatchGroup group, int poolIndexFallback )
	{
		int matrixIndex = slot.MatrixIndex;
		if ( matrixIndex < 0 || group.Matrices == null || matrixIndex >= group.Matrices.Length )
			matrixIndex = poolIndexFallback;
		return matrixIndex;
	}

	void AppendChunkStreamMatrix( ref BatchGroup group, Slot slot, Matrix4x4 matrix, int chunkCount )
	{
		int chunkIndex = slot.ChunkZ * _chunkGrid.CountX + slot.ChunkX;
		if ( chunkIndex < 0
			|| chunkIndex >= chunkCount
			|| group.ChunkStreamMatrices == null
			|| group.ChunkStreamCounts == null )
			return;

		int chunkFill = group.ChunkStreamCounts[ chunkIndex ];
		Matrix4x4[] chunkMats = group.ChunkStreamMatrices[ chunkIndex ];
		if ( chunkMats == null || chunkFill >= chunkMats.Length )
		{
			int newLen = Mathf.Max( chunkFill + 16, chunkMats != null ? chunkMats.Length * 2 : 16 );
			Matrix4x4[] grown = new Matrix4x4[ newLen ];
			if ( chunkMats != null && chunkFill > 0 )
				System.Array.Copy( chunkMats, grown, chunkFill );
			group.ChunkStreamMatrices[ chunkIndex ] = grown;
			chunkMats = grown;
		}

		chunkMats[ chunkFill ] = matrix;
		group.ChunkStreamCounts[ chunkIndex ] = chunkFill + 1;
	}

	static void EnsureChunkStreamBuffers( ref BatchGroup group, int chunkCount, int poolCount )
	{
		if ( group.ChunkStreamMatrices == null || group.ChunkStreamMatrices.Length != chunkCount )
			group.ChunkStreamMatrices = new Matrix4x4[ chunkCount ][];
		if ( group.ChunkStreamCounts == null || group.ChunkStreamCounts.Length != chunkCount )
			group.ChunkStreamCounts = new int[ chunkCount ];

		int perChunkHint = Mathf.Max( 8, poolCount / Mathf.Max( 1, chunkCount ) + 4 );
		for ( int c = 0; c < chunkCount; c++ )
		{
			if ( group.ChunkStreamMatrices[ c ] == null )
				group.ChunkStreamMatrices[ c ] = new Matrix4x4[ perChunkHint ];
		}
	}

	void ConcatChunkStreamsToFlat( ref BatchGroup group, int chunkCount )
	{
		if ( group.ChunkStreamCounts == null || group.ChunkStreamMatrices == null )
		{
			group.StreamCount = 0;
			return;
		}

		int total = 0;
		int n = Mathf.Min( chunkCount, group.ChunkStreamCounts.Length );
		for ( int c = 0; c < n; c++ )
			total += group.ChunkStreamCounts[ c ];

		if ( group.StreamMatrices == null || group.StreamMatrices.Length < total )
			group.StreamMatrices = new Matrix4x4[ Mathf.Max( total, 16 ) ];

		int fill = 0;
		for ( int c = 0; c < n; c++ )
		{
			int count = group.ChunkStreamCounts[ c ];
			if ( count <= 0 )
				continue;
			Matrix4x4[] chunkMats = group.ChunkStreamMatrices[ c ];
			if ( chunkMats == null )
				continue;
			System.Array.Copy( chunkMats, 0, group.StreamMatrices, fill, count );
			fill += count;
		}

		group.StreamCount = fill;
	}

	void DrawFromStreamCache()
	{
		Bounds worldBounds = GetStreamingDrawBounds();
		LastDrawnCount = DrawBatchesFlat( worldBounds );
	}

	Bounds GetStreamingDrawBounds()
	{
		Bounds pileBounds = GetPileWorldDrawBounds();
		if ( !_streamingEnabled
			|| streamSettings == null
			|| _streamer.RenderedCount <= 0 )
			return pileBounds;

		IReadOnlyList<GoldPileChunk> rendered = _streamer.RenderedChunks;
		Bounds bounds = ExpandChunkDrawBounds( rendered[ 0 ].WorldBounds );
		for ( int i = 1; i < rendered.Count; i++ )
			bounds.Encapsulate( ExpandChunkDrawBounds( rendered[ i ].WorldBounds ) );

		// AlwaysDraw props may sit outside the rendered LOD set; keep them inside batch AABB.
		bounds.Encapsulate( pileBounds );
		return bounds;
	}

	static Bounds ExpandChunkDrawBounds( Bounds bounds )
	{
		// Coins sit on the surface; pad enough that steep slopes / embed lift stay inside
		// Unity's RenderParams frustum cull for the instanced batch.
		float pad = Mathf.Max( 2.5f, Mathf.Max( bounds.size.x, bounds.size.z ) * 0.35f );
		bounds.Expand( pad );
		return bounds;
	}

	int DrawBatchesFlat( Bounds worldBounds )
	{
		int drawn = 0;
		for ( int b = 0; b < _batches.Length; b++ )
		{
			BatchGroup group = _batches[ b ];
			if ( group.Mesh == null || group.Material == null || group.StreamMatrices == null )
				continue;

			int count = group.StreamCount;
			if ( count <= 0 )
				continue;

			drawn += SubmitInstanced( group, group.StreamMatrices, count, worldBounds );
		}

		return drawn;
	}

	int SubmitInstanced( BatchGroup group, Matrix4x4[] matrices, int count, Bounds worldBounds )
	{
		if ( group.Material.enableInstancing == false )
			group.Material.enableInstancing = true;

		int submesh = Mathf.Clamp( group.SubmeshIndex, 0, Mathf.Max( 0, group.Mesh.subMeshCount - 1 ) );

		// Coin Pile mats are authored for GPU instancing. Held DragonLoot/Coin mats often are not —
		// force a non-instanced path so a missing pile mat never yields an invisible mound.
		if ( !group.SupportsInstancing )
		{
			int drawnFallback = 0;
			for ( int i = 0; i < count; i++ )
			{
				Matrix4x4 m = matrices[ i ];
				Vector3 pos = m.GetColumn( 3 );
				RenderParams rpOne = new RenderParams( group.Material )
				{
					layer = gameObject.layer,
					shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
					receiveShadows = false,
					renderingLayerMask = uint.MaxValue,
					worldBounds = new Bounds( pos, Vector3.one * 2f )
				};
				Graphics.RenderMesh( rpOne, group.Mesh, submesh, m );
				drawnFallback++;
			}

			return drawnFallback;
		}

		RenderParams rp = new RenderParams( group.Material )
		{
			layer = gameObject.layer,
			shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
			receiveShadows = false,
			renderingLayerMask = uint.MaxValue,
			worldBounds = worldBounds
		};

		if ( count <= BatchSize )
		{
			Graphics.RenderMeshInstanced( rp, group.Mesh, submesh, matrices, count );
			return count;
		}

		int drawn = 0;
		for ( int start = 0; start < count; start += BatchSize )
		{
			int n = Mathf.Min( BatchSize, count - start );
			for ( int i = 0; i < n; i++ )
				group.DrawBatch[ i ] = matrices[ start + i ];
			Graphics.RenderMeshInstanced( rp, group.Mesh, submesh, group.DrawBatch, n );
			drawn += n;
		}

		return drawn;
	}

	Bounds GetPileWorldDrawBounds()
	{
		if ( _pileRoot == null || _heightfield == null )
			return new Bounds( transform.position, Vector3.one * 50f );

		float size = _heightfield.WorldSize;
		float height = Mathf.Max( 1f, _heightfield.MaxHeight );
		Vector3 center = _pileRoot.TransformPoint( new Vector3( 0f, height * 0.5f, 0f ) );
		float scale = Mathf.Max(
			Mathf.Abs( _pileRoot.lossyScale.x ),
			Mathf.Abs( _pileRoot.lossyScale.z ) );
		float worldWidth = size * Mathf.Max( 0.01f, scale );
		return new Bounds(
			center,
			new Vector3( worldWidth * 1.5f, height * 2f + 4f, worldWidth * 1.5f ) );
	}

	void RefreshStreamStateForPick()
	{
		if ( !_streamingEnabled || streamSettings == null || _chunkGrid.ChunkCount == 0 )
			return;

		RefreshStreamState();
	}

	/// <summary>Returns true when stream membership / LOD changed this frame.</summary>
	bool RefreshStreamState()
	{
		if ( !_streamingEnabled || streamSettings == null || _chunkGrid.ChunkCount == 0 )
			return false;

		if ( !TreasureProximitySleep.TryGetPlayerPosition( out Vector3 playerPos ) )
		{
			Camera camFallback = ResolveCamera();
			if ( camFallback == null )
				return false;
			playerPos = camFallback.transform.position;
		}

		Camera camera = ResolveCamera();
		return _streamer.Tick( playerPos, camera );
	}

	bool PassesStreamFilter( int slotIndex, Slot slot, bool checkExists )
	{
		if ( checkExists
			&& _heightfield != null
			&& !_heightfield.ExistsAtLocal( slot.LocalPos.x, slot.LocalPos.z ) )
			return false;

		if ( checkExists && _pickPrioritySlots.Contains( slotIndex ) )
			return true;

		if ( !checkExists && _pickPrioritySlots.Count > 0 && _pickPrioritySlots.Contains( slotIndex ) )
			return true;

		if ( !_streamingEnabled || streamSettings == null || _chunkGrid.ChunkCount == 0 )
			return true;

		bool categoryCull = streamSettings.useCategoryDistanceCulling;
		int cullTier = categoryCull ? GetLootStreamCullTier( slot.Definition ) : LootStreamCullTierLarge;

		// Artifacts / large props: never distance-culled. Unity frustum-culls the draw bounds.
		if ( categoryCull && cullTier >= LootStreamCullTierLarge )
			return true;

		GoldPileChunk chunk = _chunkGrid.GetChunk( slot.ChunkX, slot.ChunkZ );
		if ( chunk == null )
			return false;

		if ( categoryCull && !PassesCategoryLodForTier( cullTier, chunk.Lod ) )
			return false;

		if ( chunk.State != GoldPileChunkStreamState.Rendered )
			return false;

		float density = DensityForLodCached( chunk.Lod );
		if ( density <= 0f )
			return false;
		if ( density >= 1f )
			return true;

		return Hash01( slotIndex, chunk.Seed ) < density;
	}

	const int LootStreamCullTierCoin = 0;
	const int LootStreamCullTierGem = 1;
	const int LootStreamCullTierLarge = 2;

	static int GetLootStreamCullTier( TreasureDefinition definition )
	{
		if ( definition == null )
			return LootStreamCullTierCoin;

		switch ( definition.category )
		{
			case TreasureCategory.Coin:
				return LootStreamCullTierCoin;
			case TreasureCategory.Gem:
				return LootStreamCullTierGem;
			default:
				return LootStreamCullTierLarge;
		}
	}

	static bool PassesCategoryLodForTier( int tier, int lod )
	{
		if ( tier >= LootStreamCullTierLarge )
			return true;
		// Coins stay eligible at every rendered LOD; lod0/1/2Density thins them.
		if ( tier == LootStreamCullTierCoin )
			return true;
		return lod <= 1;
	}

	static float Hash01( int slotIndex, int seed )
	{
		unchecked
		{
			uint h = ( uint )slotIndex;
			h ^= ( uint )seed * 747796405u;
			h ^= h >> 16;
			h *= 2246822519u;
			h ^= h >> 13;
			h *= 3266489917u;
			h ^= h >> 16;
			return ( h & 0x00FFFFFFu ) / 16777215f;
		}
	}

	Camera ResolveCamera()
	{
		if ( _cachedCamera != null )
			return _cachedCamera;

		Camera main = Camera.main;
		if ( main != null )
		{
			_cachedCamera = main;
			return _cachedCamera;
		}

		if ( GameMode.Instance != null && GameMode.Instance.cameraController != null )
		{
			CameraController controller = GameMode.Instance.cameraController;
			if ( controller.FirstPerson != null )
			{
				Camera cam = controller.FirstPerson.GetComponent<Camera>();
				if ( cam == null )
					cam = controller.FirstPerson.GetComponentInChildren<Camera>();
				if ( cam != null )
				{
					_cachedCamera = cam;
					return _cachedCamera;
				}
			}
		}

		return null;
	}

	void BuildRemaining( TreasurePileDefinition definition )
	{
		_remaining = new Dictionary<TreasureDefinition, int>();
		AddRemainingFromEntries( definition.coinContents );
		AddRemainingFromEntries( definition.treasureContents );
	}

	void AddRemainingFromEntries( TreasurePileEntry[] entries )
	{
		if ( entries == null )
			return;

		for ( int i = 0; i < entries.Length; i++ )
		{
			TreasurePileEntry entry = entries[ i ];
			if ( entry.treasure == null || entry.count <= 0 )
				continue;

			if ( _remaining.ContainsKey( entry.treasure ) )
				_remaining[ entry.treasure ] += entry.count;
			else
				_remaining[ entry.treasure ] = entry.count;
		}
	}

	struct VisualPart
	{
		public Mesh Mesh;
		public int SubmeshIndex;
		public Material Material;
		public Matrix4x4 LocalToRoot;
	}

	struct VisualAssets
	{
		public VisualPart[] Parts;
	}

	async Task EnsureFallbackVisualsAsync( TreasurePileDefinition definition )
	{
		if ( fallbackMesh == null )
		{
			GameObject temp = GameObject.CreatePrimitive( PrimitiveType.Cylinder );
			MeshFilter mf = temp.GetComponent<MeshFilter>();
			if ( mf != null )
				fallbackMesh = mf.sharedMesh;
			Destroy( temp );
		}

		if ( fallbackMaterial == null )
		{
			if ( pileMaterial != null )
				fallbackMaterial = pileMaterial;
			else
			{
				fallbackMaterial = new Material( Shader.Find( "Universal Render Pipeline/Lit" ) );
				fallbackMaterial.enableInstancing = true;
			}
		}
		else
			fallbackMaterial.enableInstancing = true;

		EnsurePileMaterialsRuntime();

		await Task.CompletedTask;
	}

	void EnsurePileMaterialsRuntime()
	{
		Shader pileShader = Shader.Find( "DragonLoot/Coin Pile" );
		if ( pileShader == null )
		{
			Debug.LogWarning(
				"[GoldPileLootInstances] Shader 'DragonLoot/Coin Pile' not found. "
				+ "Assign M_*CoinPile materials on this component so mound coins can draw.",
				this );
			return;
		}

		if ( pileMaterial == null )
			pileMaterial = CreateRuntimePileMaterial( pileShader, fallbackMaterial );

		if ( silverPileMaterial == null && pileMaterial != null )
			silverPileMaterial = CreateRuntimePileMaterial( pileShader, pileMaterial );
		if ( copperPileMaterial == null && pileMaterial != null )
			copperPileMaterial = CreateRuntimePileMaterial( pileShader, pileMaterial );

		if ( pileMaterial != null )
			pileMaterial.enableInstancing = true;
		if ( silverPileMaterial != null )
			silverPileMaterial.enableInstancing = true;
		if ( copperPileMaterial != null )
			copperPileMaterial.enableInstancing = true;
	}

	static Material CreateRuntimePileMaterial( Shader pileShader, Material source )
	{
		Material mat = new Material( pileShader );
		mat.enableInstancing = true;
		if ( source == null )
			return mat;

		if ( source.HasProperty( "_BaseMap" ) && mat.HasProperty( "_BaseMap" ) )
			mat.SetTexture( "_BaseMap", source.GetTexture( "_BaseMap" ) );
		if ( source.HasProperty( "_BumpMap" ) && mat.HasProperty( "_BumpMap" ) )
			mat.SetTexture( "_BumpMap", source.GetTexture( "_BumpMap" ) );
		if ( source.HasProperty( "_MetallicGlossMap" ) && mat.HasProperty( "_MetallicGlossMap" ) )
			mat.SetTexture( "_MetallicGlossMap", source.GetTexture( "_MetallicGlossMap" ) );
		if ( source.HasProperty( "_BaseColor" ) && mat.HasProperty( "_BaseColor" ) )
			mat.SetColor( "_BaseColor", source.GetColor( "_BaseColor" ) );
		if ( source.HasProperty( "_Metallic" ) && mat.HasProperty( "_Metallic" ) )
			mat.SetFloat( "_Metallic", source.GetFloat( "_Metallic" ) );
		if ( source.HasProperty( "_Smoothness" ) && mat.HasProperty( "_Smoothness" ) )
			mat.SetFloat( "_Smoothness", source.GetFloat( "_Smoothness" ) );
		if ( source.HasProperty( "_FresnelColor" ) && mat.HasProperty( "_FresnelColor" ) )
			mat.SetColor( "_FresnelColor", source.GetColor( "_FresnelColor" ) );
		return mat;
	}

	Material ResolveCoinPileMaterial( TreasureDefinition def )
	{
		if ( def == null || def.category != TreasureCategory.Coin )
			return null;

		if ( MatchesCoinVariant( def, "Silver" ) )
			return silverPileMaterial;
		if ( MatchesCoinVariant( def, "Copper" ) )
			return copperPileMaterial;
		if ( MatchesCoinVariant( def, "Gold" ) )
			return pileMaterial;

		return pileMaterial;
	}

	static bool MatchesCoinVariant( TreasureDefinition def, string variant )
	{
		if ( !string.IsNullOrEmpty( def.variant )
			&& string.Equals( def.variant, variant, System.StringComparison.OrdinalIgnoreCase ) )
			return true;

		if ( !string.IsNullOrEmpty( def.id )
			&& def.id.IndexOf( variant, System.StringComparison.OrdinalIgnoreCase ) >= 0 )
			return true;

		if ( !string.IsNullOrEmpty( def.displayName )
			&& def.displayName.IndexOf( variant, System.StringComparison.OrdinalIgnoreCase ) >= 0 )
			return true;

		return false;
	}

	static bool MaterialSupportsPileInstancing( Material material )
	{
		if ( material == null || !material.enableInstancing )
			return false;

		Shader shader = material.shader;
		return shader != null
			&& shader.name.IndexOf( "Coin Pile", System.StringComparison.Ordinal ) >= 0;
	}

	Material ResolveBatchMaterial( TreasureDefinition def, Material prefabOrOverrideMaterial )
	{
		if ( def != null && def.category == TreasureCategory.Coin )
		{
			Material coinPile = ResolveCoinPileMaterial( def );
			if ( coinPile != null )
				return coinPile;

			// Missing typed pile mat — keep the coin's own held/prefab look rather than gold.
			if ( prefabOrOverrideMaterial != null )
				return prefabOrOverrideMaterial;
			return fallbackMaterial;
		}

		if ( def != null && def.materialOverride != null )
			return def.materialOverride;
		if ( prefabOrOverrideMaterial != null )
			return prefabOrOverrideMaterial;
		return fallbackMaterial;
	}

	async Task<Dictionary<TreasureDefinition, VisualAssets>> ResolveVisualsAsync( TreasurePileDefinition definition )
	{
		Dictionary<TreasureDefinition, VisualAssets> map = new Dictionary<TreasureDefinition, VisualAssets>();
		await ResolveVisualsFromEntriesAsync( definition.coinContents, map );
		return map;
	}

	async Task ResolveVisualsFromEntriesAsync(
		TreasurePileEntry[] entries,
		Dictionary<TreasureDefinition, VisualAssets> map )
	{
		if ( entries == null )
			return;

		for ( int i = 0; i < entries.Length; i++ )
		{
			TreasureDefinition def = entries[ i ].treasure;
			if ( def == null || map.ContainsKey( def ) )
				continue;
			if ( !UsesGpuInstances( def ) )
				continue;

			VisualAssets assets = default;
			bool loadPrefab = def.prefab != null && def.prefab.RuntimeKeyIsValid()
				&& def.meshOverride == null;

			if ( loadPrefab )
				assets = await LoadVisualFromPrefabAsync( def.prefab );

			if ( assets.Parts == null || assets.Parts.Length == 0 )
			{
				Mesh mesh = def.meshOverride != null ? def.meshOverride : fallbackMesh;
				Material authored = def.materialOverride;
				Material batchMaterial = ResolveBatchMaterial( def, authored );
				if ( def.category == TreasureCategory.Coin
					&& ResolveCoinPileMaterial( def ) == null
					&& authored != null )
				{
					batchMaterial = EnsureRuntimeCoinPileMaterial( def, authored );
				}

				if ( batchMaterial != null )
					batchMaterial.enableInstancing = true;

				assets.Parts = new[]
				{
					new VisualPart
					{
						Mesh = mesh,
						SubmeshIndex = 0,
						Material = batchMaterial,
						LocalToRoot = Matrix4x4.identity
					}
				};
			}
			else
			{
				for ( int p = 0; p < assets.Parts.Length; p++ )
				{
					VisualPart part = assets.Parts[ p ];
					Material sourceLook = def.materialOverride != null ? def.materialOverride : part.Material;
					Material batchMaterial = ResolveBatchMaterial( def, sourceLook );

					if ( def.category == TreasureCategory.Coin
						&& ResolveCoinPileMaterial( def ) == null
						&& sourceLook != null )
					{
						batchMaterial = EnsureRuntimeCoinPileMaterial( def, sourceLook );
					}

					if ( batchMaterial != null )
						batchMaterial.enableInstancing = true;

					part.Material = batchMaterial;
					if ( part.Mesh == null )
						part.Mesh = fallbackMesh;
					assets.Parts[ p ] = part;
				}
			}

			map[ def ] = assets;
		}
	}

	Material EnsureRuntimeCoinPileMaterial( TreasureDefinition def, Material heldSource )
	{
		Shader pileShader = Shader.Find( "DragonLoot/Coin Pile" );
		if ( pileShader == null || heldSource == null )
			return heldSource;

		Material created = CreateRuntimePileMaterial( pileShader, heldSource );
		if ( MatchesCoinVariant( def, "Silver" ) )
		{
			if ( silverPileMaterial == null )
				silverPileMaterial = created;
			return silverPileMaterial;
		}

		if ( MatchesCoinVariant( def, "Copper" ) )
		{
			if ( copperPileMaterial == null )
				copperPileMaterial = created;
			return copperPileMaterial;
		}

		if ( pileMaterial == null )
			pileMaterial = created;
		return pileMaterial != null ? pileMaterial : created;
	}

	async Task<VisualAssets> LoadVisualFromPrefabAsync( AssetReferenceGameObject prefabRef )
	{
		VisualAssets assets = default;
		if ( prefabRef == null || !prefabRef.RuntimeKeyIsValid() )
			return assets;

		string key = prefabRef.RuntimeKey.ToString();
		if ( _visualsByKey.TryGetValue( key, out VisualAssets cached ) )
			return cached;

		AsyncOperationHandle<GameObject> handle = Addressables.LoadAssetAsync<GameObject>( prefabRef.RuntimeKey );
		await handle.Task;
		if ( handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null )
		{
			if ( handle.IsValid() )
				Addressables.Release( handle );
			return assets;
		}

		_visualHandles.Add( handle );

		ArtifactPrefabMeshSnapshot snapshot = ArtifactPresentationPrefabSnapshot.ExtractFromPrefabRoot( handle.Result );
		if ( !snapshot.HasParts )
		{
			_visualsByKey[ key ] = assets;
			return assets;
		}

		VisualPart[] parts = new VisualPart[ snapshot.Parts.Length ];
		for ( int i = 0; i < snapshot.Parts.Length; i++ )
		{
			ArtifactPrefabMeshPart source = snapshot.Parts[ i ];
			Material material = null;
			if ( source.Material != null )
			{
				// Instance so pile enableInstancing does not mutate Addressable shared mats.
				material = new Material( source.Material );
				material.enableInstancing = true;
			}

			parts[ i ] = new VisualPart
			{
				Mesh = source.Mesh,
				SubmeshIndex = source.SubmeshIndex,
				Material = material,
				LocalToRoot = source.LocalMatrix
			};
		}

		assets.Parts = parts;
		_visualsByKey[ key ] = assets;
		return assets;
	}

	void ReleaseVisualHandles()
	{
		for ( int i = 0; i < _visualHandles.Count; i++ )
		{
			if ( _visualHandles[ i ].IsValid() )
				Addressables.Release( _visualHandles[ i ] );
		}

		_visualHandles.Clear();
		_visualsByKey.Clear();
	}

	void OnDestroy()
	{
		TreasureSparkleMaskRegistrar.UnregisterInstanceSource( this );
		ReleaseVisualHandles();
		_streamer.Clear();
		_chunkGrid.Release();
	}

	void OnEnable()
	{
		TreasureSparkleMaskRegistrar.RegisterInstanceSource( this );
	}

	void OnDisable()
	{
		TreasureSparkleMaskRegistrar.UnregisterInstanceSource( this );
		_cachedCamera = null;
		_streamer.Clear();
		LastDrawnCount = 0;
		LastCulledCount = 0;
	}

	public TreasureSparkleDefinition.SparkleSourceKind MaskKind => TreasureSparkleDefinition.SparkleSourceKind.Coin;

	public bool TryGetSparkleWorldBounds( out Bounds bounds )
	{
		// Discovery walks the pile terrain AABB; loot only contributes to the mask.
		bounds = default;
		return false;
	}

	public void DrawSparkleMask( UnityEngine.Rendering.RasterCommandBuffer cmd, Material maskMaterial )
	{
		TreasureSparkleDefinition definition = TreasureSparkleRendererFeature.ActiveDefinition;
		if ( definition != null && !definition.maskLootInstances )
			return;

		if ( cmd == null || maskMaterial == null || !_ready || _batches == null )
			return;

		if ( _sparkleMaskPropertyBlock == null )
			_sparkleMaskPropertyBlock = new MaterialPropertyBlock();
		_sparkleMaskPropertyBlock.SetFloat(
			TreasureSparkleMaskPass.MaskWriteValueId,
			TreasureSparkleDefinition.MaskWriteValueForKind( MaskKind ) );

		for ( int b = 0; b < _batches.Length; b++ )
		{
			BatchGroup group = _batches[ b ];
			if ( group.Mesh == null || group.StreamMatrices == null || group.StreamCount <= 0 )
				continue;

			if ( group.DrawBatch == null || group.DrawBatch.Length < BatchSize )
				group.DrawBatch = new Matrix4x4[ BatchSize ];

			int submesh = Mathf.Clamp( group.SubmeshIndex, 0, Mathf.Max( 0, group.Mesh.subMeshCount - 1 ) );
			int count = group.StreamCount;
			Matrix4x4[] matrices = group.StreamMatrices;
			for ( int start = 0; start < count; start += BatchSize )
			{
				int n = Mathf.Min( BatchSize, count - start );
				for ( int i = 0; i < n; i++ )
					group.DrawBatch[ i ] = matrices[ start + i ];
				cmd.DrawMeshInstanced(
					group.Mesh,
					submesh,
					maskMaterial,
					0,
					group.DrawBatch,
					n,
					_sparkleMaskPropertyBlock );
			}

			_batches[ b ] = group;
		}
	}

	void BuildSlots( TreasurePileDefinition definition, Dictionary<TreasureDefinition, VisualAssets> visuals )
	{
		List<Slot> list = new List<Slot>( 256 );
		List<BatchGroup> batches = new List<BatchGroup>();
		_coinMeshBoundsByDef = new Dictionary<TreasureDefinition, Bounds>();
		_batchKeyByDef = new Dictionary<TreasureDefinition, int>();
		_coinUnitSerial = 0;

		TreasurePileEntry[] coinContents = definition.coinContents;
		if ( coinContents == null || coinContents.Length == 0 )
		{
			_slots = new Slot[ 0 ];
			_batches = new BatchGroup[ 0 ];
			_visibleCountByEntry = null;
			return;
		}

		_visibleCountByEntry = new int[ coinContents.Length ];
		int[] seatTargets = definition.ComputeCoinSeatTargets();
		float buryBand = Mathf.Max( _buryDepth, _initialRevealDepth );
		int drawCap = Mathf.Max( 1, definition.maxVisibleTotal );
		int steadyBudget = definition.SteadyCoinVisibleBudget();
		// Prefer shallow (visible) seats for the steady fill; dig-buffer remainder stays buried volume.
		float shallowFraction = Mathf.Clamp01( steadyBudget / ( float )drawCap );

		for ( int e = 0; e < coinContents.Length; e++ )
		{
			TreasurePileEntry entry = coinContents[ e ];
			if ( entry.treasure == null || entry.count <= 0 )
				continue;

			// Large props are real MeshRenderer objects (GoldPileArtifactProps), not GPU instances.
			if ( !UsesGpuInstances( entry.treasure ) )
				continue;

			if ( !visuals.TryGetValue( entry.treasure, out VisualAssets visual )
				|| visual.Parts == null
				|| visual.Parts.Length == 0 )
			{
				visual = new VisualAssets
				{
					Parts = new[]
					{
						new VisualPart
						{
							Mesh = fallbackMesh,
							SubmeshIndex = 0,
							Material = fallbackMaterial,
							LocalToRoot = Matrix4x4.identity
						}
					}
				};
			}

			if ( !_batchKeyByDef.TryGetValue( entry.treasure, out int batchKey ) )
			{
				batchKey = batches.Count;
				_batchKeyByDef[ entry.treasure ] = batchKey;
				CacheCoinMeshBounds( entry.treasure, visual );
				List<int> sharedSlots = new List<int>();
				bool alwaysDraw = false;
				for ( int p = 0; p < visual.Parts.Length; p++ )
				{
					VisualPart part = visual.Parts[ p ];
					Material mat = part.Material != null ? part.Material : fallbackMaterial;
					if ( mat != null )
						mat.enableInstancing = true;

					batches.Add( new BatchGroup
					{
						Mesh = part.Mesh != null ? part.Mesh : fallbackMesh,
						Material = mat,
						SubmeshIndex = part.SubmeshIndex,
						PartLocal = part.LocalToRoot,
						IsPrimaryPart = p == 0,
						AlwaysDraw = alwaysDraw,
						SupportsInstancing = MaterialSupportsPileInstancing( mat )
							|| ( alwaysDraw && mat != null && mat.enableInstancing ),
						SlotIndices = sharedSlots,
						Matrices = null,
						DrawBatch = new Matrix4x4[ BatchSize ]
					} );
				}
			}

			Mesh coinMesh = GetCoinMesh( entry.treasure );
			Bounds meshBounds = GetCoinMeshBounds( entry.treasure );

			// Visual densify seats (draw-budget sized). Mix weights come from entry.count; not 1:1 inventory.
			int want = 0;
			if ( seatTargets != null && e < seatTargets.Length )
				want = Mathf.Max( 0, seatTargets[ e ] );

			int shallowWant = Mathf.Clamp( Mathf.RoundToInt( want * shallowFraction ), 0, want );

			for ( int i = 0; i < want; i++ )
			{
				int unitIndex = _coinUnitSerial++;
				float scale = entry.treasure.worldScale.x;
				if ( scale < 0.01f )
					scale = 0.12f;
				if ( _placementScaleJitter > 0f )
				{
					float jitter = GoldPileTreasurePlacement.HashRange(
						_pileLootSeed,
						unitIndex * 19 + e,
						1f - _placementScaleJitter,
						1f + _placementScaleJitter );
					scale *= jitter;
				}

				float probe = Mathf.Max(
					0.04f,
					Mathf.Max( meshBounds.extents.x, meshBounds.extents.z ) * scale );

				bool preferShallow = i < shallowWant;
				Slot volumeSlot;
				bool placed = false;

				if ( preferShallow )
				{
					placed = TryCreateShallowCoinSlot(
						entry.treasure,
						e,
						batchKey,
						unitIndex,
						scale,
						probe,
						buryBand,
						coinMesh,
						preferNear: Vector3.zero,
						searchRadius: 0f,
						out volumeSlot,
						out _ );
					if ( !placed )
					{
						placed = TryCreateVolumeCoinSlot(
							entry.treasure,
							e,
							batchKey,
							unitIndex,
							scale,
							probe,
							coinMesh,
							out volumeSlot,
							out _ );
					}
				}
				else
				{
					placed = TryCreateVolumeCoinSlot(
						entry.treasure,
						e,
						batchKey,
						unitIndex,
						scale,
						probe,
						coinMesh,
						out volumeSlot,
						out _ );
					if ( !placed )
					{
						placed = TryCreateShallowCoinSlot(
							entry.treasure,
							e,
							batchKey,
							unitIndex,
							scale,
							probe,
							buryBand,
							coinMesh,
							preferNear: Vector3.zero,
							searchRadius: 0f,
							out volumeSlot,
							out _ );
					}
				}

				if ( !placed )
					continue;

				list.Add( volumeSlot );
			}
		}

		_slots = list.ToArray();
		_batches = batches.ToArray();
		_cellLists = new List<int>[ spatialCells * spatialCells ];
		_drawnCellLists = new List<int>[ spatialCells * spatialCells ];
		for ( int i = 0; i < _cellLists.Length; i++ )
		{
			_cellLists[ i ] = new List<int>( 16 );
			_drawnCellLists[ i ] = new List<int>( 8 );
		}

		for ( int i = 0; i < _slots.Length; i++ )
			AssignCell( i );
	}

	bool TryCreateVolumeCoinSlot(
		TreasureDefinition definition,
		int entryIndex,
		int batchKey,
		int unitIndex,
		float scale,
		float probe,
		Mesh coinMesh,
		out Slot slot,
		out Bounds localBounds )
	{
		slot = default;
		localBounds = default;
		if ( definition == null || _heightfield == null )
			return false;

		if ( !GoldPileTreasurePlacement.TrySampleVolumePose(
			_heightfield,
			_pileLootSeed,
			unitIndex,
			_placementRadiusFraction,
			scale,
			probe,
			_treasureRadialPower,
			_treasureHeightBias,
			out GoldPileTreasurePlacement.VolumePose pose ) )
		{
			return false;
		}

		Quaternion coinRot = GoldPileTreasurePlacement.CoinSurfaceTiltRotation(
			_heightfield,
			_pileLootSeed,
			unitIndex,
			pose.LocalPos.x,
			pose.LocalPos.z );
		Vector3 localPos = pose.LocalPos;
		float surface = _heightfield.SampleNormalized( localPos.x, localPos.z )
			* _heightfield.MaxHeight;
		float embed = Mathf.Max( 0.01f, surface - localPos.y );

		// Near-surface volume seats get mesh-bottom conform so they sit flush on the mound.
		if ( embed <= Mathf.Max( _initialRevealDepth * 2f, probe * 1.5f ) )
		{
			Bounds meshBounds = GetCoinMeshBounds( definition );
			Vector3 conformPos = localPos;
			Quaternion conformRot = coinRot;
			if ( GoldPileTreasurePlacement.ConformCoinToPileSurface(
				_heightfield,
				_pileLootSeed,
				unitIndex,
				ref conformPos,
				ref conformRot,
				scale,
				meshBounds,
				embed,
				_coinSurfaceHeightFraction ) )
			{
				localPos = conformPos;
				coinRot = conformRot;
				surface = _heightfield.SampleNormalized( localPos.x, localPos.z )
					* _heightfield.MaxHeight;
				embed = Mathf.Max( 0.01f, surface - localPos.y );
			}
			else if ( !GoldPileTreasurePlacement.IsCoinPivotInside( _heightfield, localPos ) )
			{
				return false;
			}
		}
		else if ( !GoldPileTreasurePlacement.IsCoinPivotInside( _heightfield, localPos ) )
		{
			return false;
		}

		localBounds = GoldPileTreasurePlacement.LocalAabbFromPose(
			localPos,
			coinRot,
			scale,
			coinMesh );

		slot = new Slot
		{
			Definition = definition,
			EntryIndex = entryIndex,
			LocalPos = localPos,
			LocalRot = coinRot,
			Scale = scale,
			EmbedDepth = embed,
			PlaceSurfaceHeight = surface,
			ProbeRadius = probe,
			FixedVolumePose = true,
			CoinVisual = false,
			Taken = false,
			Drawn = false,
			BatchKey = batchKey,
			MatrixIndex = -1
		};
		return true;
	}

	bool TryCreateShallowCoinSlot(
		TreasureDefinition definition,
		int entryIndex,
		int batchKey,
		int unitIndex,
		float scale,
		float probe,
		float buryBand,
		Mesh coinMesh,
		Vector3 preferNear,
		float searchRadius,
		out Slot slot,
		out Bounds localBounds )
	{
		slot = default;
		localBounds = default;
		if ( definition == null || _heightfield == null )
			return false;

		if ( !GoldPileTreasurePlacement.TrySampleShallowCoinPose(
			_heightfield,
			_pileLootSeed,
			unitIndex,
			_placementRadiusFraction,
			scale,
			probe,
			buryBand,
			_coinSurfaceHeightFraction,
			preferNear,
			searchRadius,
			out GoldPileTreasurePlacement.VolumePose pose ) )
		{
			return false;
		}

		Quaternion coinRot = GoldPileTreasurePlacement.CoinSurfaceTiltRotation(
			_heightfield,
			_pileLootSeed,
			unitIndex,
			pose.LocalPos.x,
			pose.LocalPos.z );
		Vector3 localPos = pose.LocalPos;
		Bounds meshBounds = GetCoinMeshBounds( definition );
		float surface = _heightfield.SampleNormalized( localPos.x, localPos.z )
			* _heightfield.MaxHeight;
		float embed = Mathf.Max( 0.01f, surface - localPos.y );

		if ( !GoldPileTreasurePlacement.ConformCoinToPileSurface(
			_heightfield,
			_pileLootSeed,
			unitIndex,
			ref localPos,
			ref coinRot,
			scale,
			meshBounds,
			embed,
			_coinSurfaceHeightFraction ) )
		{
			return false;
		}

		surface = _heightfield.SampleNormalized( localPos.x, localPos.z )
			* _heightfield.MaxHeight;
		embed = Mathf.Max( 0.01f, surface - localPos.y );
		if ( !GoldPileTreasurePlacement.IsCoinPivotInside( _heightfield, localPos ) )
			return false;

		localBounds = GoldPileTreasurePlacement.LocalAabbFromPose(
			localPos,
			coinRot,
			scale,
			coinMesh );

		slot = new Slot
		{
			Definition = definition,
			EntryIndex = entryIndex,
			LocalPos = localPos,
			LocalRot = coinRot,
			Scale = scale,
			EmbedDepth = embed,
			PlaceSurfaceHeight = surface,
			ProbeRadius = probe,
			FixedVolumePose = true,
			CoinVisual = false,
			Taken = false,
			Drawn = false,
			BatchKey = batchKey,
			MatrixIndex = -1
		};
		return true;
	}

	int RecycleUnsupportedCoinsNear( Vector3 localCenter, float radius )
	{
		if ( _slots == null || _heightfield == null )
			return 0;

		float buryBand = Mathf.Max( _buryDepth, _initialRevealDepth );
		float radiusSq = radius * radius;
		int recycled = 0;
		const int maxRecycle = 32;

		for ( int i = 0; i < _slots.Length && recycled < maxRecycle; i++ )
		{
			Slot slot = _slots[ i ];
			if ( slot.Taken || !slot.FixedVolumePose || slot.Definition == null )
				continue;

			float dx = slot.LocalPos.x - localCenter.x;
			float dz = slot.LocalPos.z - localCenter.z;
			if ( dx * dx + dz * dz > radiusSq )
				continue;

			// Only recycle seats the carve left floating / without a mound — not still-buried ones.
			if ( !CoinSeatNeedsReseat( slot ) )
				continue;

			int unitIndex = _coinUnitSerial++;
			Mesh coinMesh = GetCoinMesh( slot.Definition );
			if ( !TryCreateShallowCoinSlot(
				slot.Definition,
				slot.EntryIndex,
				slot.BatchKey,
				unitIndex,
				slot.Scale,
				slot.ProbeRadius,
				buryBand,
				coinMesh,
				localCenter,
				radius,
				out Slot reseated,
				out _ ) )
			{
				continue;
			}

			RemoveFromCell( i );
			reseated.Drawn = false;
			reseated.MatrixIndex = -1;
			_slots[ i ] = reseated;
			AssignCell( i );
			recycled++;
		}

		return recycled;
	}

	bool CoinSeatNeedsReseat( Slot slot )
	{
		return !GoldPileTreasurePlacement.IsCoinPivotInside( _heightfield, slot.LocalPos );
	}

	/// <summary>
	/// Re-seat coins that float above the carved surface without relocating XZ.
	/// </summary>
	int StickFloatingCoinsNear( Vector3 localCenter, float radius )
	{
		if ( _slots == null || _heightfield == null )
			return 0;

		float radiusSq = radius * radius;
		int stuck = 0;
		const int maxStick = 48;

		for ( int i = 0; i < _slots.Length && stuck < maxStick; i++ )
		{
			Slot slot = _slots[ i ];
			if ( slot.Taken || !slot.FixedVolumePose || slot.Definition == null )
				continue;

			float dx = slot.LocalPos.x - localCenter.x;
			float dz = slot.LocalPos.z - localCenter.z;
			if ( dx * dx + dz * dz > radiusSq )
				continue;

			if ( !_heightfield.ExistsAtLocal( slot.LocalPos.x, slot.LocalPos.z ) )
				continue;

			float currentSurface = _heightfield.SampleNormalized( slot.LocalPos.x, slot.LocalPos.z )
				* _heightfield.MaxHeight;
			// Any visible float — stick before the larger recycle floatEps kicks in.
			if ( slot.LocalPos.y <= currentSurface + 0.002f )
				continue;

			Vector3 localPos = slot.LocalPos;
			Quaternion localRot = slot.LocalRot;
			Bounds meshBounds = GetCoinMeshBounds( slot.Definition );
			// Stick onto the new surface — don't keep a deep pre-carve bury depth.
			float embed = Mathf.Min(
				slot.EmbedDepth,
				Mathf.Max( _initialRevealDepth, slot.ProbeRadius * 0.45f ) );
			if ( !GoldPileTreasurePlacement.ConformCoinToPileSurface(
				_heightfield,
				_pileLootSeed,
				i,
				ref localPos,
				ref localRot,
				slot.Scale,
				meshBounds,
				embed,
				_coinSurfaceHeightFraction ) )
			{
				continue;
			}

			if ( ( localPos - slot.LocalPos ).sqrMagnitude < 1e-8f
				&& localRot == slot.LocalRot )
			{
				continue;
			}

			int oldChunkX = slot.ChunkX;
			int oldChunkZ = slot.ChunkZ;
			int oldCellX = slot.CellX;
			int oldCellZ = slot.CellZ;
			bool keepDrawn = slot.Drawn && slot.MatrixIndex >= 0;

			RemoveFromCell( i );
			slot.LocalPos = localPos;
			slot.LocalRot = localRot;
			slot.PlaceSurfaceHeight = _heightfield.SampleNormalized( localPos.x, localPos.z )
				* _heightfield.MaxHeight;
			slot.EmbedDepth = Mathf.Max( 0.01f, slot.PlaceSurfaceHeight - localPos.y );
			_slots[ i ] = slot;
			AssignCell( i );

			if ( keepDrawn )
			{
				Slot updated = _slots[ i ];
				if ( updated.ChunkX != oldChunkX || updated.ChunkZ != oldChunkZ )
					MoveDrawnSlotChunk( i, oldChunkX, oldChunkZ, updated.ChunkX, updated.ChunkZ );
				if ( updated.CellX != oldCellX || updated.CellZ != oldCellZ )
					MoveDrawnSlotCell( i, oldCellX, oldCellZ, updated.CellX, updated.CellZ );
				PatchDrawnSlotMatrix( i );
			}

			stuck++;
		}

		return stuck;
	}

	int TopUpShallowCoinSeatsNear( Vector3 localCenter, float radius, int maxSpawn = 48 )
	{
		if ( _definition == null || _definition.coinContents == null || _batchKeyByDef == null )
			return 0;

		int remaining = CountRemainingCoinInventory();
		if ( remaining <= 0 )
			return HideAllCoinVisuals();

		// Visual densify only — keep near-surface draw near the steady target while the pile has stock.
		int target = _steadyVisibleBudget;
		int drawn = CountDrawnCoinSlots();
		int need = target - drawn;
		if ( need <= 0 )
			return 0;

		need = Mathf.Min( need, Mathf.Max( 1, maxSpawn ) );
		float buryBand = Mathf.Max( _buryDepth, _initialRevealDepth );
		int seatCap = _maxVisibleTotal;
		int untaken = CountUntakenCoinSlots();
		int spawned = 0;

		TreasurePileEntry[] coinContents = _definition.coinContents;
		for ( int n = 0; n < need; n++ )
		{
			if ( !TryPickCoinDefForVisualTopUp( coinContents, out TreasureDefinition def, out int entryIndex ) )
				break;

			if ( !_batchKeyByDef.TryGetValue( def, out int batchKey ) )
				break;

			int unitIndex = _coinUnitSerial++;
			float scale = def.worldScale.x;
			if ( scale < 0.01f )
				scale = 0.12f;
			Bounds meshBounds = GetCoinMeshBounds( def );
			float probe = Mathf.Max(
				0.04f,
				Mathf.Max( meshBounds.extents.x, meshBounds.extents.z ) * scale );
			Mesh coinMesh = GetCoinMesh( def );

			if ( !TryCreateShallowCoinSlot(
				def,
				entryIndex,
				batchKey,
				unitIndex,
				scale,
				probe,
				buryBand,
				coinMesh,
				localCenter,
				radius,
				out Slot slot,
				out _ ) )
			{
				continue;
			}

			if ( TryReuseTakenCoinSlot( def, slot ) )
			{
				spawned++;
				continue;
			}

			if ( TryPromoteBuriedUntakenSeat( def, slot ) )
			{
				spawned++;
				continue;
			}

			if ( untaken < seatCap )
			{
				AppendCoinSlot( slot );
				untaken++;
				spawned++;
				continue;
			}

			// Seat pool full — reuse any Taken coin seat regardless of type.
			if ( TryReuseTakenCoinSlot( null, slot ) )
			{
				spawned++;
				continue;
			}

			break;
		}

		return spawned;
	}

	int HideAllCoinVisuals()
	{
		if ( _slots == null )
			return 0;

		int hidden = 0;
		for ( int i = 0; i < _slots.Length; i++ )
		{
			Slot slot = _slots[ i ];
			if ( !slot.FixedVolumePose || slot.Taken )
				continue;

			bool wasDrawn = slot.Drawn;
			slot.Taken = true;
			_slots[ i ] = slot;
			if ( wasDrawn )
				UnmarkDrawn( i );
			RemoveFromCell( i );
			hidden++;
		}

		return hidden;
	}

	/// <summary>
	/// Pick a coin type for visual densify by authored mix weight, preferring under-drawn types.
	/// Inventory remaining only gates whether the pile still shows coins at all.
	/// </summary>
	bool TryPickCoinDefForVisualTopUp(
		TreasurePileEntry[] coinContents,
		out TreasureDefinition definition,
		out int entryIndex )
	{
		definition = null;
		entryIndex = -1;
		if ( coinContents == null )
			return false;

		int totalWeight = 0;
		for ( int e = 0; e < coinContents.Length; e++ )
		{
			TreasurePileEntry entry = coinContents[ e ];
			if ( entry.treasure == null || entry.count <= 0 || !UsesGpuInstances( entry.treasure ) )
				continue;
			if ( !_batchKeyByDef.ContainsKey( entry.treasure ) )
				continue;
			totalWeight += entry.count;
		}

		if ( totalWeight <= 0 )
			return false;

		float bestScore = float.MaxValue;
		for ( int e = 0; e < coinContents.Length; e++ )
		{
			TreasurePileEntry entry = coinContents[ e ];
			if ( entry.treasure == null || entry.count <= 0 || !UsesGpuInstances( entry.treasure ) )
				continue;
			if ( !_batchKeyByDef.ContainsKey( entry.treasure ) )
				continue;

			float wantShare = entry.count / ( float )totalWeight;
			int drawn = CountDrawnForDefinition( entry.treasure );
			float haveShare = drawn / ( float )Mathf.Max( 1, _steadyVisibleBudget );
			float score = haveShare - wantShare;
			if ( score < bestScore )
			{
				bestScore = score;
				definition = entry.treasure;
				entryIndex = e;
			}
		}

		return definition != null;
	}

	int CountBuriedUntakenSlotsForDefinition( TreasureDefinition definition )
	{
		if ( _slots == null || definition == null )
			return 0;

		int n = 0;
		for ( int i = 0; i < _slots.Length; i++ )
		{
			Slot slot = _slots[ i ];
			if ( slot.Taken || slot.Definition != definition || !slot.FixedVolumePose )
				continue;
			if ( slot.Drawn )
				continue;
			n++;
		}

		return n;
	}

	bool TryPromoteBuriedUntakenSeat( TreasureDefinition definition, Slot reseated )
	{
		if ( _slots == null || definition == null )
			return false;

		for ( int i = 0; i < _slots.Length; i++ )
		{
			Slot existing = _slots[ i ];
			if ( existing.Taken || existing.Definition != definition || !existing.FixedVolumePose )
				continue;
			if ( existing.Drawn )
				continue;

			RemoveFromCell( i );
			reseated.Taken = false;
			reseated.Drawn = false;
			reseated.MatrixIndex = -1;
			_slots[ i ] = reseated;
			AssignCell( i );
			return true;
		}

		return false;
	}

	bool TryReuseTakenCoinSlot( TreasureDefinition definition, Slot reseated )
	{
		if ( _slots == null )
			return false;

		for ( int i = 0; i < _slots.Length; i++ )
		{
			Slot existing = _slots[ i ];
			if ( !existing.Taken )
				continue;
			if ( definition != null && existing.Definition != definition )
				continue;
			if ( existing.Definition == null || !UsesGpuInstances( existing.Definition ) )
				continue;

			RemoveFromCell( i );
			reseated.Taken = false;
			reseated.Drawn = false;
			reseated.MatrixIndex = -1;
			_slots[ i ] = reseated;
			AssignCell( i );
			return true;
		}

		return false;
	}

	bool TryPickCoinDefNeedingSeats(
		TreasurePileEntry[] coinContents,
		out TreasureDefinition definition,
		out int entryIndex )
	{
		definition = null;
		entryIndex = -1;
		if ( coinContents == null || _remaining == null )
			return false;

		int bestNeed = 0;
		for ( int e = 0; e < coinContents.Length; e++ )
		{
			TreasureDefinition def = coinContents[ e ].treasure;
			if ( def == null || !UsesGpuInstances( def ) )
				continue;

			int rem = GetRemaining( def );
			if ( rem <= 0 )
				continue;

			int untaken = CountUntakenSlotsForDefinition( def );
			int need = rem - untaken;
			if ( need > bestNeed )
			{
				bestNeed = need;
				definition = def;
				entryIndex = e;
			}
		}

		return definition != null && bestNeed > 0;
	}

	int CountDrawnCoinSlots()
	{
		if ( _slots == null )
			return 0;

		int n = 0;
		for ( int i = 0; i < _slots.Length; i++ )
		{
			if ( _slots[ i ].Drawn && _slots[ i ].FixedVolumePose && !_slots[ i ].Taken )
				n++;
		}

		return n;
	}

	int CountUntakenCoinSlots()
	{
		if ( _slots == null )
			return 0;

		int n = 0;
		for ( int i = 0; i < _slots.Length; i++ )
		{
			if ( !_slots[ i ].Taken && _slots[ i ].FixedVolumePose )
				n++;
		}

		return n;
	}

	int CountUntakenSlotsForDefinition( TreasureDefinition definition )
	{
		if ( _slots == null || definition == null )
			return 0;

		int n = 0;
		for ( int i = 0; i < _slots.Length; i++ )
		{
			if ( !_slots[ i ].Taken && _slots[ i ].Definition == definition )
				n++;
		}

		return n;
	}

	int CountRemainingCoinInventory()
	{
		if ( _remaining == null )
			return 0;

		int n = 0;
		foreach ( KeyValuePair<TreasureDefinition, int> pair in _remaining )
		{
			if ( pair.Key != null && UsesGpuInstances( pair.Key ) )
				n += pair.Value;
		}

		return n;
	}

	void AppendCoinSlot( Slot slot )
	{
		int index = _slots != null ? _slots.Length : 0;
		System.Array.Resize( ref _slots, index + 1 );
		_slots[ index ] = slot;
		AssignCell( index );
	}

	/// <summary>
	/// Deterministic area-uniform footprint fallback when primary sampling fails.
	/// </summary>
	bool TrySampleCoinFootprintFallback( int slotIndex, float coinInsetRadius, out Vector3 localPos, out float surfaceHeight )
	{
		localPos = Vector3.zero;
		surfaceHeight = 0f;
		if ( _heightfield == null )
			return false;

		float half = _heightfield.WorldSize * 0.5f * _placementRadiusFraction;
		float usable = Mathf.Max( 0.05f, half - Mathf.Max( 0f, coinInsetRadius ) );
		float minSurface = Mathf.Max(
			_heightfield.GroundLevel,
			_heightfield.MaxHeight * Mathf.Clamp01( _coinSurfaceHeightFraction ) );

		for ( int attempt = 0; attempt < 64; attempt++ )
		{
			int salt = slotIndex * 53 + attempt;
			float angle = GoldPileTreasurePlacement.Hash01( _pileLootSeed, salt ) * Mathf.PI * 2f;
			float radius = usable * Mathf.Sqrt( GoldPileTreasurePlacement.Hash01( _pileLootSeed, salt + 1 ) );
			float lx = Mathf.Cos( angle ) * radius;
			float lz = Mathf.Sin( angle ) * radius;
			if ( !GoldPileTreasurePlacement.IsCoinFootprintSupported(
				_heightfield,
				new Vector3( lx, 0f, lz ),
				coinInsetRadius,
				minSurface,
				out float surface ) )
			{
				continue;
			}

			localPos = new Vector3( lx, 0f, lz );
			surfaceHeight = surface;
			return true;
		}

		return false;
	}

	void RememberDigWorld( Vector3 worldPos )
	{
		if ( _pileRoot == null )
			return;
		_lastDigLocal = _pileRoot.InverseTransformPoint( worldPos );
		_hasLastDig = true;
	}

	float GetOutsideFraction( Slot slot )
	{
		if ( _heightfield == null )
			return 0f;
		return GoldPileTreasurePlacement.OutsideFractionSphere(
			_heightfield,
			slot.LocalPos,
			Mathf.Max( 0.02f, slot.ProbeRadius ) );
	}

	bool TouchesOutside( Slot slot )
	{
		if ( _heightfield == null )
			return false;
		return GoldPileTreasurePlacement.TouchesOutsideSphere(
			_heightfield,
			slot.LocalPos,
			Mathf.Max( 0.02f, slot.ProbeRadius ) );
	}

	bool IsPickable( Slot slot )
	{
		if ( slot.Definition == null )
			return false;

		if ( slot.Definition.category != TreasureCategory.Gem )
			return true;

		return GetOutsideFraction( slot ) >= _treasurePickupOutsideFraction;
	}

	bool TryReseatCoinSlot( int slotIndex, Vector3 nearLocal, bool preserveDrawState )
	{
		if ( _slots == null || slotIndex < 0 || slotIndex >= _slots.Length )
			return false;

		Slot slot = _slots[ slotIndex ];
		if ( slot.Definition == null )
			return false;
		if ( !slot.FixedVolumePose && !slot.CoinVisual )
			return false;

		float buryBand = Mathf.Max( _buryDepth, _initialRevealDepth );
		float digRadius = Mathf.Max( _pickRadius * 3f, 1.25f );
		Mesh coinMesh = GetCoinMesh( slot.Definition );

		if ( slot.FixedVolumePose )
		{
			if ( !TryCreateShallowCoinSlot(
				slot.Definition,
				slot.EntryIndex,
				slot.BatchKey,
				_coinUnitSerial++,
				slot.Scale,
				slot.ProbeRadius,
				buryBand,
				coinMesh,
				nearLocal,
				digRadius,
				out Slot reseated,
				out _ ) )
			{
				return false;
			}

			bool keepDrawn = preserveDrawState && slot.Drawn && slot.MatrixIndex >= 0;
			int matrixIndex = slot.MatrixIndex;
			int oldChunkX = slot.ChunkX;
			int oldChunkZ = slot.ChunkZ;
			int oldCellX = slot.CellX;
			int oldCellZ = slot.CellZ;

			RemoveFromCell( slotIndex );
			reseated.Taken = false;
			if ( keepDrawn )
			{
				reseated.Drawn = true;
				reseated.MatrixIndex = matrixIndex;
			}
			else
			{
				reseated.Drawn = false;
				reseated.MatrixIndex = -1;
			}

			_slots[ slotIndex ] = reseated;
			AssignCell( slotIndex );

			if ( keepDrawn )
			{
				Slot updated = _slots[ slotIndex ];
				if ( updated.ChunkX != oldChunkX || updated.ChunkZ != oldChunkZ )
					MoveDrawnSlotChunk( slotIndex, oldChunkX, oldChunkZ, updated.ChunkX, updated.ChunkZ );
				if ( updated.CellX != oldCellX || updated.CellZ != oldCellZ )
					MoveDrawnSlotCell( slotIndex, oldCellX, oldCellZ, updated.CellX, updated.CellZ );
				PatchDrawnSlotMatrix( slotIndex );
			}

			return true;
		}

		if ( !GoldPileTreasurePlacement.TrySampleSolidCoinSurface(
			_heightfield,
			_pileLootSeed,
			slotIndex,
			nearLocal,
			digRadius,
			_placementRadiusFraction,
			_coinSurfaceHeightFraction,
			slot.Scale * 0.5f,
			out Vector3 pos,
			out float surface ) )
		{
			if ( !TrySampleCoinFootprintFallback( slotIndex, slot.Scale * 0.5f, out pos, out surface ) )
				return false;
		}

		bool keepDrawnLegacy = preserveDrawState && slot.Drawn && slot.MatrixIndex >= 0;
		int matrixIndexLegacy = slot.MatrixIndex;
		int oldChunkXLegacy = slot.ChunkX;
		int oldChunkZLegacy = slot.ChunkZ;
		int oldCellXLegacy = slot.CellX;
		int oldCellZLegacy = slot.CellZ;

		RemoveFromCell( slotIndex );
		slot.LocalPos = pos;
		slot.PlaceSurfaceHeight = surface;
		slot.EmbedDepth = Mathf.Min( _initialRevealDepth * 0.35f, _buryDepth );
		slot.Taken = false;
		if ( keepDrawnLegacy )
		{
			slot.Drawn = true;
			slot.MatrixIndex = matrixIndexLegacy;
		}
		else
		{
			slot.Drawn = false;
			slot.MatrixIndex = -1;
		}

		ConformSlot( slotIndex, ref slot );
		_slots[ slotIndex ] = slot;
		AssignCell( slotIndex );

		if ( keepDrawnLegacy )
		{
			Slot updated = _slots[ slotIndex ];
			if ( updated.ChunkX != oldChunkXLegacy || updated.ChunkZ != oldChunkZLegacy )
				MoveDrawnSlotChunk( slotIndex, oldChunkXLegacy, oldChunkZLegacy, updated.ChunkX, updated.ChunkZ );
			if ( updated.CellX != oldCellXLegacy || updated.CellZ != oldCellZLegacy )
				MoveDrawnSlotCell( slotIndex, oldCellXLegacy, oldCellZLegacy, updated.CellX, updated.CellZ );
			PatchDrawnSlotMatrix( slotIndex );
		}

		return true;
	}

	bool TryReseatCoinSlot( int slotIndex, Vector3 nearLocal )
	{
		return TryReseatCoinSlot( slotIndex, nearLocal, preserveDrawState: false );
	}

	int CountActiveCoinVisualsNear( Vector3 nearLocal, float radius )
	{
		if ( _slots == null )
			return 0;

		float radiusSq = radius * radius;
		int count = 0;
		ForEachSlotIndexNearLocal( nearLocal, radius, slotIndex =>
		{
			Slot slot = _slots[ slotIndex ];
			if ( slot.Taken || slot.Definition == null )
				return;
			if ( !slot.FixedVolumePose && !slot.CoinVisual )
				return;
			if ( !slot.Drawn && !slot.CoinVisual )
				return;
			if ( GetRemaining( slot.Definition ) <= 0 )
				return;

			float dx = slot.LocalPos.x - nearLocal.x;
			float dz = slot.LocalPos.z - nearLocal.z;
			if ( dx * dx + dz * dz > radiusSq )
				return;
			count++;
		} );
		return count;
	}

	/// <summary>
	/// Keep coin visuals near the dig site filled up to min(remaining, type cap, total cap),
	/// and pull a few distant active coins onto the carve so digs always densify visually.
	/// </summary>
	void TryReseatCoinVisualsNear(
		Vector3 nearLocal,
		float searchRadius,
		out bool needsRebuild,
		out bool patchedDrawn )
	{
		needsRebuild = false;
		patchedDrawn = false;
		if ( _slots == null || _definition == null || _remaining == null )
			return;

		_ = searchRadius;
		float digNeighborhood = Mathf.Max( _pickRadius * 2.5f, 1.1f );
		int moveBudget = _coinPullToCarveCount;
		if ( moveBudget > 0 && CountActiveCoinVisualsNear( nearLocal, digNeighborhood ) >= moveBudget )
		{
			// Neighborhood already dense — skip O(N) reseat / excess / pull scans.
			// Consume paths already mark Taken when inventory drops.
			return;
		}

		Dictionary<TreasureDefinition, int> activeByDef = new Dictionary<TreasureDefinition, int>();
		for ( int i = 0; i < _slots.Length; i++ )
		{
			Slot slot = _slots[ i ];
			if ( slot.Taken || slot.Definition == null )
				continue;
			if ( !slot.FixedVolumePose && !slot.CoinVisual )
				continue;
			if ( !slot.Drawn && !slot.CoinVisual )
				continue;
			if ( activeByDef.ContainsKey( slot.Definition ) )
				activeByDef[ slot.Definition ]++;
			else
				activeByDef[ slot.Definition ] = 1;
		}

		// Deactivate excess when remaining drops below active visuals.
		for ( int i = 0; i < _slots.Length; i++ )
		{
			Slot slot = _slots[ i ];
			if ( slot.Taken || slot.Definition == null )
				continue;
			if ( !slot.FixedVolumePose && !slot.CoinVisual )
				continue;
			if ( !slot.Drawn && !slot.CoinVisual )
				continue;

			int remaining = GetRemaining( slot.Definition );
			int active = activeByDef.TryGetValue( slot.Definition, out int n ) ? n : 0;
			if ( active <= remaining )
				continue;

			bool wasDrawn = slot.Drawn;
			slot.Taken = true;
			_slots[ i ] = slot;
			if ( wasDrawn )
				UnmarkDrawn( i );
			RemoveFromCell( i );
			activeByDef[ slot.Definition ] = active - 1;
			// Excess removal is incremental — no full RebuildVisibility.
		}

		int drawnTotal = 0;
		for ( int i = 0; i < _slots.Length; i++ )
		{
			Slot slot = _slots[ i ];
			if ( slot.Taken )
				continue;
			if ( slot.CoinVisual || ( slot.FixedVolumePose && slot.Drawn ) )
				drawnTotal++;
		}

		// Reactivate / reseat free coin seats toward the dig (may use dig buffer seats).
		int digCeiling = GetDrawnCeiling( allowDigBuffer: true );
		TreasurePileEntry[] coinContents = _definition.coinContents;
		if ( coinContents != null )
		{
			for ( int e = 0; e < coinContents.Length; e++ )
			{
				TreasurePileEntry entry = coinContents[ e ];
				if ( entry.treasure == null || entry.treasure.category != TreasureCategory.Coin )
					continue;

				int remaining = GetRemaining( entry.treasure );
				int want = Mathf.Min( remaining, digCeiling );
				int active = activeByDef.TryGetValue( entry.treasure, out int n ) ? n : 0;
				int need = want - active;
				if ( need <= 0 )
					continue;

				for ( int i = 0; i < _slots.Length && need > 0 && moveBudget > 0; i++ )
				{
					Slot slot = _slots[ i ];
					if ( slot.Definition != entry.treasure )
						continue;
					if ( !slot.FixedVolumePose && !slot.CoinVisual )
						continue;
					if ( !slot.Taken )
						continue;
					if ( drawnTotal >= digCeiling )
						break;

					if ( !TryReseatCoinSlot( i, nearLocal, preserveDrawState: false ) )
						continue;

					need--;
					moveBudget--;
					drawnTotal++;
					needsRebuild = true;
				}
			}
		}
	}

	/// <summary>
	/// Unused pull path kept for reference — dig densify tops up seats instead of relocating coins.
	/// </summary>
	int PullDistantCoinsToCarve(
		Vector3 nearLocal,
		float searchRadius,
		int maxPull,
		out bool needsRebuild,
		out bool patchedDrawn )
	{
		needsRebuild = false;
		patchedDrawn = false;
		if ( maxPull <= 0 || _slots == null || _heightfield == null )
			return 0;

		float nearRadius = Mathf.Max( searchRadius, _pickRadius * 2.5f, 1.1f );
		float nearSq = nearRadius * nearRadius;
		// Must be clearly outside the dig neighborhood before we steal them.
		float farMinSq = nearSq * 2.25f;

		int pulled = 0;
		for ( int pull = 0; pull < maxPull; pull++ )
		{
			int bestIndex = -1;
			float bestDistSq = farMinSq;
			bool bestBuried = false;

			for ( int i = 0; i < _slots.Length; i++ )
			{
				Slot slot = _slots[ i ];
				if ( slot.Taken || slot.Definition == null )
					continue;
				if ( !slot.FixedVolumePose && !slot.CoinVisual )
					continue;
				if ( GetRemaining( slot.Definition ) <= 0 )
					continue;

				float dx = slot.LocalPos.x - nearLocal.x;
				float dz = slot.LocalPos.z - nearLocal.z;
				float distSq = dx * dx + dz * dz;
				if ( distSq < farMinSq )
					continue;

				// Prefer buried (not drawn) seats so digs uncover "inside" coins first.
				bool buried = slot.FixedVolumePose && !slot.Drawn;
				if ( bestIndex >= 0 )
				{
					if ( bestBuried && !buried )
						continue;
					if ( bestBuried == buried && distSq < bestDistSq )
						continue;
				}

				bestDistSq = distSq;
				bestIndex = i;
				bestBuried = buried;
			}

			if ( bestIndex < 0 )
				break;

			// Drawn coins: move in place and patch matrices. Undrawn: membership needs rebuild.
			bool preserve = _slots[ bestIndex ].Drawn && _slots[ bestIndex ].MatrixIndex >= 0;
			if ( !TryReseatCoinSlot( bestIndex, nearLocal, preserveDrawState: preserve ) )
				break;

			if ( preserve )
				patchedDrawn = true;
			else
				needsRebuild = true;

			pulled++;
		}

		return pulled;
	}

	void RebuildVisibility()
	{
		if ( _slots == null || _definition == null )
			return;
		if ( _pileRoot == null )
			return;

		GoldPileEditTiming.Begin( "GoldPile.RebuildVisibility" );
		System.Diagnostics.Stopwatch sw = GoldPileEditTiming.StartWatchIfEnabled();

		for ( int i = 0; i < _slots.Length; i++ )
		{
			Slot s = _slots[ i ];
			s.Drawn = false;
			s.MatrixIndex = -1;
			_slots[ i ] = s;
		}

		if ( _visibleCountByEntry != null )
		{
			for ( int i = 0; i < _visibleCountByEntry.Length; i++ )
				_visibleCountByEntry[ i ] = 0;
		}

		for ( int b = 0; b < _batches.Length; b++ )
		{
			if ( !_batches[ b ].IsPrimaryPart || _batches[ b ].SlotIndices == null )
				continue;
			_batches[ b ].SlotIndices.Clear();
		}

		if ( _chunkDrawnSlots != null )
		{
			for ( int i = 0; i < _chunkDrawnSlots.Length; i++ )
				_chunkDrawnSlots[ i ].Clear();
		}

		if ( _drawnCellLists != null )
		{
			for ( int i = 0; i < _drawnCellLists.Length; i++ )
				_drawnCellLists[ i ].Clear();
		}

		List<int> eligible = _eligibleScratch;
		if ( eligible == null )
		{
			eligible = new List<int>( _slots.Length );
			_eligibleScratch = eligible;
		}
		else
			eligible.Clear();

		for ( int i = 0; i < _slots.Length; i++ )
		{
			if ( _slots[ i ].Taken )
				continue;

			// Large props stay surface-visible so they aren't stuck ineligible under gold.
			if ( !_slots[ i ].FixedVolumePose
				&& GetLootStreamCullTier( _slots[ i ].Definition ) >= LootStreamCullTierLarge )
				ForceSlotSurfaceVisible( i );

			if ( IsEligible( _slots[ i ] ) )
				eligible.Add( i );
		}

		if ( _coverageComparison == null )
			_coverageComparison = CompareCoverageScore;
		eligible.Sort( _coverageComparison );

		// Cap applies only to revealed/eligible seats that become Drawn.
		// Buried volume seats are ineligible and do not consume the budget.
		int drawCeiling = GetDrawnCeiling( allowDigBuffer: true );
		int drawnTotal = 0;

		// Volume coins already have authored spacing — draw every eligible seat up to the GPU cap.
		for ( int i = 0; i < eligible.Count && drawnTotal < drawCeiling; i++ )
		{
			int idx = eligible[ i ];
			if ( _slots[ idx ].Drawn )
				continue;

			if ( !_slots[ idx ].FixedVolumePose )
				continue;

			MarkDrawn( idx );
			drawnTotal++;
		}

		// Legacy non-volume seats (if any) fill remaining budget with spacing.
		if ( drawnTotal < drawCeiling )
		{
			int cellCount = spatialCells * spatialCells;
			if ( _cellUsedScratch == null || _cellUsedScratch.Length != cellCount )
				_cellUsedScratch = new bool[ cellCount ];
			else
				System.Array.Clear( _cellUsedScratch, 0, _cellUsedScratch.Length );

			bool[] cellUsed = _cellUsedScratch;
			float spacingSq = _placementMinSpacing * _placementMinSpacing;

			for ( int i = 0; i < eligible.Count && drawnTotal < drawCeiling; i++ )
			{
				int idx = eligible[ i ];
				if ( _slots[ idx ].Drawn || _slots[ idx ].FixedVolumePose )
					continue;

				Slot slot = _slots[ idx ];
				int cell = CellIndex( slot.CellX, slot.CellZ );
				if ( cell >= 0 && cell < cellUsed.Length && cellUsed[ cell ] )
					continue;
				if ( _enforcePlacementSpacing && IsTooCloseToDrawn( slot.LocalPos, spacingSq ) )
					continue;

				MarkDrawn( idx );
				if ( cell >= 0 && cell < cellUsed.Length )
					cellUsed[ cell ] = true;
				drawnTotal++;
			}

			for ( int i = 0; i < eligible.Count && drawnTotal < drawCeiling; i++ )
			{
				int idx = eligible[ i ];
				if ( _slots[ idx ].Drawn || _slots[ idx ].FixedVolumePose )
					continue;

				MarkDrawn( idx );
				drawnTotal++;
			}
		}

		RebuildBatchMatrices();
		InvalidateDrawCache();

		if ( sw != null )
		{
			sw.Stop();
			GoldPileEditTiming.Record(
				"densify.rebuildVisibility",
				sw.Elapsed.TotalMilliseconds,
				$"eligible={eligible.Count} drawn={drawnTotal} ceiling={drawCeiling}" );
		}

		GoldPileEditTiming.End();
	}

	int CompareCoverageScore( int a, int b )
	{
		float sa = CoverageScore( _slots[ a ] );
		float sb = CoverageScore( _slots[ b ] );
		return sa.CompareTo( sb );
	}

	void MoveDrawnSlotChunk( int slotIndex, int oldChunkX, int oldChunkZ, int newChunkX, int newChunkZ )
	{
		if ( _chunkDrawnSlots == null || _chunkGrid.ChunkCount <= 0 )
			return;

		int oldIndex = oldChunkZ * _chunkGrid.CountX + oldChunkX;
		if ( oldIndex >= 0 && oldIndex < _chunkDrawnSlots.Length )
			_chunkDrawnSlots[ oldIndex ].Remove( slotIndex );

		int newIndex = newChunkZ * _chunkGrid.CountX + newChunkX;
		if ( newIndex >= 0 && newIndex < _chunkDrawnSlots.Length )
			_chunkDrawnSlots[ newIndex ].Add( slotIndex );
	}

	void ForceSlotSurfaceVisible( int slotIndex )
	{
		Slot slot = _slots[ slotIndex ];
		slot.EmbedDepth = Mathf.Min( slot.EmbedDepth, _initialRevealDepth * 0.5f );
		ConformSlot( slotIndex, ref slot );
		_slots[ slotIndex ] = slot;
	}

	void ForceSlotIntoBlankFootprint( int slotIndex )
	{
		ForceSlotSurfaceVisible( slotIndex );

		if ( _heightfield == null || _pileRoot == null )
			return;

		Slot slot = _slots[ slotIndex ];
		if ( slot.FixedVolumePose )
			return;

		float spacingSq = _placementMinSpacing * _placementMinSpacing;
		if ( !IsTooCloseToDrawn( slot.LocalPos, spacingSq ) )
		{
			int cell = CellIndex( slot.CellX, slot.CellZ );
			if ( !CellHasDrawn( cell, slotIndex ) )
				return;
		}

		Vector3 near = _hasLastDig ? _lastDigLocal : slot.LocalPos;
		if ( !GoldPileTreasurePlacement.TrySampleSolidCoinSurface(
			_heightfield,
			_pileLootSeed,
			slotIndex,
			near,
			Mathf.Max( _pickRadius * 3f, 1.25f ),
			_placementRadiusFraction,
			_coinSurfaceHeightFraction,
			slot.Scale * 0.5f,
			out Vector3 candidate,
			out float surface ) )
		{
			if ( !TrySampleCoinFootprintFallback( slotIndex, slot.Scale * 0.5f, out candidate, out surface ) )
				return;
		}

		if ( IsTooCloseToDrawn( candidate, spacingSq ) )
			return;

		RemoveFromCell( slotIndex );
		slot.LocalPos = candidate;
		slot.PlaceSurfaceHeight = surface;
		slot.EmbedDepth = Mathf.Min( slot.EmbedDepth, _initialRevealDepth * 0.5f );
		ConformSlot( slotIndex, ref slot );
		_slots[ slotIndex ] = slot;
		AssignCell( slotIndex );
	}

	bool CellHasDrawn( int cell, int ignoreSlot )
	{
		if ( cell < 0 || cell >= _cellLists.Length )
			return false;

		List<int> list = _cellLists[ cell ];
		for ( int i = 0; i < list.Count; i++ )
		{
			int idx = list[ i ];
			if ( idx == ignoreSlot )
				continue;
			if ( _slots[ idx ].Drawn )
				return true;
		}

		return false;
	}

	bool IsTooCloseToDrawn( Vector3 localPos, float spacingSq )
	{
		if ( !_enforcePlacementSpacing )
			return false;

		List<int>[] lists = _drawnCellLists != null ? _drawnCellLists : _cellLists;
		if ( lists == null || _heightfield == null )
			return false;

		float cellSize = _heightfield.WorldSize / spatialCells;
		int radius = Mathf.Max( 1, Mathf.CeilToInt( _placementMinSpacing / Mathf.Max( 0.01f, cellSize ) ) );
		int cx = LocalToCell( localPos.x );
		int cz = LocalToCell( localPos.z );
		bool filterDrawn = lists == _cellLists;
		for ( int oz = -radius; oz <= radius; oz++ )
		{
			int zz = cz + oz;
			if ( zz < 0 || zz >= spatialCells )
				continue;
			for ( int ox = -radius; ox <= radius; ox++ )
			{
				int xx = cx + ox;
				if ( xx < 0 || xx >= spatialCells )
					continue;

				List<int> cell = lists[ CellIndex( xx, zz ) ];
				for ( int i = 0; i < cell.Count; i++ )
				{
					Slot other = _slots[ cell[ i ] ];
					if ( filterDrawn && !other.Drawn )
						continue;
					float dx = other.LocalPos.x - localPos.x;
					float dz = other.LocalPos.z - localPos.z;
					if ( dx * dx + dz * dz < spacingSq )
						return true;
				}
			}
		}

		return false;
	}

	bool IsEligible( Slot slot )
	{
		if ( _heightfield == null )
			return false;

		if ( slot.Definition != null && slot.Definition.category == TreasureCategory.Coin )
		{
			// Frozen until release threshold — keep drawing floating seats; hide only while fully buried.
			if ( ShouldReleaseCoinSeat( slot ) )
				return false;

			if ( slot.FixedVolumePose && !TouchesOutside( slot ) )
				return false;

			return true;
		}

		if ( slot.FixedVolumePose )
		{
			// Buried volume seats: reveal when any probe is outside the mound.
			float sampledSurface = _heightfield.SampleNormalized( slot.LocalPos.x, slot.LocalPos.z )
				* _heightfield.MaxHeight;
			float ground = _heightfield.GroundLevel;
			float surfaceOrGround = sampledSurface < ground ? ground : sampledSurface;

			float probe = Mathf.Max( 0.02f, slot.ProbeRadius );
			if ( slot.LocalPos.y + probe <= surfaceOrGround )
				return false;

			// Despawn when the column is gone or the seat floats above the remaining surface.
			if ( sampledSurface < ground )
				return false;

			float floatEps = Mathf.Max( 0.02f, slot.Scale * 0.08f );
			return slot.LocalPos.y <= sampledSurface + floatEps;
		}

		if ( slot.CoinVisual )
		{
			float currentSurface = _heightfield.SampleNormalized( slot.LocalPos.x, slot.LocalPos.z )
				* _heightfield.MaxHeight;
			if ( currentSurface < _heightfield.GroundLevel )
				return false;

			float floatEps = Mathf.Max( 0.02f, slot.Scale * 0.08f );
			return slot.LocalPos.y <= currentSurface + floatEps;
		}

		if ( !_heightfield.ExistsAtLocal( slot.LocalPos.x, slot.LocalPos.z ) )
			return false;

		if ( slot.EmbedDepth <= _initialRevealDepth )
			return true;

		float current = _heightfield.SampleNormalized( slot.LocalPos.x, slot.LocalPos.z ) * _heightfield.MaxHeight;
		float carved = slot.PlaceSurfaceHeight - current;
		float needed = slot.EmbedDepth - _initialRevealDepth;
		return carved >= needed;
	}

	float CoverageScore( Slot slot )
	{
		float radial = new Vector2( slot.LocalPos.x, slot.LocalPos.z ).magnitude;
		return slot.EmbedDepth * 10f + radial * 0.01f;
	}

	void MarkDrawn( int slotIndex )
	{
		Slot slot = _slots[ slotIndex ];
		slot.Drawn = true;
		_slots[ slotIndex ] = slot;
		_visibleCountByEntry[ slot.EntryIndex ]++;
		_batches[ slot.BatchKey ].SlotIndices.Add( slotIndex );

		if ( _chunkDrawnSlots != null && _chunkGrid.ChunkCount > 0 )
		{
			int chunkIndex = slot.ChunkZ * _chunkGrid.CountX + slot.ChunkX;
			if ( chunkIndex >= 0 && chunkIndex < _chunkDrawnSlots.Length )
				_chunkDrawnSlots[ chunkIndex ].Add( slotIndex );
		}

		AddToDrawnCell( slotIndex, slot.CellX, slot.CellZ );
	}

	/// <summary>
	/// Remove a drawn slot from GPU batches without a full RebuildVisibility.
	/// </summary>
	void UnmarkDrawn( int slotIndex )
	{
		if ( _slots == null || slotIndex < 0 || slotIndex >= _slots.Length )
			return;

		Slot slot = _slots[ slotIndex ];
		if ( !slot.Drawn )
			return;

		RemoveFromDrawnCell( slotIndex, slot.CellX, slot.CellZ );

		if ( _chunkDrawnSlots != null && _chunkGrid.ChunkCount > 0 )
		{
			int chunkIndex = slot.ChunkZ * _chunkGrid.CountX + slot.ChunkX;
			if ( chunkIndex >= 0 && chunkIndex < _chunkDrawnSlots.Length )
				_chunkDrawnSlots[ chunkIndex ].Remove( slotIndex );
		}

		if ( _visibleCountByEntry != null
			&& slot.EntryIndex >= 0
			&& slot.EntryIndex < _visibleCountByEntry.Length
			&& _visibleCountByEntry[ slot.EntryIndex ] > 0 )
		{
			_visibleCountByEntry[ slot.EntryIndex ]--;
		}

		if ( _batches != null
			&& slot.BatchKey >= 0
			&& slot.BatchKey < _batches.Length
			&& _batches[ slot.BatchKey ].SlotIndices != null )
		{
			List<int> sharedSlots = _batches[ slot.BatchKey ].SlotIndices;
			int matrixIndex = slot.MatrixIndex;
			if ( matrixIndex >= 0
				&& matrixIndex < sharedSlots.Count
				&& sharedSlots[ matrixIndex ] == slotIndex )
			{
				int last = sharedSlots.Count - 1;
				if ( matrixIndex != last )
				{
					int moved = sharedSlots[ last ];
					sharedSlots[ matrixIndex ] = moved;
					Slot movedSlot = _slots[ moved ];
					movedSlot.MatrixIndex = matrixIndex;
					_slots[ moved ] = movedSlot;

					for ( int b = 0; b < _batches.Length; b++ )
					{
						BatchGroup group = _batches[ b ];
						if ( group.SlotIndices != sharedSlots )
							continue;
						if ( group.Matrices != null
							&& last < group.Matrices.Length
							&& matrixIndex < group.Matrices.Length )
						{
							group.Matrices[ matrixIndex ] = group.Matrices[ last ];
							_batches[ b ] = group;
						}
					}
				}

				sharedSlots.RemoveAt( last );
			}
			else
			{
				sharedSlots.Remove( slotIndex );
			}
		}

		slot.Drawn = false;
		slot.MatrixIndex = -1;
		_slots[ slotIndex ] = slot;
		InvalidateDrawCache();
	}

	void AddToDrawnCell( int slotIndex, int cellX, int cellZ )
	{
		if ( _drawnCellLists == null )
			return;

		int cell = CellIndex( cellX, cellZ );
		if ( cell < 0 || cell >= _drawnCellLists.Length )
			return;

		List<int> list = _drawnCellLists[ cell ];
		if ( list != null && !list.Contains( slotIndex ) )
			list.Add( slotIndex );
	}

	void RemoveFromDrawnCell( int slotIndex, int cellX, int cellZ )
	{
		if ( _drawnCellLists == null )
			return;

		int cell = CellIndex( cellX, cellZ );
		if ( cell < 0 || cell >= _drawnCellLists.Length )
			return;

		_drawnCellLists[ cell ].Remove( slotIndex );
	}

	void MoveDrawnSlotCell( int slotIndex, int oldCellX, int oldCellZ, int newCellX, int newCellZ )
	{
		RemoveFromDrawnCell( slotIndex, oldCellX, oldCellZ );
		AddToDrawnCell( slotIndex, newCellX, newCellZ );
	}

	void RebuildBatchMatrices()
	{
		if ( _pileRoot == null || _batches == null )
			return;

		for ( int b = 0; b < _batches.Length; b++ )
		{
			BatchGroup group = _batches[ b ];
			int count = group.SlotIndices.Count;
			if ( group.Matrices == null || group.Matrices.Length < count )
				group.Matrices = new Matrix4x4[ count ];

			for ( int i = 0; i < count; i++ )
			{
				int slotIndex = group.SlotIndices[ i ];
				Slot slot = _slots[ slotIndex ];
				group.Matrices[ i ] = BuildMatrix( slot ) * group.PartLocal;
				if ( group.IsPrimaryPart )
				{
					slot.MatrixIndex = i;
					_slots[ slotIndex ] = slot;
				}
			}

			_batches[ b ] = group;
		}
	}

	void PatchDrawnSlotMatrix( int slotIndex )
	{
		if ( _batches == null || _pileRoot == null )
			return;

		Slot slot = _slots[ slotIndex ];
		if ( !slot.Drawn || slot.MatrixIndex < 0 )
			return;

		if ( slot.BatchKey < 0 || slot.BatchKey >= _batches.Length )
			return;

		List<int> sharedSlots = _batches[ slot.BatchKey ].SlotIndices;
		Matrix4x4 rootMatrix = BuildMatrix( slot );
		for ( int b = 0; b < _batches.Length; b++ )
		{
			BatchGroup group = _batches[ b ];
			if ( group.SlotIndices != sharedSlots )
				continue;
			if ( group.Matrices == null || slot.MatrixIndex >= group.Matrices.Length )
				continue;

			group.Matrices[ slot.MatrixIndex ] = rootMatrix * group.PartLocal;
			_batches[ b ] = group;
		}
	}

	void ConformSlot( int slotIndex, ref Slot slot )
	{
		if ( slot.FixedVolumePose || _heightfield == null )
			return;

		if ( slot.CoinVisual )
		{
			Bounds meshBounds = GetCoinMeshBounds( slot.Definition );
			if ( !GoldPileTreasurePlacement.ConformCoinToPileSurface(
				_heightfield,
				_pileLootSeed,
				slotIndex,
				ref slot.LocalPos,
				ref slot.LocalRot,
				slot.Scale,
				meshBounds,
				slot.EmbedDepth,
				_coinSurfaceHeightFraction ) )
			{
				return;
			}

			slot.PlaceSurfaceHeight = _heightfield.SampleNormalized(
				slot.LocalPos.x,
				slot.LocalPos.z ) * _heightfield.MaxHeight;
			return;
		}

		float surface = _heightfield.SampleNormalized( slot.LocalPos.x, slot.LocalPos.z ) * _heightfield.MaxHeight;
		bool large = GetLootStreamCullTier( slot.Definition ) >= LootStreamCullTierLarge;
		float embed = large ? Mathf.Min( slot.EmbedDepth, _initialRevealDepth * 0.35f ) : slot.EmbedDepth;
		float lift = large ? Mathf.Max( 0.08f, slot.Scale * 0.2f ) : slot.Scale * 0.05f;
		slot.LocalPos.y = surface - embed + lift;
		slot.PlaceSurfaceHeight = surface;
	}

	Mesh GetCoinMesh( TreasureDefinition definition )
	{
		if ( definition == null )
			return fallbackMesh;
		if ( definition.meshOverride != null )
			return definition.meshOverride;
		return fallbackMesh;
	}

	Bounds GetCoinMeshBounds( TreasureDefinition definition )
	{
		if ( definition != null
			&& _coinMeshBoundsByDef != null
			&& _coinMeshBoundsByDef.TryGetValue( definition, out Bounds cached ) )
		{
			return cached;
		}

		Mesh mesh = GetCoinMesh( definition );
		if ( mesh != null )
			return mesh.bounds;

		return new Bounds( Vector3.zero, Vector3.one * 0.2f );
	}

	void CacheCoinMeshBounds( TreasureDefinition definition, VisualAssets visual )
	{
		if ( definition == null || _coinMeshBoundsByDef == null || _coinMeshBoundsByDef.ContainsKey( definition ) )
			return;

		Bounds combined = default;
		bool hasBounds = false;
		if ( visual.Parts != null )
		{
			for ( int p = 0; p < visual.Parts.Length; p++ )
			{
				Mesh mesh = visual.Parts[ p ].Mesh;
				if ( mesh == null )
					continue;

				Bounds partBounds = mesh.bounds;
				Matrix4x4 local = visual.Parts[ p ].LocalToRoot;
				Vector3 center = local.MultiplyPoint( partBounds.center );
				Vector3 extents = partBounds.extents;
				Vector3 axisX = local.MultiplyVector( Vector3.right * extents.x );
				Vector3 axisY = local.MultiplyVector( Vector3.up * extents.y );
				Vector3 axisZ = local.MultiplyVector( Vector3.forward * extents.z );
				Vector3 worldExtents = new Vector3(
					Mathf.Abs( axisX.x ) + Mathf.Abs( axisY.x ) + Mathf.Abs( axisZ.x ),
					Mathf.Abs( axisX.y ) + Mathf.Abs( axisY.y ) + Mathf.Abs( axisZ.y ),
					Mathf.Abs( axisX.z ) + Mathf.Abs( axisY.z ) + Mathf.Abs( axisZ.z ) );
				Bounds transformed = new Bounds( center, worldExtents * 2f );
				if ( !hasBounds )
				{
					combined = transformed;
					hasBounds = true;
				}
				else
				{
					combined.Encapsulate( transformed.min );
					combined.Encapsulate( transformed.max );
				}
			}
		}

		if ( !hasBounds )
		{
			Mesh mesh = GetCoinMesh( definition );
			combined = mesh != null ? mesh.bounds : new Bounds( Vector3.zero, Vector3.one * 0.2f );
		}

		_coinMeshBoundsByDef[ definition ] = combined;
	}

	delegate void SlotIndexVisitor( int slotIndex );

	void ForEachSlotIndexNearLocal( Vector3 local, float radius, SlotIndexVisitor visitor )
	{
		if ( visitor == null || _slots == null )
			return;

		float radiusSq = radius * radius;
		if ( _cellLists == null || _heightfield == null )
		{
			for ( int i = 0; i < _slots.Length; i++ )
			{
				float dx = _slots[ i ].LocalPos.x - local.x;
				float dz = _slots[ i ].LocalPos.z - local.z;
				if ( dx * dx + dz * dz > radiusSq )
					continue;
				visitor( i );
			}
			return;
		}

		float cellSize = _heightfield.WorldSize / Mathf.Max( 1, spatialCells );
		int cellRadius = Mathf.Max( 1, Mathf.CeilToInt( radius / Mathf.Max( 0.01f, cellSize ) ) );
		int cx = LocalToCell( local.x );
		int cz = LocalToCell( local.z );
		for ( int oz = -cellRadius; oz <= cellRadius; oz++ )
		{
			int zz = cz + oz;
			if ( zz < 0 || zz >= spatialCells )
				continue;
			for ( int ox = -cellRadius; ox <= cellRadius; ox++ )
			{
				int xx = cx + ox;
				if ( xx < 0 || xx >= spatialCells )
					continue;

				List<int> cell = _cellLists[ CellIndex( xx, zz ) ];
				if ( cell == null )
					continue;
				for ( int i = 0; i < cell.Count; i++ )
				{
					int slotIndex = cell[ i ];
					if ( slotIndex < 0 || slotIndex >= _slots.Length )
						continue;
					float dx = _slots[ slotIndex ].LocalPos.x - local.x;
					float dz = _slots[ slotIndex ].LocalPos.z - local.z;
					if ( dx * dx + dz * dz > radiusSq )
						continue;
					visitor( slotIndex );
				}
			}
		}
	}

	Matrix4x4 BuildMatrix( Slot slot )
	{
		if ( _pileRoot == null )
			return Matrix4x4.identity;

		Vector3 worldPos = _pileRoot.TransformPoint( slot.LocalPos );
		Quaternion worldRot = _pileRoot.rotation * slot.LocalRot;
		return Matrix4x4.TRS( worldPos, worldRot, Vector3.one * slot.Scale );
	}

	void AssignCell( int slotIndex )
	{
		Slot slot = _slots[ slotIndex ];
		slot.CellX = LocalToCell( slot.LocalPos.x );
		slot.CellZ = LocalToCell( slot.LocalPos.z );
		_chunkGrid.LocalToChunk( slot.LocalPos.x, slot.LocalPos.z, out slot.ChunkX, out slot.ChunkZ );
		_slots[ slotIndex ] = slot;
		_cellLists[ CellIndex( slot.CellX, slot.CellZ ) ].Add( slotIndex );
	}

	void RemoveFromCell( int slotIndex )
	{
		Slot slot = _slots[ slotIndex ];
		int cell = CellIndex( slot.CellX, slot.CellZ );
		if ( cell < 0 || cell >= _cellLists.Length )
			return;
		_cellLists[ cell ].Remove( slotIndex );
	}

	int LocalToCell( float local )
	{
		float half = _heightfield.WorldSize * 0.5f;
		float u = ( local + half ) / _heightfield.WorldSize;
		int c = Mathf.FloorToInt( Mathf.Clamp01( u ) * spatialCells );
		return Mathf.Clamp( c, 0, spatialCells - 1 );
	}

	int CellIndex( int cx, int cz )
	{
		return cz * spatialCells + cx;
	}

}
