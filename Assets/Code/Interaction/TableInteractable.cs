using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Freeform sorting surface: place carried treasure at the look hit with Physics,
/// track resting items, accept any treasure type.
/// </summary>
public class TableInteractable : InteractableBase, ITreasureOwner, ITreasurePlacementTarget
{
	[SerializeField]
	[Tooltip( "Optional explicit placement surface collider. Defaults to this object's collider (or a child)." )]
	Collider placementSurface;

	[SerializeField]
	[Tooltip( "Optional bounds collider for surface extent checks. Defaults to placement surface." )]
	Collider surfaceBounds;

	[SerializeField]
	float placeUpBias = 0.05f;

	readonly List<TreasureItem> _itemsOnTable = new List<TreasureItem>();

	public TreasureOwnerKind OwnerKind => TreasureOwnerKind.SortingTable;

	public IReadOnlyList<TreasureItem> ItemsOnTable => _itemsOnTable;
	public Collider TableCollider => placementSurface;

	void Reset()
	{
		SetInteractionName( "Sorting Table" );
	}

	void Awake()
	{
		if ( string.IsNullOrEmpty( InteractionName ) || InteractionName == "Interactable" || InteractionName == "Table" )
			SetInteractionName( "Sorting Table" );

		EnsureSurfaceReferences();
	}

	public void ReleaseTreasure( TreasureItem item )
	{
		Remove( item );
	}

	public void Remove( TreasureItem item )
	{
		if ( item == null )
			return;

		if ( !_itemsOnTable.Remove( item ) )
			return;

		EventBus.Publish( new TreasureRemovedEvent
		{
			Target = this,
			Item = item,
			Definition = item.Definition
		} );
	}

	void UnregisterItemSilent( TreasureItem item )
	{
		if ( item == null )
			return;

		_itemsOnTable.Remove( item );
	}

	public override bool CanInteract( PlayerController player )
	{
		// Placement is right-click only via ITreasurePlacementTarget.
		return false;
	}

	public override void Interact( PlayerController player )
	{
	}

	public bool CanPlace( TreasureItem item, in PlacementQuery query )
	{
		if ( item == null || !IsAvailable || query.Player == null )
			return false;

		// Coins must use snappable slots / floor — not freeform sorting surfaces.
		if ( item.Definition != null && item.Definition.category == TreasureCategory.Coin )
			return false;

		PlayerCarry carry = query.Player.Carry;
		if ( carry == null || carry.Count <= 0 )
			return false;

		return TryResolvePlacePosition( in query, out _ );
	}

	public bool TryGetPlacementPreview( TreasureItem item, in PlacementQuery query, out PlacementPreview preview )
	{
		preview = default;
		if ( item == null )
			return false;

		if ( !TryResolvePlacePosition( in query, out Vector3 placePos ) )
			return false;

		placePos = ClampToSurfaceBounds( placePos );
		preview.Position = placePos;
		preview.Rotation = item.transform.rotation;
		preview.Scale = item.GetWorldScale();
		preview.IsValid = CanPlace( item, in query );
		return true;
	}

	public bool TryPlace( TreasureItem item, in PlacementQuery query )
	{
		if ( !CanPlace( item, in query ) )
			return false;

		PlayerController player = query.Player;
		PlayerCarry carry = player.Carry;
		PlayerInteraction interaction = player.Interaction;
		if ( carry == null || interaction == null )
			return false;

		if ( !TryResolvePlacePosition( in query, out Vector3 placePos ) )
			return false;

		if ( !IsWithinSurfaceBounds( placePos ) )
			placePos = ClampToSurfaceBounds( placePos );

		Vector3 velocity = interaction.GetSoftReleaseVelocity();
		if ( !carry.TryRemoveBottomCluster( out List<TreasureItem> cluster ) || cluster == null || cluster.Count == 0 )
			return false;

		ReleaseClusterOntoTable( cluster, placePos, velocity );

		TreasureDefinition definition = item != null ? item.Definition : null;
		if ( definition == null && cluster[ 0 ] != null )
			definition = cluster[ 0 ].Definition;

		EventBus.Publish( new TreasurePlacedOnSortingTableEvent
		{
			Treasure = definition,
			Amount = cluster.Count
		} );
		return true;
	}

