using System.Collections;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Generic waypoint conveyor: discrete occupancy slots along a polyline, continuous advance
/// when the next slot is free, place or nearby-absorb into the input, backpressure at output.
/// </summary>
[RequireComponent( typeof( Collider ) )]
public class ConveyorBelt : InteractableBase, ITreasureOwner, ITreasurePlacementTarget
{
	const float MinSlotSpacing = 0.05f;
	const float MinPathLength = 0.01f;

	static readonly Collider[] AbsorbOverlap = new Collider[ 32 ];

	readonly List<Vector3> _pathPoints = new List<Vector3>( 8 );
	readonly List<float> _pathCum = new List<float>( 8 );

	[Header( "Path" )]
	[SerializeField]
	Transform[] waypoints;

	[SerializeField]
	[Min( 2 )]
	int slotCount = 4;

	[Header( "Motion" )]
	[SerializeField]
	[Min( 0.05f )]
	float moveSpeed = 1.25f;

	[Header( "Input Absorb" )]
	[SerializeField]
	[Min( 0f )]
	float absorbRadius = 0.55f;

	[SerializeField]
	[Min( 0.05f )]
	float snapDuration = 0.22f;

	[Header( "Gizmos" )]
	[SerializeField]
	Color pathGizmoColor = new Color( 0.2f, 0.85f, 1f, 0.9f );

	[SerializeField]
	Color slotGizmoColor = new Color( 1f, 0.85f, 0.2f, 0.95f );

	[SerializeField]
	Color occupiedGizmoColor = new Color( 1f, 0.35f, 0.2f, 0.95f );

	TreasureItem[] _occupants;
	bool[] _transferring;
	float[] _transferT;
	bool[] _claimed;
	Vector3[] _slotPositions;
	Quaternion[] _slotRotations;
	float[] _slotDistances;
	float _pathLength;
	bool _pathValid;
	bool _absorbingNearby;
	Coroutine _inputSnapRoutine;

	public TreasureOwnerKind OwnerKind => TreasureOwnerKind.Conveyor;

	public int SlotCount => _occupants != null ? _occupants.Length : 0;

	public bool IsInputFree
	{
		get
		{
			EnsureCapacity();
			return _occupants[ 0 ] == null && !_claimed[ 0 ];
		}
	}

	void Reset()
	{
		SetInteractionName( "Conveyor" );
	}

	void Awake()
	{
		if ( string.IsNullOrEmpty( InteractionName ) || InteractionName == "Interactable" )
			SetInteractionName( "Conveyor" );

		RebuildPath();
	}

	void OnValidate()
	{
		slotCount = Mathf.Max( 2, slotCount );
		moveSpeed = Mathf.Max( 0.05f, moveSpeed );
		absorbRadius = Mathf.Max( 0f, absorbRadius );
		snapDuration = Mathf.Max( 0.05f, snapDuration );
		RebuildPath();
	}

	void Update()
	{
		RebuildPath();

		if ( !_pathValid || _occupants == null )
			return;

		TickTransfers( Time.deltaTime );
		DriveOccupantPoses();

		if ( IsInputFree && absorbRadius > 0.0001f )
			AbsorbNearbyLooseItems();
	}

	public override bool CanInteract( PlayerController player )
	{
		return false;
	}

	public override void Interact( PlayerController player )
	{
	}

	public void ReleaseTreasure( TreasureItem item )
	{
		Remove( item );
	}

	public void Remove( TreasureItem item )
	{
		if ( item == null || _occupants == null )
			return;

		for ( int i = 0; i < _occupants.Length; i++ )
		{
			if ( _occupants[ i ] != item )
				continue;

			ClearSlot( i );
			return;
		}
	}

	public bool CanPlace( TreasureItem item, in PlacementQuery query )
	{
		if ( item == null || !IsAvailable || item.Definition == null )
			return false;

		PlayerCarry carry = query.Player != null ? query.Player.Carry : null;
		if ( carry == null || carry.Count <= 0 )
			return false;

		return IsInputFree;
	}

