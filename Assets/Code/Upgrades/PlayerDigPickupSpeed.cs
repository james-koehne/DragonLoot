using UnityEngine;

/// <summary>
/// Resolves the player dig / pickup speed multiplier from <see cref="UpgradeDefinition.DigPickupSpeedId"/>.
/// </summary>
public static class PlayerDigPickupSpeed
{
	public const float DefaultMultiplier = 1.5f;

	public static bool IsActive()
	{
		return ResolveMultiplier() > 1.0001f;
	}

	/// <summary>1 when locked, disabled, or missing. Otherwise the catalog multiplier (typically 1.5).</summary>
	public static float ResolveMultiplier()
	{
		UpgradeSystem system = UpgradeSystem.Instance;
		if ( system == null )
			return 1f;

		if ( !system.TryGetDefinition( UpgradeDefinition.DigPickupSpeedId, out UpgradeDefinition definition )
			|| definition == null
			|| !definition.enabled )
			return 1f;

		if ( system.GetUpgradeLevel( UpgradeDefinition.DigPickupSpeedId ) < 1 )
			return 1f;

		return definition.ResolveDigPickupSpeedMultiplier();
	}

	/// <summary>Shortens a wait / tween so the action runs at the upgrade speed.</summary>
	public static float ScaleDuration( float duration )
	{
		float multiplier = ResolveMultiplier();
		if ( multiplier <= 0.0001f )
			return duration;
		return duration / multiplier;
	}
}
