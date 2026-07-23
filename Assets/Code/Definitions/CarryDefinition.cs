using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu( fileName = "CarryDefinition", menuName = "Definitions/CarryDefinition" )]
public class CarryDefinition : ScriptableObject
{
	[Header( "Carry Weight" )]
	[Tooltip( "Carried weight at or above this value applies full movement burden (see minBurdenedMoveSpeedScale)." )]
	[Min( 1 )]
	[FormerlySerializedAs( "maxCapacity" )]
	public int maxCarryWeight = 10;

	[Tooltip( "Walk/sprint speed multiplier at full burden (always able to move, but very slow)." )]
	[Range( 0.02f, 1f )]
	public float minBurdenedMoveSpeedScale = 0.08f;

	[Header( "Hold Root Pose" )]
	[Tooltip( "Local offset of HoldRoot under the camera (held stack / hand position)." )]
	public Vector3 holdLocalOffset = new Vector3( 0.25f, -0.2f, 0.45f );

	[Tooltip( "Extra local euler degrees applied to HoldRoot before upright / sway." )]
	public Vector3 holdLocalEuler = Vector3.zero;

	[Header( "Active Item Root" )]
	[Tooltip( "Local offset of ActiveRoot under the camera (toward screen center)." )]
	public Vector3 activeItemOffset = new Vector3( 0f, -0.12f, 0.55f );

	[Tooltip( "Extra local euler degrees applied to ActiveRoot." )]
	public Vector3 activeLocalEuler = Vector3.zero;

	[Header( "Held Stack" )]
	[Tooltip( "Local offset of the held stack base under HoldRoot." )]
	public Vector3 heldStackOffset = Vector3.zero;

	[Tooltip( "Max random X/Z offset per held-stack slot (deterministic per slot)." )]
	[Min( 0f )]
	public float heldStackHorizontalSpread = 0.012f;

	[Tooltip( "How quickly visual poses catch up when cycling the Active Item." )]
	[Min( 0.1f )]
	public float itemCycleSpeed = 14f;

	[Tooltip( "How quickly held-stack items ease into new slot poses when the stack shifts." )]
	[Min( 0.1f )]
	public float stackPoseSmoothSpeed = 16f;

	[Tooltip( "Scroll delta magnitude required before cycling (debounce)." )]
	[Min( 0.01f )]
	public float itemCycleScrollThreshold = 0.1f;

	[Header( "Held Stack Spacing" )]
	[Tooltip( "Extra gap added on top of each held item's measured height." )]
	[Min( 0f )]
	public float stackPadding = 0.01f;

	[Tooltip( "Fallback step height when an item has no measurable mesh/collider bounds." )]
	[Min( 0.001f )]
	public float fallbackStackStep = 0.04f;

	[Header( "Pickup Tween" )]
	[Min( 0.05f )]
	public float holdTweenDuration = 0.2f;

	[Header( "Coin Flip (pickup)" )]
	[Tooltip( "Duration of the coin flip arc into the hand. 0 uses holdTweenDuration." )]
	[Min( 0f )]
	public float coinFlipDuration = 0.32f;

	[Tooltip( "Peak height of the pickup arc in HoldRoot local space." )]
	[Min( 0f )]
	public float coinFlipArcHeight = 0.18f;

	[Tooltip( "Full end-over-end revolutions during pickup." )]
	[Min( 0f )]
	public float coinFlipSpins = 1.25f;

	[Header( "Item Arc (pickup, non-coins)" )]
	[Tooltip( "Peak height of the mild pickup arc for gems and other non-coin treasure." )]
	[Min( 0f )]
	public float itemArcHeight = 0.08f;

	[Header( "Hand Bob (while moving)" )]
	[Min( 0f )]
	public float bobAmplitude = 0.025f;

	[Min( 0f )]
	public float bobFrequency = 8f;

	[Tooltip( "Planar speed above which full bob applies." )]
	[Min( 0.01f )]
	public float bobFullSpeed = 4f;

	[Tooltip( "Bob scale while standing still (0 = none)." )]
	[Range( 0f, 1f )]
	public float idleBobScale = 0.15f;

	[Header( "Hand Sway" )]
	[Tooltip( "Positional sway amplitude in HoldRoot local space (XYZ)." )]
	public Vector3 swayAmplitude = new Vector3( 0.02f, 0.01f, 0.015f );

	[Min( 0f )]
	public float swayFrequency = 1.6f;

	[Tooltip( "Extra lateral offset from strafe input / velocity." )]
	[Min( 0f )]
	public float strafeSway = 0.04f;

	[Tooltip( "Extra forward/back offset from forward move velocity." )]
	[Min( 0f )]
	public float moveSway = 0.03f;

	[Tooltip( "How quickly hand motion catches target bob/sway." )]
	[Min( 0.1f )]
	public float handMotionSmoothSpeed = 12f;

	[Header( "Upright Favor" )]
	[Tooltip( "0 = follow camera pitch fully, 1 = keep HoldRoot world-upright." )]
	[Range( 0f, 1f )]
	public float uprightPitchFavor = 0.65f;

	[Min( 0.1f )]
	public float uprightSmoothSpeed = 10f;

	void OnValidate()
	{
		maxCarryWeight = Mathf.Max( 1, maxCarryWeight );
		minBurdenedMoveSpeedScale = Mathf.Clamp( minBurdenedMoveSpeedScale, 0.02f, 1f );
		heldStackHorizontalSpread = Mathf.Max( 0f, heldStackHorizontalSpread );
		itemCycleSpeed = Mathf.Max( 0.1f, itemCycleSpeed );
		stackPoseSmoothSpeed = Mathf.Max( 0.1f, stackPoseSmoothSpeed );
		itemCycleScrollThreshold = Mathf.Max( 0.01f, itemCycleScrollThreshold );
		stackPadding = Mathf.Max( 0f, stackPadding );
		fallbackStackStep = Mathf.Max( 0.001f, fallbackStackStep );
		holdTweenDuration = Mathf.Max( 0.05f, holdTweenDuration );
		coinFlipDuration = Mathf.Max( 0f, coinFlipDuration );
		coinFlipArcHeight = Mathf.Max( 0f, coinFlipArcHeight );
		coinFlipSpins = Mathf.Max( 0f, coinFlipSpins );
		itemArcHeight = Mathf.Max( 0f, itemArcHeight );
		bobAmplitude = Mathf.Max( 0f, bobAmplitude );
		bobFrequency = Mathf.Max( 0f, bobFrequency );
		bobFullSpeed = Mathf.Max( 0.01f, bobFullSpeed );
		idleBobScale = Mathf.Clamp01( idleBobScale );
		swayFrequency = Mathf.Max( 0f, swayFrequency );
		strafeSway = Mathf.Max( 0f, strafeSway );
		moveSway = Mathf.Max( 0f, moveSway );
		handMotionSmoothSpeed = Mathf.Max( 0.1f, handMotionSmoothSpeed );
		uprightPitchFavor = Mathf.Clamp01( uprightPitchFavor );
		uprightSmoothSpeed = Mathf.Max( 0.1f, uprightSmoothSpeed );
	}
}
