using System;

using UnityEngine;

/// <summary>Scene-view sculpt modes for gold pile heightfield authoring.</summary>
public enum GoldPileEditorBrushMode
{
	None,
	Raise,
	Lower,
	Smooth,
	Flatten,
	Stamp,
	ResetMound,
	Settle,
	Flow,
	Inflate,
	Scrape,
	Pinch,
	Noise,
	Erode,
	Fill,
	Peak,
	Ridge
}

/// <summary>Stack-friendly brush parameters for gold pile editor sculpt tools.</summary>
public struct GoldPileBrushParams
{
	public float radius;
	public float strength;
	public float falloff;
	public float flattenTarget;
	public Texture2D stampMask;
	public bool stampInvert;
	public bool stampFullFootprint;
	public float angleOfReposeDegrees;
	public int iterations;
	public float noiseFrequency;
	public int noiseOctaves;
	public int noiseSeed;
	public float peakHeight;
	public bool settleAfterPeak;
	public float ridgeWidth;
	public float moveDeltaX;
	public float moveDeltaZ;
	public float profileFalloff;
	public float splineSmooth;
	public bool invert;

	public static GoldPileBrushParams Default => new GoldPileBrushParams
	{
		radius = 1.5f,
		strength = 0.08f,
		falloff = 1f,
		flattenTarget = 0.5f,
		angleOfReposeDegrees = 35f,
		iterations = 8,
		noiseFrequency = 2f,
		noiseOctaves = 3,
		noiseSeed = 0,
		peakHeight = 0.35f,
		settleAfterPeak = true,
		ridgeWidth = 1.2f,
		profileFalloff = 0.85f,
		splineSmooth = 0.5f
	};
}

/// <summary>
/// CPU heightfield for a deformable gold pile. Owns the float buffer and runtime deform texture.
/// </summary>
public sealed class GoldPileHeightfield
{
	const float BlurKernelCorner = 1f;
	const float BlurKernelEdge = 2f;
	const float BlurKernelCenter = 4f;
	const float BlurKernelSum = 16f;
	const float DirtyRectFullUploadThreshold = 0.25f;

	float[] _heights;
	float[] _blurScratch;
	float[] _delta;
	Texture2D _texture;
	ushort[] _rawPixels;
	bool _dirty;
	bool _dirtyFull;
	int _dirtyMinX;
	int _dirtyMaxX;
	int _dirtyMinZ;
	int _dirtyMaxZ;
	int _resolution;
	float _worldSize;
	float _maxHeight;
	float _groundLevel = 0.01f;
	float _initialVolume;

	public int Resolution => _resolution;
	public float WorldSize => _worldSize;
	public float MaxHeight => _maxHeight;
	public float GroundLevel => _groundLevel;
	public Texture2D Texture => _texture;
	public bool IsDirty => _dirty;
	public bool IsInitialized => _heights != null && _texture != null;

	/// <summary>
	/// Deterministic fingerprint of layout params + initial height samples (for latent bake keys).
	/// </summary>
	public int ComputeLayoutFingerprint()
	{
		unchecked
		{
			uint h = 2166136261u;
			h = ( h ^ ( uint )_resolution ) * 16777619u;
			h = ( h ^ ( uint )FloatToBits( _worldSize ) ) * 16777619u;
			h = ( h ^ ( uint )FloatToBits( _maxHeight ) ) * 16777619u;
			h = ( h ^ ( uint )FloatToBits( _groundLevel ) ) * 16777619u;
			if ( _heights == null )
				return ( int )h;

			h = ( h ^ ( uint )_heights.Length ) * 16777619u;
			int step = Mathf.Max( 1, _heights.Length / 4096 );
			for ( int i = 0; i < _heights.Length; i += step )
				h = ( h ^ ( uint )FloatToBits( _heights[ i ] ) ) * 16777619u;
			return ( int )h;
		}
	}

	static int FloatToBits( float value )
	{
		return System.BitConverter.SingleToInt32Bits( value );
	}

	public void Initialize( int res, float size, float height )
	{
		Initialize( res, size, height, groundLevel: 0.01f );
	}

	public void Initialize( int res, float size, float height, float groundLevel )
	{
		_resolution = Mathf.Max( 8, res );
		_worldSize = Mathf.Max( 0.1f, size );
		_maxHeight = Mathf.Max( 0.01f, height );
		_groundLevel = Mathf.Max( 0f, groundLevel );

		int count = _resolution * _resolution;
		_heights = new float[ count ];
		_blurScratch = new float[ count ];
		_delta = new float[ count ];
		_rawPixels = new ushort[ count ];

		if ( _texture != null )
		{
#if UNITY_EDITOR
			if ( !Application.isPlaying )
				UnityEngine.Object.DestroyImmediate( _texture );
			else
#endif
				UnityEngine.Object.Destroy( _texture );
		}

		_texture = new Texture2D( _resolution, _resolution, TextureFormat.R16, false, true )
		{
			name = "GoldPileDeform",
			wrapMode = TextureWrapMode.Clamp,
			// Bilinear for smooth normals/lighting. Mesh UVs use texel centers so vertex
			// displace still matches CPU/collider grid heights (avoids edge-UV drift).
			filterMode = FilterMode.Bilinear
		};

		MarkDirtyFull();
	}

	public void SetGroundLevel( float groundLevel )
	{
		float next = Mathf.Max( 0f, groundLevel );
		if ( Mathf.Abs( next - _groundLevel ) < 0.0001f )
			return;

		_groundLevel = next;
		if ( _heights != null )
			MarkDirtyFull();
	}

	/// <summary>True when the heightfield surface at this local XZ is at or above ground level.</summary>
	public bool ExistsAtLocal( float localX, float localZ )
	{
		if ( _heights == null )
			return false;
		return SampleNormalized( localX, localZ ) * _maxHeight >= _groundLevel;
	}

	/// <summary>True when the heightfield surface under this world point is at or above ground level.</summary>
	public bool ExistsAtWorld( Vector3 worldPos, Transform pileRoot )
	{
		if ( pileRoot == null || _heights == null )
			return false;

		Vector3 local = pileRoot.InverseTransformPoint( worldPos );
		float half = _worldSize * 0.5f;
		if ( Mathf.Abs( local.x ) > half || Mathf.Abs( local.z ) > half )
			return false;

		return ExistsAtLocal( local.x, local.z );
	}

	public void Release()
	{
		if ( _texture != null )
		{
#if UNITY_EDITOR
			if ( !Application.isPlaying )
				UnityEngine.Object.DestroyImmediate( _texture );
			else
#endif
				UnityEngine.Object.Destroy( _texture );
			_texture = null;
		}

		_heights = null;
		_blurScratch = null;
		_delta = null;
		_rawPixels = null;
		_dirty = false;
		_dirtyFull = false;
	}

	/// <summary>Copies normalized 0..1 heights from a packed U16 buffer (length must be res*res).</summary>
	public bool CopyFromNormalizedU16( ushort[] source )
	{
		if ( _heights == null || source == null || source.Length != _heights.Length )
			return false;

		for ( int i = 0; i < _heights.Length; i++ )
			_heights[ i ] = source[ i ] * ( 1f / 65535f );

		_initialVolume = SumHeights();
		MarkDirtyFull();
		return true;
	}

	/// <summary>Writes normalized heights into a packed U16 buffer (allocates if needed).</summary>
	public bool CopyToNormalizedU16( ref ushort[] destination )
	{
		if ( _heights == null )
			return false;

		int count = _heights.Length;
		if ( destination == null || destination.Length != count )
			destination = new ushort[ count ];

		for ( int i = 0; i < count; i++ )
			destination[ i ] = HeightToUShort( _heights[ i ] );

		return true;
	}

	/// <summary>Raises heights under a soft brush. amountNormalized is peak add in 0..1 height units.</summary>
	public void RaiseAtLocal( float localX, float localZ, float radius, float amountNormalized, float falloff = 1f )
	{
		ApplySignedBrush( localX, localZ, radius, Mathf.Abs( amountNormalized ), falloff, raise: true );
	}

	/// <summary>Lowers heights under a soft brush. amountNormalized is peak subtract in 0..1 height units.</summary>
	public void LowerAtLocal( float localX, float localZ, float radius, float amountNormalized, float falloff = 1f )
	{
		ApplySignedBrush( localX, localZ, radius, Mathf.Abs( amountNormalized ), falloff, raise: false );
	}

