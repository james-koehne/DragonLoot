using UnityEngine;

/// <summary>
/// Manual crank for level-1 coin sorting. Hold Interact to keep processing active.
/// Hidden (non-interactable) once the station is automatic (level 2+).
/// </summary>
[RequireComponent( typeof( Collider ) )]
public class CoinSortingCrankInteractable : InteractableBase
{
	[SerializeField]
	CoinSortingStation station;

	public void BindStation( CoinSortingStation owner )
	{
		station = owner;
	}

	void Awake()
	{
		if ( station == null )
			station = GetComponentInParent<CoinSortingStation>();
		SetInteractionName( "Crank Sorter" );
	}

	public override bool CanInteract( PlayerController player )
	{
		if ( !IsAvailable || player == null || station == null )
			return false;

		if ( station.IsRepositioning )
			return false;

		PlayerSorterReposition reposition = player.SorterReposition;
		if ( reposition != null && reposition.IsCarrying )
			return false;

		int level = station.StationLevel;
		if ( level >= 2 )
			return false;

		return level >= 1 && station.BufferedCount > 0;
	}

	public override void Interact( PlayerController player )
	{
		if ( station == null )
			return;

		station.NotifyCrankPulse();
	}
}
