using UnityEngine;

/// <summary>
/// How GPU coin seats pick XZ when <see cref="GoldPileLootStreamSettings.enforceCoinOverlap"/> is on.
/// JitteredGrid is the cheap path; BridsonQuery keeps hash dart-throw look with an O(1) occupancy test.
/// </summary>
public enum CoinOverlapMode
{
	JitteredGrid = 0,
	BridsonQuery = 1
}

/// <summary>
/// Tunables for treasure-linked loot instance chunk streaming, LOD density, prop stream distances, and debug.
/// </summary>
[CreateAssetMenu( fileName = "GoldPileLootStreamSettings", menuName = "DragonLoot/Graphics/Gold Pile/Loot Stream Settings" )]
public class GoldPileLootStreamSettings : ScriptableObject
{
	public const string AssetPath = "Assets/Definitions/Treasure/Pile/GoldPileLootStreamSettings.asset";
	public const string LegacyAssetPath = "Assets/Materials/Shaders/GoldPile/GoldPileLootStreamSettings.asset";

	public static readonly int CoinLod0EndId = Shader.PropertyToID( "_GoldPileCoinLod0End" );
	public static readonly int CoinLod1EndId = Shader.PropertyToID( "_GoldPileCoinLod1End" );
	public static readonly int CoinLod2EndId = Shader.PropertyToID( "_GoldPileCoinLod2End" );
	public static readonly int CoinDitherFadeId = Shader.PropertyToID( "_GoldPileCoinDitherFade" );
	public static readonly int CoinLodKeep1Id = Shader.PropertyToID( "_GoldPileCoinLodKeep1" );
	public static readonly int CoinLodKeep2Id = Shader.PropertyToID( "_GoldPileCoinLodKeep2" );
	public static readonly int CoinDitherEnableId = Shader.PropertyToID( "_GoldPileCoinDitherEnable" );

	[Header( "Chunks" )]
	[Min( 1f )]
	public float chunkSize = 8f;

	[Header( "Category culling" )]
	[Tooltip( "Legacy GPU path: when enabled, large props skip distance density. Coins use per-chunk instance budgets." )]
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

	[Header( "Per-chunk instance budgets (coins)" )]
	[Tooltip( "Max Drawn / submitted coin instances per chunk at LOD0 (near)." )]
	[Min( 1 )]
	public int lod0InstancesPerChunk = 250;

	[Tooltip( "Submitted coin instances per chunk at LOD1 (stable subset of Drawn)." )]
	[Min( 0 )]
	public int lod1InstancesPerChunk = 150;

	[Tooltip( "Submitted coin instances per chunk at LOD2." )]
	[Min( 0 )]
	public int lod2InstancesPerChunk = 80;

	[Header( "LOD dither fade" )]
	[Tooltip( "Meters for screen-dither dissolve at lod0→lod1, lod1→lod2, and before lod2End." )]
	[Min( 0.1f )]
	public float ditherFadeWidth = 5f;

	[Header( "Coin Pose" )]
	[Tooltip( "0 = upright, 1 = full heightfield-normal tilt." )]
	[Range( 0f, 1f )]
	public float coinTiltStrength = 1f;

	[Tooltip( "Random tip jitter in degrees around surface axes." )]
	[Min( 0f )]
	public float coinTipJitterDegrees = 6f;

	[Tooltip( "Random yaw range in degrees (360 = full spin)." )]
	[Min( 0f )]
	public float coinYawJitterDegrees = 360f;

	[Tooltip( "Extra sink into the mesh as a fraction of coin scale (Mode A embed / Mode B surface seat)." )]
	[Min( 0f )]
	public float coinEmbedSinkFraction = 0.08f;

	[Tooltip( "Scale jitter for GPU coin seats. 0 = fall back to TreasurePileDefinition.placementScaleJitter." )]
	[Range( 0f, 0.5f )]
	public float coinScaleJitter = 0f;

	[Header( "Coin Density / Overlap" )]
	[Tooltip( "Min XZ spacing between GPU coin seats when enforceCoinOverlap is on." )]
	[Min( 0.01f )]
	public float coinPlacementMinSpacing = 0.18f;

	[Tooltip( "When true, new coin seats use CoinSeatOccupancy so XZ spacing is enforced cheaply." )]
	public bool enforceCoinOverlap = true;

	[Tooltip( "JitteredGrid claims free occupancy cells (cheap). BridsonQuery keeps hash sampling with an O(1) 5x5 test." )]
	public CoinOverlapMode coinOverlapMode = CoinOverlapMode.JitteredGrid;