	/// <summary>Blends each cell toward its 3x3 neighborhood average under the brush.</summary>
	public void SmoothAtLocal( float localX, float localZ, float radius, float strength, float falloff = 1f )
	{
		if ( _heights == null || strength <= 0f )
			return;

		strength = Mathf.Clamp01( strength );
		falloff = Mathf.Max( 0.01f, falloff );
		if ( !TryGetBrushBounds( localX, localZ, radius, out int minX, out int maxX, out int minZ, out int maxZ,
			out float half, out float cell, out float radiusSq ) )
			return;

		int copyMinX = Mathf.Max( 0, minX - 1 );
		int copyMaxX = Mathf.Min( _resolution - 1, maxX + 1 );
		int copyMinZ = Mathf.Max( 0, minZ - 1 );
		int copyMaxZ = Mathf.Min( _resolution - 1, maxZ + 1 );
		int copyWidth = copyMaxX - copyMinX + 1;
		for ( int z = copyMinZ; z <= copyMaxZ; z++ )
		{
			int src = Index( copyMinX, z );
			Array.Copy( _heights, src, _blurScratch, src, copyWidth );
		}

		for ( int z = minZ; z <= maxZ; z++ )
		{
			for ( int x = minX; x <= maxX; x++ )
			{
				float wx = -half + x * cell;
				float wz = -half + z * cell;
				float dx = wx - localX;
				float dz = wz - localZ;
				float distSq = dx * dx + dz * dz;
				if ( distSq > radiusSq )
					continue;

				float w = BrushWeight( distSq, radiusSq, falloff ) * strength;
				float avg = 0f;
				int samples = 0;
				for ( int oz = -1; oz <= 1; oz++ )
				{
					int zz = Mathf.Clamp( z + oz, 0, _resolution - 1 );
					for ( int ox = -1; ox <= 1; ox++ )
					{
						int xx = Mathf.Clamp( x + ox, 0, _resolution - 1 );
						avg += _blurScratch[ Index( xx, zz ) ];
						samples++;
					}
				}

				avg /= Mathf.Max( 1, samples );
				int idx = Index( x, z );
				_heights[ idx ] = Mathf.Clamp01( Mathf.Lerp( _blurScratch[ idx ], avg, w ) );
			}
		}

		ExpandDirtyRect( minX, maxX, minZ, maxZ );
	}

	/// <summary>Lerps heights toward targetNormalized under a soft brush.</summary>
	public void FlattenAtLocal(
		float localX,
		float localZ,
		float radius,
		float targetNormalized,
		float strength,
		float falloff = 1f )
	{
		if ( _heights == null || strength <= 0f )
			return;

		targetNormalized = Mathf.Clamp01( targetNormalized );
		strength = Mathf.Clamp01( strength );
		falloff = Mathf.Max( 0.01f, falloff );
		if ( !TryGetBrushBounds( localX, localZ, radius, out int minX, out int maxX, out int minZ, out int maxZ,
			out float half, out float cell, out float radiusSq ) )
			return;

		for ( int z = minZ; z <= maxZ; z++ )
		{
			for ( int x = minX; x <= maxX; x++ )
			{
				float wx = -half + x * cell;
				float wz = -half + z * cell;
				float dx = wx - localX;
				float dz = wz - localZ;
				float distSq = dx * dx + dz * dz;
				if ( distSq > radiusSq )
					continue;

				float w = BrushWeight( distSq, radiusSq, falloff ) * strength;
				int idx = Index( x, z );
				_heights[ idx ] = Mathf.Clamp01( Mathf.Lerp( _heights[ idx ], targetNormalized, w ) );
			}
		}

		ExpandDirtyRect( minX, maxX, minZ, maxZ );
	}

	/// <summary>
	/// Stamps a grayscale mask as a raise/lower weight.
	/// Full footprint uses pile UVs; otherwise the mask is centered on the brush.
	/// </summary>
	public void StampMaskAtLocal(
		Texture2D mask,
		float localX,
		float localZ,
		float radius,
		float amountNormalized,
		bool invert,
		bool fullFootprint,
		float falloff = 1f )
	{
		if ( _heights == null || mask == null || amountNormalized == 0f )
			return;

		if ( !mask.isReadable )
		{
			Debug.LogWarning(
				"Gold pile stamp mask '" + mask.name + "' is not Read/Write enabled. Enable it in the import settings.",
				mask );
			return;
		}

		falloff = Mathf.Max( 0.01f, falloff );
		float signedAmount = Mathf.Abs( amountNormalized ) * ( invert ? -1f : 1f );
		float half = _worldSize * 0.5f;
		float cell = _worldSize / Mathf.Max( 1, _resolution - 1 );

		int minX;
		int maxX;
		int minZ;
		int maxZ;
		float radiusSq = 0f;
		if ( fullFootprint )
		{
			minX = 0;
			maxX = _resolution - 1;
			minZ = 0;
			maxZ = _resolution - 1;
		}
		else
		{
			if ( !TryGetBrushBounds( localX, localZ, radius, out minX, out maxX, out minZ, out maxZ,
				out _, out _, out radiusSq ) )
				return;
		}

		for ( int z = minZ; z <= maxZ; z++ )
		{
			for ( int x = minX; x <= maxX; x++ )
			{
				float wx = -half + x * cell;
				float wz = -half + z * cell;
				float brushW = 1f;
				if ( !fullFootprint )
				{
					float dx = wx - localX;
					float dz = wz - localZ;
					float distSq = dx * dx + dz * dz;
					if ( distSq > radiusSq )
						continue;
					brushW = BrushWeight( distSq, radiusSq, falloff );
				}

				float u;
				float v;
				if ( fullFootprint )
				{
					u = _resolution <= 1 ? 0.5f : ( float )x / ( _resolution - 1 );
					v = _resolution <= 1 ? 0.5f : ( float )z / ( _resolution - 1 );
				}
				else
				{
					float r = Mathf.Max( 0.05f, radius );
					u = Mathf.Clamp01( ( wx - localX ) / ( r * 2f ) + 0.5f );
					v = Mathf.Clamp01( ( wz - localZ ) / ( r * 2f ) + 0.5f );
				}

				Color c = mask.GetPixelBilinear( u, v );
				float maskW = Mathf.Clamp01( c.r * 0.299f + c.g * 0.587f + c.b * 0.114f );
				float delta = signedAmount * maskW * brushW;
				if ( Mathf.Abs( delta ) < 1e-6f )
					continue;

				int idx = Index( x, z );
				_heights[ idx ] = Mathf.Clamp01( _heights[ idx ] + delta );
			}
		}

		ExpandDirtyRect( minX, maxX, minZ, maxZ );
	}

	/// <summary>
	/// Angle-of-repose settle: moves excess height to lower neighbours when slope is too steep.
	/// Volume conserved. iterations typically 4–20.
	/// </summary>
	public void SettleAtLocal( float localX, float localZ, float radius, int iterations, float angleOfReposeDegrees )
	{
		if ( _heights == null || _delta == null )
			return;

		iterations = Mathf.Clamp( iterations, 1, 20 );
		float tanRepose = Mathf.Tan( Mathf.Clamp( angleOfReposeDegrees, 1f, 89f ) * Mathf.Deg2Rad );
		if ( !TryGetBrushBounds( localX, localZ, radius, out int minX, out int maxX, out int minZ, out int maxZ,
			out float half, out float cell, out float radiusSq ) )
			return;

		int padMinX = Mathf.Max( 0, minX - 1 );
		int padMaxX = Mathf.Min( _resolution - 1, maxX + 1 );
		int padMinZ = Mathf.Max( 0, minZ - 1 );
		int padMaxZ = Mathf.Min( _resolution - 1, maxZ + 1 );

		for ( int iter = 0; iter < iterations; iter++ )
		{
			ClearDeltaRect( padMinX, padMaxX, padMinZ, padMaxZ );
			for ( int z = minZ; z <= maxZ; z++ )
			{
				for ( int x = minX; x <= maxX; x++ )
				{
					float wx = -half + x * cell;
					float wz = -half + z * cell;
					float dx = wx - localX;
					float dz = wz - localZ;
					if ( dx * dx + dz * dz > radiusSq )
						continue;

					int idx = Index( x, z );
					float h = _heights[ idx ];
					float transferBudget = 0f;
					float weightSum = 0f;

					AccumulateSettleNeighbor( x, z, h, cell, tanRepose, 1, 0, ref transferBudget, ref weightSum );
					AccumulateSettleNeighbor( x, z, h, cell, tanRepose, -1, 0, ref transferBudget, ref weightSum );
					AccumulateSettleNeighbor( x, z, h, cell, tanRepose, 0, 1, ref transferBudget, ref weightSum );
					AccumulateSettleNeighbor( x, z, h, cell, tanRepose, 0, -1, ref transferBudget, ref weightSum );

					if ( transferBudget <= 1e-8f || weightSum <= 1e-8f )
						continue;

					float remove = Mathf.Min( h, transferBudget );
					_delta[ idx ] -= remove;
					float inv = remove / weightSum;
					DistributeSettleNeighbor( x, z, h, cell, tanRepose, 1, 0, inv );
					DistributeSettleNeighbor( x, z, h, cell, tanRepose, -1, 0, inv );
					DistributeSettleNeighbor( x, z, h, cell, tanRepose, 0, 1, inv );
					DistributeSettleNeighbor( x, z, h, cell, tanRepose, 0, -1, inv );
				}
			}

			ApplyDeltaRect( padMinX, padMaxX, padMinZ, padMaxZ );
		}

		ExpandDirtyRect( padMinX, padMaxX, padMinZ, padMaxZ );
	}

