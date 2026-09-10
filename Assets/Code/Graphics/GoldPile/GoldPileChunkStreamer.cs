using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Frustum + distance-LOD streaming for pile loot chunks.
/// Chunks stay loaded while the pile is bound; only visibility gates drawing.
/// Tick evaluates a player-centered LOD window (not the full grid) and only dirties
/// chunks whose draw-relevant rendered membership / LOD changes.
/// </summary>
public sealed class GoldPileChunkStreamer
{
	const float LodPlayerMoveEpsilonSqr = 0.0625f; // 0.25m — matches prop residency
	const float CameraMoveEpsilonSqr = 0.0025f;
	const float CameraForwardDotMin = 0.9995f;

	readonly List<GoldPileChunk> _rendered = new List<GoldPileChunk>( 32 );
	readonly List<GoldPileChunk> _visible = new List<GoldPileChunk>( 32 );
	readonly List<int> _dirtyChunkIndices = new List<int>( 32 );
	readonly List<int> _prevRenderedIndices = new List<int>( 32 );
	readonly List<int> _evalScratch = new List<int>( 128 );
	readonly HashSet<int> _evalSet = new HashSet<int>();

	GoldPileChunkGrid _grid;
	GoldPileLootStreamSettings _settings;

	Vector3 _lastPlayerPos;
	Vector3 _lastCameraPos;
	Vector3 _lastCameraForward;
	bool _hasLastSample;
	bool _forceTick = true;
	int _pendingHysteresisCount;

	public IReadOnlyList<GoldPileChunk> RenderedChunks => _rendered;
	public IReadOnlyList<GoldPileChunk> VisibleChunks => _visible;
	public IReadOnlyList<int> DirtyChunkIndices => _dirtyChunkIndices;
	public int LoadedCount { get; private set; }
	public int VisibleCount { get; private set; }
	public int RenderedCount => _rendered.Count;
	public int FrustumCulledCount { get; private set; }
	public int LastDirtyChunkCount { get; private set; }
	public bool LastTickChanged { get; private set; }
	public bool HasTicked => _hasLastSample;

	public void ForceRetick()
	{
		_forceTick = true;
	}

	public void Bind( GoldPileChunkGrid grid, GoldPileLootStreamSettings settings )
	{
		_grid = grid;
		_settings = settings;
		_rendered.Clear();
		_visible.Clear();
		_dirtyChunkIndices.Clear();
		_prevRenderedIndices.Clear();
		_evalScratch.Clear();
		_evalSet.Clear();
		LoadedCount = 0;
		VisibleCount = 0;
		FrustumCulledCount = 0;
		LastDirtyChunkCount = 0;
		LastTickChanged = true;
		_hasLastSample = false;
		_forceTick = true;
		_pendingHysteresisCount = 0;

		if ( _grid == null )
			return;

		IReadOnlyList<GoldPileChunk> chunks = _grid.Chunks;
		LoadedCount = chunks.Count;
		for ( int i = 0; i < chunks.Count; i++ )
		{
			GoldPileChunk chunk = chunks[ i ];
			// Lod starts at 0; State stays Loaded until Tick marks Rendered. Out-of-window
			// chunks are never marked Rendered by the windowed tick, so they stay undrawn.
			chunk.State = GoldPileChunkStreamState.Loaded;
			chunk.FrustumVisible = false;
			chunk.Lod = 0;
			chunk.PendingLod = 0;
			chunk.LodStableFrames = 0;
			chunk.Dirty = true;
			chunk.LastDrawnCount = 0;
		}
	}

	public void Clear()
	{
		if ( _grid != null )
		{
			IReadOnlyList<GoldPileChunk> chunks = _grid.Chunks;
			for ( int i = 0; i < chunks.Count; i++ )
			{
				GoldPileChunk chunk = chunks[ i ];
				chunk.State = GoldPileChunkStreamState.Unloaded;
				chunk.FrustumVisible = false;
				chunk.Lod = 3;
				chunk.PendingLod = 3;
				chunk.LodStableFrames = 0;
				chunk.LastDrawnCount = 0;
			}
		}

		_rendered.Clear();
		_visible.Clear();
		_dirtyChunkIndices.Clear();
		_prevRenderedIndices.Clear();
		_evalScratch.Clear();
		_evalSet.Clear();
		LoadedCount = 0;
		VisibleCount = 0;
		FrustumCulledCount = 0;
		LastDirtyChunkCount = 0;
		LastTickChanged = true;
		_hasLastSample = false;
		_forceTick = true;
		_pendingHysteresisCount = 0;
	}