	public bool TryGetPlacementPreview( TreasureItem item, in PlacementQuery query, out PlacementPreview preview )
	{
		preview = default;
		if ( item == null || !_pathValid )
			return false;

		EnsureCapacity();
		GetSlotPose( 0, out Vector3 pos, out Quaternion rot );
		preview.SetItemMesh( pos, rot, item.GetWorldScale(), CanPlace( item, in query ) );
		return true;
	}

	public bool TryPlace( TreasureItem item, in PlacementQuery query )
	{
		if ( !CanPlace( item, in query ) )
			return false;

		PlayerController player = query.Player;
		if ( player == null )
			return false;

		PlayerCarry carry = player.Carry;
		if ( carry == null )
			return false;

		if ( !carry.TryConsumeActive( out TreasureItem removed ) || removed == null || removed != item )
		{
			if ( removed != null && removed != item )
				removed.EnterPhysics( removed.transform.position, removed.transform.rotation );
			return false;
		}

		if ( !TrySeatAtInput( removed, snap: true ) )
		{
			removed.EnterPhysics( removed.transform.position, removed.transform.rotation );
			return false;
		}

		AbsorbNearbyLooseItems();
		return true;
	}

	/// <summary>
	/// Sweep nearby world-loose treasure into the input slot when free.
	/// </summary>
	public void AbsorbNearbyLooseItems()
	{
		if ( _absorbingNearby || !IsInputFree || absorbRadius <= 0.0001f || !_pathValid )
			return;

		EnsureCapacity();
		GetSlotPose( 0, out Vector3 inputPos, out _ );

		_absorbingNearby = true;
		try
		{
			int hits = Physics.OverlapSphereNonAlloc(
				inputPos,
				absorbRadius,
				AbsorbOverlap,
				Physics.DefaultRaycastLayers,
				QueryTriggerInteraction.Ignore );

			for ( int i = 0; i < hits; i++ )
			{
				if ( !IsInputFree )
					break;

				Collider col = AbsorbOverlap[ i ];
				if ( col == null )
					continue;

				TreasureItem candidate = col.GetComponentInParent<TreasureItem>();
				if ( candidate == null || !candidate.IsWorldLoose || candidate.IsInFlight || candidate.IsReclaiming )
					continue;

				if ( candidate.Owner != null )
					continue;

				Vector3 delta = candidate.transform.position - inputPos;
				float xzSq = delta.x * delta.x + delta.z * delta.z;
				if ( xzSq > absorbRadius * absorbRadius )
					continue;

				TrySeatAtInput( candidate, snap: true );
				break;
			}
		}
		finally
		{
			_absorbingNearby = false;
		}
	}

	bool TrySeatAtInput( TreasureItem item, bool snap )
	{
		if ( item == null || item.Definition == null || !IsInputFree || !_pathValid )
			return false;

		EnsureCapacity();

		TreasureSurfaceWorld surfaceWorld = TreasureSurfaceWorld.Instance;
		if ( surfaceWorld != null && surfaceWorld.Simulator != null )
			surfaceWorld.Simulator.Unregister( item );

		_occupants[ 0 ] = item;
		_transferring[ 0 ] = false;
		_transferT[ 0 ] = 0f;
		_claimed[ 0 ] = false;

		GetSlotPose( 0, out Vector3 endPos, out Quaternion endRot );

		if ( snap )
		{
			item.ClaimPendingStackOwner( this );
			item.BeginFlight();
			if ( _inputSnapRoutine != null )
				StopCoroutine( _inputSnapRoutine );
			_inputSnapRoutine = StartCoroutine( SnapIntoInputRoutine( item, endPos, endRot ) );
		}
		else
		{
			item.EnterDisplayed( this, endPos, endRot );
		}

		return true;
	}

