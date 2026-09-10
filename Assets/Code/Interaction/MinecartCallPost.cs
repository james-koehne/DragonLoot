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

	static readonly int BaseColorId = Shader.PropertyToID( "_BaseColor" );
	static readonly int EmissionColorId = Shader.PropertyToID( "_EmissionColor" );
	static readonly Color PresentBaseColor = new Color( 0.18f, 0.85f, 0.22f, 1f );
	static readonly Color PresentEmissionColor = new Color( 0.4f, 2.5f, 0.5f, 1f );

	MinecartInteractable _inbound;
	bool _bound;
	bool _lampPresent;
	bool _lampReady;
	Renderer _lampRenderer;
	MaterialPropertyBlock _lampBlock;

	public MinecartTrack BoundTrack => track;

	void Reset()
	{
		SetInteractionName( "Call minecart" );
	}

	void Awake()
	{
		if ( string.IsNullOrEmpty( InteractionName ) || InteractionName == "Interactable" )
			SetInteractionName( "Call minecart" );

		Transform lamp = transform.Find( "Lamp" );
		if ( lamp != null )
			_lampRenderer = lamp.GetComponent<Renderer>();
	}

	void OnEnable()
	{
		_inbound = null;
		_lampReady = false;
		_lampPresent = false;
		PlayFeedbacks( onIdleFeedbacks );
	}

	void Start()
	{
		EnsureBoundTrack();
		RefreshLamp( playArriveSfx: false );
	}

	void Update()
	{
		if ( _inbound != null )
			return;

		RefreshLamp( playArriveSfx: false );
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
		_lampPresent = false;
		_lampReady = true;
		PlayFeedbacks( onCallFeedbacks );
	}

	public void NotifyCartArrived( MinecartInteractable lead )
	{
		if ( lead != _inbound )
			return;

		_inbound = null;
		_lampPresent = true;
		_lampReady = true;
		PlayFeedbacks( onArriveFeedbacks );
	}

	public void NotifyRecallCancelled( MinecartInteractable lead )
	{
		if ( lead != _inbound )
			return;

		_inbound = null;
		_lampReady = false;
		RefreshLamp( playArriveSfx: false );
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

	void RefreshLamp( bool playArriveSfx )
	{
		bool present = IsAnyCartAtPost();
		if ( _lampReady && present == _lampPresent )
			return;

		_lampReady = true;
		_lampPresent = present;
		if ( present )
		{
			if ( playArriveSfx )
				PlayFeedbacks( onArriveFeedbacks );
			else
				ApplyPresentLamp();
			return;
		}

		PlayFeedbacks( onIdleFeedbacks );
	}

	bool IsAnyCartAtPost()
	{
		if ( !EnsureBoundTrack() )
			return false;

		float postDistance = track.GetNearestDistance( transform.position );
		IReadOnlyList<MinecartInteractable> carts = MinecartInteractable.ActiveCarts;
		for ( int i = 0; i < carts.Count; i++ )
		{
			MinecartInteractable candidate = carts[ i ];
			if ( candidate == null || candidate.BoundTrack != track || !candidate.IsConsistLead )
				continue;

			float stop = Mathf.Max( 0.2f, candidate.RecallStopDistance );
			float sep = Mathf.Abs( track.SignedAlong( candidate.DistanceAlongTrack, postDistance ) );
			if ( sep <= stop )
				return true;
		}

		return false;
	}

	void ApplyPresentLamp()
	{
		if ( _lampRenderer == null )
			return;

		if ( _lampBlock == null )
			_lampBlock = new MaterialPropertyBlock();

		_lampRenderer.GetPropertyBlock( _lampBlock );
		_lampBlock.SetColor( BaseColorId, PresentBaseColor );
		_lampBlock.SetColor( EmissionColorId, PresentEmissionColor );
		_lampRenderer.SetPropertyBlock( _lampBlock );
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
