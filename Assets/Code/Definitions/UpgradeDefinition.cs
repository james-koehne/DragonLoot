using UnityEngine;

[CreateAssetMenu( fileName = "UpgradeDefinition", menuName = "Definitions/UpgradeDefinition" )]
public class UpgradeDefinition : ScriptableObject
{
	public const string DigPickupSpeedId = "dig_pickup_speed";
	public const string CoinSorterAutomaticId = "coin_sorter_automatic";
	public const string CoinSorterSpeedId = "coin_sorter_speed";
	public const string CoinSorterHopperId = "coin_sorter_hopper";
	public const string LegacyCoinSortingStationId = "coin_sorting_station";

	[Header( "Identity" )]
	public string id;

	public string displayName;

	[TextArea( 2, 4 )]
	public string description;

	public Sprite icon;

	[Header( "Progression" )]
	[Min( 1 )]
	public int maxLevel = 1;

	[Tooltip( "Optional per-level unlock toast titles (index 0 = level 1). Empty slot falls back to displayName." )]
	public string[] levelUnlockLabels;

	[Tooltip( "Designer kill-switch. Disabled upgrades stay locked to gameplay consumers." )]
	public bool enabled = true;

	[Header( "Dig / Pickup Speed" )]
	[Tooltip( "Multiplier applied to digging and pickup speed when this upgrade is at level 1+. 1 = no change. Used by the dig_pickup_speed upgrade." )]
	[Min( 0.01f )]
	public float digPickupSpeedMultiplier = 1f;

	public string ResolveDisplayName()
	{
		if ( !string.IsNullOrEmpty( displayName ) )
			return displayName;
		if ( !string.IsNullOrEmpty( id ) )
			return id;
		return name;
	}

	/// <summary>Toast title for unlocking/reaching <paramref name="level"/> (1-based).</summary>
	public string ResolveUnlockToastTitle( int level )
	{
		if ( levelUnlockLabels != null && level >= 1 && level <= levelUnlockLabels.Length )
		{
			string label = levelUnlockLabels[ level - 1 ];
			if ( !string.IsNullOrEmpty( label ) )
				return label;
		}

		return ResolveDisplayName();
	}

	public int ResolveMaxLevel()
	{
		return Mathf.Max( 1, maxLevel );
	}

	public float ResolveDigPickupSpeedMultiplier()
	{
		return Mathf.Max( 0.01f, digPickupSpeedMultiplier );
	}

	void OnValidate()
	{
		maxLevel = Mathf.Max( 1, maxLevel );
		digPickupSpeedMultiplier = Mathf.Max( 0.01f, digPickupSpeedMultiplier );
	}
}
