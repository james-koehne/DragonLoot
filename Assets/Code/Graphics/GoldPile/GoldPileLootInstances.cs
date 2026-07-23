using System.Collections.Generic;
using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Multi-type buried GPU-instanced loot covering the heightfield. Pickable by instance.
/// Draws through frustum/LOD chunk streaming over the Drawn visibility pool.
/// Per-type distance culling: coins at LOD0, gems through LOD1, large props always (frustum only).
/// </summary>
[DisallowMultipleComponent]
public class GoldPileLootInstances : MonoBehaviour
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
		public List<int> SlotIndices;
		public Matrix4x4[] Matrices;
		public Matrix4x4[] DrawBatch;
		public Matrix4x4[] StreamMatrices;
		public int StreamCount;
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
	public bool StreamingEnabled => _streamingEnabled;
	public bool IsReady => _ready;

	public void SetStreamingEnabled( bool enabled )
	{
		_streamingEnabled = enabled;
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
	readonly HashSet<int> _renderedChunkKeys = new HashSet<int>();
	readonly HashSet<int> _visibleChunkKeys = new HashSet<int>();
	readonly HashSet<int> _pickPrioritySlots = new HashSet<int>();

	GoldPileHeightfield _heightfield;
	Transform _pileRoot;
	TreasurePileDefinition _definition;
	Slot[] _slots;
	BatchGroup[] _batches;
	List<int>[] _cellLists;
	List<int>[] _chunkDrawnSlots;
	Dictionary<TreasureDefinition, int> _remaining;
	int[] _visibleCountByEntry;
	List<int> _eligibleScratch;
	bool[] _cellUsedScratch;
	bool _ready;
	bool _streamingEnabled = true;
	bool _drawCacheDirty = true;
	ulong _lastStreamFingerprint;
	float _pickRadius = 0.45f;
	float _buryDepth = 0.18f;
	float _initialRevealDepth = 0.06f;
	int _maxVisibleTotal = 400;
	float _placementMinSpacing = 0.35f;
	float _placementJitter = 0.3f;
	float _placementRadiusFraction = 0.88f;
	float _placementScaleJitter = 0.1f;
	int _bindSerial;
	Camera _cachedCamera;

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

	public async Task BindAsync(
		TreasurePileDefinition definition,
		GoldPileHeightfield heightfield,
		Transform pileRoot )
	{
		int bindId = ++_bindSerial;
		_definition = definition;
		_heightfield = heightfield;
		_pileRoot = pileRoot != null ? pileRoot : transform;
		_ready = false;
		_drawCacheDirty = true;
		_lastStreamFingerprint = 0;
		ReleaseVisualHandles();
		_streamer.Clear();
		_chunkGrid.Release();

		if ( definition == null || heightfield == null )
			return;

		_pickRadius = definition.pickRadius;
		_buryDepth = definition.buryDepth;
		_initialRevealDepth = definition.initialRevealDepth;
		_maxVisibleTotal = Mathf.Max( 1, definition.maxVisibleTotal );
		_placementMinSpacing = Mathf.Max( 0.05f, definition.placementMinSpacing );
		_placementJitter = Mathf.Clamp( definition.placementJitter, 0f, 0.49f );
		_placementRadiusFraction = Mathf.Clamp( definition.placementRadiusFraction, 0.4f, 1f );
		_placementScaleJitter = Mathf.Clamp( definition.placementScaleJitter, 0f, 0.5f );

		EnsureStreamSettings();

		BuildRemaining( definition );
		await EnsureFallbackVisualsAsync( definition );
		if ( !IsBindTargetAlive( bindId ) )
			return;

		Dictionary<TreasureDefinition, VisualAssets> visuals = await ResolveVisualsAsync( definition );
		if ( !IsBindTargetAlive( bindId ) )
			return;

		int worldSeed = GoldPileChunkGrid.HashChunkSeed(
			_pileRoot.position.GetHashCode(),
			Mathf.RoundToInt( _pileRoot.position.x * 10f ),
			Mathf.RoundToInt( _pileRoot.position.z * 10f ) );
		_chunkGrid.Build( _heightfield, _pileRoot, streamSettings.chunkSize, worldSeed );
		_streamer.Bind( _chunkGrid, streamSettings );
		AllocateChunkDrawnLists();

		BuildSlots( definition, visuals );
		RebuildVisibility();
		if ( !IsBindTargetAlive( bindId ) )
			return;

		_ready = _slots != null;

		if ( logInitialSpawnStats )
			LogInitialSpawnStats();
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
		const string Path = "Assets/Materials/Shaders/GoldPile/GoldPileLootStreamSettings.asset";
		streamSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<GoldPileLootStreamSettings>( Path );
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
		sb.Append( slotCount ).Append( " slots (cap " ).Append( _maxVisibleTotal ).Append( ")" );
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

		if ( _definition != null && _definition.contents != null && _visibleCountByEntry != null )
		{
			sb.Append( "\n  By treasure type:" );
			for ( int e = 0; e < _definition.contents.Length && e < _visibleCountByEntry.Length; e++ )
			{
				TreasurePileEntry entry = _definition.contents[ e ];
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

		_chunkGrid.MarkDirtyInRadius( worldCenter, radius );

		Vector3 local = _pileRoot.InverseTransformPoint( worldCenter );
		float radiusSq = radius * radius;
		bool eligibilityChanged = false;
		bool patchedDrawn = false;

		for ( int i = 0; i < _slots.Length; i++ )
		{
			if ( _slots[ i ].Taken )
				continue;

			float dx = _slots[ i ].LocalPos.x - local.x;
			float dz = _slots[ i ].LocalPos.z - local.z;
			if ( dx * dx + dz * dz > radiusSq )
				continue;

			Slot slot = _slots[ i ];
			ConformSlot( ref slot );
			_slots[ i ] = slot;

			bool eligible = IsEligible( slot );
			if ( !eligible && slot.Drawn )
			{
				// Deposit buried a previously drawn slot.
				eligibilityChanged = true;
			}
			else if ( eligible && !slot.Drawn && slot.EmbedDepth > _initialRevealDepth )
			{
				// Carve revealed a previously buried slot that may need a draw seat.
				eligibilityChanged = true;
			}

			if ( slot.Drawn && slot.MatrixIndex >= 0 )
			{
				PatchDrawnSlotMatrix( i );
				patchedDrawn = true;
			}
		}

		if ( eligibilityChanged )
			RebuildVisibility();
		else if ( patchedDrawn )
			InvalidateDrawCache();
	}

	public void RefreshAll()
	{
		if ( !_ready )
			return;

		for ( int i = 0; i < _slots.Length; i++ )
		{
			if ( _slots[ i ].Taken )
				continue;
			ConformSlot( ref _slots[ i ] );
		}

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
					if ( !PassesStreamFilter( idx, slot ) )
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
		slot.Drawn = false;
		_slots[ slotIndex ] = slot;
		RemoveFromCell( slotIndex );
		if ( wasDrawn )
			RebuildVisibility();
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

		if ( !TryMarkOneInventorySlotTaken( definition, out bool wasDrawn ) )
			return false;

		if ( wasDrawn )
			RebuildVisibility();

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

		int want = Mathf.Min( count, left );
		int taken = 0;
		bool needRebuild = false;

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

			needRebuild = true;
			slot.Taken = true;
			slot.Drawn = false;
			_slots[ i ] = slot;
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

		if ( needRebuild )
			RebuildVisibility();

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
		slot.Drawn = false;
		_slots[ slotIndex ] = slot;
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

		switch ( definition.category )
		{
			case TreasureCategory.Coin:
			case TreasureCategory.Gem:
				return true;
			default:
				return false;
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
			|| CountDrawnForDefinition( definition ) >= GetMaxVisibleForDefinition( definition );

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
			slot.EmbedDepth = Mathf.Min( slot.EmbedDepth, _initialRevealDepth * 0.5f );
			if ( requireVisible )
			{
				Vector3 placeLocal = localPreferred;
				if ( _heightfield != null )
				{
					float half = _heightfield.WorldSize * 0.5f;
					placeLocal.x = Mathf.Clamp( placeLocal.x, -half, half );
					placeLocal.z = Mathf.Clamp( placeLocal.z, -half, half );
				}

				float surface = _heightfield != null
					? _heightfield.SampleNormalized( placeLocal.x, placeLocal.z ) * _heightfield.MaxHeight
					: slot.PlaceSurfaceHeight;
				slot.LocalPos = new Vector3(
					placeLocal.x,
					surface - slot.EmbedDepth + slot.Scale * 0.05f,
					placeLocal.z );
				slot.PlaceSurfaceHeight = surface;
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
			ConformSlot( ref slot );
			_slots[ slotIndex ] = slot;
		}

		MarkDrawn( slotIndex );
		_pickPrioritySlots.Add( slotIndex );
		RebuildBatchMatrices();
		InvalidateDrawCache();
	}

	int GetMaxVisibleForDefinition( TreasureDefinition definition )
	{
		if ( _definition == null || definition == null || _definition.contents == null )
			return _maxVisibleTotal;

		for ( int i = 0; i < _definition.contents.Length; i++ )
		{
			if ( _definition.contents[ i ].treasure != definition )
				continue;
			return _definition.GetMaxVisibleFor( _definition.contents[ i ] );
		}

		return _definition.defaultPerTypeVisible;
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
		if ( !_ready || _batches == null )
		{
			LastDrawnCount = 0;
			LastCulledCount = 0;
			return;
		}

		RefreshStreamState();

		ulong fingerprint = ComputeStreamFingerprint();
		if ( fingerprint != _lastStreamFingerprint )
		{
			_lastStreamFingerprint = fingerprint;
			_drawCacheDirty = true;
		}

		if ( _drawCacheDirty )
			RebuildStreamDrawCache();

		DrawFromStreamCache();
	}

	void InvalidateDrawCache()
	{
		_drawCacheDirty = true;
	}

	ulong ComputeStreamFingerprint()
	{
		unchecked
		{
			ulong h = _streamingEnabled ? 1u : 0u;
			if ( !_streamingEnabled || _chunkGrid.ChunkCount == 0 )
				return h;

			IReadOnlyList<GoldPileChunk> chunks = _chunkGrid.Chunks;
			for ( int i = 0; i < chunks.Count; i++ )
			{
				GoldPileChunk chunk = chunks[ i ];
				h = h * 31u + ( ulong )( int )chunk.State;
				h = h * 31u + ( ulong )chunk.Lod;
				h = h * 31u + ( chunk.FrustumVisible ? 1u : 0u );
			}

			return h;
		}
	}

	void RebuildStreamDrawCache()
	{
		_drawCacheDirty = false;
		CacheRebuildCount++;

		if ( _chunkGrid.ChunkCount > 0 )
		{
			IReadOnlyList<GoldPileChunk> chunks = _chunkGrid.Chunks;
			for ( int i = 0; i < chunks.Count; i++ )
				chunks[ i ].LastDrawnCount = 0;
		}

		int culled = 0;
		int drawn = 0;
		bool filter = _streamingEnabled && streamSettings != null && _chunkGrid.ChunkCount > 0;

		for ( int b = 0; b < _batches.Length; b++ )
		{
			BatchGroup group = _batches[ b ];
			if ( group.SlotIndices == null )
				continue;

			int poolCount = group.SlotIndices.Count;
			if ( group.StreamMatrices == null || group.StreamMatrices.Length < poolCount )
				group.StreamMatrices = new Matrix4x4[ Mathf.Max( poolCount, 16 ) ];

			int fill = 0;
			for ( int i = 0; i < poolCount; i++ )
			{
				int slotIndex = group.SlotIndices[ i ];
				Slot slot = _slots[ slotIndex ];
				if ( slot.Taken || !slot.Drawn )
				{
					culled++;
					continue;
				}

				if ( filter && !PassesStreamFilter( slotIndex, slot ) )
				{
					culled++;
					continue;
				}

				int matrixIndex = slot.MatrixIndex;
				if ( matrixIndex < 0 || group.Matrices == null || matrixIndex >= group.Matrices.Length )
					matrixIndex = i;

				group.StreamMatrices[ fill++ ] = group.Matrices[ matrixIndex ];
				drawn++;

				if ( filter )
				{
					GoldPileChunk chunk = _chunkGrid.GetChunk( slot.ChunkX, slot.ChunkZ );
					if ( chunk != null )
						chunk.LastDrawnCount++;
				}
			}

			group.StreamCount = fill;
			_batches[ b ] = group;
		}

		LastCulledCount = culled;
		DrawnPoolCount = 0;
		for ( int b = 0; b < _batches.Length; b++ )
		{
			if ( !_batches[ b ].IsPrimaryPart || _batches[ b ].SlotIndices == null )
				continue;
			DrawnPoolCount += _batches[ b ].SlotIndices.Count;
		}
	}

	void DrawFromStreamCache()
	{
		int drawn = 0;
		Bounds pileBounds = GetPileWorldDrawBounds();

		for ( int b = 0; b < _batches.Length; b++ )
		{
			BatchGroup group = _batches[ b ];
			if ( group.Mesh == null || group.Material == null || group.StreamMatrices == null )
				continue;

			int count = group.StreamCount;
			if ( count <= 0 )
				continue;

			if ( group.Material.enableInstancing == false )
				group.Material.enableInstancing = true;

			RenderParams rp = new RenderParams( group.Material )
			{
				layer = gameObject.layer,
				shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
				receiveShadows = false,
				renderingLayerMask = uint.MaxValue,
				worldBounds = pileBounds
			};

			int submesh = Mathf.Clamp( group.SubmeshIndex, 0, Mathf.Max( 0, group.Mesh.subMeshCount - 1 ) );

			// Large artifact counts are tiny; draw without GPU instancing so URP Lit
			// materials that fail to instance still show.
			if ( group.AlwaysDraw )
			{
				for ( int i = 0; i < count; i++ )
				{
					Graphics.RenderMesh( rp, group.Mesh, submesh, group.StreamMatrices[ i ] );
					drawn++;
				}

				continue;
			}

			for ( int start = 0; start < count; start += BatchSize )
			{
				int n = Mathf.Min( BatchSize, count - start );
				for ( int i = 0; i < n; i++ )
					group.DrawBatch[ i ] = group.StreamMatrices[ start + i ];
				Graphics.RenderMeshInstanced( rp, group.Mesh, submesh, group.DrawBatch, n );
				drawn += n;
			}
		}

		LastDrawnCount = drawn;
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
		{
			_renderedChunkKeys.Clear();
			return;
		}

		RefreshStreamState();
	}

	void RefreshStreamState()
	{
		_renderedChunkKeys.Clear();
		_visibleChunkKeys.Clear();

		if ( !_streamingEnabled || streamSettings == null || _chunkGrid.ChunkCount == 0 )
			return;

		if ( !TreasureProximitySleep.TryGetPlayerPosition( out Vector3 playerPos ) )
		{
			Camera camFallback = ResolveCamera();
			if ( camFallback == null )
				return;
			playerPos = camFallback.transform.position;
		}

		Camera camera = ResolveCamera();
		_streamer.Tick( playerPos, camera );

		IReadOnlyList<GoldPileChunk> visible = _streamer.VisibleChunks;
		for ( int i = 0; i < visible.Count; i++ )
		{
			GoldPileChunk chunk = visible[ i ];
			_visibleChunkKeys.Add( ChunkKey( chunk.Coord.X, chunk.Coord.Z ) );
		}

		IReadOnlyList<GoldPileChunk> rendered = _streamer.RenderedChunks;
		for ( int i = 0; i < rendered.Count; i++ )
		{
			GoldPileChunk chunk = rendered[ i ];
			_renderedChunkKeys.Add( ChunkKey( chunk.Coord.X, chunk.Coord.Z ) );
		}
	}

	bool PassesStreamFilter( int slotIndex, Slot slot )
	{
		if ( _pickPrioritySlots.Contains( slotIndex ) )
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

		int key = ChunkKey( slot.ChunkX, slot.ChunkZ );
		if ( !_renderedChunkKeys.Contains( key ) )
			return false;

		if ( chunk.State != GoldPileChunkStreamState.Rendered )
			return false;

		float density = streamSettings.DensityForLod( chunk.Lod );
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
		if ( tier == LootStreamCullTierCoin )
			return lod <= 0;
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

	static int ChunkKey( int x, int z )
	{
		return ( z << 16 ) ^ ( x & 0xFFFF );
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
		if ( definition.contents == null )
			return;

		for ( int i = 0; i < definition.contents.Length; i++ )
		{
			TreasurePileEntry entry = definition.contents[ i ];
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
			return;

		if ( pileMaterial == null )
			pileMaterial = CreateRuntimePileMaterial( pileShader, fallbackMaterial );

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
		if ( definition.contents == null )
			return map;

		for ( int i = 0; i < definition.contents.Length; i++ )
		{
			TreasureDefinition def = definition.contents[ i ].treasure;
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

		return map;
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
		ReleaseVisualHandles();
		_streamer.Clear();
		_chunkGrid.Release();
	}

	void OnDisable()
	{
		_cachedCamera = null;
		_streamer.Clear();
		LastDrawnCount = 0;
		LastCulledCount = 0;
	}

	void BuildSlots( TreasurePileDefinition definition, Dictionary<TreasureDefinition, VisualAssets> visuals )
	{
		List<Slot> list = new List<Slot>( definition.TotalUnits() );
		Dictionary<TreasureDefinition, int> batchKeyByDef = new Dictionary<TreasureDefinition, int>();
		List<BatchGroup> batches = new List<BatchGroup>();

		if ( definition.contents == null )
		{
			_slots = new Slot[ 0 ];
			_batches = new BatchGroup[ 0 ];
			return;
		}

		float half = _heightfield.WorldSize * 0.5f;
		_visibleCountByEntry = new int[ definition.contents.Length ];

		List<int> entryIndices = new List<int>( definition.TotalUnits() );
		List<TreasureDefinition> unitDefs = new List<TreasureDefinition>( definition.TotalUnits() );

		for ( int e = 0; e < definition.contents.Length; e++ )
		{
			TreasurePileEntry entry = definition.contents[ e ];
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

			if ( !batchKeyByDef.TryGetValue( entry.treasure, out int batchKey ) )
			{
				batchKey = batches.Count;
				batchKeyByDef[ entry.treasure ] = batchKey;
				List<int> sharedSlots = new List<int>();
				bool alwaysDraw = GetLootStreamCullTier( entry.treasure ) >= LootStreamCullTierLarge;
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
						SlotIndices = sharedSlots,
						Matrices = null,
						DrawBatch = new Matrix4x4[ BatchSize ]
					} );
				}
			}

			for ( int i = 0; i < entry.count; i++ )
			{
				entryIndices.Add( e );
				unitDefs.Add( entry.treasure );
			}
		}

		ShuffleParallel( entryIndices, unitDefs );

		float usableHalf = half * _placementRadiusFraction;
		float extent = usableHalf * 2f;
		int gridSide = Mathf.Max( 2, Mathf.FloorToInt( extent / _placementMinSpacing ) );
		float cell = extent / gridSide;
		float jitter = cell * _placementJitter;
		float minSurface = 0.05f * _heightfield.MaxHeight;

		List<Vector2> candidates = new List<Vector2>( gridSide * gridSide );
		for ( int gz = 0; gz < gridSide; gz++ )
		{
			for ( int gx = 0; gx < gridSide; gx++ )
			{
				float lx = -usableHalf + ( gx + 0.5f ) * cell + Random.Range( -jitter, jitter );
				float lz = -usableHalf + ( gz + 0.5f ) * cell + Random.Range( -jitter, jitter );
				lx = Mathf.Clamp( lx, -usableHalf, usableHalf );
				lz = Mathf.Clamp( lz, -usableHalf, usableHalf );
				if ( _heightfield.SampleNormalized( lx, lz ) * _heightfield.MaxHeight < minSurface )
					continue;
				candidates.Add( new Vector2( lx, lz ) );
			}
		}

		ShuffleList( candidates );

		int unitCount = entryIndices.Count;
		int candidateCount = candidates.Count;

		for ( int i = 0; i < unitCount; i++ )
		{
			TreasureDefinition def = unitDefs[ i ];
			int entryIndex = entryIndices[ i ];
			int batchKey = batchKeyByDef[ def ];

			Vector2 pos;
			if ( i < candidateCount )
			{
				pos = candidates[ i ];
			}
			else
			{
				pos = new Vector2(
					Random.Range( -usableHalf, usableHalf ),
					Random.Range( -usableHalf, usableHalf ) );
			}

			float surface = _heightfield.SampleNormalized( pos.x, pos.y ) * _heightfield.MaxHeight;
			if ( surface < minSurface && i >= candidateCount )
			{
				pos *= 0.5f;
				surface = _heightfield.SampleNormalized( pos.x, pos.y ) * _heightfield.MaxHeight;
			}
			float embed = Mathf.Lerp( _initialRevealDepth * 0.35f, _buryDepth, Random.value );
			if ( GetLootStreamCullTier( def ) >= LootStreamCullTierLarge )
				embed = Mathf.Min( embed, _initialRevealDepth * 0.35f );

			float scale = def.worldScale.x;
			if ( scale < 0.01f )
				scale = 0.12f;

			float scaleMul = 1f;
			if ( _placementScaleJitter > 0f )
				scaleMul = Random.Range( 1f - _placementScaleJitter, 1f + _placementScaleJitter );

			Slot slot = new Slot
			{
				Definition = def,
				EntryIndex = entryIndex,
				LocalPos = new Vector3( pos.x, 0f, pos.y ),
				LocalRot = Quaternion.identity,
				Scale = scale * scaleMul,
				EmbedDepth = embed,
				PlaceSurfaceHeight = surface,
				Taken = false,
				Drawn = false,
				BatchKey = batchKey,
				MatrixIndex = -1
			};
			ConformSlot( ref slot );
			list.Add( slot );
		}

		_slots = list.ToArray();
		_batches = batches.ToArray();
		_cellLists = new List<int>[ spatialCells * spatialCells ];
		for ( int i = 0; i < _cellLists.Length; i++ )
			_cellLists[ i ] = new List<int>( 16 );

		for ( int i = 0; i < _slots.Length; i++ )
			AssignCell( i );
	}

	static void ShuffleParallel( List<int> a, List<TreasureDefinition> b )
	{
		for ( int i = a.Count - 1; i > 0; i-- )
		{
			int j = Random.Range( 0, i + 1 );
			int tmpA = a[ i ];
			a[ i ] = a[ j ];
			a[ j ] = tmpA;
			TreasureDefinition tmpB = b[ i ];
			b[ i ] = b[ j ];
			b[ j ] = tmpB;
		}
	}

	static void ShuffleList( List<Vector2> list )
	{
		for ( int i = list.Count - 1; i > 0; i-- )
		{
			int j = Random.Range( 0, i + 1 );
			Vector2 tmp = list[ i ];
			list[ i ] = list[ j ];
			list[ j ] = tmp;
		}
	}

	void RebuildVisibility()
	{
		if ( _slots == null || _definition == null )
			return;
		if ( _pileRoot == null )
			return;

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
			if ( GetLootStreamCullTier( _slots[ i ].Definition ) >= LootStreamCullTierLarge )
				ForceSlotSurfaceVisible( i );

			if ( IsEligible( _slots[ i ] ) )
				eligible.Add( i );
		}

		eligible.Sort( ( a, b ) =>
		{
			float sa = CoverageScore( _slots[ a ] );
			float sb = CoverageScore( _slots[ b ] );
			return sa.CompareTo( sb );
		} );

		int drawnTotal = 0;
		int cellCount = spatialCells * spatialCells;
		if ( _cellUsedScratch == null || _cellUsedScratch.Length != cellCount )
			_cellUsedScratch = new bool[ cellCount ];
		else
			System.Array.Clear( _cellUsedScratch, 0, _cellUsedScratch.Length );

		bool[] cellUsed = _cellUsedScratch;
		float spacingSq = _placementMinSpacing * _placementMinSpacing;

		// Reserve large props first so coins cannot crowd them out of the draw pool.
		for ( int i = 0; i < eligible.Count && drawnTotal < _maxVisibleTotal; i++ )
		{
			int idx = eligible[ i ];
			Slot slot = _slots[ idx ];
			if ( GetLootStreamCullTier( slot.Definition ) < LootStreamCullTierLarge )
				continue;

			int maxType = _definition.GetMaxVisibleFor( _definition.contents[ slot.EntryIndex ] );
			if ( _visibleCountByEntry[ slot.EntryIndex ] >= maxType )
				continue;

			MarkDrawn( idx );
			int cell = CellIndex( slot.CellX, slot.CellZ );
			if ( cell >= 0 && cell < cellUsed.Length )
				cellUsed[ cell ] = true;
			drawnTotal++;
		}

		for ( int i = 0; i < eligible.Count && drawnTotal < _maxVisibleTotal; i++ )
		{
			int idx = eligible[ i ];
			if ( _slots[ idx ].Drawn )
				continue;

			Slot slot = _slots[ idx ];
			int maxType = _definition.GetMaxVisibleFor( _definition.contents[ slot.EntryIndex ] );
			if ( _visibleCountByEntry[ slot.EntryIndex ] >= maxType )
				continue;

			int cell = CellIndex( slot.CellX, slot.CellZ );
			if ( cellUsed[ cell ] )
				continue;
			if ( IsTooCloseToDrawn( slot.LocalPos, spacingSq ) )
				continue;

			MarkDrawn( idx );
			cellUsed[ cell ] = true;
			drawnTotal++;
		}

		for ( int i = 0; i < eligible.Count && drawnTotal < _maxVisibleTotal; i++ )
		{
			int idx = eligible[ i ];
			if ( _slots[ idx ].Drawn )
				continue;

			Slot slot = _slots[ idx ];
			int maxType = _definition.GetMaxVisibleFor( _definition.contents[ slot.EntryIndex ] );
			if ( _visibleCountByEntry[ slot.EntryIndex ] >= maxType )
				continue;
			if ( IsTooCloseToDrawn( slot.LocalPos, spacingSq ) )
				continue;

			MarkDrawn( idx );
			drawnTotal++;
		}

		if ( drawnTotal < _maxVisibleTotal )
		{
			for ( int i = 0; i < eligible.Count && drawnTotal < _maxVisibleTotal; i++ )
			{
				int idx = eligible[ i ];
				if ( _slots[ idx ].Drawn )
					continue;

				Slot slot = _slots[ idx ];
				int maxType = _definition.GetMaxVisibleFor( _definition.contents[ slot.EntryIndex ] );
				if ( _visibleCountByEntry[ slot.EntryIndex ] >= maxType )
					continue;

				MarkDrawn( idx );
				drawnTotal++;
			}
		}

		if ( drawnTotal < _maxVisibleTotal )
		{
			for ( int i = 0; i < eligible.Count && drawnTotal < _maxVisibleTotal; i++ )
			{
				int idx = eligible[ i ];
				if ( _slots[ idx ].Drawn )
					continue;

				MarkDrawn( idx );
				drawnTotal++;
			}
		}

		if ( drawnTotal < _maxVisibleTotal )
		{
			for ( int i = 0; i < _slots.Length && drawnTotal < _maxVisibleTotal; i++ )
			{
				if ( _slots[ i ].Taken || _slots[ i ].Drawn )
					continue;

				ForceSlotIntoBlankFootprint( i );
				MarkDrawn( i );
				drawnTotal++;
			}
		}

		RebuildBatchMatrices();
		InvalidateDrawCache();
	}

	void ForceSlotSurfaceVisible( int slotIndex )
	{
		Slot slot = _slots[ slotIndex ];
		slot.EmbedDepth = Mathf.Min( slot.EmbedDepth, _initialRevealDepth * 0.5f );
		ConformSlot( ref slot );
		_slots[ slotIndex ] = slot;
	}

	void ForceSlotIntoBlankFootprint( int slotIndex )
	{
		ForceSlotSurfaceVisible( slotIndex );

		if ( _heightfield == null || _pileRoot == null )
			return;

		Slot slot = _slots[ slotIndex ];
		float spacingSq = _placementMinSpacing * _placementMinSpacing;
		if ( !IsTooCloseToDrawn( slot.LocalPos, spacingSq ) )
		{
			int cell = CellIndex( slot.CellX, slot.CellZ );
			if ( !CellHasDrawn( cell, slotIndex ) )
				return;
		}

		float half = _heightfield.WorldSize * 0.5f * _placementRadiusFraction;
		float minSurface = 0.05f * _heightfield.MaxHeight;
		const int attempts = 24;
		for ( int attempt = 0; attempt < attempts; attempt++ )
		{
			float lx = Random.Range( -half, half );
			float lz = Random.Range( -half, half );
			float surface = _heightfield.SampleNormalized( lx, lz ) * _heightfield.MaxHeight;
			if ( surface < minSurface )
				continue;

			Vector3 candidate = new Vector3( lx, 0f, lz );
			if ( IsTooCloseToDrawn( candidate, spacingSq ) )
				continue;

			RemoveFromCell( slotIndex );
			slot.LocalPos = candidate;
			slot.EmbedDepth = Mathf.Min( slot.EmbedDepth, _initialRevealDepth * 0.5f );
			ConformSlot( ref slot );
			_slots[ slotIndex ] = slot;
			AssignCell( slotIndex );
			return;
		}
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
		float cellSize = _heightfield.WorldSize / spatialCells;
		int radius = Mathf.Max( 1, Mathf.CeilToInt( _placementMinSpacing / Mathf.Max( 0.01f, cellSize ) ) );
		int cx = LocalToCell( localPos.x );
		int cz = LocalToCell( localPos.z );
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

				List<int> cell = _cellLists[ CellIndex( xx, zz ) ];
				for ( int i = 0; i < cell.Count; i++ )
				{
					Slot other = _slots[ cell[ i ] ];
					if ( !other.Drawn )
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

	void ConformSlot( ref Slot slot )
	{
		float surface = _heightfield.SampleNormalized( slot.LocalPos.x, slot.LocalPos.z ) * _heightfield.MaxHeight;
		bool large = GetLootStreamCullTier( slot.Definition ) >= LootStreamCullTierLarge;
		float embed = large ? Mathf.Min( slot.EmbedDepth, _initialRevealDepth * 0.35f ) : slot.EmbedDepth;
		float lift = large ? Mathf.Max( 0.08f, slot.Scale * 0.2f ) : slot.Scale * 0.05f;
		slot.LocalPos.y = surface - embed + lift;

		float step = _heightfield.WorldSize / Mathf.Max( 1, _heightfield.Resolution - 1 );
		float hL = _heightfield.SampleNormalized( slot.LocalPos.x - step, slot.LocalPos.z ) * _heightfield.MaxHeight;
		float hR = _heightfield.SampleNormalized( slot.LocalPos.x + step, slot.LocalPos.z ) * _heightfield.MaxHeight;
		float hD = _heightfield.SampleNormalized( slot.LocalPos.x, slot.LocalPos.z - step ) * _heightfield.MaxHeight;
		float hU = _heightfield.SampleNormalized( slot.LocalPos.x, slot.LocalPos.z + step ) * _heightfield.MaxHeight;
		Vector3 normal = new Vector3( hL - hR, step * 2f, hD - hU ).normalized;
		if ( normal.sqrMagnitude < 0.0001f )
			normal = Vector3.up;

		Quaternion tilt = Quaternion.FromToRotation( Vector3.up, normal );
		float yaw = ( slot.LocalPos.x * 37.1f + slot.LocalPos.z * 91.7f ) * 25f;
		float tipX = Mathf.Sin( slot.LocalPos.x * 12.3f ) * 10f;
		float tipZ = Mathf.Cos( slot.LocalPos.z * 9.2f ) * 10f;
		slot.LocalRot = tilt * Quaternion.Euler( tipX, yaw, tipZ );
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
