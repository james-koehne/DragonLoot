using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// On-demand auto travel for unattached cargo carts between input/output stations.
/// Routes across the full junction network, not only the station's local track.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent( typeof( MinecartInteractable ) )]
public class MinecartAutoController : MonoBehaviour
{
	public enum AutoState
	{
		Idle = 0,
		Traveling = 1,
		Docked = 2,
		Waiting = 3,
		Paused = 4
	}

	const float DefaultInactivitySeconds = 10f;

	static readonly List<MinecartNetworkPathfinder.RouteLeg> RouteScratch = new List<MinecartNetworkPathfinder.RouteLeg>( 12 );

	MinecartInteractable _cart;
	MinecartStationBase _targetStation;
	MinecartStationBase _dockedStation;
	MinecartInputStation _homeInput;
	AutoState _state = AutoState.Idle;
	float _waitTimer;
	float _waitDuration = DefaultInactivitySeconds;

	public MinecartInteractable Cart => _cart != null ? _cart : ( _cart = GetComponent<MinecartInteractable>() );

	/// <summary>Cargo car for load/unload / routing decisions. Falls back to the lead cart.</summary>
	public MinecartInteractable CargoCart
	{
		get
		{
			MinecartInteractable cart = Cart;
			if ( cart == null )
				return null;

			MinecartInteractable cargo = cart.ResolveCargoCart();
			return cargo != null ? cargo : cart;
		}
	}

	public MinecartStationBase TargetStation => _targetStation;

	public MinecartInputStation HomeInput => _homeInput;

	public AutoState State => _state;

	public bool IsBusy => _state != AutoState.Idle;

	public bool IsDockedAt( MinecartStationBase station )
	{
		return _state == AutoState.Docked && _dockedStation == station;
	}

	public bool IsAvailableForCall
	{
		get
		{
			MinecartInteractable cart = Cart;
			return cart != null
				&& cart.IsAutoEligible
				&& _state == AutoState.Idle
				&& !cart.IsRecalling
				&& !cart.IsAutoMoving;
		}
	}

	public bool CanPlayerSend
	{
		get
		{
			MinecartInteractable cart = Cart;
			if ( cart == null || !cart.IsAutoEligible )
				return false;

			return _state == AutoState.Idle
				|| _state == AutoState.Traveling
				|| _state == AutoState.Docked
				|| _state == AutoState.Waiting
				|| _state == AutoState.Paused;
		}
	}

	/// <summary>True while auto-traveling; used for stop vs send prompts.</summary>
	public bool IsTraveling => _state == AutoState.Traveling;

	public void SetHomeInput( MinecartInputStation input )
	{
		if ( input != null )
			_homeInput = input;
	}

	void Awake()
	{
		_cart = GetComponent<MinecartInteractable>();
	}

	void OnDisable()
	{
		ClearDock( notifyStation: false );
		_targetStation = null;
		_waitTimer = 0f;
		_state = AutoState.Idle;
	}

	public bool TryDispatchTo( MinecartStationBase station )
	{
		MinecartInteractable cart = Cart;
		if ( station == null || cart == null || !IsAvailableForCall )
			return false;

		if ( !station.EnsureBoundTrackPublic() )
			return false;

		MinecartTrack targetTrack = station.BoundTrack;
		float targetDistance = station.TrackStopDistance;
		if ( targetTrack == null || cart.BoundTrack == null )
			return false;

		RouteScratch.Clear();
		float cost;
		if ( !MinecartNetworkPathfinder.TryFindRoute(
			    cart.BoundTrack,
			    cart.DistanceAlongTrack,
			    targetTrack,
			    targetDistance,
			    RouteScratch,
			    out cost ) )
			return false;

		_targetStation = station;
		if ( !cart.BeginAutoMove( targetTrack, targetDistance, this, RouteScratch ) )
		{
			_targetStation = null;
			return false;
		}

		if ( station is MinecartInputStation input )
			_homeInput = input;

		_dockedStation = null;
		_waitTimer = 0f;
		_state = AutoState.Traveling;
		return true;
	}

	public void LeaveDock( MinecartStationBase continueTo )
	{
		LeaveDockTo( continueTo, requireAutomationEnabled: true );
	}

