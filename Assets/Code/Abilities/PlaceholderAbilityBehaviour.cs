using UnityEngine;

[CreateAssetMenu( fileName = "PlaceholderAbilityBehaviour", menuName = "Abilities/Placeholder Ability Behaviour" )]
public class PlaceholderAbilityBehaviour : AbilityBehaviour
{
	public override bool TryActivate( in AbilityActivationContext context )
	{
		AbilityDefinition definition = context.Definition;
		string name = definition != null ? definition.displayName : "(null)";
		string id = definition != null ? definition.id : "(null)";
		Debug.Log( $"[Ability] Activated '{name}' ({id}) on slot {context.SlotIndex + 1}." );
		return true;
	}
}
