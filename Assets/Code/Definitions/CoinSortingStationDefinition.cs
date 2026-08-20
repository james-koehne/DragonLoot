using UnityEngine;

/// <summary>
/// Tuning for the coin sorting station. Asset name must be
/// <c>CoinSortingStationDefinition</c> for <see cref="GameInstance.GetDefinition{T}"/>.
/// </summary>
[CreateAssetMenu( fileName = "CoinSortingStationDefinition", menuName = "Definitions/CoinSortingStationDefinition" )]
public class CoinSortingStationDefinition : ScriptableObject
{
	public const string DefaultUpgradeId = "coin_sorting_station";

	[Header( "Upgrade" )]
	[Tooltip( "UpgradeDefinition.id driving L1–L4 station capabilities." )]
	public string upgradeId = DefaultUpgradeId;

	[Header( "Hopper Capacity" )]
	[Tooltip( "Max buffered coins at levels 1–3." )]
	[Min( 1 )]
	public int baseHopperCapacity = 50;

	[Tooltip( "Max buffered coins at level 4+." )]
	[Min( 1 )]
	public int level4HopperCapacity = 150;

	[Header( "Process Rate" )]
	[Tooltip( "Coins processed per second at levels 1–2 (and while cranking at L1)." )]
	[Min( 0.01f )]
	public float baseCoinsPerSecond = 4f;

	[Tooltip( "Coins processed per second at level 3+." )]
	[Min( 0.01f )]
	public float level3CoinsPerSecond = 10f;

	[Header( "Crank" )]
	[Tooltip( "Seconds after an Interact pulse that L1 still counts as cranking (covers hold-repeat gaps)." )]
	[Min( 0.05f )]
	public float crankHoldGrace = 0.2f;

	[Tooltip( "Sort-seconds gained per second of cranking. 2 means crank 1s → 2s of sorting." )]
	[Min( 0.01f )]
	public float crankToSortMultiplier = 2f;

	[Tooltip( "Maximum stored sort-seconds on the energy gauge." )]
	[Min( 0.25f )]
	public float maxReserveSeconds = 8f;

	[Tooltip( "Crank cube spin speed while the player is holding Interact." )]
	[Min( 0f )]
	public float crankChargeSpinDegreesPerSecond = 360f;

	[Tooltip( "Crank cube spin speed while reserve is draining after release." )]
	[Min( 0f )]
	public float crankDischargeSpinDegreesPerSecond = 140f;

	[Tooltip( "Degrees of crank spin between click SFX / gauge pulses while charging." )]
	[Min( 15f )]
	public float crankClickDegrees = 90f;

	[Header( "Energy Gauge" )]
	public Color gaugeEmptyColor = new Color( 0.92f, 0.12f, 0.1f, 1f );
	public Color gaugeMidColor = new Color( 1f, 0.55f, 0.08f, 1f );
	public Color gaugeFullColor = new Color( 0.18f, 0.92f, 0.28f, 1f );

	[Tooltip( "Fill at or below this flickers red and uses quieter / lower clicks." )]
	[Range( 0.02f, 0.5f )]
	public float gaugeEmptyWarningNormalized = 0.15f;

	[Header( "Crank Audio" )]
	[Tooltip( "Clips cycled on each crank one-shot. Wraps forever." )]
	public AudioClip[] crankLoopClips;

	[Range( 0f, 1f )]
	public float crankVolume = 0.7f;

	[Range( -3f, 3f )]
	public float crankPitchMin = 0.85f;

	[Range( -3f, 3f )]
	public float crankPitchMax = 1.25f;

	[Tooltip( "Minimum seconds between crank click one-shots (safety gap)." )]
	[Min( 0.05f )]
	public float crankPlayInterval = 0.05f;

	[Tooltip( "When a new crank one-shot starts, fade the previous this fast. 0 = stop previous immediately." )]
	[Min( 0f )]
	public float crankOverlapFadeSeconds = 0.12f;

	[Tooltip( "Fade out when cranking stops. Unused for one-shots that are allowed to finish." )]
	[Min( 0f )]
	public float crankStopFadeSeconds = 0.15f;

	[Header( "Sort Audio" )]
	[Tooltip( "Clips cycled while the station is processing / sorting. Wraps forever." )]
	public AudioClip[] sortLoopClips;

	[Range( 0f, 1f )]
	public float sortVolume = 0.5f;

	[Range( -3f, 3f )]
	public float sortPitchMin = 0.95f;

	[Range( -3f, 3f )]
	public float sortPitchMax = 1.05f;

	[Tooltip( "Minimum seconds between sort-loop one-shots while processing." )]
	[Min( 0.05f )]
	public float sortPlayInterval = 1f;

	[Header( "Output" )]
	[Tooltip( "World-space lateral step when starting a new stack beside a full chute stack." )]
	[Min( 0.05f )]
	public float fullStackLateralOffset = 0.35f;

	[Tooltip( "Seconds for a sorted coin to fly from the machine output onto the chute stack." )]
	[Min( 0.05f )]
	public float sortedCoinFlightDuration = 0.28f;

	[Tooltip( "World-space arc height of the sorted-coin flight from output to chute." )]
	[Min( 0f )]
	public float sortedCoinFlightArcHeight = 0.35f;