	[Header( "Coin Dig Modes (A/B)" )]
	[Tooltip( "Mode A: place volume / shallow-embed seats that dig can reveal and release." )]
	public bool useEmbeddedVolumeSeats = true;

	[Tooltip( "Mode B visual: place decorative GPU coins sitting on the surface (never dig-released)." )]
	public bool useSurfaceDecorSeats = false;

	[Tooltip( "Mode A dig: release embedded seats to world when their pivot leaves the mound." )]
	public bool releaseEmbeddedSeatsOnDig = true;

	[Tooltip( "Mode B dig: spawn separate physical world coins from inventory based on dig amount." )]
	public bool spawnPhysicalCoinsOnDig = false;

	[Tooltip( "Chance per dig-amount roll to spawn one physical coin." )]
	[Range( 0f, 1f )]
	public float digPhysicalSpawnChance = 0.35f;

	[Tooltip( "Hard cap on physical coin spawns per carve." )]
	[Min( 0 )]
	public int digPhysicalMaxPerCarve = 3;

	[Tooltip( "Carve inventory units per spawn roll (rolls = floor(carveUnits / this))." )]
	[Min( 0.01f )]
	public float digPhysicalUnitsPerRoll = 1f;

	[Tooltip( "Max new surface decor GPU seats spawned per dig TopUp pass (Mode B)." )]
	[Range( 0, 48 )]
	public int digDecorTopUpMax = 10;

	[Tooltip( "Max surface decor coins re-snapped per dig stick pass (Mode B)." )]
	[Range( 0, 48 )]
	public int digStickMax = 16;

	[Header( "Density (legacy — unused for coins; kept for asset compatibility)" )]
	[Tooltip( "Legacy hash density. Coins use lodNInstancesPerChunk instead." )]
	[Range( 0f, 1f )]
	public float lod0Density = 1f;

	[Range( 0f, 1f )]
	public float lod1Density = 0.6f;

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

	[Header( "World clutter streaming (stacks / loose coins)" )]
	[Tooltip( "When true, loose world coins are never distance-hidden." )]
	public bool coinNeverCull = false;

	[Tooltip( "XZ meters for a live GroundCoinStack / GroundGoldBarStack GameObject. Beyond this the stack is a data record only." )]
	[Min( 0.1f )]
	public float stackResidentDistance = 20f;

	[Tooltip( "Coin stacks with more coins than this use largeStackResidentDistance." )]
	[Min( 1 )]
	public int largeStackCountThreshold = 50;

	[Tooltip( "XZ meters for live GroundCoinStacks taller than largeStackCountThreshold." )]
	[Min( 0.1f )]
	public float largeStackResidentDistance = 48f;

	[Tooltip( "Hard cap on live streamable stack GameObjects (coin + gold-bar combined). Steal farthest when full." )]
	[Min( 8 )]
	public int stackInstancePoolSize = 64;

	[Tooltip( "XZ meters for a live loose coin visual. Beyond this the coin is a data record only." )]
	[Min( 0.1f )]
	public float coinResidentDistance = 20f;

	[Tooltip( "Working-set cap for pooled loose-coin visuals (TreasureItemFactory)." )]
	[Min( 8 )]
	public int coinVisualPoolSize = 128;

	[Tooltip( "Same-frame renderer/collider hides per tick before records are extracted." )]
	[Min( 1 )]
	public int streamEvictBudget = 16;

	[Tooltip( "Stack / loose-coin GameObject wakes (binds) per tick." )]
	[Min( 1 )]
	public int streamWakeBudget = 8;

	[Tooltip( "Pool returns (record extract) per tick after a hide." )]
	[Min( 1 )]
	public int streamExtractBudget = 8;

	[Header( "Debug" )]
	public bool drawChunkGizmos = true;

	public bool drawOverlayStats = true;

	/// <summary>
	/// Hash of knobs that change GPU coin seat poses / densify targets. Used by coin-seat bake fingerprints.
	/// </summary>
	public int ComputeCoinSeatPlacementFingerprint()
	{
		unchecked
		{
			uint h = 2166136261u;
			h = ( h ^ ( uint )FloatBits( chunkSize ) ) * 16777619u;
			h = ( h ^ ( uint )Mathf.Max( 1, lod0InstancesPerChunk ) ) * 16777619u;
			h = ( h ^ ( uint )FloatBits( coinTiltStrength ) ) * 16777619u;
			h = ( h ^ ( uint )FloatBits( coinTipJitterDegrees ) ) * 16777619u;
			h = ( h ^ ( uint )FloatBits( coinYawJitterDegrees ) ) * 16777619u;
			h = ( h ^ ( uint )FloatBits( coinEmbedSinkFraction ) ) * 16777619u;
			h = ( h ^ ( uint )FloatBits( coinScaleJitter ) ) * 16777619u;
			h = ( h ^ ( uint )FloatBits( coinPlacementMinSpacing ) ) * 16777619u;
			h = ( h ^ ( enforceCoinOverlap ? 1u : 0u ) ) * 16777619u;
			h = ( h ^ ( uint )( int )coinOverlapMode ) * 16777619u;
			h = ( h ^ ( useEmbeddedVolumeSeats ? 1u : 0u ) ) * 16777619u;
			h = ( h ^ ( useSurfaceDecorSeats ? 1u : 0u ) ) * 16777619u;
			return ( int )h;
		}
	}

