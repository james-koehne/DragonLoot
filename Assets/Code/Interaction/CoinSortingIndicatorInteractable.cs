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

	const string StatusPrefix = "<size=26>";
	const string StatusSuffix = "</size>";
	const string StaticPrefix = "\n\n<color=#D8D8D8><size=18>";
	const string StaticSuffix = "</size></color>";

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
			return FormatLook( "Energy: automatic sorting ready", "Ready" );

		string state;
		if ( station.IsCranking )
			state = "Charging";
		else
			state = FormatFillStatus( station.ReserveNormalized );

		return FormatLook( "Energy: charge by cranking", state );
	}

	string BuildHopperDescription()
	{
		return FormatLook( "Hopper: coins ready to sort", FormatFillStatus( station.HopperFillNormalized ) );
	}

	string BuildOutputDescription()
	{
		return FormatLook( "Output: room for sorted coins", FormatFillStatus( station.OutputFillNormalized ) );
	}

	static string FormatFillStatus( float fill01 )
	{
		float t = Mathf.Clamp01( fill01 );
		if ( t <= 0.001f )
			return "Empty";
		if ( t >= 0.98f )
			return "Full";
		return Mathf.RoundToInt( t * 100f ) + "%";
	}

	static string FormatLook( string staticLine, string status )
	{
		return StatusPrefix + status + StatusSuffix + StaticPrefix + staticLine + StaticSuffix;
	}
}
