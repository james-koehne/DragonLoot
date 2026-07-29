using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Resolves which coin in a vertical column the player is aiming at (ray vs. stack axis),
/// so pickup can take from that coin upward instead of always using the top or raycast collider.
/// Coin transforms are seated at the bottom of each slot; selection bands match that layout
/// and the cylinder band shader (<c>floor(stackY01 * count)</c>).
/// </summary>
public static class CoinColumnPickup
{
	const float MinHorizontalDir = 0.0001f;

	/// <summary>
	/// Index into <paramref name="columnBottomToTop"/> whose height best matches the aim.
	/// When <paramref name="hasHitWorldY"/> is set (raycast hit on the column), that Y is
	/// preferred over closest-approach-to-axis — looking down at a thick stack otherwise
	/// resolves one slot too low.
	/// </summary>
	public static int ResolveIndexFromAimRay(
		IReadOnlyList<TreasureItem> columnBottomToTop,
		Ray aimRay,
		bool hasHitWorldY = false,
		float hitWorldY = 0f )
	{
		if ( columnBottomToTop == null || columnBottomToTop.Count == 0 )
			return 0;

		TreasureItem bottom = columnBottomToTop[ 0 ];
		if ( bottom == null )
			return 0;

		Vector3 axis = bottom.transform.position;
		float aimY;
		if ( hasHitWorldY )
		{
			aimY = hitWorldY;
			ClampAimYToColumn( columnBottomToTop, ref aimY );
			return ResolveIndexFromWorldY( columnBottomToTop, aimY );
		}

		if ( TryGetAimHeightOnColumnAxis( aimRay, axis.x, axis.z, columnBottomToTop, out aimY ) )
			return ResolveIndexFromWorldY( columnBottomToTop, aimY );

		return ResolveIndexFromWorldY( columnBottomToTop, axis.y );
	}

	/// <summary>
	/// Coin slot index for a homogeneous tower from an aim ray / optional surface hit Y.
	/// </summary>
	public static int ResolveCoinStackIndexFromAimRay(
		Ray aimRay,
		Transform stackRoot,
		float coinStep,
		int coinCount,
		bool hasHitWorldY = false,
		float hitWorldY = 0f )
	{
		if ( stackRoot == null || coinCount <= 0 || coinStep <= 0.0001f )
			return 0;

		Vector3 axis = stackRoot.position;
		float bottomY = axis.y;
		float topY = bottomY + coinStep * coinCount;
		float aimY;

		if ( hasHitWorldY )
		{
			aimY = hitWorldY;
			if ( topY > bottomY + 0.0001f )
				aimY = Mathf.Clamp( aimY, bottomY, topY );
		}
		else if ( !TryGetAimHeightOnVerticalAxis( aimRay, axis.x, axis.z, bottomY, topY, out aimY ) )
		{
			return coinCount - 1;
		}

		int index = Mathf.FloorToInt( ( aimY - bottomY ) / coinStep );
		if ( index >= coinCount )
			index = coinCount - 1;
		return Mathf.Clamp( index, 0, coinCount - 1 );
	}

	/// <summary>
	/// Picks the slot whose bottom-aligned band best contains <paramref name="worldY"/>.
	/// Slot <c>i</c> occupies <c>[pos.y, pos.y + step)</c> (last slot includes the top).
	/// </summary>
	public static int ResolveIndexFromWorldY( IReadOnlyList<TreasureItem> columnBottomToTop, float worldY )
	{
		if ( columnBottomToTop == null || columnBottomToTop.Count == 0 )
			return 0;

		int bestIndex = 0;
		float bestScore = float.MaxValue;
		int last = columnBottomToTop.Count - 1;

		for ( int i = 0; i < columnBottomToTop.Count; i++ )
		{
			TreasureItem item = columnBottomToTop[ i ];
			if ( item == null )
				continue;

			float step = TreasureStackSpacing.GetStep( item );
			float yMin = item.transform.position.y;
			float yMax = yMin + step;
			float centerY = yMin + step * 0.5f;

			float score;
			bool inside = i == last
				? worldY >= yMin && worldY <= yMax
				: worldY >= yMin && worldY < yMax;
			if ( inside )
				score = 0f;
			else
				score = Mathf.Abs( worldY - centerY );

			if ( score < bestScore - 0.0001f || ( Mathf.Abs( score - bestScore ) <= 0.0001f && i > bestIndex ) )
			{
				bestScore = score;
				bestIndex = i;
			}
		}

		return bestIndex;
	}

	static void ClampAimYToColumn( IReadOnlyList<TreasureItem> columnBottomToTop, ref float aimY )
	{
		if ( columnBottomToTop == null || columnBottomToTop.Count == 0 )
			return;

		float bottomY = float.MaxValue;
		float topY = float.MinValue;
		for ( int i = 0; i < columnBottomToTop.Count; i++ )
		{
			TreasureItem item = columnBottomToTop[ i ];
			if ( item == null )
				continue;

			float yMin = item.transform.position.y;
			float yMax = yMin + TreasureStackSpacing.GetStep( item );
			bottomY = Mathf.Min( bottomY, yMin );
			topY = Mathf.Max( topY, yMax );
		}

		if ( topY > bottomY + 0.0001f )
			aimY = Mathf.Clamp( aimY, bottomY, topY );
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

		float bottomY = float.MaxValue;
		float topY = float.MinValue;
		for ( int i = 0; i < columnBottomToTop.Count; i++ )
		{
			TreasureItem item = columnBottomToTop[ i ];
			if ( item == null )
				continue;

			float yMin = item.transform.position.y;
			float yMax = yMin + TreasureStackSpacing.GetStep( item );
			bottomY = Mathf.Min( bottomY, yMin );
			topY = Mathf.Max( topY, yMax );
		}

		if ( topY < bottomY )
			return false;

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
