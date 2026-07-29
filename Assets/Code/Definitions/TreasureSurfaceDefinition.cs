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

	[Header( "Throw To Surface" )]
	[Tooltip( "Ballistic gravity used to predict throw landing on the surface." )]
	[Min( 1f )]
	public float throwBallisticGravity = 18f;

	[Tooltip( "How much of the throw's horizontal speed remains after the flight tween." )]
	[Range( 0f, 1f )]
	public float throwLandingSpeedScale = 0.85f;

	[Tooltip( "Extra arc height scale for coin throw flight (1 = CoinFlip default)." )]
	[Min( 0.1f )]
	public float throwCoinArcScale = 1.35f;

	[Tooltip( "Extra arc height scale for gem/artifact throw flight." )]
	[Min( 0.1f )]
	public float throwItemArcScale = 1f;

	[Header( "Auto Stack" )]
	[Tooltip( "XZ radius to seek a nearby coin to stack onto when settling." )]
	[Min( 0.01f )]
	public float autoStackRadius = 0.36f;

	[Tooltip( "Max vertical difference when considering another coin as a stack base." )]
	[Min( 0.01f )]
	public float autoStackHeightTolerance = 0.65f;

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

	[Tooltip( "Min horizontal impact speed that triggers the first hop." )]
	[Min( 0f )]
	public float hopImpactSpeedThreshold = 0.35f;

	[Min( 0 )]
	public int gemMaxBounces = 2;

	[Range( 0f, 1.5f )]
	public float gemBounceRestitution = 0.45f;

	[Tooltip( "How many low hops crowns/artifacts get after a throw landing." )]
	[Min( 0 )]
	public int artifactMaxBounces = 1;

	[Tooltip( "Heavy bounce damping for crowns/artifacts (much lower than coins/gems)." )]
	[Range( 0f, 1.5f )]
	public float artifactBounceRestitution = 0.18f;

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

	[Header( "Seating" )]
	[Tooltip( "Seat gems/artifacts using mesh bottom offset instead of pivot-on-plane." )]
	public bool gemSeatUsesBottomOffset = true;

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
