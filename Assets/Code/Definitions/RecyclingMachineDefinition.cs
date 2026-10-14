using UnityEngine;

/// <summary>
/// Tuning for the recycling machine. Asset name must be
/// <c>RecyclingMachineDefinition</c> for <see cref="GameInstance.GetDefinition{T}"/>.
/// </summary>
[CreateAssetMenu( fileName = "RecyclingMachineDefinition", menuName = "Definitions/RecyclingMachineDefinition" )]
public class RecyclingMachineDefinition : ScriptableObject
{
	public const string DefaultUpgradeId = "recycling_machine";

	[Header( "Upgrade" )]
	[Tooltip( "UpgradeDefinition.id driving L1–L5 machine capabilities." )]
	public string upgradeId = DefaultUpgradeId;

	[Header( "Hopper Capacity" )]
	[Min( 1 )]
	public int baseHopperCapacity = 4;

	[Min( 1 )]
	public int level4HopperCapacity = 8;

	[Min( 1 )]
	public int level5HopperCapacity = 12;

	[Header( "Charge" )]
	[Tooltip( "Charge added per pull-chain Interact pulse (1 = full)." )]
	[Range( 0.05f, 1f )]
	public float chargePerPull = 0.2f;

	[Tooltip( "Seconds after an Interact pulse that still counts as pulling." )]
	[Min( 0.05f )]
	public float pullHoldGrace = 0.22f;

	[Tooltip( "Chain drop distance at empty charge (metres, local Y)." )]
	[Min( 0.05f )]
	public float pullStrokeLength = 0.45f;

	[Tooltip( "Stroke length multiplier when charge is nearly full (lower = more resistance)." )]
	[Range( 0.25f, 1f )]
	public float pullStrokeMinScale = 0.55f;

	[Tooltip( "Seconds the chain hangs at the bottom of a stroke before creeping up." )]
	[Min( 0f )]
	public float pullHangSeconds = 0.08f;

	[Tooltip( "Seconds for the chain to creep partway back up after a stroke." )]
	[Min( 0.01f )]
	public float pullCreepSeconds = 0.18f;

	[Tooltip( "Fraction of stroke the chain recovers to after each pull (ratchet)." )]
	[Range( 0f, 1f )]
	public float pullCreepRecover = 0.35f;

	[Tooltip( "Seconds for snap retract when shred starts." )]
	[Min( 0.05f )]
	public float chainRetractSeconds = 0.22f;

	[Header( "Shred" )]
	[Tooltip( "Shred runtime seconds after a full charge at L1." )]
	[Min( 0.5f )]
	public float baseRuntimeSeconds = 4f;

	[Min( 0.5f )]
	public float level5RuntimeSeconds = 8f;

	[Tooltip( "Items processed per chew beat at L1–L2." )]
	[Min( 1 )]
	public int baseItemsPerCycle = 1;

	[Min( 1 )]
	public int level3ItemsPerCycle = 2;

	[Tooltip( "Seconds between chew beats at L1–L2." )]
	[Min( 0.1f )]
	public float baseChewInterval = 0.55f;

	[Min( 0.1f )]
	public float level3ChewInterval = 0.32f;

	[Tooltip( "Wind-up before rollers hit full speed." )]
	[Min( 0.05f )]
	public float shredWindupSeconds = 0.35f;

	[Tooltip( "Spin-down coda after runtime ends." )]
	[Min( 0.05f )]
	public float shredSpinDownSeconds = 0.55f;

	[Tooltip( "Hopper fill fraction that triggers heavy-load struggle." )]
	[Range( 0.4f, 1f )]
	public float heavyLoadThreshold = 0.7f;

	[Tooltip( "Roller speed scale during heavy-load struggle." )]
	[Range( 0.2f, 1f )]
	public float heavyLoadSpinScale = 0.55f;

	[Tooltip( "Seconds of heavy-load struggle at shred start." )]
	[Min( 0f )]
	public float heavyLoadSeconds = 1f;

	[Header( "Yield" )]
	[Tooltip( "Output count multiplier at L1–L3." )]
	[Min( 1f )]
	public float baseYieldMultiplier = 1f;

	[Min( 1f )]
	public float level4YieldMultiplier = 1.5f;

	[Min( 1f )]
	public float level5YieldMultiplier = 2f;

	[Header( "Intake / Output Motion" )]
	[Min( 0.05f )]
	public float intakeSnapSeconds = 0.28f;

	[Min( 0f )]
	public float intakeArcHeight = 0.35f;

	[Min( 0.05f )]
	public float chewYankSeconds = 0.18f;

	[Min( 0.05f )]
	public float outputFlightSeconds = 0.32f;

	[Min( 0f )]
	public float outputArcHeight = 0.4f;

	[Min( 0f )]
	public float outputTossForce = 1.2f;

	[Header( "Energy Gauge" )]
	public Color gaugeEmptyColor = new Color( 0.92f, 0.12f, 0.1f, 1f );
	public Color gaugeMidColor = new Color( 1f, 0.55f, 0.08f, 1f );
	public Color gaugeFullColor = new Color( 0.18f, 0.92f, 0.28f, 1f );

	public string ResolveUpgradeId()
	{
		if ( !string.IsNullOrEmpty( upgradeId ) )
			return upgradeId;
		return DefaultUpgradeId;
	}

	public int ResolveHopperCapacity( int level )
	{
		if ( level >= 5 )
			return Mathf.Max( 1, level5HopperCapacity );
		if ( level >= 4 )
			return Mathf.Max( 1, level4HopperCapacity );
		return Mathf.Max( 1, baseHopperCapacity );
	}

	public int ResolveItemsPerCycle( int level )
	{
		if ( level >= 3 )
			return Mathf.Max( 1, level3ItemsPerCycle );
		return Mathf.Max( 1, baseItemsPerCycle );
	}

	public float ResolveChewInterval( int level )
	{
		if ( level >= 3 )
			return Mathf.Max( 0.1f, level3ChewInterval );
		return Mathf.Max( 0.1f, baseChewInterval );
	}

	public float ResolveRuntimeSeconds( int level )
	{
		if ( level >= 5 )
			return Mathf.Max( 0.5f, level5RuntimeSeconds );
		float t = Mathf.Clamp01( ( level - 1 ) / 4f );
		return Mathf.Lerp( Mathf.Max( 0.5f, baseRuntimeSeconds ), Mathf.Max( 0.5f, level5RuntimeSeconds ), t );
	}

	public float ResolveYieldMultiplier( int level )
	{
		if ( level >= 5 )
			return Mathf.Max( 1f, level5YieldMultiplier );
		if ( level >= 4 )
			return Mathf.Max( 1f, level4YieldMultiplier );
		return Mathf.Max( 1f, baseYieldMultiplier );
	}

	public Color EvaluateGaugeColor( float normalized )
	{
		float t = Mathf.Clamp01( normalized );
		if ( t <= 0.5f )
			return Color.Lerp( gaugeEmptyColor, gaugeMidColor, t * 2f );
		return Color.Lerp( gaugeMidColor, gaugeFullColor, ( t - 0.5f ) * 2f );
	}
}
