using System.Collections.Generic;

using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Shared dock + mixed-storage transfer logic for minecart input/output stations.
/// </summary>
[RequireComponent( typeof( Collider ) )]
public abstract class MinecartStationBase : InteractableBase
{
	[SerializeField]
	MinecartTrack track;

	[SerializeField]
	MinecartStationDefinition definition;

	[SerializeField]
	MixedDisplayTableInteractable storage;

	[SerializeField]
	[Min( 0.5f )]
	float trackSnapRadius = 6f;

	[SerializeField]
	Feedbacks onCallFeedbacks;

	[SerializeField]
	Feedbacks onArriveFeedbacks;

	[SerializeField]
	Feedbacks onIdleFeedbacks;

	static readonly List<MinecartStationBase> All = new List<MinecartStationBase>( 8 );
	static readonly int BaseColorId = Shader.PropertyToID( "_BaseColor" );
	static readonly int EmissionColorId = Shader.PropertyToID( "_EmissionColor" );
	static readonly Color PresentBaseColor = new Color( 0.18f, 0.85f, 0.22f, 1f );
	static readonly Color PresentEmissionColor = new Color( 0.4f, 2.5f, 0.5f, 1f );
	static readonly Color CallingBaseColor = new Color( 0.95f, 0.72f, 0.12f, 1f );
	static readonly Color CallingEmissionColor = new Color( 2.2f, 1.4f, 0.2f, 1f );

	protected MinecartInteractable DockedCart { get; private set; }
	protected MinecartInteractable InboundCart { get; private set; }

	bool _bound;
	float _transferTimer;
	float _inactivityTimer;
	bool _jobCompleteWaiting;
	Renderer _lampRenderer;
	MaterialPropertyBlock _lampBlock;
	bool _lampCalling;

	public static IReadOnlyList<MinecartStationBase> ActiveStations => All;

	public MinecartTrack BoundTrack => track;

	public MixedDisplayTableInteractable Storage => storage;

	public float TrackStopDistance
	{
		get
		{
			EnsureBoundTrack();
			return track != null ? track.GetNearestDistance( transform.position ) : 0f;
		}
	}

	protected float TransferInterval => definition != null ? definition.transferInterval : 0.25f;

	protected float InactivitySeconds => definition != null ? definition.inactivitySeconds : 10f;

	public float ResolvedInactivitySeconds => InactivitySeconds;

	protected float TrackSnapRadius => definition != null ? definition.trackSnapRadius : trackSnapRadius;

	protected bool HasDockedCart => DockedCart != null;

	protected bool HasInboundCart => InboundCart != null;

	protected virtual bool ShowsCallVisualization => false;

	protected abstract bool TryTransferOnce( MinecartInteractable cart );

	protected abstract bool ShouldLeaveAfterTransfer( MinecartInteractable cart );

	protected abstract void OnForcedOrIdleLeave( MinecartInteractable cart, MinecartAutoController auto );

	protected virtual void Awake()
	{
		if ( string.IsNullOrEmpty( InteractionName ) || InteractionName == "Interactable" )
			SetInteractionName( "Send cart" );

		if ( storage == null )
			storage = GetComponentInChildren<MixedDisplayTableInteractable>( true );

		Transform lamp = transform.Find( "Lamp" );
		if ( lamp != null )
			_lampRenderer = lamp.GetComponent<Renderer>();
	}

	protected virtual void OnEnable()
	{
		if ( !All.Contains( this ) )
			All.Add( this );

		InboundCart = null;
		DockedCart = null;
		_transferTimer = 0f;
		_inactivityTimer = 0f;
		_jobCompleteWaiting = false;
		_lampCalling = false;
		PlayFeedbacks( onIdleFeedbacks );
	}

	protected virtual void OnDisable()
	{
		All.Remove( this );
	}

	protected virtual void Start()
	{
		EnsureBoundTrack();
	}

	protected virtual void Update()
	{
		EnsureBoundTrack();
		TickDockedTransfer();
		TickCallVisualization();
	}

	public override bool CanInteract( PlayerController player )
	{
		return IsAvailable && player != null && DockedCart != null;
	}

	public override void Interact( PlayerController player )
	{
		RequestForcedLeave();
	}

	public void RequestForcedLeave()
	{
		if ( DockedCart == null )
			return;

		MinecartAutoController auto = DockedCart.GetComponent<MinecartAutoController>();
		if ( auto == null )
			return;

		OnForcedOrIdleLeave( DockedCart, auto );
	}

	/// <summary>Player placed cargo while docked — refresh the leave inactivity timer.</summary>
	public void NotifyPlayerCargoOnDockedCart()
	{
		_inactivityTimer = 0f;
		_jobCompleteWaiting = false;
		_transferTimer = 0f;
	}