	IEnumerator SnapIntoInputRoutine( TreasureItem item, Vector3 endWorldPos, Quaternion endWorldRot )
	{
		if ( item == null )
			yield break;

		Transform t = item.transform;
		Vector3 startPos = t.position;
		Quaternion startRot = t.rotation;
		bool flipCoin = CoinFlipMotion.IsCoin( item );
		float duration = flipCoin
			? Mathf.Max( snapDuration, CoinFlipMotion.DefaultDuration )
			: Mathf.Max( snapDuration, CoinFlipMotion.DefaultItemArcDuration );
		float arcHeight = flipCoin ? CoinFlipMotion.DefaultArcHeight : CoinFlipMotion.DefaultItemArcHeight;
		float spins = flipCoin ? CoinFlipMotion.DefaultSpins : 0f;
		float elapsed = 0f;

		while ( elapsed < duration )
		{
			if ( item == null || _occupants == null || _occupants[ 0 ] != item )
			{
				if ( item != null )
					item.EndFlight();
				_inputSnapRoutine = null;
				yield break;
			}

			elapsed += Time.deltaTime;
			float u = Mathf.Clamp01( elapsed / duration );

			if ( flipCoin )
			{
				t.position = CoinFlipMotion.EvaluateArcPosition( startPos, endWorldPos, u, arcHeight );
				t.rotation = CoinFlipMotion.EvaluateFlipRotation(
					startRot,
					endWorldRot,
					startPos,
					endWorldPos,
					u,
					spins );
			}
			else
			{
				float ease = CoinFlipMotion.SmoothStep( u );
				t.position = CoinFlipMotion.EvaluateArcPosition( startPos, endWorldPos, u, arcHeight );
				t.rotation = Quaternion.Slerp( startRot, endWorldRot, ease );
			}

			yield return null;
		}

		_inputSnapRoutine = null;
		if ( item == null || _occupants == null || _occupants[ 0 ] != item )
		{
			if ( item != null )
				item.EndFlight();
			yield break;
		}

		item.EndFlight();
		GetSlotPose( 0, out endWorldPos, out endWorldRot );
		item.EnterDisplayed( this, endWorldPos, endWorldRot );
	}

	void TickTransfers( float dt )
	{
		if ( dt <= 0f || _occupants == null )
			return;

		int last = _occupants.Length - 1;
		for ( int i = last - 1; i >= 0; i-- )
		{
			TreasureItem item = _occupants[ i ];
			if ( item == null )
				continue;

			if ( item.IsInFlight )
				continue;

			if ( _transferring[ i ] )
			{
				AdvanceTransfer( i, dt );
				continue;
			}

			int next = i + 1;
			if ( _occupants[ next ] != null || _claimed[ next ] )
				continue;

			_claimed[ next ] = true;
			_transferring[ i ] = true;
			_transferT[ i ] = 0f;
		}
	}

	void AdvanceTransfer( int from, float dt )
	{
		TreasureItem item = _occupants[ from ];
		if ( item == null )
		{
			AbortTransfer( from );
			return;
		}

		int to = from + 1;
		float segment = Mathf.Max( MinSlotSpacing, Mathf.Abs( _slotDistances[ to ] - _slotDistances[ from ] ) );
		float delta = moveSpeed * dt / segment;
		_transferT[ from ] = Mathf.Clamp01( _transferT[ from ] + delta );

		GetSlotPose( from, out Vector3 fromPos, out Quaternion fromRot );
		GetSlotPose( to, out Vector3 toPos, out Quaternion toRot );
		float u = CoinFlipMotion.SmoothStep( _transferT[ from ] );
		item.transform.SetPositionAndRotation(
			Vector3.Lerp( fromPos, toPos, u ),
			Quaternion.Slerp( fromRot, toRot, u ) );

		if ( _transferT[ from ] < 1f )
			return;

		_occupants[ from ] = null;
		_transferring[ from ] = false;
		_transferT[ from ] = 0f;
		_claimed[ to ] = false;
		_occupants[ to ] = item;
		_transferring[ to ] = false;
		_transferT[ to ] = 0f;
		item.EnterDisplayed( this, toPos, toRot );
	}

