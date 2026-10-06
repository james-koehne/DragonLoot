using UnityEngine;

public enum MinecartKind
{
	Cargo = 0,
	Drive = 1
}

[CreateAssetMenu( fileName = "MinecartDefinition", menuName = "Definitions/MinecartDefinition" )]
public class MinecartDefinition : ScriptableObject
{
	[Header( "Kind" )]
	public MinecartKind kind = MinecartKind.Cargo;
	[Header( "Push" )]
	[Tooltip( "Max distance from player to cart center to keep a push session alive." )]
	[Min( 0.5f )]
	public float pushAttachRadius = 2.5f;

	[Tooltip( "Hold Use this long before follow-push starts. Shorter release is a shove." )]
	[Min( 0.05f )]
	public float pushHoldThreshold = 0.18f;

	[Tooltip( "Initial along-track speed applied by a tap shove." )]
	[Min( 0.1f )]
	public float shoveSpeed = 7f;

	[Tooltip( "How quickly shove speed decays to zero (units/sec²)." )]
	[Min( 0.1f )]
	public float shoveDrag = 8f;

	[Tooltip( "Along-track meters per look-delta unit while holding Interact. Look-delta is already scaled mouse pixels (~0.05×)." )]
	[Min( 0f )]
	public float mouseSteerScale = 0.25f;

	[Tooltip( "Cart origin offset along the track up vector so wheels sit on the rails." )]
	public float rideHeight = 0.15f;

	[Tooltip( "Used to roll wheel meshes from distance travelled." )]
	[Min( 0.05f )]
	public float wheelRadius = 0.21f;

	[Tooltip( "Half of the along-track length used so two carts cannot overlap." )]
	[Min( 0.1f )]
	public float blockingHalfLength = 0.85f;

	[Tooltip( "If no track is assigned, bind the nearest track within this radius on play." )]
	[Min( 0.5f )]
	public float trackSnapRadius = 4f;

	[Header( "Junctions" )]
	[Tooltip( "Along-track distance from a baked junction where A/D steer is read and the exit is committed." )]
	[Min( 0.1f )]
	public float junctionApproachRadius = 2.5f;

	[Tooltip( "Minimum side-align score for A/D to pick a branch. Below this, prefer continuing on the current track." )]
	[Range( 0f, 1f )]
	public float junctionLookMinAlign = 0.15f;

	[Header( "Rider" )]
	[Tooltip( "When the player stands on the cart, carry them with it so they can walk around while it moves." )]
	public bool attachPlayerWhenStanding = true;

	[Tooltip( "Along-track speed multiplier while a player is standing on the cart." )]
	[Min( 0.1f )]
	public float riderSpeedMultiplier = 2f;

	[Header( "Recall" )]
	[Tooltip( "Along-track speed used when a call post summons this consist." )]
	[Min( 0.1f )]
	public float recallSpeed = 8f;

	[Tooltip( "Stop when the lead is this close to the call post along the track." )]
	[Min( 0.02f )]
	public float recallStopDistance = 0.2f;

	[Header( "Drive" )]
	[Min( 0.1f )]
	public float driveMaxSpeed = 10f;

	[Min( 0.1f )]
	public float driveAcceleration = 12f;

	[Min( 0.1f )]
	public float driveBrake = 18f;

	[Header( "Cargo Grid" )]
	[Min( 1 )]
	public int gridColumns = 4;

	[Min( 1 )]
	public int gridRows = 3;

	[Tooltip( "Local-space distance between adjacent cell centers." )]
	[Min( 0.05f )]
	public float cellSpacing = 0.22f;

	[Tooltip( "0 = unlimited vertical stack height per footprint for canStack treasure." )]
	[Min( 0 )]
	public int maxStackPerCell = 0;

	[Header( "Unload" )]
	[Tooltip( "Default radius used by unload points to detect a nearby cart." )]
	[Min( 0.5f )]
	public float unloadDetectRadius = 3f;

	[Tooltip( "World scatter radius when dumping cargo at an unload spout." )]
	[Min( 0f )]
	public float unloadScatterRadius = 0.35f;