	void AccumulateSettleNeighbor(
		int x,
		int z,
		float h,
		float cell,
		float tanRepose,
		int ox,
		int oz,
		ref float transferBudget,
		ref float weightSum )
	{
		int nx = x + ox;
		int nz = z + oz;
		if ( nx < 0 || nz < 0 || nx >= _resolution || nz >= _resolution )
			return;

		float nh = _heights[ Index( nx, nz ) ];
		if ( nh >= h )
			return;

		float worldDrop = ( h - nh ) * _maxHeight;
		float maxDrop = tanRepose * cell;
		float excessWorld = worldDrop - maxDrop;
		if ( excessWorld <= 0f )
			return;

		float excessNorm = excessWorld / _maxHeight;
		transferBudget += excessNorm;
		weightSum += excessNorm;
	}

	void DistributeSettleNeighbor(
		int x,
		int z,
		float h,
		float cell,
		float tanRepose,
		int ox,
		int oz,
		float inv )
	{
		int nx = x + ox;
		int nz = z + oz;
		if ( nx < 0 || nz < 0 || nx >= _resolution || nz >= _resolution )
			return;

		float nh = _heights[ Index( nx, nz ) ];
		if ( nh >= h )
			return;

		float worldDrop = ( h - nh ) * _maxHeight;
		float maxDrop = tanRepose * cell;
		float excessWorld = worldDrop - maxDrop;
		if ( excessWorld <= 0f )
			return;

		float excessNorm = excessWorld / _maxHeight;
		_delta[ Index( nx, nz ) ] += inv * excessNorm;
	}

	/// <summary>Downslope volume transport along the height gradient. Volume conserved.</summary>
	public void FlowAtLocal( float localX, float localZ, float radius, float strength, float falloff = 1f )
	{
		if ( _heights == null || _delta == null || strength <= 0f )
			return;

		falloff = Mathf.Max( 0.01f, falloff );
		strength = Mathf.Clamp01( strength );
		if ( !TryGetBrushBounds( localX, localZ, radius, out int minX, out int maxX, out int minZ, out int maxZ,
			out float half, out float cell, out float radiusSq ) )
			return;

		int padMinX = Mathf.Max( 0, minX - 1 );
		int padMaxX = Mathf.Min( _resolution - 1, maxX + 1 );
		int padMinZ = Mathf.Max( 0, minZ - 1 );
		int padMaxZ = Mathf.Min( _resolution - 1, maxZ + 1 );
		ClearDeltaRect( padMinX, padMaxX, padMinZ, padMaxZ );

		for ( int z = minZ; z <= maxZ; z++ )
		{
			for ( int x = minX; x <= maxX; x++ )
			{
				float wx = -half + x * cell;
				float wz = -half + z * cell;
				float dx = wx - localX;
				float dz = wz - localZ;
				float distSq = dx * dx + dz * dz;
				if ( distSq > radiusSq )
					continue;

				float w = BrushWeight( distSq, radiusSq, falloff );
				int idx = Index( x, z );
				float h = _heights[ idx ];
				if ( h <= 1e-6f )
					continue;

				float hL = HeightAtClamped( x - 1, z );
				float hR = HeightAtClamped( x + 1, z );
				float hD = HeightAtClamped( x, z - 1 );
				float hU = HeightAtClamped( x, z + 1 );
				float gx = hR - hL;
				float gz = hU - hD;
				float gMag = Mathf.Sqrt( gx * gx + gz * gz );
				if ( gMag < 1e-6f )
					continue;

				// Downhill direction in grid space.
				float dirX = -gx / gMag;
				float dirZ = -gz / gMag;
				int ox = dirX > 0.35f ? 1 : ( dirX < -0.35f ? -1 : 0 );
				int oz = dirZ > 0.35f ? 1 : ( dirZ < -0.35f ? -1 : 0 );
				if ( ox == 0 && oz == 0 )
				{
					if ( Mathf.Abs( dirX ) >= Mathf.Abs( dirZ ) )
						ox = dirX >= 0f ? 1 : -1;
					else
						oz = dirZ >= 0f ? 1 : -1;
				}

				int nx = x + ox;
				int nz = z + oz;
				if ( nx < 0 || nz < 0 || nx >= _resolution || nz >= _resolution )
					continue;

				float amount = Mathf.Min( h, strength * w * gMag * 0.5f );
				if ( amount <= 1e-8f )
					continue;

				_delta[ idx ] -= amount;
				_delta[ Index( nx, nz ) ] += amount;
			}
		}

		ApplyDeltaRect( padMinX, padMaxX, padMinZ, padMaxZ );
		ExpandDirtyRect( padMinX, padMaxX, padMinZ, padMaxZ );
	}

	/// <summary>Inflate along local curvature for rounded domes without spikes.</summary>
	public void InflateAtLocal( float localX, float localZ, float radius, float strength, float falloff = 1f )
	{
		if ( _heights == null || _blurScratch == null || strength == 0f )
			return;

		falloff = Mathf.Max( 0.01f, falloff );
		float signed = strength;
		if ( !TryGetBrushBounds( localX, localZ, radius, out int minX, out int maxX, out int minZ, out int maxZ,
			out float half, out float cell, out float radiusSq ) )
			return;

		CopyHeightsRect( minX, maxX, minZ, maxZ, pad: 1 );

		for ( int z = minZ; z <= maxZ; z++ )
		{
			for ( int x = minX; x <= maxX; x++ )
			{
				float wx = -half + x * cell;
				float wz = -half + z * cell;
				float dx = wx - localX;
				float dz = wz - localZ;
				float distSq = dx * dx + dz * dz;
				if ( distSq > radiusSq )
					continue;

				float w = BrushWeight( distSq, radiusSq, falloff );
				int idx = Index( x, z );
				float h = _blurScratch[ idx ];
				float avg = (
					ScratchAtClamped( x - 1, z )
					+ ScratchAtClamped( x + 1, z )
					+ ScratchAtClamped( x, z - 1 )
					+ ScratchAtClamped( x, z + 1 ) ) * 0.25f;
				float curvature = Mathf.Clamp( h - avg, -0.2f, 0.2f );
				_heights[ idx ] = Mathf.Clamp01( h + signed * w * curvature );
			}
		}

		ExpandDirtyRect( minX, maxX, minZ, maxZ );
	}

	/// <summary>Lateral volume transfer along brush movement. Volume conserved.</summary>
	public void ScrapeAtLocal(
		float localX,
		float localZ,
		float radius,
		float strength,
		float falloff,
		float moveDeltaX,
		float moveDeltaZ )
	{
		if ( _heights == null || _delta == null || strength <= 0f )
			return;

		float moveLen = Mathf.Sqrt( moveDeltaX * moveDeltaX + moveDeltaZ * moveDeltaZ );
		if ( moveLen < 1e-5f )
			return;

		falloff = Mathf.Max( 0.01f, falloff );
		strength = Mathf.Clamp01( strength );
		float expand = radius + moveLen;
		float midX = localX + moveDeltaX * 0.5f;
		float midZ = localZ + moveDeltaZ * 0.5f;
		if ( !TryGetBrushBounds( midX, midZ, expand, out int minX, out int maxX, out int minZ, out int maxZ,
			out float half, out float cell, out _ ) )
			return;

		// Also cover source brush centred on current position.
		if ( TryGetBrushBounds( localX, localZ, expand, out int sMinX, out int sMaxX, out int sMinZ, out int sMaxZ,
			out _, out _, out _ ) )
		{
			minX = Mathf.Min( minX, sMinX );
			maxX = Mathf.Max( maxX, sMaxX );
			minZ = Mathf.Min( minZ, sMinZ );
			maxZ = Mathf.Max( maxZ, sMaxZ );
		}

		ClearDeltaRect( minX, maxX, minZ, maxZ );
		float radiusSq = radius * radius;

		for ( int z = minZ; z <= maxZ; z++ )
		{
			for ( int x = minX; x <= maxX; x++ )
			{
				float wx = -half + x * cell;
				float wz = -half + z * cell;
				float dx = wx - localX;
				float dz = wz - localZ;
				float distSq = dx * dx + dz * dz;
				if ( distSq > radiusSq )
					continue;

				float w = BrushWeight( distSq, radiusSq, falloff );
				int idx = Index( x, z );
				float amount = Mathf.Min( _heights[ idx ], strength * w * 0.35f );
				if ( amount <= 1e-8f )
					continue;

				_delta[ idx ] -= amount;

				float tx = wx + moveDeltaX;
				float tz = wz + moveDeltaZ;
				DepositBilinear( tx, tz, half, cell, amount );
			}
		}

		ApplyDeltaRect( minX, maxX, minZ, maxZ );
		ExpandDirtyRect( minX, maxX, minZ, maxZ );
	}

