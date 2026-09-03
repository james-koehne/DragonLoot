using System.Collections.Generic;
using System.Threading.Tasks;

using UnityEngine;

/// <summary>
/// Real MeshRenderer treasure objects for large pile props (artifacts, crowns, gems, etc.).
/// Latent seeded volume poses spawn when bounds touch outside the mound and are in stream range;
/// out-of-range props lazy-despawn back to latent poses. Once stolen they leave the pile.
/// </summary>
[DisallowMultipleComponent]
public class GoldPileArtifactProps : MonoBehaviour
{
	const int MaxStreamSpawnsPerFrame = 3;
	const int MaxStreamDespawnsPerFrame = 3;
	const int MaxStreamLatentChecksPerFrame = 64;
	const float StreamPlayerMoveEpsilon = 0.25f;
	const float StreamPlayerMoveEpsilonSqr = StreamPlayerMoveEpsilon * StreamPlayerMoveEpsilon;

	struct LatentEntry
	{
		public TreasureDefinition Definition;
		public Vector3 LocalPos;
		public Quaternion LocalRot;
		public float Scale;
		public Bounds LocalBounds;
		public bool Taken;
		public bool Spawned;
		public bool Exposed;
		public bool IsAuthored;
		public int PropIndex;
		public bool LoggedMissingSpawn;
	}

	public struct LatentFillReport
	{
		public int Expected;
		public int Placed;
		public string PerType;

		public bool Complete => Placed >= Expected;
	}

	struct PropEntry
	{
		public TreasureItem Item;
		public TreasureDefinition Definition;
		public Vector3 LocalPos;
		public Quaternion LocalRot;
		public int LatentIndex;
		public int ChunkX;
		public int ChunkZ;
		public bool RenderVisible;
		public bool Pickable;
		public Bounds WorldBounds;
		public Collider CachedCollider;
	}

	readonly List<LatentEntry> _latent = new List<LatentEntry>( 64 );
	readonly List<PropEntry> _props = new List<PropEntry>( 64 );
	readonly Dictionary<TreasureDefinition, int> _visibleByDef = new Dictionary<TreasureDefinition, int>();

	TreasurePileVisual _owner;
	TreasurePileDefinition _definition;
	GoldPileHeightfield _heightfield;
	Transform _pileRoot;
	GoldPileLootInstances _loot;
	GoldPileLootStreamSettings _streamSettings;
	Camera _cachedCamera;
	int _bindSerial;
	int _revealGeneration;
	float _placementRadiusFraction = 1f;
	float _placementMinSpacing = 0.35f;
	float _treasureRadialPower = 1.25f;
	float _treasureHeightBias = 0.75f;
	float _treasureXZSpread = 1f;
	int _latentSurfaceNeighborhoodCells = 1;
	float _treasurePickupOutsideFraction = 0.4f;
	float _treasureReleaseOutsideFraction = 0.9f;
	int _pileLootSeed = 1;
	bool _pendingReveal;
	Vector3 _pendingRevealWorld;
	float _pendingRevealRadius;
	int _revealCursor;
	int _streamSpawnBudget;
	int _streamDespawnBudget;
	int _streamLatentCursor;
	bool _streamResidencyDirty;
	bool _hasLastStreamPlayerPos;
	Vector3 _lastStreamPlayerPos;
	readonly List<TreasureItem> _pendingWorldReleases = new List<TreasureItem>( 8 );
	readonly List<TreasurePileAuthoredItem> _authoredScratch = new List<TreasurePileAuthoredItem>( 16 );
	readonly List<GameObject> _disabledAuthoredProxies = new List<GameObject>( 16 );

	public int PropCount => _props.Count;
	public int LatentCount => _latent.Count;
	public int ExposedLatentCount { get; private set; }
	public int StreamedOutCount { get; private set; }
	public int LivePropCount => _props.Count;

	public void Bind(
		TreasurePileVisual owner,
		TreasurePileDefinition definition,
		GoldPileHeightfield heightfield,
		Transform pileRoot,
		GoldPileLootInstances loot,
		GoldPileLootStreamSettings streamSettings,
		int authoredLayoutSeed = 0 )
	{
		int bindId = BeginBind( owner, definition, heightfield, pileRoot, loot, streamSettings, authoredLayoutSeed );
		LatentBindSettings settings = ResolveLatentBindSettings();
		CreateOccupancy( settings, out List<Bounds> occupiedBounds, out VolumeOccupancyGrid occupancyGrid );
		AppendAuthoredLatentsFromScene( disableProxies: Application.isPlaying, occupiedBounds, occupancyGrid );

		bool appliedBake = false;
		if ( settings.PreferBake )
			appliedBake = TryAppendBakedLatents( settings );

		if ( !appliedBake )
			PlaceDefinitionContents( settings, occupiedBounds, occupancyGrid );

		StartReveal( bindId, spatialFilter: false, default, 0f, startIndex: 0 );
	}

	public async Task BindAsync(
		TreasurePileVisual owner,
		TreasurePileDefinition definition,
		GoldPileHeightfield heightfield,
		Transform pileRoot,
		GoldPileLootInstances loot,
		GoldPileLootStreamSettings streamSettings,
		int authoredLayoutSeed = 0 )
	{
		int bindId = BeginBind( owner, definition, heightfield, pileRoot, loot, streamSettings, authoredLayoutSeed );

		LatentBindSettings settings = ResolveLatentBindSettings();
		CreateOccupancy( settings, out List<Bounds> occupiedBounds, out VolumeOccupancyGrid occupancyGrid );
		AppendAuthoredLatentsFromScene( disableProxies: Application.isPlaying, occupiedBounds, occupancyGrid );

		bool appliedBake = false;
		if ( settings.PreferBake )
			appliedBake = TryAppendBakedLatents( settings );

		if ( !appliedBake )
		{
			if ( Application.isPlaying )
				await PlaceDefinitionContentsAsync( bindId, settings, occupiedBounds, occupancyGrid );
			else
				PlaceDefinitionContents( settings, occupiedBounds, occupancyGrid );
		}

		if ( !IsBindStillValid( bindId ) )
			return;

		StartReveal( bindId, spatialFilter: false, default, 0f, startIndex: 0 );
	}

	/// <summary>
	/// Editor-only: procedural remainder latent build into a pure-data bake asset (no reveal / no scene props).
	/// Authored curated props stay on scene objects; only auto-fill poses are written to the bake.
	/// </summary>
	public LatentFillReport BakeLatentsInto(
		TreasurePileVisual owner,
		TreasurePileDefinition definition,
		GoldPileHeightfield heightfield,
		Transform pileRoot,
		GoldPileLootInstances loot,
		int authoredLayoutSeed,
		TreasurePileLatentBake bake )
	{
		if ( bake == null || definition == null || heightfield == null )
			return default;

		BeginBind( owner, definition, heightfield, pileRoot, loot, null, authoredLayoutSeed );
		LatentBindSettings settings = ResolveLatentBindSettings();
		CreateOccupancy( settings, out List<Bounds> occupiedBounds, out VolumeOccupancyGrid occupancyGrid );
		SeedOccupancyFromAuthoredScene( occupiedBounds, occupancyGrid );
		LatentFillReport report = PlaceDefinitionContents( settings, occupiedBounds, occupancyGrid );
		WriteBakeAsset( bake, authoredLayoutSeed );
		ClearAll();
		return report;
	}

	int BeginBind(
		TreasurePileVisual owner,
		TreasurePileDefinition definition,
		GoldPileHeightfield heightfield,
		Transform pileRoot,
		GoldPileLootInstances loot,
		GoldPileLootStreamSettings streamSettings,
		int authoredLayoutSeed )
	{
		int bindId = ++_bindSerial;
		_revealGeneration++;
		ClearAll();

		_owner = owner;
		_definition = definition;
		_heightfield = heightfield;
		_pileRoot = pileRoot != null ? pileRoot : transform;
		_loot = loot;
		_cachedCamera = null;
		_streamSettings = streamSettings;
		_streamSpawnBudget = 0;
		_streamDespawnBudget = 0;
		_streamLatentCursor = 0;
		_streamResidencyDirty = true;
		_hasLastStreamPlayerPos = false;

		if ( definition != null )
		{
			_placementRadiusFraction = Mathf.Clamp( definition.placementRadiusFraction, 0.4f, 1f );
			_placementMinSpacing = Mathf.Max( 0.05f, definition.placementMinSpacing );
			_treasureRadialPower = Mathf.Clamp( definition.treasureRadialPower, 0.25f, 3f );
			_treasureHeightBias = Mathf.Clamp( definition.treasureHeightBias, 0f, 3f );
			_treasureXZSpread = definition.treasureXZSpread < 0.25f
				? 1f
				: Mathf.Clamp( definition.treasureXZSpread, 0.25f, 3f );
			_latentSurfaceNeighborhoodCells = definition.ResolveLatentSurfaceNeighborhoodCells();
			_treasurePickupOutsideFraction = Mathf.Clamp( definition.treasurePickupOutsideFraction, 0.05f, 0.95f );
			_treasureReleaseOutsideFraction = Mathf.Clamp( definition.treasureReleaseOutsideFraction, 0.5f, 1f );
		}

		_pileLootSeed = WorldLootSeed.GetPileEffectiveSeed( _pileRoot, authoredLayoutSeed );
		return bindId;
	}

	bool IsBindStillValid( int bindId )
	{
		return this != null && bindId == _bindSerial;
	}

	public bool Contains( TreasureItem item )
	{
		return FindPropIndex( item ) >= 0;
	}

	public bool TryBeginSteal( TreasureItem item )
	{
		int index = FindPropIndex( item );
		if ( index < 0 )
			return false;
		return _props[ index ].Pickable;
	}

	public void CancelSteal( TreasureItem item )
	{
		if ( item == null || FindPropIndex( item ) >= 0 )
			return;

		if ( item.Definition == null || !IsLargeProp( item.Definition ) )
			return;

		SeatExisting( item, item.transform.position );
	}

	public bool CompleteSteal( TreasureItem item )
	{
		if ( !DetachStolenProp( item ) )
			return false;

		StartReveal( _bindSerial, spatialFilter: false, default, 0f, startIndex: 0 );
		return true;
	}

