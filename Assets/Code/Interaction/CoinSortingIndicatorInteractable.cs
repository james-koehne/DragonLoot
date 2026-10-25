using UnityEngine;

/// <summary>
/// Info-only aim target for coin sorter status wells (energy / hopper / output).
/// Outlines on hover; no interact action.
/// </summary>
[RequireComponent( typeof( Collider ) )]
public class CoinSortingIndicatorInteractable : InteractableBase
{
	public enum IndicatorKind
	{
		Energy = 0,
		Hopper = 1,
		Output = 2,
	}

	[SerializeField]
	CoinSortingStation station;

	[SerializeField]
	IndicatorKind kind = IndicatorKind.Energy;

	public IndicatorKind Kind => kind;

	public void BindStation( CoinSortingStation owner )
	{
		station = owner;
	}

	void Awake()
	{
		if ( station == null )
			station = GetComponentInParent<CoinSortingStation>();
		SetInteractionName( ResolveTitle() );
	}

	public override bool CanInteract( PlayerController player )
	{
		return false;
	}

	public bool CanOutlineFocus( PlayerController player )
	{
		if ( !IsAvailable || player == null || station == null )
			return false;
		return station.IsUnlocked;
	}

	public override void Interact( PlayerController player )
	{
	}

	public string BuildLookDescription()
	{
		if ( station == null )
			return ResolveTitle();

		switch ( kind )
		{
			case IndicatorKind.Energy:
				return BuildEnergyDescription();
			case IndicatorKind.Hopper:
				return BuildHopperDescription();
			case IndicatorKind.Output:
				return BuildOutputDescription();
			default:
				return ResolveTitle();
		}
	}

	string ResolveTitle()
	{
		switch ( kind )
		{
			case IndicatorKind.Energy:
				return "Energy";
			case IndicatorKind.Hopper:
				return "Hopper";
			case IndicatorKind.Output:
				return "Output";
			default:
				return "Indicator";
		}
	}

	string BuildEnergyDescription()
	{
		if ( station.HasAutomatic )
			return "Energy: automatic sorting ready";

		string state;
		if ( station.IsCranking )
			state = "Charging";
		else if ( station.ReserveNormalized >= 0.98f )
			state = "Full";
		else if ( station.ReserveNormalized <= 0.001f )
			state = "Empty";
		else
			state = Mathf.RoundToInt( station.ReserveNormalized * 100f ) + "%";

		return "Energy: charge by cranking\n" + state;
	}

	string BuildHopperDescription()
	{
		string state = station.BufferedCount > 0 ? "Has coins" : "Empty";
		return "Hopper: coins ready to sort\n" + state;
	}

	string BuildOutputDescription()
	{
		string state = station.IsOutputBlocked ? "Full" : "Clear";
		return "Output: room for sorted coins\n" + state;
	}
}
