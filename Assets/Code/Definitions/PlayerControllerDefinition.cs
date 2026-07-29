using UnityEngine;

[CreateAssetMenu( fileName = "PlayerControllerDefinition", menuName = "Definitions/PlayerControllerDefinition" )]
public class PlayerControllerDefinition : ScriptableObject
{
	[Header( "Movement" )]
	[Min( 0f )]
	public float walkSpeed = 6f;

	[Min( 0f )]
	public float walkAcceleration = 40f;

	[Min( 0f )]
	public float walkDeceleration = 50f;

	[Min( 0f )]
	public float jumpForce = 7f;

	[Tooltip( "Seconds after leaving ground during which a jump is still allowed." )]
	[Min( 0f )]
	public float coyoteTime = 0.12f;

	[Header( "Double Jump / Glide" )]
	[Tooltip( "Upward velocity applied on the second jump while airborne. Set to 0 to enter glide without an extra boost." )]
	[Min( 0f )]
	public float doubleJumpForce = 4f;

	[Tooltip( "Vertical acceleration while gliding (negative = down). Usually gentler than normal gravity." )]
	public float glideGravity = -6f;

	[Tooltip( "Most negative vertical speed while gliding (e.g. -5 = fall at most 5 units/sec)." )]
	public float glideMaxFallSpeed = -5f;

	[Tooltip( "Planar acceleration while gliding." )]
	[Min( 0f )]
	public float glideAcceleration = 10f;

	[Tooltip( "Planar deceleration while gliding." )]
	[Min( 0f )]
	public float glideDeceleration = 8f;

	[Tooltip( "When enabled, gliding only applies while the jump button is held." )]
	public bool glideRequiresJumpHeld = true;

	[Header( "Air Control" )]
	[Tooltip( "Planar acceleration while airborne (keep small for subtle feel)." )]
	[Min( 0f )]
	public float airAcceleration = 6f;

	[Tooltip( "Planar deceleration while airborne (keep small for subtle feel)." )]
	[Min( 0f )]
	public float airDeceleration = 4f;

	[Header( "Sprint" )]
	[Min( 0f )]
	public float sprintSpeed = 9f;

	[Min( 0f )]
	public float sprintAcceleration = 50f;

	[Min( 0f )]
	public float sprintDeceleration = 45f;

	[Tooltip( "Planar acceleration/deceleration scale while airborne with sprint held." )]
	[Min( 0f )]
	public float sprintAirControl = 8f;

	[Header( "Gravity" )]
	[Tooltip( "Vertical acceleration applied while airborne (negative = down)." )]
	public float gravity = -25f;

	[Tooltip( "Downward velocity applied while grounded to stay stuck to the surface." )]
	public float groundStickVelocity = -8f;

	[Header( "Grounding" )]
	[Min( 0f )]
	public float groundCheckDistance = 0.2f;

	[Tooltip( "Extra downward probe range used to snap onto descending slopes / uneven piles." )]
	[Min( 0f )]
	public float groundSnapDistance = 0.35f;

	[Tooltip( "Max downward snap speed (units/sec) when closing a small air gap over ground." )]
	[Min( 0f )]
	public float groundSnapSpeed = 12f;

	[Header( "Slide" )]
	[Tooltip( "Slope angle (degrees) at which downhill sliding can begin." )]
	[Range( 0f, 89f )]
	public float slideAngle = 45f;

	[Tooltip( "Scales gravity-driven acceleration along the slope while sliding." )]
	[Min( 0f )]
	public float slideGravityScale = 1f;

	[Tooltip( "Lateral steer acceleration while sliding." )]
	[Min( 0f )]
	public float slideSteer = 12f;

	[Tooltip( "Braking acceleration when holding against slide velocity." )]
	[Min( 0f )]
	public float slideBrake = 18f;

	[Tooltip( "Uphill move intent dot required to exit slide and regain climbing." )]
	[Range( 0f, 1f )]
	public float slideExitDot = 0.55f;

	[Tooltip( "Degrees below slideAngle before slide exits due to flattening." )]
	[Min( 0f )]
	public float slideExitHysteresis = 5f;

	[Tooltip( "Seconds W + look-down + downhill alignment must be held before sliding starts." )]
	[Min( 0f )]
	public float slideEnterHoldTime = 0.4f;

	[Tooltip( "Camera pitch (degrees, positive = look down) required to begin slide charge." )]
	[Range( 0f, 89f )]
	public float slideEnterMinPitch = 20f;

	[Tooltip( "Move intent dot with downhill required while charging slide entry." )]
	[Range( 0f, 1f )]
	public float slideEnterDownhillDot = 0.5f;