	/// <summary>Pull height toward brush centre. Volume conserved.</summary>
	public void PinchAtLocal( float localX, float localZ, float radius, float strength )
	{
		if ( _heights == null || _delta == null || strength <= 0f )
			return;

		strength = Mathf.Clamp01( strength );
		if ( !TryGetBrushBounds( localX, localZ, radius, out int minX, out int maxX, out int minZ, out int maxZ,
			out float half, out float cell, out float radiusSq ) )
			return;

		ClearDeltaRect( minX, maxX, minZ, maxZ );

		for ( int z = minZ; z <= maxZ; z++ )
		{
			for ( int x = minX; x <= maxX; x++ )
			{
				float wx = -half + x * cell;
				float wz = -half + z * cell;
				float dx = wx - localX;
				float dz = wz - localZ;
				float distSq = dx * dx + dz * dz;
				if ( distSq > radiusSq || distSq < 1e-8f )
					continue;

				float w = BrushWeight( distSq, radiusSq, 1f );
				int idx = Index( x, z );
				float amount = Mathf.Min( _heights[ idx ], strength * w * 0.25f );
				if ( amount <= 1e-8f )
					continue;

				_delta[ idx ] -= amount;
				DepositBilinear( localX, localZ, half, cell, amount );
			}
		}

		ApplyDeltaRect( minX, maxX, minZ, maxZ );
		ExpandDirtyRect( minX, maxX, minZ, maxZ );
	}

	/// <summary>Layered FBM noise additive sculpt. Deterministic for seed + inputs.</summary>
	public void NoiseAtLocal(
		float localX,
		float localZ,
		float radius,
		float strength,
		float falloff,
		float frequency,
		int octaves,
		int seed )
	{
		if ( _heights == null || strength == 0f )
			return;

		falloff = Mathf.Max( 0.01f, falloff );
		frequency = Mathf.Max( 0.01f, frequency );
		octaves = Mathf.Clamp( octaves, 1, 8 );
		if ( !TryGetBrushBounds( localX, localZ, radius, out int minX, out int maxX, out int minZ, out int maxZ,
			out float half, out float cell, out float radiusSq ) )
			return;

		float seedX = ( seed % 997 ) * 0.173f + 11.3f;
		float seedZ = ( seed % 991 ) * 0.271f + 23.7f;

		for ( int z = minZ; z <= maxZ; z++ )
		{
			for ( int x = minX; x <= maxX; x++ )
			{
				float wx = -half + x * cell;
				float wz = -half + z * cell;
				float dx = wx - localX;
				float dz = wz - localZ;
				float distSq = dx * dx + dz * dz;
				if ( distSq > radiusSq )
					continue;

				float w = BrushWeight( distSq, radiusSq, falloff );
				float n = Fbm( wx * frequency + seedX, wz * frequency + seedZ, octaves );
				// Remap 0..1 Perlin stack to -1..1
				n = n * 2f - 1f;
				int idx = Index( x, z );
				_heights[ idx ] = Mathf.Clamp01( _heights[ idx ] + n * strength * w );
			}
		}

		ExpandDirtyRect( minX, maxX, minZ, maxZ );
	}

	/// <summary>Soft iterative downhill transfer. Strength scales transfer size.</summary>
	public void ErodeAtLocal( float localX, float localZ, float radius, float strength, int iterations )
	{
		if ( _heights == null || _delta == null || strength <= 0f )
			return;

		iterations = Mathf.Clamp( iterations, 1, 50 );
		strength = Mathf.Clamp01( strength );
		if ( !TryGetBrushBounds( localX, localZ, radius, out int minX, out int maxX, out int minZ, out int maxZ,
			out float half, out float cell, out float radiusSq ) )
			return;

		int padMinX = Mathf.Max( 0, minX - 1 );
		int padMaxX = Mathf.Min( _resolution - 1, maxX + 1 );
		int padMinZ = Mathf.Max( 0, minZ - 1 );
		int padMaxZ = Mathf.Min( _resolution - 1, maxZ + 1 );
		float transferScale = 0.02f * strength;

		for ( int iter = 0; iter < iterations; iter++ )
		{
			ClearDeltaRect( padMinX, padMaxX, padMinZ, padMaxZ );
			for ( int z = minZ; z <= maxZ; z++ )
			{
				for ( int x = minX; x <= maxX; x++ )
				{
					float wx = -half + x * cell;
					float wz = -half + z * cell;
					float dx = wx - localX;
					float dz = wz - localZ;
					if ( dx * dx + dz * dz > radiusSq )
						continue;

					int idx = Index( x, z );
					float h = _heights[ idx ];
					int bestX = x;
					int bestZ = z;
					float bestH = h;
					TryLowerNeighbor( x + 1, z, ref bestX, ref bestZ, ref bestH );
					TryLowerNeighbor( x - 1, z, ref bestX, ref bestZ, ref bestH );
					TryLowerNeighbor( x, z + 1, ref bestX, ref bestZ, ref bestH );
					TryLowerNeighbor( x, z - 1, ref bestX, ref bestZ, ref bestH );
					if ( bestX == x && bestZ == z )
						continue;

					float amount = Mathf.Min( h, ( h - bestH ) * transferScale );
					if ( amount <= 1e-8f )
						continue;

					_delta[ idx ] -= amount;
					_delta[ Index( bestX, bestZ ) ] += amount;
				}
			}

			ApplyDeltaRect( padMinX, padMaxX, padMinZ, padMaxZ );
		}

		ExpandDirtyRect( padMinX, padMaxX, padMinZ, padMaxZ );
	}

	void TryLowerNeighbor( int nx, int nz, ref int bestX, ref int bestZ, ref float bestH )
	{
		if ( nx < 0 || nz < 0 || nx >= _resolution || nz >= _resolution )
			return;

		float nh = _heights[ Index( nx, nz ) ];
		if ( nh < bestH )
		{
			bestH = nh;
			bestX = nx;
			bestZ = nz;
		}
	}

	/// <summary>Fill depressions toward neighbour average. Never lowers.</summary>
	public void FillDepressionsAtLocal( float localX, float localZ, float radius, float strength, float falloff = 1f )
	{
		if ( _heights == null || _blurScratch == null || strength <= 0f )
			return;

		strength = Mathf.Clamp01( strength );
		falloff = Mathf.Max( 0.01f, falloff );
		if ( !TryGetBrushBounds( localX, localZ, radius, out int minX, out int maxX, out int minZ, out int maxZ,
			out float half, out float cell, out float radiusSq ) )
			return;

		CopyHeightsRect( minX, maxX, minZ, maxZ, pad: 1 );

		for ( int z = minZ; z <= maxZ; z++ )
		{
			for ( int x = minX; x <= maxX; x++ )
			{
				float wx = -half + x * cell;
				float wz = -half + z * cell;
				float dx = wx - localX;
				float dz = wz - localZ;
				float distSq = dx * dx + dz * dz;
				if ( distSq > radiusSq )
					continue;

				float w = BrushWeight( distSq, radiusSq, falloff );
				int idx = Index( x, z );
				float h = _blurScratch[ idx ];
				float avg = (
					ScratchAtClamped( x - 1, z )
					+ ScratchAtClamped( x + 1, z )
					+ ScratchAtClamped( x, z - 1 )
					+ ScratchAtClamped( x, z + 1 ) ) * 0.25f;
				float delta = Mathf.Max( 0f, avg - h ) * strength * w;
				_heights[ idx ] = Mathf.Clamp01( h + delta );
			}
		}

		ExpandDirtyRect( minX, maxX, minZ, maxZ );
	}

	/// <summary>Radial bell peak stamp. Optional single Settle pass after.</summary>
	public void PeakAtLocal(
		float localX,
		float localZ,
		float radius,
		float peakHeight,
		float profileFalloff,
		bool settleAfter )
	{
		if ( _heights == null || Mathf.Abs( peakHeight ) < 1e-8f )
			return;

		profileFalloff = Mathf.Clamp( profileFalloff, 0.1f, 0.99f );
		if ( !TryGetBrushBounds( localX, localZ, radius, out int minX, out int maxX, out int minZ, out int maxZ,
			out float half, out float cell, out float radiusSq ) )
			return;

		float r = Mathf.Max( 0.05f, radius );
		for ( int z = minZ; z <= maxZ; z++ )
		{
			for ( int x = minX; x <= maxX; x++ )
			{
				float wx = -half + x * cell;
				float wz = -half + z * cell;
				float dx = wx - localX;
				float dz = wz - localZ;
				float distSq = dx * dx + dz * dz;
				if ( distSq > radiusSq )
					continue;

				float t = 1f - Mathf.Sqrt( distSq ) / r;
				t = Mathf.Clamp01( t / profileFalloff );
				t = t * t * ( 3f - 2f * t );
				int idx = Index( x, z );
				_heights[ idx ] = Mathf.Clamp01( _heights[ idx ] + peakHeight * t );
			}
		}

		ExpandDirtyRect( minX, maxX, minZ, maxZ );

		if ( settleAfter )
			SettleAtLocal( localX, localZ, radius, 4, 35f );
	}

