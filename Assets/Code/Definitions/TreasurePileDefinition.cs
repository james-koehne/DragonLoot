using System;

using UnityEngine;

[Serializable]
public struct TreasurePileEntry
{
	public TreasureDefinition treasure;

	[Min( 0 )]
	public int count;
}

[CreateAssetMenu( fileName = "TreasurePileDefinition", menuName = "Definitions/TreasurePileDefinition" )]
public class TreasurePileDefinition : ScriptableObject
{
	[Header( "Identity" )]
	public string displayName = "Treasure Pile";

	[Header( "Coin Contents" )]
	[Tooltip( "Coin types + inventory counts for dig/pick economy. GPU coin visuals are a separate densify budget (maxVisibleTotal / steady fill), not 1:1 with count." )]
	public TreasurePileEntry[] coinContents;

	[Header( "Treasure Contents" )]
	[Tooltip( "Gems and artifacts. Count = units in the pile (latent or exposed). No visibility caps." )]
	public TreasurePileEntry[] treasureContents;

	[Tooltip( "Cap on simultaneously drawn GPU coin instances (visual densify only). Buried seats do not consume this. Steady near-surface fill uses maxVisibleTotal × (1 − coinVisibleBufferFraction)." )]
	[Min( 1 )]
	public int maxVisibleTotal = 1200;

	[Tooltip( "Headroom above the steady near-surface fill. Steady target = maxVisibleTotal × (1 − this). Default 0.2 → keep ~80% of the draw cap near-surface (e.g. 1200 → ~960)." )]
	[Range( 0f, 0.9f )]
	public float coinVisibleBufferFraction = 0.2f;

	[Header( "Heightfield" )]
	[Min( 8 )]
	public int heightResolution = 64;

	[Min( 0.5f )]
	public float worldSize = 6f;

	[Min( 0.1f )]
	public float maxHeight = 1.75f;

	[Tooltip( "Local height below which the pile does not exist (no render, collider, or interaction)." )]
	[Min( 0f )]
	public float groundLevelHeight = 0.01f;

	[Header( "Mesh" )]
	[Tooltip( "Visual mesh grid resolution. 0 = match heightResolution. Lower values reduce vert cost while keeping carve fidelity." )]
	[Min( 0 )]
	public int meshResolution = 0;

	[Tooltip( "Softens heightfield-derived normals toward the flat mesh normal. Higher = smoother lighting, less carved detail." )]
	[Range( 0f, 1f )]
	public float meshDeformNormalSoften = 0f;

	[Tooltip( "Cross-blur radius (in heightfield texels) when sampling the deform map for displacement. Higher = smoother silhouette / less stair-stepping." )]
	[Range( 0f, 4f )]
	public float meshDeformSampleBlur = 4f;

	[Header( "Interaction" )]
	[Min( 0.05f )]
	public float pickRadius = 0.45f;

	[Tooltip( "How deep buried slots sit under the surface (world units)." )]
	[Min( 0.01f )]
	public float buryDepth = 0.18f;

	[Tooltip( "Slots with embed less than this are treated as initially covering the mesh." )]
	[Min( 0f )]
	public float initialRevealDepth = 0.06f;

	[Header( "Loot Placement" )]
	[Tooltip( "Minimum XZ distance between loot slots (and between visible instances)." )]
	[Min( 0.05f )]
	public float placementMinSpacing = 0.35f;

	[Tooltip( "When off, RebuildVisibility skips IsTooCloseToDrawn (A/B perf vs density)." )]
	public bool enforcePlacementSpacing = true;

	[Tooltip( "Random offset within each placement cell as a fraction of cell size (0 = rigid grid)." )]
	[Range( 0f, 0.49f )]
	public float placementJitter = 0.3f;

	[Tooltip( "How much of the pile footprint is used for loot (1 = full worldSize)." )]
	[Range( 0.4f, 1f )]
	public float placementRadiusFraction = 0.88f;

	[Tooltip( "Random scale variation around treasure worldScale (0.1 = ±10%)." )]
	[Range( 0f, 0.5f )]
	public float placementScaleJitter = 0.1f;

	[Tooltip( "Min relative surface height (0-1 of maxHeight) for coin instance placement. Higher pulls coins off the thin skirt." )]
	[Range( 0f, 0.5f )]
	public float coinSurfaceHeightFraction = 0.12f;

	[Tooltip( "Unused — dig densify tops up seats from inventory; coins are not pulled toward carves." )]
	[Min( 0 )]
	public int coinPullToCarveCount = 2;

	[Tooltip( "Gem/artifact volume radial power. 0.5 ≈ base-heavy, 1 ≈ even height, >1 pulls toward the tip." )]
	[Range( 0.25f, 3f )]
	public float treasureRadialPower = 1.25f;

	[Tooltip( "Extra weight toward the upper portion of each column for gems/artifacts (0 = uniform in column height)." )]
	[Range( 0f, 3f )]
	public float treasureHeightBias = 0.75f;

