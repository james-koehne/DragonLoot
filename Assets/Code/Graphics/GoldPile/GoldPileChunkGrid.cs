using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Divides a pile heightfield into fixed-size chunk views (default 8x8m).
/// </summary>
public sealed class GoldPileChunkGrid
{
	public const float DefaultChunkSize = 8f;

	readonly List<GoldPileChunk> _chunks = new List<GoldPileChunk>();
	float _chunkSize = DefaultChunkSize;
	int _countX;
	int _countZ;
	float _worldSize;
	float _maxHeight;
	int _worldSeed;
	Transform _pileRoot;
	GoldPileHeightfield _heightfield;

	public float ChunkSize => _chunkSize;
	public int CountX => _countX;
	public int CountZ => _countZ;
	public int ChunkCount => _chunks.Count;
	public IReadOnlyList<GoldPileChunk> Chunks => _chunks;

	public void Build(
		GoldPileHeightfield heightfield,
		Transform pileRoot,
		float chunkSize,
		int worldSeed )
	{
		Release();

		_heightfield = heightfield;
		_pileRoot = pileRoot;
		_chunkSize = Mathf.Max( 1f, chunkSize );
		_worldSeed = worldSeed;
		_worldSize = heightfield != null ? heightfield.WorldSize : _chunkSize;
		_maxHeight = heightfield != null ? heightfield.MaxHeight : 1f;

		_countX = Mathf.Max( 1, Mathf.CeilToInt( _worldSize / _chunkSize ) );
		_countZ = Mathf.Max( 1, Mathf.CeilToInt( _worldSize / _chunkSize ) );

		_chunks.Capacity = _countX * _countZ;
		for ( int z = 0; z < _countZ; z++ )
		{
			for ( int x = 0; x < _countX; x++ )
			{
				GoldPileChunkCoord coord = new GoldPileChunkCoord( x, z );
				int seed = HashChunkSeed( _worldSeed, x, z );
				GoldPileChunk chunk = new GoldPileChunk( coord, seed );
				RebuildChunkBounds( chunk );
				_chunks.Add( chunk );
			}
		}
	}

	public void Release()
	{
		for ( int i = 0; i < _chunks.Count; i++ )
			_chunks[ i ].State = GoldPileChunkStreamState.Unloaded;

		_chunks.Clear();
		_heightfield = null;
		_pileRoot = null;
		_countX = 0;
		_countZ = 0;
	}

	public GoldPileChunk FindChunkAtLocal( float localX, float localZ )
	{
		if ( _countX <= 0 || _countZ <= 0 )
			return null;

		float half = _worldSize * 0.5f;
		int x = Mathf.FloorToInt( ( localX + half ) / _chunkSize );
		int z = Mathf.FloorToInt( ( localZ + half ) / _chunkSize );
		x = Mathf.Clamp( x, 0, _countX - 1 );
		z = Mathf.Clamp( z, 0, _countZ - 1 );
		return GetChunk( x, z );
	}

	public void LocalToChunk( float localX, float localZ, out int chunkX, out int chunkZ )
	{
		if ( _countX <= 0 || _countZ <= 0 )
		{
			chunkX = 0;
			chunkZ = 0;
			return;
		}

		float half = _worldSize * 0.5f;
		chunkX = Mathf.Clamp( Mathf.FloorToInt( ( localX + half ) / _chunkSize ), 0, _countX - 1 );
		chunkZ = Mathf.Clamp( Mathf.FloorToInt( ( localZ + half ) / _chunkSize ), 0, _countZ - 1 );
	}

	public GoldPileChunk GetChunk( int x, int z )
	{
		if ( x < 0 || z < 0 || x >= _countX || z >= _countZ )
			return null;
		return _chunks[ z * _countX + x ];
	}

	public GoldPileChunk GetChunk( GoldPileChunkCoord coord )
	{
		return GetChunk( coord.X, coord.Z );
	}

	public void RebuildAllBounds()
	{
		for ( int i = 0; i < _chunks.Count; i++ )
			RebuildChunkBounds( _chunks[ i ] );
	}

	public void MarkDirtyInRadius( Vector3 worldCenter, float radius )
	{
		if ( _pileRoot == null || _chunks.Count == 0 )
			return;

		float markRadius = Mathf.Max( 0.01f, radius ) + _chunkSize * 0.15f;
		float markRadiusSq = markRadius * markRadius;
		Vector3 local = _pileRoot.InverseTransformPoint( worldCenter );

		for ( int i = 0; i < _chunks.Count; i++ )
		{
			GoldPileChunk chunk = _chunks[ i ];
			Vector3 closest = chunk.LocalBounds.ClosestPoint( local );
			float dx = closest.x - local.x;
			float dz = closest.z - local.z;
			if ( dx * dx + dz * dz > markRadiusSq )
				continue;

			chunk.Dirty = true;
			MarkBorderNeighborsDirty( chunk.Coord );
			RebuildChunkBounds( chunk );
		}
	}

	public void MarkAllDirty()
	{
		for ( int i = 0; i < _chunks.Count; i++ )
		{
			_chunks[ i ].Dirty = true;
			RebuildChunkBounds( _chunks[ i ] );
		}
	}

	void MarkBorderNeighborsDirty( GoldPileChunkCoord coord )
	{
		for ( int oz = -1; oz <= 1; oz++ )
		{
			for ( int ox = -1; ox <= 1; ox++ )
			{
				if ( ox == 0 && oz == 0 )
					continue;
				GoldPileChunk neighbor = GetChunk( coord.X + ox, coord.Z + oz );
				if ( neighbor != null )
					neighbor.Dirty = true;
			}
		}
	}

	void RebuildChunkBounds( GoldPileChunk chunk )
	{
		float half = _worldSize * 0.5f;
		float minX = -half + chunk.Coord.X * _chunkSize;
		float minZ = -half + chunk.Coord.Z * _chunkSize;
		float maxX = Mathf.Min( half, minX + _chunkSize );
		float maxZ = Mathf.Min( half, minZ + _chunkSize );

		float minY = 0f;
		float maxY = 0.05f;
		if ( _heightfield != null && _heightfield.IsInitialized )
		{
			const int samples = 5;
			for ( int iz = 0; iz < samples; iz++ )
			{
				float tz = iz / ( float )( samples - 1 );
				float lz = Mathf.Lerp( minZ, maxZ, tz );
				for ( int ix = 0; ix < samples; ix++ )
				{
					float tx = ix / ( float )( samples - 1 );
					float lx = Mathf.Lerp( minX, maxX, tx );
					float h = _heightfield.SampleNormalized( lx, lz ) * _heightfield.MaxHeight;
					if ( h > maxY )
						maxY = h;
				}
			}
		}
		else
		{
			maxY = _maxHeight;
		}

		Vector3 center = new Vector3( ( minX + maxX ) * 0.5f, maxY * 0.5f, ( minZ + maxZ ) * 0.5f );
		Vector3 size = new Vector3( maxX - minX, Mathf.Max( 0.1f, maxY - minY ), maxZ - minZ );
		chunk.SetLocalBounds( new Bounds( center, size ), _pileRoot );
	}

	public static int HashChunkSeed( int worldSeed, int chunkX, int chunkZ )
	{
		unchecked
		{
			int h = worldSeed;
			h = ( h * 397 ) ^ chunkX;
			h = ( h * 397 ) ^ chunkZ;
			h ^= ( h << 13 );
			h ^= ( h >> 17 );
			h ^= ( h << 5 );
			return h == 0 ? 1 : h;
		}
	}
}
