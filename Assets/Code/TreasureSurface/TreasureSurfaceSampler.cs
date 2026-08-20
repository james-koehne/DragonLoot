using UnityEngine;

/// <summary>
/// O(1) world-space sampling against loaded treasure surface chunks.
/// </summary>
public sealed class TreasureSurfaceSampler
{
	readonly TreasureSurfaceWorld _world;

	public TreasureSurfaceSampler( TreasureSurfaceWorld world )
	{
		_world = world;
	}

	public bool TrySample( Vector3 worldPos, out TreasureSurfaceSample sample )
	{
		sample = default;
		if ( _world == null || !_world.IsInitialized )
			return false;

		if ( !_world.TryGetChunkCoord( worldPos, out TreasureChunkCoord coord ) )
			return false;

		TreasureChunk chunk = _world.GetLoadedChunk( coord );
		if ( chunk == null || !chunk.Loaded || chunk.Height == null )
			return false;

		TreasureSurfaceDefinition def = _world.Definition;
		float cell = def.CellSize;
		float halfX = def.worldSizeX * 0.5f;
		float halfZ = def.worldSizeZ * 0.5f;
		float localX = worldPos.x - def.worldOrigin.x + halfX;
		float localZ = worldPos.z - def.worldOrigin.z + halfZ;

		float chunkOriginX = coord.X * def.chunkSize;
		float chunkOriginZ = coord.Z * def.chunkSize;
		float fx = ( localX - chunkOriginX ) / cell;
		float fz = ( localZ - chunkOriginZ ) / cell;

		int res = chunk.Resolution;
		int x0 = Mathf.Clamp( Mathf.FloorToInt( fx ), 0, res - 1 );
		int z0 = Mathf.Clamp( Mathf.FloorToInt( fz ), 0, res - 1 );
		int x1 = Mathf.Min( x0 + 1, res - 1 );
		int z1 = Mathf.Min( z0 + 1, res - 1 );
		float tx = Mathf.Clamp01( fx - x0 );
		float tz = Mathf.Clamp01( fz - z0 );

		int i00 = chunk.Index( x0, z0 );
		int i10 = chunk.Index( x1, z0 );
		int i01 = chunk.Index( x0, z1 );
		int i11 = chunk.Index( x1, z1 );

		float h0 = Mathf.Lerp( chunk.SmoothedHeight[ i00 ], chunk.SmoothedHeight[ i10 ], tx );
		float h1 = Mathf.Lerp( chunk.SmoothedHeight[ i01 ], chunk.SmoothedHeight[ i11 ], tx );
		float height = Mathf.Lerp( h0, h1, tz );

		float nx0 = Mathf.Lerp( chunk.NormalX[ i00 ], chunk.NormalX[ i10 ], tx );
		float nx1 = Mathf.Lerp( chunk.NormalX[ i01 ], chunk.NormalX[ i11 ], tx );
		float nz0 = Mathf.Lerp( chunk.NormalZ[ i00 ], chunk.NormalZ[ i10 ], tx );
		float nz1 = Mathf.Lerp( chunk.NormalZ[ i01 ], chunk.NormalZ[ i11 ], tx );
		float nx = Mathf.Lerp( nx0, nx1, tz );
		float nz = Mathf.Lerp( nz0, nz1, tz );
		Vector3 normal = new Vector3( nx, 1f, nz ).normalized;

		float fx0 = Mathf.Lerp( chunk.FlowX[ i00 ], chunk.FlowX[ i10 ], tx );
		float fx1 = Mathf.Lerp( chunk.FlowX[ i01 ], chunk.FlowX[ i11 ], tx );
		float fz0 = Mathf.Lerp( chunk.FlowZ[ i00 ], chunk.FlowZ[ i10 ], tx );
		float fz1 = Mathf.Lerp( chunk.FlowZ[ i01 ], chunk.FlowZ[ i11 ], tx );

		float slope0 = Mathf.Lerp( chunk.Slope[ i00 ], chunk.Slope[ i10 ], tx );
		float slope1 = Mathf.Lerp( chunk.Slope[ i01 ], chunk.Slope[ i11 ], tx );

		byte flags = chunk.Flags[ i00 ];
		bool traversable = chunk.PaintTraversable[ i00 ] != 0
			&& chunk.PaintTraversable[ i10 ] != 0
			&& chunk.PaintTraversable[ i01 ] != 0
			&& chunk.PaintTraversable[ i11 ] != 0;
		bool stable = ( flags & ( byte )TreasureCellFlags.Stable ) != 0;

		sample.Valid = true;
		sample.Height = height;
		sample.Normal = normal;
		sample.Flow = new Vector2( Mathf.Lerp( fx0, fx1, tz ), Mathf.Lerp( fz0, fz1, tz ) );
		sample.Slope = Mathf.Lerp( slope0, slope1, tz );
		sample.Material = ( TreasureSurfaceMaterial )chunk.Material[ i00 ];
		sample.Traversable = traversable;
		sample.Stable = stable;
		sample.ChunkCoord = coord;
		sample.CellX = x0;
		sample.CellZ = z0;
		return true;
	}

	/// <summary>
	/// Strict: all four bilinear paint corners must be traversable (matches <see cref="TrySample"/>).
	/// Lenient: only the floor cell under the point must be traversable.
	/// </summary>
	public bool IsTraversableAt( Vector3 worldPos, bool strict )
	{
		if ( strict )
		{
			return TrySample( worldPos, out TreasureSurfaceSample sample ) && sample.Traversable;
		}

		if ( _world == null || !_world.IsInitialized )
			return false;
		if ( !_world.TryGetChunkCoord( worldPos, out TreasureChunkCoord coord ) )
			return false;

		TreasureChunk chunk = _world.GetLoadedChunk( coord );
		if ( chunk == null || !chunk.Loaded || chunk.PaintTraversable == null )
			return false;

		TreasureSurfaceDefinition def = _world.Definition;
		float cell = def.CellSize;
		float halfX = def.worldSizeX * 0.5f;
		float halfZ = def.worldSizeZ * 0.5f;
		float localX = worldPos.x - def.worldOrigin.x + halfX;
		float localZ = worldPos.z - def.worldOrigin.z + halfZ;
		float chunkOriginX = coord.X * def.chunkSize;
		float chunkOriginZ = coord.Z * def.chunkSize;
		float fx = ( localX - chunkOriginX ) / cell;
		float fz = ( localZ - chunkOriginZ ) / cell;
		int x = Mathf.Clamp( Mathf.FloorToInt( fx ), 0, chunk.Resolution - 1 );
		int z = Mathf.Clamp( Mathf.FloorToInt( fz ), 0, chunk.Resolution - 1 );
		return chunk.PaintTraversable[ chunk.Index( x, z ) ] != 0;
	}
}