	public void NotifyCartDocked( MinecartInteractable cart )
	{
		if ( cart == null )
			return;

		if ( InboundCart == cart )
			InboundCart = null;

		DockedCart = cart;
		_transferTimer = 0f;
		_inactivityTimer = 0f;
		_jobCompleteWaiting = false;
		_lampCalling = false;
		ApplyPresentLamp();
		PlayFeedbacks( onArriveFeedbacks );
	}

	public void NotifyCartUndocked( MinecartInteractable cart )
	{
		if ( DockedCart == cart )
			DockedCart = null;

		_transferTimer = 0f;
		_inactivityTimer = 0f;
		_jobCompleteWaiting = false;
		_lampCalling = false;
		PlayFeedbacks( onIdleFeedbacks );
	}

	public void NotifyInboundCancelled( MinecartInteractable cart )
	{
		if ( InboundCart == cart )
			InboundCart = null;

		_lampCalling = false;
		PlayFeedbacks( onIdleFeedbacks );
	}

	protected bool TryBeginCall( MinecartInteractable cart )
	{
		if ( cart == null || HasDockedCart || HasInboundCart )
			return false;

		MinecartAutoController auto = cart.GetComponent<MinecartAutoController>();
		if ( auto == null || !auto.IsAvailableForCall )
			return false;

		if ( !auto.TryDispatchTo( this ) )
			return false;

		InboundCart = cart;
		_lampCalling = true;
		ApplyCallingLamp();
		PlayFeedbacks( onCallFeedbacks );
		return true;
	}

	/// <summary>Player / external send: claim this station and dispatch the cart if free.</summary>
	public bool TryDispatchCart( MinecartInteractable cart )
	{
		return TryBeginCall( cart );
	}

	protected MinecartInteractable FindNearestAvailableCart()
	{
		if ( !EnsureBoundTrack() )
			return null;

		float best = float.MaxValue;
		MinecartInteractable bestCart = null;
		IReadOnlyList<MinecartInteractable> carts = MinecartInteractable.ActiveCarts;
		for ( int i = 0; i < carts.Count; i++ )
		{
			MinecartInteractable candidate = carts[ i ];
			if ( candidate == null || !candidate.IsConsistLead )
				continue;

			if ( candidate.IsDriveCart )
				continue;

			MinecartAutoController auto = candidate.GetComponent<MinecartAutoController>();
			if ( auto == null || !auto.IsAvailableForCall )
				continue;

			if ( candidate.BoundTrack == null )
				continue;

			float cost;
			if ( !MinecartNetworkPathfinder.TryEstimateCost(
				    candidate.BoundTrack,
				    candidate.DistanceAlongTrack,
				    track,
				    TrackStopDistance,
				    out cost ) )
				continue;

			if ( cost >= best )
				continue;

			best = cost;
			bestCart = candidate;
		}

		return bestCart;
	}

	void TickDockedTransfer()
	{
		MinecartInteractable cart = DockedCart;
		if ( cart == null )
			return;

		MinecartAutoController auto = cart.GetComponent<MinecartAutoController>();
		if ( auto == null || !auto.IsDockedAt( this ) )
		{
			DockedCart = null;
			return;
		}

		_transferTimer += Time.deltaTime;
		_inactivityTimer += Time.deltaTime;

		// After the transfer job finishes, wait inactivitySeconds before leaving.
		if ( _jobCompleteWaiting )
		{
			if ( _inactivityTimer >= InactivitySeconds )
				OnForcedOrIdleLeave( cart, auto );
			return;
		}

		if ( _inactivityTimer >= InactivitySeconds )
		{
			OnForcedOrIdleLeave( cart, auto );
			return;
		}

		if ( ShouldLeaveAfterTransfer( cart ) )
		{
			BeginJobCompleteWait();
			return;
		}

		if ( IsTransferBusy( cart ) )
			return;

		if ( _transferTimer < TransferInterval )
			return;

		_transferTimer = 0f;
		if ( TryTransferOnce( cart ) )
		{
			_inactivityTimer = 0f;
			if ( ShouldLeaveAfterTransfer( cart ) )
				BeginJobCompleteWait();
		}
		else if ( ShouldLeaveAfterTransfer( cart ) )
			BeginJobCompleteWait();
		else if ( !CanTransferProgress( cart ) )
			OnForcedOrIdleLeave( cart, auto );
	}

	void BeginJobCompleteWait()
	{
		_jobCompleteWaiting = true;
		_inactivityTimer = 0f;
		_transferTimer = 0f;
	}

	bool IsTransferBusy( MinecartInteractable cart )
	{
		if ( cart != null && cart.HasTransferInFlight )
			return true;

		MixedDisplayTableInteractable table = storage;
		if ( table == null )
			return false;

		IReadOnlyList<TreasureItem> displayed = table.DisplayedItems;
		if ( displayed == null )
			return false;

		for ( int i = 0; i < displayed.Count; i++ )
		{
			TreasureItem item = displayed[ i ];
			if ( item != null && item.IsInFlight )
				return true;
		}

		return false;
	}

