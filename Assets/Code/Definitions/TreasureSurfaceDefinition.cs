using UnityEngine;

[CreateAssetMenu( fileName = "TreasureSurfaceDefinition", menuName = "Definitions/TreasureSurfaceDefinition" )]
public class TreasureSurfaceDefinition : ScriptableObject
{
	[Header( "World" )]
	[Tooltip( "World-space center of the surface grid." )]
	public Vector3 worldOrigin = Vector3.zero;

	[Tooltip( "Total world extent on X (meters)." )]
	[Min( 8f )]
	public float worldSizeX = 128f;

	[Tooltip( "Total world extent on Z (meters)." )]
	[Min( 8f )]
	public float worldSizeZ = 128f;

	[Tooltip( "Chunk edge length in meters." )]
	[Min( 1f )]
	public float chunkSize = 8f;

	[Tooltip( "Cells along one chunk edge." )]
	[Min( 8 )]
	public int cellsPerChunk = 64;

	[Tooltip( "Base / empty-floor height (world Y)." )]
	public float baseHeight = 0f;

	[Tooltip( "Minimum valid world Y for recovery checks." )]
	public float minWorldY = -2f;

	[Tooltip( "Maximum valid world Y for recovery checks." )]
	public float maxWorldY = 32f;

	[Header( "Streaming" )]
	[Min( 8f )]
	public float activeRadius = 24f;

	[Header( "Rebuild" )]
	[Tooltip( "Surface relaxation iterations after height edits. 0 = instant (recommended for pile stamps)." )]
	[Range( 0, 8 )]
	public int relaxIterations = 0;

	[Min( 1 )]
	public int maxDirtyChunksPerFrame = 4;

	[Tooltip( "Slope below this is considered stable for sleep." )]
	[Min( 0.001f )]
	public float stableSlopeThreshold = 0.08f;

	[Tooltip( "Slope above this is steep (unstable)." )]
	[Min( 0.01f )]
	public float steepSlopeThreshold = 0.45f;

	[Header( "Simulation" )]
	[Min( 0.001f )]
	public float sleepSpeedThreshold = 0.05f;

	[Min( 0f )]
	public float sleepRestTime = 0.25f;

	[Min( 0.1f )]
	public float gravity = 18f;

	[Min( 0.1f )]
	public float flowAcceleration = 12f;

	[Tooltip( "Max horizontal speed while sliding on the surface." )]
	[Min( 0.5f )]
	public float maxSlideSpeed = 14f;

	[Tooltip( "Seconds for pile-released coins/gems to ramp from rest to full flow acceleration." )]
	[Min( 0.05f )]
	public float pileReleaseAccelRampTime = 1f;

	[Tooltip( "Soft edge deflect strength multiplier applied to material bounce (coins/gems)." )]
	[Range( 0.1f, 1.5f )]
	public float softEdgeDeflectScale = 0.6f;

	[Tooltip( "Sheer height delta (m): step-ups above this are blocked; step-downs above this go airborne and bounce." )]
	[Min( 0.01f )]
	public float sheerStepHeight = 0.5f;

	[Tooltip( "When a nearly-stopped artifact is farther than this from surface contact Y, snap it down." )]
	[Min( 0.01f )]
	public float artifactSurfaceSnapDistance = 0.07f;

	[Header( "Throw To Surface" )]
	[Tooltip( "Ballistic gravity used to predict throw landing on the surface." )]
	[Min( 1f )]
	public float throwBallisticGravity = 18f;

	[Tooltip( "How much of the throw's horizontal speed remains after the flight tween." )]
	[Range( 0f, 1f )]
	public float throwLandingSpeedScale = 0.85f;

	[Tooltip( "Reserved / unused by ballistic throw flight (kept for asset continuity)." )]
	[Min( 0.1f )]
	public float throwCoinArcScale = 1.35f;

	[Tooltip( "Reserved / unused by ballistic throw flight (kept for asset continuity)." )]
	[Min( 0.1f )]
	public float throwItemArcScale = 1f;

	[Tooltip( "End-over-end revolutions during coin throw flight." )]
	[Min( 0f )]
	public float throwCoinSpins = 1.25f;

	[Tooltip( "End-over-end revolutions during gem throw flight." )]
	[Min( 0f )]
	public float throwGemSpins = 0.55f;

	[Tooltip( "End-over-end revolutions during artifact throw flight." )]
	[Min( 0f )]
	public float throwArtifactSpins = 1f;

	[Header( "Auto Stack" )]
	[Tooltip( "XZ radius to seek a nearby coin to stack onto when settling." )]
	[Min( 0.01f )]
	public float autoStackRadius = 0.36f;

	[Tooltip( "Max vertical difference when considering another coin as a stack base." )]
	[Min( 0.01f )]
	public float autoStackHeightTolerance = 0.65f;

