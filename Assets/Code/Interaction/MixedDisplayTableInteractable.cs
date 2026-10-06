using System.Collections;
using System.Collections.Generic;

using FeedbackSystem;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Open display table: accepts any <see cref="TreasureItem"/>.
/// Slots sit on a horizontal XZ plane. Items claim a rectangular footprint via
/// <see cref="TreasureDefinition.cartGridSize"/> (coins forced to 1×1).
/// Stackable treasure (<see cref="TreasureDefinition.canStack"/>) can pile vertically
/// on a matching footprint; non-stackable treasure (gems) uses one item per footprint.
/// </summary>
public class MixedDisplayTableInteractable : InteractableBase, ITreasureOwner, ITreasurePlacementTarget, ITreasureDisplayStackOwner
{
	class SlotStack
	{
		public int OriginX;
		public int OriginY;
		public Vector2Int Footprint = Vector2Int.one;
		public TreasureDefinition Definition;
		public Quaternion LocalRotation = Quaternion.identity;
		public readonly List<TreasureItem> Items = new List<TreasureItem>();
		public CoinStackCylinderVisual Cylinder;

		public bool IsEmpty => Items.Count == 0;
		public int Count => Items.Count;

		public TreasureItem Top => Items.Count > 0 ? Items[ Items.Count - 1 ] : null;
	}

	[Header( "Layout" )]
	[Tooltip( "Local space for generated slots on the horizontal display plane. Defaults to this transform." )]
	[SerializeField]
	Transform displayArea;

	[SerializeField]
	[Min( 1 )]
	int rows = 3;

	[SerializeField]
	[Min( 1 )]
	int columns = 4;

	[SerializeField]
	[Min( 0.01f )]
	float slotSpacing = 0.18f;

	[SerializeField]
	[Min( 0f )]
	float margin = 0.05f;

	[Header( "Stacking" )]
	[Tooltip( "0 = unlimited stack height per slot for canStack treasure." )]
	[SerializeField]
	[Min( 0 )]
	int maxStackPerSlot = 0;

	[Header( "Placement" )]
	[SerializeField]
	[Min( 0.05f )]
	float snapDuration = 0.25f;

	[SerializeField]
	[Min( 1f )]
	float bounceScale = 1.15f;

	[Header( "Filters" )]
	[Tooltip( "Empty = accept any category. When set, only listed categories may be placed." )]
	[SerializeField]
	TreasureCategory[] allowedCategories;

	[SerializeField]
	bool allowDisplayedPickup = true;

	[Header( "Feedback" )]
	[SerializeField]
	Text countLabel;

	[Header( "Editor" )]
	[SerializeField]
	bool drawLayoutGizmosAlways;

	Collider _collider;
	SlotStack[ , ] _cellStacks;
	readonly List<SlotStack> _stacks = new List<SlotStack>( 16 );
	readonly List<TreasureItem> _allItems = new List<TreasureItem>();
	int _itemCount;
	int _previewOutlineSlot = -1;
	Feedbacks _placeFeedbacks;
	bool _burnLocked;

	public TreasureOwnerKind OwnerKind => TreasureOwnerKind.Table;
	public bool AllowsDisplayedPickup => allowDisplayedPickup && !_burnLocked;
	public bool BurnLocked => _burnLocked;

	public int ItemCount => _itemCount;
	public int SlotCount => DisplayTableSlotLayout.SlotCount( rows, columns );
	public IReadOnlyList<TreasureItem> DisplayedItems => _allItems;
	public Collider TableCollider => _collider;

	public int LayoutRows => rows;
	public int LayoutColumns => columns;
	public float LayoutSlotSpacing => slotSpacing;
	public float LayoutMargin => margin;
	public Transform DisplayArea => displayArea != null ? displayArea : transform;

	int GridColumns => Mathf.Max( 1, columns );
	int GridRows => Mathf.Max( 1, rows );

	void Reset()
	{
		SetInteractionName( "Display Table" );
	}

	void Awake()
	{
		EnsureCollider();
		EnsureDisplayArea();
		RebuildSlots();
		if ( string.IsNullOrEmpty( InteractionName ) || InteractionName == "Interactable" )
			SetInteractionName( "Display Table" );
		RefreshCountLabel();
	}

	void OnValidate()
	{
		rows = Mathf.Max( 1, rows );
		columns = Mathf.Max( 1, columns );
		slotSpacing = Mathf.Max( 0.01f, slotSpacing );
		margin = Mathf.Max( 0f, margin );
		maxStackPerSlot = Mathf.Max( 0, maxStackPerSlot );
		snapDuration = Mathf.Max( 0.05f, snapDuration );
		bounceScale = Mathf.Max( 1f, bounceScale );
	}

	void OnDestroy()
	{
		StopAllCoroutines();
	}

	public void ReleaseTreasure( TreasureItem item )
	{
		Remove( item );
	}

	public void SetBurnLocked( bool locked )
	{
		_burnLocked = locked;
	}

	/// <summary>Copies currently displayed items into <paramref name="results"/> (clears first).</summary>
	public void CopyDisplayedItems( List<TreasureItem> results )
	{
		if ( results == null )
			return;

		results.Clear();
		for ( int i = 0; i < _allItems.Count; i++ )
		{
			TreasureItem item = _allItems[ i ];
			if ( item != null )
				results.Add( item );
		}
	}

	bool IsCategoryAllowed( TreasureDefinition definition )
	{
		if ( definition == null )
			return false;
		if ( allowedCategories == null || allowedCategories.Length == 0 )
			return true;

		TreasureCategory category = definition.category;
		for ( int i = 0; i < allowedCategories.Length; i++ )
		{
			if ( allowedCategories[ i ] == category )
				return true;
		}

		return false;
	}