	protected virtual bool CanTransferProgress( MinecartInteractable cart )
	{
		return true;
	}

	void TickCallVisualization()
	{
		if ( !ShowsCallVisualization )
			return;

		if ( HasInboundCart )
		{
			if ( !_lampCalling )
			{
				_lampCalling = true;
				ApplyCallingLamp();
			}

			return;
		}

		if ( HasDockedCart )
			return;

		if ( _lampCalling )
		{
			_lampCalling = false;
			PlayFeedbacks( onIdleFeedbacks );
		}
	}

	protected bool EnsureBoundTrack()
	{
		MinecartJunctionGraph graph = MinecartJunctionGraph.FindActive();
		if ( track != null && track.IsTravelReady && IsTrackInJunctionGraph( graph, track ) )
		{
			_bound = true;
			return true;
		}

		float radius = Mathf.Max( TrackSnapRadius, 12f );
		MinecartTrack graphTrack;
		float graphDistance;
		if ( TryGetNearestGraphTrack( transform.position, radius, graph, out graphTrack, out graphDistance ) )
		{
			track = graphTrack;
			_bound = true;
			return true;
		}

		if ( track != null && track.IsTravelReady )
		{
			_bound = true;
			return true;
		}

		MinecartTrack nearest;
		float distance;
		if ( MinecartTrack.TryGetNearest( transform.position, radius, out nearest, out distance ) )
		{
			track = nearest;
			_bound = track != null && track.IsTravelReady;
			return _bound;
		}

		_bound = false;
		return false;
	}

	static bool IsTrackInJunctionGraph( MinecartJunctionGraph graph, MinecartTrack candidate )
	{
		if ( graph == null || candidate == null )
			return false;

		List<float> distances = new List<float>( 4 );
		List<int> junctions = new List<int>( 4 );
		graph.CollectPortsOnTrack( candidate, distances, junctions );
		return distances.Count > 0;
	}

	static bool TryGetNearestGraphTrack(
		Vector3 worldPosition,
		float maxRadius,
		MinecartJunctionGraph graph,
		out MinecartTrack track,
		out float distance )
	{
		track = null;
		distance = 0f;
		if ( graph == null )
			return false;

		float best = maxRadius >= 0f ? maxRadius * maxRadius : float.MaxValue;
		bool found = false;
		IReadOnlyList<MinecartTrack> tracks = MinecartTrack.ActiveTracks;
		for ( int i = 0; i < tracks.Count; i++ )
		{
			MinecartTrack candidate = tracks[ i ];
			if ( candidate == null || !candidate.isActiveAndEnabled || !candidate.IsTravelReady )
				continue;

			if ( !IsTrackInJunctionGraph( graph, candidate ) )
				continue;

			float candDistance;
			Vector3 nearest;
			if ( !candidate.TryGetNearestPoint( worldPosition, out candDistance, out nearest ) )
				continue;

			float sqr = ( nearest - worldPosition ).sqrMagnitude;
			if ( sqr >= best )
				continue;

			best = sqr;
			track = candidate;
			distance = candDistance;
			found = true;
		}

		return found;
	}

	public bool EnsureBoundTrackPublic()
	{
		return EnsureBoundTrack();
	}

	void ApplyPresentLamp()
	{
		ApplyLampColors( PresentBaseColor, PresentEmissionColor );
	}

	void ApplyCallingLamp()
	{
		ApplyLampColors( CallingBaseColor, CallingEmissionColor );
	}

	void ApplyLampColors( Color baseColor, Color emission )
	{
		if ( _lampRenderer == null )
			return;

		if ( _lampBlock == null )
			_lampBlock = new MaterialPropertyBlock();

		_lampRenderer.GetPropertyBlock( _lampBlock );
		_lampBlock.SetColor( BaseColorId, baseColor );
		_lampBlock.SetColor( EmissionColorId, emission );
		_lampRenderer.SetPropertyBlock( _lampBlock );
	}

	static void PlayFeedbacks( Feedbacks feedbacks )
	{
		if ( feedbacks != null )
			feedbacks.Play();
	}

	public void EditorSetTrack( MinecartTrack value )
	{
		track = value;
	}

	public void EditorSetDefinition( MinecartStationDefinition value )
	{
		definition = value;
	}

	public void EditorSetStorage( MixedDisplayTableInteractable value )
	{
		storage = value;
	}

	public void EditorSetFeedbacks( Feedbacks call, Feedbacks arrive, Feedbacks idle )
	{
		onCallFeedbacks = call;
		onArriveFeedbacks = arrive;
		onIdleFeedbacks = idle;
	}
}