	/// <summary>
	/// Removes the prop from pile ownership and consumes inventory without queuing a reveal pass.
	/// </summary>
	bool DetachStolenProp( TreasureItem item )
	{
		int index = FindPropIndex( item );
		if ( index < 0 )
			return false;

		PropEntry prop = _props[ index ];
		TreasureDefinition def = prop.Definition;
		int latentIndex = prop.LatentIndex;
		RemovePropSwap( index );

		if ( def != null )
		{
			int visible = GetVisibleCount( def );
			if ( visible > 0 )
				_visibleByDef[ def ] = visible - 1;
		}

		if ( latentIndex >= 0 && latentIndex < _latent.Count )
		{
			LatentEntry latent = _latent[ latentIndex ];
			latent.Taken = true;
			latent.Spawned = false;
			latent.PropIndex = -1;
			_latent[ latentIndex ] = latent;
		}

		if ( _loot != null && def != null )
			_loot.ConsumeFallbackDefinition( def );

		return true;
	}

	public bool TrySeatDeposit( TreasureDefinition definition, Vector3 preferredWorldPos, out Vector3 worldPos, out Quaternion worldRot, out bool becameVisible )
	{
		worldPos = preferredWorldPos;
		worldRot = Quaternion.identity;
		becameVisible = false;
		if ( definition == null || !IsLargeProp( definition ) || _pileRoot == null || _heightfield == null )
			return false;

		if ( _loot != null )
			_loot.AddRemaining( definition );

		int latentIndex = FindUnusedLatent( definition );
		if ( latentIndex < 0 )
		{
			latentIndex = AppendLatentFromWorld( definition, preferredWorldPos );
			if ( latentIndex < 0 )
				return true;
		}

		LatentEntry latent = _latent[ latentIndex ];
		latent.Taken = false;
		_latent[ latentIndex ] = latent;

		worldPos = _pileRoot.TransformPoint( latent.LocalPos );
		worldRot = _pileRoot.rotation * latent.LocalRot;

		float outside = GoldPileTreasurePlacement.TouchesOutsideAabb( _heightfield, latent.LocalBounds ) ? 1f : 0f;
		if ( outside <= 0f )
			return true;

		becameVisible = true;
		_ = SeatLatentAsync( latentIndex, null );
		return true;
	}

	public bool TryAbsorb( TreasureItem item, Vector3 worldPos )
	{
		if ( item == null || item.Definition == null || !IsLargeProp( item.Definition ) )
			return false;

		if ( _loot != null )
			_loot.AddRemaining( item.Definition );

		int latentIndex = FindUnusedLatent( item.Definition );
		if ( latentIndex < 0 )
			latentIndex = AppendLatentFromWorld( item.Definition, worldPos );

		if ( latentIndex < 0 )
		{
			TreasureItemFactory.Despawn( item );
			return true;
		}

		UpdateLatentPoseFromWorld( latentIndex, worldPos, item.transform.rotation );

		LatentEntry latent = _latent[ latentIndex ];
		latent.Taken = false;
		_latent[ latentIndex ] = latent;

		float outside = GoldPileTreasurePlacement.TouchesOutsideAabb( _heightfield, latent.LocalBounds ) ? 1f : 0f;
		if ( outside <= 0f || !IsLatentInStreamRange( latentIndex, currentlyResident: false ) )
		{
			TreasureItemFactory.Despawn( item );
			return true;
		}

		_ = SeatLatentAsync( latentIndex, item );
		return true;
	}

	/// <summary>
	/// World-cap reclaim: return a loose gem/artifact into the pile at a new pose near <paramref name="worldPos"/>.
	/// Always succeeds (no visibility caps). Despawns the live item into latent.
	/// </summary>
	public bool AbsorbReclaim( TreasureItem item, Vector3 worldPos )
	{
		if ( item == null || item.Definition == null || !IsLargeProp( item.Definition ) )
			return false;

		WorldTreasurePersistence.CancelParkForItem( item );

		if ( _loot != null )
			_loot.AddRemaining( item.Definition );

		int latentIndex = FindUnusedLatent( item.Definition );
		if ( latentIndex < 0 )
			latentIndex = AppendLatentFromWorld( item.Definition, worldPos );

		if ( latentIndex < 0 )
		{
			TreasureItemFactory.Despawn( item );
			return true;
		}

		UpdateLatentPoseFromWorld( latentIndex, worldPos, item.transform.rotation );
		LatentEntry latent = _latent[ latentIndex ];
		latent.Taken = false;
		latent.Spawned = false;
		latent.PropIndex = -1;
		latent.Exposed = _heightfield != null
			&& GoldPileTreasurePlacement.TouchesOutsideAabb( _heightfield, latent.LocalBounds );
		_latent[ latentIndex ] = latent;

		TreasureItemFactory.Despawn( item );
		return true;
	}

	/// <summary>
	/// World-cap reclaim of a parked (already despawned) prop: seat as latent at <paramref name="worldPos"/>.
	/// </summary>
	public bool AbsorbParkedLatent( TreasureDefinition definition, Vector3 worldPos )
	{
		if ( definition == null || !IsLargeProp( definition ) )
			return false;

		if ( _loot != null )
			_loot.AddRemaining( definition );

		int latentIndex = FindUnusedLatent( definition );
		if ( latentIndex < 0 )
			latentIndex = AppendLatentFromWorld( definition, worldPos );

		if ( latentIndex < 0 )
			return true;

		UpdateLatentPoseFromWorld( latentIndex, worldPos, Quaternion.identity );
		LatentEntry latent = _latent[ latentIndex ];
		latent.Taken = false;
		latent.Spawned = false;
		latent.PropIndex = -1;
		latent.Exposed = _heightfield != null
			&& GoldPileTreasurePlacement.TouchesOutsideAabb( _heightfield, latent.LocalBounds );
		_latent[ latentIndex ] = latent;
		return true;
	}

	/// <summary>
	/// Debug: seat one of each definition inside the mound volume and force-spawn live props
	/// (ignores carve exposure and stream distance). Returns how many seats were started.
	/// </summary>
	public void DebugSpawnDefinitionsInside(
		IReadOnlyList<TreasureDefinition> definitions,
		System.Action<int, int> onComplete = null )
	{
		_ = DebugSpawnDefinitionsInsideAsync( definitions, onComplete );
	}

	/// <summary>
	/// Debug: force-spawn every untaken latent gem/artifact already authored in this pile.
	/// </summary>
	public void DebugForceSpawnExistingInside( System.Action<int, int> onComplete = null )
	{
		_ = DebugForceSpawnExistingInsideAsync( onComplete );
	}

	async Task DebugSpawnDefinitionsInsideAsync(
		IReadOnlyList<TreasureDefinition> definitions,
		System.Action<int, int> onComplete )
	{
		int attempted = 0;
		int spawned = 0;
		if ( definitions == null || _heightfield == null || _pileRoot == null )
		{
			if ( onComplete != null )
				onComplete( spawned, attempted );
			return;
		}

		int bindId = _bindSerial;
		for ( int i = 0; i < definitions.Count; i++ )
		{
			if ( this == null || bindId != _bindSerial )
				break;

			TreasureDefinition def = definitions[ i ];
			if ( def == null || !IsLargeProp( def ) )
				continue;
			if ( def.category != TreasureCategory.Gem && def.category != TreasureCategory.Artifact )
				continue;

			attempted++;
			int latentIndex = AppendLatentVolumeSample( def, unitSalt: attempted * 97 + 11 );
			if ( latentIndex < 0 )
				continue;

			if ( _loot != null )
				_loot.AddRemaining( def );

			LatentEntry latent = _latent[ latentIndex ];
			latent.Exposed = true;
			_latent[ latentIndex ] = latent;

			await SeatLatentAsync( latentIndex, null );
			if ( this == null || bindId != _bindSerial )
				break;

			if ( latentIndex < _latent.Count && _latent[ latentIndex ].Spawned )
				spawned++;
		}

		_streamResidencyDirty = true;
		if ( onComplete != null )
			onComplete( spawned, attempted );
	}

	async Task DebugForceSpawnExistingInsideAsync( System.Action<int, int> onComplete )
	{
		int attempted = 0;
		int spawned = 0;
		if ( _heightfield == null || _pileRoot == null )
		{
			if ( onComplete != null )
				onComplete( spawned, attempted );
			return;
		}

		int bindId = _bindSerial;
		for ( int i = 0; i < _latent.Count; i++ )
		{
			if ( this == null || bindId != _bindSerial )
				break;

			LatentEntry latent = _latent[ i ];
			if ( latent.Taken || latent.Definition == null || latent.Spawned )
				continue;

			TreasureCategory cat = latent.Definition.category;
			if ( cat != TreasureCategory.Gem && cat != TreasureCategory.Artifact )
				continue;

			attempted++;
			latent.Exposed = true;
			_latent[ i ] = latent;
			await SeatLatentAsync( i, null );
			if ( this == null || bindId != _bindSerial )
				break;

			if ( i < _latent.Count && _latent[ i ].Spawned )
				spawned++;
		}

		_streamResidencyDirty = true;
		if ( onComplete != null )
			onComplete( spawned, attempted );
	}

	/// <summary>
	/// Evaluate latent poses after the mound carves — spawn when bounds touch outside.
	/// Prefer <see cref="QueueRevealAfterCarve"/> on dig frames.
	/// </summary>
	public void RefreshAfterCarve()
	{
		StartReveal( _bindSerial, spatialFilter: false, default, 0f, startIndex: 0 );
	}

	/// <summary>
	/// Queue reveal + pick/release updates for LateUpdate so dig frame stays cheap.
	/// </summary>
	public void QueueRevealAfterCarve( Vector3 worldCenter, float radius )
	{
		float r = Mathf.Max( 0.05f, radius );
		if ( _pendingReveal )
		{
			_pendingRevealWorld = worldCenter;
			_pendingRevealRadius = Mathf.Max( _pendingRevealRadius, r );
			return;
		}

		_pendingRevealWorld = worldCenter;
		_pendingRevealRadius = r;
		_pendingReveal = true;
		_revealCursor = 0;
	}

	/// <summary>
	/// True when any unseeded latent prop AABB intersects the carve influence sphere.
	/// </summary>
	public bool MightRevealNear( Vector3 worldCenter, float radius )
	{
		if ( _latent.Count == 0 || _pileRoot == null || _heightfield == null )
			return false;

		float radiusSq = radius * radius;
		for ( int i = 0; i < _latent.Count; i++ )
		{
			LatentEntry latent = _latent[ i ];
			if ( latent.Taken || latent.Spawned )
				continue;

			Vector3 worldPos = _pileRoot.TransformPoint( latent.LocalBounds.center );
			float dx = worldPos.x - worldCenter.x;
			float dz = worldPos.z - worldCenter.z;
			float extent = latent.LocalBounds.extents.magnitude;
			float reach = radius + extent;
			if ( dx * dx + dz * dz <= reach * reach )
				return true;
		}

		return false;
	}

