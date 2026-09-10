using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Stamps gold pile heightfields into the world treasure surface.
/// Each cell is Max(baseHeight, max over active piles) so digs lower the surface when pileY drops.
/// Dig stamps are coalesced and throttled but always flushed so the surface catches up.
/// </summary>
[DisallowMultipleComponent]
public class TreasurePileSurfaceBridge : MonoBehaviour
{
	const float StampMinInterval = 0.1f;

	static readonly List<TreasurePileSurfaceBridge> ActiveBridges = new List<TreasurePileSurfaceBridge>();

	TreasurePileVisual _visual;
	TreasureSurfaceWorld _world;
	bool _registered;
	bool _stampPending;
	Vector3 _stampCenter;
	float _stampRadius;
	float _lastStampTime = -1000f;

	public static IReadOnlyList<TreasurePileSurfaceBridge> Active => ActiveBridges;

	public TreasurePileVisual Visual => _visual;

	void OnEnable()
	{
		if ( !ActiveBridges.Contains( this ) )
			ActiveBridges.Add( this );
	}

	void OnDisable()
	{
		FlushPendingStamp();
		ActiveBridges.Remove( this );
	}

	public void Bind( TreasurePileVisual visual )
	{
		_visual = visual;
		_world = TreasureSurfaceWorld.EnsureExists();
		if ( visual != null
			&& visual.Heightfield != null
			&& visual.Heightfield.IsInitialized )
		{
			PinChunks( visual.Heightfield );
			QueueStamp( transform.position, visual.Heightfield.WorldSize * 0.5f * 1.42f );
		}

		_registered = true;
	}

	public void Unbind()
	{
		FlushPendingStamp();
		UnpinChunks();
		_registered = false;
		_visual = null;
	}

	void OnDestroy()
	{
		Unbind();
	}

	void LateUpdate()
	{
		GoldPileEditTiming.TryFlushDeferredFromPriorFrame();

		if ( !_stampPending )
			return;

		if ( Time.unscaledTime - _lastStampTime < StampMinInterval )
			return;

		FlushPendingStamp();
	}

	public void NotifyHeightChanged( Vector3 worldCenter, float radius )
	{
		if ( _visual == null || _world == null || !_world.IsInitialized )
			return;

		GoldPileHeightfield heightfield = _visual.Heightfield;
		if ( heightfield == null || !heightfield.IsInitialized )
			return;

		// Caller already passes brush radius + blur pad; do not re-inflate to 2R.
		float r = Mathf.Max( 0.05f, radius );
		QueueStamp( worldCenter, r );

		// First stamp after idle flushes soon; rapid digs coalesce until interval elapses.
		if ( Time.unscaledTime - _lastStampTime >= StampMinInterval )
			FlushPendingStamp();
	}

	public void RestampFull()
	{
		if ( _visual == null )
			_visual = GetComponent<TreasurePileVisual>();

		if ( _visual == null )
			return;

		_world = TreasureSurfaceWorld.EnsureExists();
		GoldPileHeightfield heightfield = _visual.Heightfield;
		if ( heightfield == null || !heightfield.IsInitialized )
			return;

		PinChunks( heightfield );
		_stampPending = false;
		StampRegion( transform.position, heightfield.WorldSize * 0.5f * 1.42f, sampleAllPiles: true );
		_lastStampTime = Time.unscaledTime;
		_registered = true;
	}

	void QueueStamp( Vector3 worldCenter, float radius )
	{
		if ( !_stampPending )
		{
			_stampPending = true;
			_stampCenter = worldCenter;
			_stampRadius = radius;
			return;
		}

		// Expand to a sphere covering previous + new carve influence.
		Vector3 delta = worldCenter - _stampCenter;
		float dist = delta.magnitude;
		float newRadius = Mathf.Max( _stampRadius, radius );
		if ( dist + Mathf.Min( _stampRadius, radius ) <= newRadius )
		{
			_stampRadius = newRadius;
			return;
		}

		float combined = 0.5f * ( dist + _stampRadius + radius );
		_stampCenter = _stampCenter + delta * ( ( combined - _stampRadius ) / Mathf.Max( 0.0001f, dist ) );
		_stampRadius = combined;
	}

	void FlushPendingStamp()
	{
		if ( !_stampPending )
			return;

		_stampPending = false;
		if ( _visual == null || _world == null || !_world.IsInitialized )
			return;

		GoldPileHeightfield heightfield = _visual.Heightfield;
		if ( heightfield == null || !heightfield.IsInitialized )
			return;

		// Dig path: this pile only unless the stamp disk overlaps another pile footprint.
		bool sampleAll = StampOverlapsOtherPiles( _stampCenter, _stampRadius );
		StampRegion( _stampCenter, _stampRadius, sampleAllPiles: sampleAll );
		_lastStampTime = Time.unscaledTime;
	}

