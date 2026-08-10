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
		public int PropIndex;
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
	float _placementRadiusFraction = 0.88f;
	float _placementMinSpacing = 0.35f;
	float _treasureRadialPower = 1.25f;
	float _treasureHeightBias = 0.75f;
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
			_treasurePickupOutsideFraction = Mathf.Clamp( definition.treasurePickupOutsideFraction, 0.05f, 0.95f );
			_treasureReleaseOutsideFraction = Mathf.Clamp( definition.treasureReleaseOutsideFraction, 0.5f, 1f );
		}

		_pileLootSeed = WorldLootSeed.GetPileEffectiveSeed( _pileRoot, authoredLayoutSeed );
		BuildLatentEntries();
		StartReveal( bindId, spatialFilter: false, default, 0f, startIndex: 0 );
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
		_pendingRevealWorld = worldCenter;
		_pendingRevealRadius = Mathf.Max( 0.05f, radius );
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

	void BuildLatentEntries()
	{
		_latent.Clear();
		ExposedLatentCount = 0;
		StreamedOutCount = 0;
		if ( _definition == null || _definition.treasureContents == null || _heightfield == null )
			return;

		List<Bounds> occupiedBounds = new List<Bounds>( 128 );
		if ( _loot != null )
			_loot.CollectVolumeBoundsOccupancy( occupiedBounds );

		for ( int e = 0; e < _definition.treasureContents.Length; e++ )
		{
			TreasurePileEntry entry = _definition.treasureContents[ e ];
			TreasureDefinition def = entry.treasure;
			if ( def == null || !IsLargeProp( def ) || entry.count <= 0 )
				continue;

			// Always author entry.count latent poses from the seed so layout is identical every run.
			// Inventory remaining only gates how many can spawn/reveal later.
			int count = entry.count;
			for ( int i = 0; i < count; i++ )
			{
				int unitIndex = WorldLootSeed.StableUnitIndex( e, i );
				float scale = def.worldScale.x;
				if ( scale < 0.01f )
					scale = 1f;

				float probe = Mathf.Max( 0.08f, scale * 0.35f );
				float spacing = Mathf.Max( _placementMinSpacing, probe * 1.5f );
				GoldPileTreasurePlacement.VolumePose pose;
				Bounds localBounds;
				bool placed = GoldPileTreasurePlacement.TrySampleValidVolumePose(
					_heightfield,
					_pileLootSeed,
					unitIndex,
					_placementRadiusFraction,
					scale,
					probe,
					_treasureRadialPower,
					_treasureHeightBias,
					def.category,
					occupiedBounds,
					spacing,
					null,
					out pose,
					out localBounds );
				if ( !placed )
				{
					placed = GoldPileTreasurePlacement.TrySampleValidVolumePose(
						_heightfield,
						_pileLootSeed,
						unitIndex + 5003,
						_placementRadiusFraction,
						scale,
						probe,
						_treasureRadialPower,
						0f,
						def.category,
						null,
						0f,
						null,
						out pose,
						out localBounds );
				}

				if ( !placed )
				{
					// Last resort: more salts, no spacing — never drop authored treasure units.
					for ( int attempt = 0; attempt < 8 && !placed; attempt++ )
					{
						placed = GoldPileTreasurePlacement.TrySampleValidVolumePose(
							_heightfield,
							_pileLootSeed,
							unitIndex + 9001 + attempt * 137,
							_placementRadiusFraction,
							scale,
							probe,
							_treasureRadialPower,
							0f,
							def.category,
							null,
							0f,
							null,
							out pose,	
							out localBounds );
					}
				}

				if ( !placed )
				{
					Debug.LogWarning(
						$"[GoldPileArtifactProps] Forced latent seat failed for {def.name} unit {i}; using mound center fallback.",
						this );
					float half = _heightfield.WorldSize * 0.25f;
					float lx = GoldPileTreasurePlacement.HashRange( _pileLootSeed, unitIndex * 17 + 3, -half, half );
					float lz = GoldPileTreasurePlacement.HashRange( _pileLootSeed, unitIndex * 17 + 5, -half, half );
					float surface = _heightfield.SampleNormalized( lx, lz ) * _heightfield.MaxHeight;
					float embed = Mathf.Max( 0.05f, probe );
					pose = new GoldPileTreasurePlacement.VolumePose
					{
						LocalPos = new Vector3( lx, surface - embed, lz ),
						LocalRot = GoldPileTreasurePlacement.HashRotation( _pileLootSeed, unitIndex ),
						Scale = scale,
						ProbeRadius = probe
					};
					localBounds = GoldPileTreasurePlacement.LocalAabbFromPose(
						pose.LocalPos,
						pose.LocalRot,
						scale,
						null );
					placed = true;
				}

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
					PropIndex = -1
				} );
			}
		}
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
				_heightfield.GroundLevel,
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
		for ( int i = 0; i < _latent.Count; i++ )
		{
			if ( i == latentIndex )
				continue;
			if ( _latent[ i ].Taken )
				continue;
			occupiedBounds.Add( _latent[ i ].LocalBounds );
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
			out Bounds localBounds ) )
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
		if ( entry.CachedCollider != null )
			entry.WorldBounds = entry.CachedCollider.bounds;
		else
			entry.WorldBounds = GetItemBounds( item );
	}

	void RefreshPropWorldBoundsIfNeeded()
	{
		if ( _pileRoot == null || !_pileRoot.hasChanged )
			return;

		_pileRoot.hasChanged = false;
		for ( int i = 0; i < _props.Count; i++ )
		{
			PropEntry entry = _props[ i ];
			if ( entry.Item == null )
				continue;

			if ( entry.CachedCollider != null )
				entry.WorldBounds = entry.CachedCollider.bounds;
			else
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

		bool enabled = prop.RenderVisible && prop.Pickable;
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

		float scale = definition.worldScale.x;
		if ( scale < 0.01f )
			scale = 1f;

		float probe = Mathf.Max( 0.08f, scale * 0.35f );
		float spacing = Mathf.Max( _placementMinSpacing, probe * 1.5f );
		List<Bounds> occupiedBounds = new List<Bounds>( _latent.Count + 8 );
		for ( int i = 0; i < _latent.Count; i++ )
		{
			if ( _latent[ i ].Taken )
				continue;
			occupiedBounds.Add( _latent[ i ].LocalBounds );
		}

		if ( _loot != null )
			_loot.CollectVolumeBoundsOccupancy( occupiedBounds );

		GoldPileTreasurePlacement.VolumePose pose;
		Bounds localBounds;
		bool placed = GoldPileTreasurePlacement.TrySampleValidVolumePose(
			_heightfield,
			_pileLootSeed,
			unitSalt,
			_placementRadiusFraction,
			scale,
			probe,
			_treasureRadialPower,
			_treasureHeightBias,
			definition.category,
			occupiedBounds,
			spacing,
			null,
			out pose,
			out localBounds );
		if ( !placed )
		{
			placed = GoldPileTreasurePlacement.TrySampleValidVolumePose(
				_heightfield,
				_pileLootSeed,
				unitSalt + 5003,
				_placementRadiusFraction,
				scale,
				probe,
				_treasureRadialPower,
				0f,
				definition.category,
				null,
				0f,
				null,
				out pose,
				out localBounds );
		}

		if ( !placed )
			return -1;

		_latent.Add( new LatentEntry
		{
			Definition = definition,
			LocalPos = pose.LocalPos,
			LocalRot = pose.LocalRot,
			Scale = scale,
			LocalBounds = localBounds,
			Taken = false,
			Spawned = false,
			Exposed = false,
			PropIndex = -1
		} );
		return _latent.Count - 1;
	}

	int AppendLatentFromWorld( TreasureDefinition definition, Vector3 preferredWorld )
	{
		if ( definition == null || _pileRoot == null || _heightfield == null )
			return -1;

		Vector3 local = _pileRoot.InverseTransformPoint( preferredWorld );
		float half = _heightfield.WorldSize * 0.5f * _placementRadiusFraction;
		local.x = Mathf.Clamp( local.x, -half, half );
		local.z = Mathf.Clamp( local.z, -half, half );
		float surface = _heightfield.SampleNormalized( local.x, local.z ) * _heightfield.MaxHeight;
		float scale = definition.worldScale.x;
		if ( scale < 0.01f )
			scale = 1f;
		float probe = Mathf.Max( 0.08f, scale * 0.35f );
		float floorY = GoldPileTreasurePlacement.FloorClearanceY( _heightfield.GroundLevel, probe );
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
				occupiedBounds.Add( _latent[ i ].LocalBounds );
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
				out Bounds validBounds ) )
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
					occupiedBounds.Add( _latent[ i ].LocalBounds );
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
					out Bounds validBounds ) )
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
			if ( useChunkFrustum )
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

	static Bounds GetItemBounds( TreasureItem item )
	{
		Collider col = item.GetComponent<Collider>();
		if ( col != null )
			return col.bounds;

		Renderer renderer = item.GetComponentInChildren<Renderer>();
		if ( renderer != null )
			return renderer.bounds;

		return new Bounds( item.transform.position, Vector3.one * 0.5f );
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

	public bool IsPickable( TreasureItem item )
	{
		int index = FindPropIndex( item );
		if ( index < 0 )
			return false;
		return _props[ index ].Pickable;
	}
}
