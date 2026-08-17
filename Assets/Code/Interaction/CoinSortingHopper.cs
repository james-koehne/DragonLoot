using UnityEngine;

/// <summary>
/// Hopper intake for <see cref="CoinSortingStation"/>: player place/dump onto the
/// visible hopper <see cref="GroundCoinStack"/>. Coins stay as a stack and are consumed as the
/// station processes. World coins are not auto-absorbed.
/// </summary>
[RequireComponent( typeof( Collider ) )]
public class CoinSortingHopper : MonoBehaviour, ITreasureOwner, ITreasurePlacementTarget
{
	[SerializeField]
	CoinSortingStation station;

	Collider _collider;

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
