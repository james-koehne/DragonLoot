using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Frustum + distance-LOD streaming for pile loot chunks.
/// Chunks stay loaded while the pile is bound; only visibility gates drawing.
/// </summary>
public sealed class GoldPileChunkStreamer
{
	static readonly Plane[] FrustumPlanes = new Plane[ 6 ];

	readonly List<GoldPileChunk> _rendered = new List<GoldPileChunk>( 32 );
	readonly List<GoldPileChunk> _visible = new List<GoldPileChunk>( 32 );

	GoldPileChunkGrid _grid;
	GoldPileLootStreamSettings _settings;

	public IReadOnlyList<GoldPileChunk> RenderedChunks => _rendered;
	public IReadOnlyList<GoldPileChunk> VisibleChunks => _visible;
	public int LoadedCount { get; private set; }
	public int VisibleCount { get; private set; }
	public int RenderedCount => _rendered.Count;
	public int FrustumCulledCount { get; private set; }

	public void Bind( GoldPileChunkGrid grid, GoldPileLootStreamSettings settings )
	{
		_grid = grid;
		_settings = settings;
		_rendered.Clear();
		_visible.Clear();
		LoadedCount = 0;
		VisibleCount = 0;
		FrustumCulledCount = 0;

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
		LoadedCount = 0;
		VisibleCount = 0;
		FrustumCulledCount = 0;
	}

	public void Tick( Vector3 playerPos, Camera camera )
	{
		_rendered.Clear();
		_visible.Clear();
		LoadedCount = 0;
		VisibleCount = 0;
		FrustumCulledCount = 0;

		if ( _grid == null || _settings == null || _grid.ChunkCount == 0 )
			return;

		bool hasFrustum = camera != null;
		if ( hasFrustum )
			GeometryUtility.CalculateFrustumPlanes( camera, FrustumPlanes );

		int hysteresis = _settings.lodHysteresisFrames;
		IReadOnlyList<GoldPileChunk> chunks = _grid.Chunks;
		for ( int i = 0; i < chunks.Count; i++ )
		{
			GoldPileChunk chunk = chunks[ i ];
			// Planar distance to the chunk volume (0 when the player is inside),
			// never distance to the pile root / chunk AABB center alone.
			float dist = PlanarDistanceToBounds( playerPos, chunk.WorldBounds );

			LoadedCount++;
			chunk.State = GoldPileChunkStreamState.Loaded;

			int desiredLod = _settings.EvaluateLod( dist );
			ApplyLodHysteresis( chunk, desiredLod, hysteresis );

			bool inFrustum = !hasFrustum || GeometryUtility.TestPlanesAABB( FrustumPlanes, chunk.WorldBounds );
			chunk.FrustumVisible = inFrustum;

			if ( !inFrustum )
			{
				FrustumCulledCount++;
				continue;
			}

			VisibleCount++;
			_visible.Add( chunk );

			if ( chunk.Lod >= 3 )
			{
				chunk.State = GoldPileChunkStreamState.Visible;
				continue;
			}

			chunk.State = GoldPileChunkStreamState.Rendered;
			_rendered.Add( chunk );
		}
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

	/// <summary>XZ distance from <paramref name="worldPos"/> to the closest point on <paramref name="bounds"/>.</summary>
	static float PlanarDistanceToBounds( Vector3 worldPos, Bounds bounds )
	{
		Vector3 closest = bounds.ClosestPoint( worldPos );
		float dx = closest.x - worldPos.x;
		float dz = closest.z - worldPos.z;
		return Mathf.Sqrt( dx * dx + dz * dz );
	}
}
