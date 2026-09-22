using UnityEngine;

/// <summary>
/// Contextual (E) interactable for the fairy helper companion.
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
		if ( helper != null && helper.IsIntroActive )
			return false;
		return IsAvailable && player != null && helper != null;
	}

	public override void Interact( PlayerController player )
	{
		if ( helper == null || player == null )
			return;
		if ( helper.IsIntroActive )
			return;
		if ( Time.unscaledTime < _nextTalkAllowedAt )
			return;

		_nextTalkAllowedAt = Time.unscaledTime + talkCooldownSeconds;
		helper.NotifyTalkStarted();
		helper.PlayInteractFeedback();

		string line = ResolveTalkLine();
		if ( !string.IsNullOrEmpty( line ) )
			helper.Say( line );

		CancelInvoke( nameof( EndTalk ) );
		Invoke( nameof( EndTalk ), 2.2f );
	}

	public string ResolvePromptLabel()
	{
		if ( helper == null )
			return "Talk";

		if ( helper.NearbyPile != null )
			return "Ask about this pile";
		if ( helper.PerchedDisplay != null )
			return "Ask about this display";
		return "Talk";
	}

	string ResolveTalkLine()
	{
		// Prefer the pile the fairy is over, then perched display, then free-roam hints.
		TreasurePileVisual pile = helper.NearbyPile;
		if ( pile != null )
			return BuildPileProgressLine( pile );

		Component display = helper.PerchedDisplay;
		if ( display != null )
			return BuildDisplayProgressLine( display );

		if ( helper.TryBuildHintTalkLine( out string hint ) && !string.IsNullOrEmpty( hint ) )
			return hint;

		return "Need a hand? Look at me near a display or gold pile.";
	}

	string BuildDisplayProgressLine( Component display )
	{
		if ( FairyHelper.TryGetDisplayProgressCounts( display, out int current, out int capacity, out bool complete ) )
		{
			int pct = FormatPercent( current, capacity );
			if ( complete || ( capacity > 0 && current >= capacity ) )
				return pct + "% complete, this display is full.";
		}

		if ( helper.TryBuildDisplayProgress( display, out string progress ) )
			return progress;
		return "This display is coming along.";
	}

	static string BuildPileProgressLine( TreasurePileVisual pile )
	{
		int remaining = pile.TotalRemainingLoot;
		int initial = pile.TotalInitialLoot;
		if ( initial <= 0 )
			initial = remaining;

		int cleared = Mathf.Max( 0, initial - remaining );
		int pct = FormatPercent( cleared, initial );

		if ( remaining <= 0 )
			return "100% cleared";
		if ( remaining == 1 )
			return pct + "% cleared, 1 piece left in this pile.";
		return pct + "% cleared, " + remaining + " pieces left in this pile.";
	}

	static int FormatPercent( int numerator, int denominator )
	{
		if ( denominator <= 0 )
			return numerator > 0 ? 100 : 0;
		return Mathf.Clamp( Mathf.RoundToInt( 100f * numerator / denominator ), 0, 100 );
	}

	void EndTalk()
	{
		if ( helper != null )
			helper.NotifyTalkEnded();
	}
}