	/// <summary>
	/// Undock and optionally dispatch to <paramref name="continueTo"/>.
	/// When <paramref name="requireAutomationEnabled"/> is false, manual go still dispatches while automation is paused.
	/// Returns true when travel toward <paramref name="continueTo"/> began (or undock-only when continueTo is null).
	/// </summary>
	public bool LeaveDockTo( MinecartStationBase continueTo, bool requireAutomationEnabled )
	{
		MinecartInteractable cart = Cart;
		MinecartStationBase wasDockedAt = _dockedStation;
		bool wasDocked = _state == AutoState.Docked;

		ClearDock( notifyStation: true );

		bool automationOk = !requireAutomationEnabled || MinecartStationBase.AutomationEnabled;
		if ( continueTo != null && cart != null && cart.IsAutoEligible && automationOk )
		{
			// Prefer station claim so inbound reservation / lamps stay consistent.
			if ( continueTo.TryDispatchCart( cart ) )
				return true;

			if ( TryDispatchTo( continueTo ) )
				return true;

			// Restore dock if we failed to leave toward a destination.
			if ( wasDocked && wasDockedAt != null )
			{
				_dockedStation = wasDockedAt;
				_state = AutoState.Docked;
				wasDockedAt.NotifyCartDocked( cart );
				return false;
			}
		}

		_waitTimer = 0f;
		_state = AutoState.Idle;
		_targetStation = null;
		return continueTo == null;
	}

	public void NotifyTravelCancelled( MinecartInteractable lead )
	{
		if ( lead != Cart )
			return;

		MinecartStationBase station = _targetStation;
		_targetStation = null;
		if ( ( _state == AutoState.Traveling || _state == AutoState.Waiting || _state == AutoState.Paused ) && station != null )
			station.NotifyInboundCancelled( lead );

		if ( _state != AutoState.Docked )
		{
			_waitTimer = 0f;
			_state = AutoState.Idle;
		}
	}

	public void NotifyArrived( MinecartInteractable lead )
	{
		MinecartInteractable cart = Cart;
		if ( lead != cart || _targetStation == null )
			return;

		MinecartStationBase station = _targetStation;
		_targetStation = null;
		_dockedStation = station;
		_waitTimer = 0f;
		_state = AutoState.Docked;
		station.NotifyCartDocked( cart );
	}

	/// <summary>
	/// Player placed cargo: stop travel and wait inactivity, or refresh a docked leave timer.
	/// </summary>
	public void NotifyPlayerCargoAdded()
	{
		MinecartInteractable cart = CargoCart;
		if ( cart == null || !cart.CargoEnabled )
			return;

		if ( _state == AutoState.Docked )
		{
			if ( _dockedStation != null )
				_dockedStation.NotifyPlayerCargoOnDockedCart();
			return;
		}

		if ( _state == AutoState.Traveling || _state == AutoState.Paused )
		{
			BeginPlayerCargoWait();
			return;
		}

		if ( _state == AutoState.Waiting )
			_waitTimer = 0f;
	}

	/// <summary>Interact: stop travel, resume pause/wait, leave dock, or dispatch toward a useful station.</summary>
	public bool TryPlayerSend()
	{
		MinecartInteractable cart = Cart;
		if ( cart == null || !cart.IsAutoEligible )
			return false;

		if ( _state == AutoState.Docked )
		{
			if ( _dockedStation == null )
				return false;

			_dockedStation.RequestForcedLeave();
			return true;
		}

		if ( _state == AutoState.Traveling )
			return BeginPlayerPause();

		if ( _state == AutoState.Paused )
			return ResumeTravelToTarget();

		if ( _state == AutoState.Waiting )
			return ResumeAfterWait();

		if ( _state == AutoState.Idle )
			return TryDispatchToNearestUsefulStation();

		return false;
	}

	public void ForceLeave()
	{
		LeaveDock( continueTo: null );
	}

	/// <summary>Hard-stop automation: cancel travel, undock, and return to Idle.</summary>
	public void ForceHaltToIdle()
	{
		MinecartInteractable cart = Cart;
		if ( _state == AutoState.Idle && ( cart == null || !cart.IsAutoMoving ) )
			return;

		if ( cart != null && cart.IsAutoMoving )
			cart.CancelAutoMove( arrived: false );
		else if ( ( _state == AutoState.Traveling || _state == AutoState.Waiting || _state == AutoState.Paused ) && _targetStation != null )
		{
			_targetStation.NotifyInboundCancelled( cart );
			_targetStation = null;
		}

		ClearDock( notifyStation: true );
		_targetStation = null;
		_waitTimer = 0f;
		_state = AutoState.Idle;
	}

