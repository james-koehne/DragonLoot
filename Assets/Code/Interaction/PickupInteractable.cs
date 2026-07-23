using UnityEngine;

/// <summary>
/// Single-item pickup: prefers stealing an existing TreasureItem; otherwise definition hand-spawn.
/// </summary>
[RequireComponent( typeof( Collider ) )]
public abstract class PickupInteractable : InteractableBase
{
	[SerializeField]
	TreasureDefinition treasure;

	public TreasureDefinition Treasure => treasure;

	protected virtual void Awake()
	{
		ApplyTreasureDisplayName();
	}

	protected virtual void Reset()
	{
		ApplyTreasureDisplayName();
	}

	public override bool CanInteract( PlayerController player )
	{
		if ( !base.CanInteract( player ) )
			return false;

		return player != null && player.CanReceiveInteractable( this );
	}

	public override void Interact( PlayerController player )
	{
		if ( player == null )
			return;

		TreasureItem existing = GetComponent<TreasureItem>();
		if ( existing == null )
			existing = GetComponentInChildren<TreasureItem>();

		if ( existing != null )
		{
			if ( !player.TryReceiveTreasureItem( existing ) )
				return;

			return;
		}

		if ( !player.TryReceiveInteractable( this ) )
			return;

		gameObject.SetActive( false );
	}

	protected void ApplyTreasureDisplayName()
	{
		if ( treasure != null && !string.IsNullOrEmpty( treasure.displayName ) )
			SetInteractionName( treasure.displayName );
	}

	protected void EnsureFallbackName( string fallbackName )
	{
		if ( treasure != null && !string.IsNullOrEmpty( treasure.displayName ) )
		{
			SetInteractionName( treasure.displayName );
			return;
		}

		if ( string.IsNullOrEmpty( InteractionName ) || InteractionName == "Interactable" )
			SetInteractionName( fallbackName );
	}
}
