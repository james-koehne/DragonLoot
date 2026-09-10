using System;
using System.Collections.Generic;
using System.Diagnostics;

using UnityEngine;

/// <summary>
/// World-scale treasure surface: chunked height / flow / material maps with dirty rebuilds.
/// </summary>
[DisallowMultipleComponent]
public class TreasureSurfaceWorld : MonoBehaviour
{
	static TreasureSurfaceWorld _instance;

	[SerializeField]
	TreasureSurfaceDefinition definition;

	readonly Dictionary<long, TreasureChunk> _chunks = new Dictionary<long, TreasureChunk>();
	readonly List<TreasureChunk> _dirtyList = new List<TreasureChunk>( 32 );
	readonly List<TreasureChunk> _loadedList = new List<TreasureChunk>( 64 );

	TreasureSurfaceSampler _sampler;
	TreasureSurfaceStreamer _streamer;
	TreasureSurfaceSimulator _simulator;
	bool _initialized;

	// Timing stats (ms, last frame)
	public float LastChunkRebuildMs { get; private set; }
	public float LastSurfaceUpdateMs { get; private set; }
	public int ActiveChunkCount { get; private set; }
	public int SleepingChunkCount { get; private set; }
	public int DirtyChunkCount { get; private set; }
	public int LoadedChunkCount => _loadedList.Count;

	public static TreasureSurfaceWorld Instance => _instance;
	public TreasureSurfaceDefinition Definition => definition;
	public TreasureSurfaceSampler Sampler => _sampler;
	public TreasureSurfaceStreamer Streamer => _streamer;
	public TreasureSurfaceSimulator Simulator => _simulator;
	public bool IsInitialized => _initialized && definition != null;

	public static TreasureSurfaceWorld EnsureExists()
	{
		if ( _instance != null )
			return _instance;

		GameObject go = new GameObject( "TreasureSurfaceWorld" );
		_instance = go.AddComponent<TreasureSurfaceWorld>();
		_instance.InitializeRuntime();
		return _instance;
	}

	void Awake()
	{
		if ( _instance != null && _instance != this )
		{
			Destroy( gameObject );
			return;
		}

		_instance = this;
		if ( !_initialized )
			InitializeRuntime();
	}

	void OnDestroy()
	{
		ReleaseAllChunks();
		if ( _instance == this )
			_instance = null;
	}

	void Update()
	{
		if ( !_initialized )
			return;

		Stopwatch sw = Stopwatch.StartNew();
		if ( _streamer != null )
			_streamer.Tick();

		RebuildDirtyChunks();
		LastSurfaceUpdateMs = ( float )sw.Elapsed.TotalMilliseconds;

		if ( _simulator != null )
			_simulator.Tick( Time.deltaTime );
	}

	public void InitializeRuntime()
	{
		if ( definition == null )
			definition = TryLoadDefaultDefinition();

		if ( definition == null )
			definition = ScriptableObject.CreateInstance<TreasureSurfaceDefinition>();

		definition.EnsureDefaults();
		ApplyAuthoringLayout();

		_sampler = new TreasureSurfaceSampler( this );
		_streamer = new TreasureSurfaceStreamer( this );
		_simulator = new TreasureSurfaceSimulator( this );
		_initialized = true;

		// Preload a center ring so early samples / stamps succeed before player exists.
		PreloadAroundOrigin( 2 );
	}

	void ApplyAuthoringLayout()
	{
		TreasureSurfaceAuthoring authoring = TreasureSurfaceAuthoring.Instance;
		if ( authoring == null )
			return;

		if ( authoring.Definition != null && definition == null )
			definition = authoring.Definition;

		authoring.EnsurePaintBuffers();
		authoring.ApplyLayoutToDefinition( definition );
	}

	static TreasureSurfaceDefinition TryLoadDefaultDefinition()
	{
#if UNITY_EDITOR
		TreasureSurfaceDefinition asset = UnityEditor.AssetDatabase.LoadAssetAtPath<TreasureSurfaceDefinition>(
			"Assets/Definitions/TreasureSurface/TreasureSurfaceDefinition.asset" );
		if ( asset != null )
			return asset;
#endif
		TreasureSurfaceAuthoring authoring = TreasureSurfaceAuthoring.Instance;
		if ( authoring != null && authoring.Definition != null )
			return authoring.Definition;

		return null;
	}