	[Header( "Hop" )]
	[Tooltip( "Base visual lift applied to VisualRoot while hopping over another cart." )]
	[Min( 0.05f )]
	public float hopHeight = 0.75f;

	[Tooltip( "Minimum visual sine-arc duration for the lead car (followers stagger after this)." )]
	[Min( 0.1f )]
	public float hopDuration = 0.45f;

	[Tooltip( "Per-car delay before each follower starts its hop arc." )]
	[Min( 0f )]
	public float hopStaggerDelay = 0.06f;

	[Tooltip( "Extra hop height scaled by follower count in the hopping consist." )]
	[Min( 0f )]
	public float hopHeightPerExtraCar = 0.12f;

	[Tooltip( "Extra hop height scale from blocker BlockingHalfLength / hopping cart half-length." )]
	[Min( 0f )]
	public float hopBlockerScale = 0.25f;

	[Tooltip( "Max open track (m) after current hop clearance before chain-extend stops. Contiguous carts closer than this keep the hop; larger gaps end it." )]
	[Min( 0f )]
	public float hopChainMaxGap = 2.5f;

	[Tooltip( "Begin hop this many meters before bumper contact so the arc peaks over the blocker." )]
	[Min( 0f )]
	public float hopApproachLead = 2.75f;

	[Tooltip( "Minimum free track past the blocker's far bumper required to land. If less, stop against the cart instead of hopping." )]
	[Min( 0.1f )]
	public float hopLandingClearance = 2.4f;

	void OnValidate()
	{
		pushAttachRadius = Mathf.Max( 0.5f, pushAttachRadius );
		pushHoldThreshold = Mathf.Max( 0.05f, pushHoldThreshold );
		shoveSpeed = Mathf.Max( 0.1f, shoveSpeed );
		shoveDrag = Mathf.Max( 0.1f, shoveDrag );
		mouseSteerScale = Mathf.Max( 0f, mouseSteerScale );
		wheelRadius = Mathf.Max( 0.05f, wheelRadius );
		blockingHalfLength = Mathf.Max( 0.1f, blockingHalfLength );
		trackSnapRadius = Mathf.Max( 0.5f, trackSnapRadius );
		junctionApproachRadius = Mathf.Max( 0.1f, junctionApproachRadius );
		junctionLookMinAlign = Mathf.Clamp01( junctionLookMinAlign );
		riderSpeedMultiplier = Mathf.Max( 0.1f, riderSpeedMultiplier );
		recallSpeed = Mathf.Max( 0.1f, recallSpeed );
		recallStopDistance = Mathf.Max( 0.02f, recallStopDistance );
		driveMaxSpeed = Mathf.Max( 0.1f, driveMaxSpeed );
		driveAcceleration = Mathf.Max( 0.1f, driveAcceleration );
		driveBrake = Mathf.Max( 0.1f, driveBrake );
		gridColumns = Mathf.Max( 1, gridColumns );
		gridRows = Mathf.Max( 1, gridRows );
		cellSpacing = Mathf.Max( 0.05f, cellSpacing );
		maxStackPerCell = Mathf.Max( 0, maxStackPerCell );
		unloadDetectRadius = Mathf.Max( 0.5f, unloadDetectRadius );
		unloadScatterRadius = Mathf.Max( 0f, unloadScatterRadius );
		hopHeight = Mathf.Max( 0.05f, hopHeight );
		hopDuration = Mathf.Max( 0.1f, hopDuration );
		hopStaggerDelay = Mathf.Max( 0f, hopStaggerDelay );
		hopHeightPerExtraCar = Mathf.Max( 0f, hopHeightPerExtraCar );
		hopBlockerScale = Mathf.Max( 0f, hopBlockerScale );
		hopChainMaxGap = Mathf.Max( 0f, hopChainMaxGap );
		hopApproachLead = Mathf.Max( 0f, hopApproachLead );
		hopLandingClearance = Mathf.Max( 0.1f, hopLandingClearance );
	}
}