	[Header( "Auto Stack Anim" )]
	[Tooltip( "Duration of the hop-then-arc flip onto a nearby stack." )]
	[Min( 0.05f )]
	public float autoStackFlipDuration = 0.4f;

	[Tooltip( "How high the coin rises above max(start,end) before arcing to the stack." )]
	[Min( 0f )]
	public float autoStackHopHeight = 0.7f;

	[Tooltip( "Fraction of the anim spent rising in place before the lateral arc (0-1)." )]
	[Range( 0.05f, 0.6f )]
	public float autoStackRiseFraction = 0.35f;

	[Tooltip( "Extra arc height during the lateral hop-to-stack phase." )]
	[Min( 0f )]
	public float autoStackSecondaryArcHeight = 0.15f;

	[Tooltip( "End-over-end revolutions during auto-stack flight." )]
	[Min( 0f )]
	public float autoStackFlipSpins = 1.25f;

	[Tooltip( "Random ± fraction applied to hop height per coin (0.25 = ±25%)." )]
	[Range( 0f, 0.75f )]
	public float autoStackHopHeightVariance = 0.3f;

	[Tooltip( "Random ± fraction applied to flip duration per coin." )]
	[Range( 0f, 0.5f )]
	public float autoStackDurationVariance = 0.2f;

	[Tooltip( "Random ± fraction applied to rise fraction per coin." )]
	[Range( 0f, 0.5f )]
	public float autoStackRiseFractionVariance = 0.2f;

	[Tooltip( "Random ± fraction applied to flip spins per coin." )]
	[Range( 0f, 0.5f )]
	public float autoStackSpinVariance = 0.25f;

	[Tooltip( "Random ± meters of lateral apex offset so hops don't look identical." )]
	[Min( 0f )]
	public float autoStackApexJitter = 0.08f;

	[Header( "Category Motion" )]
	public float coinFrictionScale = 0.55f;
	public float coinBounceScale = 0.35f;
	public float coinSpeedScale = 1.2f;

	public float gemFrictionScale = 0.4f;
	public float gemBounceScale = 0.55f;
	public float gemSpeedScale = 1.35f;
	public float gemWobble = 0.08f;

	[Tooltip( "Friction multiplier for crowns/artifacts while settling on the surface." )]
	[Min( 0.1f )]
	public float artifactFrictionScale = 2.5f;

	[Tooltip( "Max slide speed multiplier for crowns/artifacts." )]
	[Min( 0.05f )]
	public float artifactSpeedScale = 0.35f;

	[Tooltip( "Legacy settle rate; kept for inspector continuity. Artifact decel uses artifactFrictionScale." )]
	public float artifactSettleSpeed = 0.35f;

	[Tooltip( "Extra gravity-scaled deceleration when sliding against painted flow (uphill)." )]
	[Min( 0f )]
	public float flowUphillResistance = 5f;

	[Header( "Hop / Bounce" )]
	[Tooltip( "How many vertical hops a coin gets after first surface contact (then slides)." )]
	[Min( 0 )]
	public int coinMaxBounces = 1;

	[Range( 0f, 1.5f )]
	public float coinBounceRestitution = 0.55f;

	[Tooltip( "Flip angular speed (rad/s) applied on the coin's first bounce." )]
	[Min( 0f )]
	public float coinPostBounceFlipSpins = 14f;

	[Tooltip( "Min hop vertical velocity for coins." )]
	[Min( 0f )]
	public float coinHopMin = 0.8f;

	[Tooltip( "Max hop vertical velocity for coins." )]
	[Min( 0.01f )]
	public float coinHopMax = 4.5f;

	[Tooltip( "Min impact speed that triggers the first hop." )]
	[Min( 0f )]
	public float hopImpactSpeedThreshold = 0.35f;

	[Min( 0 )]
	public int gemMaxBounces = 2;

	[Range( 0f, 1.5f )]
	public float gemBounceRestitution = 0.45f;

	[Tooltip( "Min hop vertical velocity for gems." )]
	[Min( 0f )]
	public float gemHopMin = 0.8f;

	[Tooltip( "Max hop vertical velocity for gems." )]
	[Min( 0.01f )]
	public float gemHopMax = 4.5f;

	[Tooltip( "How many low hops crowns/artifacts get after a throw landing." )]
	[Min( 0 )]
	public int artifactMaxBounces = 1;

	[Tooltip( "Max times a throw can bounce off a gold pile before seating into surface flow." )]
	[Min( 0 )]
	public int throwPileMaxBounces = 3;

	[Tooltip( "Restitution when throws bounce off a gold pile mound." )]
	[Range( 0f, 1.5f )]
	public float throwPileBounceRestitution = 0.45f;

	[Tooltip( "Max times a throw can bounce off the treasure-surface edge or a non-traversable cell (blockers)." )]
	[Min( 0 )]
	public int throwBoundaryMaxBounces = 4;

