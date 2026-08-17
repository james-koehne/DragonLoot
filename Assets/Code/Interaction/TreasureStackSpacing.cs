using UnityEngine;

/// <summary>
/// Uniform vertical stack spacing for coins (and other stackables).
/// World columns (ground, tables, instanced coins) use
/// <see cref="TreasureDefinition.GetStackThickness"/>. Held columns scale that
/// thickness by <see cref="GetHeldScaleRatio"/>.
/// </summary>
public static class TreasureStackSpacing
{
	public const float FallbackStep = 0.04f;

	public static float GetStep( TreasureDefinition definition )
	{
		if ( definition == null )
			return FallbackStep;

		float step = definition.GetStackThickness();
		return step > 0.0001f ? step : FallbackStep;
	}

	public static float GetStep( TreasureItem item )
	{
		return GetStep( item != null ? item.Definition : null );
	}

	/// <summary>
	/// heldScale.y / worldScale.y so held coins and held stacks shrink uniformly
	/// (0.2 / 0.3 = 2/3, a 33% reduction).
	/// </summary>
	public static float GetHeldScaleRatio( TreasureDefinition definition )
	{
		if ( definition == null )
			return 1f;

		float worldY = Mathf.Abs( definition.worldScale.y );
		float heldY = Mathf.Abs( definition.heldScale.y );
		if ( worldY > 0.0001f && heldY > 0.0001f )
			return heldY / worldY;

		return 1f;
	}

	public static float GetHeldStep( TreasureDefinition definition )
	{
		return GetStep( definition ) * GetHeldScaleRatio( definition );
	}

	public static float GetHeldStep( TreasureItem item )
	{
		return GetHeldStep( item != null ? item.Definition : null );
	}

	/// <summary>Half-step used when seating a center on top of a surface / bounds max.</summary>
	public static float GetHalfStep( TreasureDefinition definition )
	{
		return GetStep( definition ) * 0.5f;
	}

	public static float GetHalfStep( TreasureItem item )
	{
		return GetStep( item ) * 0.5f;
	}

	/// <summary>
	/// Local / world Y offset for the item at <paramref name="index"/> (0 = bottom),
	/// summing each lower item's stack step.
	/// </summary>
	public static float GetOffsetForIndex( System.Collections.Generic.IList<TreasureItem> itemsBelowInclusiveCount, TreasureItem placing, int index )
	{
		float height = 0f;
		for ( int i = 0; i < index; i++ )
		{
			TreasureDefinition def = null;
			if ( itemsBelowInclusiveCount != null && i < itemsBelowInclusiveCount.Count && itemsBelowInclusiveCount[ i ] != null )
				def = itemsBelowInclusiveCount[ i ].Definition;
			else if ( placing != null )
				def = placing.Definition;

			height += GetStep( def );
		}

		return height;
	}
}
