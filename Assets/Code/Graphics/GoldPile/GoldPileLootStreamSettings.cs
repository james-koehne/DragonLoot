using UnityEngine;

/// <summary>
/// Tunables for treasure-linked loot instance chunk streaming, LOD density, prop stream distances, and debug.
/// </summary>
[CreateAssetMenu( fileName = "GoldPileLootStreamSettings", menuName = "DragonLoot/Graphics/Gold Pile/Loot Stream Settings" )]
public class GoldPileLootStreamSettings : ScriptableObject
{
	public const string AssetPath = "Assets/Definitions/Treasure/Pile/GoldPileLootStreamSettings.asset";
	public const string LegacyAssetPath = "Assets/Materials/Shaders/GoldPile/GoldPileLootStreamSettings.asset";

	[Header( "Chunks" )]
	[Min( 1f )]
	public float chunkSize = 8f;

	[Header( "Category culling" )]
	[Tooltip( "Legacy GPU path: when enabled, large props skip distance density. Coins always use lod density." )]
	public bool useCategoryDistanceCulling = true;

	[Header( "LOD distances (meters to chunk bounds, XZ) — coins" )]
	[Min( 0.1f )]
	public float lod0End = 14f;

	[Min( 0.1f )]
	public float lod1End = 28f;

	[Min( 0.1f )]
	public float lod2End = 48f;

	[Min( 1 )]
	public int lodHysteresisFrames = 8;

	[Header( "Density" )]
	[Tooltip( "Fraction of coins drawn at LOD0 (near)." )]
	[Range( 0f, 1f )]
	public float lod0Density = 1f;

	[Tooltip( "Fraction of coins drawn at LOD1." )]
	[Range( 0f, 1f )]
	public float lod1Density = 0.6f;

	[Tooltip( "Fraction of coins drawn at LOD2." )]
	[Range( 0f, 1f )]
	public float lod2Density = 0.25f;

	[Header( "Prop streaming (GameObject gems / artifacts)" )]
	[Tooltip( "When true, gems are never distance-despawned (frustum only)." )]
	public bool gemNeverCull = false;

	[Tooltip( "Max player XZ distance for gem spawn residency. Ignored when gemNeverCull." )]
	[Min( 0.1f )]
	public float gemMaxStreamDistance = 24f;

	[Tooltip( "When true, artifacts/large props are never distance-despawned (frustum only)." )]
	public bool artifactNeverCull = true;

	[Tooltip( "Max player XZ distance for artifact spawn residency. Ignored when artifactNeverCull." )]
	[Min( 0.1f )]
	public float artifactMaxStreamDistance = 48f;

	[Tooltip( "Extra meters beyond max distance before stream-out (enter uses max; exit uses max + pad)." )]
	[Min( 0f )]
	public float propStreamHysteresisMeters = 2f;

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

	public int EvaluateLodSqr( float distanceMetersSqr )
	{
		float lod0 = lod0End;
		if ( distanceMetersSqr <= lod0 * lod0 )
			return 0;
		float lod1 = lod1End;
		if ( distanceMetersSqr <= lod1 * lod1 )
			return 1;
		float lod2 = lod2End;
		if ( distanceMetersSqr <= lod2 * lod2 )
			return 2;
		return 3;
	}

	/// <summary>True for Gem; everything else that is not Coin uses the artifact bucket.</summary>
	public static bool IsArtifactStreamBucket( TreasureCategory category )
	{
		return category != TreasureCategory.Coin && category != TreasureCategory.Gem;
	}

	public bool NeverCullsProp( TreasureCategory category )
	{
		if ( category == TreasureCategory.Gem )
			return gemNeverCull;
		if ( IsArtifactStreamBucket( category ) )
			return artifactNeverCull;
		return true;
	}

	public float GetPropMaxStreamDistance( TreasureCategory category )
	{
		if ( category == TreasureCategory.Gem )
			return gemMaxStreamDistance;
		if ( IsArtifactStreamBucket( category ) )
			return artifactMaxStreamDistance;
		return float.MaxValue;
	}

	/// <summary>
	/// Stream-in test (no hysteresis). True when never-cull or planar distance within max.
	/// </summary>
	public bool IsPropInStreamRange( TreasureCategory category, float distanceMetersSqr )
	{
		if ( NeverCullsProp( category ) )
			return true;

		float max = GetPropMaxStreamDistance( category );
		return distanceMetersSqr <= max * max;
	}

	/// <summary>
	/// Stream residency with hysteresis: stay in range until past max+pad; enter when within max.
	/// </summary>
	public bool IsPropInStreamRange( TreasureCategory category, float distanceMetersSqr, bool currentlyResident )
	{
		if ( NeverCullsProp( category ) )
			return true;

		float max = GetPropMaxStreamDistance( category );
		if ( currentlyResident )
		{
			float exit = max + Mathf.Max( 0f, propStreamHysteresisMeters );
			return distanceMetersSqr <= exit * exit;
		}

		return distanceMetersSqr <= max * max;
	}
}