	/// <summary>
	/// Stamp an elongated Gaussian along a segment (from→to) for ridge banks.
	/// splineSmooth 0..1 blends toward segment midpoint influence.
	/// </summary>
	public void RidgeStampSegmentAtLocal(
		float fromX,
		float fromZ,
		float toX,
		float toZ,
		float width,
		float height,
		float falloff,
		float splineSmooth )
	{
		if ( _heights == null || Mathf.Abs( height ) < 1e-8f )
			return;

		float segDx = toX - fromX;
		float segDz = toZ - fromZ;
		float segLen = Mathf.Sqrt( segDx * segDx + segDz * segDz );
		if ( segLen < 1e-5f )
		{
			PeakAtLocal( toX, toZ, width, height, falloff, settleAfter: false );
			return;
		}

		falloff = Mathf.Max( 0.01f, falloff );
		splineSmooth = Mathf.Clamp01( splineSmooth );
		width = Mathf.Max( 0.05f, width );
		float invLen = 1f / segLen;
		float dirX = segDx * invLen;
		float dirZ = segDz * invLen;
		float nx = -dirZ;
		float nz = dirX;

		float midX = ( fromX + toX ) * 0.5f;
		float midZ = ( fromZ + toZ ) * 0.5f;
		float expand = segLen * 0.5f + width * 2f;
		if ( !TryGetBrushBounds( midX, midZ, expand, out int minX, out int maxX, out int minZ, out int maxZ,
			out float half, out float cell, out _ ) )
			return;

		float halfWidth = width * 0.5f;
		float sigma = Mathf.Max( 0.01f, halfWidth / Mathf.Max( 0.5f, falloff ) );
		float twoSigmaSq = 2f * sigma * sigma;

		for ( int z = minZ; z <= maxZ; z++ )
		{
			for ( int x = minX; x <= maxX; x++ )
			{
				float wx = -half + x * cell;
				float wz = -half + z * cell;
				float vx = wx - fromX;
				float vz = wz - fromZ;
				float along = vx * dirX + vz * dirZ;
				if ( along < 0f || along > segLen )
					continue;

				float lat = vx * nx + vz * nz;
				float gauss = Mathf.Exp( -( lat * lat ) / twoSigmaSq );
				float endFade = 1f;
				float edge = Mathf.Min( along, segLen - along );
				float fadeDist = Mathf.Max( 0.01f, halfWidth );
				if ( edge < fadeDist )
					endFade = edge / fadeDist;

				float t = along * invLen;
				float smoothW = Mathf.Lerp( 1f, 1f - Mathf.Abs( t - 0.5f ) * 2f, splineSmooth );
				float add = height * gauss * endFade * smoothW;
				if ( add <= 1e-8f )
					continue;

				int idx = Index( x, z );
				_heights[ idx ] = Mathf.Clamp01( _heights[ idx ] + add );
			}
		}

		ExpandDirtyRect( minX, maxX, minZ, maxZ );
	}

	void ClearDeltaRect( int minX, int maxX, int minZ, int maxZ )
	{
		for ( int z = minZ; z <= maxZ; z++ )
		{
			int row = Index( minX, z );
			int count = maxX - minX + 1;
			for ( int i = 0; i < count; i++ )
				_delta[ row + i ] = 0f;
		}
	}

	void ApplyDeltaRect( int minX, int maxX, int minZ, int maxZ )
	{
		for ( int z = minZ; z <= maxZ; z++ )
		{
			for ( int x = minX; x <= maxX; x++ )
			{
				int idx = Index( x, z );
				float d = _delta[ idx ];
				if ( d == 0f )
					continue;
				_heights[ idx ] = Mathf.Clamp01( _heights[ idx ] + d );
			}
		}
	}

	void CopyHeightsRect( int minX, int maxX, int minZ, int maxZ, int pad )
	{
		int copyMinX = Mathf.Max( 0, minX - pad );
		int copyMaxX = Mathf.Min( _resolution - 1, maxX + pad );
		int copyMinZ = Mathf.Max( 0, minZ - pad );
		int copyMaxZ = Mathf.Min( _resolution - 1, maxZ + pad );
		int copyWidth = copyMaxX - copyMinX + 1;
		for ( int z = copyMinZ; z <= copyMaxZ; z++ )
		{
			int src = Index( copyMinX, z );
			Array.Copy( _heights, src, _blurScratch, src, copyWidth );
		}
	}

	void DepositBilinear( float localX, float localZ, float half, float cell, float amount )
	{
		float u = ( localX + half ) / cell;
		float v = ( localZ + half ) / cell;
		int x0 = Mathf.FloorToInt( u );
		int z0 = Mathf.FloorToInt( v );
		float tx = u - x0;
		float tz = v - z0;
		int x1 = x0 + 1;
		int z1 = z0 + 1;
		AddDeltaClamped( x0, z0, amount * ( 1f - tx ) * ( 1f - tz ) );
		AddDeltaClamped( x1, z0, amount * tx * ( 1f - tz ) );
		AddDeltaClamped( x0, z1, amount * ( 1f - tx ) * tz );
		AddDeltaClamped( x1, z1, amount * tx * tz );
	}

	void AddDeltaClamped( int x, int z, float amount )
	{
		if ( amount == 0f || x < 0 || z < 0 || x >= _resolution || z >= _resolution )
			return;
		_delta[ Index( x, z ) ] += amount;
	}

	float HeightAtClamped( int x, int z )
	{
		x = Mathf.Clamp( x, 0, _resolution - 1 );
		z = Mathf.Clamp( z, 0, _resolution - 1 );
		return _heights[ Index( x, z ) ];
	}

	float ScratchAtClamped( int x, int z )
	{
		x = Mathf.Clamp( x, 0, _resolution - 1 );
		z = Mathf.Clamp( z, 0, _resolution - 1 );
		return _blurScratch[ Index( x, z ) ];
	}

	static float Fbm( float x, float z, int octaves )
	{
		float sum = 0f;
		float amp = 0.5f;
		float freq = 1f;
		float norm = 0f;
		for ( int i = 0; i < octaves; i++ )
		{
			sum += Mathf.PerlinNoise( x * freq, z * freq ) * amp;
			norm += amp;
			amp *= 0.5f;
			freq *= 2f;
		}

		return norm > 1e-6f ? sum / norm : 0f;
	}

	void ApplySignedBrush(
		float localX,
		float localZ,
		float radius,
		float amountNormalized,
		float falloff,
		bool raise )
	{
		if ( _heights == null || amountNormalized <= 0f )
			return;

		falloff = Mathf.Max( 0.01f, falloff );
		if ( !TryGetBrushBounds( localX, localZ, radius, out int minX, out int maxX, out int minZ, out int maxZ,
			out float half, out float cell, out float radiusSq ) )
			return;

		for ( int z = minZ; z <= maxZ; z++ )
		{
			for ( int x = minX; x <= maxX; x++ )
			{
				float wx = -half + x * cell;
				float wz = -half + z * cell;
				float dx = wx - localX;
				float dz = wz - localZ;
				float distSq = dx * dx + dz * dz;
				if ( distSq > radiusSq )
					continue;

				float w = BrushWeight( distSq, radiusSq, falloff );
				float delta = amountNormalized * w;
				int idx = Index( x, z );
				if ( raise )
					_heights[ idx ] = Mathf.Clamp01( _heights[ idx ] + delta );
				else
					_heights[ idx ] = Mathf.Max( 0f, _heights[ idx ] - delta );
			}
		}

		ExpandDirtyRect( minX, maxX, minZ, maxZ );
	}

	bool TryGetBrushBounds(
		float localX,
		float localZ,
		float radius,
		out int minX,
		out int maxX,
		out int minZ,
		out int maxZ,
		out float half,
		out float cell,
		out float radiusSq )
	{
		minX = maxX = minZ = maxZ = 0;
		half = _worldSize * 0.5f;
		cell = _worldSize / Mathf.Max( 1, _resolution - 1 );
		radius = Mathf.Max( 0.05f, radius );
		radiusSq = radius * radius;

		minX = Mathf.Clamp( Mathf.FloorToInt( ( localX - radius + half ) / cell ), 0, _resolution - 1 );
		maxX = Mathf.Clamp( Mathf.CeilToInt( ( localX + radius + half ) / cell ), 0, _resolution - 1 );
		minZ = Mathf.Clamp( Mathf.FloorToInt( ( localZ - radius + half ) / cell ), 0, _resolution - 1 );
		maxZ = Mathf.Clamp( Mathf.CeilToInt( ( localZ + radius + half ) / cell ), 0, _resolution - 1 );
		return maxX >= minX && maxZ >= minZ;
	}

