using UnityEngine;

public abstract class AbilityBehaviour : ScriptableObject
{
	/// <summary>Returns true when the ability effect was applied successfully.</summary>
	public abstract bool TryActivate( in AbilityActivationContext context );
}