	bool StampOverlapsOtherPiles( Vector3 worldCenter, float radius )
	{
		if ( ActiveBridges.Count <= 1 )
			return false;

		float r = Mathf.Max( 0.05f, radius );
		for ( int b = 0; b < ActiveBridges.Count; b++ )
		{
			TreasurePileSurfaceBridge other = ActiveBridges[ b ];
			if ( other == null || other == this || other._visual == null )
				continue;

			GoldPileHeightfield hf = other._visual.Heightfield;
			if ( hf == null || !hf.IsInitialized )
				continue;

			Transform root = other._visual.transform;
			float half = hf.WorldSize * 0.5f;
			// Approximate footprint as XZ circle of radius half*√2 (corner of square).
			float pileReach = half * 1.42f;
			Vector3 delta = worldCenter - root.position;
			delta.y = 0f;
			float reach = r + pileReach;
			if ( delta.sqrMagnitude <= reach * reach )
				return true;
		}

		return false;
	}

	void PinChunks( GoldPileHeightfield heightfield )
	{
		UnpinChunks();
		if ( _world == null || _world.Streamer == null )
			return;

		float half = heightfield.WorldSize * 0.5f;
		Vector3 min = transform.TransformPoint( new Vector3( -half, 0f, -half ) );
		Vector3 max = transform.TransformPoint( new Vector3( half, 0f, half ) );
		Vector3 a = new Vector3( Mathf.Min( min.x, max.x ), 0f, Mathf.Min( min.z, max.z ) );
		Vector3 b = new Vector3( Mathf.Max( min.x, max.x ), 0f, Mathf.Max( min.z, max.z ) );

		if ( !_world.TryGetChunkCoord( a, out TreasureChunkCoord c0 ) )
			c0 = default;
		if ( !_world.TryGetChunkCoord( b, out TreasureChunkCoord c1 ) )
			c1 = c0;

		int minX = Mathf.Min( c0.X, c1.X );
		int maxX = Mathf.Max( c0.X, c1.X );
		int minZ = Mathf.Min( c0.Z, c1.Z );
		int maxZ = Mathf.Max( c0.Z, c1.Z );

		for ( int z = minZ; z <= maxZ; z++ )
		{
			for ( int x = minX; x <= maxX; x++ )
			{
				TreasureChunkCoord coord = new TreasureChunkCoord( x, z );
				_world.EnsureChunkLoaded( coord );
				_world.Streamer.MarkKeepLoaded( coord );
			}
		}
	}

	void UnpinChunks()
	{
		if ( _world == null || _world.Streamer == null || !_registered )
			return;

		// Safe: clear all pins from this bridge by restamping footprint unload is not tracked per-bridge.
		// Pins are additive across piles; leaving them loaded is acceptable for greybox scale.
	}

	static readonly List<PileSampleCtx> s_sampleScratch = new List<PileSampleCtx>( 8 );