	public bool TryCollectPickupColumn(
		TreasureItem selected,
		List<TreasureItem> results,
		Ray aimRay,
		bool hasAimRay,
		bool hasHitWorldY = false,
		float hitWorldY = 0f )
	{
		if ( results == null )
			return false;

		results.Clear();
		if ( selected == null || _cellStacks == null )
			return false;

		for ( int s = 0; s < _stacks.Count; s++ )
		{
			SlotStack slot = _stacks[ s ];
			if ( slot == null )
				continue;

			int selectedIndex = slot.Items.IndexOf( selected );
			if ( selectedIndex < 0 )
				continue;

			if ( hasAimRay && slot.Items.Count > 0 )
			{
				int aimIndex = CoinColumnPickup.ResolveIndexFromAimRay(
					slot.Items,
					aimRay,
					hasHitWorldY,
					hitWorldY );
				selectedIndex = Mathf.Clamp( aimIndex, 0, slot.Items.Count - 1 );
			}

			for ( int i = selectedIndex; i < slot.Items.Count; i++ )
			{
				TreasureItem member = slot.Items[ i ];
				if ( member != null )
					results.Add( member );
			}

			return results.Count > 0;
		}

		return false;
	}

	public bool TryGetSlotIndex( TreasureItem selected, out int slotIndex )
	{
		slotIndex = -1;
		if ( selected == null || _cellStacks == null )
			return false;

		for ( int s = 0; s < _stacks.Count; s++ )
		{
			SlotStack stack = _stacks[ s ];
			if ( stack == null || stack.Items.IndexOf( selected ) < 0 )
				continue;

			slotIndex = SlotIndex( stack.OriginX, stack.OriginY );
			return true;
		}

		return false;
	}

	public int GetSlotCount( int slotIndex )
	{
		SlotStack stack = GetOriginStack( slotIndex );
		return stack != null ? stack.Count : 0;
	}

	public void AppendSlotOutlineRenderers( TreasureItem selected, List<Renderer> renderers )
	{
		if ( !TryGetSlotIndex( selected, out int slotIndex ) )
			return;

		AppendSlotOutlineRenderers( GetOriginStack( slotIndex ), renderers );
	}

	public bool TryConsumeSlotDefinitions(
		int slotIndex,
		List<TreasureDefinition> into,
		out Vector3 contact,
		out Quaternion rotation,
		int maxCount )
	{
		contact = transform.position;
		rotation = transform.rotation;
		if ( into == null || _cellStacks == null || maxCount <= 0 )
			return false;

		RemapSlotToOrigin( ref slotIndex );
		SlotStack slot = GetOriginStack( slotIndex );
		if ( slot == null || slot.Count <= 0 )
			return false;

		GetSlotBaseWorldPose( slotIndex, out contact, out rotation );

		List<TreasureItem> taken = new List<TreasureItem>( slot.Items.Count );
		for ( int i = 0; i < slot.Items.Count; i++ )
		{
			TreasureItem member = slot.Items[ i ];
			if ( member == null || member.Definition == null )
				continue;

			taken.Add( member );
		}

		if ( taken.Count == 0 )
			return false;

		int takeItems = Mathf.Min( maxCount, taken.Count );
		int itemStart = taken.Count - takeItems;
		slot.Items.Clear();
		for ( int i = 0; i < itemStart; i++ )
			slot.Items.Add( taken[ i ] );

		for ( int i = itemStart; i < taken.Count; i++ )
		{
			TreasureItem member = taken[ i ];
			into.Add( member.Definition );
			_allItems.Remove( member );
			_itemCount = Mathf.Max( 0, _itemCount - 1 );
			NotifySortedDelta( member.Definition, -1 );
			EventBus.Publish( new TreasureRemovedEvent
			{
				Target = this,
				Item = member,
				Definition = member.Definition
			} );
			TreasureItemFactory.Despawn( member );
		}

		if ( slot.Items.Count == 0 )
		{
			ClearFootprint( slot );
			_stacks.Remove( slot );
		}

		RefreshSlotCylinder( slot );
		RefreshCountLabel();
		PublishChanged();
		return into.Count > 0;
	}

	public int GetSlotCoinAppendCapacity( int slotIndex, TreasureDefinition probe )
	{
		if ( probe == null || probe.category != TreasureCategory.Coin || _cellStacks == null )
			return 0;
		if ( !IsTableStackable( probe ) )
			return 0;

		RemapSlotToOrigin( ref slotIndex );
		if ( !TryGetCell( slotIndex, out int ox, out int oy ) )
			return 0;

		SlotStack slot = _cellStacks[ ox, oy ];
		if ( slot == null )
		{
			if ( !IsRectangleFree( ox, oy, Vector2Int.one ) )
				return 0;
			return maxStackPerSlot > 0 ? maxStackPerSlot : int.MaxValue;
		}

		if ( slot.OriginX != ox || slot.OriginY != oy )
			return 0;

		if ( !CanStackOnto( probe, slot ) )
			return 0;

		int max = maxStackPerSlot > 0 ? maxStackPerSlot : int.MaxValue;
		return Mathf.Max( 0, max - slot.Count );
	}

	public bool TryGetSlotAppendPose( int slotIndex, out Vector3 contact, out Quaternion rotation )
	{
		contact = transform.position;
		rotation = transform.rotation;
		if ( _cellStacks == null )
			return false;

		RemapSlotToOrigin( ref slotIndex );
		SlotStack slot = GetOriginStack( slotIndex );
		if ( slot == null )
		{
			if ( !TryGetCell( slotIndex, out int ox, out int oy ) )
				return false;
			GetCellWorldPose( ox, oy, 0, null, null, Vector2Int.one, out contact, out rotation );
			return true;
		}

		GetSlotBaseWorldPose( slotIndex, out contact, out rotation );
		float height = 0f;
		for ( int i = 0; i < slot.Items.Count; i++ )
			height += TreasureStackSpacing.GetStep( slot.Items[ i ] );
		contact += Vector3.up * height;
		return true;
	}

	public int TryAppendSlotDefinitions( int slotIndex, IReadOnlyList<TreasureDefinition> definitions )
	{
		if ( definitions == null || definitions.Count == 0 || _cellStacks == null )
			return 0;

		RemapSlotToOrigin( ref slotIndex );
		if ( !TryGetCell( slotIndex, out int ox, out int oy ) )
			return 0;

		int added = 0;
		for ( int i = 0; i < definitions.Count; i++ )
		{
			TreasureDefinition def = definitions[ i ];
			if ( GetSlotCoinAppendCapacity( slotIndex, def ) <= 0 )
				break;

			TryGetSlotAppendPose( slotIndex, out Vector3 pos, out Quaternion rot );
			TreasureItem visual = TreasureItemFactory.RentVisualCoin( def, pos, rot );
			if ( visual == null )
				visual = TreasureItemFactory.SpawnFallback( def, pos, rot, null );
			if ( visual == null )
				break;

			if ( !CommitItem( visual, ox, oy, animate: false ) )
			{
				TreasureItemFactory.Despawn( visual );
				break;
			}

			PlayTreasurePlaceFeedback( visual );
			added++;
		}

		if ( added <= 0 )
			return 0;

		RefreshCountLabel();
		PublishChanged();
		return added;
	}