	/// <summary>
	/// True when a spawned prop near the carve may need pickability / world-release updates.
	/// </summary>
	public bool MightUpdateSpawnedNear( Vector3 worldCenter, float radius )
	{
		if ( _props.Count == 0 || _pileRoot == null )
			return false;

		float radiusSq = radius * radius;
		for ( int i = 0; i < _props.Count; i++ )
		{
			PropEntry prop = _props[ i ];
			if ( prop.Item == null )
				continue;

			Vector3 worldPos = prop.Item.transform.position;
			float dx = worldPos.x - worldCenter.x;
			float dz = worldPos.z - worldCenter.z;
			if ( dx * dx + dz * dz <= radiusSq )
				return true;
		}

		return false;
	}

	/// <summary>
	/// True when dig needs an artifact LateUpdate pass (new reveals and/or pick/release).
	/// </summary>
	public bool NeedsCarveUpdateNear( Vector3 worldCenter, float radius )
	{
		return MightRevealNear( worldCenter, radius ) || MightUpdateSpawnedNear( worldCenter, radius );
	}

	public void ClearAll()
	{
		for ( int i = 0; i < _props.Count; i++ )
		{
			if ( _props[ i ].Item != null )
				TreasureItemFactory.Despawn( _props[ i ].Item );
		}

		_props.Clear();
		_latent.Clear();
		_visibleByDef.Clear();
		_pendingReveal = false;
		_revealCursor = 0;
		_streamLatentCursor = 0;
		_streamResidencyDirty = true;
		_hasLastStreamPlayerPos = false;
		ExposedLatentCount = 0;
		StreamedOutCount = 0;
		_pendingWorldReleases.Clear();
		// Authored scene proxies are never despawned here — only re-enabled when rebinding.
		RestoreDisabledAuthoredProxies();
	}

	void RestoreDisabledAuthoredProxies()
	{
		for ( int i = 0; i < _disabledAuthoredProxies.Count; i++ )
		{
			GameObject go = _disabledAuthoredProxies[ i ];
			if ( go != null )
				go.SetActive( true );
		}

		_disabledAuthoredProxies.Clear();
	}

	void LateUpdate()
	{
		if ( _pendingReveal )
			RunPendingReveal();

		_streamSpawnBudget = MaxStreamSpawnsPerFrame;
		_streamDespawnBudget = MaxStreamDespawnsPerFrame;
		GoldPileEditTiming.Begin( "GoldPile.RefreshStreamResidency" );
		RefreshStreamResidency();
		GoldPileEditTiming.End();

		if ( _props.Count == 0 )
			return;

		GoldPileEditTiming.Begin( "GoldPile.RefreshCulling" );
		RefreshCulling();
		GoldPileEditTiming.End();
	}

	void RunPendingReveal()
	{
		_pendingReveal = false;
		Vector3 world = _pendingRevealWorld;
		float radius = _pendingRevealRadius;
		int start = _revealCursor;
		StartReveal( _bindSerial, spatialFilter: true, world, radius, start );
	}

	void StartReveal(
		int bindId,
		bool spatialFilter,
		Vector3 worldCenter,
		float radius,
		int startIndex )
	{
		int generation = ++_revealGeneration;
		_ = RefreshRevealAsync( bindId, generation, spatialFilter, worldCenter, radius, startIndex );
	}

	void OnDisable()
	{
		_cachedCamera = null;
	}

	void OnDestroy()
	{
		_revealGeneration++;
		ClearAll();
	}

	struct LatentBindSettings
	{
		public bool PreferBake;
		public int VolumeMaxAttempts;
		public int RetryNoOccupancyPasses;
		public int RetrySaltPasses;
		public bool UseSpatialHash;
		public bool AvoidCoinSeats;
		public float BudgetMs;
		public int MaxAttemptsPerFrame;
		public bool NearSurface;
	}

	LatentBindSettings ResolveLatentBindSettings()
	{
		LatentBindSettings s = new LatentBindSettings
		{
			PreferBake = true,
			VolumeMaxAttempts = 24,
			RetryNoOccupancyPasses = 1,
			RetrySaltPasses = 0,
			UseSpatialHash = true,
			AvoidCoinSeats = false,
			BudgetMs = 12f,
			MaxAttemptsPerFrame = 64,
			NearSurface = false
		};

		if ( _definition == null )
			return s;

		if ( _owner != null )
		{
			TreasurePileLatentBakeSettings bakeSettings = _owner.LatentBakeSettings;
			if ( bakeSettings != null )
				s.NearSurface = bakeSettings.spawnTreasureNearSurface;
		}

		s.PreferBake = _definition.preferBakedLatents;
		s.VolumeMaxAttempts = Mathf.Max( 1, _definition.latentVolumeMaxAttempts );
		s.RetryNoOccupancyPasses = Mathf.Max( 0, _definition.latentRetryNoOccupancyPasses );
		s.RetrySaltPasses = Mathf.Max( 0, _definition.latentRetrySaltPasses );
		s.UseSpatialHash = _definition.latentUseSpatialHash;
		s.AvoidCoinSeats = _definition.latentAvoidCoinSeats;
		s.BudgetMs = Mathf.Max( 1f, _definition.latentBuildBudgetMs );
		s.MaxAttemptsPerFrame = Mathf.Max( 1, _definition.latentBuildMaxAttemptsPerFrame );
		return s;
	}

	bool TryAppendBakedLatents( LatentBindSettings settings )
	{
		TreasurePileLatentBake bake = _owner != null ? _owner.LatentBake : null;
		if ( bake == null || bake.poses == null )
			return false;
		if ( _heightfield == null || _definition == null )
			return false;

		int heightFp;
		int contentsFp;
		int volumeAttempts;
		bool spatialHash;
		bool avoidCoins;
		int authoredFp;
		bool nearSurface;
		int layoutSeed;
		if ( _owner == null
			|| !_owner.TryGetLatentBakeFingerprint(
				out layoutSeed,
				out heightFp,
				out contentsFp,
				out volumeAttempts,
				out spatialHash,
				out avoidCoins,
				out authoredFp,
				out nearSurface ) )
		{
			layoutSeed = _owner != null ? _owner.LootLayoutSeed : 0;
			heightFp = _heightfield.ComputeLayoutFingerprint();
			contentsFp = _definition.HashLargePropContents();
			volumeAttempts = settings.VolumeMaxAttempts;
			spatialHash = settings.UseSpatialHash;
			avoidCoins = settings.AvoidCoinSeats;
			authoredFp = 0;
			nearSurface = settings.NearSurface;
		}

		if ( !bake.MatchesFingerprint(
			layoutSeed,
			heightFp,
			contentsFp,
			volumeAttempts,
			spatialHash,
			avoidCoins,
			authoredFp,
			nearSurface ) )
		{
			return false;
		}

		int skippedSurface = 0;
		int skippedInvalid = 0;
		int added = 0;
		for ( int i = 0; i < bake.poses.Length; i++ )
		{
			TreasurePileLatentBake.Pose pose = bake.poses[ i ];
			TreasureDefinition def = pose.definition;
			if ( def == null || !IsLargeProp( def ) )
			{
				skippedInvalid++;
				continue;
			}

			float scale = pose.scale;
			if ( scale < 0.01f )
				scale = def.worldScale.x > 0.01f ? def.worldScale.x : 1f;

			Bounds localBounds = new Bounds( pose.boundsCenter, pose.boundsSize );
			if ( !GoldPileTreasurePlacement.HasTreasureSurfaceBelow( _pileRoot, pose.localPos, _latentSurfaceNeighborhoodCells ) )
			{
				skippedSurface++;
				continue;
			}

			_latent.Add( new LatentEntry
			{
				Definition = def,
				LocalPos = pose.localPos,
				LocalRot = pose.localRot,
				Scale = scale,
				LocalBounds = localBounds,
				Taken = false,
				Spawned = false,
				Exposed = false,
				IsAuthored = false,
				PropIndex = -1
			} );
			added++;
		}

		int expected = CountExpectedRemainderLatents();
		if ( added < expected )
		{
			WarnTreasureMissing(
				"baked bind",
				expected,
				added,
				$"bake poses={bake.poses.Length} skipped no-surface={skippedSurface} skipped invalid={skippedInvalid}" );
		}

		return true;
	}

	void WriteBakeAsset( TreasurePileLatentBake bake, int authoredLayoutSeed )
	{
		if ( bake == null || _definition == null || _heightfield == null )
			return;

		LatentBindSettings settings = ResolveLatentBindSettings();
		bake.authoredLayoutSeed = authoredLayoutSeed;
		bake.effectivePileSeed = _pileLootSeed;
		bake.sourceDefinitionName = _definition.name;
		if ( _owner != null
			&& _owner.TryGetLatentBakeFingerprint(
				out int layoutSeed,
				out int heightFp,
				out int contentsFp,
				out int volumeAttempts,
				out bool spatialHash,
				out bool avoidCoins,
				out int authoredFp,
				out bool nearSurface ) )
		{
			bake.authoredLayoutSeed = layoutSeed;
			bake.heightFingerprint = heightFp;
			bake.contentsFingerprint = contentsFp;
			bake.volumeMaxAttempts = volumeAttempts;
			bake.usedSpatialHash = spatialHash;
			bake.avoidedCoinSeats = avoidCoins;
			bake.authoredFingerprint = authoredFp;
			bake.bakedNearSurface = nearSurface;
		}
		else
		{
			bake.heightFingerprint = _heightfield.ComputeLayoutFingerprint();
			bake.contentsFingerprint = _definition.HashLargePropContents();
			bake.authoredFingerprint = 0;
			bake.volumeMaxAttempts = settings.VolumeMaxAttempts;
			bake.usedSpatialHash = settings.UseSpatialHash;
			bake.avoidedCoinSeats = settings.AvoidCoinSeats;
			bake.bakedNearSurface = settings.NearSurface;
		}

		// Bake stores remainder only — authored curated props stay on scene objects.
		int remainderCount = 0;
		for ( int i = 0; i < _latent.Count; i++ )
		{
			if ( !_latent[ i ].IsAuthored )
				remainderCount++;
		}

		TreasurePileLatentBake.Pose[] poses = new TreasurePileLatentBake.Pose[ remainderCount ];
		int write = 0;
		for ( int i = 0; i < _latent.Count; i++ )
		{
			LatentEntry latent = _latent[ i ];
			if ( latent.IsAuthored )
				continue;

			poses[ write++ ] = new TreasurePileLatentBake.Pose
			{
				definition = latent.Definition,
				localPos = latent.LocalPos,
				localRot = latent.LocalRot,
				scale = latent.Scale,
				boundsCenter = latent.LocalBounds.center,
				boundsSize = latent.LocalBounds.size
			};
		}

		bake.poses = poses;
	}

