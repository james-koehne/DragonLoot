using UnityEngine;

[CreateAssetMenu( fileName = "AbilityDefinition", menuName = "Definitions/AbilityDefinition" )]
public class AbilityDefinition : ScriptableObject
{
	[Header( "Identity" )]
	public string id;

	public string displayName;

	[TextArea( 2, 4 )]
	public string description;

	public Sprite icon;

	[Header( "Gameplay" )]
	[Min( 0f )]
	public float cooldown = 1f;

	[Tooltip( "Designer kill-switch. Disabled abilities never activate." )]
	public bool enabled = true;

	[Header( "Behaviour" )]
	public AbilityBehaviour behaviour;

	public string ResolveDisplayName()
	{
		if ( !string.IsNullOrEmpty( displayName ) )
			return displayName;
		if ( !string.IsNullOrEmpty( id ) )
			return id;
		return name;
	}
}