	public void Remove( TreasureItem item )
	{
		if ( item == null || _cellStacks == null )
			return;

		for ( int s = 0; s < _stacks.Count; s++ )
		{
			SlotStack slot = _stacks[ s ];
			if ( slot == null )
				continue;

			int index = slot.Items.IndexOf( item );
			if ( index < 0 )
				continue;

			slot.Items.RemoveAt( index );
			_allItems.Remove( item );
			_itemCount = Mathf.Max( 0, _itemCount - 1 );

			if ( slot.Items.Count == 0 )
			{
				ClearFootprint( slot );
				_stacks.RemoveAt( s );
				RefreshSlotCylinder( slot );
			}
			else
			{
				RestackSlot( slot );
			}

			RefreshCountLabel();
			PublishChanged();
			NotifySortedDelta( item.Definition, -1 );
			EventBus.Publish( new TreasureRemovedEvent
			{
				Target = this,
				Item = item,
				Definition = item.Definition
			} );
			return;
		}
	}

	/// <summary>Detaches one displayed item for automated minecart transfer.</summary>
	public bool TryExtractOneItem( out TreasureItem item )
	{
		item = null;
		for ( int s = _stacks.Count - 1; s >= 0; s-- )
		{
			SlotStack slot = _stacks[ s ];
			if ( slot == null || slot.IsEmpty )
				continue;

			item = slot.Top;
			if ( item == null )
				continue;

			Remove( item );
			if ( item != null )
				item.transform.SetParent( null, true );
			return item != null;
		}

		return false;
	}

	/// <summary>
	/// Finds a ground-stackable coin slot whose full stack fits in <paramref name="maxCoins"/>,
	/// preferring the nearest to <paramref name="worldPos"/>.
	/// </summary>
	public bool TryFindPullableCoinStack(
		Vector3 worldPos,
		int maxCoins,
		out int slotIndex,
		out int coinCount,
		out float distSq )
	{
		slotIndex = -1;
		coinCount = 0;
		distSq = float.MaxValue;
		if ( maxCoins <= 0 || _stacks == null || _stacks.Count == 0 )
			return false;

		for ( int s = 0; s < _stacks.Count; s++ )
		{
			SlotStack slot = _stacks[ s ];
			if ( slot == null || slot.IsEmpty || slot.Definition == null )
				continue;
			if ( !GroundCoinStack.IsGroundStackableCoin( slot.Definition ) )
				continue;
			if ( slot.Count <= 0 || slot.Count > maxCoins )
				continue;

			int candidateSlot = SlotIndex( slot.OriginX, slot.OriginY );
			GetSlotBaseWorldPose( candidateSlot, out Vector3 contact, out _ );
			Vector3 delta = contact - worldPos;
			float sq = delta.x * delta.x + delta.z * delta.z;
			if ( sq >= distSq )
				continue;

			distSq = sq;
			slotIndex = candidateSlot;
			coinCount = slot.Count;
		}

		return slotIndex >= 0 && coinCount > 0;
	}

	/// <summary>Accepts an already-detached world item into the first free / stackable slot.</summary>
	public bool TryAcceptWorldItem( TreasureItem item )
	{
		if ( item == null || item.Definition == null || _cellStacks == null || !IsAvailable )
			return false;

		if ( !TryFindAutoPlacement( item.Definition, out int ox, out int oy ) )
			return false;

		if ( !CommitItem( item, ox, oy, animate: true ) )
			return false;

		RefreshCountLabel();
		PublishChanged();
		return true;
	}

	public bool CanAcceptWorldItem( TreasureDefinition def )
	{
		return def != null && TryFindAutoPlacement( def, out _, out _ );
	}

	bool TryFindAutoPlacement( TreasureDefinition def, out int ox, out int oy )
	{
		ox = 0;
		oy = 0;
		if ( def == null || _cellStacks == null )
			return false;

		Vector2Int footprint = GetItemFootprint( def );
		int cols = GridColumns;
		int rowCount = GridRows;

		if ( IsTableStackable( def ) )
		{
			for ( int y = 0; y <= rowCount - footprint.y; y++ )
			{
				for ( int x = 0; x <= cols - footprint.x; x++ )
				{
					SlotStack existing = _cellStacks[ x, y ];
					if ( existing == null )
						continue;
					if ( existing.OriginX != x || existing.OriginY != y )
						continue;
					if ( !CanStackOnto( def, existing ) || existing.Footprint != footprint )
						continue;

					ox = x;
					oy = y;
					return true;
				}
			}
		}

		for ( int y = 0; y <= rowCount - footprint.y; y++ )
		{
			for ( int x = 0; x <= cols - footprint.x; x++ )
			{
				if ( !IsRectangleFree( x, y, footprint ) )
					continue;

				ox = x;
				oy = y;
				return true;
			}
		}

		return false;
	}

	public override bool CanInteract( PlayerController player )
	{
		return false;
	}

	public override void Interact( PlayerController player )
	{
	}

	public bool CanPlace( TreasureItem item, in PlacementQuery query )
	{
		if ( item == null || !IsAvailable || _burnLocked || item.Definition == null )
			return false;

		if ( !IsCategoryAllowed( item.Definition ) )
			return false;

		PlayerCarry carry = query.Player != null ? query.Player.Carry : null;
		if ( carry == null || carry.Count <= 0 )
			return false;

		if ( !TryResolveNearestSlot( item, in query, out int slotIndex, out int stackIndex, out bool valid ) )
			return false;

		return valid;
	}

