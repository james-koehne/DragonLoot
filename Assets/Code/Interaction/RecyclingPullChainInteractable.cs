using UnityEngine;

/// <summary>
/// Hanging pull-chain starter. Hold ContextualInteract (E) or Interact (LMB)
/// to pulse charge into <see cref="RecyclingMachine"/>.
/// </summary>
[RequireComponent( typeof( Collider ) )]
public class RecyclingPullChainInteractable : InteractableBase
{
	[SerializeField]
	RecyclingMachine station;

	public override bool AcceptsPrimaryInteract => true;

	public void BindStation( RecyclingMachine owner )
	{
		station = owner;
	}

	void Awake()
	{
		if ( station == null )
			station = GetComponentInParent<RecyclingMachine>();
		SetInteractionName( "Pull Chain" );
	}

	public override bool CanInteract( PlayerController player )
	{
		if ( !IsAvailable || player == null || station == null )
			return false;
		return station.CanAcceptPull;
	}

	public override void Interact( PlayerController player )
	{
		if ( station == null )
			return;
		station.NotifyPullPulse();
	}
}
