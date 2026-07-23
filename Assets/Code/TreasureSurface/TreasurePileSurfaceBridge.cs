using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Stamps a gold pile heightfield into the world treasure surface (max-composite).
/// </summary>
[DisallowMultipleComponent]
public class TreasurePileSurfaceBridge : MonoBehaviour
{
	static readonly List<TreasurePileSurfaceBridge> ActiveBridges = new List<TreasurePileSurfaceBridge>();

	TreasurePileVisual _visual;
	TreasureSurfaceWorld _world;
	bool _registered;

	public static IReadOnlyList<TreasurePileSurfaceBridge> Active => ActiveBridges;

	void OnEnable()
	{
		if ( !ActiveBridges.Contains( this ) )
			ActiveBridges.Add( this );
	}

	void OnDisable()
	{
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
		UnpinChunks();
		_registered = false;
		_visual = null;
	}

	void OnDestroy()
	{
		Unbind();
	}

	public void NotifyHeightChanged( Vector3 worldCenter, float radius )
	{
		if ( _visual == null || _world == null || !_world.IsInitialized )
			return;

		GoldPileHeightfield heightfield = _visual.Heightfield;
		if ( heightfield == null || !heightfield.IsInitialized )
			return;

		StampRegion( worldCenter, Mathf.Max( radius, _visual.CarveRadius * 2f ), heightfield );
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
		StampRegion( transform.position, heightfield.WorldSize * 0.5f * 1.42f, heightfield );
		_registered = true;
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

	void StampRegion( Vector3 worldCenter, float radius, GoldPileHeightfield heightfield )
	{
		Transform pileRoot = _visual.transform;
		float half = heightfield.WorldSize * 0.5f;

		_world.ForEachCellInRadius( worldCenter, radius, ( chunk, x, z, wx, wz, distSq, radiusSq ) =>
		{
			Vector3 world = new Vector3( wx, pileRoot.position.y, wz );
			Vector3 local = pileRoot.InverseTransformPoint( world );
			if ( Mathf.Abs( local.x ) > half || Mathf.Abs( local.z ) > half )
				return;

			float localH = heightfield.SampleNormalized( local.x, local.z ) * heightfield.MaxHeight;
			float pileY = pileRoot.TransformPoint( new Vector3( local.x, localH, local.z ) ).y;
			int i = chunk.Index( x, z );
			float baseH = chunk.BaseHeight[ i ];
			chunk.Height[ i ] = Mathf.Max( baseH, pileY );
			chunk.PaintTraversable[ i ] = 1;
			if ( chunk.PaintMaterial[ i ] == ( byte )TreasureSurfaceMaterial.Stone )
				chunk.PaintMaterial[ i ] = ( byte )TreasureSurfaceMaterial.Gold;

			chunk.ExpandDirtyRect( x, x, z, z );
			chunk.Dirty = true;
		} );
	}
}