	public bool TryGetPlacementPreview( TreasureItem item, in PlacementQuery query, out PlacementPreview preview )
	{
		preview = default;
		_previewOutlineSlot = -1;
		if ( item == null || _burnLocked || item.Definition == null || !IsCategoryAllowed( item.Definition ) )
			return false;

		if ( !TryResolveNearestSlot( item, in query, out int slotIndex, out int stackIndex, out bool valid ) )
			return false;

		_previewOutlineSlot = slotIndex;
		Vector3 scale = item.GetWorldScale();

		GetSlotWorldPose( slotIndex, stackIndex, item, out Vector3 pos, out Quaternion itemRot );
		preview.SetItemMesh( pos, itemRot, scale, valid );
		return true;
	}

	/// <summary>
	/// Appends mesh renderers for the last placement-preview slot (cylinder + visible coins).
	/// </summary>
	public void AppendPreviewStackOutlineRenderers( List<Renderer> renderers )
	{
		if ( renderers == null || _cellStacks == null || _previewOutlineSlot < 0 )
			return;

		int slotIndex = _previewOutlineSlot;
		RemapSlotToOrigin( ref slotIndex );
		AppendSlotOutlineRenderers( GetOriginStack( slotIndex ), renderers );
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

		if ( !TryResolveNearestSlot( item, in query, out int slotIndex, out int stackIndex, out bool valid ) || !valid )
			return false;

		if ( !TryGetCell( slotIndex, out int ox, out int oy ) )
			return false;

		if ( !carry.TryConsumeActive( out TreasureItem removed ) || removed == null || removed != item )
		{
			if ( removed != null && removed != item )
				removed.EnterPhysics( removed.transform.position, removed.transform.rotation );
			return false;
		}

		if ( !CommitItem( removed, ox, oy, animate: true ) )
		{
			removed.EnterPhysics( removed.transform.position, removed.transform.rotation );
			return false;
		}

		RefreshCountLabel();
		PublishChanged();
		return true;
	}

	/// <summary>Hold-F whole coin stack: resolve a placeable slot under the aim ray.</summary>
	public bool TryResolveWholeCoinPlaceSlot(
		TreasureItem probe,
		in PlacementQuery query,
		out int slotIndex,
		out Vector3 contact,
		out Quaternion rotation )
	{
		slotIndex = -1;
		contact = transform.position;
		rotation = transform.rotation;
		if ( probe == null || probe.Definition == null )
			return false;

		if ( !TryFindWholeCoinPlaceSlot( probe.Definition, in query, out slotIndex, out contact, out rotation ) )
			return false;

		return GetSlotCoinAppendCapacity( slotIndex, probe.Definition ) > 0;
	}

	/// <summary>
	/// True when at least one carried coin can append onto this table (mixed coin stacks stay mixed).
	/// </summary>
	public bool CanAcceptWholeMixedCoinStack(
		IReadOnlyList<TreasureDefinition> definitions,
		in PlacementQuery query )
	{
		if ( definitions == null || definitions.Count == 0 )
			return false;

		for ( int i = 0; i < definitions.Count; i++ )
		{
			TreasureDefinition def = definitions[ i ];
			if ( def == null || def.category != TreasureCategory.Coin )
				continue;

			if ( TryFindWholeCoinPlaceSlot( def, in query, out _, out _, out _ ) )
				return true;
		}

		return false;
	}

	/// <summary>Nearest slot that can accept another stack of <paramref name="definition"/>.</summary>
	public bool TryFindWholeCoinPlaceSlot(
		TreasureDefinition definition,
		in PlacementQuery query,
		out int slotIndex,
		out Vector3 contact,
		out Quaternion rotation )
	{
		slotIndex = -1;
		contact = transform.position;
		rotation = transform.rotation;
		if ( definition == null || !IsAvailable || !IsTableStackable( definition ) || _cellStacks == null )
			return false;

		if ( query.HasHit
			&& TryResolveAimedSlot( in query, out int aimedSlot )
			&& GetSlotCoinAppendCapacity( aimedSlot, definition ) > 0 )
		{
			slotIndex = aimedSlot;
			RemapSlotToOrigin( ref slotIndex );
			return TryGetSlotAppendPose( slotIndex, out contact, out rotation );
		}

		Vector3 reference = GetSlotSearchReference( in query );
		float bestDistSq = float.MaxValue;
		int bestSlot = -1;
		int cols = GridColumns;
		int rowCount = GridRows;

		for ( int y = 0; y < rowCount; y++ )
		{
			for ( int x = 0; x < cols; x++ )
			{
				int candidate = SlotIndex( x, y );
				if ( GetSlotCoinAppendCapacity( candidate, definition ) <= 0 )
					continue;

				if ( !TryGetSlotDistanceSq( reference, candidate, out float distSq ) )
					continue;

				if ( distSq >= bestDistSq )
					continue;

				bestDistSq = distSq;
				bestSlot = candidate;
			}
		}

		if ( bestSlot < 0 )
			return false;

		slotIndex = bestSlot;
		RemapSlotToOrigin( ref slotIndex );
		return TryGetSlotAppendPose( slotIndex, out contact, out rotation );
	}

	/// <summary>
	/// Always snaps to the nearest generated slot based on aim. Invalid slots still resolve
	/// so the preview stays on the hovered pile instead of jumping to table center.
	/// </summary>
	bool TryResolveNearestSlot(
		TreasureItem item,
		in PlacementQuery query,
		out int slotIndex,
		out int stackIndex,
		out bool valid )
	{
		slotIndex = -1;
		stackIndex = 0;
		valid = false;
		if ( item == null || item.Definition == null || _cellStacks == null )
			return false;

		if ( !TryResolveAimedSlot( in query, out slotIndex ) )
			return false;

		int hoveredSlot = slotIndex;
		RemapSlotToOrigin( ref hoveredSlot );
		slotIndex = hoveredSlot;

		if ( TryGetStackIndexForSlot( item, hoveredSlot, out stackIndex ) )
		{
			valid = true;
			return true;
		}

		if ( query.AutoFindValidSlot
			&& TryFindNearestValidSlot( item, in query, out int autoSlot, out int autoStack ) )
		{
			slotIndex = autoSlot;
			stackIndex = autoStack;
			valid = true;
			return true;
		}

		// Invalid: still preview at the hovered origin (top of occupied stack or base).
		slotIndex = hoveredSlot;
		if ( !TryGetCell( slotIndex, out _, out _ ) )
			return false;

		SlotStack hovered = GetOriginStack( slotIndex );
		stackIndex = hovered != null ? hovered.Count : 0;
		valid = false;
		return true;
	}

