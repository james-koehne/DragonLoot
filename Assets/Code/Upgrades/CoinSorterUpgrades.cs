using UnityEngine;

/// <summary>
/// Resolves coin sorter capabilities from the split upgrade catalog
/// (automatic / speed / hopper) instead of a single monolithic level.
/// </summary>
public static class CoinSorterUpgrades
{
	public static bool HasAutomatic()
	{
		UpgradeSystem system = UpgradeSystem.Instance;
		if ( system == null )
			return false;
		return system.GetUpgradeLevel( UpgradeDefinition.CoinSorterAutomaticId ) >= 1;
	}

	/// <summary>0 = base, 1 = +25%, 2 = +50%.</summary>
	public static int SpeedLevel()
	{
		UpgradeSystem system = UpgradeSystem.Instance;
		if ( system == null )
			return 0;
		return Mathf.Clamp( system.GetUpgradeLevel( UpgradeDefinition.CoinSorterSpeedId ), 0, 2 );
	}

	public static bool HasLargerHopper()
	{
		UpgradeSystem system = UpgradeSystem.Instance;
		if ( system == null )
			return false;
		return system.GetUpgradeLevel( UpgradeDefinition.CoinSorterHopperId ) >= 1;
	}

	/// <summary>
	/// Maps legacy <c>coin_sorting_station</c> levels (L1 speed, L2 auto, L3 hopper, L4 speed2)
	/// into the split upgrades, then clears the legacy entry.
	/// </summary>
	public static void MigrateLegacyIfNeeded( UpgradeSystem system )
	{
		if ( system == null )
			return;
		system.MigrateLegacyCoinSortingStation();
	}
}
