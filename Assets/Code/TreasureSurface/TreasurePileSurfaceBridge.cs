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
		RestampFull();
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

		float r = Mathf.Max( radius, _visual.CarveRadius * 2f );
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
		StampRegion( transform.position, heightfield.WorldSize * 0.5f * 1.42f );
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

		StampRegion( _stampCenter, _stampRadius );
		_lastStampTime = Time.unscaledTime;
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

	void StampRegion( Vector3 worldCenter, float radius )
	{
		GoldPileEditTiming.Begin( "GoldPile.StampRegion" );
		System.Diagnostics.Stopwatch sw = GoldPileEditTiming.StartWatchIfEnabled();
		int cellsTouched = 0;
		int cellsChanged = 0;

		_world.ForEachCellInRadius( worldCenter, radius, ( chunk, x, z, wx, wz, distSq, radiusSq ) =>
		{
			cellsTouched++;
			float pileY = SampleMaxPileWorldY( wx, wz );
			int i = chunk.Index( x, z );
			float baseH = chunk.BaseHeight[ i ];
			// Assign Max(base, piles) — not Max(current, piles) — so digs lower Height when pileY drops.
			float desired = pileY > baseH ? pileY : baseH;
			if ( Mathf.Abs( chunk.Height[ i ] - desired ) <= 1e-5f )
				return;

			chunk.Height[ i ] = desired;
			chunk.PaintTraversable[ i ] = 1;
			if ( chunk.PaintMaterial[ i ] == ( byte )TreasureSurfaceMaterial.Stone )
				chunk.PaintMaterial[ i ] = ( byte )TreasureSurfaceMaterial.Gold;

			chunk.ExpandDirtyRect( x, x, z, z );
			chunk.Dirty = true;
			cellsChanged++;
		} );

		if ( sw != null )
		{
			sw.Stop();
			GoldPileEditTiming.LogIfEnabled(
				$"[GoldPileEdit] stamp={sw.Elapsed.TotalMilliseconds:F2}ms touched={cellsTouched} changed={cellsChanged}",
				this );
		}

		GoldPileEditTiming.End();
	}

	/// <summary>
	/// Highest world-Y contribution from any active pile at this XZ (0 piles → -Infinity so base wins).
	/// </summary>
	static float SampleMaxPileWorldY( float wx, float wz )
	{
		float best = float.NegativeInfinity;
		for ( int b = 0; b < ActiveBridges.Count; b++ )
		{
			TreasurePileSurfaceBridge bridge = ActiveBridges[ b ];
			if ( bridge == null || bridge._visual == null )
				continue;

			GoldPileHeightfield hf = bridge._visual.Heightfield;
			if ( hf == null || !hf.IsInitialized )
				continue;

			Transform pileRoot = bridge._visual.transform;
			float half = hf.WorldSize * 0.5f;
			Vector3 local = pileRoot.InverseTransformPoint( new Vector3( wx, pileRoot.position.y, wz ) );
			if ( Mathf.Abs( local.x ) > half || Mathf.Abs( local.z ) > half )
				continue;

			float localH = hf.SampleNormalized( local.x, local.z ) * hf.MaxHeight;
			float pileY = pileRoot.TransformPoint( new Vector3( local.x, localH, local.z ) ).y;
			if ( pileY > best )
				best = pileY;
		}

		return best;
	}
}