	[Tooltip( "Artifact (non-gem) pick threshold: fraction of AABB outside the mound required before the prop is pickable. Gems become pickable as soon as any probe is outside." )]
	[Range( 0.05f, 0.95f )]
	public float treasurePickupOutsideFraction = 0.4f;

	[Tooltip( "When a gem/artifact AABB is at least this far outside the mound, it leaves pile ownership as loose world loot." )]
	[Range( 0.5f, 1f )]
	public float treasureReleaseOutsideFraction = 0.9f;

	[Header( "Visuals" )]
	public Material pileMaterial;

	/// <summary>Resolved visual mesh resolution (falls back to heightResolution when meshResolution is 0).</summary>
	public int ResolveMeshResolution()
	{
		if ( meshResolution <= 0 )
			return Mathf.Max( 8, heightResolution );
		return Mathf.Max( 8, meshResolution );
	}

	public int SteadyCoinVisibleBudget()
	{
		float buffer = Mathf.Clamp01( coinVisibleBufferFraction );
		return Mathf.Max( 1, Mathf.FloorToInt( maxVisibleTotal * ( 1f - buffer ) ) );
	}

	public int DigCoinBufferSeats()
	{
		return Mathf.Max( 0, maxVisibleTotal - SteadyCoinVisibleBudget() );
	}

	public int TotalCoinUnits()
	{
		return SumUnits( coinContents );
	}

	public int TotalTreasureUnits()
	{
		return SumUnits( treasureContents );
	}

	public int TotalUnits()
	{
		return TotalCoinUnits() + TotalTreasureUnits();
	}

	static int SumUnits( TreasurePileEntry[] entries )
	{
		if ( entries == null )
			return 0;

		int total = 0;
		for ( int i = 0; i < entries.Length; i++ )
		{
			if ( entries[ i ].treasure != null )
				total += Mathf.Max( 0, entries[ i ].count );
		}

		return total;
	}

	/// <summary>
	/// Proportional visual coin seats by authored mix weights (entry.count), sized to maxVisibleTotal.
	/// Not capped by inventory — coins are densify visuals; count only drives mix + dig economy.
	/// </summary>
	public int[] ComputeCoinSeatTargets()
	{
		if ( coinContents == null || coinContents.Length == 0 )
			return Array.Empty<int>();

		int budget = Mathf.Max( 1, maxVisibleTotal );
		int totalWeight = 0;
		for ( int i = 0; i < coinContents.Length; i++ )
		{
			TreasurePileEntry entry = coinContents[ i ];
			if ( entry.treasure != null && entry.count > 0 )
				totalWeight += entry.count;
		}

		int[] targets = new int[ coinContents.Length ];
		if ( totalWeight <= 0 || budget <= 0 )
			return targets;

		int assigned = 0;
		float[] remainders = new float[ coinContents.Length ];
		for ( int i = 0; i < coinContents.Length; i++ )
		{
			TreasurePileEntry entry = coinContents[ i ];
			if ( entry.treasure == null || entry.count <= 0 )
				continue;

			float exact = ( entry.count / ( float )totalWeight ) * budget;
			int floor = Mathf.FloorToInt( exact );
			targets[ i ] = floor;
			remainders[ i ] = exact - floor;
			assigned += floor;
		}

		int leftover = budget - assigned;
		while ( leftover > 0 )
		{
			int best = -1;
			float bestRem = -1f;
			for ( int i = 0; i < coinContents.Length; i++ )
			{
				TreasurePileEntry entry = coinContents[ i ];
				if ( entry.treasure == null || entry.count <= 0 )
					continue;
				if ( remainders[ i ] > bestRem )
				{
					bestRem = remainders[ i ];
					best = i;
				}
			}

			if ( best < 0 )
				break;

			targets[ best ]++;
			remainders[ best ] = -1f;
			leftover--;
		}

		return targets;
	}

	public TreasureDefinition GetPrimaryTreasure()
	{
		TreasureDefinition primary = FirstWithCount( coinContents );
		if ( primary != null )
			return primary;
		primary = FirstWithCount( treasureContents );
		if ( primary != null )
			return primary;

		primary = FirstAny( coinContents );
		if ( primary != null )
			return primary;
		return FirstAny( treasureContents );
	}

	static TreasureDefinition FirstWithCount( TreasurePileEntry[] entries )
	{
		if ( entries == null )
			return null;

		for ( int i = 0; i < entries.Length; i++ )
		{
			if ( entries[ i ].treasure != null && entries[ i ].count > 0 )
				return entries[ i ].treasure;
		}

		return null;
	}

	static TreasureDefinition FirstAny( TreasurePileEntry[] entries )
	{
		if ( entries == null )
			return null;

		for ( int i = 0; i < entries.Length; i++ )
		{
			if ( entries[ i ].treasure != null )
				return entries[ i ].treasure;
		}

		return null;
	}
}
