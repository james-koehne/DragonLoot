using System;
using UnityEngine;

/// <summary>
/// Tunables for treasure-surface grid pathfinding.
/// </summary>
[Serializable]
public struct TreasureSurfacePathSettings
{
	[Tooltip( "Preferred clearance from non-traversable paint / blockers (meters)." )]
	[Min( 0f )]
	public float edgeMargin;

	[Tooltip( "Extra A* cost per meter short of edgeMargin. Higher = stronger pull away from edges." )]
	[Min( 0f )]
	public float edgePenalty;

	[Tooltip( "Block neighbor steps whose absolute height delta exceeds this (meters). <= 0 disables height blocking (recommended; paint connectivity is enough)." )]
	public float maxStepHeight;

	[Tooltip( "Expand the A–B search AABB by this many meters so the path can detour." )]
	[Min( 0f )]
	public float searchPadding;

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
				searchPadding = 16f,
				allowDiagonal = true
			};
		}
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
