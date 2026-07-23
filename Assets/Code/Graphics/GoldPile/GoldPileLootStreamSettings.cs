using UnityEngine;

/// <summary>
/// Tunables for treasure-linked loot instance chunk streaming, LOD density, and debug.
/// </summary>
[CreateAssetMenu( fileName = "GoldPileLootStreamSettings", menuName = "DragonLoot/Graphics/Gold Pile Loot Stream Settings" )]
public class GoldPileLootStreamSettings : ScriptableObject
{
	[Header( "Chunks" )]
	[Min( 1f )]
	public float chunkSize = 8f;

	[Header( "Category culling" )]
	[Tooltip( "When enabled: coins only at LOD0, gems through LOD1, crowns/artifacts/etc. at any distance while in frustum." )]
	public bool useCategoryDistanceCulling = true;

	[Header( "LOD distances (meters to chunk center)" )]
	[Min( 0.1f )]
	public float lod0End = 14f;

	[Min( 0.1f )]
	public float lod1End = 28f;

	[Min( 0.1f )]
	public float lod2End = 48f;

	[Min( 1 )]
	public int lodHysteresisFrames = 8;

	[Header( "Density" )]
	[Range( 0f, 1f )]
	public float lod0Density = 1f;

	[Range( 0f, 1f )]
	public float lod1Density = 0.6f;

	[Range( 0f, 1f )]
	public float lod2Density = 0.25f;

	[Header( "Debug" )]
	public bool drawChunkGizmos = true;

	public bool drawOverlayStats = true;

	public float DensityForLod( int lod )
	{
		switch ( lod )
		{
			case 0: return lod0Density;
			case 1: return lod1Density;
			case 2: return lod2Density;
			default: return 0f;
		}
	}

	public int EvaluateLod( float distanceMeters )
	{
		if ( distanceMeters <= lod0End )
			return 0;
		if ( distanceMeters <= lod1End )
			return 1;
		if ( distanceMeters <= lod2End )
			return 2;
		return 3;
	}
}