	void BuildLatentEntries()
	{
		LatentBindSettings settings = ResolveLatentBindSettings();
		CreateOccupancy( settings, out List<Bounds> occupiedBounds, out VolumeOccupancyGrid occupancyGrid );
		AppendAuthoredLatentsFromScene( disableProxies: false, occupiedBounds, occupancyGrid );
		PlaceDefinitionContents( settings, occupiedBounds, occupancyGrid );
	}

	void SeedOccupancyFromAuthoredScene( List<Bounds> occupiedBounds, VolumeOccupancyGrid occupancyGrid )
	{
		if ( _owner == null || _pileRoot == null )
			return;

		_owner.CollectAuthoredItems( _authoredScratch );
		for ( int i = 0; i < _authoredScratch.Count; i++ )
		{
			if ( !TryBuildAuthoredLatent( _authoredScratch[ i ], out LatentEntry latent ) )
				continue;

			if ( occupancyGrid != null )
				occupancyGrid.Add( latent.LocalBounds );
			else if ( occupiedBounds != null )
				occupiedBounds.Add( latent.LocalBounds );
		}
	}

	void AppendAuthoredLatentsFromScene(
		bool disableProxies,
		List<Bounds> occupiedBounds,
		VolumeOccupancyGrid occupancyGrid )
	{
		if ( _owner == null || _pileRoot == null )
			return;

		_owner.CollectAuthoredItems( _authoredScratch );
		for ( int i = 0; i < _authoredScratch.Count; i++ )
		{
			TreasurePileAuthoredItem authored = _authoredScratch[ i ];
			if ( !TryBuildAuthoredLatent( authored, out LatentEntry latent ) )
				continue;

			_latent.Add( latent );
			if ( occupancyGrid != null )
				occupancyGrid.Add( latent.LocalBounds );
			else if ( occupiedBounds != null )
				occupiedBounds.Add( latent.LocalBounds );

			if ( disableProxies && authored != null && authored.gameObject != null && authored.gameObject.activeSelf )
			{
				authored.gameObject.SetActive( false );
				_disabledAuthoredProxies.Add( authored.gameObject );
			}
		}
	}

	bool TryBuildAuthoredLatent( TreasurePileAuthoredItem authored, out LatentEntry latent )
	{
		latent = default;
		if ( authored == null || authored.Definition == null || _pileRoot == null )
			return false;
		if ( !TreasurePileAuthoredItem.IsCuratable( authored.Definition ) )
			return false;

		TreasureDefinition def = authored.Definition;
		Transform t = authored.transform;
		Vector3 localPos = _pileRoot.InverseTransformPoint( t.position );
		Quaternion localRot = Quaternion.Inverse( _pileRoot.rotation ) * t.rotation;
		float scale = def.worldScale.x > 0.01f ? def.worldScale.x : 1f;
		float lossy = t.lossyScale.x;
		if ( lossy > 0.01f )
			scale = lossy;

		Bounds worldBounds = authored.GetWorldBounds();
		Bounds localBounds = WorldBoundsToLocal( worldBounds );
		if ( localBounds.size.sqrMagnitude < 1e-6f )
			localBounds = GoldPileTreasurePlacement.LocalAabbFromPose( localPos, localRot, scale, null );

		latent = new LatentEntry
		{
			Definition = def,
			LocalPos = localPos,
			LocalRot = localRot,
			Scale = scale,
			LocalBounds = localBounds,
			Taken = false,
			Spawned = false,
			Exposed = false,
			IsAuthored = true,
			PropIndex = -1
		};
		return true;
	}

	async Task PlaceDefinitionContentsAsync(
		int bindId,
		LatentBindSettings settings,
		List<Bounds> occupiedBounds,
		VolumeOccupancyGrid occupancyGrid )
	{
		if ( _definition == null || _definition.treasureContents == null || _heightfield == null )
			return;

		System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
		int attemptsThisFrame = 0;
		int expected = 0;
		int placed = 0;
		string perType = "";

		for ( int e = 0; e < _definition.treasureContents.Length; e++ )
		{
			if ( !IsBindStillValid( bindId ) )
				return;

			TreasurePileEntry entry = _definition.treasureContents[ e ];
			TreasureDefinition def = entry.treasure;
			if ( def == null || !IsLargeProp( def ) || entry.count <= 0 )
				continue;

			int count = entry.count;
			expected += count;
			int got = 0;
			for ( int i = 0; i < count; i++ )
			{
				if ( !IsBindStillValid( bindId ) )
					return;

				if ( Application.isPlaying
					&& ( sw.Elapsed.TotalMilliseconds >= settings.BudgetMs
						|| attemptsThisFrame >= settings.MaxAttemptsPerFrame ) )
				{
					await Awaitable.NextFrameAsync();
					if ( !IsBindStillValid( bindId ) )
						return;
					sw.Restart();
					attemptsThisFrame = 0;
				}

				int unitIndex = WorldLootSeed.StableUnitIndex( e, i );
				if ( PlaceLatentUnit( def, unitIndex, settings, occupiedBounds, occupancyGrid ) )
					got++;
				attemptsThisFrame++;
			}

			placed += got;
			perType = AppendMissingType( perType, def, got, count );
		}

		WarnTreasureMissing( "procedural bind", expected, placed, perType );
	}

	LatentFillReport PlaceDefinitionContents(
		LatentBindSettings settings,
		List<Bounds> occupiedBounds,
		VolumeOccupancyGrid occupancyGrid )
	{
		LatentFillReport report = default;
		if ( _definition == null || _definition.treasureContents == null || _heightfield == null )
			return report;

		string perType = "";
		for ( int e = 0; e < _definition.treasureContents.Length; e++ )
		{
			TreasurePileEntry entry = _definition.treasureContents[ e ];
			TreasureDefinition def = entry.treasure;
			if ( def == null || !IsLargeProp( def ) || entry.count <= 0 )
				continue;

			int count = entry.count;
			report.Expected += count;
			int got = 0;
			for ( int i = 0; i < count; i++ )
			{
				int unitIndex = WorldLootSeed.StableUnitIndex( e, i );
				if ( PlaceLatentUnit( def, unitIndex, settings, occupiedBounds, occupancyGrid ) )
					got++;
			}

			report.Placed += got;
			perType = AppendMissingType( perType, def, got, count );
		}

		report.PerType = perType;
		WarnTreasureMissing( "latent fill", report.Expected, report.Placed, perType );
		return report;
	}

	async Task BuildLatentEntriesAsync( int bindId, LatentBindSettings settings )
	{
		_latent.Clear();
		ExposedLatentCount = 0;
		StreamedOutCount = 0;
		CreateOccupancy( settings, out List<Bounds> occupiedBounds, out VolumeOccupancyGrid occupancyGrid );
		AppendAuthoredLatentsFromScene( disableProxies: Application.isPlaying, occupiedBounds, occupancyGrid );
		await PlaceDefinitionContentsAsync( bindId, settings, occupiedBounds, occupancyGrid );
	}

	void BuildLatentEntriesSync( LatentBindSettings settings )
	{
		_latent.Clear();
		ExposedLatentCount = 0;
		StreamedOutCount = 0;
		CreateOccupancy( settings, out List<Bounds> occupiedBounds, out VolumeOccupancyGrid occupancyGrid );
		AppendAuthoredLatentsFromScene( disableProxies: false, occupiedBounds, occupancyGrid );
		PlaceDefinitionContents( settings, occupiedBounds, occupancyGrid );
	}

	void CreateOccupancy(
		LatentBindSettings settings,
		out List<Bounds> occupiedBounds,
		out VolumeOccupancyGrid occupancyGrid )
	{
		occupiedBounds = new List<Bounds>( 128 );
		occupancyGrid = null;

		if ( settings.UseSpatialHash && _heightfield != null )
		{
			float cell = Mathf.Max( _placementMinSpacing, 0.25f );
			occupancyGrid = new VolumeOccupancyGrid( _heightfield.WorldSize, cell );
		}

		if ( settings.AvoidCoinSeats && _loot != null )
		{
			_loot.CollectVolumeBoundsOccupancy( occupiedBounds );
			if ( occupancyGrid != null )
			{
				for ( int i = 0; i < occupiedBounds.Count; i++ )
					occupancyGrid.Add( occupiedBounds[ i ] );
			}
		}
	}

	bool PlaceLatentUnit(
		TreasureDefinition def,
		int unitIndex,
		LatentBindSettings settings,
		List<Bounds> occupiedBounds,
		VolumeOccupancyGrid occupancyGrid )
	{
		float scale = def.worldScale.x;
		if ( scale < 0.01f )
			scale = 1f;

		float probe = Mathf.Max( 0.08f, scale * 0.35f );
		float spacing = Mathf.Max( _placementMinSpacing, probe * 1.5f );
		GoldPileTreasurePlacement.VolumePose pose;
		Bounds localBounds;
		bool placed = TrySampleLatentPose(
			def,
			unitIndex,
			scale,
			probe,
			spacing,
			_treasureHeightBias,
			occupiedBounds,
			occupancyGrid,
			settings.VolumeMaxAttempts,
			settings.NearSurface,
			out pose,
			out localBounds );

		if ( !placed )
		{
			for ( int pass = 0; pass < settings.RetryNoOccupancyPasses && !placed; pass++ )
			{
				placed = TrySampleLatentPose(
					def,
					unitIndex + 5003 + pass * 17,
					scale,
					probe,
					0f,
					0f,
					null,
					null,
					settings.VolumeMaxAttempts,
					settings.NearSurface,
					out pose,
					out localBounds );
			}
		}

		if ( !placed )
		{
			for ( int attempt = 0; attempt < settings.RetrySaltPasses && !placed; attempt++ )
			{
				placed = TrySampleLatentPose(
					def,
					unitIndex + 9001 + attempt * 137,
					scale,
					probe,
					0f,
					0f,
					null,
					null,
					settings.VolumeMaxAttempts,
					settings.NearSurface,
					out pose,
					out localBounds );
			}
		}

		if ( !placed )
			return false;

		if ( occupancyGrid != null )
			occupancyGrid.Add( localBounds );
		else if ( occupiedBounds != null )
			occupiedBounds.Add( localBounds );

		_latent.Add( new LatentEntry
		{
			Definition = def,
			LocalPos = pose.LocalPos,
			LocalRot = pose.LocalRot,
			Scale = scale,
			LocalBounds = localBounds,
			Taken = false,
			Spawned = false,
			Exposed = false,
			IsAuthored = false,
			PropIndex = -1
		} );
		return true;
	}

