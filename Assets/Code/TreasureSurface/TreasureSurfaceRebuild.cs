using UnityEngine;

/// <summary>
/// Rebuilds derived maps from height: slope, normal, flow, stability, flags.
/// </summary>
public static class TreasureSurfaceRebuild
{
	public static void RebuildDerived( TreasureChunk chunk, TreasureSurfaceDefinition def, float cellSize )
	{
		if ( chunk == null || chunk.SmoothedHeight == null || def == null )
			return;

		int res = chunk.Resolution;
		float invCell = 1f / Mathf.Max( 0.0001f, cellSize );
		float stableThresh = def.stableSlopeThreshold;

		for ( int z = 0; z < res; z++ )
		{
			for ( int x = 0; x < res; x++ )
			{
				int i = chunk.Index( x, z );
				int xL = Mathf.Max( 0, x - 1 );
				int xR = Mathf.Min( res - 1, x + 1 );
				int zD = Mathf.Max( 0, z - 1 );
				int zU = Mathf.Min( res - 1, z + 1 );

				float hL = chunk.SmoothedHeight[ chunk.Index( xL, z ) ];
				float hR = chunk.SmoothedHeight[ chunk.Index( xR, z ) ];
				float hD = chunk.SmoothedHeight[ chunk.Index( x, zD ) ];
				float hU = chunk.SmoothedHeight[ chunk.Index( x, zU ) ];

				float gx = ( hR - hL ) * invCell * 0.5f;
				float gz = ( hU - hD ) * invCell * 0.5f;
				float slope = Mathf.Sqrt( gx * gx + gz * gz );

				chunk.Slope[ i ] = slope;
				chunk.NormalX[ i ] = -gx;
				chunk.NormalZ[ i ] = -gz;

				float flowMag = slope;
				if ( flowMag > 0.0001f )
				{
					// Full downhill unit vector; magnitude encodes how steep (not clamped away on mid slopes).
					float strength = Mathf.Clamp01( slope / Mathf.Max( 0.0001f, def.stableSlopeThreshold ) );
					strength = Mathf.Min( 1f, strength );
					chunk.FlowX[ i ] = -gx / flowMag * strength;
					chunk.FlowZ[ i ] = -gz / flowMag * strength;
				}
				else
				{
					chunk.FlowX[ i ] = 0f;
					chunk.FlowZ[ i ] = 0f;
				}

				chunk.Material[ i ] = chunk.PaintMaterial[ i ];

				byte flags = 0;
				if ( chunk.PaintTraversable[ i ] != 0 )
					flags |= ( byte )TreasureCellFlags.Traversable;
				if ( slope <= stableThresh )
					flags |= ( byte )TreasureCellFlags.Stable;
				chunk.Flags[ i ] = flags;
			}
		}

		chunk.Version++;
		chunk.Dirty = false;
		chunk.MarkGpuDirtyFull();
	}
}