	public void SetDefinition( TreasureSurfaceDefinition def )
	{
		definition = def;
		if ( definition != null )
			definition.EnsureDefaults();

		ReleaseAllChunks();
		_initialized = false;
		InitializeRuntime();
	}

	/// <summary>
	/// Re-applies level authoring layout/paint after the Level scene loads.
	/// </summary>
	public void RebindAuthoring()
	{
		if ( definition == null )
			definition = TryLoadDefaultDefinition();

		if ( definition == null )
			definition = ScriptableObject.CreateInstance<TreasureSurfaceDefinition>();

		definition.EnsureDefaults();
		ApplyAuthoringLayout();

		if ( !_initialized )
		{
			InitializeRuntime();
			return;
		}

		for ( int i = 0; i < _loadedList.Count; i++ )
		{
			TreasureChunk chunk = _loadedList[ i ];
			if ( chunk == null || !chunk.Loaded )
				continue;

			ApplyAuthoringPaint( chunk );
			TreasureSurfaceRelax.CopyHeightToSmoothed( chunk );
			TreasureSurfaceRebuild.RebuildDerived( chunk, definition, definition.CellSize );
			chunk.MarkDirtyFull();
		}
	}

	void PreloadAroundOrigin( int radiusChunks )
	{
		int cx = definition.ChunkCountX / 2;
		int cz = definition.ChunkCountZ / 2;
		for ( int z = cz - radiusChunks; z <= cz + radiusChunks; z++ )
		{
			for ( int x = cx - radiusChunks; x <= cx + radiusChunks; x++ )
			{
				if ( x < 0 || z < 0 || x >= definition.ChunkCountX || z >= definition.ChunkCountZ )
					continue;
				EnsureChunkLoaded( new TreasureChunkCoord( x, z ) );
			}
		}
	}

	static long Key( TreasureChunkCoord coord )
	{
		return ( ( long )coord.X << 32 ) ^ ( uint )coord.Z;
	}

	public bool TryGetChunkCoord( Vector3 worldPos, out TreasureChunkCoord coord )
	{
		coord = default;
		if ( definition == null )
			return false;

		float halfX = definition.worldSizeX * 0.5f;
		float halfZ = definition.worldSizeZ * 0.5f;
		float localX = worldPos.x - definition.worldOrigin.x + halfX;
		float localZ = worldPos.z - definition.worldOrigin.z + halfZ;
		if ( localX < 0f || localZ < 0f || localX >= definition.worldSizeX || localZ >= definition.worldSizeZ )
			return false;

		int x = Mathf.FloorToInt( localX / definition.chunkSize );
		int z = Mathf.FloorToInt( localZ / definition.chunkSize );
		x = Mathf.Clamp( x, 0, definition.ChunkCountX - 1 );
		z = Mathf.Clamp( z, 0, definition.ChunkCountZ - 1 );
		coord = new TreasureChunkCoord( x, z );
		return true;
	}

	public Vector3 GetChunkCenter( TreasureChunkCoord coord )
	{
		float halfX = definition.worldSizeX * 0.5f;
		float halfZ = definition.worldSizeZ * 0.5f;
		float x = definition.worldOrigin.x - halfX + ( coord.X + 0.5f ) * definition.chunkSize;
		float z = definition.worldOrigin.z - halfZ + ( coord.Z + 0.5f ) * definition.chunkSize;
		return new Vector3( x, definition.baseHeight, z );
	}

	public Bounds GetChunkBounds( TreasureChunkCoord coord )
	{
		float halfX = definition.worldSizeX * 0.5f;
		float halfZ = definition.worldSizeZ * 0.5f;
		float minX = definition.worldOrigin.x - halfX + coord.X * definition.chunkSize;
		float minZ = definition.worldOrigin.z - halfZ + coord.Z * definition.chunkSize;
		Vector3 center = new Vector3(
			minX + definition.chunkSize * 0.5f,
			( definition.minWorldY + definition.maxWorldY ) * 0.5f,
			minZ + definition.chunkSize * 0.5f );
		Vector3 size = new Vector3(
			definition.chunkSize,
			definition.maxWorldY - definition.minWorldY,
			definition.chunkSize );
		return new Bounds( center, size );
	}