	void AbortTransfer( int from )
	{
		if ( _occupants == null || from < 0 || from >= _occupants.Length - 1 )
			return;

		_transferring[ from ] = false;
		_transferT[ from ] = 0f;
		_claimed[ from + 1 ] = false;
	}

	void DriveOccupantPoses()
	{
		if ( _occupants == null )
			return;

		for ( int i = 0; i < _occupants.Length; i++ )
		{
			TreasureItem item = _occupants[ i ];
			if ( item == null || item.IsInFlight || _transferring[ i ] )
				continue;

			GetSlotPose( i, out Vector3 pos, out Quaternion rot );
			item.transform.SetPositionAndRotation( pos, rot );
		}
	}

	void ClearSlot( int index )
	{
		if ( _occupants == null || index < 0 || index >= _occupants.Length )
			return;

		TreasureItem item = _occupants[ index ];
		_occupants[ index ] = null;

		if ( _transferring[ index ] )
			AbortTransfer( index );
		else
			_transferring[ index ] = false;

		_transferT[ index ] = 0f;
		_claimed[ index ] = false;

		// If something was transferring into this slot, abort that transfer and return it home.
		if ( index > 0 )
		{
			int prev = index - 1;
			if ( _transferring[ prev ] && _occupants[ prev ] != null )
			{
				AbortTransfer( prev );
				GetSlotPose( prev, out Vector3 pos, out Quaternion rot );
				TreasureItem returning = _occupants[ prev ];
				if ( returning != null && !returning.IsInFlight )
					returning.EnterDisplayed( this, pos, rot );
			}
		}

		if ( index == 0 && item != null && _inputSnapRoutine != null )
		{
			StopCoroutine( _inputSnapRoutine );
			_inputSnapRoutine = null;
			item.EndFlight();
		}
	}

	void EnsureCapacity()
	{
		int count = Mathf.Max( 2, slotCount );
		if ( _occupants != null && _occupants.Length == count )
			return;

		TreasureItem[] oldOccupants = _occupants;
		bool[] oldTransferring = _transferring;
		float[] oldTransferT = _transferT;

		_occupants = new TreasureItem[ count ];
		_transferring = new bool[ count ];
		_transferT = new float[ count ];
		_claimed = new bool[ count ];

		if ( oldOccupants == null )
			return;

		int copy = Mathf.Min( oldOccupants.Length, count );
		for ( int i = 0; i < copy; i++ )
		{
			_occupants[ i ] = oldOccupants[ i ];
			_transferring[ i ] = oldTransferring != null && i < oldTransferring.Length && oldTransferring[ i ];
			_transferT[ i ] = oldTransferT != null && i < oldTransferT.Length ? oldTransferT[ i ] : 0f;
			if ( _transferring[ i ] && i + 1 < count )
				_claimed[ i + 1 ] = true;
		}
	}