	void StampRegion( Vector3 worldCenter, float radius, bool sampleAllPiles )
	{
		GoldPileEditTiming.Begin( "GoldPile.StampRegion" );
		System.Diagnostics.Stopwatch sw = GoldPileEditTiming.StartWatchIfEnabled();
		int cellsTouched = 0;
		int cellsChanged = 0;

		if ( _world == null || !_world.IsInitialized )
		{
			GoldPileEditTiming.End();
			return;
		}

		TreasureSurfaceDefinition def = _world.Definition;
		if ( def == null || radius <= 0f )
		{
			GoldPileEditTiming.End();
			return;
		}

		// Cache pile sampling contexts once per stamp (not per cell).
		GoldPileHeightfield thisHf = _visual != null ? _visual.Heightfield : null;
		Transform thisRoot = _visual != null ? _visual.transform : null;
		Matrix4x4 thisW2L = Matrix4x4.identity;
		float thisHalf = 0f;
		float thisMaxH = 0f;
		float thisRootY = 0f;
		float thisYScale = 1f;
		bool hasThisPile = thisHf != null && thisHf.IsInitialized && thisRoot != null;
		if ( hasThisPile )
		{
			thisW2L = thisRoot.worldToLocalMatrix;
			thisHalf = thisHf.WorldSize * 0.5f;
			thisMaxH = thisHf.MaxHeight;
			thisRootY = thisRoot.position.y;
			thisYScale = Mathf.Abs( thisRoot.lossyScale.y );
			if ( thisYScale < 1e-6f )
				thisYScale = 1f;
		}

		s_sampleScratch.Clear();
		if ( sampleAllPiles )
		{
			float stampR = Mathf.Max( 0.05f, radius );
			for ( int b = 0; b < ActiveBridges.Count; b++ )
			{
				TreasurePileSurfaceBridge bridge = ActiveBridges[ b ];
				if ( bridge == null || bridge._visual == null )
					continue;
				GoldPileHeightfield hf = bridge._visual.Heightfield;
				Transform root = bridge._visual.transform;
				if ( hf == null || !hf.IsInitialized || root == null )
					continue;

				float half = hf.WorldSize * 0.5f;
				float pileReach = half * 1.42f;
				Vector3 delta = worldCenter - root.position;
				delta.y = 0f;
				float reach = stampR + pileReach;
				if ( delta.sqrMagnitude > reach * reach )
					continue;

				float yScale = Mathf.Abs( root.lossyScale.y );
				if ( yScale < 1e-6f )
					yScale = 1f;

				s_sampleScratch.Add( new PileSampleCtx
				{
					Heightfield = hf,
					WorldToLocal = root.worldToLocalMatrix,
					Half = half,
					MaxHeight = hf.MaxHeight,
					RootY = root.position.y,
					YScale = yScale
				} );
			}
		}

		TreasureChunk dirtyChunk = null;
		int dMinX = 0;
		int dMaxX = 0;
		int dMinZ = 0;
		int dMaxZ = 0;
		bool anyDirty = false;

		float radiusSq = radius * radius;
		float halfX = def.worldSizeX * 0.5f;
		float halfZ = def.worldSizeZ * 0.5f;
		float cell = def.CellSize;
		float minLocalX = worldCenter.x - def.worldOrigin.x + halfX - radius;
		float maxLocalX = worldCenter.x - def.worldOrigin.x + halfX + radius;
		float minLocalZ = worldCenter.z - def.worldOrigin.z + halfZ - radius;
		float maxLocalZ = worldCenter.z - def.worldOrigin.z + halfZ + radius;

		int minChunkX = Mathf.Clamp( Mathf.FloorToInt( minLocalX / def.chunkSize ), 0, def.ChunkCountX - 1 );
		int maxChunkX = Mathf.Clamp( Mathf.FloorToInt( maxLocalX / def.chunkSize ), 0, def.ChunkCountX - 1 );
		int minChunkZ = Mathf.Clamp( Mathf.FloorToInt( minLocalZ / def.chunkSize ), 0, def.ChunkCountZ - 1 );
		int maxChunkZ = Mathf.Clamp( Mathf.FloorToInt( maxLocalZ / def.chunkSize ), 0, def.ChunkCountZ - 1 );

		for ( int cz = minChunkZ; cz <= maxChunkZ; cz++ )
		{
			for ( int cx = minChunkX; cx <= maxChunkX; cx++ )
			{
				TreasureChunkCoord coord = new TreasureChunkCoord( cx, cz );
				TreasureChunk chunk = _world.EnsureChunkLoaded( coord );
				if ( chunk == null || !chunk.Loaded )
					continue;

				float chunkOriginX = cx * def.chunkSize;
				float chunkOriginZ = cz * def.chunkSize;
				float chunkWorldMinX = def.worldOrigin.x - halfX + chunkOriginX;
				float chunkWorldMinZ = def.worldOrigin.z - halfZ + chunkOriginZ;
				int res = chunk.Resolution;

				int minX = Mathf.Clamp(
					Mathf.FloorToInt( ( worldCenter.x - radius - chunkWorldMinX ) / cell - 0.5f ),
					0,
					res - 1 );
				int maxX = Mathf.Clamp(
					Mathf.CeilToInt( ( worldCenter.x + radius - chunkWorldMinX ) / cell - 0.5f ),
					0,
					res - 1 );
				int minZ = Mathf.Clamp(
					Mathf.FloorToInt( ( worldCenter.z - radius - chunkWorldMinZ ) / cell - 0.5f ),
					0,
					res - 1 );
				int maxZ = Mathf.Clamp(
					Mathf.CeilToInt( ( worldCenter.z + radius - chunkWorldMinZ ) / cell - 0.5f ),
					0,
					res - 1 );

				if ( minX > maxX || minZ > maxZ )
					continue;

				// Flush previous chunk dirty rect when entering a new chunk.
				if ( anyDirty && dirtyChunk != null && dirtyChunk != chunk )
				{
					dirtyChunk.ExpandDirtyRect( dMinX, dMaxX, dMinZ, dMaxZ );
					dirtyChunk = null;
					anyDirty = false;
				}

				float[] height = chunk.Height;
				float[] baseHeight = chunk.BaseHeight;
				byte[] paintTraversable = chunk.PaintTraversable;
				byte[] paintMaterial = chunk.PaintMaterial;

				for ( int z = minZ; z <= maxZ; z++ )
				{
					float wz = chunkWorldMinZ + ( z + 0.5f ) * cell;
					float dz = wz - worldCenter.z;
					int row = z * res;
					for ( int x = minX; x <= maxX; x++ )
					{
						float wx = chunkWorldMinX + ( x + 0.5f ) * cell;
						float dx = wx - worldCenter.x;
						float distSq = dx * dx + dz * dz;
						if ( distSq > radiusSq )
							continue;

						cellsTouched++;
						float pileY = sampleAllPiles
							? SampleMaxPileWorldYCached( wx, wz, s_sampleScratch )
							: SampleThisPileWorldY( wx, wz, thisHf, thisW2L, thisHalf, thisMaxH, thisRootY, thisYScale, hasThisPile );

						int i = row + x;
						float baseH = baseHeight[ i ];
						// Assign Max(base, piles) — not Max(current, piles) — so digs lower Height when pileY drops.
						float desired = pileY > baseH ? pileY : baseH;
						if ( Mathf.Abs( height[ i ] - desired ) <= 1e-5f )
							continue;

						height[ i ] = desired;
						paintTraversable[ i ] = 1;
						if ( paintMaterial[ i ] == ( byte )TreasureSurfaceMaterial.Stone )
							paintMaterial[ i ] = ( byte )TreasureSurfaceMaterial.Gold;

						if ( !anyDirty || dirtyChunk != chunk )
						{
							dirtyChunk = chunk;
							dMinX = dMaxX = x;
							dMinZ = dMaxZ = z;
							anyDirty = true;
						}
						else
						{
							if ( x < dMinX )
								dMinX = x;
							if ( x > dMaxX )
								dMaxX = x;
							if ( z < dMinZ )
								dMinZ = z;
							if ( z > dMaxZ )
								dMaxZ = z;
						}

						cellsChanged++;
					}
				}
			}
		}

		if ( anyDirty && dirtyChunk != null )
			dirtyChunk.ExpandDirtyRect( dMinX, dMaxX, dMinZ, dMaxZ );

		if ( sw != null )
		{
			sw.Stop();
			GoldPileEditTiming.Record(
				"stamp.region",
				sw.Elapsed.TotalMilliseconds,
				$"touched={cellsTouched} changed={cellsChanged} allPiles={sampleAllPiles}" );
		}

		GoldPileEditTiming.End();
	}