	static float BrushWeight( float distSq, float radiusSq, float falloff )
	{
		float t = 1f - Mathf.Sqrt( distSq / Mathf.Max( 1e-6f, radiusSq ) );
		t = Mathf.Clamp01( t );
		t = Mathf.Pow( t, falloff );
		return t * t * ( 3f - 2f * t );
	}

	/// <summary>Builds a rounded mound. peakNormalized is 0..1 of max height.</summary>
	public void FillMound( float peakNormalized, float edgeFalloff = 0.85f )
	{
		if ( _heights == null )
			return;

		peakNormalized = Mathf.Clamp01( peakNormalized );
		edgeFalloff = Mathf.Clamp( edgeFalloff, 0.1f, 0.99f );
		float half = ( _resolution - 1 ) * 0.5f;

		for ( int z = 0; z < _resolution; z++ )
		{
			for ( int x = 0; x < _resolution; x++ )
			{
				float nx = ( x - half ) / half;
				float nz = ( z - half ) / half;
				float dist = Mathf.Sqrt( nx * nx + nz * nz );
				float t = Mathf.Clamp01( 1f - dist / edgeFalloff );
				t = t * t * ( 3f - 2f * t );
				_heights[ Index( x, z ) ] = peakNormalized * t;
			}
		}

		_initialVolume = SumHeights();
		MarkDirtyFull();
	}

	/// <summary>Scales all heights so average volume tracks remaining/total ratio.</summary>
	public void RescaleToVolumeRatio( float ratio )
	{
		if ( _heights == null )
			return;

		ratio = Mathf.Clamp01( ratio );
		float current = SumHeights();
		if ( current < 0.0001f )
		{
			if ( ratio > 0f )
				FillMound( ratio );
			return;
		}

		float target = ratio * _resolution * _resolution * 0.35f;
		float scale = target / current;
		for ( int i = 0; i < _heights.Length; i++ )
			_heights[ i ] = Mathf.Clamp01( _heights[ i ] * scale );

		MarkDirtyFull();
	}

	public float SampleNormalized( float localX, float localZ )
	{
		if ( _heights == null )
			return 0f;

		float u = ( localX / _worldSize ) + 0.5f;
		float v = ( localZ / _worldSize ) + 0.5f;
		return SampleNormalizedUV( u, v );
	}

	public float SampleNormalizedUV( float u, float v )
	{
		if ( _heights == null )
			return 0f;

		u = Mathf.Clamp01( u );
		v = Mathf.Clamp01( v );
		float fx = u * ( _resolution - 1 );
		float fz = v * ( _resolution - 1 );
		int x0 = Mathf.FloorToInt( fx );
		int z0 = Mathf.FloorToInt( fz );
		int x1 = Mathf.Min( x0 + 1, _resolution - 1 );
		int z1 = Mathf.Min( z0 + 1, _resolution - 1 );
		float tx = fx - x0;
		float tz = fz - z0;

		float h00 = _heights[ Index( x0, z0 ) ];
		float h10 = _heights[ Index( x1, z0 ) ];
		float h01 = _heights[ Index( x0, z1 ) ];
		float h11 = _heights[ Index( x1, z1 ) ];
		float h0 = Mathf.Lerp( h00, h10, tx );
		float h1 = Mathf.Lerp( h01, h11, tx );
		return Mathf.Lerp( h0, h1, tz );
	}

	public float SampleWorldHeight( Vector3 worldPos, Transform pileRoot )
	{
		if ( pileRoot == null || _heights == null )
			return 0f;

		Vector3 local = pileRoot.InverseTransformPoint( worldPos );
		return SampleNormalized( local.x, local.z ) * _maxHeight;
	}

	public Vector3 SampleWorldNormal( Vector3 worldPos, Transform pileRoot )
	{
		if ( pileRoot == null || _heights == null )
			return Vector3.up;

		Vector3 local = pileRoot.InverseTransformPoint( worldPos );
		float step = _worldSize / Mathf.Max( 1, _resolution - 1 );
		float hL = SampleNormalized( local.x - step, local.z ) * _maxHeight;
		float hR = SampleNormalized( local.x + step, local.z ) * _maxHeight;
		float hD = SampleNormalized( local.x, local.z - step ) * _maxHeight;
		float hU = SampleNormalized( local.x, local.z + step ) * _maxHeight;
		Vector3 normal = new Vector3( hL - hR, step * 2f, hD - hU ).normalized;
		return pileRoot.TransformDirection( normal ).normalized;
	}

	/// <summary>
	/// Raycasts the heightfield surface in pile-local space so editor brushes hit where the cursor aims.
	/// </summary>
	public bool TryRaycast( Ray worldRay, Transform pileRoot, out Vector3 worldHit, out Vector3 worldNormal )
	{
		worldHit = default;
		worldNormal = Vector3.up;
		if ( pileRoot == null || _heights == null )
			return false;

		Vector3 origin = pileRoot.InverseTransformPoint( worldRay.origin );
		Vector3 direction = pileRoot.InverseTransformDirection( worldRay.direction );
		float dirLen = direction.magnitude;
		if ( dirLen < 1e-8f )
			return false;
		direction /= dirLen;

		float half = _worldSize * 0.5f;
		float minY = -0.05f * _maxHeight;
		float maxY = _maxHeight * 1.15f;

		if ( !TryIntersectLocalAabb(
			origin,
			direction,
			new Vector3( -half, minY, -half ),
			new Vector3( half, maxY, half ),
			out float tEnter,
			out float tExit ) )
			return false;

		tEnter = Mathf.Max( 0f, tEnter );
		if ( tExit < tEnter )
			return false;

		const int CoarseSteps = 96;
		float span = tExit - tEnter;
		float step = span / CoarseSteps;
		float prevT = tEnter;
		Vector3 prevP = origin + direction * prevT;
		float prevSurface = SampleNormalized( prevP.x, prevP.z ) * _maxHeight;
		float prevSign = prevP.y - prevSurface;
		bool havePrev = IsInsideFootprint( prevP.x, prevP.z, half ) && prevSurface >= _groundLevel;

		for ( int i = 1; i <= CoarseSteps; i++ )
		{
			float t = tEnter + step * i;
			Vector3 p = origin + direction * t;
			if ( !IsInsideFootprint( p.x, p.z, half ) )
			{
				havePrev = false;
				prevT = t;
				prevP = p;
				continue;
			}

			float surface = SampleNormalized( p.x, p.z ) * _maxHeight;
			if ( surface < _groundLevel )
			{
				havePrev = false;
				prevT = t;
				prevP = p;
				continue;
			}

			float sign = p.y - surface;
			if ( havePrev && prevSign > 0f && sign <= 0f )
			{
				float tHit = RefineSurfaceCrossing( origin, direction, prevT, t, half );
				Vector3 localHit = origin + direction * tHit;
				float h = SampleNormalized( localHit.x, localHit.z ) * _maxHeight;
				if ( h < _groundLevel )
				{
					havePrev = true;
					prevSign = sign;
					prevT = t;
					prevP = p;
					continue;
				}

				localHit.y = h;
				worldHit = pileRoot.TransformPoint( localHit );
				worldNormal = SampleWorldNormal( worldHit, pileRoot );
				return true;
			}

			havePrev = true;
			prevSign = sign;
			prevT = t;
			prevP = p;
		}

		return false;
	}

	float RefineSurfaceCrossing( Vector3 origin, Vector3 direction, float t0, float t1, float half )
	{
		for ( int i = 0; i < 12; i++ )
		{
			float tm = ( t0 + t1 ) * 0.5f;
			Vector3 p = origin + direction * tm;
			if ( !IsInsideFootprint( p.x, p.z, half ) )
			{
				t1 = tm;
				continue;
			}

			float surface = SampleNormalized( p.x, p.z ) * _maxHeight;
			if ( p.y > surface )
				t0 = tm;
			else
				t1 = tm;
		}

		return ( t0 + t1 ) * 0.5f;
	}

	static bool IsInsideFootprint( float localX, float localZ, float half )
	{
		return Mathf.Abs( localX ) <= half && Mathf.Abs( localZ ) <= half;
	}

