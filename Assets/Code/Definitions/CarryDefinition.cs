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

	[Tooltip( "Walk/sprint speed multiplier at full burden. Set to 1 to disable weight move penalty." )]
	[Range( 0.02f, 1f )]
	public float minBurdenedMoveSpeedScale = 1f;

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

	[Header( "Category Swap" )]
	[Tooltip( "Extra local offset applied to inactive category rigs (rest / parked pose)." )]
	public Vector3 categoryRestOffset = new Vector3( 0f, -0.35f, -0.2f );

	[Tooltip( "Extra local euler applied to inactive category rigs." )]
	public Vector3 categoryRestEuler = new Vector3( 25f, 0f, 0f );

	[Tooltip( "Seconds to animate a category rig between active and rest poses." )]
	[Min( 0.05f )]
	public float categorySwapDuration = 0.35f;

	[Header( "Pouch Summary HUD" )]
	[Tooltip( "Seconds to slide the pouch summary in from the left." )]
	[Min( 0f )]
	public float pouchSummaryFadeIn = 0.2f;

	[Tooltip( "Seconds the pouch summary stays fully visible." )]
	[Min( 0f )]
	public float pouchSummaryHold = 2f;

	[Tooltip( "Seconds to slide the pouch summary back out to the left." )]
	[Min( 0f )]
	public float pouchSummaryFadeOut = 0.35f;

	[Header( "Held Coin Visual" )]
	[Tooltip( "Max coins represented by the left-hand cylinder height. Logical count may exceed this." )]
	[Min( 1 )]
	public int heldVisualMaxCoins = 40;

	[Header( "Whole-Stack Interaction" )]
	[Tooltip( "Legacy flat hold duration (used only if the curve has no keys)." )]
	[Min( 0.1f )]
	public float wholeStackHoldSeconds = 5f;

	[Tooltip( "Hold duration in seconds (Y) by stack quantity (X). Clamped to [0.1, wholeStackHoldMaxSeconds]." )]
	public AnimationCurve wholeStackHoldCurve = new AnimationCurve(
		new Keyframe( 0f, 2f ),
		new Keyframe( 10f, 2f ),
		new Keyframe( 30f, 4f ),
		new Keyframe( 60f, 6f )
	);

	[Tooltip( "Hard cap for E/F whole-stack charge duration." )]
	[Min( 0.1f )]
	public float wholeStackHoldMaxSeconds = 6f;

	[Tooltip( "Radius of the circular progress ring around the crosshair." )]
	[Min( 8f )]
	public float wholeStackProgressRingSize = 96f;

	[Tooltip( "How quickly absorbed items ease into the left-hand stack base." )]
	[Min( 0.05f )]
	public float wholeStackAbsorbTweenDuration = 0.28f;

	[Tooltip( "How quickly the next item pulls from left-hand bottom into the right hand." )]
	[Min( 0.05f )]
	public float activeRefillTweenDuration = 0.22f;

	[Tooltip( "Delay between starting each gold-pile multi-steal coin flight." )]
	[Min( 0f )]
	public float pileStealStaggerSeconds = 0.06f;

	[Header( "Coin Visual Pool" )]
	[Tooltip( "Max pooled visual coins kept ready for pickup / refill flights." )]
	[Min( 4 )]
	public int coinVisualPoolSize = 24;

	[Header( "Coin Stack SFX" )]
	[Tooltip( "One-shot when a whole coin stack is grabbed (hold E)." )]
	public AudioClip[] coinStackPickupClips;

	[Range( 0f, 1f )]
	public float coinStackPickupVolumeMin = 0.85f;

	[Range( 0f, 1f )]
	public float coinStackPickupVolumeMax = 1f;

	[Range( -3f, 3f )]
	public float coinStackPickupPitchMin = 0.95f;

	[Range( -3f, 3f )]
	public float coinStackPickupPitchMax = 1.05f;

	[Tooltip( "One-shot when a whole coin stack lands on the ground / table / hopper." )]
	public AudioClip[] coinStackPlaceClips;

	[Range( 0f, 1f )]
	public float coinStackPlaceVolumeMin = 0.85f;

	[Range( 0f, 1f )]
	public float coinStackPlaceVolumeMax = 1f;

	[Range( -3f, 3f )]
	public float coinStackPlacePitchMin = 0.95f;

	[Range( -3f, 3f )]
	public float coinStackPlacePitchMax = 1.05f;

	[Header( "Stack Hand Land (burst pickup SFX)" )]
	[Tooltip( "How many individual coin pickup one-shots play (Y) when a stack settles in the hand, by stack quantity (X)." )]
	public AnimationCurve stackHandLandPickupCountCurve = new AnimationCurve(
		new Keyframe( 1f, 1f ),
		new Keyframe( 5f, 3f ),
		new Keyframe( 15f, 6f ),
		new Keyframe( 40f, 10f )
	);

	[Tooltip( "Hard cap on hand-land pickup one-shots regardless of curve." )]
	[Min( 1 )]
	public int stackHandLandPickupMaxCount = 12;

	[Tooltip( "Delay between each hand-land pickup one-shot." )]
	[Min( 0f )]
	public float stackHandLandPickupStaggerSeconds = 0.04f;

	[Tooltip( "Volume multiplier applied to each burst pickup clip (uses the coin definition's pickup clips)." )]
	[Range( 0f, 1f )]
	public float stackHandLandPickupVolumeScale = 0.45f;

	[Tooltip( "0 = 2D in-hand burst, 1 = 3D at the hand position." )]
	[Range( 0f, 1f )]
	public float stackHandLandPickupSpatialBlend;

	/// <summary>Hold seconds for E/F based on stack or carried quantity.</summary>
	public float ResolveWholeStackHoldSeconds( int quantity )
	{
		quantity = Mathf.Max( 0, quantity );
		float resolved = wholeStackHoldSeconds;
		if ( wholeStackHoldCurve != null && wholeStackHoldCurve.length > 0 )
			resolved = wholeStackHoldCurve.Evaluate( quantity );

		float cap = Mathf.Max( 0.1f, wholeStackHoldMaxSeconds );
		return Mathf.Clamp( resolved, 0.1f, cap );
	}

	/// <summary>Pickup one-shot count when a whole stack settles into the hand.</summary>
	public int ResolveStackHandLandPickupCount( int quantity )
	{
		quantity = Mathf.Max( 0, quantity );
		if ( quantity == 0 )
			return 0;

		int resolved = 1;
		if ( stackHandLandPickupCountCurve != null && stackHandLandPickupCountCurve.length > 0 )
			resolved = Mathf.RoundToInt( stackHandLandPickupCountCurve.Evaluate( quantity ) );

		resolved = Mathf.Max( 1, resolved );
		int cap = Mathf.Max( 1, stackHandLandPickupMaxCount );
		return Mathf.Min( resolved, cap, quantity );
	}

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
		categorySwapDuration = Mathf.Max( 0.05f, categorySwapDuration );
		pouchSummaryFadeIn = Mathf.Max( 0f, pouchSummaryFadeIn );
		pouchSummaryHold = Mathf.Max( 0f, pouchSummaryHold );
		pouchSummaryFadeOut = Mathf.Max( 0f, pouchSummaryFadeOut );
		heldVisualMaxCoins = Mathf.Max( 1, heldVisualMaxCoins );
		wholeStackHoldSeconds = Mathf.Max( 0.1f, wholeStackHoldSeconds );
		wholeStackHoldMaxSeconds = Mathf.Max( 0.1f, wholeStackHoldMaxSeconds );
		wholeStackProgressRingSize = Mathf.Max( 8f, wholeStackProgressRingSize );
		wholeStackAbsorbTweenDuration = Mathf.Max( 0.05f, wholeStackAbsorbTweenDuration );
		activeRefillTweenDuration = Mathf.Max( 0.05f, activeRefillTweenDuration );
		pileStealStaggerSeconds = Mathf.Max( 0f, pileStealStaggerSeconds );
		coinVisualPoolSize = Mathf.Max( 4, coinVisualPoolSize );
		stackHandLandPickupMaxCount = Mathf.Max( 1, stackHandLandPickupMaxCount );
		stackHandLandPickupStaggerSeconds = Mathf.Max( 0f, stackHandLandPickupStaggerSeconds );
		stackHandLandPickupVolumeScale = Mathf.Clamp01( stackHandLandPickupVolumeScale );
		stackHandLandPickupSpatialBlend = Mathf.Clamp01( stackHandLandPickupSpatialBlend );

		if ( stackHandLandPickupCountCurve == null || stackHandLandPickupCountCurve.length == 0 )
		{
			stackHandLandPickupCountCurve = new AnimationCurve(
				new Keyframe( 1f, 1f ),
				new Keyframe( 5f, 3f ),
				new Keyframe( 15f, 6f ),
				new Keyframe( 40f, 10f )
			);
		}

		if ( wholeStackHoldCurve == null || wholeStackHoldCurve.length == 0 )
		{
			wholeStackHoldCurve = new AnimationCurve(
				new Keyframe( 0f, 2f ),
				new Keyframe( 10f, 2f ),
				new Keyframe( 30f, 4f ),
				new Keyframe( 60f, 6f )
			);
		}
	}
}
