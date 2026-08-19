using UnityEngine;

/// <summary>
/// Bridson-style XZ occupancy for GPU coin seats: at most one point per cell of size
/// spacing/sqrt(2). Neighbor queries scan a fixed 5x5 window (exact min-distance).
/// </summary>
public sealed class CoinSeatOccupancy
{
	public const int Empty = int.MinValue;
	const int NeighborRadius = 2;

	readonly int[] _ids;
	readonly float[] _xs;
	readonly float[] _zs;
	readonly float _worldSize;
	readonly float _spacing;
	readonly float _spacingSq;
	readonly float _cellSize;
	readonly float _originX;
	readonly float _originZ;
	readonly int _countX;
	readonly int _countZ;
	int _count;

	public int Count => _count;
	public float Spacing => _spacing;
	public float CellSize => _cellSize;
	public int CountX => _countX;
	public int CountZ => _countZ;

	public CoinSeatOccupancy( float worldSize, float spacing )
	{
		_worldSize = Mathf.Max( 0.1f, worldSize );
		_spacing = Mathf.Max( 0.01f, spacing );
		_spacingSq = _spacing * _spacing;
		_cellSize = _spacing / Mathf.Sqrt( 2f );
		_originX = -_worldSize * 0.5f;
		_originZ = -_worldSize * 0.5f;
		_countX = Mathf.Max( 1, Mathf.CeilToInt( _worldSize / _cellSize ) );
		_countZ = Mathf.Max( 1, Mathf.CeilToInt( _worldSize / _cellSize ) );
		int n = _countX * _countZ;
		_ids = new int[ n ];
		_xs = new float[ n ];
		_zs = new float[ n ];
		for ( int i = 0; i < n; i++ )
			_ids[ i ] = Empty;
	}

	public bool Matches( float worldSize, float spacing )
	{
		return Mathf.Abs( _worldSize - worldSize ) <= 0.01f
			&& Mathf.Abs( _spacing - spacing ) <= 0.001f;
	}

	public void Clear()
	{
		for ( int i = 0; i < _ids.Length; i++ )
			_ids[ i ] = Empty;
		_count = 0;
	}

	public bool IsTooClose( float x, float z )
	{
		int cx = CellX( x );
		int cz = CellZ( z );
		int minX = cx - NeighborRadius;
		int maxX = cx + NeighborRadius;
		int minZ = cz - NeighborRadius;
		int maxZ = cz + NeighborRadius;
		if ( minX < 0 )
			minX = 0;
		if ( maxX >= _countX )
			maxX = _countX - 1;
		if ( minZ < 0 )
			minZ = 0;
		if ( maxZ >= _countZ )
			maxZ = _countZ - 1;

		for ( int iz = minZ; iz <= maxZ; iz++ )
		{
			int row = iz * _countX;
			for ( int ix = minX; ix <= maxX; ix++ )
			{
				int idx = row + ix;
				if ( _ids[ idx ] == Empty )
					continue;
				float dx = _xs[ idx ] - x;
				float dz = _zs[ idx ] - z;
				if ( dx * dx + dz * dz < _spacingSq )
					return true;
			}
		}

		return false;
	}

	public bool TryAdd( int id, float x, float z )
	{
		int idx = Index( x, z );
		if ( _ids[ idx ] != Empty )
			return _ids[ idx ] == id;

		if ( IsTooClose( x, z ) )
			return false;

		_ids[ idx ] = id;
		_xs[ idx ] = x;
		_zs[ idx ] = z;
		_count++;
		return true;
	}

	public void Remove( float x, float z )
	{
		int cx = CellX( x );
		int cz = CellZ( z );
		int minX = cx - NeighborRadius;
		int maxX = cx + NeighborRadius;
		int minZ = cz - NeighborRadius;
		int maxZ = cz + NeighborRadius;
		if ( minX < 0 )
			minX = 0;
		if ( maxX >= _countX )
			maxX = _countX - 1;
		if ( minZ < 0 )
			minZ = 0;
		if ( maxZ >= _countZ )
			maxZ = _countZ - 1;

		const float epsSq = 1e-8f;
		for ( int iz = minZ; iz <= maxZ; iz++ )
		{
			int row = iz * _countX;
			for ( int ix = minX; ix <= maxX; ix++ )
			{
				int idx = row + ix;
				if ( _ids[ idx ] == Empty )
					continue;
				float dx = _xs[ idx ] - x;
				float dz = _zs[ idx ] - z;
				if ( dx * dx + dz * dz > epsSq )
					continue;

				_ids[ idx ] = Empty;
				_count--;
				if ( _count < 0 )
					_count = 0;
				return;
			}
		}
	}

	public bool IsCellEmpty( int cellX, int cellZ )
	{
		if ( cellX < 0 || cellZ < 0 || cellX >= _countX || cellZ >= _countZ )
			return false;
		return _ids[ cellZ * _countX + cellX ] == Empty;
	}

	public void CellCenter( int cellX, int cellZ, out float x, out float z )
	{
		x = _originX + ( cellX + 0.5f ) * _cellSize;
		z = _originZ + ( cellZ + 0.5f ) * _cellSize;
	}

	public int CellX( float localX )
	{
		int c = Mathf.FloorToInt( ( localX - _originX ) / _cellSize );
		if ( c < 0 )
			return 0;
		if ( c >= _countX )
			return _countX - 1;
		return c;
	}

	public int CellZ( float localZ )
	{
		int c = Mathf.FloorToInt( ( localZ - _originZ ) / _cellSize );
		if ( c < 0 )
			return 0;
		if ( c >= _countZ )
			return _countZ - 1;
		return c;
	}

	int Index( float x, float z )
	{
		return CellZ( z ) * _countX + CellX( x );
	}
}
