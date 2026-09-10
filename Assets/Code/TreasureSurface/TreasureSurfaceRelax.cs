using UnityEngine;

/// <summary>
/// Height relaxation: redistributes excess height to neighbours.
/// </summary>
public static class TreasureSurfaceRelax
{
	public static void Relax( TreasureChunk chunk, int iterations )
	{
		if ( chunk == null || chunk.Height == null )
			return;

		Relax( chunk, iterations, 0, chunk.Resolution - 1, 0, chunk.Resolution - 1 );
	}

	public static void Relax(
		TreasureChunk chunk,
		int iterations,
		int minX,
		int maxX,
		int minZ,
		int maxZ )
	{
		if ( chunk == null || chunk.Height == null || iterations <= 0 )
		{
			CopyHeightToSmoothed( chunk, minX, maxX, minZ, maxZ );
			return;
		}

		int res = chunk.Resolution;
		if ( res <= 0 )
			return;

		ClampRect( res, ref minX, ref maxX, ref minZ, ref maxZ );
		int pad = iterations;
		int padMinX = minX - pad;
		int padMaxX = maxX + pad;
		int padMinZ = minZ - pad;
		int padMaxZ = maxZ + pad;
		ClampRect( res, ref padMinX, ref padMaxX, ref padMinZ, ref padMaxZ );

		float[] a = chunk.Height;
		float[] b = chunk.SmoothedHeight;
		float[] baseHeight = chunk.BaseHeight;
		CopyRect( a, b, res, padMinX, padMaxX, padMinZ, padMaxZ );

		for ( int iter = 0; iter < iterations; iter++ )
		{
			for ( int z = padMinZ; z <= padMaxZ; z++ )
			{
				int row = z * res;
				int rowN = z > 0 ? ( z - 1 ) * res : row;
				int rowS = z < res - 1 ? ( z + 1 ) * res : row;
				for ( int x = padMinX; x <= padMaxX; x++ )
				{
					int i = row + x;
					float h = a[ i ];
					float sum = h;
					int count = 1;

					if ( x > 0 )
					{
						sum += a[ i - 1 ];
						count++;
					}

					if ( x < res - 1 )
					{
						sum += a[ i + 1 ];
						count++;
					}

					if ( z > 0 )
					{
						sum += a[ rowN + x ];
						count++;
					}

					if ( z < res - 1 )
					{
						sum += a[ rowS + x ];
						count++;
					}

					float avg = sum / count;
					float next = Mathf.Lerp( h, avg, 0.35f );
					float floor = baseHeight[ i ];
					b[ i ] = next < floor ? floor : next;
				}
			}

			float[] swap = a;
			a = b;
			b = swap;
		}

		// Ensure both Height and SmoothedHeight hold the relaxed result in the padded rect.
		if ( a == chunk.Height )
			CopyRect( chunk.Height, chunk.SmoothedHeight, res, padMinX, padMaxX, padMinZ, padMaxZ );
		else
			CopyRect( chunk.SmoothedHeight, chunk.Height, res, padMinX, padMaxX, padMinZ, padMaxZ );
	}

	public static void CopyHeightToSmoothed( TreasureChunk chunk )
	{
		if ( chunk == null || chunk.Height == null )
			return;

		CopyHeightToSmoothed( chunk, 0, chunk.Resolution - 1, 0, chunk.Resolution - 1 );
	}

	public static void CopyHeightToSmoothed(
		TreasureChunk chunk,
		int minX,
		int maxX,
		int minZ,
		int maxZ )
	{
		if ( chunk == null || chunk.Height == null )
			return;

		int res = chunk.Resolution;
		if ( res <= 0 )
			return;

		ClampRect( res, ref minX, ref maxX, ref minZ, ref maxZ );
		if ( minX == 0 && maxX == res - 1 && minZ == 0 && maxZ == res - 1 )
		{
			System.Array.Copy( chunk.Height, chunk.SmoothedHeight, chunk.Height.Length );
			return;
		}

		CopyRect( chunk.Height, chunk.SmoothedHeight, res, minX, maxX, minZ, maxZ );
	}

	static void CopyRect( float[] src, float[] dst, int res, int minX, int maxX, int minZ, int maxZ )
	{
		int width = maxX - minX + 1;
		for ( int z = minZ; z <= maxZ; z++ )
		{
			int row = z * res + minX;
			System.Array.Copy( src, row, dst, row, width );
		}
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