	public TreasureChunk EnsureChunkLoaded( TreasureChunkCoord coord )
	{
		long key = Key( coord );
		if ( _chunks.TryGetValue( key, out TreasureChunk existing ) && existing.Loaded )
			return existing;

		TreasureChunk chunk = existing ?? new TreasureChunk( coord );
		chunk.Bounds = GetChunkBounds( coord );
		chunk.Allocate( definition.cellsPerChunk, definition.baseHeight );
		ApplyAuthoringPaint( chunk );
		TreasureSurfaceRelax.CopyHeightToSmoothed( chunk );
		TreasureSurfaceRebuild.RebuildDerived( chunk, definition, definition.CellSize );
		_chunks[ key ] = chunk;

		if ( !_loadedList.Contains( chunk ) )
			_loadedList.Add( chunk );

		return chunk;
	}

	void ApplyAuthoringPaint( TreasureChunk chunk )
	{
		TreasureSurfaceAuthoring authoring = TreasureSurfaceAuthoring.Instance;
		if ( authoring == null )
			return;

		authoring.ApplyPaintToChunk( chunk );
	}

	public TreasureChunk GetLoadedChunk( TreasureChunkCoord coord )
	{
		if ( _chunks.TryGetValue( Key( coord ), out TreasureChunk chunk ) && chunk.Loaded )
			return chunk;
		return null;
	}

	public void ForEachLoadedChunk( Action<TreasureChunk> action )
	{
		if ( action == null )
			return;

		for ( int i = 0; i < _loadedList.Count; i++ )
		{
			TreasureChunk chunk = _loadedList[ i ];
			if ( chunk != null && chunk.Loaded )
				action( chunk );
		}
	}

	public delegate void CellVisitor(
		TreasureChunk chunk,
		int cellX,
		int cellZ,
		float worldX,
		float worldZ,
		float distSq,
		float radiusSq );

	public void ForEachCellInRadius( Vector3 worldCenter, float radius, CellVisitor visitor )
	{
		if ( visitor == null || definition == null || radius <= 0f )
			return;

		float radiusSq = radius * radius;
		float halfX = definition.worldSizeX * 0.5f;
		float halfZ = definition.worldSizeZ * 0.5f;
		float cell = definition.CellSize;
		float minLocalX = worldCenter.x - definition.worldOrigin.x + halfX - radius;
		float maxLocalX = worldCenter.x - definition.worldOrigin.x + halfX + radius;
		float minLocalZ = worldCenter.z - definition.worldOrigin.z + halfZ - radius;
		float maxLocalZ = worldCenter.z - definition.worldOrigin.z + halfZ + radius;

		int minChunkX = Mathf.Clamp( Mathf.FloorToInt( minLocalX / definition.chunkSize ), 0, definition.ChunkCountX - 1 );
		int maxChunkX = Mathf.Clamp( Mathf.FloorToInt( maxLocalX / definition.chunkSize ), 0, definition.ChunkCountX - 1 );
		int minChunkZ = Mathf.Clamp( Mathf.FloorToInt( minLocalZ / definition.chunkSize ), 0, definition.ChunkCountZ - 1 );
		int maxChunkZ = Mathf.Clamp( Mathf.FloorToInt( maxLocalZ / definition.chunkSize ), 0, definition.ChunkCountZ - 1 );

		for ( int cz = minChunkZ; cz <= maxChunkZ; cz++ )
		{
			for ( int cx = minChunkX; cx <= maxChunkX; cx++ )
			{
				TreasureChunkCoord coord = new TreasureChunkCoord( cx, cz );
				TreasureChunk chunk = EnsureChunkLoaded( coord );
				float chunkOriginX = cx * definition.chunkSize;
				float chunkOriginZ = cz * definition.chunkSize;
				float chunkWorldMinX = definition.worldOrigin.x - halfX + chunkOriginX;
				float chunkWorldMinZ = definition.worldOrigin.z - halfZ + chunkOriginZ;
				int res = chunk.Resolution;

				// Clamp inner loops to the stamp AABB in cell space (centers at x+0.5).
				int minX = Mathf.Clamp(
					Mathf.FloorToInt( ( worldCenter.x - radius - chunkWorldMinX ) / cell - 0.5f ),
					0,
					res - 1 );
				int maxX = Mathf.Clamp(
					Mathf.CeilToInt( ( worldCenter.x + radius - chunkWorldMinX ) / cell - 0.5f ),
					0,
					res - 1 );
				int minZ = Mathf.Clamp(
					Mathf.FloorToInt( ( worldCenter.z - radius - chunkWorldMinZ ) / cell - 0.5f ),
					0,
					res - 1 );
				int maxZ = Mathf.Clamp(
					Mathf.CeilToInt( ( worldCenter.z + radius - chunkWorldMinZ ) / cell - 0.5f ),
					0,
					res - 1 );

				if ( minX > maxX || minZ > maxZ )
					continue;

				for ( int z = minZ; z <= maxZ; z++ )
				{
					float wz = chunkWorldMinZ + ( z + 0.5f ) * cell;
					float dz = wz - worldCenter.z;
					for ( int x = minX; x <= maxX; x++ )
					{
						float wx = chunkWorldMinX + ( x + 0.5f ) * cell;
						float dx = wx - worldCenter.x;
						float distSq = dx * dx + dz * dz;
						if ( distSq > radiusSq )
							continue;

						visitor( chunk, x, z, wx, wz, distSq, radiusSq );
					}
				}
			}
		}
	}

