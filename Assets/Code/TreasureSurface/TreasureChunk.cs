using UnityEngine;

/// <summary>
/// Simulation-only chunk data. No gameplay object references.
/// </summary>
public sealed class TreasureChunk
{
	public readonly TreasureChunkCoord Coord;
	public Bounds Bounds;
	public int Version;
	public bool Dirty;
	public bool Frozen;
	public bool Loaded;

	public float[] Height;
	public float[] SmoothedHeight;
	public float[] NormalX;
	public float[] NormalZ;
	public float[] FlowX;
	public float[] FlowZ;
	public float[] Slope;
	public byte[] Material;
	public byte[] Flags;
	public float[] BaseHeight;
	public byte[] PaintTraversable;
	public byte[] PaintMaterial;

	int _resolution;
	bool _dirtyFull;
	int _dirtyMinX;
	int _dirtyMaxX;
	int _dirtyMinZ;
	int _dirtyMaxZ;

	public int Resolution => _resolution;

	public TreasureChunk( TreasureChunkCoord coord )
	{
		Coord = coord;
	}

	public void Allocate( int resolution, float baseHeight )
	{
		_resolution = Mathf.Max( 8, resolution );
		int count = _resolution * _resolution;

		Height = new float[ count ];
		SmoothedHeight = new float[ count ];
		NormalX = new float[ count ];
		NormalZ = new float[ count ];
		FlowX = new float[ count ];
		FlowZ = new float[ count ];
		Slope = new float[ count ];
		Material = new byte[ count ];
		Flags = new byte[ count ];
		BaseHeight = new float[ count ];
		PaintTraversable = new byte[ count ];
		PaintMaterial = new byte[ count ];

		for ( int i = 0; i < count; i++ )
		{
			BaseHeight[ i ] = baseHeight;
			Height[ i ] = baseHeight;
			SmoothedHeight[ i ] = baseHeight;
			NormalX[ i ] = 0f;
			NormalZ[ i ] = 0f;
			FlowX[ i ] = 0f;
			FlowZ[ i ] = 0f;
			Slope[ i ] = 0f;
			Material[ i ] = ( byte )TreasureSurfaceMaterial.Stone;
			PaintMaterial[ i ] = ( byte )TreasureSurfaceMaterial.Stone;
			PaintTraversable[ i ] = 1;
			Flags[ i ] = ( byte )( TreasureCellFlags.Traversable | TreasureCellFlags.Stable );
		}

		Loaded = true;
		Frozen = false;
		MarkDirtyFull();
	}

	public void Release()
	{
		Height = null;
		SmoothedHeight = null;
		NormalX = null;
		NormalZ = null;
		FlowX = null;
		FlowZ = null;
		Slope = null;
		Material = null;
		Flags = null;
		BaseHeight = null;
		PaintTraversable = null;
		PaintMaterial = null;
		Loaded = false;
		ClearDirty();
	}

	public int Index( int x, int z )
	{
		return z * _resolution + x;
	}

	public void MarkDirtyFull()
	{
		Dirty = true;
		_dirtyFull = true;
		_dirtyMinX = 0;
		_dirtyMaxX = _resolution - 1;
		_dirtyMinZ = 0;
		_dirtyMaxZ = _resolution - 1;
	}

	public void ExpandDirtyRect( int minX, int maxX, int minZ, int maxZ )
	{
		Dirty = true;
		if ( _dirtyFull )
			return;

		if ( !DirtyHadRect() )
		{
			_dirtyMinX = minX;
			_dirtyMaxX = maxX;
			_dirtyMinZ = minZ;
			_dirtyMaxZ = maxZ;
			return;
		}

		if ( minX < _dirtyMinX )
			_dirtyMinX = minX;
		if ( maxX > _dirtyMaxX )
			_dirtyMaxX = maxX;
		if ( minZ < _dirtyMinZ )
			_dirtyMinZ = minZ;
		if ( maxZ > _dirtyMaxZ )
			_dirtyMaxZ = maxZ;
	}

	/// <summary>
	/// Returns the accumulated dirty cell rect. When not dirty, returns false.
	/// </summary>
	public bool TryGetDirtyRect( out int minX, out int maxX, out int minZ, out int maxZ )
	{
		if ( !Dirty || _resolution <= 0 )
		{
			minX = maxX = minZ = maxZ = 0;
			return false;
		}

		if ( _dirtyFull || !DirtyHadRect() )
		{
			minX = 0;
			maxX = _resolution - 1;
			minZ = 0;
			maxZ = _resolution - 1;
			return true;
		}

		minX = Mathf.Clamp( _dirtyMinX, 0, _resolution - 1 );
		maxX = Mathf.Clamp( _dirtyMaxX, 0, _resolution - 1 );
		minZ = Mathf.Clamp( _dirtyMinZ, 0, _resolution - 1 );
		maxZ = Mathf.Clamp( _dirtyMaxZ, 0, _resolution - 1 );
		if ( minX > maxX || minZ > maxZ )
		{
			minX = 0;
			maxX = _resolution - 1;
			minZ = 0;
			maxZ = _resolution - 1;
		}

		return true;
	}

	public void ClearDirty()
	{
		Dirty = false;
		_dirtyFull = false;
		_dirtyMinX = 0;
		_dirtyMaxX = -1;
		_dirtyMinZ = 0;
		_dirtyMaxZ = -1;
	}

	bool DirtyHadRect()
	{
		return _dirtyMaxX >= _dirtyMinX && _dirtyMaxZ >= _dirtyMinZ;
	}
}
