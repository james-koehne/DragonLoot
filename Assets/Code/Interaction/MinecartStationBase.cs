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
	[Tooltip( "World pose projected onto the bound track for auto-cart docking. Defaults to this transform." )]
	Transform dockPoint;

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
	static readonly int ColorId = Shader.PropertyToID( "_Color" );
	static readonly int EmissionColorId = Shader.PropertyToID( "_EmissionColor" );
	static readonly int EmissionIntensityId = Shader.PropertyToID( "_EmissionIntensity" );
	static readonly Color PresentBaseColor = new Color( 0.18f, 0.85f, 0.22f, 1f );
	static readonly Color PresentEmissionColor = new Color( 0.25f, 0.75f, 0.3f, 1f );
	static readonly Color CallingBaseColor = new Color( 0.95f, 0.72f, 0.12f, 1f );
	static readonly Color CallingEmissionColor = new Color( 0.85f, 0.6f, 0.15f, 1f );
	const float LampEmissionBase = 0.55f;
	const float LampEmissionIntensity = LampEmissionBase * 3f;

	protected MinecartInteractable DockedCart { get; private set; }
	protected MinecartInteractable InboundCart { get; private set; }

	bool _bound;
	float _transferTimer;
	float _inactivityTimer;
	bool _jobCompleteWaiting;
	Renderer _lampRenderer;
	MaterialPropertyBlock _lampBlock;
	MaterialPropertyBlock _roleBlock;
	bool _lampCalling;

	public static IReadOnlyList<MinecartStationBase> ActiveStations => All;

	/// <summary>When false, stations will not auto-call or auto-route carts.</summary>
	public static bool AutomationEnabled { get; private set; } = true;

	public MinecartTrack BoundTrack => track;

	public MixedDisplayTableInteractable Storage => storage;

	/// <summary>World position used when projecting the station onto the track for auto docking.</summary>
	public Vector3 DockWorldPosition => dockPoint != null ? dockPoint.position : transform.position;

	public float TrackStopDistance
	{
		get
		{
			EnsureBoundTrack();
			return track != null ? track.GetNearestDistance( DockWorldPosition ) : 0f;
		}
	}

	protected float TransferInterval => definition != null ? definition.transferInterval : 0.25f;

	protected float InactivitySeconds => definition != null ? definition.inactivitySeconds : 10f;

	public float ResolvedInactivitySeconds => InactivitySeconds;

	protected float TrackSnapRadius => definition != null ? definition.trackSnapRadius : trackSnapRadius;

	protected bool HasDockedCart => DockedCart != null;

	protected bool HasInboundCart => InboundCart != null;

	protected virtual bool ShowsCallVisualization => false;

	/// <summary>Prefab role-marker tint (arrow / RoleLamp). Call lamp stays separate.</summary>
	protected virtual Color RoleTint => Color.white;

	protected virtual string DefaultInteractionName => "Send cart";

	protected abstract bool TryTransferOnce( MinecartInteractable cart );

	protected abstract bool ShouldLeaveAfterTransfer( MinecartInteractable cart );

	protected abstract void OnForcedOrIdleLeave( MinecartInteractable cart, MinecartAutoController auto );

	protected virtual void Awake()
	{
		if ( string.IsNullOrEmpty( InteractionName )
		     || InteractionName == "Interactable"
		     || InteractionName == "Send cart" )
			SetInteractionName( DefaultInteractionName );

		if ( storage == null )
			storage = GetComponentInChildren<MixedDisplayTableInteractable>( true );

		if ( dockPoint == null )
		{
			Transform found = transform.Find( "DockPoint" );
			if ( found != null )
				dockPoint = found;
		}

		Transform lamp = transform.Find( "Lamp" );
		if ( lamp != null )
		{
			_lampRenderer = lamp.GetComponent<Renderer>();
			EnsureEmissionEnabled( _lampRenderer );
		}

		ApplyRoleTint();
		ApplyIdleLamp();
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
		ApplyIdleLamp();
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

	/// <summary>Stop every automated cart immediately and ignore further automation until resumed.</summary>
	public static void StopAllAutomation()
	{
		AutomationEnabled = false;

		IReadOnlyList<MinecartInteractable> carts = MinecartInteractable.ActiveCarts;
		for ( int i = 0; i < carts.Count; i++ )
		{
			MinecartInteractable cart = carts[ i ];
			if ( cart == null || !cart.IsConsistLead )
				continue;

			MinecartAutoController auto = cart.GetComponent<MinecartAutoController>();
			if ( auto != null )
				auto.ForceHaltToIdle();
		}

		for ( int i = 0; i < All.Count; i++ )
		{
			MinecartStationBase station = All[ i ];
			if ( station != null )
				station.ClearAutomationReservations();
		}
	}

	public static void ResumeAutomation()
	{
		AutomationEnabled = true;
	}

	/// <summary>
	/// Green button: if a cart is docked, force leave; otherwise dispatch the nearest available cart here.
	/// </summary>
	public virtual void RequestImmediateSend()
	{
		if ( DockedCart != null )
		{
			RequestForcedLeave();
			return;
		}

		MinecartInteractable cart = FindNearestAvailableCart();
		if ( cart != null )
			TryBeginCall( cart );
	}

	protected static bool HasActiveInputStation()
	{
		IReadOnlyList<MinecartStationBase> stations = ActiveStations;
		for ( int i = 0; i < stations.Count; i++ )
		{
			if ( stations[ i ] is MinecartInputStation )
				return true;
		}

		return false;
	}

	void ClearAutomationReservations()
	{
		InboundCart = null;
		_lampCalling = false;
		PlayFeedbacks( onIdleFeedbacks );
		ApplyIdleLamp();
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
		ApplyIdleLamp();
	}

	public void NotifyInboundCancelled( MinecartInteractable cart )
	{
		if ( InboundCart == cart )
			InboundCart = null;

		_lampCalling = false;
		PlayFeedbacks( onIdleFeedbacks );
		ApplyIdleLamp();
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
			ApplyIdleLamp();
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
		Vector3 dockPos = DockWorldPosition;
		MinecartTrack graphTrack;
		float graphDistance;
		if ( TryGetNearestGraphTrack( dockPos, radius, graph, out graphTrack, out graphDistance ) )
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
		if ( MinecartTrack.TryGetNearest( dockPos, radius, out nearest, out distance ) )
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

	void ApplyIdleLamp()
	{
		if ( _lampRenderer == null )
			return;

		if ( HasDockedCart || HasInboundCart )
			return;

		Color tint = RoleTint;
		if ( tint.a <= 0f || tint == Color.white )
			tint = CallingBaseColor;

		ApplyLampColors( tint, tint );
	}

	void ApplyLampColors( Color baseColor, Color emission )
	{
		if ( _lampRenderer == null )
			return;

		EnsureEmissionEnabled( _lampRenderer );

		if ( _lampBlock == null )
			_lampBlock = new MaterialPropertyBlock();

		_lampRenderer.GetPropertyBlock( _lampBlock );
		_lampBlock.SetColor( BaseColorId, baseColor );
		_lampBlock.SetColor( ColorId, baseColor );
		_lampBlock.SetColor( EmissionColorId, emission );
		_lampBlock.SetFloat( EmissionIntensityId, LampEmissionIntensity );
		_lampRenderer.SetPropertyBlock( _lampBlock );
	}

	void ApplyRoleTint()
	{
		Color tint = RoleTint;
		if ( tint.a <= 0f || tint == Color.white )
			return;

		Transform roleLamp = transform.Find( "RoleLamp" );
		if ( roleLamp == null )
			return;

		if ( _roleBlock == null )
			_roleBlock = new MaterialPropertyBlock();

		ApplyTintToRenderers( roleLamp, tint, tint );
	}

	void ApplyTintToRenderers( Transform root, Color baseColor, Color emission )
	{
		Renderer[] renderers = root.GetComponentsInChildren<Renderer>( true );
		for ( int i = 0; i < renderers.Length; i++ )
		{
			Renderer renderer = renderers[ i ];
			if ( renderer == null )
				continue;

			EnsureEmissionEnabled( renderer );

			renderer.GetPropertyBlock( _roleBlock );
			_roleBlock.SetColor( BaseColorId, baseColor );
			_roleBlock.SetColor( ColorId, baseColor );
			_roleBlock.SetColor( EmissionColorId, emission );
			_roleBlock.SetFloat( EmissionIntensityId, LampEmissionIntensity );
			renderer.SetPropertyBlock( _roleBlock );
		}
	}

	static void EnsureEmissionEnabled( Renderer renderer )
	{
		if ( renderer == null )
			return;

		Material mat = renderer.material;
		if ( mat == null )
			return;

		if ( mat.HasProperty( EmissionIntensityId ) && mat.GetFloat( EmissionIntensityId ) < 0.01f )
			mat.SetFloat( EmissionIntensityId, LampEmissionIntensity );

		if ( mat.HasProperty( EmissionColorId ) && mat.GetColor( EmissionColorId ).maxColorComponent < 0.01f )
			mat.SetColor( EmissionColorId, Color.white );
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

	public void EditorSetDockPoint( Transform value )
	{
		dockPoint = value;
	}

	public void EditorSetFeedbacks( Feedbacks call, Feedbacks arrive, Feedbacks idle )
	{
		onCallFeedbacks = call;
		onArriveFeedbacks = arrive;
		onIdleFeedbacks = idle;
	}

	void OnDrawGizmosSelected()
	{
		Vector3 pos = DockWorldPosition;
		Gizmos.color = new Color( 0.2f, 0.85f, 1f, 0.9f );
		Gizmos.DrawWireSphere( pos, 0.22f );
		Gizmos.DrawLine( pos + Vector3.up * 0.35f, pos - Vector3.up * 0.05f );
	}
}