	/// <summary>Closest output that accepts any cargo currently on the cart (or its cargo car).</summary>
	public static MinecartOutputStation FindBestOutputForCart( MinecartInteractable cart, MinecartStationBase exclude = null )
	{
		MinecartInteractable routeCart = ResolveRouteCart( cart );
		MinecartInteractable cargo = ResolveCargoForRouting( cart );
		if ( routeCart == null || routeCart.BoundTrack == null || cargo == null || cargo.ItemCount <= 0 )
			return null;

		float best = float.MaxValue;
		MinecartOutputStation bestStation = null;
		IReadOnlyList<MinecartStationBase> stations = MinecartStationBase.ActiveStations;
		for ( int i = 0; i < stations.Count; i++ )
		{
			MinecartOutputStation output = stations[ i ] as MinecartOutputStation;
			if ( output == null || output == exclude || !output.isActiveAndEnabled )
				continue;

			if ( !output.AcceptsAnyCargoOn( cargo ) )
				continue;

			if ( !output.EnsureBoundTrackPublic() || output.BoundTrack == null )
				continue;

			float cost;
			if ( !MinecartNetworkPathfinder.TryEstimateCost(
				    routeCart.BoundTrack,
				    routeCart.DistanceAlongTrack,
				    output.BoundTrack,
				    output.TrackStopDistance,
				    out cost ) )
				continue;

			if ( cost >= best )
				continue;

			best = cost;
			bestStation = output;
		}

		return bestStation;
	}

	public static MinecartInputStation FindNearestInput( MinecartInteractable cart )
	{
		MinecartInteractable routeCart = ResolveRouteCart( cart );
		if ( routeCart == null || routeCart.BoundTrack == null )
			return null;

		float best = float.MaxValue;
		MinecartInputStation bestStation = null;
		IReadOnlyList<MinecartStationBase> stations = MinecartStationBase.ActiveStations;
		for ( int i = 0; i < stations.Count; i++ )
		{
			MinecartInputStation input = stations[ i ] as MinecartInputStation;
			if ( input == null || !input.isActiveAndEnabled )
				continue;

			if ( !input.EnsureBoundTrackPublic() || input.BoundTrack == null )
				continue;

			float cost;
			if ( !MinecartNetworkPathfinder.TryEstimateCost(
				    routeCart.BoundTrack,
				    routeCart.DistanceAlongTrack,
				    input.BoundTrack,
				    input.TrackStopDistance,
				    out cost ) )
				continue;

			if ( cost >= best )
				continue;

			best = cost;
			bestStation = input;
		}

		return bestStation;
	}

	static MinecartInteractable ResolveRouteCart( MinecartInteractable cart )
	{
		return cart != null ? cart.ConsistLead : null;
	}

	static MinecartInteractable ResolveCargoForRouting( MinecartInteractable cart )
	{
		if ( cart == null )
			return null;

		MinecartInteractable cargo = cart.ResolveCargoCart();
		return cargo != null ? cargo : cart;
	}

	void ClearDock( bool notifyStation )
	{
		MinecartStationBase docked = _dockedStation;
		_dockedStation = null;
		if ( _state == AutoState.Docked )
			_state = AutoState.Idle;

		if ( notifyStation && docked != null )
			docked.NotifyCartUndocked( Cart );
	}

	void BeginPlayerCargoWait()
	{
		MinecartInteractable cart = Cart;
		if ( cart == null || _targetStation == null )
			return;

		_waitDuration = Mathf.Max( 0.5f, _targetStation.ResolvedInactivitySeconds );
		_waitTimer = 0f;
		cart.PauseAutoMove();
		_state = AutoState.Waiting;
	}

	bool BeginPlayerPause()
	{
		MinecartInteractable cart = Cart;
		if ( cart == null || _state != AutoState.Traveling )
			return false;

		cart.PauseAutoMove();
		_waitTimer = 0f;
		_state = AutoState.Paused;
		return true;
	}