	public void ForEachCellInBounds( Bounds worldBounds, CellVisitor visitor )
	{
		if ( visitor == null || definition == null )
			return;

		float halfX = definition.worldSizeX * 0.5f;
		float halfZ = definition.worldSizeZ * 0.5f;
		float cell = definition.CellSize;
		float minX = worldBounds.min.x;
		float maxX = worldBounds.max.x;
		float minZ = worldBounds.min.z;
		float maxZ = worldBounds.max.z;
		if ( minX > maxX || minZ > maxZ )
			return;

		float minLocalX = minX - definition.worldOrigin.x + halfX;
		float maxLocalX = maxX - definition.worldOrigin.x + halfX;
		float minLocalZ = minZ - definition.worldOrigin.z + halfZ;
		float maxLocalZ = maxZ - definition.worldOrigin.z + halfZ;

		int minChunkX = Mathf.Clamp( Mathf.FloorToInt( minLocalX / definition.chunkSize ), 0, definition.ChunkCountX - 1 );
		int maxChunkX = Mathf.Clamp( Mathf.FloorToInt( maxLocalX / definition.chunkSize ), 0, definition.ChunkCountX - 1 );
		int minChunkZ = Mathf.Clamp( Mathf.FloorToInt( minLocalZ / definition.chunkSize ), 0, definition.ChunkCountZ - 1 );
		int maxChunkZ = Mathf.Clamp( Mathf.FloorToInt( maxLocalZ / definition.chunkSize ), 0, definition.ChunkCountZ - 1 );

		for ( int cz = minChunkZ; cz <= maxChunkZ; cz++ )
		{
			for ( int cx = minChunkX; cx <= maxChunkX; cx++ )
			{
				TreasureChunkCoord coord = new TreasureChunkCoord( cx, cz );
				TreasureChunk chunk = EnsureChunkLoaded( coord );
				float chunkOriginX = cx * definition.chunkSize;
				float chunkOriginZ = cz * definition.chunkSize;
				float chunkWorldMinX = definition.worldOrigin.x - halfX + chunkOriginX;
				float chunkWorldMinZ = definition.worldOrigin.z - halfZ + chunkOriginZ;
				int res = chunk.Resolution;

				int cellMinX = Mathf.Clamp(
					Mathf.FloorToInt( ( minX - chunkWorldMinX ) / cell - 0.5f ),
					0,
					res - 1 );
				int cellMaxX = Mathf.Clamp(
					Mathf.CeilToInt( ( maxX - chunkWorldMinX ) / cell - 0.5f ),
					0,
					res - 1 );
				int cellMinZ = Mathf.Clamp(
					Mathf.FloorToInt( ( minZ - chunkWorldMinZ ) / cell - 0.5f ),
					0,
					res - 1 );
				int cellMaxZ = Mathf.Clamp(
					Mathf.CeilToInt( ( maxZ - chunkWorldMinZ ) / cell - 0.5f ),
					0,
					res - 1 );

				if ( cellMinX > cellMaxX || cellMinZ > cellMaxZ )
					continue;

				for ( int z = cellMinZ; z <= cellMaxZ; z++ )
				{
					float wz = chunkWorldMinZ + ( z + 0.5f ) * cell;
					if ( wz < minZ || wz > maxZ )
						continue;

					for ( int x = cellMinX; x <= cellMaxX; x++ )
					{
						float wx = chunkWorldMinX + ( x + 0.5f ) * cell;
						if ( wx < minX || wx > maxX )
							continue;

						visitor( chunk, x, z, wx, wz, 0f, 0f );
					}
				}
			}
		}
	}