	[Tooltip( "Restitution when throws bounce back from outside the surface or a non-traversable fence." )]
	[Range( 0f, 1.5f )]
	public float throwBoundaryBounceRestitution = 0.55f;

	[Tooltip( "Heavy bounce damping for crowns/artifacts (much lower than coins/gems)." )]
	[Range( 0f, 1.5f )]
	public float artifactBounceRestitution = 0.08f;

	[Tooltip( "Min hop vertical velocity for artifacts." )]
	[Min( 0f )]
	public float artifactHopMin = 2f;

	[Tooltip( "Max hop vertical velocity for artifacts (small thud ~20cm at gravity 18)." )]
	[Min( 0.01f )]
	public float artifactHopMax = 3.2f;

	[Tooltip( "Converts horizontal speed into gem roll angular velocity." )]
	[Min( 0f )]
	public float gemRollAngularScale = 6f;

	[Min( 0f )]
	public float gemAngularDamping = 2.5f;

	[Header( "Gem Push" )]
	[Min( 0.01f )]
	public float gemPushRadius = 0.18f;

	[Min( 0f )]
	public float gemPushStrength = 8f;

	[Tooltip( "Scales push radius when separating gems from coins / coin stacks." )]
	[Min( 0.1f )]
	public float gemVsCoinPushRadiusScale = 1.15f;

	[Header( "Artifact Push" )]
	[Tooltip( "Minimum XZ radius floor when mesh bounds are tiny (place reject + throw separation)." )]
	[Min( 0.01f )]
	public float artifactPushRadius = 0.22f;

	[Tooltip( "Scales push radius when separating artifacts from coins / coin stacks." )]
	[Min( 0.1f )]
	public float artifactVsCoinPushRadiusScale = 1.15f;

	[Tooltip( "Extra world-space padding around the artifact mesh bounds for place reject / throw separation." )]
	[Min( 0f )]
	public float artifactPlaceMeshLeeway = 0.04f;

	[Header( "Gem Pyramid" )]
	[Tooltip( "Spacing as a multiple of gem push / estimate radius." )]
	[Min( 0.5f )]
	public float gemPyramidSpacingScale = 1.05f;

	[Tooltip( "Extra XZ padding beyond the pyramid footprint used when deciding if a gem should join." )]
	[Min( 0.05f )]
	public float gemPyramidJoinRadius = 0.75f;

	[Tooltip( "Duration of the gem tuck-arc when joining / collapsing." )]
	[Min( 0.05f )]
	public float gemPyramidTuckDuration = 0.28f;

	[Tooltip( "Hop height of the gem tuck-arc." )]
	[Min( 0f )]
	public float gemPyramidTuckHopHeight = 0.12f;

	[Header( "Seating" )]
	[Tooltip( "Seat gems/artifacts using mesh bottom offset instead of pivot-on-plane." )]
	public bool gemSeatUsesBottomOffset = true;

	[Tooltip( "When true, gems snap yaw-upright on settle. When false, keep the rotation they stopped at." )]
	public bool gemFlattenOnSettle = false;

	[Header( "Materials" )]
	public TreasureSurfaceMaterialParams[] materials;

	public void EnsureDefaults()
	{
		if ( materials != null && materials.Length >= 6 )
			return;

		materials = new TreasureSurfaceMaterialParams[ 6 ];
		for ( int i = 0; i < 6; i++ )
			materials[ i ] = TreasureSurfaceMaterialParams.DefaultFor( ( TreasureSurfaceMaterial )i );
	}

	public TreasureSurfaceMaterialParams GetMaterialParams( TreasureSurfaceMaterial material )
	{
		EnsureDefaults();
		int index = ( int )material;
		if ( index < 0 || index >= materials.Length )
			return TreasureSurfaceMaterialParams.DefaultFor( TreasureSurfaceMaterial.Stone );
		return materials[ index ];
	}

	public int ChunkCountX
	{
		get { return Mathf.Max( 1, Mathf.CeilToInt( worldSizeX / Mathf.Max( 1f, chunkSize ) ) ); }
	}

	public int ChunkCountZ
	{
		get { return Mathf.Max( 1, Mathf.CeilToInt( worldSizeZ / Mathf.Max( 1f, chunkSize ) ) ); }
	}

	public float CellSize
	{
		get { return Mathf.Max( 0.01f, chunkSize / Mathf.Max( 1, cellsPerChunk ) ); }
	}

	public Bounds WorldBounds
	{
		get
		{
			Vector3 size = new Vector3( worldSizeX, maxWorldY - minWorldY, worldSizeZ );
			Vector3 center = worldOrigin + new Vector3( 0f, ( minWorldY + maxWorldY ) * 0.5f, 0f );
			return new Bounds( center, size );
		}
	}
}