	bool TrySampleLatentPose(
		TreasureDefinition def,
		int unitIndex,
		float scale,
		float probe,
		float spacing,
		float heightBias,
		List<Bounds> occupiedBounds,
		VolumeOccupancyGrid occupancyGrid,
		int maxAttempts,
		bool nearSurface,
		out GoldPileTreasurePlacement.VolumePose pose,
		out Bounds localBounds )
	{
		return GoldPileTreasurePlacement.TrySampleValidVolumePose(
			_heightfield,
			_pileLootSeed,
			unitIndex,
			_placementRadiusFraction,
			scale,
			probe,
			_treasureRadialPower,
			heightBias,
			def.category,
			occupiedBounds,
			spacing,
			null,
			out pose,
			out localBounds,
			maxAttempts,
			occupancyGrid,
			_pileRoot,
			_treasureXZSpread,
			_latentSurfaceNeighborhoodCells,
			nearSurface );
	}

	async Task RefreshRevealAsync(
		int bindId,
		int generation,
		bool spatialFilter,
		Vector3 worldCenter,
		float radius,
		int startIndex )
	{
		if ( _definition == null || _heightfield == null || _pileRoot == null )
			return;

		GoldPileEditTiming.Begin( "GoldPile.ArtifactReveal" );
		System.Diagnostics.Stopwatch sw = GoldPileEditTiming.StartWatchIfEnabled();

		int spawnsThisPass = 0;
		// Dig frames budget a few spawns; full bind refresh is uncapped.
		int maxSpawns = spatialFilter ? 3 : int.MaxValue;
		int i = Mathf.Max( 0, startIndex );
		bool budgetHit = false;

		try
		{
			_pendingWorldReleases.Clear();

			for ( ; i < _latent.Count; i++ )
			{
				if ( !IsRevealStillValid( bindId, generation ) )
				{
					FlushPendingWorldReleases();
					return;
				}

				LatentEntry latent = _latent[ i ];
				if ( latent.Taken || latent.Definition == null )
					continue;

				if ( spatialFilter )
				{
					Vector3 worldPos = _pileRoot.TransformPoint( latent.LocalBounds.center );
					float dx = worldPos.x - worldCenter.x;
					float dz = worldPos.z - worldCenter.z;
					float extent = latent.LocalBounds.extents.magnitude;
					float reach = radius + extent;
					if ( dx * dx + dz * dz > reach * reach )
						continue;
				}

				bool touchesOutside = GoldPileTreasurePlacement.TouchesOutsideAabb(
					_heightfield,
					latent.LocalBounds );
				float outsideFraction = GoldPileTreasurePlacement.OutsideFractionAabb(
					_heightfield,
					latent.LocalBounds );

				if ( latent.Spawned )
				{
					if ( TryResolveProp( i, out int propIndex, out PropEntry prop ) )
					{
						if ( outsideFraction >= _treasureReleaseOutsideFraction && prop.Item != null )
						{
							_pendingWorldReleases.Add( prop.Item );
							continue;
						}

						// Once revealed pickable, stay pickable — poses no longer re-bury.
						bool pickable = prop.Pickable
							|| IsNewlyPickable( latent.Definition, touchesOutside, outsideFraction );
						if ( prop.Pickable != pickable )
						{
							prop.Pickable = pickable;
							_props[ propIndex ] = prop;
							ApplyInteractableState( prop );
						}
					}
					continue;
				}

				if ( !touchesOutside )
					continue;

				if ( !CanSpawnBakedLatent( latent ) )
					continue;

				latent.Exposed = true;
				_latent[ i ] = latent;
				_streamResidencyDirty = true;

				if ( !IsLatentInStreamRange( i, currentlyResident: false ) )
					continue;

				// Exposed gems/artifacts spawn when in stream range — no maxVisible cap.
				if ( spawnsThisPass >= maxSpawns )
				{
					budgetHit = true;
					_streamResidencyDirty = true;
					break;
				}

				await SeatLatentAsync( i, null );

				if ( !IsRevealStillValid( bindId, generation ) )
				{
					FlushPendingWorldReleases();
					return;
				}

				spawnsThisPass++;

				// Freshly seated props may already qualify for pick / world release.
				latent = _latent[ i ];
				if ( latent.Spawned && TryResolveProp( i, out int seatedPropIndex, out PropEntry seatedProp ) )
				{
					outsideFraction = GoldPileTreasurePlacement.OutsideFractionAabb(
						_heightfield,
						latent.LocalBounds );
					if ( outsideFraction >= _treasureReleaseOutsideFraction && seatedProp.Item != null )
						_pendingWorldReleases.Add( seatedProp.Item );
				}
			}

			if ( !IsRevealStillValid( bindId, generation ) )
			{
				FlushPendingWorldReleases();
				return;
			}

			FlushPendingWorldReleases();

			if ( budgetHit )
			{
				_revealCursor = i;
				_pendingRevealWorld = worldCenter;
				_pendingRevealRadius = radius;
				_pendingReveal = true;
			}
			else
				_revealCursor = 0;
		}
		finally
		{
			if ( sw != null )
			{
				sw.Stop();
				GoldPileEditTiming.Record(
					"artifacts.reveal",
					sw.Elapsed.TotalMilliseconds,
					$"spawns={spawnsThisPass} budgetHit={budgetHit} spatial={spatialFilter}" );
			}

			GoldPileEditTiming.End();
		}
	}

	bool IsRevealStillValid( int bindId, int generation )
	{
		return this != null
			&& bindId == _bindSerial
			&& generation == _revealGeneration
			&& _heightfield != null
			&& _pileRoot != null;
	}

	bool IsNewlyPickable( TreasureDefinition definition, bool touchesOutside, float outsideFraction )
	{
		if ( definition == null || !touchesOutside )
			return false;

		// Gems: any outside contact. Artifacts / other large props: fraction threshold.
		if ( definition.category == TreasureCategory.Gem )
			return true;

		return outsideFraction >= _treasurePickupOutsideFraction;
	}

	void FlushPendingWorldReleases()
	{
		if ( _pendingWorldReleases.Count == 0 )
			return;

		for ( int i = 0; i < _pendingWorldReleases.Count; i++ )
		{
			TreasureItem item = _pendingWorldReleases[ i ];
			if ( item == null )
				continue;

			ReleasePropToWorld( item );
		}

		_pendingWorldReleases.Clear();
	}

	void ReleasePropToWorld( TreasureItem item )
	{
		if ( item == null || FindPropIndex( item ) < 0 )
			return;

		Vector3 worldPos = item.transform.position;
		Quaternion worldRot = item.transform.rotation;
		TreasureDefinition def = item.Definition;

		if ( !DetachStolenProp( item ) )
			return;

		Vector3 velocity = Vector3.zero;
		if ( TreasureItem.UsesSurfaceSimulation( def ) )
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
				{
					// Start falling immediately — small downward seed so gravity isn't delayed a frame.
					velocity.y = -Mathf.Min( surfaceDef.gravity * 0.15f, 2.5f );
				}
			}