	public void MarkChunksDirtyInRadius( Vector3 worldCenter, float radius )
	{
		ForEachCellInRadius( worldCenter, radius, ( chunk, x, z, wx, wz, distSq, radiusSq ) =>
		{
			chunk.ExpandDirtyRect( x, x, z, z );
			chunk.Dirty = true;
		} );
	}

	void RebuildDirtyChunks()
	{
		RebuildDirtyChunksInternal( budget: Mathf.Max( 1, definition.maxDirtyChunksPerFrame ) );
	}

	public void RebuildDirtyChunksImmediate()
	{
		if ( definition == null )
			return;

		RebuildDirtyChunksInternal( budget: int.MaxValue );
	}

	void RebuildDirtyChunksInternal( int budget )
	{
		_dirtyList.Clear();
		int active = 0;
		int sleeping = 0;

		for ( int i = 0; i < _loadedList.Count; i++ )
		{
			TreasureChunk chunk = _loadedList[ i ];
			if ( chunk == null || !chunk.Loaded )
				continue;

			if ( chunk.Frozen )
				sleeping++;
			else
				active++;

			if ( chunk.Dirty && !chunk.Frozen )
				_dirtyList.Add( chunk );
		}

		ActiveChunkCount = active;
		SleepingChunkCount = sleeping;
		DirtyChunkCount = _dirtyList.Count;

		Stopwatch sw = Stopwatch.StartNew();
		int rebuilt = 0;
		float cellSize = definition.CellSize;
		for ( int i = 0; i < _dirtyList.Count && rebuilt < budget; i++ )
		{
			TreasureChunk chunk = _dirtyList[ i ];
			if ( !chunk.TryGetDirtyRect( out int minX, out int maxX, out int minZ, out int maxZ ) )
				continue;

			int iters = definition.relaxIterations;
			if ( iters > 0 )
				TreasureSurfaceRelax.Relax( chunk, iters, minX, maxX, minZ, maxZ );
			else
				TreasureSurfaceRelax.CopyHeightToSmoothed( chunk, minX, maxX, minZ, maxZ );

			TreasureSurfaceRebuild.RebuildDerived( chunk, definition, cellSize, minX, maxX, minZ, maxZ );
			rebuilt++;
		}

		LastChunkRebuildMs = ( float )sw.Elapsed.TotalMilliseconds;
	}

	void ReleaseAllChunks()
	{
		for ( int i = 0; i < _loadedList.Count; i++ )
		{
			if ( _loadedList[ i ] != null )
				_loadedList[ i ].Release();
		}

		_loadedList.Clear();
		_chunks.Clear();
	}

	public bool ContainsWorldPoint( Vector3 worldPos )
	{
		return TryGetChunkCoord( worldPos, out _ );
	}

