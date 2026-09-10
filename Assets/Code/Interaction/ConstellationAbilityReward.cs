using UnityEngine;

/// <summary>
/// Scene hook: unlocks a configured ability when this constellation is completed by the player.
/// Add on the constellation GameObject and assign <see cref="rewardAbility"/>.
/// </summary>
public class ConstellationAbilityReward : MonoBehaviour
{
	[SerializeField] GemConstellationInteractable constellation;
	[SerializeField] AbilityDefinition rewardAbility;

	bool _subscribed;

	void Awake()
	{
		if ( constellation == null )
			constellation = GetComponent<GemConstellationInteractable>();
		if ( constellation == null )
			constellation = GetComponentInParent<GemConstellationInteractable>();
	}

	void OnEnable()
	{
		Subscribe();
	}

	void OnDisable()
	{
		Unsubscribe();
	}

	void Subscribe()
	{
		if ( _subscribed )
			return;
		EventBus.Subscribe<GemConstellationCompletedEvent>( OnConstellationCompleted );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;
		EventBus.Unsubscribe<GemConstellationCompletedEvent>( OnConstellationCompleted );
		_subscribed = false;
	}

	void OnConstellationCompleted( GemConstellationCompletedEvent evt )
	{
		if ( !evt.FromPlayer )
			return;
		if ( constellation == null || evt.Constellation != constellation )
			return;
		if ( rewardAbility == null || string.IsNullOrEmpty( rewardAbility.id ) )
			return;

		AbilitySystem system = AbilitySystem.Instance;
		if ( system == null )
			return;

		system.UnlockAbility( rewardAbility.id );
	}
}
