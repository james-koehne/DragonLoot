using System.Collections;
using UnityEngine;

/// <summary>
/// Scene hook: unlocks a configured ability when this constellation is completed by the player.
/// Add on the constellation GameObject and assign <see cref="rewardAbility"/>.
/// </summary>
public class ConstellationAbilityReward : MonoBehaviour
{
	const float RewardDelaySeconds = 1.75f;

	[SerializeField] GemConstellationInteractable constellation;
	[SerializeField] AbilityDefinition rewardAbility;

	bool _subscribed;
	bool _rewardAnnounced;
	Coroutine _rewardRoutine;

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
		if ( _rewardRoutine != null )
		{
			StopCoroutine( _rewardRoutine );
			_rewardRoutine = null;
		}
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
		if ( _rewardAnnounced || _rewardRoutine != null )
			return;

		_rewardRoutine = StartCoroutine( GrantRewardAfterDelayRoutine() );
	}

	IEnumerator GrantRewardAfterDelayRoutine()
	{
		yield return new WaitForSeconds( RewardDelaySeconds );
		_rewardRoutine = null;

		if ( _rewardAnnounced )
			yield break;
		if ( rewardAbility == null || string.IsNullOrEmpty( rewardAbility.id ) )
			yield break;

		AbilitySystem system = AbilitySystem.Instance;
		if ( system == null )
			yield break;

		system.UnlockAbility( rewardAbility.id );

		if ( _rewardAnnounced )
			yield break;

		_rewardAnnounced = true;
		UnlockRewardToastUI.NotifyUnlock( rewardAbility );
	}
}