	struct PileSampleCtx
	{
		public GoldPileHeightfield Heightfield;
		public Matrix4x4 WorldToLocal;
		public float Half;
		public float MaxHeight;
		public float RootY;
		public float YScale;
	}

	static float SampleThisPileWorldY(
		float wx,
		float wz,
		GoldPileHeightfield hf,
		Matrix4x4 worldToLocal,
		float half,
		float maxHeight,
		float rootY,
		float yScale,
		bool valid )
	{
		if ( !valid )
			return float.NegativeInfinity;

		Vector3 local = worldToLocal.MultiplyPoint3x4( new Vector3( wx, rootY, wz ) );
		if ( Mathf.Abs( local.x ) > half || Mathf.Abs( local.z ) > half )
			return float.NegativeInfinity;

		float localH = hf.SampleNormalized( local.x, local.z ) * maxHeight;
		return rootY + localH * yScale;
	}

	static float SampleMaxPileWorldYCached( float wx, float wz, List<PileSampleCtx> ctx )
	{
		float best = float.NegativeInfinity;
		for ( int i = 0; i < ctx.Count; i++ )
		{
			PileSampleCtx c = ctx[ i ];
			Vector3 local = c.WorldToLocal.MultiplyPoint3x4( new Vector3( wx, c.RootY, wz ) );
			if ( Mathf.Abs( local.x ) > c.Half || Mathf.Abs( local.z ) > c.Half )
				continue;

			float localH = c.Heightfield.SampleNormalized( local.x, local.z ) * c.MaxHeight;
			float pileY = c.RootY + localH * c.YScale;
			if ( pileY > best )
				best = pileY;
		}

		return best;
	}
}