	[Tooltip( "Minimum forward move input (W) while charging slide entry." )]
	[Range( 0f, 1f )]
	public float slideEnterForwardInput = 0.1f;

	[Tooltip( "Walk speed multiplier while slide entry is charging." )]
	[Range( 0.1f, 1f )]
	public float slideEnterMoveSpeedScale = 0.85f;

	[Tooltip( "Move intent dot against slide velocity to apply reverse brake." )]
	[Range( 0f, 1f )]
	public float slideBrakeExitDot = 0.4f;

	[Tooltip( "Planar speed at or below which reverse brake ends the slide." )]
	[Min( 0f )]
	public float slideBrakeExitSpeed = 2.5f;

	[Tooltip( "Multiplier on slideBrake when braking against slide direction." )]
	[Min( 0f )]
	public float slideReverseBrakeScale = 2f;

	[Tooltip( "Max planar speed granted by the post-slide exit boost." )]
	[Min( 0f )]
	public float slideExitBoostMaxSpeed = 24f;

	[Tooltip( "Multiplier applied to slide speed when exiting onto flatter ground." )]
	[Min( 0f )]
	public float slideExitBoostMultiplier = 1f;

	[Tooltip( "Deceleration from boosted exit speed back toward walk/sprint speed." )]
	[Min( 0f )]
	public float slideExitBoostDecay = 70f;

	[Tooltip( "How quickly exit boost direction blends toward move input." )]
	[Min( 0f )]
	public float slideExitBoostSteer = 25f;

	void OnValidate()
	{
		walkSpeed = Mathf.Max( 0f, walkSpeed );
		walkAcceleration = Mathf.Max( 0f, walkAcceleration );
		walkDeceleration = Mathf.Max( 0f, walkDeceleration );
		jumpForce = Mathf.Max( 0f, jumpForce );
		coyoteTime = Mathf.Max( 0f, coyoteTime );
		doubleJumpForce = Mathf.Max( 0f, doubleJumpForce );
		glideMaxFallSpeed = Mathf.Min( 0f, glideMaxFallSpeed );
		glideAcceleration = Mathf.Max( 0f, glideAcceleration );
		glideDeceleration = Mathf.Max( 0f, glideDeceleration );
		airAcceleration = Mathf.Max( 0f, airAcceleration );
		airDeceleration = Mathf.Max( 0f, airDeceleration );
		sprintSpeed = Mathf.Max( 0f, sprintSpeed );
		sprintAcceleration = Mathf.Max( 0f, sprintAcceleration );
		sprintDeceleration = Mathf.Max( 0f, sprintDeceleration );
		sprintAirControl = Mathf.Max( 0f, sprintAirControl );
		groundCheckDistance = Mathf.Max( 0f, groundCheckDistance );
		groundSnapDistance = Mathf.Max( 0f, groundSnapDistance );
		groundSnapSpeed = Mathf.Max( 0f, groundSnapSpeed );
		slideAngle = Mathf.Clamp( slideAngle, 0f, 89f );
		slideGravityScale = Mathf.Max( 0f, slideGravityScale );
		slideSteer = Mathf.Max( 0f, slideSteer );
		slideBrake = Mathf.Max( 0f, slideBrake );
		slideExitDot = Mathf.Clamp01( slideExitDot );
		slideExitHysteresis = Mathf.Max( 0f, slideExitHysteresis );
		slideEnterHoldTime = Mathf.Max( 0f, slideEnterHoldTime );
		slideEnterMinPitch = Mathf.Clamp( slideEnterMinPitch, 0f, 89f );
		slideEnterDownhillDot = Mathf.Clamp01( slideEnterDownhillDot );
		slideEnterForwardInput = Mathf.Clamp01( slideEnterForwardInput );
		slideEnterMoveSpeedScale = Mathf.Clamp( slideEnterMoveSpeedScale, 0.1f, 1f );
		slideBrakeExitDot = Mathf.Clamp01( slideBrakeExitDot );
		slideBrakeExitSpeed = Mathf.Max( 0f, slideBrakeExitSpeed );
		slideReverseBrakeScale = Mathf.Max( 0f, slideReverseBrakeScale );
		slideExitBoostMaxSpeed = Mathf.Max( 0f, slideExitBoostMaxSpeed );
		slideExitBoostMultiplier = Mathf.Max( 0f, slideExitBoostMultiplier );
		slideExitBoostDecay = Mathf.Max( 0f, slideExitBoostDecay );
		slideExitBoostSteer = Mathf.Max( 0f, slideExitBoostSteer );
	}
}
