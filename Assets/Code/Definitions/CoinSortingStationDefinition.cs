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
	public float crankHoldGrace = 0.35f;

	[Header( "Output" )]
	[Tooltip( "World-space lateral step when starting a new stack beside a full chute stack." )]
	[Min( 0.05f )]
	public float fullStackLateralOffset = 0.35f;

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
}
