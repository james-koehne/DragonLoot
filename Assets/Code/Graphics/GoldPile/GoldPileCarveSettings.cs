using UnityEngine;

/// <summary>
/// Soft heightfield brush used when the player digs or deposits into a gold pile.
/// Tuned on <see cref="GoldPileCarveDefinition"/> so dig feel is global, not per-player or per-pile.
/// </summary>
public struct GoldPileCarveSettings
{
	public const float DefaultRadius = 2f;
	public const float DefaultMinRadiusFractionOfPile = 0.06f;
	public const int DefaultBlurPadCells = 4;
	public const int DefaultBlurPasses = 1;
	public const float DefaultBlurStrength = 1f;
	public const float DefaultFalloffSharpness = 3.5f;

	/// <summary>World-space soft brush radius.</summary>
	public float radius;

	/// <summary>
	/// Minimum brush radius as a fraction of pile <c>worldSize</c> (avoids pinholes on large piles).
	/// </summary>
	public float minRadiusFractionOfPile;

	/// <summary>Max cells of blur pad beyond the brush AABB (caps work for large radii).</summary>
	public int blurPadCells;

	/// <summary>How many 3x3 re-blur passes to run after the brush (0 = no re-blur).</summary>
	public int blurPasses;

	/// <summary>0 = leave brush result sharp, 1 = full neighborhood blur each pass.</summary>
	public float blurStrength;

	/// <summary>Gaussian falloff exponent inside the soft brush (higher = sharper center).</summary>
	public float falloffSharpness;

	public static GoldPileCarveSettings Default => new GoldPileCarveSettings
	{
		radius = DefaultRadius,
		minRadiusFractionOfPile = DefaultMinRadiusFractionOfPile,
		blurPadCells = DefaultBlurPadCells,
		blurPasses = DefaultBlurPasses,
		blurStrength = DefaultBlurStrength,
		falloffSharpness = DefaultFalloffSharpness
	};

	public static GoldPileCarveSettings FromDefinition( GoldPileCarveDefinition definition )
	{
		if ( definition == null )
			return Default;

		return new GoldPileCarveSettings
		{
			radius = Mathf.Max( 0.05f, definition.carveRadius ),
			minRadiusFractionOfPile = Mathf.Clamp01( definition.carveMinRadiusFractionOfPile ),
			blurPadCells = Mathf.Max( 0, definition.carveBlurPadCells ),
			blurPasses = Mathf.Max( 0, definition.carveBlurPasses ),
			blurStrength = Mathf.Clamp01( definition.carveBlurStrength ),
			falloffSharpness = Mathf.Max( 0.1f, definition.carveFalloffSharpness )
		};
	}

	/// <summary>Loads the global carve definition when available; otherwise returns <see cref="Default"/>.</summary>
	public static GoldPileCarveSettings FromGlobalDefinition()
	{
		GoldPileCarveDefinition definition = GameInstance.GetDefinition<GoldPileCarveDefinition>();
		return FromDefinition( definition );
	}

	/// <summary>Applies the pile-size minimum radius floor.</summary>
	public GoldPileCarveSettings ResolvedForPile( float pileWorldSize )
	{
		GoldPileCarveSettings resolved = this;
		float worldSize = Mathf.Max( 0.5f, pileWorldSize );
		float minRadius = worldSize * Mathf.Clamp01( minRadiusFractionOfPile );
		resolved.radius = Mathf.Max( Mathf.Max( 0.05f, radius ), minRadius );
		resolved.blurPadCells = Mathf.Max( 0, blurPadCells );
		resolved.blurPasses = Mathf.Max( 0, blurPasses );
		resolved.blurStrength = Mathf.Clamp01( blurStrength );
		resolved.falloffSharpness = Mathf.Max( 0.1f, falloffSharpness );
		return resolved;
	}
}