	static bool TryIntersectLocalAabb(
		Vector3 origin,
		Vector3 dir,
		Vector3 min,
		Vector3 max,
		out float tEnter,
		out float tExit )
	{
		tEnter = 0f;
		tExit = float.PositiveInfinity;

		for ( int axis = 0; axis < 3; axis++ )
		{
			float o = origin[ axis ];
			float d = dir[ axis ];
			float mn = min[ axis ];
			float mx = max[ axis ];
			if ( Mathf.Abs( d ) < 1e-8f )
			{
				if ( o < mn || o > mx )
					return false;
				continue;
			}

			float inv = 1f / d;
			float t0 = ( mn - o ) * inv;
			float t1 = ( mx - o ) * inv;
			if ( t0 > t1 )
			{
				float tmp = t0;
				t0 = t1;
				t1 = tmp;
			}

			if ( t0 > tEnter )
				tEnter = t0;
			if ( t1 < tExit )
				tExit = t1;
			if ( tEnter > tExit )
				return false;
		}

		return tExit >= 0f;
	}

	public float GradientMagnitude( float localX, float localZ )
	{
		float step = _worldSize / Mathf.Max( 1, _resolution - 1 );
		float hL = SampleNormalized( localX - step, localZ );
		float hR = SampleNormalized( localX + step, localZ );
		float hD = SampleNormalized( localX, localZ - step );
		float hU = SampleNormalized( localX, localZ + step );
		float gx = hR - hL;
		float gz = hU - hD;
		return Mathf.Sqrt( gx * gx + gz * gz );
	}

	/// <summary>
	/// Removes <paramref name="volumeToRemove"/> from the sum of normalized heights,
	/// distributed with a soft Gaussian falloff so the dent blends into the mound.
	/// </summary>
	public void CarveAtLocal( float localX, float localZ, float radius, float volumeToRemove )
	{
		CarveAtLocal( localX, localZ, radius, volumeToRemove, GoldPileCarveSettings.Default );
	}

	public void CarveAtLocal(
		float localX,
		float localZ,
		float radius,
		float volumeToRemove,
		GoldPileCarveSettings settings )
	{
		if ( _heights == null || volumeToRemove <= 0f )
			return;

		GoldPileEditTiming.Begin( "GoldPile.CarveBrush" );
		System.Diagnostics.Stopwatch phaseSw = GoldPileEditTiming.StartWatchIfEnabled();

		radius = Mathf.Max( 0.05f, radius );
		float radiusSq = radius * radius;
		float half = _worldSize * 0.5f;
		float cell = _worldSize / Mathf.Max( 1, _resolution - 1 );
		float falloffSharpness = Mathf.Max( 0.1f, settings.falloffSharpness );

		int minX = Mathf.Clamp( Mathf.FloorToInt( ( localX - radius + half ) / cell ), 0, _resolution - 1 );
		int maxX = Mathf.Clamp( Mathf.CeilToInt( ( localX + radius + half ) / cell ), 0, _resolution - 1 );
		int minZ = Mathf.Clamp( Mathf.FloorToInt( ( localZ - radius + half ) / cell ), 0, _resolution - 1 );
		int maxZ = Mathf.Clamp( Mathf.CeilToInt( ( localZ + radius + half ) / cell ), 0, _resolution - 1 );
		int brushW = maxX - minX + 1;
		int brushH = maxZ - minZ + 1;
		string brushDetail = $"cells={brushW}x{brushH} res={_resolution}";

		float weightSum = 0f;
		int weightedCells = 0;
		for ( int z = minZ; z <= maxZ; z++ )
		{
			for ( int x = minX; x <= maxX; x++ )
			{
				float wx = -half + x * cell;
				float wz = -half + z * cell;
				float dx = wx - localX;
				float dz = wz - localZ;
				float distSq = dx * dx + dz * dz;
				if ( distSq > radiusSq )
					continue;

				weightSum += SoftFalloff( distSq, radiusSq, falloffSharpness );
				weightedCells++;
			}
		}

		if ( phaseSw != null )
		{
			phaseSw.Stop();
			GoldPileEditTiming.Record( "brush.weight", phaseSw.Elapsed.TotalMilliseconds, $"{brushDetail} hit={weightedCells}" );
			phaseSw.Restart();
		}

		if ( weightSum < 1e-6f )
		{
			GoldPileEditTiming.End();
			return;
		}

		float invWeight = volumeToRemove / weightSum;
		for ( int z = minZ; z <= maxZ; z++ )
		{
			for ( int x = minX; x <= maxX; x++ )
			{
				float wx = -half + x * cell;
				float wz = -half + z * cell;
				float dx = wx - localX;
				float dz = wz - localZ;
				float distSq = dx * dx + dz * dz;
				if ( distSq > radiusSq )
					continue;

				float w = SoftFalloff( distSq, radiusSq, falloffSharpness );
				int idx = Index( x, z );
				_heights[ idx ] = Mathf.Max( 0f, _heights[ idx ] - invWeight * w );
			}
		}

		if ( phaseSw != null )
		{
			phaseSw.Stop();
			GoldPileEditTiming.Record( "brush.apply", phaseSw.Elapsed.TotalMilliseconds, brushDetail );
			phaseSw.Restart();
		}

		int pad = ResolveCarveBlurPad( radius, cell, settings.blurPadCells );
		ApplyCarveBlur( minX, maxX, minZ, maxZ, pad, settings.blurPasses, settings.blurStrength );

		if ( phaseSw != null )
		{
			phaseSw.Stop();
			int blurW = Mathf.Min( _resolution, brushW + pad * 2 );
			int blurH = Mathf.Min( _resolution, brushH + pad * 2 );
			GoldPileEditTiming.Record(
				"brush.blur",
				phaseSw.Elapsed.TotalMilliseconds,
				$"pad={pad} passes={settings.blurPasses} strength={settings.blurStrength:0.##} blurCells~={blurW}x{blurH}" );
		}

		GoldPileEditTiming.End();
	}

	static float SoftFalloff( float distSq, float radiusSq, float falloffSharpness )
	{
		float t = 1f - Mathf.Sqrt( distSq / radiusSq );
		t = Mathf.Clamp01( t );
		// Smoothstep * mild Gaussian: strong center, long soft skirt.
		float smooth = t * t * ( 3f - 2f * t );
		float gaussian = Mathf.Exp( -falloffSharpness * distSq / radiusSq );
		return smooth * gaussian;
	}

	public void CarveAtWorld( Vector3 worldPos, Transform pileRoot, float radius, float amountNormalized )
	{
		CarveAtWorld( worldPos, pileRoot, radius, amountNormalized, GoldPileCarveSettings.Default );
	}

	public void CarveAtWorld(
		Vector3 worldPos,
		Transform pileRoot,
		float radius,
		float amountNormalized,
		GoldPileCarveSettings settings )
	{
		if ( pileRoot == null )
			return;

		Vector3 local = pileRoot.InverseTransformPoint( worldPos );
		CarveAtLocal( local.x, local.z, radius, amountNormalized, settings );
	}

	/// <summary>
	/// Adds volume with the same soft brush as carve (inverse of removal for pile growth).
	/// </summary>
	public void DepositAtLocal( float localX, float localZ, float radius, float volumeToAdd )
	{
		DepositAtLocal( localX, localZ, radius, volumeToAdd, GoldPileCarveSettings.Default );
	}

	public void DepositAtLocal(
		float localX,
		float localZ,
		float radius,
		float volumeToAdd,
		GoldPileCarveSettings settings )
	{
		if ( _heights == null || volumeToAdd <= 0f )
			return;

		radius = Mathf.Max( 0.05f, radius );
		float radiusSq = radius * radius;
		float half = _worldSize * 0.5f;
		float cell = _worldSize / Mathf.Max( 1, _resolution - 1 );
		float falloffSharpness = Mathf.Max( 0.1f, settings.falloffSharpness );

		int minX = Mathf.Clamp( Mathf.FloorToInt( ( localX - radius + half ) / cell ), 0, _resolution - 1 );
		int maxX = Mathf.Clamp( Mathf.CeilToInt( ( localX + radius + half ) / cell ), 0, _resolution - 1 );
		int minZ = Mathf.Clamp( Mathf.FloorToInt( ( localZ - radius + half ) / cell ), 0, _resolution - 1 );
		int maxZ = Mathf.Clamp( Mathf.CeilToInt( ( localZ + radius + half ) / cell ), 0, _resolution - 1 );

		float weightSum = 0f;
		for ( int z = minZ; z <= maxZ; z++ )
		{
			for ( int x = minX; x <= maxX; x++ )
			{
				float wx = -half + x * cell;
				float wz = -half + z * cell;
				float dx = wx - localX;
				float dz = wz - localZ;
				float distSq = dx * dx + dz * dz;
				if ( distSq > radiusSq )
					continue;

				weightSum += SoftFalloff( distSq, radiusSq, falloffSharpness );
			}
		}

		if ( weightSum < 1e-6f )
			return;

		float invWeight = volumeToAdd / weightSum;
		for ( int z = minZ; z <= maxZ; z++ )
		{
			for ( int x = minX; x <= maxX; x++ )
			{
				float wx = -half + x * cell;
				float wz = -half + z * cell;
				float dx = wx - localX;
				float dz = wz - localZ;
				float distSq = dx * dx + dz * dz;
				if ( distSq > radiusSq )
					continue;

				float w = SoftFalloff( distSq, radiusSq, falloffSharpness );
				int idx = Index( x, z );
				_heights[ idx ] = Mathf.Clamp01( _heights[ idx ] + invWeight * w );
			}
		}

		int pad = ResolveCarveBlurPad( radius, cell, settings.blurPadCells );
		ApplyCarveBlur( minX, maxX, minZ, maxZ, pad, settings.blurPasses, settings.blurStrength );
	}

