using UnityEngine;

[CreateAssetMenu( fileName = "LockpickAbilityBehaviour", menuName = "Abilities/Lockpick Ability Behaviour" )]
public class LockpickAbilityBehaviour : AbilityBehaviour
{
	[Tooltip( "Fallback lockpick duration when the chest definition leaves duration at 0." )]
	[Min( 0.05f )]
	public float defaultDuration = 8f;

	public override bool TryActivate( in AbilityActivationContext context )
	{
		PlayerController player = context.Player;
		if ( player == null )
			return false;

		ChestInteractable chest = ResolveTargetChest( player );
		if ( chest == null )
			return false;

		return chest.TryBeginLockpick( player );
	}

	static ChestInteractable ResolveTargetChest( PlayerController player )
	{
		PlayerInteraction interaction = player.Interaction;
		if ( interaction == null )
			return null;

		if ( interaction.Current != null )
		{
			ChestInteractable focused = ChestInteractable.ResolveFromInteractable( interaction.Current );
			if ( focused != null )
				return focused;
		}

		return null;
	}
}