	bool ResumeAfterWait()
	{
		if ( !MinecartStationBase.AutomationEnabled )
		{
			MinecartStationBase stalled = _targetStation;
			MinecartInteractable waitingCart = Cart;
			_waitTimer = 0f;
			_state = AutoState.Idle;
			_targetStation = null;
			if ( stalled != null )
				stalled.NotifyInboundCancelled( waitingCart );
			return false;
		}

		return ResumeTravelToTarget();
	}

	/// <summary>Repath to the saved target station. Used for player pause resume and cargo-wait resume.</summary>
	bool ResumeTravelToTarget()
	{
		MinecartStationBase station = _targetStation;
		if ( station == null )
		{
			_waitTimer = 0f;
			_state = AutoState.Idle;
			return false;
		}

		MinecartInteractable cart = Cart;
		if ( cart != null && cart.IsAutoMoving )
			cart.PauseAutoMove();

		_waitTimer = 0f;
		_state = AutoState.Idle;
		_targetStation = null;

		if ( !TryDispatchTo( station ) )
		{
			station.NotifyInboundCancelled( cart );
			return false;
		}

		return true;
	}

	bool TryDispatchToNearestUsefulStation()
	{
		MinecartInteractable cart = Cart;
		MinecartInteractable cargo = CargoCart;
		if ( cart == null || !IsAvailableForCall )
			return false;

		MinecartStationBase best = null;
		int cargoCount = cargo != null ? cargo.ItemCount : 0;
		if ( cargoCount > 0 )
		{
			best = FindBestOutputForCart( cart );
			if ( best == null )
				best = _homeInput != null ? _homeInput : FindNearestInput( cart );
		}
		else
			best = FindNearestStation( wantOutput: false );

		if ( best == null )
			best = FindNearestStation( wantOutput: true );

		if ( best == null )
			return false;

		if ( best.TryDispatchCart( cart ) )
			return true;

		return TryDispatchTo( best );
	}

	MinecartStationBase FindNearestStation( bool wantOutput )
	{
		MinecartInteractable cart = Cart;
		MinecartInteractable cargo = CargoCart;
		if ( cart == null || cart.BoundTrack == null )
			return null;

		float best = float.MaxValue;
		MinecartStationBase bestStation = null;
		IReadOnlyList<MinecartStationBase> stations = MinecartStationBase.ActiveStations;
		for ( int i = 0; i < stations.Count; i++ )
		{
			MinecartStationBase station = stations[ i ];
			if ( station == null || !station.isActiveAndEnabled )
				continue;

			bool isOutput = station is MinecartOutputStation;
			if ( wantOutput != isOutput )
				continue;

			if ( wantOutput )
			{
				MinecartOutputStation output = station as MinecartOutputStation;
				int cargoCount = cargo != null ? cargo.ItemCount : 0;
				if ( output != null && cargoCount > 0 && !output.AcceptsAnyCargoOn( cargo ) )
					continue;
			}

			if ( !station.EnsureBoundTrackPublic() || station.BoundTrack == null )
				continue;

			float cost;
			if ( !MinecartNetworkPathfinder.TryEstimateCost(
				    cart.BoundTrack,
				    cart.DistanceAlongTrack,
				    station.BoundTrack,
				    station.TrackStopDistance,
				    out cost ) )
				continue;

			if ( cost >= best )
				continue;

			best = cost;
			bestStation = station;
		}

		return bestStation;
	}

	void Update()
	{
		MinecartInteractable cart = Cart;
		if ( cart == null )
			return;

		if ( !cart.IsAutoEligible && _state != AutoState.Idle )
		{
			if ( cart.IsAutoMoving )
				cart.CancelAutoMove( arrived: false );
			else if ( ( _state == AutoState.Waiting || _state == AutoState.Paused ) && _targetStation != null )
				_targetStation.NotifyInboundCancelled( cart );

			ClearDock( notifyStation: true );
			_targetStation = null;
			_waitTimer = 0f;
			_state = AutoState.Idle;
			return;
		}

		if ( _state != AutoState.Waiting )
			return;

		_waitTimer += Time.deltaTime;
		if ( _waitTimer < _waitDuration )
			return;

		ResumeAfterWait();
	}
}
