using UnityEngine;

/// <summary>
/// Simulation-only chunk data. No gameplay object references.
/// </summary>
public sealed class TreasureChunk
{
	const float DirtyRectFullUploadThreshold = 0.25f;

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

	public Texture2D HeightTexture;
	public Texture2D NormalTexture;
	public Texture2D FlowTexture;
	public Texture2D MaterialTexture;

	byte[] _heightPixels;
	Color32[] _normalPixels;
	Color32[] _flowPixels;
	byte[] _materialPixels;

	bool _gpuDirty;
	bool _gpuDirtyFull;
	int _dirtyMinX;
	int _dirtyMaxX;
	int _dirtyMinZ;
	int _dirtyMaxZ;

	int _resolution;

	public int Resolution => _resolution;
	public bool IsGpuDirty => _gpuDirty;

	public TreasureChunk( TreasureChunkCoord coord )
	{
		Coord = coord;
	}

	public void Allocate( int resolution, float baseHeight )
	{
		ReleaseGpu();

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

		_heightPixels = new byte[ count ];
		_normalPixels = new Color32[ count ];
		_flowPixels = new Color32[ count ];
		_materialPixels = new byte[ count ];

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

		HeightTexture = new Texture2D( _resolution, _resolution, TextureFormat.R8, false, true )
		{
			name = $"TreasureHeight_{Coord.X}_{Coord.Z}",
			wrapMode = TextureWrapMode.Clamp,
			filterMode = FilterMode.Bilinear
		};
		NormalTexture = new Texture2D( _resolution, _resolution, TextureFormat.RGBA32, false, true )
		{
			name = $"TreasureNormal_{Coord.X}_{Coord.Z}",
			wrapMode = TextureWrapMode.Clamp,
			filterMode = FilterMode.Bilinear
		};
		FlowTexture = new Texture2D( _resolution, _resolution, TextureFormat.RGBA32, false, true )
		{
			name = $"TreasureFlow_{Coord.X}_{Coord.Z}",
			wrapMode = TextureWrapMode.Clamp,
			filterMode = FilterMode.Bilinear
		};
		MaterialTexture = new Texture2D( _resolution, _resolution, TextureFormat.R8, false, true )
		{
			name = $"TreasureMaterial_{Coord.X}_{Coord.Z}",
			wrapMode = TextureWrapMode.Clamp,
			filterMode = FilterMode.Point
		};

		Loaded = true;
		Frozen = false;
		MarkDirtyFull();
	}

	public void Release()
	{
		ReleaseGpu();
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
		_heightPixels = null;
		_normalPixels = null;
		_flowPixels = null;
		_materialPixels = null;
		Loaded = false;
		Dirty = false;
		_gpuDirty = false;
	}

	void ReleaseGpu()
	{
		if ( HeightTexture != null )
		{
			Object.Destroy( HeightTexture );
			HeightTexture = null;
		}

		if ( NormalTexture != null )
		{
			Object.Destroy( NormalTexture );
			NormalTexture = null;
		}

		if ( FlowTexture != null )
		{
			Object.Destroy( FlowTexture );
			FlowTexture = null;
		}

		if ( MaterialTexture != null )
		{
			Object.Destroy( MaterialTexture );
			MaterialTexture = null;
		}
	}

	public int Index( int x, int z )
	{
		return z * _resolution + x;
	}

	public void MarkDirtyFull()
	{
		Dirty = true;
		MarkGpuDirtyFull();
	}

	public void MarkGpuDirtyFull()
	{
		_gpuDirty = true;
		_gpuDirtyFull = true;
		_dirtyMinX = 0;
		_dirtyMaxX = _resolution - 1;
		_dirtyMinZ = 0;
		_dirtyMaxZ = _resolution - 1;
	}

	public void ExpandDirtyRect( int minX, int maxX, int minZ, int maxZ )
	{
		Dirty = true;
		ExpandGpuDirtyRect( minX, maxX, minZ, maxZ );
	}

	public void ExpandGpuDirtyRect( int minX, int maxX, int minZ, int maxZ )
	{
		if ( !_gpuDirty )
		{
			_gpuDirty = true;
			_gpuDirtyFull = false;
			_dirtyMinX = minX;
			_dirtyMaxX = maxX;
			_dirtyMinZ = minZ;
			_dirtyMaxZ = maxZ;
			return;
		}

		if ( _gpuDirtyFull )
			return;

		if ( minX < _dirtyMinX )
			_dirtyMinX = minX;
		if ( maxX > _dirtyMaxX )
			_dirtyMaxX = maxX;
		if ( minZ < _dirtyMinZ )
			_dirtyMinZ = minZ;
		if ( maxZ > _dirtyMaxZ )
			_dirtyMaxZ = maxZ;
	}

	public bool UploadGpuIfDirty( float heightEncodeScale )
	{
		if ( !_gpuDirty || Height == null || HeightTexture == null )
			return false;

		int total = Height.Length;
		int dirtyW = _dirtyMaxX - _dirtyMinX + 1;
		int dirtyCount = dirtyW * ( _dirtyMaxZ - _dirtyMinZ + 1 );
		bool useFull = _gpuDirtyFull || dirtyCount >= total * DirtyRectFullUploadThreshold;
		float invScale = heightEncodeScale > 0.0001f ? 1f / heightEncodeScale : 1f;

		if ( useFull )
		{
			for ( int i = 0; i < total; i++ )
				WriteGpuPixel( i, invScale );
		}
		else
		{
			for ( int z = _dirtyMinZ; z <= _dirtyMaxZ; z++ )
			{
				int row = Index( _dirtyMinX, z );
				for ( int x = 0; x < dirtyW; x++ )
					WriteGpuPixel( row + x, invScale );
			}
		}

		HeightTexture.SetPixelData( _heightPixels, 0 );
		HeightTexture.Apply( false, false );
		NormalTexture.SetPixels32( _normalPixels );
		NormalTexture.Apply( false, false );
		FlowTexture.SetPixels32( _flowPixels );
		FlowTexture.Apply( false, false );
		MaterialTexture.SetPixelData( _materialPixels, 0 );
		MaterialTexture.Apply( false, false );

		_gpuDirty = false;
		_gpuDirtyFull = false;
		return true;
	}

	void WriteGpuPixel( int i, float invScale )
	{
		float h = Mathf.Clamp01( Height[ i ] * invScale );
		_heightPixels[ i ] = ( byte )Mathf.Clamp( Mathf.RoundToInt( h * 255f ), 0, 255 );

		float nx = Mathf.Clamp( NormalX[ i ] * 0.5f + 0.5f, 0f, 1f );
		float nz = Mathf.Clamp( NormalZ[ i ] * 0.5f + 0.5f, 0f, 1f );
		_normalPixels[ i ] = new Color32(
			( byte )Mathf.RoundToInt( nx * 255f ),
			255,
			( byte )Mathf.RoundToInt( nz * 255f ),
			255 );

		float fx = Mathf.Clamp( FlowX[ i ] * 0.5f + 0.5f, 0f, 1f );
		float fz = Mathf.Clamp( FlowZ[ i ] * 0.5f + 0.5f, 0f, 1f );
		_flowPixels[ i ] = new Color32(
			( byte )Mathf.RoundToInt( fx * 255f ),
			128,
			( byte )Mathf.RoundToInt( fz * 255f ),
			255 );

		_materialPixels[ i ] = Material[ i ];
	}
}
