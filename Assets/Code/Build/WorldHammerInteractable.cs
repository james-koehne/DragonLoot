using FeedbackSystem;

using UnityEngine;

/// <summary>
/// World hammer prop. ContextualInteract publishes the tutorial event and flies the hammer into the hand,
/// entering build mode with a world-pickup animation (overrides the default draw toss/spin).
/// </summary>
public class WorldHammerInteractable : InteractableBase
{
	[SerializeField]
	Feedbacks onWorldGlowFeedback;

	bool _taken;

	void Reset()
	{
		SetInteractionName( "Hammer" );
	}

	void Awake()
	{
		if ( string.IsNullOrEmpty( InteractionName ) || InteractionName == "Interactable" )
			SetInteractionName( "Hammer" );
		if ( onWorldGlowFeedback == null )
		{
			Transform glow = transform.Find( "WorldGlowFeedbacks" );
			if ( glow != null )
				onWorldGlowFeedback = glow.GetComponent<Feedbacks>();
		}
	}

	void OnEnable()
	{
		if ( onWorldGlowFeedback != null && !onWorldGlowFeedback.IsPlaying )
			onWorldGlowFeedback.Play();
	}

	void OnDisable()
	{
		if ( onWorldGlowFeedback != null )
			onWorldGlowFeedback.Stop();
	}

	public override bool CanInteract( PlayerController player )
	{
		if ( !IsAvailable || player == null || !player.GameplayInputEnabled )
			return false;
		if ( _taken )
			return false;
		if ( player.IsInBuildMode )
			return false;
		return true;
	}

	public override void Interact( PlayerController player )
	{
		EventBus.Publish( new WorldHammerInteractedEvent() );

		PlayerBuildMode buildMode = player != null ? player.BuildMode : null;
		if ( buildMode == null )
			return;

		if ( buildMode.TryTakeWorldHammer( this ) )
			_taken = true;
	}
}
