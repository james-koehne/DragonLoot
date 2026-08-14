using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Hopper intake for <see cref="CoinSortingStation"/>: overlap absorb plus place/dump onto the
/// visible hopper <see cref="GroundCoinStack"/>. Coins stay as a stack and are consumed as the
/// station processes.
/// </summary>
[RequireComponent( typeof( Collider ) )]
public class CoinSortingHopper : MonoBehaviour, ITreasureOwner, ITreasurePlacementTarget
{
	static readonly Collider[] OverlapBuffer = new Collider[ 64 ];
	static readonly HashSet<int> AbsorbedInstanceIds = new HashSet<int>();

	[SerializeField]
	CoinSortingStation station;

	[Tooltip( "How often to scan for loose coins / stacks inside the hopper volume." )]
	[SerializeField]
	[Min( 0.02f )]
	float scanInterval = 0.1f;

	Collider _collider;
	float _nextScanTime;

	public TreasureOwnerKind OwnerKind => TreasureOwnerKind.CoinSortingStation;

	public CoinSortingStation Station => station;

	public void BindStation( CoinSortingStation owner )
	{
		station = owner;
	}

	void Awake()
	{
		_collider = GetComponent<Collider>();
		// Keep non-trigger so placement / interaction raycasts can hit the hopper.
		if ( _collider != null )
			_collider.isTrigger = false;

		if ( station == null )
			station = GetComponentInParent<CoinSortingStation>();
	}

	void FixedUpdate()
	{
		if ( station == null || _collider == null )
			return;
		if ( station.IsRepositioning )
			return;
		if ( Time.time < _nextScanTime )
			return;

		_nextScanTime = Time.time + Mathf.Max( 0.02f, scanInterval );
		ScanVolume();
	}

	void ScanVolume()
	{
		if ( station.IsHopperFull )
			return;

		Bounds bounds = _collider.bounds;
		// Include coins resting on the hopper top / thrown near the opening.
		Vector3 center = bounds.center + Vector3.up * 0.25f;
		Vector3 extents = bounds.extents + new Vector3( 0.15f, 0.4f, 0.15f );
		int hits = Physics.OverlapBoxNonAlloc(
			center,
			extents,
			OverlapBuffer,
			_collider.transform.rotation,
			Physics.DefaultRaycastLayers,
			QueryTriggerInteraction.Ignore );

		AbsorbedInstanceIds.Clear();

		for ( int i = 0; i < hits; i++ )
		{
			if ( station.IsHopperFull )
				break;

			Collider hit = OverlapBuffer[ i ];
			if ( hit == null || hit == _collider )
				continue;

			GroundCoinStack stack = hit.GetComponentInParent<GroundCoinStack>();
			if ( stack != null )
			{
				if ( station.IsHopperStack( stack ) )
					continue;

				int stackId = stack.GetInstanceID();
				if ( AbsorbedInstanceIds.Contains( stackId ) )
					continue;
				AbsorbedInstanceIds.Add( stackId );
				station.TryAbsorbStackIfFits( stack );
				continue;
			}

			TreasureItem item = hit.GetComponentInParent<TreasureItem>();
			if ( item == null )
				continue;

			if ( item.Owner is GroundCoinStack owned && station.IsHopperStack( owned ) )
				continue;

			int itemId = item.GetInstanceID();
			if ( AbsorbedInstanceIds.Contains( itemId ) )
				continue;
			AbsorbedInstanceIds.Add( itemId );
			station.TryAbsorbLooseCoin( item );
		}

		AbsorbedInstanceIds.Clear();
	}

	public void ReleaseTreasure( TreasureItem item )
	{
		// Hopper intake does not retain live ownership; the hopper stack does.
	}

	public void Remove( TreasureItem item )
	{
		// Hopper intake does not retain live ownership; the hopper stack does.
	}

	public bool CanPlace( TreasureItem item, in PlacementQuery query )
	{
		if ( station == null || item == null )
			return false;
		if ( station.IsRepositioning )
			return false;
		if ( !GroundCoinStack.IsGroundStackableCoin( item ) )
			return false;
		if ( station.IsHopperFull )
			return false;

		PlayerCarry carry = query.Player != null ? query.Player.Carry : null;
		if ( carry == null || carry.GetBucketCount( CarryBucketKind.Coin ) <= 0 )
			return false;

		return true;
	}

	public bool TryGetPlacementPreview( TreasureItem item, in PlacementQuery query, out PlacementPreview preview )
	{
		preview = default;
		if ( item == null )
			return false;

		GroundCoinStack hopperStack = station != null ? station.HopperStack : null;
		if ( hopperStack != null && hopperStack.Count > 0 )
			return hopperStack.TryGetPlacementPreview( item, in query, out preview );

		bool valid = CanPlace( item, in query );
		Vector3 scale = item.GetWorldScale();
		Vector3 pos = transform.position;
		if ( _collider != null )
		{
			Bounds bounds = _collider.bounds;
			pos = new Vector3( bounds.center.x, bounds.max.y, bounds.center.z );
		}

		Quaternion rot = TreasureOrientation.FlattenUpright( transform.rotation );
		preview.SetItemMesh( pos, rot, scale, valid );
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

		if ( !carry.TryConsumeActive( out TreasureItem removed ) || removed == null )
			return false;

		if ( !GroundCoinStack.IsGroundStackableCoin( removed ) )
		{
			removed.EnterPhysics( removed.transform.position, removed.transform.rotation );
			return false;
		}

		GroundCoinStack stack = station.HopperStack;
		if ( stack == null || !stack.CanAccept( removed.Definition ) )
		{
			removed.EnterPhysics( removed.transform.position, removed.transform.rotation );
			return false;
		}

		stack.BeginAppendFlight( removed, stack.transform.rotation );
		return true;
	}
}