	[Header( "Reposition" )]
	[Tooltip( "Allow hold-to-move telekinetic reposition of the sorter." )]
	public bool repositionEnabled = false;

	[Header( "Reposition — Pickup" )]
	[Tooltip( "Hold Interact on the body this long to begin telekinetic carry." )]
	[Min( 0.1f )]
	public float moveHoldSeconds = 0.5f;

	[Header( "Reposition — Float" )]
	[Tooltip( "Distance ahead of the camera for the floating sorter." )]
	[Min( 0.5f )]
	public float carryDistance = 1.8f;

	[Tooltip( "Minimum planar distance from the player to the sorter while carrying." )]
	[Min( 0.5f )]
	public float minCarryDistance = 1.45f;

	[Tooltip( "World Y of the float target relative to the camera while moving (negative = below eye height)." )]
	public float carryHeightOffset = -0.55f;

	[Tooltip( "Minimum clearance between sorter bottom and floor while moving." )]
	[Min( 0f )]
	public float floorHoverClearance = 0.08f;

	[Tooltip( "Planar move speed that reaches full hover height. Below this, height blends toward the floor." )]
	[Min( 0.05f )]
	public float moveLiftFullSpeed = 0.85f;

	[Tooltip( "Smooth time blending between grounded idle height and moving hover height." )]
	[Min( 0.01f )]
	public float moveLiftSmoothTime = 0.12f;

	[Tooltip( "Max distance to search when freeing a sorter stuck inside geometry." )]
	[Min( 0.25f )]
	public float unstickSearchDistance = 1.1f;

	[Tooltip( "SmoothDamp time for follow position." )]
	[Min( 0.01f )]
	public float followSmoothTime = 0.18f;

	[Tooltip( "Extra yaw lag (degrees of soft follow) when the player turns quickly." )]
	[Min( 0.01f )]
	public float yawInertiaSmoothTime = 0.22f;

	[Tooltip( "Vertical bob amplitude while walking." )]
	[Min( 0f )]
	public float bobAmplitude = 0.04f;

	[Tooltip( "Bob frequency (Hz) scaled by planar walk speed." )]
	[Min( 0f )]
	public float bobFrequency = 1.6f;

	[Tooltip( "Planar speed that reaches full bob amplitude." )]
	[Min( 0.1f )]
	public float bobFullSpeed = 5f;

	[Header( "Reposition — Rotate" )]
	[Tooltip( "Yaw degrees per mouse-wheel notch / bumper tap." )]
	[Min( 1f )]
	public float rotateStepDegrees = 15f;

	[Header( "Reposition — Placement" )]
	[Tooltip( "Max downward probe distance from the floating sorter to find a floor." )]
	[Min( 0.25f )]
	public float floorSnapDistance = 2.5f;

	[Tooltip( "Minimum surface up-dot for valid machine placement (stricter than treasure floor)." )]
	[Range( 0.5f, 1f )]
	public float minFloorUpDot = 0.7f;

	[Tooltip( "Shrink placement overlap half-extents by this many metres (placement slack)." )]
	[Min( 0f )]
	public float placementBoundsShrink = 0.1f;

	[Tooltip( "Operator-side clearance box size (local +Z) so the player can stand and use the machine." )]
	public Vector3 operationalClearanceSize = new Vector3( 0.85f, 1.6f, 0.6f );

	[Tooltip( "Local-space centre of the operational clearance box relative to the station root." )]
	public Vector3 operationalClearanceCenter = new Vector3( 0f, 0.85f, 0.95f );

	[Tooltip( "Seconds to tween the real sorter into the final place pose on confirm." )]
	[Min( 0.05f )]
	public float placeTweenSeconds = 0.28f;

	[Tooltip( "How strongly place pitch/roll eases toward the floor normal (0 = upright only)." )]
	[Range( 0f, 1f )]
	public float surfaceAlignStrength = 0.25f;

	public string ResolveUpgradeId()
	{
		if ( !string.IsNullOrEmpty( upgradeId ) )
			return upgradeId;
		return DefaultUpgradeId;
	}

	public int ResolveHopperCapacity( int level )
	{
		if ( level >= 4 )
			return Mathf.Max( 1, level4HopperCapacity );
		return Mathf.Max( 1, baseHopperCapacity );
	}

	public float ResolveCoinsPerSecond( int level )
	{
		if ( level >= 3 )
			return Mathf.Max( 0.01f, level3CoinsPerSecond );
		return Mathf.Max( 0.01f, baseCoinsPerSecond );
	}

	public bool IsAutomatic( int level )
	{
		return level >= 2;
	}

	public bool RequiresCrank( int level )
	{
		return level >= 1 && level < 2;
	}

	public float ResolveMaxReserveSeconds()
	{
		return Mathf.Max( 0.25f, maxReserveSeconds );
	}

	public Color EvaluateGaugeColor( float normalized )
	{
		float t = Mathf.Clamp01( normalized );
		if ( t <= 0.5f )
			return Color.Lerp( gaugeEmptyColor, gaugeMidColor, t * 2f );
		return Color.Lerp( gaugeMidColor, gaugeFullColor, ( t - 0.5f ) * 2f );
	}
}