	static int FloatBits( float value )
	{
		return System.BitConverter.SingleToInt32Bits( value );
	}

	public int InstancesPerChunkForLod( int lod )
	{
		switch ( lod )
		{
			case 0: return Mathf.Max( 0, lod0InstancesPerChunk );
			case 1: return Mathf.Max( 0, lod1InstancesPerChunk );
			case 2: return Mathf.Max( 0, lod2InstancesPerChunk );
			default: return 0;
		}
	}

	/// <summary>
	/// Submit the previous LOD's instance count through the dither fade band so extras can dissolve
	/// in the shader before they are dropped from the CPU list.
	/// </summary>
	public int SoftInstancesPerChunk( int lod, float distanceMeters )
	{
		int budget = InstancesPerChunkForLod( lod );
		if ( lod <= 0 )
			return budget;

		float prevEnd = lod == 1 ? lod0End : lod1End;
		int prevBudget = InstancesPerChunkForLod( lod - 1 );
		float fade = Mathf.Max( 0.1f, ditherFadeWidth );
		if ( distanceMeters < prevEnd + fade )
			return prevBudget;
		return budget;
	}

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

	public void PushCoinDitherGlobals()
	{
		Shader.SetGlobalFloat( CoinLod0EndId, lod0End );
		Shader.SetGlobalFloat( CoinLod1EndId, lod1End );
		Shader.SetGlobalFloat( CoinLod2EndId, lod2End );
		Shader.SetGlobalFloat( CoinDitherFadeId, Mathf.Max( 0.1f, ditherFadeWidth ) );
		float invLod0 = 1f / Mathf.Max( 1, lod0InstancesPerChunk );
		Shader.SetGlobalFloat( CoinLodKeep1Id, Mathf.Clamp01( lod1InstancesPerChunk * invLod0 ) );
		Shader.SetGlobalFloat( CoinLodKeep2Id, Mathf.Clamp01( lod2InstancesPerChunk * invLod0 ) );
	}

	/// <summary>True for Gem; everything else that is not Coin uses the artifact bucket.</summary>
	public static bool IsArtifactStreamBucket( TreasureCategory category )
	{
		return category != TreasureCategory.Coin && category != TreasureCategory.Gem;
	}

	public bool NeverCullsProp( TreasureCategory category )
	{
		if ( category == TreasureCategory.Coin )
			return coinNeverCull;
		if ( category == TreasureCategory.Gem )
			return gemNeverCull;
		if ( IsArtifactStreamBucket( category ) )
			return artifactNeverCull;
		return true;
	}

	public float GetPropMaxStreamDistance( TreasureCategory category )
	{
		if ( category == TreasureCategory.Coin )
			return coinResidentDistance;
		if ( category == TreasureCategory.Gem )
			return gemMaxStreamDistance;
		if ( IsArtifactStreamBucket( category ) )
			return artifactMaxStreamDistance;
		return float.MaxValue;
	}

	public float GetStackMaxStreamDistance( int coinCount )
	{
		float max = Mathf.Max( 0.1f, stackResidentDistance );
		if ( coinCount > largeStackCountThreshold )
			max = Mathf.Max( max, largeStackResidentDistance );
		return max;
	}

	public bool IsStackInStreamRange( float distanceMetersSqr, bool currentlyResident )
	{
		return IsStackInStreamRange( distanceMetersSqr, currentlyResident, coinCount: 0 );
	}

	public bool IsStackInStreamRange( float distanceMetersSqr, bool currentlyResident, int coinCount )
	{
		float max = GetStackMaxStreamDistance( coinCount );
		if ( currentlyResident )
		{
			float exit = max + Mathf.Max( 0f, propStreamHysteresisMeters );
			return distanceMetersSqr <= exit * exit;
		}

		return distanceMetersSqr <= max * max;
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
