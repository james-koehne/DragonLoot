using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Frustum + distance-LOD streaming for pile loot chunks.
/// Chunks stay loaded while the pile is bound; only visibility gates drawing.
/// </summary>
public sealed class GoldPileChunkStreamer
{
	const float PlayerMoveEpsilonSqr = 0.0025f; // 5cm
	const float CameraMoveEpsilonSqr = 0.0025f;
	const float CameraForwardDotMin = 0.9995f;

	readonly List<GoldPileChunk> _rendered = new List<GoldPileChunk>( 32 );
	readonly List<GoldPileChunk> _visible = new List<GoldPileChunk>( 32 );
	readonly List<int> _dirtyChunkIndices = new List<int>( 32 );

	GoldPileChunkGrid _grid;
	GoldPileLootStreamSettings _settings;

	Vector3 _lastPlayerPos;
	Vector3 _lastCameraPos;
	Vector3 _lastCameraForward;
	bool _hasLastSample;
	bool _forceTick = true;

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
		LoadedCount = 0;
		VisibleCount = 0;
		FrustumCulledCount = 0;
		LastDirtyChunkCount = 0;
		LastTickChanged = true;
		_hasLastSample = false;
		_forceTick = true;

		if ( _grid == null )
			return;

		IReadOnlyList<GoldPileChunk> chunks = _grid.Chunks;
		for ( int i = 0; i < chunks.Count; i++ )
		{
			GoldPileChunk chunk = chunks[ i ];
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
		LoadedCount = 0;
		VisibleCount = 0;
		FrustumCulledCount = 0;
		LastDirtyChunkCount = 0;
		LastTickChanged = true;
		_hasLastSample = false;
		_forceTick = true;
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

		if ( !_forceTick && _hasLastSample && !HasPendingLodHysteresis() )
		{
			float playerDeltaSqr = PlanarDeltaSqr( playerPos, _lastPlayerPos );
			float cameraDeltaSqr = ( cameraPos - _lastCameraPos ).sqrMagnitude;
			float forwardDot = Vector3.Dot( cameraForward, _lastCameraForward );
			if ( playerDeltaSqr < PlayerMoveEpsilonSqr
				&& cameraDeltaSqr < CameraMoveEpsilonSqr
				&& forwardDot >= CameraForwardDotMin )
			{
				return false;
			}
		}

		_forceTick = false;
		_hasLastSample = true;
		_lastPlayerPos = playerPos;
		_lastCameraPos = cameraPos;
		_lastCameraForward = cameraForward;

		_rendered.Clear();
		_visible.Clear();
		LoadedCount = 0;
		VisibleCount = 0;
		FrustumCulledCount = 0;

		bool hasFrustum = GoldPileFrustumCache.TryGet( camera, out Plane[] frustumPlanes );
		int hysteresis = _settings.lodHysteresisFrames;
		IReadOnlyList<GoldPileChunk> chunks = _grid.Chunks;
		bool changed = false;

		for ( int i = 0; i < chunks.Count; i++ )
		{
			GoldPileChunk chunk = chunks[ i ];
			int prevLod = chunk.Lod;
			bool prevFrustum = chunk.FrustumVisible;
			GoldPileChunkStreamState prevState = chunk.State;

			float distSqr = PlanarDistanceToBoundsSqr( playerPos, chunk.WorldBounds );

			LoadedCount++;
			chunk.State = GoldPileChunkStreamState.Loaded;

			int desiredLod = _settings.EvaluateLodSqr( distSqr );
			ApplyLodHysteresis( chunk, desiredLod, hysteresis );

			bool inFrustum = !hasFrustum || GeometryUtility.TestPlanesAABB( frustumPlanes, chunk.WorldBounds );
			chunk.FrustumVisible = inFrustum;

			if ( !inFrustum )
			{
				FrustumCulledCount++;
				if ( prevLod != chunk.Lod || prevFrustum != inFrustum || prevState != chunk.State )
				{
					changed = true;
					_dirtyChunkIndices.Add( i );
				}

				continue;
			}

			VisibleCount++;
			_visible.Add( chunk );

			if ( chunk.Lod >= 3 )
			{
				chunk.State = GoldPileChunkStreamState.Visible;
			}
			else
			{
				chunk.State = GoldPileChunkStreamState.Rendered;
				_rendered.Add( chunk );
			}

			if ( prevLod != chunk.Lod || prevFrustum != inFrustum || prevState != chunk.State )
			{
				changed = true;
				_dirtyChunkIndices.Add( i );
			}
		}

		LastDirtyChunkCount = _dirtyChunkIndices.Count;
		LastTickChanged = changed;
		return changed;
	}

	bool HasPendingLodHysteresis()
	{
		if ( _grid == null || _settings == null )
			return false;

		int hysteresis = Mathf.Max( 1, _settings.lodHysteresisFrames );
		IReadOnlyList<GoldPileChunk> chunks = _grid.Chunks;
		for ( int i = 0; i < chunks.Count; i++ )
		{
			GoldPileChunk chunk = chunks[ i ];
			if ( chunk.PendingLod == chunk.Lod )
				continue;

			int framesNeeded = chunk.PendingLod > chunk.Lod ? hysteresis : 1;
			if ( chunk.LodStableFrames < framesNeeded )
				return true;
		}

		return false;
	}

	static void ApplyLodHysteresis( GoldPileChunk chunk, int desiredLod, int hysteresisFrames )
	{
		if ( desiredLod == chunk.PendingLod )
		{
			chunk.LodStableFrames++;
		}
		else
		{
			chunk.PendingLod = desiredLod;
			chunk.LodStableFrames = 0;
		}

		// Promote to finer LOD immediately; demote only after stable coarser samples.
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
