using UnityEngine;

/// <summary>
/// Resolves units taken per pile interact (gameplay default 1, debug override, player upgrades).
/// </summary>
public static class TreasurePilePull
{
	public const int DefaultUnitsPerInteract = 1;

	public static int ResolveUnitsPerInteract( PlayerController player )
	{
		if ( DebugGoldPileSection.TryGetGameplayPullOverride( out int debugUnits ) )
			return debugUnits;

		// Debug carve slider above 1 = immediate multi-grab (same amount as Carve button).
		int carveAmount = DebugGoldPileSection.CarveAmount;
		if ( carveAmount > DefaultUnitsPerInteract )
			return carveAmount;

		if ( player == null )
			return DefaultUnitsPerInteract;

		PlayerTreasurePilePull pull = player.PilePull;
		if ( pull == null )
			return DefaultUnitsPerInteract;

		return pull.UnitsPerInteract;
	}
}
