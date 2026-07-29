using UnityEngine;

/// <summary>
/// Previously merged loose gems / large props back into GPU piles.
/// Disabled so thrown/placed loot stays on the stamped Gold surface and flows with physics.
/// </summary>
public static class TreasurePileLooseDeposit
{
	public static bool TryAbsorbLooseItem( TreasureItem item, Vector3 worldPos )
	{
		return false;
	}
}