	static PlacementQuery BuildQuery( PlayerController player )
	{
		PlacementQuery query = new PlacementQuery { Player = player };
		PlayerInteraction interaction = player != null ? player.Interaction : null;
		if ( interaction != null )
		{
			query.InteractRange = interaction.PlacementAimRange;
			if ( interaction.TryGetLastHit( out RaycastHit hit ) )
			{
				query.Hit = hit;
				query.HasHit = true;
			}
		}

		return query;
	}

	bool TryResolvePlacePosition( in PlacementQuery query, out Vector3 placePos )
	{
		if ( query.HasHit )
		{
			placePos = query.Hit.point + query.Hit.normal.normalized * placeUpBias;
			return true;
		}

		EnsureSurfaceReferences();
		Bounds bounds = surfaceBounds != null ? surfaceBounds.bounds : new Bounds( transform.position, Vector3.one );
		placePos = bounds.center + Vector3.up * ( bounds.extents.y + placeUpBias );
		return true;
	}

	void ReleaseClusterOntoTable( List<TreasureItem> cluster, Vector3 anchorDropPos, Vector3 velocity )
	{
		TreasureItem anchor = cluster[ 0 ];
		Vector3 anchorWorld = anchor != null ? anchor.transform.position : anchorDropPos;

		Vector3[] relativePos = new Vector3[ cluster.Count ];
		Quaternion[] worldRot = new Quaternion[ cluster.Count ];
		for ( int i = 0; i < cluster.Count; i++ )
		{
			TreasureItem member = cluster[ i ];
			if ( member == null )
				continue;

			relativePos[ i ] = member.transform.position - anchorWorld;
			worldRot[ i ] = member.transform.rotation;
		}

		for ( int i = 0; i < cluster.Count; i++ )
		{
			TreasureItem member = cluster[ i ];
			if ( member == null )
				continue;

			Vector3 pos = anchorDropPos + relativePos[ i ];
			member.EnterPhysics( pos, worldRot[ i ], velocity );
			RegisterItem( member );
		}
	}

	bool IsWithinSurfaceBounds( Vector3 worldPoint )
	{
		EnsureSurfaceReferences();
		if ( surfaceBounds == null )
			return true;

		Bounds bounds = surfaceBounds.bounds;
		bounds.Expand( 0.05f );
		return bounds.Contains( worldPoint );
	}

	Vector3 ClampToSurfaceBounds( Vector3 worldPoint )
	{
		EnsureSurfaceReferences();
		if ( surfaceBounds == null )
			return worldPoint;

		Bounds bounds = surfaceBounds.bounds;
		return new Vector3(
			Mathf.Clamp( worldPoint.x, bounds.min.x, bounds.max.x ),
			Mathf.Clamp( worldPoint.y, bounds.min.y, bounds.max.y ),
			Mathf.Clamp( worldPoint.z, bounds.min.z, bounds.max.z ) );
	}

	void EnsureSurfaceReferences()
	{
		if ( placementSurface == null )
			placementSurface = GetComponent<Collider>();
		if ( placementSurface == null )
			placementSurface = GetComponentInChildren<Collider>( true );

		if ( placementSurface == null )
		{
			Debug.LogError(
				"TableInteractable on '" + name + "' requires a Collider on this object or a child.",
				this );
		}

		if ( surfaceBounds == null )
			surfaceBounds = placementSurface;
	}

	void OnCollisionEnter( Collision collision )
	{
		TryRegisterCollision( collision );
	}

	void OnCollisionStay( Collision collision )
	{
		TryRegisterCollision( collision );
	}

	void OnCollisionExit( Collision collision )
	{
		if ( collision == null || collision.collider == null )
			return;

		TreasureItem item = collision.collider.GetComponentInParent<TreasureItem>();
		UnregisterItemSilent( item );
	}

	void TryRegisterCollision( Collision collision )
	{
		if ( collision == null || collision.collider == null )
			return;

		TreasureItem item = collision.collider.GetComponentInParent<TreasureItem>();
		if ( item == null || !item.IsWorldLoose )
			return;

		RegisterItem( item );
	}

	void RegisterItem( TreasureItem item )
	{
		if ( item == null )
			return;

		if ( !_itemsOnTable.Contains( item ) )
			_itemsOnTable.Add( item );
	}

	void LateUpdate()
	{
		for ( int i = _itemsOnTable.Count - 1; i >= 0; i-- )
		{
			TreasureItem item = _itemsOnTable[ i ];
			if ( item == null || !item.IsWorldLoose )
				_itemsOnTable.RemoveAt( i );
		}
	}
}