	/// <summary>
	/// Updates chunk LOD / frustum membership. Returns true when draw-relevant state changed.
	/// Dirty chunk indices are listed in <see cref="DirtyChunkIndices"/> for incremental rebuilds.
	/// </summary>
	public bool Tick( Vector3 playerPos, Camera camera )
	{
		LastTickChanged = false;
		_dirtyChunkIndices.Clear();
		LastDirtyChunkCount = 0;

		if ( _grid == null || _settings == null || _grid.ChunkCount == 0 )
		{
			_pendingHysteresisCount = 0;
			if ( _rendered.Count > 0 || _visible.Count > 0 )
			{
				_rendered.Clear();
				_visible.Clear();
				LoadedCount = 0;
				VisibleCount = 0;
				FrustumCulledCount = 0;
				LastTickChanged = true;
			}

			return LastTickChanged;
		}

		Vector3 cameraPos = camera != null ? camera.transform.position : playerPos;
		Vector3 cameraForward = camera != null ? camera.transform.forward : Vector3.forward;

		bool forceTick = _forceTick || !_hasLastSample;
		bool playerMoved = forceTick
			|| PlanarDeltaSqr( playerPos, _lastPlayerPos ) >= LodPlayerMoveEpsilonSqr;
		bool cameraMoved = forceTick
			|| ( cameraPos - _lastCameraPos ).sqrMagnitude >= CameraMoveEpsilonSqr
			|| Vector3.Dot( cameraForward, _lastCameraForward ) < CameraForwardDotMin;
		bool pendingHyst = _pendingHysteresisCount > 0;

		if ( !forceTick && !playerMoved && !cameraMoved && !pendingHyst )
			return false;

		float chunkSize = Mathf.Max( 1f, _grid.ChunkSize );
		float chunkDiagonal = chunkSize * 1.41421356f;
		float evalRadius = _settings.lod2End + chunkDiagonal + Mathf.Max( 0.1f, _settings.ditherFadeWidth );
		float evalRadiusSqr = evalRadius * evalRadius;

		if ( !forceTick
			&& _rendered.Count == 0
			&& _grid.TryGetPileWorldBounds( out Bounds pileBounds )
			&& PlanarDistanceToBoundsSqr( playerPos, pileBounds ) > evalRadiusSqr )
		{
			_forceTick = false;
			_hasLastSample = true;
			_lastPlayerPos = playerPos;
			_lastCameraPos = cameraPos;
			_lastCameraForward = cameraForward;
			_pendingHysteresisCount = 0;
			return false;
		}

		_forceTick = false;
		_hasLastSample = true;
		_lastPlayerPos = playerPos;
		_lastCameraPos = cameraPos;
		_lastCameraForward = cameraForward;

		bool updateLod = playerMoved || pendingHyst || forceTick;

		_prevRenderedIndices.Clear();
		for ( int i = 0; i < _rendered.Count; i++ )
		{
			GoldPileChunkCoord coord = _rendered[ i ].Coord;
			_prevRenderedIndices.Add( coord.Z * _grid.CountX + coord.X );
		}

		_rendered.Clear();
		_visible.Clear();
		VisibleCount = 0;
		FrustumCulledCount = 0;
		LoadedCount = _grid.ChunkCount;

		CollectEvalChunks( playerPos, evalRadius, evalRadiusSqr );

		bool hasFrustum = GoldPileFrustumCache.TryGet( camera, out Plane[] frustumPlanes );
		int hysteresis = _settings.lodHysteresisFrames;
		IReadOnlyList<GoldPileChunk> chunks = _grid.Chunks;
		bool changed = false;
		int pendingHysteresis = 0;

		for ( int e = 0; e < _evalScratch.Count; e++ )
		{
			int i = _evalScratch[ e ];
			if ( i < 0 || i >= chunks.Count )
				continue;

			GoldPileChunk chunk = chunks[ i ];
			int prevLod = chunk.Lod;
			GoldPileChunkStreamState prevState = chunk.State;
			bool wasRendered = prevState == GoldPileChunkStreamState.Rendered;

			float distSqr = PlanarDistanceToBoundsSqr( playerPos, chunk.WorldBounds );

			chunk.State = GoldPileChunkStreamState.Loaded;

			if ( updateLod )
			{
				int desiredLod = _settings.EvaluateLodSqr( distSqr );
				ApplyLodHysteresis( chunk, desiredLod, hysteresis );
			}

			if ( IsLodHysteresisPending( chunk, hysteresis ) )
				pendingHysteresis++;

			bool inFrustum = !hasFrustum || GeometryUtility.TestPlanesAABB( frustumPlanes, chunk.WorldBounds );
			chunk.FrustumVisible = inFrustum;

			if ( !inFrustum )
			{
				FrustumCulledCount++;
				bool nowRendered = false;
				if ( wasRendered != nowRendered || ( nowRendered && prevLod != chunk.Lod ) )
				{
					changed = true;
					_dirtyChunkIndices.Add( i );
				}

				continue;
			}

			VisibleCount++;
			_visible.Add( chunk );

			bool nowInRendered = chunk.Lod < 3;
			if ( nowInRendered )
			{
				chunk.State = GoldPileChunkStreamState.Rendered;
				_rendered.Add( chunk );
			}
			else
			{
				chunk.State = GoldPileChunkStreamState.Visible;
			}

			if ( wasRendered != nowInRendered || ( nowInRendered && prevLod != chunk.Lod ) )
			{
				changed = true;
				_dirtyChunkIndices.Add( i );
			}
		}

		LastDirtyChunkCount = _dirtyChunkIndices.Count;
		LastTickChanged = changed;
		_pendingHysteresisCount = pendingHysteresis;
		return changed;
	}

