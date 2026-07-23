using UnityEngine;

/// <summary>
/// Height relaxation: redistributes excess height to neighbours.
/// </summary>
public static class TreasureSurfaceRelax
{
	public static void Relax( TreasureChunk chunk, int iterations )
	{
		if ( chunk == null || chunk.Height == null || iterations <= 0 )
		{
			CopyHeightToSmoothed( chunk );
			return;
		}

		int res = chunk.Resolution;
		float[] a = chunk.Height;
		float[] b = chunk.SmoothedHeight;
		System.Array.Copy( a, b, a.Length );

		for ( int iter = 0; iter < iterations; iter++ )
		{
			for ( int z = 0; z < res; z++ )
			{
				for ( int x = 0; x < res; x++ )
				{
					int i = chunk.Index( x, z );
					float h = a[ i ];
					float sum = h;
					int count = 1;

					if ( x > 0 )
					{
						sum += a[ chunk.Index( x - 1, z ) ];
						count++;
					}

					if ( x < res - 1 )
					{
						sum += a[ chunk.Index( x + 1, z ) ];
						count++;
					}

					if ( z > 0 )
					{
						sum += a[ chunk.Index( x, z - 1 ) ];
						count++;
					}

					if ( z < res - 1 )
					{
						sum += a[ chunk.Index( x, z + 1 ) ];
						count++;
					}

					float avg = sum / count;
					float next = Mathf.Lerp( h, avg, 0.35f );
					float floor = chunk.BaseHeight[ i ];
					b[ i ] = next < floor ? floor : next;
				}
			}

			float[] swap = a;
			a = b;
			b = swap;
		}

		// Ensure both Height and SmoothedHeight hold the relaxed result.
		if ( a == chunk.Height )
		{
			System.Array.Copy( chunk.Height, chunk.SmoothedHeight, chunk.Height.Length );
		}
		else
		{
			System.Array.Copy( chunk.SmoothedHeight, chunk.Height, chunk.Height.Length );
		}
	}

	public static void CopyHeightToSmoothed( TreasureChunk chunk )
	{
		if ( chunk == null || chunk.Height == null )
			return;

		System.Array.Copy( chunk.Height, chunk.SmoothedHeight, chunk.Height.Length );
	}
}
