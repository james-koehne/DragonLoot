/// <summary>
/// Published when an ability is newly unlocked via <see cref="AbilitySystem.UnlockAbility"/>.
/// </summary>
public struct AbilityUnlockedEvent
{
	public string AbilityId;
	public AbilityDefinition Definition;
}
