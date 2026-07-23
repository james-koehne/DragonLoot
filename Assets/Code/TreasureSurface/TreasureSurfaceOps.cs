using UnityEngine;

/// <summary>
/// Brush-based volume operations on the treasure surface.
/// </summary>
public static class TreasureSurfaceOps
{
	public static void AddVolume(
		TreasureSurfaceWorld world,
		Vector3 worldCenter,
		float radius,
		float volume )
	{
		ApplyBrush( world, worldCenter, radius, volume, remove: false );
	}

	public static void RemoveVolume(
		TreasureSurfaceWorld world,
		Vector3 worldCenter,
		float radius,
		float volume )
	{
		ApplyBrush( world, worldCenter, radius, volume, remove: true );
	}

	public static void SmoothVolume(
		TreasureSurfaceWorld world,
		Vector3 worldCenter,
		float radius )
	{
		if ( world == null || !world.IsInitialized || radius <= 0f )
			return;

		world.ForEachCellInRadius( worldCenter, radius, ( chunk, x, z, wx, wz, distSq, radiusSq ) =>
		{
			int i = chunk.Index( x, z );
			float sum = chunk.Height[ i ];
			int count = 1;
			int res = chunk.Resolution;

			if ( x > 0 )
			{
				sum += chunk.Height[ chunk.Index( x - 1, z ) ];
				count++;
			}

			if ( x < res - 1 )
			{
				sum += chunk.Height[ chunk.Index( x + 1, z ) ];
				count++;
			}

			if ( z > 0 )
			{
				sum += chunk.Height[ chunk.Index( x, z - 1 ) ];
				count++;
			}

			if ( z < res - 1 )
			{
				sum += chunk.Height[ chunk.Index( x, z + 1 ) ];
				count++;
			}

			float t = SoftFalloff( distSq, radiusSq );
			chunk.Height[ i ] = Mathf.Lerp( chunk.Height[ i ], sum / count, t * 0.5f );
			if ( chunk.Height[ i ] < chunk.BaseHeight[ i ] )
				chunk.Height[ i ] = chunk.BaseHeight[ i ];
			chunk.ExpandDirtyRect( x, x, z, z );
			chunk.Dirty = true;
		} );
	}

	public static void FlattenVolume(
		TreasureSurfaceWorld world,
		Vector3 worldCenter,
		float radius,
		float targetHeight )
	{
		if ( world == null || !world.IsInitialized || radius <= 0f )
			return;

		world.ForEachCellInRadius( worldCenter, radius, ( chunk, x, z, wx, wz, distSq, radiusSq ) =>
		{
			int i = chunk.Index( x, z );
			float t = SoftFalloff( distSq, radiusSq );
			chunk.Height[ i ] = Mathf.Lerp( chunk.Height[ i ], targetHeight, t );
			if ( chunk.Height[ i ] < chunk.BaseHeight[ i ] )
				chunk.Height[ i ] = chunk.BaseHeight[ i ];
			chunk.ExpandDirtyRect( x, x, z, z );
			chunk.Dirty = true;
		} );
	}

	public static void PaintTraversable(
		TreasureSurfaceWorld world,
		Vector3 worldCenter,
		float radius,
		bool traversable )
	{
		if ( world == null || !world.IsInitialized || radius <= 0f )
			return;

		byte value = traversable ? ( byte )1 : ( byte )0;
		world.ForEachCellInRadius( worldCenter, radius, ( chunk, x, z, wx, wz, distSq, radiusSq ) =>
		{
			chunk.PaintTraversable[ chunk.Index( x, z ) ] = value;
			chunk.ExpandDirtyRect( x, x, z, z );
			chunk.Dirty = true;
		} );
	}

	public static void PaintMaterial(
		TreasureSurfaceWorld world,
		Vector3 worldCenter,
		float radius,
		TreasureSurfaceMaterial material )
	{
		if ( world == null || !world.IsInitialized || radius <= 0f )
			return;

		byte value = ( byte )material;
		world.ForEachCellInRadius( worldCenter, radius, ( chunk, x, z, wx, wz, distSq, radiusSq ) =>
		{
			chunk.PaintMaterial[ chunk.Index( x, z ) ] = value;
			chunk.ExpandDirtyRect( x, x, z, z );
			chunk.Dirty = true;
		} );
	}

	static void ApplyBrush(
		TreasureSurfaceWorld world,
		Vector3 worldCenter,
		float radius,
		float volume,
		bool remove )
	{
		if ( world == null || !world.IsInitialized || radius <= 0f || volume <= 0f )
			return;

		float weightSum = 0f;
		world.ForEachCellInRadius( worldCenter, radius, ( chunk, x, z, wx, wz, distSq, radiusSq ) =>
		{
			weightSum += SoftFalloff( distSq, radiusSq );
		} );

		if ( weightSum < 1e-6f )
			return;

		float invWeight = volume / weightSum;
		world.ForEachCellInRadius( worldCenter, radius, ( chunk, x, z, wx, wz, distSq, radiusSq ) =>
		{
			int i = chunk.Index( x, z );
			float w = SoftFalloff( distSq, radiusSq ) * invWeight;
			if ( remove )
				chunk.Height[ i ] = Mathf.Max( chunk.BaseHeight[ i ], chunk.Height[ i ] - w );
			else
				chunk.Height[ i ] = chunk.Height[ i ] + w;

			chunk.ExpandDirtyRect( x, x, z, z );
			chunk.Dirty = true;
		} );
	}

	static float SoftFalloff( float distSq, float radiusSq )
	{
		float t = 1f - Mathf.Sqrt( distSq / radiusSq );
		t = Mathf.Clamp01( t );
		float smooth = t * t * ( 3f - 2f * t );
		float gaussian = Mathf.Exp( -3.5f * distSq / radiusSq );
		return smooth * gaussian;
	}
}
