using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Resolves which coin in a vertical column the player is aiming at (ray vs. stack axis),
/// so pickup can take from that coin upward instead of always using the top or raycast collider.
/// </summary>
public static class CoinColumnPickup
{
	const float MinHorizontalDir = 0.0001f;

	/// <summary>
	/// Index into <paramref name="columnBottomToTop"/> whose height best matches the aim ray.
	/// </summary>
	public static int ResolveIndexFromAimRay( IReadOnlyList<TreasureItem> columnBottomToTop, Ray aimRay )
	{
		if ( columnBottomToTop == null || columnBottomToTop.Count == 0 )
			return 0;

		TreasureItem bottom = columnBottomToTop[ 0 ];
		if ( bottom == null )
			return 0;

		Vector3 axis = bottom.transform.position;
		float aimY;
		if ( TryGetAimHeightOnColumnAxis( aimRay, axis.x, axis.z, columnBottomToTop, out aimY ) )
			return ResolveIndexFromWorldY( columnBottomToTop, aimY );

		return ResolveIndexFromWorldY( columnBottomToTop, axis.y );
	}

	/// <summary>
	/// Coin slot index for a homogeneous <see cref="CoinStackInteractable"/> tower from an aim ray.
	/// </summary>
	public static int ResolveCoinStackIndexFromAimRay(
		Ray aimRay,
		Transform stackRoot,
		float coinStep,
		int coinCount )
	{
		if ( stackRoot == null || coinCount <= 0 || coinStep <= 0.0001f )
			return 0;

		Vector3 axis = stackRoot.position;
		float bottomY = axis.y;
		float topY = bottomY + coinStep * coinCount;
		float aimY;

		if ( TryGetAimHeightOnVerticalAxis( aimRay, axis.x, axis.z, bottomY, topY, out aimY ) )
		{
			int index = Mathf.FloorToInt( ( aimY - bottomY ) / coinStep );
			return Mathf.Clamp( index, 0, coinCount - 1 );
		}

		return coinCount - 1;
	}

	public static int ResolveIndexFromWorldY( IReadOnlyList<TreasureItem> columnBottomToTop, float worldY )
	{
		if ( columnBottomToTop == null || columnBottomToTop.Count == 0 )
			return 0;

		int bestIndex = 0;
		float bestScore = float.MaxValue;

		for ( int i = 0; i < columnBottomToTop.Count; i++ )
		{
			TreasureItem item = columnBottomToTop[ i ];
			if ( item == null )
				continue;

			float half = TreasureStackSpacing.GetHalfStep( item );
			float centerY = item.transform.position.y;
			float yMin = centerY - half;
			float yMax = centerY + half;

			float score;
			if ( worldY >= yMin && worldY <= yMax )
				score = 0f;
			else if ( worldY < yMin )
				score = yMin - worldY;
			else
				score = worldY - yMax;

			if ( score < bestScore - 0.0001f || ( Mathf.Abs( score - bestScore ) <= 0.0001f && i > bestIndex ) )
			{
				bestScore = score;
				bestIndex = i;
			}
		}

		return bestIndex;
	}

	static bool TryGetAimHeightOnColumnAxis(
		Ray aimRay,
		float axisX,
		float axisZ,
		IReadOnlyList<TreasureItem> columnBottomToTop,
		out float aimY )
	{
		aimY = 0f;
		if ( columnBottomToTop == null || columnBottomToTop.Count == 0 )
			return false;

		float bottomY = columnBottomToTop[ 0 ].transform.position.y;
		float topY = bottomY;
		for ( int i = 0; i < columnBottomToTop.Count; i++ )
		{
			TreasureItem item = columnBottomToTop[ i ];
			if ( item == null )
				continue;

			float centerY = item.transform.position.y;
			float half = TreasureStackSpacing.GetHalfStep( item );
			topY = Mathf.Max( topY, centerY + half );
			bottomY = Mathf.Min( bottomY, centerY - half );
		}

		return TryGetAimHeightOnVerticalAxis( aimRay, axisX, axisZ, bottomY, topY, out aimY );
	}

	static bool TryGetAimHeightOnVerticalAxis(
		Ray aimRay,
		float axisX,
		float axisZ,
		float bottomY,
		float topY,
		out float aimY )
	{
		aimY = 0f;
		Vector3 origin = aimRay.origin;
		Vector3 dir = aimRay.direction;

		float a = dir.x * dir.x + dir.z * dir.z;
		if ( a < MinHorizontalDir )
			return false;

		float dx = axisX - origin.x;
		float dz = axisZ - origin.z;
		float t = ( dx * dir.x + dz * dir.z ) / a;
		if ( t < 0f )
			t = 0f;

		aimY = ( origin + dir * t ).y;
		if ( topY > bottomY + 0.0001f )
			aimY = Mathf.Clamp( aimY, bottomY, topY );

		return true;
	}
}
