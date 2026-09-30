using System;
using UnityEngine;

/// <summary>
/// Tunables for treasure-surface grid pathfinding.
/// </summary>
[Serializable]
public struct TreasureSurfacePathSettings
{
	[Tooltip( "Preferred clearance from non-traversable paint / blockers (meters). Applied as nav-grid erosion." )]
	[Min( 0f )]
	public float edgeMargin;

	[Tooltip( "Unused when edgeMargin erosion is active (kept for asset continuity / future soft costs)." )]
	[Min( 0f )]
	public float edgePenalty;

	[Tooltip( "Block neighbor steps whose absolute height delta exceeds this (meters). <= 0 disables height blocking." )]
	public float maxStepHeight;

	[Tooltip( "Extra path cost per meter of upward height gain. Higher = stronger avoidance of climbing. 0 = ignore height for cost." )]
	[Min( 0f )]
	public float uphillWeight;

	[Tooltip( "Expand the A–B search AABB by this many meters so the path can detour." )]
	[Min( 0f )]
	public float searchPadding;

	[Tooltip( "Surface cells per nav step. 4 = ~1m nav cells when surface cell is 0.25m." )]
	[Min( 1 )]
	public int navStride;

	[Tooltip( "Max nav cells in a single search AABB (after stride)." )]
	[Min( 1024 )]
	public int maxSearchCells;

	[Tooltip( "Allow 8-connected moves (diagonals)." )]
	public bool allowDiagonal;

	public static TreasureSurfacePathSettings Default
	{
		get
		{
			return new TreasureSurfacePathSettings
			{
				edgeMargin = 0.75f,
				edgePenalty = 8f,
				maxStepHeight = 0f,
				uphillWeight = 6f,
				searchPadding = 16f,
				navStride = 4,
				maxSearchCells = 262144,
				allowDiagonal = true
			};
		}
	}

	public int ResolveNavStride()
	{
		return navStride < 1 ? 4 : navStride;
	}

	public int ResolveMaxSearchCells()
	{
		return Mathf.Max( 1024, maxSearchCells > 0 ? maxSearchCells : 262144 );
	}

	/// <summary>
	/// Returns a positive step limit, or a very large value when height blocking is disabled.
	/// </summary>
	public float ResolveMaxStepHeight()
	{
		if ( maxStepHeight <= 0f )
			return float.MaxValue;
		return maxStepHeight;
	}
}
