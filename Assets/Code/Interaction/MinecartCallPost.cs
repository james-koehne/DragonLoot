using System.Collections.Generic;

using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Track-side interactable: E summons the nearest minecart consist on this track.
/// </summary>
[RequireComponent( typeof( Collider ) )]
public class MinecartCallPost : InteractableBase
{
	[SerializeField]
	MinecartTrack track;

	[SerializeField]
	[Min( 0.5f )]
	float trackSnapRadius = 6f;

	[SerializeField]
	Feedbacks onCallFeedbacks;

	[SerializeField]
	Feedbacks onArriveFeedbacks;

	[SerializeField]
	Feedbacks onIdleFeedbacks;

	MinecartInteractable _inbound;
	bool _bound;

	public MinecartTrack BoundTrack => track;

	void Reset()
	{
		SetInteractionName( "Call minecart" );
	}

	void Awake()
	{
		if ( string.IsNullOrEmpty( InteractionName ) || InteractionName == "Interactable" )
			SetInteractionName( "Call minecart" );
	}

	void OnEnable()
	{
		if ( _inbound == null )
			PlayFeedbacks( onIdleFeedbacks );
	}

	void Start()
	{
		EnsureBoundTrack();
	}

	public override bool CanInteract( PlayerController player )
	{
		if ( !IsAvailable || player == null )
			return false;

		if ( _inbound != null )
			return false;

		return TryFindNearestCart( player, out _ );
	}

	public override void Interact( PlayerController player )
	{
		if ( player == null || _inbound != null )
			return;

		if ( !EnsureBoundTrack() )
			return;

		MinecartInteractable cart;
		if ( !TryFindNearestCart( player, out cart ) )
			return;

		MinecartInteractable lead = cart.ConsistLead;
		float target = track.GetNearestDistance( transform.position );
		if ( !lead.BeginRecall( target, this ) )
			return;

		_inbound = lead;
		PlayFeedbacks( onCallFeedbacks );
	}

	public void NotifyCartArrived( MinecartInteractable lead )
	{
		if ( lead != _inbound )
			return;

		_inbound = null;
		PlayFeedbacks( onArriveFeedbacks );
	}

	public void NotifyRecallCancelled( MinecartInteractable lead )
	{
		if ( lead == _inbound )
		{
			_inbound = null;
			PlayFeedbacks( onIdleFeedbacks );
		}
	}

	public void EditorSetTrack( MinecartTrack value )
	{
		track = value;
	}

	public void EditorSetFeedbacks( Feedbacks call, Feedbacks arrive, Feedbacks idle )
	{
		onCallFeedbacks = call;
		onArriveFeedbacks = arrive;
		onIdleFeedbacks = idle;
	}

	static void PlayFeedbacks( Feedbacks feedbacks )
	{
		if ( feedbacks != null )
			feedbacks.Play();
	}

	bool EnsureBoundTrack()
	{
		if ( _bound && track != null && track.IsUsable )
			return true;

		if ( track == null )
		{
			MinecartTrack nearest;
			float distance;
			if ( MinecartTrack.TryGetNearest( transform.position, trackSnapRadius, out nearest, out distance ) )
				track = nearest;
		}

		_bound = track != null && track.IsUsable;
		return _bound;
	}

	bool TryFindNearestCart( PlayerController player, out MinecartInteractable cart )
	{
		cart = null;
		if ( !EnsureBoundTrack() )
			return false;

		float postDistance = track.GetNearestDistance( transform.position );
		float stop = 0.2f;
		float best = float.MaxValue;
		IReadOnlyList<MinecartInteractable> carts = MinecartInteractable.ActiveCarts;
		for ( int i = 0; i < carts.Count; i++ )
		{
			MinecartInteractable candidate = carts[ i ];
			if ( candidate == null || candidate.BoundTrack != track || !candidate.IsConsistLead )
				continue;

			MinecartInteractable lead = candidate;

			if ( player != null )
			{
				PlayerMinecartDrive drive = player.MinecartDrive;
				if ( drive != null && drive.IsDriving && drive.ActiveCart != null && drive.ActiveCart.SharesConsistWith( lead ) )
					continue;

				PlayerMinecartPush push = player.MinecartPush;
				if ( push != null && push.IsHandlingInteract && push.ActiveCart != null && push.ActiveCart.SharesConsistWith( lead ) )
					continue;
			}

			if ( lead.IsHoldPushing )
				continue;

			float sep = Mathf.Abs( track.SignedAlong( lead.DistanceAlongTrack, postDistance ) );
			if ( sep <= Mathf.Max( stop, lead.RecallStopDistance ) )
				continue;

			if ( sep >= best )
				continue;

			best = sep;
			cart = lead;
		}

		return cart != null;
	}
}
