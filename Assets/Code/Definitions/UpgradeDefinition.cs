using UnityEngine;

[CreateAssetMenu( fileName = "UpgradeDefinition", menuName = "Definitions/UpgradeDefinition" )]
public class UpgradeDefinition : ScriptableObject
{
	public const string DigPickupSpeedId = "dig_pickup_speed";

	[Header( "Identity" )]
	public string id;

	public string displayName;

	[TextArea( 2, 4 )]
	public string description;

	public Sprite icon;

	[Header( "Progression" )]
	[Min( 1 )]
	public int maxLevel = 1;

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
