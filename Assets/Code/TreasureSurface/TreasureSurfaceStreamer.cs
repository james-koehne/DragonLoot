using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Loads / freezes chunks around the player. Pile-overlapping chunks stay loaded.
/// </summary>
public sealed class TreasureSurfaceStreamer
{
	readonly TreasureSurfaceWorld _world;
	readonly HashSet<TreasureChunkCoord> _keepLoaded = new HashSet<TreasureChunkCoord>();

	public TreasureSurfaceStreamer( TreasureSurfaceWorld world )
	{
		_world = world;
	}

	public void MarkKeepLoaded( TreasureChunkCoord coord )
	{
		_keepLoaded.Add( coord );
	}

	public void ClearKeepLoaded()
	{
		_keepLoaded.Clear();
	}

	public void RemoveKeepLoaded( TreasureChunkCoord coord )
	{
		_keepLoaded.Remove( coord );
	}

	public bool IsPinned( TreasureChunkCoord coord )
	{
		return _keepLoaded.Contains( coord );
	}

	public void Tick()
	{
		if ( _world == null || !_world.IsInitialized )
			return;

		TreasureSurfaceDefinition def = _world.Definition;
		Vector3 focus = def.worldOrigin;
		if ( TreasureProximitySleep.TryGetPlayerPosition( out Vector3 playerPos ) )
			focus = playerPos;

		float radius = def.activeRadius;
		float radiusSq = radius * radius;
		float chunkSize = def.chunkSize;
		float halfX = def.worldSizeX * 0.5f;
		float halfZ = def.worldSizeZ * 0.5f;

		float localX = focus.x - def.worldOrigin.x + halfX;
		float localZ = focus.z - def.worldOrigin.z + halfZ;
		int centerX = Mathf.FloorToInt( localX / chunkSize );
		int centerZ = Mathf.FloorToInt( localZ / chunkSize );
		int span = Mathf.CeilToInt( radius / chunkSize ) + 1;

		for ( int z = centerZ - span; z <= centerZ + span; z++ )
		{
			for ( int x = centerX - span; x <= centerX + span; x++ )
			{
				if ( x < 0 || z < 0 || x >= def.ChunkCountX || z >= def.ChunkCountZ )
					continue;

				TreasureChunkCoord coord = new TreasureChunkCoord( x, z );
				Vector3 chunkCenter = _world.GetChunkCenter( coord );
				float dx = chunkCenter.x - focus.x;
				float dz = chunkCenter.z - focus.z;
				if ( dx * dx + dz * dz > radiusSq && !_keepLoaded.Contains( coord ) )
					continue;

				_world.EnsureChunkLoaded( coord );
				TreasureChunk chunk = _world.GetLoadedChunk( coord );
				if ( chunk != null )
					chunk.Frozen = false;
			}
		}

		_world.ForEachLoadedChunk( chunk =>
		{
			if ( _keepLoaded.Contains( chunk.Coord ) )
			{
				chunk.Frozen = false;
				return;
			}

			Vector3 chunkCenter = _world.GetChunkCenter( chunk.Coord );
			float dx = chunkCenter.x - focus.x;
			float dz = chunkCenter.z - focus.z;
			chunk.Frozen = dx * dx + dz * dz > radiusSq;
		} );
	}
}