	public void DepositAtWorld( Vector3 worldPos, Transform pileRoot, float radius, float amountNormalized )
	{
		DepositAtWorld( worldPos, pileRoot, radius, amountNormalized, GoldPileCarveSettings.Default );
	}

	public void DepositAtWorld(
		Vector3 worldPos,
		Transform pileRoot,
		float radius,
		float amountNormalized,
		GoldPileCarveSettings settings )
	{
		if ( pileRoot == null )
			return;

		Vector3 local = pileRoot.InverseTransformPoint( worldPos );
		DepositAtLocal( local.x, local.z, radius, amountNormalized, settings );
	}

	static int ResolveCarveBlurPad( float radius, float cell, int maxPadCells )
	{
		if ( maxPadCells <= 0 )
			return 0;

		// Cap blur pad so large carve radii do not expand work to ~brush diameter.
		return Mathf.Min( maxPadCells, Mathf.Max( 1, Mathf.CeilToInt( radius / cell ) ) );
	}

	void ApplyCarveBlur( int minX, int maxX, int minZ, int maxZ, int pad, int passes, float strength )
	{
		if ( passes <= 0 || strength <= 0f )
			return;

		for ( int i = 0; i < passes; i++ )
			BlurRegion( minX, maxX, minZ, maxZ, pad, strength );
	}

	public void Blur3x3Full()
	{
		if ( _heights == null )
			return;

		BlurRegion( 0, _resolution - 1, 0, _resolution - 1 );
	}

	public bool TryGetDirtyRect( out int minX, out int maxX, out int minZ, out int maxZ, out bool full )
	{
		minX = _dirtyMinX;
		maxX = _dirtyMaxX;
		minZ = _dirtyMinZ;
		maxZ = _dirtyMaxZ;
		full = _dirtyFull;
		return _dirty;
	}

	public void ClearDirtyRect()
	{
		_dirty = false;
		_dirtyFull = false;
		_dirtyMinX = 0;
		_dirtyMaxX = 0;
		_dirtyMinZ = 0;
		_dirtyMaxZ = 0;
	}

	void BlurRegion( int minX, int maxX, int minZ, int maxZ, int pad = 1, float strength = 1f )
	{
		if ( strength <= 0f )
			return;

		pad = Mathf.Max( 0, pad );
		minX = Mathf.Max( 0, minX - pad );
		maxX = Mathf.Min( _resolution - 1, maxX + pad );
		minZ = Mathf.Max( 0, minZ - pad );
		maxZ = Mathf.Min( _resolution - 1, maxZ + pad );

		// Copy only the padded rect (+1 for the 3x3 kernel neighborhood).
		int copyMinX = Mathf.Max( 0, minX - 1 );
		int copyMaxX = Mathf.Min( _resolution - 1, maxX + 1 );
		int copyMinZ = Mathf.Max( 0, minZ - 1 );
		int copyMaxZ = Mathf.Min( _resolution - 1, maxZ + 1 );
		int copyWidth = copyMaxX - copyMinX + 1;
		for ( int z = copyMinZ; z <= copyMaxZ; z++ )
		{
			int src = Index( copyMinX, z );
			Array.Copy( _heights, src, _blurScratch, src, copyWidth );
		}

		bool fullStrength = strength >= 0.999f;
		for ( int z = minZ; z <= maxZ; z++ )
		{
			for ( int x = minX; x <= maxX; x++ )
			{
				float sum = 0f;
				for ( int oz = -1; oz <= 1; oz++ )
				{
					int zz = Mathf.Clamp( z + oz, 0, _resolution - 1 );
					for ( int ox = -1; ox <= 1; ox++ )
					{
						int xx = Mathf.Clamp( x + ox, 0, _resolution - 1 );
						float w = ox == 0 && oz == 0
							? BlurKernelCenter
							: ( ox == 0 || oz == 0 ? BlurKernelEdge : BlurKernelCorner );
						sum += _blurScratch[ Index( xx, zz ) ] * w;
					}
				}

				int idx = Index( x, z );
				float blurred = sum / BlurKernelSum;
				_heights[ idx ] = fullStrength
					? blurred
					: Mathf.Lerp( _blurScratch[ idx ], blurred, strength );
			}
		}

		ExpandDirtyRect( minX, maxX, minZ, maxZ );
	}

	public bool UploadIfDirty()
	{
		if ( !_dirty || _texture == null || _heights == null || _rawPixels == null )
			return false;

		GoldPileEditTiming.Begin( "GoldPile.UploadDeform" );
		System.Diagnostics.Stopwatch phaseSw = GoldPileEditTiming.StartWatchIfEnabled();

		int total = _heights.Length;
		int dirtyW = _dirtyMaxX - _dirtyMinX + 1;
		int dirtyCount = dirtyW * ( _dirtyMaxZ - _dirtyMinZ + 1 );
		bool useFull = _dirtyFull || dirtyCount >= total * DirtyRectFullUploadThreshold;
		string detail = $"full={useFull} dirty={dirtyCount}/{total}";

		if ( useFull )
		{
			for ( int i = 0; i < total; i++ )
				_rawPixels[ i ] = HeightToUShort( HeightForGpu( _heights[ i ] ) );
		}
		else
		{
			for ( int z = _dirtyMinZ; z <= _dirtyMaxZ; z++ )
			{
				int row = Index( _dirtyMinX, z );
				for ( int x = 0; x < dirtyW; x++ )
					_rawPixels[ row + x ] = HeightToUShort( HeightForGpu( _heights[ row + x ] ) );
			}
		}

		if ( phaseSw != null )
		{
			phaseSw.Stop();
			GoldPileEditTiming.Record( "upload.pack", phaseSw.Elapsed.TotalMilliseconds, detail );
			phaseSw.Restart();
		}

		_texture.SetPixelData( _rawPixels, 0 );
		_texture.Apply( false, false );
		ClearDirtyRect();

		if ( phaseSw != null )
		{
			phaseSw.Stop();
			GoldPileEditTiming.Record( "upload.gpu", phaseSw.Elapsed.TotalMilliseconds, detail );
		}

		GoldPileEditTiming.End();
		return true;
	}

	public float SumHeights()
	{
		if ( _heights == null )
			return 0f;

		float sum = 0f;
		for ( int i = 0; i < _heights.Length; i++ )
			sum += _heights[ i ];
		return sum;
	}

	public float HeightPerCoin( int totalCount )
	{
		return VolumePerCoin( totalCount );
	}

	/// <summary>
	/// Live mound volume share per remaining coin: <see cref="SumHeights"/> / coinCount.
	/// </summary>
	public float VolumePerCoin( int remainingCoinCount )
	{
		remainingCoinCount = Mathf.Max( 1, remainingCoinCount );
		float volume = SumHeights();
		if ( volume < 0.0001f )
			return 0f;
		return volume / remainingCoinCount;
	}

	void MarkDirtyFull()
	{
		_dirty = true;
		_dirtyFull = true;
		_dirtyMinX = 0;
		_dirtyMaxX = _resolution - 1;
		_dirtyMinZ = 0;
		_dirtyMaxZ = _resolution - 1;
	}

	void ExpandDirtyRect( int minX, int maxX, int minZ, int maxZ )
	{
		if ( !_dirty )
		{
			_dirty = true;
			_dirtyFull = false;
			_dirtyMinX = minX;
			_dirtyMaxX = maxX;
			_dirtyMinZ = minZ;
			_dirtyMaxZ = maxZ;
			return;
		}

		if ( _dirtyFull )
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

	/// <summary>
	/// GPU deform tex treats below-ground cells as empty so the mesh does not displace there.
	/// CPU buffer keeps authored values for sculpt / volume math.
	/// </summary>
	float HeightForGpu( float normalizedHeight )
	{
		if ( normalizedHeight * _maxHeight < _groundLevel )
			return 0f;
		return normalizedHeight;
	}

	static ushort HeightToUShort( float h )
	{
		return ( ushort )Mathf.Clamp( Mathf.RoundToInt( h * 65535f ), 0, 65535 );
	}

	int Index( int x, int z )
	{
		return z * _resolution + x;
	}
}
