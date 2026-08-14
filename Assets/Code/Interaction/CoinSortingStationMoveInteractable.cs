using UnityEngine;

/// <summary>
/// Body/hopper focus target for telekinetic reposition. Interact() is a no-op —
/// <see cref="PlayerSorterReposition"/> owns the hold-to-lift charge.
/// </summary>
public class CoinSortingStationMoveInteractable : InteractableBase
{
	[SerializeField]
	CoinSortingStation station;

	public CoinSortingStation Station => station;

	public void BindStation( CoinSortingStation owner )
	{
		station = owner;
	}

	void Awake()
	{
		if ( station == null )
			station = GetComponentInParent<CoinSortingStation>();
		SetInteractionName( "Move Sorter" );
	}

	public override bool CanInteract( PlayerController player )
	{
		if ( !IsAvailable || player == null || station == null )
			return false;

		if ( station.IsRepositioning )
			return false;

		// Only block while the sorter is already floating/settling — charging must stay interactable
		// or the hold charge cancels on the next frame (IsBusy becomes true as soon as charging starts).
		PlayerSorterReposition reposition = player.SorterReposition;
		if ( reposition != null && reposition.IsCarrying )
			return false;

		return true;
	}

	public override void Interact( PlayerController player )
	{
		// Hold charge is owned by PlayerSorterReposition — tap/repeat Interact does nothing.
	}
}
