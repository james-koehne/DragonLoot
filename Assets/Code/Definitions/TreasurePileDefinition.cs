using System;

using UnityEngine;

[Serializable]
public struct TreasurePileEntry
{
	public TreasureDefinition treasure;

	[Min( 0 )]
	public int count;

	[Tooltip( "Max simultaneously visible instances of this type. 0 uses definition defaultPerTypeVisible." )]
	[Min( 0 )]
	public int maxVisible;
}

[CreateAssetMenu( fileName = "TreasurePileDefinition", menuName = "Definitions/TreasurePileDefinition" )]
public class TreasurePileDefinition : ScriptableObject
{
	[Header( "Identity" )]
	public string displayName = "Treasure Pile";

	[Header( "Contents" )]
	public TreasurePileEntry[] contents;

	[Tooltip( "Global cap on visible GPU instances covering the mesh." )]
	[Min( 1 )]
	public int maxVisibleTotal = 400;

	[Tooltip( "Fallback per-type visible cap when entry.maxVisible is 0." )]
	[Min( 1 )]
	public int defaultPerTypeVisible = 80;

	[Header( "Heightfield" )]
	[Min( 8 )]
	public int heightResolution = 64;

	[Tooltip( "Visual mesh grid resolution. 0 = match heightResolution. Lower values reduce vert cost while keeping carve fidelity." )]
	[Min( 0 )]
	public int meshResolution = 0;

	[Min( 0.5f )]
	public float worldSize = 6f;

	[Min( 0.1f )]
	public float maxHeight = 1.75f;

	[Header( "Interaction" )]
	[Tooltip( "World-space radius of the soft carve brush. Prefer ~5-10% of worldSize." )]
	[Min( 0.05f )]
	public float carveRadius = 0.55f;

	[Tooltip( "Fraction of one unit's volume removed per pick. Keep tiny for subtle dents." )]
	[Range( 0.0001f, 1f )]
	public float carveVolumeScale = 0.02f;

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

	[Tooltip( "Random offset within each placement cell as a fraction of cell size (0 = rigid grid)." )]
	[Range( 0f, 0.49f )]
	public float placementJitter = 0.3f;

	[Tooltip( "How much of the pile footprint is used for loot (1 = full worldSize)." )]
	[Range( 0.4f, 1f )]
	public float placementRadiusFraction = 0.88f;

	[Tooltip( "Random scale variation around treasure worldScale (0.1 = ±10%)." )]
	[Range( 0f, 0.5f )]
	public float placementScaleJitter = 0.1f;

	[Header( "Visuals" )]
	public Material pileMaterial;

	/// <summary>Resolved visual mesh resolution (falls back to heightResolution when meshResolution is 0).</summary>
	public int ResolveMeshResolution()
	{
		if ( meshResolution <= 0 )
			return Mathf.Max( 8, heightResolution );
		return Mathf.Max( 8, meshResolution );
	}

	public int TotalUnits()
	{
		if ( contents == null )
			return 0;

		int total = 0;
		for ( int i = 0; i < contents.Length; i++ )
		{
			if ( contents[ i ].treasure != null )
				total += Mathf.Max( 0, contents[ i ].count );
		}

		return total;
	}

	public int GetMaxVisibleFor( TreasurePileEntry entry )
	{
		if ( entry.maxVisible > 0 )
			return entry.maxVisible;
		return Mathf.Max( 1, defaultPerTypeVisible );
	}

	public TreasureDefinition GetPrimaryTreasure()
	{
		if ( contents == null )
			return null;

		for ( int i = 0; i < contents.Length; i++ )
		{
			if ( contents[ i ].treasure != null && contents[ i ].count > 0 )
				return contents[ i ].treasure;
		}

		for ( int i = 0; i < contents.Length; i++ )
		{
			if ( contents[ i ].treasure != null )
				return contents[ i ].treasure;
		}

		return null;
	}
}
