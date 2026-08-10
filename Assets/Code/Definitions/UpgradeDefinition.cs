using UnityEngine;

[CreateAssetMenu( fileName = "UpgradeDefinition", menuName = "Definitions/UpgradeDefinition" )]
public class UpgradeDefinition : ScriptableObject
{
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
}