	bool TryFindNearestValidSlot(
		TreasureItem item,
		in PlacementQuery query,
		out int slotIndex,
		out int stackIndex )
	{
		slotIndex = -1;
		stackIndex = 0;
		if ( item == null || item.Definition == null || _cellStacks == null )
			return false;

		Vector2Int footprint = GetItemFootprint( item.Definition );
		Vector3 reference = GetSlotSearchReference( in query );
		float bestDistSq = float.MaxValue;
		int cols = GridColumns;
		int rowCount = GridRows;

		for ( int y = 0; y <= rowCount - footprint.y; y++ )
		{
			for ( int x = 0; x <= cols - footprint.x; x++ )
			{
				if ( !TryGetStackIndexForOrigin( item, x, y, out int candidateStack ) )
					continue;

				int candidateSlot = SlotIndex( x, y );
				if ( !TryGetSlotDistanceSq( reference, candidateSlot, out float distSq ) )
					continue;

				if ( distSq >= bestDistSq )
					continue;

				bestDistSq = distSq;
				slotIndex = candidateSlot;
				stackIndex = candidateStack;
			}
		}

		return slotIndex >= 0;
	}

	Vector3 GetSlotSearchReference( in PlacementQuery query )
	{
		if ( query.HasHit )
			return query.Hit.point;

		Transform area = displayArea != null ? displayArea : transform;
		return area.position;
	}

	bool TryGetSlotDistanceSq( Vector3 worldPoint, int slotIndex, out float distSq )
	{
		distSq = float.MaxValue;
		if ( !TryGetCell( slotIndex, out int ox, out int oy ) )
			return false;

		Transform area = displayArea != null ? displayArea : transform;
		Vector3 local = area.InverseTransformPoint( worldPoint );
		local.y = 0f;
		Vector3 slotLocal = ComputeSlotLocalPosition( SlotIndex( ox, oy ) );
		slotLocal.y = 0f;
		distSq = ( slotLocal - local ).sqrMagnitude;
		return true;
	}

	bool TryResolveAimedSlot( in PlacementQuery query, out int slotIndex )
	{
		slotIndex = -1;
		if ( !query.HasHit || query.Hit.collider == null || _cellStacks == null )
			return false;

		TreasureItem hitItem = query.Hit.collider.GetComponentInParent<TreasureItem>();
		if ( hitItem != null && TryFindSlotContaining( hitItem, out slotIndex ) )
			return true;

		if ( !TryFindNearestSlot( query.Hit.point, out slotIndex, out _ ) )
			return false;

		RemapSlotToOrigin( ref slotIndex );
		return true;
	}

	bool TryGetStackIndexForSlot( TreasureItem item, int slotIndex, out int stackIndex )
	{
		stackIndex = 0;
		if ( !TryGetCell( slotIndex, out int ox, out int oy ) )
			return false;

		return TryGetStackIndexForOrigin( item, ox, oy, out stackIndex );
	}

	bool TryGetStackIndexForOrigin( TreasureItem item, int originX, int originY, out int stackIndex )
	{
		stackIndex = 0;
		if ( item == null || item.Definition == null || _cellStacks == null )
			return false;

		if ( originX < 0 || originY < 0 || originX >= GridColumns || originY >= GridRows )
			return false;

		Vector2Int footprint = GetItemFootprint( item.Definition );
		SlotStack stack = _cellStacks[ originX, originY ];
		if ( stack != null )
		{
			if ( stack.OriginX != originX || stack.OriginY != originY )
				return false;

			if ( !CanStackOnto( item.Definition, stack ) )
				return false;

			if ( stack.Footprint != footprint )
				return false;

			stackIndex = stack.Count;
			return true;
		}

		if ( !IsRectangleFree( originX, originY, footprint ) )
			return false;

		stackIndex = 0;
		return true;
	}

	bool TryFindSlotContaining( TreasureItem item, out int slotIndex )
	{
		slotIndex = -1;
		if ( item == null || _cellStacks == null )
			return false;

		for ( int s = 0; s < _stacks.Count; s++ )
		{
			SlotStack stack = _stacks[ s ];
			if ( stack == null || stack.Items.IndexOf( item ) < 0 )
				continue;

			slotIndex = SlotIndex( stack.OriginX, stack.OriginY );
			return true;
		}

		return false;
	}

	bool TryFindNearestSlot( Vector3 worldPoint, out int slotIndex, out float distSq )
	{
		slotIndex = -1;
		distSq = float.MaxValue;
		if ( _cellStacks == null )
			return false;

		Transform area = displayArea != null ? displayArea : transform;
		Vector3 local = area.InverseTransformPoint( worldPoint );
		local.y = 0f;
		int count = SlotCount;

		for ( int i = 0; i < count; i++ )
		{
			Vector3 slotLocal = ComputeSlotLocalPosition( i );
			slotLocal.y = 0f;
			float d = ( slotLocal - local ).sqrMagnitude;
			if ( d >= distSq )
				continue;

			distSq = d;
			slotIndex = i;
		}

		return slotIndex >= 0;
	}

	bool CommitItem( TreasureItem item, int ox, int oy, bool animate )
	{
		if ( item == null || item.Definition == null || _cellStacks == null )
			return false;

		TreasureDefinition def = item.Definition;
		Vector2Int footprint = GetItemFootprint( def );
		SlotStack stack = _cellStacks[ ox, oy ];
		if ( stack != null )
		{
			if ( stack.OriginX != ox || stack.OriginY != oy )
				return false;
			if ( !CanStackOnto( def, stack ) || stack.Footprint != footprint )
				return false;
		}
		else
		{
			if ( !IsRectangleFree( ox, oy, footprint ) )
				return false;

			stack = new SlotStack
			{
				OriginX = ox,
				OriginY = oy,
				Footprint = footprint,
				Definition = def,
				LocalRotation = Quaternion.identity
			};
			_stacks.Add( stack );
			MarkFootprint( ox, oy, footprint, stack );
		}

		stack.Items.Add( item );
		if ( !_allItems.Contains( item ) )
			_allItems.Add( item );
		_itemCount++;
		NotifySortedDelta( def, 1 );

		int stackIndex = stack.Items.Count - 1;
		int slotIndex = SlotIndex( ox, oy );
		if ( animate && isActiveAndEnabled )
		{
			StartCoroutine( SnapIntoSlotRoutine( item, slotIndex, stackIndex ) );
			return true;
		}

		GetSlotWorldPose( slotIndex, stackIndex, item, out Vector3 pos, out Quaternion rot );
		item.EnterDisplayed( this, pos, rot );
		RefreshSlotCylinder( stack );
		return true;
	}

