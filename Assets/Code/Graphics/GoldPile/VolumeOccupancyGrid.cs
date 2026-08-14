using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Deterministic XZ spatial hash for volume occupancy (artifact-vs-artifact).
/// Insert order is author order; queries only scan neighbor cells.
/// </summary>
public sealed class VolumeOccupancyGrid
{
	readonly List<Bounds> _bounds = new List<Bounds>( 128 );
	readonly List<int>[] _cells;
	readonly List<int> _stamp = new List<int>( 128 );
	readonly float _cellSize;
	readonly float _originX;
	readonly float _originZ;
	readonly int _countX;
	readonly int _countZ;
	int _queryStamp = 1;

	public int Count => _bounds.Count;

	public VolumeOccupancyGrid( float worldSize, float cellSize )
	{
		float size = Mathf.Max( 0.1f, worldSize );
		_cellSize = Mathf.Max( 0.05f, cellSize );
		_originX = -size * 0.5f;
		_originZ = -size * 0.5f;
		_countX = Mathf.Max( 1, Mathf.CeilToInt( size / _cellSize ) );
		_countZ = Mathf.Max( 1, Mathf.CeilToInt( size / _cellSize ) );
		_cells = new List<int>[ _countX * _countZ ];
		for ( int i = 0; i < _cells.Length; i++ )
			_cells[ i ] = new List<int>( 4 );
	}

	public void Clear()
	{
		_bounds.Clear();
		_stamp.Clear();
		for ( int i = 0; i < _cells.Length; i++ )
			_cells[ i ].Clear();
		_queryStamp = 1;
	}

	public void Add( Bounds bounds )
	{
		int id = _bounds.Count;
		_bounds.Add( bounds );
		_stamp.Add( 0 );
		GetCellRange( bounds, out int minX, out int maxX, out int minZ, out int maxZ );
		for ( int z = minZ; z <= maxZ; z++ )
		{
			for ( int x = minX; x <= maxX; x++ )
				_cells[ z * _countX + x ].Add( id );
		}
	}

	public float OverlapScore( Bounds candidate )
	{
		if ( _bounds.Count == 0 )
			return 0f;

		int stamp = NextStamp();
		GetCellRange( candidate, out int minX, out int maxX, out int minZ, out int maxZ );
		float total = 0f;
		for ( int z = minZ; z <= maxZ; z++ )
		{
			for ( int x = minX; x <= maxX; x++ )
			{
				List<int> cell = _cells[ z * _countX + x ];
				for ( int i = 0; i < cell.Count; i++ )
				{
					int id = cell[ i ];
					if ( _stamp[ id ] == stamp )
						continue;
					_stamp[ id ] = stamp;

					Bounds other = _bounds[ id ];
					if ( !candidate.Intersects( other ) )
						continue;

					Vector3 min = Vector3.Max( candidate.min, other.min );
					Vector3 max = Vector3.Min( candidate.max, other.max );
					Vector3 size = max - min;
					if ( size.x <= 0f || size.y <= 0f || size.z <= 0f )
						continue;

					total += size.x * size.y * size.z;
				}
			}
		}

		return total;
	}

	public bool IsTooClose( Vector3 localPos, float spacingSq )
	{
		if ( _bounds.Count == 0 || spacingSq <= 1e-8f )
			return false;

		int stamp = NextStamp();
		float spacing = Mathf.Sqrt( spacingSq );
		Bounds probe = new Bounds( localPos, Vector3.one * ( spacing * 2f ) );
		GetCellRange( probe, out int minX, out int maxX, out int minZ, out int maxZ );
		for ( int z = minZ; z <= maxZ; z++ )
		{
			for ( int x = minX; x <= maxX; x++ )
			{
				List<int> cell = _cells[ z * _countX + x ];
				for ( int i = 0; i < cell.Count; i++ )
				{
					int id = cell[ i ];
					if ( _stamp[ id ] == stamp )
						continue;
					_stamp[ id ] = stamp;

					Vector3 d = _bounds[ id ].ClosestPoint( localPos ) - localPos;
					if ( d.sqrMagnitude < spacingSq )
						return true;
				}
			}
		}

		return false;
	}

	int NextStamp()
	{
		_queryStamp++;
		if ( _queryStamp == int.MaxValue )
		{
			_queryStamp = 1;
			for ( int i = 0; i < _stamp.Count; i++ )
				_stamp[ i ] = 0;
		}

		return _queryStamp;
	}

	void GetCellRange( Bounds bounds, out int minX, out int maxX, out int minZ, out int maxZ )
	{
		minX = CellX( bounds.min.x );
		maxX = CellX( bounds.max.x );
		minZ = CellZ( bounds.min.z );
		maxZ = CellZ( bounds.max.z );
	}

	int CellX( float localX )
	{
		int c = Mathf.FloorToInt( ( localX - _originX ) / _cellSize );
		if ( c < 0 )
			return 0;
		if ( c >= _countX )
			return _countX - 1;
		return c;
	}

	int CellZ( float localZ )
	{
		int c = Mathf.FloorToInt( ( localZ - _originZ ) / _cellSize );
		if ( c < 0 )
			return 0;
		if ( c >= _countZ )
			return _countZ - 1;
		return c;
	}
}
