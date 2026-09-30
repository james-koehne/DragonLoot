using UnityEngine;

/// <summary>
/// Contextual (E) interactable for fairy helpers (world / idle talk).
/// </summary>
[RequireComponent( typeof( Collider ) )]
public class FairyInteractable : InteractableBase
{
	[SerializeField]
	FairyHelper helper;

	[SerializeField]
	[Min( 0.5f )]
	float talkCooldownSeconds = 0.75f;

	float _nextTalkAllowedAt = -1f;

	void Reset()
	{
		SetInteractionName( "Talk" );
	}

	void Awake()
	{
		if ( helper == null )
			helper = GetComponent<FairyHelper>();
		if ( string.IsNullOrEmpty( InteractionName ) || InteractionName == "Interactable" )
			SetInteractionName( "Talk" );
	}

	public override bool CanInteract( PlayerController player )
	{
		if ( helper == null || !helper.CanAcceptTalk )
			return false;
		return IsAvailable && player != null;
	}

	public override void Interact( PlayerController player )
	{
		if ( helper == null || player == null )
			return;
		if ( !helper.CanAcceptTalk )
			return;
		if ( Time.unscaledTime < _nextTalkAllowedAt )
			return;

		_nextTalkAllowedAt = Time.unscaledTime + talkCooldownSeconds;
		helper.NotifyTalkStarted();
		helper.PlayInteractFeedback();

		string line = helper.ResolveTalkLine();
		if ( !string.IsNullOrEmpty( line ) )
			helper.Say( line );

		CancelInvoke( nameof( EndTalk ) );
		Invoke( nameof( EndTalk ), 2.2f );
	}

	public string ResolvePromptLabel()
	{
		return "Talk";
	}

	void EndTalk()
	{
		if ( helper != null )
			helper.NotifyTalkEnded();
	}
}
