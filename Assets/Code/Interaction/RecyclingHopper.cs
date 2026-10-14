using UnityEngine;

/// <summary>
/// Hopper intake for <see cref="RecyclingMachine"/>: place/dump recipe-gated Junk.
/// </summary>
[RequireComponent( typeof( Collider ) )]
public class RecyclingHopper : MonoBehaviour, ITreasureOwner, ITreasurePlacementTarget
{
	[SerializeField]
	RecyclingMachine station;

	Collider _collider;

	public TreasureOwnerKind OwnerKind => TreasureOwnerKind.RecyclingMachine;

	public RecyclingMachine Station => station;

	public void BindStation( RecyclingMachine owner )
	{
		station = owner;
	}

	void Awake()
	{
		_collider = GetComponent<Collider>();
		if ( _collider != null )
			_collider.isTrigger = false;

		if ( station == null )
			station = GetComponentInParent<RecyclingMachine>();
	}

	public void ReleaseTreasure( TreasureItem item )
	{
		if ( station != null )
			station.NotifyHopperItemReleased( item );
	}

	public void Remove( TreasureItem item )
	{
		ReleaseTreasure( item );
	}

	public bool CanPlace( TreasureItem item, in PlacementQuery query )
	{
		if ( station == null || item == null )
			return false;
		return station.CanAcceptJunk( item );
	}

	public bool TryGetPlacementPreview( TreasureItem item, in PlacementQuery query, out PlacementPreview preview )
	{
		preview = default;
		if ( item == null )
			return false;

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
		{
			if ( station != null )
				station.NotifyHopperReject( item );
			return false;
		}

		PlayerController player = query.Player;
		if ( player == null || player.Carry == null )
			return false;

		if ( !player.Carry.TryConsumeActive( out TreasureItem removed ) || removed == null )
			return false;

		if ( removed != item )
		{
			if ( !station.CanAcceptJunk( removed ) )
			{
				removed.EnterPhysics( removed.transform.position, removed.transform.rotation );
				station.NotifyHopperReject( removed );
				return false;
			}
		}

		return station.TryAcceptJunkItem( removed, fromHand: true );
	}
}
