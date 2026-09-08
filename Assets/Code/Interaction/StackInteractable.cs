using UnityEngine;

/// <summary>
/// Multi-count pickup: each Interact removes one unit until empty.
/// </summary>
public abstract class StackInteractable : InteractableBase
{
	[SerializeField]
	TreasureDefinition treasure;

	[SerializeField]
	int remainingCount = 3;

	int _totalCount;

	public TreasureDefinition Treasure => treasure;
	public int RemainingCount => remainingCount;
	public int TotalCount => _totalCount;

	public override bool UsesPickupInteract => true;

	protected void SetTreasureDefinition( TreasureDefinition definition )
	{
		treasure = definition;
		ApplyTreasureDisplayName();
	}

	protected virtual void Awake()
	{
		_totalCount = Mathf.Max( 1, remainingCount );
		ApplyTreasureDisplayName();
	}

	protected virtual void Reset()
	{
		ApplyTreasureDisplayName();
	}

	public override bool CanInteract( PlayerController player )
	{
		if ( !base.CanInteract( player ) || remainingCount <= 0 )
			return false;

		return player != null && player.CanReceiveInteractable( this );
	}

	public override void Interact( PlayerController player )
	{
		if ( player == null || remainingCount <= 0 )
			return;

		if ( !player.TryReceiveInteractable( this ) )
			return;

		ConsumeOneUnit();
	}

	/// <summary>Decrements remaining after an external receive/steal already succeeded.</summary>
	public bool ConsumeOneUnit()
	{
		if ( remainingCount <= 0 )
			return false;

		remainingCount--;
		OnUnitTaken();
		if ( remainingCount <= 0 )
			OnEmptied();

		return true;
	}

	/// <summary>Called after a unit was successfully taken from the stack.</summary>
	protected virtual void OnUnitTaken()
	{
	}

	/// <summary>Called when the stack reaches zero. Default deactivates the GameObject.</summary>
	protected virtual void OnEmptied()
	{
		gameObject.SetActive( false );
	}

	/// <summary>Sets remaining and total count (useful after AddComponent / greybox setup).</summary>
	public virtual void InitializeCount( int count )
	{
		remainingCount = Mathf.Max( 0, count );
		_totalCount = Mathf.Max( 1, remainingCount );
	}

	/// <summary>Syncs remaining count without clamping total (used by physical stacks).</summary>
	protected void SetRemainingCount( int count )
	{
		remainingCount = Mathf.Max( 0, count );
	}

	protected void SetTotalCount( int count )
	{
		_totalCount = Mathf.Max( 0, count );
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