	void RebuildPath()
	{
		EnsureCapacity();

		_pathPoints.Clear();
		if ( waypoints != null )
		{
			for ( int i = 0; i < waypoints.Length; i++ )
			{
				Transform wp = waypoints[ i ];
				if ( wp == null )
					continue;
				_pathPoints.Add( wp.position );
			}
		}

		if ( _pathPoints.Count < 2 )
		{
			_pathValid = false;
			_pathLength = 0f;
			_slotPositions = null;
			_slotRotations = null;
			_slotDistances = null;
			return;
		}

		for ( int i = _pathPoints.Count - 1; i > 0; i-- )
		{
			if ( ( _pathPoints[ i ] - _pathPoints[ i - 1 ] ).sqrMagnitude < 0.0000001f )
				_pathPoints.RemoveAt( i );
		}

		if ( _pathPoints.Count < 2 )
		{
			_pathValid = false;
			return;
		}

		_pathCum.Clear();
		_pathCum.Add( 0f );
		for ( int i = 0; i < _pathPoints.Count - 1; i++ )
			_pathCum.Add( _pathCum[ i ] + Vector3.Distance( _pathPoints[ i ], _pathPoints[ i + 1 ] ) );

		_pathLength = _pathCum[ _pathCum.Count - 1 ];
		if ( _pathLength < MinPathLength )
		{
			_pathValid = false;
			return;
		}

		int count = Mathf.Max( 2, slotCount );
		if ( _slotPositions == null || _slotPositions.Length != count )
		{
			_slotPositions = new Vector3[ count ];
			_slotRotations = new Quaternion[ count ];
			_slotDistances = new float[ count ];
		}

		for ( int s = 0; s < count; s++ )
		{
			float t = count == 1 ? 0f : (float)s / ( count - 1 );
			float dist = t * _pathLength;
			_slotDistances[ s ] = dist;
			SamplePath( dist, out Vector3 pos, out Vector3 tangent );
			_slotPositions[ s ] = pos;
			if ( tangent.sqrMagnitude < 0.0001f )
				tangent = transform.forward;
			_slotRotations[ s ] = Quaternion.LookRotation( tangent.normalized, Vector3.up );
		}

		_pathValid = true;
	}

	void SamplePath( float distance, out Vector3 position, out Vector3 tangent )
	{
		distance = Mathf.Clamp( distance, 0f, _pathCum[ _pathCum.Count - 1 ] );
		int seg = 0;
		for ( int i = 0; i < _pathCum.Count - 1; i++ )
		{
			if ( distance <= _pathCum[ i + 1 ] || i == _pathCum.Count - 2 )
			{
				seg = i;
				break;
			}
		}

		float segStart = _pathCum[ seg ];
		float segEnd = _pathCum[ seg + 1 ];
		float segLen = Mathf.Max( MinPathLength, segEnd - segStart );
		float u = Mathf.Clamp01( ( distance - segStart ) / segLen );
		position = Vector3.Lerp( _pathPoints[ seg ], _pathPoints[ seg + 1 ], u );
		tangent = _pathPoints[ seg + 1 ] - _pathPoints[ seg ];
	}

	void GetSlotPose( int index, out Vector3 position, out Quaternion rotation )
	{
		if ( _slotPositions != null && index >= 0 && index < _slotPositions.Length )
		{
			position = _slotPositions[ index ];
			rotation = _slotRotations[ index ];
			return;
		}

		position = transform.position;
		rotation = transform.rotation;
	}

	void OnDrawGizmos()
	{
		if ( !Application.isPlaying )
			RebuildPath();

		if ( waypoints != null )
		{
			Gizmos.color = pathGizmoColor;
			Vector3? prev = null;
			for ( int i = 0; i < waypoints.Length; i++ )
			{
				Transform wp = waypoints[ i ];
				if ( wp == null )
					continue;

				Gizmos.DrawSphere( wp.position, 0.06f );
				if ( prev.HasValue )
					Gizmos.DrawLine( prev.Value, wp.position );
				prev = wp.position;
			}
		}

		if ( _slotPositions == null )
			return;

		for ( int i = 0; i < _slotPositions.Length; i++ )
		{
			bool occupied = Application.isPlaying
				&& _occupants != null
				&& i < _occupants.Length
				&& _occupants[ i ] != null;

			Gizmos.color = occupied ? occupiedGizmoColor : slotGizmoColor;
			Gizmos.DrawWireSphere( _slotPositions[ i ], 0.1f );
			Vector3 forward = _slotRotations[ i ] * Vector3.forward;
			Gizmos.DrawRay( _slotPositions[ i ], forward * 0.25f );
		}
	}
}