	bool CanStackOnto( TreasureDefinition placing, SlotStack slot )
	{
		if ( placing == null || slot == null || slot.IsEmpty )
			return false;

		if ( !IsTableStackable( placing ) )
			return false;

		TreasureDefinition occupied = slot.Definition;
		if ( occupied == null && slot.Items.Count > 0 && slot.Items[ 0 ] != null )
			occupied = slot.Items[ 0 ].Definition;
		if ( occupied == null || !IsTableStackable( occupied ) )
			return false;

		// Coins may mix types in one slot; other stackables remain type-locked.
		bool placingCoin = placing.category == TreasureCategory.Coin;
		bool occupiedCoin = occupied.category == TreasureCategory.Coin;
		if ( placingCoin != occupiedCoin )
			return false;
		if ( !placingCoin && placing != occupied )
			return false;

		if ( slot.Footprint != GetItemFootprint( placing ) )
			return false;

		if ( maxStackPerSlot > 0 && slot.Count >= maxStackPerSlot )
			return false;

		return true;
	}

	static bool IsTableStackable( TreasureDefinition definition )
	{
		return definition != null && definition.canStack;
	}

	static Vector2Int GetItemFootprint( TreasureDefinition definition )
	{
		if ( definition == null )
			return Vector2Int.one;
		if ( definition.category == TreasureCategory.Coin )
			return Vector2Int.one;
		return definition.GetCartGridSize();
	}

	bool IsRectangleFree( int ox, int oy, Vector2Int footprint )
	{
		int cols = GridColumns;
		int rowCount = GridRows;
		if ( ox < 0 || oy < 0 || ox + footprint.x > cols || oy + footprint.y > rowCount )
			return false;

		for ( int y = 0; y < footprint.y; y++ )
		{
			for ( int x = 0; x < footprint.x; x++ )
			{
				if ( _cellStacks[ ox + x, oy + y ] != null )
					return false;
			}
		}

		return true;
	}

	void MarkFootprint( int ox, int oy, Vector2Int footprint, SlotStack stack )
	{
		for ( int y = 0; y < footprint.y; y++ )
		{
			for ( int x = 0; x < footprint.x; x++ )
				_cellStacks[ ox + x, oy + y ] = stack;
		}
	}

	void ClearFootprint( SlotStack stack )
	{
		if ( stack == null || _cellStacks == null )
			return;

		for ( int y = 0; y < stack.Footprint.y; y++ )
		{
			for ( int x = 0; x < stack.Footprint.x; x++ )
			{
				int cx = stack.OriginX + x;
				int cy = stack.OriginY + y;
				if ( cx < 0 || cy < 0 || cx >= GridColumns || cy >= GridRows )
					continue;

				if ( _cellStacks[ cx, cy ] == stack )
					_cellStacks[ cx, cy ] = null;
			}
		}
	}

	void RemapSlotToOrigin( ref int slotIndex )
	{
		if ( !TryGetCell( slotIndex, out int ox, out int oy ) || _cellStacks == null )
			return;

		SlotStack stack = _cellStacks[ ox, oy ];
		if ( stack == null )
			return;

		slotIndex = SlotIndex( stack.OriginX, stack.OriginY );
	}

	SlotStack GetOriginStack( int slotIndex )
	{
		RemapSlotToOrigin( ref slotIndex );
		if ( !TryGetCell( slotIndex, out int ox, out int oy ) || _cellStacks == null )
			return null;

		SlotStack stack = _cellStacks[ ox, oy ];
		if ( stack == null )
			return null;
		if ( stack.OriginX != ox || stack.OriginY != oy )
			return null;
		return stack;
	}

	bool TryGetCell( int slotIndex, out int ox, out int oy )
	{
		ox = 0;
		oy = 0;
		int cols = GridColumns;
		int count = SlotCount;
		if ( slotIndex < 0 || slotIndex >= count )
			return false;

		ox = slotIndex % cols;
		oy = slotIndex / cols;
		return true;
	}

	int SlotIndex( int ox, int oy )
	{
		return oy * GridColumns + ox;
	}

	IEnumerator SnapIntoSlotRoutine( TreasureItem item, int slotIndex, int stackIndex )
	{
		SlotStack stack = GetOriginStack( slotIndex );
		if ( item == null || stack == null )
			yield break;

		item.BeginFlight();
		GetSlotWorldPose( slotIndex, stackIndex, item, out Vector3 endWorldPos, out Quaternion endWorldRot );

		Transform t = item.transform;
		Vector3 startPos = t.position;
		Quaternion startRot = t.rotation;
		Vector3 startScale = t.lossyScale;
		Vector3 endScale = item.GetWorldScale();
		bool flipCoin = CoinFlipMotion.IsCoin( item );

		float duration = flipCoin ? Mathf.Max( snapDuration, CoinFlipMotion.DefaultDuration ) : Mathf.Max( snapDuration, CoinFlipMotion.DefaultItemArcDuration );
		float arcHeight = flipCoin ? CoinFlipMotion.DefaultArcHeight : CoinFlipMotion.DefaultItemArcHeight;
		float spins = flipCoin ? CoinFlipMotion.DefaultSpins : 0f;
		float elapsed = 0f;

		while ( elapsed < duration )
		{
			if ( item == null )
				yield break;

			if ( !stack.Items.Contains( item ) )
			{
				item.EndFlight();
				yield break;
			}

			elapsed += Time.deltaTime;
			float u = Mathf.Clamp01( elapsed / duration );

			if ( flipCoin )
			{
				t.position = CoinFlipMotion.EvaluateArcPosition( startPos, endWorldPos, u, arcHeight );
				t.rotation = CoinFlipMotion.EvaluateFlipRotation( startRot, endWorldRot, startPos, endWorldPos, u, spins );
				ApplyWorldScaleAsLocal( t, Vector3.Lerp( startScale, endScale, CoinFlipMotion.SmoothStep( u ) ) );
			}
			else
			{
				float ease = CoinFlipMotion.SmoothStep( u );
				float bounce = 1f + ( bounceScale - 1f ) * Mathf.Sin( u * Mathf.PI );
				t.position = CoinFlipMotion.EvaluateArcPosition( startPos, endWorldPos, u, arcHeight );
				t.rotation = Quaternion.Slerp( startRot, endWorldRot, ease );
				ApplyWorldScaleAsLocal( t, Vector3.Lerp( startScale, endScale, ease ) * bounce );
			}

			yield return null;
		}

		if ( item == null )
			yield break;

		item.EndFlight();

		if ( stack.Items.Contains( item ) )
		{
			GetSlotWorldPose( slotIndex, stackIndex, item, out endWorldPos, out endWorldRot );
			item.EnterDisplayed( this, endWorldPos, endWorldRot );
			RefreshSlotCylinder( stack );
			PlayTreasurePlaceFeedback( item );
		}
	}

