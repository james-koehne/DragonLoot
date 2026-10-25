using UnityEngine;

/// <summary>
/// Crank for the coin sorter. Hold ContextualInteract (E) or Interact (LMB)
/// to sort at L0–L1, or boost automatic sorting at L2+.
/// </summary>
[RequireComponent( typeof( Collider ) )]
public class CoinSortingCrankInteractable : InteractableBase
{
	[SerializeField]
	CoinSortingStation station;

	public override bool AcceptsPrimaryInteract => true;

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
		if ( !base.CanInteract( player ) || station == null )
			return false;

		if ( station.IsRepositioning )
			return false;

		PlayerSorterReposition reposition = player.SorterReposition;
		if ( reposition != null && reposition.IsCarrying )
			return false;

		CoinSortingStationDefinition def = station.Definition;
		return def != null && def.AllowsCrank();
	}

	public override void Interact( PlayerController player )
	{
		if ( station == null )
			return;

		station.NotifyCrankPulse();
	}
}
