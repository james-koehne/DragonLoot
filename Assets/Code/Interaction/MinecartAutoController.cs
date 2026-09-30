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
		Waiting = 3
	}

	const float DefaultInactivitySeconds = 10f;

	static readonly List<MinecartNetworkPathfinder.RouteLeg> RouteScratch = new List<MinecartNetworkPathfinder.RouteLeg>( 12 );

	MinecartInteractable _cart;
	MinecartStationBase _targetStation;
	MinecartStationBase _dockedStation;
	AutoState _state = AutoState.Idle;
	float _waitTimer;
	float _waitDuration = DefaultInactivitySeconds;

	public MinecartInteractable Cart => _cart != null ? _cart : ( _cart = GetComponent<MinecartInteractable>() );

	public MinecartStationBase TargetStation => _targetStation;

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

			if ( _state == AutoState.Waiting || _state == AutoState.Docked )
				return true;

			return _state == AutoState.Idle;
		}
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

		_dockedStation = null;
		_waitTimer = 0f;
		_state = AutoState.Traveling;
		return true;
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

	public void NotifyTravelCancelled( MinecartInteractable lead )
	{
		if ( lead != Cart )
			return;

		MinecartStationBase station = _targetStation;
		_targetStation = null;
		if ( ( _state == AutoState.Traveling || _state == AutoState.Waiting ) && station != null )
			station.NotifyInboundCancelled( lead );

		if ( _state != AutoState.Docked )
		{
			_waitTimer = 0f;
			_state = AutoState.Idle;
		}
	}

	/// <summary>
	/// Player placed cargo: stop travel and wait inactivity, or refresh a docked leave timer.
	/// </summary>
	public void NotifyPlayerCargoAdded()
	{
		MinecartInteractable cart = Cart;
		if ( cart == null || !cart.CargoEnabled )
			return;

		if ( _state == AutoState.Docked )
		{
			if ( _dockedStation != null )
				_dockedStation.NotifyPlayerCargoOnDockedCart();
			return;
		}

		if ( _state == AutoState.Traveling )
		{
			BeginPlayerCargoWait();
			return;
		}

		if ( _state == AutoState.Waiting )
			_waitTimer = 0f;
	}

	/// <summary>Interact: leave dock, skip wait, or dispatch toward a useful station.</summary>
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

	public void LeaveDock( MinecartStationBase continueTo )
	{
		MinecartInteractable cart = Cart;
		ClearDock( notifyStation: true );

		if ( continueTo != null && cart != null && cart.IsAutoEligible )
		{
			if ( TryDispatchTo( continueTo ) )
				return;
		}

		_waitTimer = 0f;
		_state = AutoState.Idle;
		_targetStation = null;
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

	bool ResumeAfterWait()
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
		if ( cart == null || !IsAvailableForCall )
			return false;

		bool preferOutput = cart.ItemCount > 0;
		MinecartStationBase best = FindNearestStation( preferOutput );
		if ( best == null )
			best = FindNearestStation( !preferOutput );

		if ( best == null )
			return false;

		// Prefer station claim so inbound reservation / lamps stay consistent.
		if ( best.TryDispatchCart( cart ) )
			return true;

		return TryDispatchTo( best );
	}

	MinecartStationBase FindNearestStation( bool wantOutput )
	{
		MinecartInteractable cart = Cart;
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
			else if ( _state == AutoState.Waiting && _targetStation != null )
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