	void CollectEvalChunks( Vector3 playerPos, float evalRadius, float evalRadiusSqr )
	{
		_evalScratch.Clear();
		_evalSet.Clear();

		IReadOnlyList<GoldPileChunk> chunks = _grid.Chunks;
		int countX = _grid.CountX;
		int countZ = _grid.CountZ;
		float chunkSize = Mathf.Max( 1f, _grid.ChunkSize );

		// Include previously rendered so they can leave the draw set.
		for ( int i = 0; i < _prevRenderedIndices.Count; i++ )
		{
			int idx = _prevRenderedIndices[ i ];
			if ( idx < 0 || idx >= chunks.Count )
				continue;
			if ( _evalSet.Add( idx ) )
				_evalScratch.Add( idx );
		}

		if ( !_grid.TryWorldToChunk( playerPos, out int playerCx, out int playerCz ) )
		{
			// Fallback: scan all chunks by distance (small piles / missing root).
			for ( int i = 0; i < chunks.Count; i++ )
			{
				if ( PlanarDistanceToBoundsSqr( playerPos, chunks[ i ].WorldBounds ) > evalRadiusSqr )
					continue;
				if ( _evalSet.Add( i ) )
					_evalScratch.Add( i );
			}

			return;
		}

		int radiusChunks = Mathf.Max( 1, Mathf.CeilToInt( evalRadius / chunkSize ) + 1 );
		int minX = Mathf.Max( 0, playerCx - radiusChunks );
		int maxX = Mathf.Min( countX - 1, playerCx + radiusChunks );
		int minZ = Mathf.Max( 0, playerCz - radiusChunks );
		int maxZ = Mathf.Min( countZ - 1, playerCz + radiusChunks );

		for ( int z = minZ; z <= maxZ; z++ )
		{
			int row = z * countX;
			for ( int x = minX; x <= maxX; x++ )
			{
				int i = row + x;
				GoldPileChunk chunk = chunks[ i ];
				if ( PlanarDistanceToBoundsSqr( playerPos, chunk.WorldBounds ) > evalRadiusSqr )
					continue;
				if ( _evalSet.Add( i ) )
					_evalScratch.Add( i );
			}
		}
	}

	static bool IsLodHysteresisPending( GoldPileChunk chunk, int hysteresisFrames )
	{
		if ( chunk.PendingLod == chunk.Lod )
			return false;

		int framesNeeded = chunk.PendingLod > chunk.Lod ? Mathf.Max( 1, hysteresisFrames ) : 1;
		return chunk.LodStableFrames < framesNeeded;
	}

	static void ApplyLodHysteresis( GoldPileChunk chunk, int desiredLod, int hysteresisFrames )
	{
		// Promote to finer LOD immediately so the first Tick after bind can mark Rendered
		// and the same-frame draw-cache rebuild actually has instances to submit.
		if ( desiredLod < chunk.Lod )
		{
			if ( chunk.Lod != desiredLod )
				chunk.Dirty = true;
			chunk.PendingLod = desiredLod;
			chunk.Lod = desiredLod;
			chunk.LodStableFrames = 0;
			return;
		}

		if ( desiredLod == chunk.PendingLod )
		{
			chunk.LodStableFrames++;
		}
		else
		{
			chunk.PendingLod = desiredLod;
			chunk.LodStableFrames = 0;
		}

		// Demote only after stable coarser samples.
		int framesNeeded = desiredLod > chunk.Lod ? Mathf.Max( 1, hysteresisFrames ) : 1;

		if ( chunk.LodStableFrames >= framesNeeded || chunk.Lod == desiredLod )
		{
			if ( chunk.Lod != desiredLod )
				chunk.Dirty = true;
			chunk.Lod = desiredLod;
		}
	}

	static float PlanarDeltaSqr( Vector3 a, Vector3 b )
	{
		float dx = a.x - b.x;
		float dz = a.z - b.z;
		return dx * dx + dz * dz;
	}

	/// <summary>Squared XZ distance from <paramref name="worldPos"/> to the closest point on <paramref name="bounds"/>.</summary>
	static float PlanarDistanceToBoundsSqr( Vector3 worldPos, Bounds bounds )
	{
		Vector3 min = bounds.min;
		Vector3 max = bounds.max;
		float cx = worldPos.x < min.x ? min.x : ( worldPos.x > max.x ? max.x : worldPos.x );
		float cz = worldPos.z < min.z ? min.z : ( worldPos.z > max.z ? max.z : worldPos.z );
		float dx = cx - worldPos.x;
		float dz = cz - worldPos.z;
		return dx * dx + dz * dz;
	}
}
