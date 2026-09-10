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

		RebuildDerived( chunk, def, cellSize, 0, chunk.Resolution - 1, 0, chunk.Resolution - 1 );
	}

	public static void RebuildDerived(
		TreasureChunk chunk,
		TreasureSurfaceDefinition def,
		float cellSize,
		int minX,
		int maxX,
		int minZ,
		int maxZ )
	{
		if ( chunk == null || chunk.SmoothedHeight == null || def == null )
			return;

		int res = chunk.Resolution;
		if ( res <= 0 )
			return;

		ClampRect( res, ref minX, ref maxX, ref minZ, ref maxZ );

		// Pad by 1 so finite differences at the dirty edge stay correct.
		int padMinX = minX > 0 ? minX - 1 : 0;
		int padMaxX = maxX < res - 1 ? maxX + 1 : res - 1;
		int padMinZ = minZ > 0 ? minZ - 1 : 0;
		int padMaxZ = maxZ < res - 1 ? maxZ + 1 : res - 1;

		float invCell = 1f / Mathf.Max( 0.0001f, cellSize );
		float halfInvCell = invCell * 0.5f;
		float stableThresh = def.stableSlopeThreshold;
		float invStable = 1f / Mathf.Max( 0.0001f, stableThresh );

		float[] height = chunk.SmoothedHeight;
		float[] slopeArr = chunk.Slope;
		float[] normalX = chunk.NormalX;
		float[] normalZ = chunk.NormalZ;
		float[] flowX = chunk.FlowX;
		float[] flowZ = chunk.FlowZ;
		byte[] material = chunk.Material;
		byte[] paintMaterial = chunk.PaintMaterial;
		byte[] paintTraversable = chunk.PaintTraversable;
		byte[] flagsArr = chunk.Flags;

		for ( int z = padMinZ; z <= padMaxZ; z++ )
		{
			int row = z * res;
			int rowN = z > 0 ? ( z - 1 ) * res : row;
			int rowS = z < res - 1 ? ( z + 1 ) * res : row;
			for ( int x = padMinX; x <= padMaxX; x++ )
			{
				int i = row + x;
				int iL = x > 0 ? i - 1 : i;
				int iR = x < res - 1 ? i + 1 : i;

				float hL = height[ iL ];
				float hR = height[ iR ];
				float hD = height[ rowN + x ];
				float hU = height[ rowS + x ];

				float gx = ( hR - hL ) * halfInvCell;
				float gz = ( hU - hD ) * halfInvCell;
				float slope = Mathf.Sqrt( gx * gx + gz * gz );

				slopeArr[ i ] = slope;
				normalX[ i ] = -gx;
				normalZ[ i ] = -gz;

				if ( slope > 0.0001f )
				{
					float strength = slope * invStable;
					if ( strength > 1f )
						strength = 1f;
					float invMag = 1f / slope;
					flowX[ i ] = -gx * invMag * strength;
					flowZ[ i ] = -gz * invMag * strength;
				}
				else
				{
					flowX[ i ] = 0f;
					flowZ[ i ] = 0f;
				}

				material[ i ] = paintMaterial[ i ];

				byte flags = 0;
				if ( paintTraversable[ i ] != 0 )
					flags |= ( byte )TreasureCellFlags.Traversable;
				if ( slope <= stableThresh )
					flags |= ( byte )TreasureCellFlags.Stable;
				flagsArr[ i ] = flags;
			}
		}

		chunk.Version++;
		chunk.ClearDirty();
	}

	static void ClampRect( int res, ref int minX, ref int maxX, ref int minZ, ref int maxZ )
	{
		if ( minX < 0 )
			minX = 0;
		if ( minZ < 0 )
			minZ = 0;
		if ( maxX >= res )
			maxX = res - 1;
		if ( maxZ >= res )
			maxZ = res - 1;
		if ( minX > maxX )
		{
			minX = 0;
			maxX = res - 1;
		}

		if ( minZ > maxZ )
		{
			minZ = 0;
			maxZ = res - 1;
		}
	}
}