	public bool TryFindNearestTraversable(
		Vector3 from,
		out Vector3 worldPos,
		out TreasureSurfaceSample sample,
		bool preferStable )
	{
		worldPos = from;
		sample = default;
		if ( !IsInitialized )
			return false;

		float bestSq = float.MaxValue;
		bool found = false;
		Vector3 bestPos = from;
		TreasureSurfaceSample bestSample = default;

		float halfX = definition.worldSizeX * 0.5f;
		float halfZ = definition.worldSizeZ * 0.5f;
		float cell = definition.CellSize;

		for ( int i = 0; i < _loadedList.Count; i++ )
		{
			TreasureChunk chunk = _loadedList[ i ];
			if ( chunk == null || !chunk.Loaded || chunk.Frozen || chunk.PaintTraversable == null )
				continue;

			float chunkOriginX = chunk.Coord.X * definition.chunkSize;
			float chunkOriginZ = chunk.Coord.Z * definition.chunkSize;

			for ( int z = 0; z < chunk.Resolution; z += 2 )
			{
				for ( int x = 0; x < chunk.Resolution; x += 2 )
				{
					int idx = chunk.Index( x, z );
					if ( chunk.PaintTraversable[ idx ] == 0 )
						continue;

					byte flags = chunk.Flags[ idx ];
					if ( preferStable && ( flags & ( byte )TreasureCellFlags.Stable ) == 0 )
						continue;

					float wx = definition.worldOrigin.x - halfX + chunkOriginX + ( x + 0.5f ) * cell;
					float wz = definition.worldOrigin.z - halfZ + chunkOriginZ + ( z + 0.5f ) * cell;
					float wy = chunk.SmoothedHeight[ idx ];
					float dx = wx - from.x;
					float dz = wz - from.z;
					float sq = dx * dx + dz * dz;
					if ( sq >= bestSq )
						continue;

					bestSq = sq;
					bestPos = new Vector3( wx, wy, wz );
					found = true;
					bestSample.Valid = true;
					bestSample.Height = wy;
					bestSample.Traversable = true;
					bestSample.Stable = ( flags & ( byte )TreasureCellFlags.Stable ) != 0;
					bestSample.Material = ( TreasureSurfaceMaterial )chunk.Material[ idx ];
					bestSample.Flow = new Vector2( chunk.FlowX[ idx ], chunk.FlowZ[ idx ] );
					bestSample.Slope = chunk.Slope[ idx ];
					bestSample.Normal = new Vector3( chunk.NormalX[ idx ], 1f, chunk.NormalZ[ idx ] ).normalized;
					bestSample.ChunkCoord = chunk.Coord;
					bestSample.CellX = x;
					bestSample.CellZ = z;
				}
			}
		}

		if ( !found && preferStable )
			return TryFindNearestTraversable( from, out worldPos, out sample, preferStable: false );

		if ( !found )
			return false;

		worldPos = bestPos;
		sample = bestSample;
		return true;
	}

	/// <summary>
	/// Resolves a surface entry XZ to strict traversable paint, or the nearest traversable cell.
	/// </summary>
	public bool TryResolveTraversableEntry(
		Vector3 desired,
		out Vector3 resolved,
		out TreasureSurfaceSample sample,
		bool preferStable = true )
	{
		resolved = desired;
		sample = default;
		if ( !IsInitialized || _sampler == null )
			return false;

		if ( TryGetChunkCoord( desired, out TreasureChunkCoord coord ) )
			EnsureChunkLoaded( coord );

		if ( _sampler.TrySample( desired, out sample ) && sample.Traversable )
			return true;

		if ( TryFindNearestTraversable( desired, out resolved, out sample, preferStable ) )
			return true;

		return false;
	}

	void OnDrawGizmosSelected()
	{
		TreasureSurfaceDefinition def = definition;
		if ( def == null )
			return;

		DrawSimulationBounds( def, selected: true );
	}

	public static void DrawSimulationBounds( TreasureSurfaceDefinition def, bool selected )
	{
		if ( def == null )
			return;

		Bounds wb = def.WorldBounds;
		Vector3 center = new Vector3( wb.center.x, def.baseHeight + 0.02f, wb.center.z );
		Vector3 size = new Vector3( wb.size.x, 0.04f, wb.size.z );

		Gizmos.color = selected
			? new Color( 0.2f, 1f, 0.55f, 0.9f )
			: new Color( 0.15f, 0.85f, 0.45f, 0.55f );
		Gizmos.DrawWireCube( center, size );

		if ( !selected )
			return;

		float halfX = def.worldSizeX * 0.5f;
		float halfZ = def.worldSizeZ * 0.5f;
		float y = def.baseHeight + 0.03f;
		Gizmos.color = new Color( 0.3f, 0.75f, 1f, 0.35f );

		int countX = def.ChunkCountX;
		int countZ = def.ChunkCountZ;
		for ( int x = 0; x <= countX; x++ )
		{
			float wx = def.worldOrigin.x - halfX + x * def.chunkSize;
			Gizmos.DrawLine(
				new Vector3( wx, y, def.worldOrigin.z - halfZ ),
				new Vector3( wx, y, def.worldOrigin.z - halfZ + def.worldSizeZ ) );
		}

		for ( int z = 0; z <= countZ; z++ )
		{
			float wz = def.worldOrigin.z - halfZ + z * def.chunkSize;
			Gizmos.DrawLine(
				new Vector3( def.worldOrigin.x - halfX, y, wz ),
				new Vector3( def.worldOrigin.x - halfX + def.worldSizeX, y, wz ) );
		}
	}
}