	static void PlayTreasurePlaceFeedback( TreasureItem item )
	{
		if ( item == null )
			return;

		if ( ArtifactPlantFeedback.IsArtifact( item ) )
		{
			ArtifactPlantFeedback.PlayOn( item );
			TreasureInteractSfx.PlayPlace( item );
			return;
		}

		if ( CoinGemInteractFeedback.IsCoinOrGem( item ) )
			CoinGemInteractFeedback.PlayPlace( item );

		TreasureInteractSfx.PlayPlace( item );
	}

	void RestackSlot( SlotStack slot )
	{
		if ( slot == null )
			return;

		int slotIndex = SlotIndex( slot.OriginX, slot.OriginY );
		for ( int i = 0; i < slot.Items.Count; i++ )
		{
			TreasureItem member = slot.Items[ i ];
			if ( member == null || member.IsInFlight )
				continue;

			GetSlotWorldPose( slotIndex, i, member, out Vector3 pos, out Quaternion rot );
			member.EnterDisplayed( this, pos, rot );
		}

		RefreshSlotCylinder( slot );
	}

	void RefreshSlotCylinder( SlotStack slot )
	{
		if ( slot == null )
			return;

		Transform area = displayArea != null ? displayArea : transform;
		Vector3 localBase = GetFootprintLocalBase( slot.OriginX, slot.OriginY, slot.Footprint );
		CoinStackCylinderVisual visual = slot.Cylinder;
		CoinColumnCylinderBinder.Bind(
			ref visual,
			area,
			slot.Items,
			snap: false,
			localPosition: localBase,
			localRotation: slot.LocalRotation,
			hostName: CoinColumnCylinderBinder.HostChildName + "_Mixed_" + SlotIndex( slot.OriginX, slot.OriginY ) );
		slot.Cylinder = visual;
	}

	void GetSlotWorldPose( int slotIndex, int stackIndex, TreasureItem item, out Vector3 worldPos, out Quaternion worldRot )
	{
		RemapSlotToOrigin( ref slotIndex );
		if ( !TryGetCell( slotIndex, out int ox, out int oy ) )
		{
			worldPos = transform.position;
			worldRot = transform.rotation;
			return;
		}

		SlotStack stack = GetOriginStack( slotIndex );
		TreasureDefinition def = item != null ? item.Definition : null;
		if ( def == null && stack != null )
			def = stack.Definition;

		Vector2Int footprint = stack != null ? stack.Footprint : GetItemFootprint( def );
		GetCellWorldPose( ox, oy, stackIndex, item, stack, footprint, out worldPos, out worldRot );
	}

	void GetSlotBaseWorldPose( int slotIndex, out Vector3 worldPos, out Quaternion worldRot )
	{
		RemapSlotToOrigin( ref slotIndex );
		if ( !TryGetCell( slotIndex, out int ox, out int oy ) )
		{
			worldPos = transform.position;
			worldRot = transform.rotation;
			return;
		}

		SlotStack stack = GetOriginStack( slotIndex );
		Vector2Int footprint = stack != null ? stack.Footprint : Vector2Int.one;
		GetCellWorldPose( ox, oy, 0, null, stack, footprint, out worldPos, out worldRot );
	}

	void GetCellWorldPose(
		int ox,
		int oy,
		int stackIndex,
		TreasureItem item,
		SlotStack stack,
		Vector2Int footprint,
		out Vector3 worldPos,
		out Quaternion worldRot )
	{
		Transform area = displayArea != null ? displayArea : transform;
		Vector3 local = GetFootprintLocalBase( ox, oy, footprint );
		local.y += GetStackHeightForIndex( stack, stackIndex, item );
		worldPos = area.TransformPoint( local );
		Quaternion localRot = stack != null ? stack.LocalRotation : Quaternion.identity;
		worldRot = area.rotation * localRot;
	}

	Vector3 GetFootprintLocalBase( int ox, int oy, Vector2Int footprint )
	{
		footprint = new Vector2Int( Mathf.Max( 1, footprint.x ), Mathf.Max( 1, footprint.y ) );
		Vector3 local = ComputeSlotLocalPosition( SlotIndex( ox, oy ) );
		if ( footprint.x > 1 || footprint.y > 1 )
		{
			Vector3 far = local;
			far.x += ( footprint.x - 1 ) * Mathf.Max( 0.01f, slotSpacing );
			far.z -= ( footprint.y - 1 ) * Mathf.Max( 0.01f, slotSpacing );
			local = ( local + far ) * 0.5f;
		}

		return local;
	}

	float GetStackHeightForIndex( SlotStack slot, int stackIndex, TreasureItem placing )
	{
		if ( slot == null )
			return TreasureStackSpacing.GetStep( placing ) * Mathf.Max( 0, stackIndex );

		return TreasureStackSpacing.GetOffsetForIndex( slot.Items, placing, stackIndex );
	}

	static void AppendSlotOutlineRenderers( SlotStack slot, List<Renderer> renderers )
	{
		if ( slot == null || renderers == null )
			return;

		if ( slot.Cylinder != null )
		{
			Transform host = slot.Cylinder.transform.parent;
			GameObject root = host != null ? host.gameObject : slot.Cylinder.gameObject;
			HoverOutlineTargetUtility.AppendEnabledMeshRenderers( root, renderers );
		}

		for ( int i = 0; i < slot.Items.Count; i++ )
		{
			TreasureItem member = slot.Items[ i ];
			if ( member == null )
				continue;

			HoverOutlineTargetUtility.AppendEnabledMeshRenderers( member.gameObject, renderers );
		}
	}