			// From rest: no horizontal seed — surface sim ramps flow acceleration over time.
			item.EnterSurface( worldPos, worldRot, velocity, snapToSeat: false, fromRest: true );
		}
		else
			item.EnterPhysics( worldPos, worldRot, velocity );

		if ( _owner != null )
			_owner.NotifyPropReleasedToWorld( worldPos );
	}

	async Task SeatLatentAsync( int latentIndex, TreasureItem reuse )
	{
		if ( latentIndex < 0 || latentIndex >= _latent.Count || _pileRoot == null )
			return;

		LatentEntry latent = _latent[ latentIndex ];
		if ( latent.Taken || latent.Definition == null )
			return;

		if ( latent.Spawned && reuse == null )
			return;

		if ( reuse == null && !CanSpawnBakedLatent( latent ) )
		{
			if ( !latent.LoggedMissingSpawn )
			{
				latent.LoggedMissingSpawn = true;
				_latent[ latentIndex ] = latent;
				string defName = latent.Definition != null ? latent.Definition.name : "null";
				Vector3 worldPosBlocked = _pileRoot.TransformPoint( latent.LocalPos );
				Debug.LogWarning(
					$"[GoldPileArtifactProps] Pile '{PileLogName()}' could not spawn '{defName}' at {worldPosBlocked} "
					+ "(no Treasure surface pad below). All treasure needs to spawn.",
					this );
			}

			return;
		}

		TreasureDefinition definition = latent.Definition;
		Vector3 worldPos = _pileRoot.TransformPoint( latent.LocalPos );
		Quaternion worldRot = _pileRoot.rotation * latent.LocalRot;

		TreasureItem item = reuse;
		if ( item == null )
			item = await TreasureItemFactory.SpawnAsync( definition, worldPos, worldRot, transform );

		if ( item == null || this == null || _pileRoot == null )
		{
			if ( item != null && reuse == null )
				TreasureItemFactory.Despawn( item );
			if ( item == null && reuse == null )
			{
				Debug.LogWarning(
					$"[GoldPileArtifactProps] Pile '{PileLogName()}' failed to instantiate '{definition.name}' at {worldPos}. "
					+ "All treasure needs to spawn.",
					this );
			}

			return;
		}

		if ( latentIndex < 0 || latentIndex >= _latent.Count )
		{
			if ( reuse == null )
				TreasureItemFactory.Despawn( item );
			return;
		}

		latent = _latent[ latentIndex ];
		if ( latent.Taken || latent.Definition == null || ( latent.Spawned && reuse == null ) )
		{
			if ( reuse == null )
				TreasureItemFactory.Despawn( item );
			return;
		}

		RegisterProp( item, latent.Definition, latentIndex );
	}

	void SeatExisting( TreasureItem item, Vector3 preferredWorld )
	{
		if ( item == null || item.Definition == null || _pileRoot == null )
			return;

		int latentIndex = FindUnusedLatent( item.Definition );
		if ( latentIndex < 0 )
			latentIndex = AppendLatentFromWorld( item.Definition, preferredWorld );
		if ( latentIndex < 0 )
			return;

		LatentEntry latent = _latent[ latentIndex ];
		latent.Taken = false;
		_latent[ latentIndex ] = latent;
		_ = SeatLatentAsync( latentIndex, item );
	}

	void RegisterProp( TreasureItem item, TreasureDefinition definition, int latentIndex )
	{
		if ( item == null || definition == null )
			return;
		if ( latentIndex < 0 || latentIndex >= _latent.Count )
			return;

		LatentEntry latent = _latent[ latentIndex ];
		bool touchesOutside = _heightfield != null
			&& GoldPileTreasurePlacement.TouchesOutsideAabb( _heightfield, latent.LocalBounds );
		float outsideFraction = _heightfield != null
			? GoldPileTreasurePlacement.OutsideFractionAabb( _heightfield, latent.LocalBounds )
			: 0f;
		bool pickable = IsNewlyPickable( definition, touchesOutside, outsideFraction );

		int existing = FindPropIndex( item );
		if ( existing >= 0 )
		{
			PropEntry updated = _props[ existing ];
			updated.LocalPos = latent.LocalPos;
			updated.LocalRot = latent.LocalRot;
			updated.LatentIndex = latentIndex;
			updated.Pickable = pickable;
			AssignChunk( ref updated );
			ApplyFixedPose( item, latent );
			CachePropCullData( ref updated, item );
			_props[ existing ] = updated;
			ApplyInteractableState( updated );
			latent.Spawned = true;
			latent.Exposed = true;
			latent.PropIndex = existing;
			_latent[ latentIndex ] = latent;
			return;
		}

		item.SetOriginPile( _owner );
		item.EnterPile( _owner );
		item.transform.SetParent( transform, true );
		if ( definition.category == TreasureCategory.Gem )
			item.EnsureGemSphereCollider();
		ApplyFixedPose( item, latent );

		// Refine latent AABB from the real prop for carve reveal / pick thresholds.
		Bounds worldBounds = GetItemBounds( item );
		latent.LocalBounds = WorldBoundsToLocal( worldBounds );
		float probe = Mathf.Max( 0.08f, latent.Scale * 0.35f );
		float maxEmbed = GoldPileTreasurePlacement.GetMaxGroundEmbedFraction( definition.category );
		if ( _heightfield != null
			&& GoldPileTreasurePlacement.ViolatesGroundEmbed(
				latent.LocalBounds,
				_heightfield.LootGroundLevel,
				maxEmbed ) )
		{
			if ( TryResampleLatentSeat( latentIndex, probe ) )
				latent = _latent[ latentIndex ];
			else
			{
				Vector3 seatedPos = latent.LocalPos;
				Bounds seatedBounds = latent.LocalBounds;
				GoldPileTreasurePlacement.LiftBoundsIntoPileColumn(
					_heightfield,
					ref seatedPos,
					ref seatedBounds,
					probe,
					maxEmbed );
				latent.LocalPos = seatedPos;
				latent.LocalBounds = seatedBounds;
			}

			ApplyFixedPose( item, latent );
			worldBounds = GetItemBounds( item );
			latent.LocalBounds = WorldBoundsToLocal( worldBounds );
		}

		if ( _heightfield != null )
		{
			Vector3 seatedPos = latent.LocalPos;
			Bounds seatedBounds = latent.LocalBounds;
			GoldPileTreasurePlacement.ClampMaxProtrusion(
				_heightfield,
				ref seatedPos,
				ref seatedBounds );
			if ( ( seatedPos - latent.LocalPos ).sqrMagnitude > 1e-8f )
			{
				latent.LocalPos = seatedPos;
				latent.LocalBounds = seatedBounds;
				ApplyFixedPose( item, latent );
			}
		}

		touchesOutside = _heightfield != null
			&& GoldPileTreasurePlacement.TouchesOutsideAabb( _heightfield, latent.LocalBounds );
		outsideFraction = _heightfield != null
			? GoldPileTreasurePlacement.OutsideFractionAabb( _heightfield, latent.LocalBounds )
			: 0f;
		pickable = IsNewlyPickable( definition, touchesOutside, outsideFraction );

		PropEntry entry = new PropEntry
		{
			Item = item,
			Definition = definition,
			LocalPos = latent.LocalPos,
			LocalRot = latent.LocalRot,
			LatentIndex = latentIndex,
			RenderVisible = true,
			Pickable = pickable
		};
		AssignChunk( ref entry );
		CachePropCullData( ref entry, item );
		_props.Add( entry );
		_visibleByDef[ definition ] = GetVisibleCount( definition ) + 1;

		latent.Spawned = true;
		latent.Exposed = true;
		latent.PropIndex = _props.Count - 1;
		_latent[ latentIndex ] = latent;
		ApplyInteractableState( entry );
	}

	bool TryResampleLatentSeat( int latentIndex, float probe )
	{
		if ( latentIndex < 0 || latentIndex >= _latent.Count || _heightfield == null )
			return false;

		LatentEntry latent = _latent[ latentIndex ];
		if ( latent.Definition == null )
			return false;

		List<Bounds> occupiedBounds = new List<Bounds>( _latent.Count );
		LatentBindSettings settings = ResolveLatentBindSettings();
		VolumeOccupancyGrid occupancyGrid = null;
		if ( settings.UseSpatialHash )
		{
			float cell = Mathf.Max( _placementMinSpacing, 0.25f );
			occupancyGrid = new VolumeOccupancyGrid( _heightfield.WorldSize, cell );
		}

		for ( int i = 0; i < _latent.Count; i++ )
		{
			if ( i == latentIndex )
				continue;
			if ( _latent[ i ].Taken )
				continue;
			Bounds b = _latent[ i ].LocalBounds;
			if ( occupancyGrid != null )
				occupancyGrid.Add( b );
			else
				occupiedBounds.Add( b );
		}

		float scale = latent.Scale;
		float spacing = Mathf.Max( _placementMinSpacing, probe * 1.5f );
		int unitIndex = latentIndex * 17 + 3;
		TreasureCategory category = latent.Definition.category;
		if ( !GoldPileTreasurePlacement.TrySampleValidVolumePose(
			_heightfield,
			_pileLootSeed,
			unitIndex,
			_placementRadiusFraction,
			scale,
			probe,
			_treasureRadialPower,
			_treasureHeightBias,
			category,
			occupiedBounds,
			spacing,
			null,
			out GoldPileTreasurePlacement.VolumePose pose,
			out Bounds localBounds,
			settings.VolumeMaxAttempts,
			occupancyGrid,
			_pileRoot,
			_treasureXZSpread,
			_latentSurfaceNeighborhoodCells,
			settings.NearSurface ) )
		{
			return false;
		}

		latent.LocalPos = pose.LocalPos;
		latent.LocalRot = pose.LocalRot;
		latent.LocalBounds = localBounds;
		_latent[ latentIndex ] = latent;
		return true;
	}

	void CachePropCullData( ref PropEntry entry, TreasureItem item )
	{
		if ( item == null )
			return;

		entry.CachedCollider = item.GetComponent<Collider>();
		entry.WorldBounds = GetItemBounds( item );
	}

	void RefreshPropWorldBoundsIfNeeded()
	{
		if ( _pileRoot == null )
			return;

		for ( int i = 0; i < _props.Count; i++ )
		{
			PropEntry entry = _props[ i ];
			int latentIndex = entry.LatentIndex;
			if ( latentIndex >= 0 && latentIndex < _latent.Count )
				entry.WorldBounds = LocalBoundsToWorld( _latent[ latentIndex ].LocalBounds );
			else if ( entry.Item != null && entry.RenderVisible )
				entry.WorldBounds = GetItemBounds( entry.Item );

			_props[ i ] = entry;
		}
	}

	void ApplyFixedPose( TreasureItem item, LatentEntry latent )
	{
		if ( item == null || _pileRoot == null )
			return;

		Vector3 localPos = latent.LocalPos;
		if ( _heightfield != null )
		{
			float probe = Mathf.Max( 0.08f, latent.Scale * 0.35f );
			localPos = GoldPileTreasurePlacement.ClampAboveFloor( _heightfield, localPos, probe );
		}

		Vector3 worldPos = _pileRoot.TransformPoint( localPos );
		Quaternion worldRot = _pileRoot.rotation * latent.LocalRot;
		item.transform.SetPositionAndRotation( worldPos, worldRot );
		item.ApplyWorldScale();
		item.SetMeshVisible( true );
	}

	void ApplyInteractableState( PropEntry prop )
	{
		if ( prop.Item == null )
			return;

		// Solid pile obstacles (statues, chests) collide as soon as they are seated.
		// Pickup still waits for outside-fraction; frustum hide must not drop player collision.
		bool solidOnPile = prop.Definition != null && prop.Definition.collideWithPlayerOnPile;
		bool enabled = solidOnPile || ( prop.RenderVisible && prop.Pickable );
		Collider[] cols = prop.Item.GetComponentsInChildren<Collider>( true );
		for ( int i = 0; i < cols.Length; i++ )
		{
			if ( cols[ i ] != null )
				cols[ i ].enabled = enabled;
		}
	}

	int FindUnusedLatent( TreasureDefinition definition )
	{
		for ( int i = 0; i < _latent.Count; i++ )
		{
			LatentEntry latent = _latent[ i ];
			if ( latent.Definition != definition )
				continue;
			if ( !latent.Taken && !latent.Spawned )
				return i;
			if ( latent.Taken )
				return i;
		}

		return -1;
	}

	int AppendLatentVolumeSample( TreasureDefinition definition, int unitSalt )
	{
		if ( definition == null || _pileRoot == null || _heightfield == null )
			return -1;

		LatentBindSettings settings = ResolveLatentBindSettings();
		CreateOccupancy(
			settings,
			out List<Bounds> occupiedBounds,
			out VolumeOccupancyGrid occupancyGrid );

		for ( int i = 0; i < _latent.Count; i++ )
		{
			if ( _latent[ i ].Taken )
				continue;
			Bounds b = _latent[ i ].LocalBounds;
			if ( occupancyGrid != null )
				occupancyGrid.Add( b );
			else
				occupiedBounds.Add( b );
		}

		int before = _latent.Count;
		PlaceLatentUnit( definition, unitSalt, settings, occupiedBounds, occupancyGrid );
		if ( _latent.Count <= before )
			return -1;
		return _latent.Count - 1;
	}

	int AppendLatentFromWorld( TreasureDefinition definition, Vector3 preferredWorld )
	{
		if ( definition == null || _pileRoot == null || _heightfield == null )
			return -1;

		Vector3 local = _pileRoot.InverseTransformPoint( preferredWorld );
		float half = _heightfield.WorldSize * 0.5f;
		local.x = Mathf.Clamp( local.x, -half, half );
		local.z = Mathf.Clamp( local.z, -half, half );
		float surface = _heightfield.SampleNormalized( local.x, local.z ) * _heightfield.MaxHeight;
		float scale = definition.worldScale.x;
		if ( scale < 0.01f )
			scale = 1f;
		float probe = Mathf.Max( 0.08f, scale * 0.35f );
		float floorY = GoldPileTreasurePlacement.FloorClearanceY( _heightfield.LootGroundLevel, probe );
		float yMax = Mathf.Max( floorY, surface - probe * 0.35f );
		local.y = Mathf.Clamp( local.y, floorY, yMax );
		local = GoldPileTreasurePlacement.ClampAboveFloor( _heightfield, local, probe );

		// Prefer a solid mound column; if preferred XZ is below-ground, snap toward center on that ray.
		if ( !_heightfield.ExistsAtLocal( local.x, local.z ) )
		{
			float angle = Mathf.Atan2( local.z, local.x );
			float radius = new Vector2( local.x, local.z ).magnitude;
			for ( int step = 0; step < 8; step++ )
			{
				radius *= 0.65f;
				local.x = Mathf.Cos( angle ) * radius;
				local.z = Mathf.Sin( angle ) * radius;
				if ( _heightfield.ExistsAtLocal( local.x, local.z ) )
				{
					surface = _heightfield.SampleNormalized( local.x, local.z ) * _heightfield.MaxHeight;
					local.y = Mathf.Clamp( local.y, floorY, Mathf.Max( floorY, surface ) );
					break;
				}
			}
		}

		Quaternion rot = GoldPileTreasurePlacement.HashRotation( _pileLootSeed, _latent.Count + 17 );
		Bounds bounds = GoldPileTreasurePlacement.LocalAabbFromPose( local, rot, scale, null );
		bounds.Expand( probe );
		float maxEmbed = GoldPileTreasurePlacement.GetMaxGroundEmbedFraction( definition.category );
		LatentBindSettings settings = ResolveLatentBindSettings();
		VolumeOccupancyGrid occupancyGrid = null;
		if ( settings.UseSpatialHash )
		{
			float cell = Mathf.Max( _placementMinSpacing, 0.25f );
			occupancyGrid = new VolumeOccupancyGrid( _heightfield.WorldSize, cell );
		}

		if ( GoldPileTreasurePlacement.IsInvalidFloorSeat(
			_heightfield,
			local,
			bounds,
			probe,
			maxEmbed ) )
		{
			List<Bounds> occupiedBounds = new List<Bounds>( _latent.Count + 1 );
			for ( int i = 0; i < _latent.Count; i++ )
			{
				if ( _latent[ i ].Taken )
					continue;
				Bounds b = _latent[ i ].LocalBounds;
				if ( occupancyGrid != null )
					occupancyGrid.Add( b );
				else
					occupiedBounds.Add( b );
			}

			float spacing = Mathf.Max( _placementMinSpacing, probe * 1.5f );
			if ( !GoldPileTreasurePlacement.TrySampleValidVolumePose(
				_heightfield,
				_pileLootSeed,
				_latent.Count + 31,
				_placementRadiusFraction,
				scale,
				probe,
				_treasureRadialPower,
				_treasureHeightBias,
				definition.category,
				occupiedBounds,
				spacing,
				null,
				out GoldPileTreasurePlacement.VolumePose pose,
				out Bounds validBounds,
				settings.VolumeMaxAttempts,
				occupancyGrid,
				_pileRoot,
				_treasureXZSpread,
				_latentSurfaceNeighborhoodCells,
				settings.NearSurface ) )
			{
				return -1;
			}

			local = pose.LocalPos;
			rot = pose.LocalRot;
			bounds = validBounds;
		}
		else
		{
			GoldPileTreasurePlacement.LiftBoundsIntoPileColumn(
				_heightfield,
				ref local,
				ref bounds,
				probe,
				maxEmbed );
			if ( GoldPileTreasurePlacement.IsInvalidFloorSeat(
				_heightfield,
				local,
				bounds,
				probe,
				maxEmbed ) )
			{
				List<Bounds> occupiedBounds = new List<Bounds>( _latent.Count + 1 );
				for ( int i = 0; i < _latent.Count; i++ )
				{
					if ( _latent[ i ].Taken )
						continue;
					Bounds b = _latent[ i ].LocalBounds;
					if ( occupancyGrid != null )
						occupancyGrid.Add( b );
					else
						occupiedBounds.Add( b );
				}

				float spacing = Mathf.Max( _placementMinSpacing, probe * 1.5f );
				if ( !GoldPileTreasurePlacement.TrySampleValidVolumePose(
					_heightfield,
					_pileLootSeed,
					_latent.Count + 47,
					_placementRadiusFraction,
					scale,
					probe,
					_treasureRadialPower,
					_treasureHeightBias,
					definition.category,
					occupiedBounds,
					spacing,
					null,
					out GoldPileTreasurePlacement.VolumePose pose,
					out Bounds validBounds,
					settings.VolumeMaxAttempts,
					occupancyGrid,
					_pileRoot,
					_treasureXZSpread,
					_latentSurfaceNeighborhoodCells,
					settings.NearSurface ) )
				{
					return -1;
				}

				local = pose.LocalPos;
				rot = pose.LocalRot;
				bounds = validBounds;
			}
		}

		GoldPileTreasurePlacement.ClampMaxProtrusion( _heightfield, ref local, ref bounds );

		_latent.Add( new LatentEntry
		{
			Definition = definition,
			LocalPos = local,
			LocalRot = rot,
			Scale = scale,
			LocalBounds = bounds,
			Taken = false,
			Spawned = false,
			Exposed = false,
			IsAuthored = false,
			PropIndex = -1
		} );
		return _latent.Count - 1;
	}

	void AssignChunk( ref PropEntry entry )
	{
		if ( _loot == null || _loot.ChunkGrid == null || _loot.ChunkGrid.ChunkCount == 0 )
		{
			entry.ChunkX = 0;
			entry.ChunkZ = 0;
			return;
		}

		_loot.ChunkGrid.LocalToChunk( entry.LocalPos.x, entry.LocalPos.z, out entry.ChunkX, out entry.ChunkZ );
	}

	void RefreshCulling()
	{
		RefreshPropWorldBoundsIfNeeded();

		Camera camera = ResolveCamera();
		bool hasFrustum = GoldPileFrustumCache.TryGet( camera, out Plane[] frustumPlanes );
		bool useChunkFrustum = _loot != null
			&& _loot.StreamingEnabled
			&& _loot.Streamer != null
			&& _loot.Streamer.HasTicked
			&& _loot.ChunkGrid != null
			&& _loot.ChunkGrid.ChunkCount > 0;

		for ( int i = 0; i < _props.Count; i++ )
		{
			PropEntry entry = _props[ i ];
			TreasureItem item = entry.Item;
			if ( item == null )
				continue;

			bool visible = true;
			if ( useChunkFrustum && !PropBoundsExceedChunk( entry ) )
			{
				GoldPileChunk chunk = _loot.ChunkGrid.GetChunk( entry.ChunkX, entry.ChunkZ );
				if ( chunk != null && !chunk.FrustumVisible )
					visible = false;
			}

			if ( visible && hasFrustum )
				visible = GeometryUtility.TestPlanesAABB( frustumPlanes, entry.WorldBounds );

			if ( visible == entry.RenderVisible )
				continue;

			entry.RenderVisible = visible;
			_props[ i ] = entry;
			item.SetMeshVisible( visible );
			ApplyInteractableState( entry );
		}
	}

	bool PropBoundsExceedChunk( PropEntry entry )
	{
		if ( _loot == null || _loot.ChunkGrid == null )
			return true;

		float chunkSize = _loot.ChunkGrid.ChunkSize;
		Vector3 size = entry.WorldBounds.size;
		return size.x > chunkSize * 0.5f || size.z > chunkSize * 0.5f || size.y > chunkSize;
	}

	static Bounds GetItemBounds( TreasureItem item )
	{
		if ( item == null )
			return new Bounds( Vector3.zero, Vector3.one * 0.5f );

		return TreasureItem.GetCombinedRendererWorldBounds( item.transform, item.transform.position );
	}

	Bounds WorldBoundsToLocal( Bounds worldBounds )
	{
		if ( _pileRoot == null )
			return worldBounds;

		Vector3 c = _pileRoot.InverseTransformPoint( worldBounds.center );
		Vector3 e = worldBounds.extents;
		Vector3 x = _pileRoot.InverseTransformVector( new Vector3( e.x, 0f, 0f ) );
		Vector3 y = _pileRoot.InverseTransformVector( new Vector3( 0f, e.y, 0f ) );
		Vector3 z = _pileRoot.InverseTransformVector( new Vector3( 0f, 0f, e.z ) );
		Vector3 localExtents = new Vector3(
			Mathf.Abs( x.x ) + Mathf.Abs( y.x ) + Mathf.Abs( z.x ),
			Mathf.Abs( x.y ) + Mathf.Abs( y.y ) + Mathf.Abs( z.y ),
			Mathf.Abs( x.z ) + Mathf.Abs( y.z ) + Mathf.Abs( z.z ) );
		return new Bounds( c, localExtents * 2f );
	}

	Bounds LocalBoundsToWorld( Bounds localBounds )
	{
		if ( _pileRoot == null )
			return localBounds;

		Vector3 c = _pileRoot.TransformPoint( localBounds.center );
		Vector3 e = localBounds.extents;
		Vector3 x = _pileRoot.TransformVector( new Vector3( e.x, 0f, 0f ) );
		Vector3 y = _pileRoot.TransformVector( new Vector3( 0f, e.y, 0f ) );
		Vector3 z = _pileRoot.TransformVector( new Vector3( 0f, 0f, e.z ) );
		Vector3 worldExtents = new Vector3(
			Mathf.Abs( x.x ) + Mathf.Abs( y.x ) + Mathf.Abs( z.x ),
			Mathf.Abs( x.y ) + Mathf.Abs( y.y ) + Mathf.Abs( z.y ),
			Mathf.Abs( x.z ) + Mathf.Abs( y.z ) + Mathf.Abs( z.z ) );
		return new Bounds( c, worldExtents * 2f );
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

	int CountVisible( TreasureDefinition definition )
	{
		return GetVisibleCount( definition );
	}

	int GetVisibleCount( TreasureDefinition definition )
	{
		if ( definition == null )
			return 0;
		return _visibleByDef.TryGetValue( definition, out int n ) ? n : 0;
	}

	void RefreshStreamResidency()
	{
		if ( _pileRoot == null || _heightfield == null )
			return;
		if ( _loot != null && !_loot.StreamingEnabled )
			return;

		if ( !TreasureProximitySleep.TryGetPlayerPosition( out Vector3 playerPos ) )
		{
			Camera cam = ResolveCamera();
			if ( cam == null )
				return;
			playerPos = cam.transform.position;
		}

		bool playerMoved = !_hasLastStreamPlayerPos
			|| PlanarDistanceSqr( playerPos, _lastStreamPlayerPos ) >= StreamPlayerMoveEpsilonSqr;
		if ( !playerMoved && !_streamResidencyDirty )
			return;

		_lastStreamPlayerPos = playerPos;
		_hasLastStreamPlayerPos = true;

		// Stream out live props past distance.
		bool despawnBudgetHit = false;
		for ( int i = _props.Count - 1; i >= 0 && _streamDespawnBudget > 0; i-- )
		{
			PropEntry prop = _props[ i ];
			if ( prop.Item == null || prop.Definition == null )
				continue;

			int latentIndex = prop.LatentIndex;
			if ( latentIndex < 0 || latentIndex >= _latent.Count )
				continue;

			LatentEntry latent = _latent[ latentIndex ];
			if ( latent.Taken )
				continue;

			float distSqr = PlanarDistanceSqr( playerPos, prop.WorldBounds.center );
			if ( IsCategoryInStreamRange( prop.Definition.category, distSqr, currentlyResident: true ) )
				continue;

			StreamDespawnProp( i );
			_streamDespawnBudget--;
			if ( _streamDespawnBudget <= 0 )
				despawnBudgetHit = true;
		}

		int latentCount = _latent.Count;
		if ( latentCount == 0 )
		{
			_streamLatentCursor = 0;
			ExposedLatentCount = 0;
			StreamedOutCount = 0;
			_streamResidencyDirty = despawnBudgetHit;
			return;
		}

		int cursor = _streamLatentCursor;
		if ( cursor < 0 || cursor >= latentCount )
			cursor = 0;

		int checks = Mathf.Min( MaxStreamLatentChecksPerFrame, latentCount );
		int seatsAttempted = 0;
		bool wrapped = false;

		for ( int checkedCount = 0; checkedCount < checks && _streamSpawnBudget > 0; checkedCount++ )
		{
			int i = cursor;
			cursor++;
			if ( cursor >= latentCount )
			{
				cursor = 0;
				wrapped = true;
			}

			LatentEntry latent = _latent[ i ];
			if ( latent.Taken || latent.Spawned || !latent.Exposed || latent.Definition == null )
				continue;

			if ( !CanSpawnBakedLatent( latent ) )
				continue;

			Vector3 worldCenter = _pileRoot.TransformPoint( latent.LocalBounds.center );
			float spawnDistSqr = PlanarDistanceSqr( playerPos, worldCenter );
			if ( !IsCategoryInStreamRange( latent.Definition.category, spawnDistSqr, currentlyResident: false ) )
				continue;

			_ = SeatLatentAsync( i, null );
			_streamSpawnBudget--;
			seatsAttempted++;
		}

		_streamLatentCursor = cursor;

		if ( wrapped )
			RecountStreamCounters( playerPos );

		if ( wrapped && seatsAttempted == 0 && !despawnBudgetHit )
			_streamResidencyDirty = false;
		else
			_streamResidencyDirty = true;
	}

	void RecountStreamCounters( Vector3 playerPos )
	{
		int exposed = 0;
		int streamedOut = 0;

		for ( int i = 0; i < _latent.Count; i++ )
		{
			LatentEntry latent = _latent[ i ];
			if ( latent.Taken || !latent.Exposed )
				continue;

			exposed++;
			if ( latent.Spawned || latent.Definition == null )
				continue;

			Vector3 worldCenter = _pileRoot != null
				? _pileRoot.TransformPoint( latent.LocalBounds.center )
				: latent.LocalBounds.center;
			float distSqr = PlanarDistanceSqr( playerPos, worldCenter );
			if ( !IsCategoryInStreamRange( latent.Definition.category, distSqr, currentlyResident: false ) )
				streamedOut++;
		}

		ExposedLatentCount = exposed;
		StreamedOutCount = streamedOut;
	}

	void StreamDespawnProp( int propIndex )
	{
		if ( propIndex < 0 || propIndex >= _props.Count )
			return;

		PropEntry prop = _props[ propIndex ];
		int latentIndex = prop.LatentIndex;
		TreasureItem item = prop.Item;

		if ( latentIndex >= 0 && latentIndex < _latent.Count )
		{
			LatentEntry latent = _latent[ latentIndex ];
			latent.Spawned = false;
			latent.PropIndex = -1;
			latent.Exposed = true;
			_latent[ latentIndex ] = latent;
		}

		DecrementVisible( prop.Definition );
		RemovePropSwap( propIndex );
		_streamResidencyDirty = true;

		if ( item != null )
			TreasureItemFactory.Despawn( item );
	}

	void RemovePropSwap( int propIndex )
	{
		if ( propIndex < 0 || propIndex >= _props.Count )
			return;

		int last = _props.Count - 1;
		if ( propIndex != last )
		{
			PropEntry swapped = _props[ last ];
			_props[ propIndex ] = swapped;

			int swappedLatent = swapped.LatentIndex;
			if ( swappedLatent >= 0 && swappedLatent < _latent.Count )
			{
				LatentEntry latent = _latent[ swappedLatent ];
				latent.PropIndex = propIndex;
				_latent[ swappedLatent ] = latent;
			}
		}

		_props.RemoveAt( last );
	}

	bool CanSpawnBakedLatent( LatentEntry latent )
	{
		if ( latent.IsAuthored )
			return true;

		return GoldPileTreasurePlacement.HasTreasureSurfaceBelow( _pileRoot, latent.LocalPos, _latentSurfaceNeighborhoodCells );
	}

	bool IsLatentInStreamRange( int latentIndex, bool currentlyResident )
	{
		if ( !TreasureProximitySleep.TryGetPlayerPosition( out Vector3 playerPos ) )
		{
			Camera cam = ResolveCamera();
			if ( cam == null )
				return true;
			playerPos = cam.transform.position;
		}

		return IsLatentInStreamRange( latentIndex, currentlyResident, playerPos );
	}

	bool IsLatentInStreamRange( int latentIndex, bool currentlyResident, Vector3 playerPos )
	{
		if ( _loot != null && !_loot.StreamingEnabled )
			return true;

		if ( latentIndex < 0 || latentIndex >= _latent.Count )
			return false;

		LatentEntry latent = _latent[ latentIndex ];
		if ( latent.Definition == null )
			return false;

		Vector3 worldCenter = _pileRoot != null
			? _pileRoot.TransformPoint( latent.LocalBounds.center )
			: latent.LocalBounds.center;
		float distSqr = PlanarDistanceSqr( playerPos, worldCenter );
		return IsCategoryInStreamRange( latent.Definition.category, distSqr, currentlyResident );
	}

	bool IsCategoryInStreamRange( TreasureCategory category, float distanceMetersSqr, bool currentlyResident )
	{
		if ( _streamSettings == null )
			return true;
		return _streamSettings.IsPropInStreamRange( category, distanceMetersSqr, currentlyResident );
	}

	static float PlanarDistanceSqr( Vector3 a, Vector3 b )
	{
		float dx = a.x - b.x;
		float dz = a.z - b.z;
		return dx * dx + dz * dz;
	}

	void UpdateLatentPoseFromWorld( int latentIndex, Vector3 worldPos, Quaternion worldRot )
	{
		if ( latentIndex < 0 || latentIndex >= _latent.Count || _pileRoot == null )
			return;

		LatentEntry latent = _latent[ latentIndex ];
		Vector3 localPos = _pileRoot.InverseTransformPoint( worldPos );
		Quaternion localRot = Quaternion.Inverse( _pileRoot.rotation ) * worldRot;
		float scale = latent.Scale > 0.01f ? latent.Scale : 1f;
		Vector3 size = latent.LocalBounds.size;
		if ( size.sqrMagnitude < 0.0001f )
			size = Vector3.one * Mathf.Max( 0.1f, scale );

		latent.LocalPos = localPos;
		latent.LocalRot = localRot;
		latent.LocalBounds = new Bounds( localPos, size );
		_latent[ latentIndex ] = latent;
	}

	void DecrementVisible( TreasureDefinition definition )
	{
		if ( definition == null )
			return;
		if ( !_visibleByDef.TryGetValue( definition, out int n ) )
			return;
		n--;
		if ( n <= 0 )
			_visibleByDef.Remove( definition );
		else
			_visibleByDef[ definition ] = n;
	}

	int FindPropIndex( TreasureItem item )
	{
		for ( int i = 0; i < _props.Count; i++ )
		{
			if ( _props[ i ].Item == item )
				return i;
		}

		return -1;
	}

	bool TryResolveProp( int latentIndex, out int propIndex, out PropEntry prop )
	{
		propIndex = -1;
		prop = default;
		if ( latentIndex < 0 || latentIndex >= _latent.Count )
			return false;

		LatentEntry latent = _latent[ latentIndex ];
		int idx = latent.PropIndex;
		if ( idx >= 0 && idx < _props.Count && _props[ idx ].LatentIndex == latentIndex )
		{
			propIndex = idx;
			prop = _props[ idx ];
			return true;
		}

		idx = FindPropByLatent( latentIndex );
		if ( idx < 0 )
			return false;

		propIndex = idx;
		prop = _props[ idx ];
		latent.PropIndex = idx;
		_latent[ latentIndex ] = latent;
		return true;
	}

	int FindPropByLatent( int latentIndex )
	{
		for ( int i = 0; i < _props.Count; i++ )
		{
			if ( _props[ i ].LatentIndex == latentIndex )
				return i;
		}

		return -1;
	}

	public static bool IsLargeProp( TreasureDefinition definition )
	{
		if ( definition == null )
			return false;

		return definition.category != TreasureCategory.Coin;
	}

	int CountExpectedRemainderLatents()
	{
		if ( _definition == null || _definition.treasureContents == null )
			return 0;

		int total = 0;
		for ( int i = 0; i < _definition.treasureContents.Length; i++ )
		{
			TreasurePileEntry entry = _definition.treasureContents[ i ];
			if ( entry.treasure == null || !IsLargeProp( entry.treasure ) || entry.count <= 0 )
				continue;
			total += entry.count;
		}

		return total;
	}

	static string AppendMissingType( string perType, TreasureDefinition def, int placed, int expected )
	{
		if ( def == null || placed >= expected )
			return perType;

		string piece = $"{def.name} {placed}/{expected}";
		if ( string.IsNullOrEmpty( perType ) )
			return piece;
		return perType + "; " + piece;
	}

	string PileLogName()
	{
		if ( _owner != null )
			return _owner.name;
		return name;
	}

	void WarnTreasureMissing( string phase, int expected, int placed, string details )
	{
		int missing = Mathf.Max( 0, expected - placed );
		if ( missing <= 0 )
			return;

		string extra = string.IsNullOrEmpty( details ) ? "" : " " + details + ".";
		Debug.LogWarning(
			$"[GoldPileArtifactProps] {phase}: pile '{PileLogName()}' placed {placed}/{expected} definition latents "
			+ $"({missing} missing). All treasure needs to spawn.{extra}",
			this );
	}

	public bool IsPickable( TreasureItem item )
	{
		int index = FindPropIndex( item );
		if ( index < 0 )
			return false;
		return _props[ index ].Pickable;
	}
}