	static void ApplyWorldScaleAsLocal( Transform t, Vector3 desiredLossy )
	{
		if ( t.parent == null )
		{
			t.localScale = desiredLossy;
			return;
		}

		Vector3 parentLossy = t.parent.lossyScale;
		t.localScale = new Vector3(
			SafeDiv( desiredLossy.x, parentLossy.x ),
			SafeDiv( desiredLossy.y, parentLossy.y ),
			SafeDiv( desiredLossy.z, parentLossy.z ) );
	}

	static float SafeDiv( float a, float b )
	{
		return Mathf.Abs( b ) < 0.0001f ? a : a / b;
	}

	void RebuildSlots()
	{
		_cellStacks = new SlotStack[ GridColumns, GridRows ];
		_stacks.Clear();
		_itemCount = 0;
		_allItems.Clear();
	}

	Vector3 ComputeSlotLocalPosition( int index )
	{
		return DisplayTableSlotLayout.GetSlotLocalPosition( index, rows, columns, slotSpacing, margin );
	}

	void EnsureDisplayArea()
	{
		if ( displayArea == null )
			displayArea = transform;
	}

	void EnsureCollider()
	{
		_collider = GetComponent<Collider>();
		if ( _collider == null )
			_collider = GetComponentInChildren<Collider>( true );

		if ( _collider == null )
		{
			Debug.LogError(
				"MixedDisplayTableInteractable on '" + name + "' requires a Collider on this object or a child.",
				this );
		}
	}

	void OnDrawGizmos()
	{
		if ( drawLayoutGizmosAlways )
			DrawLayoutGizmos();
	}

	void OnDrawGizmosSelected()
	{
		DrawLayoutGizmos();
	}

	void DrawLayoutGizmos()
	{
		Transform area = displayArea != null ? displayArea : transform;
		DisplayTableSlotLayout.DrawLayoutGizmos(
			area,
			rows,
			columns,
			slotSpacing,
			margin,
			new Color( 0.25f, 0.85f, 1f, 0.9f ),
			new Color( 0.25f, 0.85f, 1f, 0.35f ) );

		if ( !Application.isPlaying || _stacks == null )
			return;

		Gizmos.color = new Color( 1f, 0.55f, 0.15f, 0.85f );
		for ( int s = 0; s < _stacks.Count; s++ )
		{
			SlotStack stack = _stacks[ s ];
			if ( stack == null || stack.IsEmpty )
				continue;

			DrawFootprintGizmo( area, stack.OriginX, stack.OriginY, stack.Footprint );
		}
	}

	void DrawFootprintGizmo( Transform area, int ox, int oy, Vector2Int footprint )
	{
		if ( area == null )
			return;

		footprint = new Vector2Int( Mathf.Max( 1, footprint.x ), Mathf.Max( 1, footprint.y ) );
		Vector3 localA = ComputeSlotLocalPosition( SlotIndex( ox, oy ) );
		Vector3 localB = localA;
		localB.x += ( footprint.x - 1 ) * Mathf.Max( 0.01f, slotSpacing );
		localB.z -= ( footprint.y - 1 ) * Mathf.Max( 0.01f, slotSpacing );
		Vector3 localCenter = ( localA + localB ) * 0.5f;
		Vector3 localSize = new Vector3(
			Mathf.Abs( localB.x - localA.x ) + slotSpacing * 0.85f,
			0.02f,
			Mathf.Abs( localB.z - localA.z ) + slotSpacing * 0.85f );
		Matrix4x4 prev = Gizmos.matrix;
		Gizmos.matrix = area.localToWorldMatrix;
		Gizmos.DrawWireCube( localCenter, localSize );
		Gizmos.matrix = prev;
	}

#if UNITY_EDITOR
	public void DrawLayoutSceneHandles()
	{
		Transform area = displayArea != null ? displayArea : transform;
		if ( area == null )
			return;

		UnityEditor.Handles.color = new Color( 1f, 0.85f, 0.2f, 0.95f );
		int count = DisplayTableSlotLayout.SlotCount( rows, columns );
		for ( int i = 0; i < count; i++ )
		{
			Vector3 world = area.TransformPoint(
				DisplayTableSlotLayout.GetSlotLocalPosition( i, rows, columns, slotSpacing, margin ) );
			UnityEditor.Handles.Label( world + area.up * 0.05f, i.ToString() );
		}

		if ( !Application.isPlaying || _stacks == null )
			return;

		UnityEditor.Handles.color = new Color( 1f, 0.55f, 0.15f, 0.95f );
		for ( int s = 0; s < _stacks.Count; s++ )
		{
			SlotStack stack = _stacks[ s ];
			if ( stack == null || stack.IsEmpty )
				continue;

			Vector3 localA = ComputeSlotLocalPosition( SlotIndex( stack.OriginX, stack.OriginY ) );
			Vector3 localB = localA;
			localB.x += ( stack.Footprint.x - 1 ) * Mathf.Max( 0.01f, slotSpacing );
			localB.z -= ( stack.Footprint.y - 1 ) * Mathf.Max( 0.01f, slotSpacing );
			Vector3 center = area.TransformPoint( ( localA + localB ) * 0.5f );
			UnityEditor.Handles.Label(
				center + area.up * 0.08f,
				stack.Footprint.x + "x" + stack.Footprint.y );
		}
	}
#endif

	void RefreshCountLabel()
	{
		if ( countLabel == null )
			return;

		countLabel.text = _itemCount.ToString();
	}

	void PublishChanged()
	{
		EventBus.Publish( new MixedDisplayTableChangedEvent
		{
			Table = this,
			ItemCount = _itemCount,
			SlotCount = SlotCount
		} );
	}

	static void NotifySortedDelta( TreasureDefinition definition, int delta )
	{
		TreasureCounterManager manager = TreasureCounterManager.Instance;
		if ( manager != null && definition != null )
			manager.NotifySortedDelta( definition, delta );
	}
}
