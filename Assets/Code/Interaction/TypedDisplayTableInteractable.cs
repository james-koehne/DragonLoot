using System.Collections;
using System.Collections.Generic;

using FeedbackSystem;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Typed display surface: accepts one <see cref="TreasureDefinition"/> and snaps matching
/// carried items into a generated horizontal slot grid (Displayed state).
/// If the accepted treasure is stackable (<see cref="TreasureDefinition.canStack"/>), items
/// pile vertically in a slot; otherwise each slot holds one item (gems).
/// </summary>
public abstract class TypedDisplayTableInteractable : InteractableBase, ITreasureOwner, ITreasurePlacementTarget, ITreasureDisplayStackOwner
{
	public enum FillDirection
	{
		LeftRightTopBottom
	}

	protected class DisplaySlot
	{
		public Vector3 LocalBasePosition;
		public Quaternion LocalRotation;
		public readonly List<TreasureItem> Items = new List<TreasureItem>();
		public CoinStackCylinderVisual Cylinder;

		public bool IsEmpty => Items.Count == 0;
		public int Count => Items.Count;
	}

	[Header( "Acceptance" )]
	[Tooltip( "Only items matching this TreasureDefinition asset are accepted." )]
	[SerializeField]
	protected TreasureDefinition acceptedTreasure;

	[Header( "Layout" )]
	[Tooltip( "Local space for generated slots on the horizontal display plane. Defaults to this transform." )]
	[SerializeField]
	protected Transform displayArea;

	[SerializeField]
	[Min( 1 )]
	protected int rows = 3;

	[SerializeField]
	[Min( 1 )]
	protected int columns = 4;

	[SerializeField]
	[Min( 0.01f )]
	protected float slotSpacing = 0.2f;

	[SerializeField]
	[Min( 0f )]
	protected float margin = 0.05f;

	[SerializeField]
	protected FillDirection fillDirection = FillDirection.LeftRightTopBottom;

	[Header( "Stacking" )]
	[Tooltip( "0 = unlimited when the accepted treasure is stackable. Ignored for non-stackable treasure." )]
	[SerializeField]
	[Min( 0 )]
	protected int maxStackPerSlot = 0;

	[Header( "Placement" )]
	[SerializeField]
	[Min( 0.05f )]
	protected float snapDuration = 0.25f;

	[SerializeField]
	[Min( 1f )]
	protected float bounceScale = 1.15f;

	[Header( "Feedback" )]
	[SerializeField]
	protected Text countLabel;

	[SerializeField]
	protected GameObject completedHighlight;

	[Header( "Editor" )]
	[SerializeField]
	bool drawLayoutGizmosAlways;

	protected Collider TableCollider;
	protected DisplaySlot[] Slots;
	readonly List<TreasureItem> _displayedItems = new List<TreasureItem>();
	int _currentCount;
	bool _isComplete;
	int _previewOutlineSlot = -1;

	public abstract TreasureOwnerKind OwnerKind { get; }

	protected abstract TreasureCategory RequiredCategory { get; }

	protected abstract string DefaultInteractionName { get; }

	public TreasureDefinition AcceptedTreasure => acceptedTreasure;
	public int CurrentCount => _currentCount;
	public int SlotCount => DisplayTableSlotLayout.SlotCount( rows, columns );
	public virtual bool AllowsVerticalStack =>
		acceptedTreasure != null && ( acceptedTreasure.canStack || acceptedTreasure.usesInterleavedBarStack );

	protected bool UsesInterleavedBarLayout =>
		acceptedTreasure != null && acceptedTreasure.usesInterleavedBarStack;

	/// <summary>Completion target: slot count, or slots × max stack when capped.</summary>
	public int Capacity
	{
		get
		{
			if ( AllowsVerticalStack && maxStackPerSlot > 0 )
				return SlotCount * maxStackPerSlot;
			return SlotCount;
		}
	}

	public bool IsComplete => _isComplete;
	public IReadOnlyList<TreasureItem> DisplayedItems => _displayedItems;

	protected virtual void Reset()
	{
		SetInteractionName( DefaultInteractionName );
	}

	protected virtual void Awake()
	{
		EnsureCollider();
		EnsureDisplayArea();
		RebuildSlots();
		ApplyInteractionName();
		RefreshCountLabel();
		SetCompletedVisual( false );
	}

	protected virtual void OnValidate()
	{
		rows = Mathf.Max( 1, rows );
		columns = Mathf.Max( 1, columns );
		slotSpacing = Mathf.Max( 0.01f, slotSpacing );
		margin = Mathf.Max( 0f, margin );
		maxStackPerSlot = Mathf.Max( 0, maxStackPerSlot );
		snapDuration = Mathf.Max( 0.05f, snapDuration );
		bounceScale = Mathf.Max( 1f, bounceScale );
	}

	protected virtual void OnDestroy()
	{
		StopAllCoroutines();
	}

	public void ReleaseTreasure( TreasureItem item )
	{
		Remove( item );
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
		if ( selected == null || Slots == null )
			return false;

		for ( int s = 0; s < Slots.Length; s++ )
		{
			DisplaySlot slot = Slots[ s ];
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
		if ( selected == null || Slots == null )
			return false;

		for ( int s = 0; s < Slots.Length; s++ )
		{
			if ( Slots[ s ].Items.IndexOf( selected ) < 0 )
				continue;

			slotIndex = s;
			return true;
		}

		return false;
	}

	public int GetSlotCount( int slotIndex )
	{
		if ( Slots == null || slotIndex < 0 || slotIndex >= Slots.Length )
			return 0;

		return Slots[ slotIndex ].Count;
	}

	public void AppendSlotOutlineRenderers( TreasureItem selected, List<Renderer> renderers )
	{
		if ( !TryGetSlotIndex( selected, out int slotIndex ) )
			return;

		AppendSlotOutlineRenderers( Slots[ slotIndex ], renderers );
	}

	public bool TryConsumeSlotDefinitions(
		int slotIndex,
		List<TreasureDefinition> into,
		out Vector3 contact,
		out Quaternion rotation )
	{
		contact = transform.position;
		rotation = transform.rotation;
		if ( into == null || Slots == null || slotIndex < 0 || slotIndex >= Slots.Length )
			return false;

		DisplaySlot slot = Slots[ slotIndex ];
		if ( slot.Count <= 0 )
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

		slot.Items.Clear();
		for ( int i = 0; i < taken.Count; i++ )
		{
			TreasureItem member = taken[ i ];
			into.Add( member.Definition );
			_displayedItems.Remove( member );
			_currentCount = Mathf.Max( 0, _currentCount - 1 );
			NotifySortedDelta( member.Definition, -1 );
			EventBus.Publish( new TreasureRemovedEvent
			{
				Target = this,
				Item = member,
				Definition = member.Definition
			} );
			TreasureItemFactory.Despawn( member );
		}

		_isComplete = false;
		SetCompletedVisual( false );
		RestackSlot( slotIndex );
		RefreshSlotCylinder( slotIndex, animate: true );
		RefreshCountLabel();
		PublishChanged();
		OnDisplaySlotChanged( slotIndex );
		return into.Count > 0;
	}

	public int GetSlotCoinAppendCapacity( int slotIndex, TreasureDefinition probe )
	{
		if ( !Accepts( probe ) || Slots == null )
			return 0;
		if ( slotIndex < 0 || slotIndex >= Slots.Length )
			return 0;

		DisplaySlot slot = Slots[ slotIndex ];
		if ( slot.IsEmpty )
		{
			if ( !AllowsVerticalStack )
				return 1;
			return maxStackPerSlot > 0 ? maxStackPerSlot : int.MaxValue;
		}

		if ( !CanStackOnto( slot ) )
			return 0;

		int max = maxStackPerSlot > 0 ? maxStackPerSlot : int.MaxValue;
		return Mathf.Max( 0, max - slot.Count );
	}

	public bool TryGetSlotAppendPose( int slotIndex, out Vector3 contact, out Quaternion rotation )
	{
		contact = transform.position;
		rotation = transform.rotation;
		if ( Slots == null || slotIndex < 0 || slotIndex >= Slots.Length )
			return false;

		if ( UsesInterleavedBarLayout )
		{
			DisplaySlot slot = Slots[ slotIndex ];
			TreasureItem probe = slot.Count > 0 ? slot.Items[ 0 ] : null;
			GetSlotWorldPose( slotIndex, slot.Count, probe, out contact, out rotation );
			return true;
		}

		GetSlotBaseWorldPose( slotIndex, out contact, out rotation );
		float height = 0f;
		DisplaySlot slotItems = Slots[ slotIndex ];
		for ( int i = 0; i < slotItems.Items.Count; i++ )
			height += TreasureStackSpacing.GetStep( slotItems.Items[ i ] );
		contact += Vector3.up * height;
		return true;
	}

	public int TryAppendSlotDefinitions( int slotIndex, IReadOnlyList<TreasureDefinition> definitions )
	{
		if ( definitions == null || definitions.Count == 0 || Slots == null )
			return 0;
		if ( slotIndex < 0 || slotIndex >= Slots.Length )
			return 0;

		DisplaySlot slot = Slots[ slotIndex ];
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

			slot.Items.Add( visual );
			if ( !_displayedItems.Contains( visual ) )
				_displayedItems.Add( visual );
			_currentCount++;
			visual.EnterDisplayed( this, pos, rot );
			NotifySortedDelta( def, 1 );
			PlayTreasurePlaceFeedback( visual );
			added++;
		}

		if ( added <= 0 )
			return 0;

		RefreshSlotCylinder( slotIndex, animate: true );
		RefreshCountLabel();
		PublishChanged();
		if ( !_isComplete && _currentCount >= Capacity )
		{
			_isComplete = true;
			SetCompletedVisual( true );
			PublishCompleted();
			PlayCompleteFx();
		}

		return added;
	}

	/// <summary>
	/// Quiet start-of-play fill: spawn into a slot without place SFX or punch juice.
	/// </summary>
	protected int SpawnDisplayedStack( int slotIndex, TreasureDefinition definition, int count )
	{
		if ( definition == null || count <= 0 || Slots == null )
			return 0;
		if ( slotIndex < 0 || slotIndex >= Slots.Length )
			return 0;

		int added = 0;
		for ( int i = 0; i < count; i++ )
		{
			if ( GetSlotCoinAppendCapacity( slotIndex, definition ) <= 0 )
				break;

			TryGetSlotAppendPose( slotIndex, out Vector3 pos, out Quaternion rot );
			TreasureItem visual = TreasureItemFactory.RentVisualCoin( definition, pos, rot );
			if ( visual == null )
				visual = TreasureItemFactory.SpawnFallback( definition, pos, rot, null );
			if ( visual == null )
				break;

			Slots[ slotIndex ].Items.Add( visual );
			if ( !_displayedItems.Contains( visual ) )
				_displayedItems.Add( visual );
			_currentCount++;
			visual.EnterDisplayed( this, pos, rot );
			NotifySortedDelta( definition, 1 );
			added++;
		}

		if ( added > 0 )
			RefreshSlotCylinder( slotIndex, animate: false );

		return added;
	}

	protected void FinishStartFill()
	{
		RefreshCountLabel();
		if ( _currentCount <= 0 )
			return;

		PublishChanged();
		if ( !_isComplete && EvaluateComplete() )
		{
			_isComplete = true;
			SetCompletedVisual( true );
			PublishCompleted();
		}
	}

	public void Remove( TreasureItem item )
	{
		if ( item == null || Slots == null )
			return;

		for ( int s = 0; s < Slots.Length; s++ )
		{
			DisplaySlot slot = Slots[ s ];
			int index = slot.Items.IndexOf( item );
			if ( index < 0 )
				continue;

			slot.Items.RemoveAt( index );
			_displayedItems.Remove( item );
			_currentCount = Mathf.Max( 0, _currentCount - 1 );
			_isComplete = false;
			SetCompletedVisual( false );
			RestackSlot( s );
			RefreshCountLabel();
			PublishChanged();
			NotifySortedDelta( item.Definition, -1 );
			EventBus.Publish( new TreasureRemovedEvent
			{
				Target = this,
				Item = item,
				Definition = item.Definition
			} );
			OnDisplaySlotChanged( s );
			return;
		}
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
		if ( item == null || !IsAvailable || acceptedTreasure == null )
			return false;

		if ( !Accepts( item.Definition ) )
			return false;

		if ( !TryResolveNearestSlot( item, in query, out _, out _, out bool valid ) )
			return false;

		return valid;
	}

	public bool TryGetPlacementPreview( TreasureItem item, in PlacementQuery query, out PlacementPreview preview )
	{
		preview = default;
		_previewOutlineSlot = -1;
		if ( item == null )
			return false;

		if ( !TryResolveNearestSlot( item, in query, out int slotIndex, out int stackIndex, out bool valid ) )
			return false;

		_previewOutlineSlot = slotIndex;
		Vector3 scale = item.GetWorldScale();
		// Slot occupancy alone is not enough — wrong treasure type must show as invalid.
		bool placementValid = Accepts( item.Definition ) && valid;

		GetSlotWorldPose( slotIndex, stackIndex, item, out Vector3 pos, out Quaternion itemRot );
		preview.SetItemMesh( pos, itemRot, scale, placementValid );
		return true;
	}

	/// <summary>
	/// Appends mesh renderers for the last placement-preview slot (cylinder + visible coins).
	/// </summary>
	public void AppendPreviewStackOutlineRenderers( List<Renderer> renderers )
	{
		if ( renderers == null || Slots == null || _previewOutlineSlot < 0 || _previewOutlineSlot >= Slots.Length )
			return;

		AppendSlotOutlineRenderers( Slots[ _previewOutlineSlot ], renderers );
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

		if ( !carry.TryConsumeActive( out TreasureItem removed ) || removed == null || removed != item )
		{
			if ( removed != null && removed != item )
				removed.EnterPhysics( removed.transform.position, removed.transform.rotation );
			return false;
		}

		// Reserve the slot immediately so concurrent places get the next index
		// and this tween can finish without being cancelled.
		Slots[ slotIndex ].Items.Add( removed );
		if ( !_displayedItems.Contains( removed ) )
			_displayedItems.Add( removed );
		_currentCount++;
		RefreshCountLabel();
		PublishChanged();
		NotifySortedDelta( acceptedTreasure, 1 );

		StartCoroutine( SnapIntoSlotRoutine( removed, slotIndex, stackIndex ) );
		return true;
	}

	/// <summary>Hold-F whole coin stack: resolve a placeable slot (lowest pile on coin tables).</summary>
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
		if ( probe == null || !CanPlace( probe, in query ) )
			return false;

		if ( !TryResolveNearestSlot( probe, in query, out slotIndex, out _, out bool valid ) || !valid )
			return false;

		if ( GetSlotCoinAppendCapacity( slotIndex, probe.Definition ) <= 0 )
			return false;

		return TryGetSlotAppendPose( slotIndex, out contact, out rotation );
	}

	/// <summary>
	/// Coin tables: aim at an existing pile to stack onto it; aim at the table body (or an
	/// unstackable pile) fills the shortest slot in top-left order.
	/// Other typed tables snap to the nearest generated slot based on aim; invalid slots
	/// still resolve so the preview stays on the hovered pile instead of jumping to table center.
	/// </summary>
	protected virtual bool UsesLowestPilePlacement => false;

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
		if ( item == null || Slots == null )
			return false;

		if ( UsesLowestPilePlacement )
		{
			// Prefer stacking onto the aimed column when the ray hits a displayed coin in that slot.
			if ( TryResolveAimedOccupiedSlot( in query, out int aimedSlot )
				&& TryGetStackIndexForSlot( item, aimedSlot, out int aimedStack ) )
			{
				slotIndex = aimedSlot;
				stackIndex = aimedStack;
				valid = true;
				return true;
			}

			return TryResolveLowestPileSlot( item, out slotIndex, out stackIndex, out valid );
		}

		if ( !TryResolveAimedSlot( in query, out slotIndex ) )
			return false;

		int hoveredSlot = slotIndex;
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

		// Invalid: still preview at the hovered slot (top of occupied stack or base).
		slotIndex = hoveredSlot;
		if ( slotIndex < 0 || slotIndex >= Slots.Length )
			return false;

		stackIndex = Slots[ slotIndex ].Count;
		valid = false;
		return true;
	}

	/// <summary>
	/// True when aim hits a displayed treasure in a slot, or lands close to an occupied column
	/// (cylinder visuals often have no collider, so the table mesh under a pile still counts).
	/// </summary>
	bool TryResolveAimedOccupiedSlot( in PlacementQuery query, out int slotIndex )
	{
		slotIndex = -1;
		if ( !query.HasHit || query.Hit.collider == null || Slots == null )
			return false;

		TreasureItem hitItem = query.Hit.collider.GetComponentInParent<TreasureItem>();
		if ( hitItem != null && TryFindSlotContaining( hitItem, out slotIndex ) )
			return true;

		if ( !TryFindNearestSlot( query.Hit.point, out int nearest, out float distSq ) )
			return false;

		if ( nearest < 0 || nearest >= Slots.Length || Slots[ nearest ].IsEmpty )
			return false;

		float maxDist = Mathf.Max( 0.05f, slotSpacing * 0.55f );
		if ( distSq > maxDist * maxDist )
			return false;

		slotIndex = nearest;
		return true;
	}

	/// <summary>
	/// Scan left-to-right, top-to-bottom and pick the placeable slot with the fewest coins.
	/// Ties keep the first (top-left) slot. Full tables still resolve for an invalid preview.
	/// </summary>
	bool TryResolveLowestPileSlot(
		TreasureItem item,
		out int slotIndex,
		out int stackIndex,
		out bool valid )
	{
		slotIndex = -1;
		stackIndex = 0;
		valid = false;
		if ( item == null || Slots == null )
			return false;

		int lowestCount = int.MaxValue;
		for ( int i = 0; i < Slots.Length; i++ )
		{
			if ( !TryGetStackIndexForSlot( item, i, out int candidateStack ) )
				continue;

			int count = Slots[ i ].Count;
			if ( count >= lowestCount )
				continue;

			lowestCount = count;
			slotIndex = i;
			stackIndex = candidateStack;
		}

		if ( slotIndex >= 0 )
		{
			valid = true;
			return true;
		}

		lowestCount = int.MaxValue;
		for ( int i = 0; i < Slots.Length; i++ )
		{
			int count = Slots[ i ].Count;
			if ( count >= lowestCount )
				continue;

			lowestCount = count;
			slotIndex = i;
			stackIndex = count;
		}

		if ( slotIndex < 0 )
			return false;

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
		if ( item == null || Slots == null )
			return false;

		Vector3 reference = GetSlotSearchReference( in query );
		float bestDistSq = float.MaxValue;

		for ( int i = 0; i < Slots.Length; i++ )
		{
			if ( !TryGetStackIndexForSlot( item, i, out int candidateStack ) )
				continue;

			if ( !TryGetSlotDistanceSq( reference, i, out float distSq ) )
				continue;

			if ( distSq >= bestDistSq )
				continue;

			bestDistSq = distSq;
			slotIndex = i;
			stackIndex = candidateStack;
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
		if ( Slots == null || slotIndex < 0 || slotIndex >= Slots.Length )
			return false;

		Transform area = displayArea != null ? displayArea : transform;
		Vector3 local = area.InverseTransformPoint( worldPoint );
		local.y = 0f;
		Vector3 slotLocal = Slots[ slotIndex ].LocalBasePosition;
		slotLocal.y = 0f;
		distSq = ( slotLocal - local ).sqrMagnitude;
		return true;
	}

	bool TryResolveAimedSlot( in PlacementQuery query, out int slotIndex )
	{
		slotIndex = -1;
		if ( !query.HasHit || query.Hit.collider == null || Slots == null )
			return false;

		TreasureItem hitItem = query.Hit.collider.GetComponentInParent<TreasureItem>();
		if ( hitItem != null && TryFindSlotContaining( hitItem, out slotIndex ) )
			return true;

		return TryFindNearestSlot( query.Hit.point, out slotIndex, out _ );
	}

	bool TryGetStackIndexForSlot( TreasureItem item, int slotIndex, out int stackIndex )
	{
		stackIndex = 0;
		if ( item == null || Slots == null || slotIndex < 0 || slotIndex >= Slots.Length )
			return false;

		DisplaySlot slot = Slots[ slotIndex ];
		if ( slot.IsEmpty )
		{
			stackIndex = 0;
			return true;
		}

		if ( !CanStackOnto( slot ) )
			return false;

		stackIndex = slot.Count;
		return true;
	}

	bool TryFindSlotContaining( TreasureItem item, out int slotIndex )
	{
		slotIndex = -1;
		if ( item == null || Slots == null )
			return false;

		for ( int i = 0; i < Slots.Length; i++ )
		{
			if ( Slots[ i ].Items.IndexOf( item ) < 0 )
				continue;

			slotIndex = i;
			return true;
		}

		return false;
	}

	bool TryFindNearestSlot( Vector3 worldPoint, out int slotIndex, out float distSq )
	{
		slotIndex = -1;
		distSq = float.MaxValue;
		if ( Slots == null )
			return false;

		Transform area = displayArea != null ? displayArea : transform;
		Vector3 local = area.InverseTransformPoint( worldPoint );
		local.y = 0f;

		for ( int i = 0; i < Slots.Length; i++ )
		{
			Vector3 slotLocal = Slots[ i ].LocalBasePosition;
			slotLocal.y = 0f;
			float d = ( slotLocal - local ).sqrMagnitude;
			if ( d >= distSq )
				continue;

			distSq = d;
			slotIndex = i;
		}

		return slotIndex >= 0;
	}

	bool CanStackOnto( DisplaySlot slot )
	{
		if ( slot == null || slot.IsEmpty || !AllowsVerticalStack )
			return false;

		if ( maxStackPerSlot > 0 && slot.Count >= maxStackPerSlot )
			return false;

		return true;
	}

	bool Accepts( TreasureDefinition definition )
	{
		if ( definition == null || acceptedTreasure == null )
			return false;

		if ( definition.category != RequiredCategory )
			return false;

		return definition == acceptedTreasure;
	}

	IEnumerator SnapIntoSlotRoutine( TreasureItem item, int slotIndex, int stackIndex )
	{
		if ( item == null || Slots == null || slotIndex < 0 || slotIndex >= Slots.Length )
			yield break;

		item.BeginFlight();
		yield return AnimateTreasureItemFlightToSlot( item, slotIndex, stackIndex, requireReservedInSlot: true );

		if ( item == null )
			yield break;

		if ( Slots[ slotIndex ].Items.Contains( item ) )
		{
			GetSlotWorldPose( slotIndex, stackIndex, item, out Vector3 endWorldPos, out Quaternion endWorldRot );
			item.EnterDisplayed( this, endWorldPos, endWorldRot );
			RefreshSlotVisual( slotIndex, animate: true );
			PlayTreasurePlaceFeedback( item );
		}

		PlayPlaceFx();
		TryMarkCompleteIfNeeded();
		OnDisplaySlotChanged( slotIndex );
	}

	/// <summary>Hook after a slot's displayed coins change from a player place or pickup.</summary>
	protected virtual void OnDisplaySlotChanged( int slotIndex )
	{
	}

	/// <summary>
	/// Arc / flip tween for a coin already removed from its slot (leveling) or reserved in the destination slot (place).
	/// </summary>
	protected IEnumerator AnimateTreasureItemFlightToSlot(
		TreasureItem item,
		int slotIndex,
		int stackIndex,
		bool requireReservedInSlot,
		float arcHeightOverride = -1f,
		float speedScale = 1f )
	{
		if ( item == null || Slots == null || slotIndex < 0 || slotIndex >= Slots.Length )
			yield break;

		GetSlotWorldPose( slotIndex, stackIndex, item, out Vector3 endWorldPos, out Quaternion endWorldRot );

		Transform t = item.transform;
		Vector3 startPos = t.position;
		Quaternion startRot = t.rotation;
		Vector3 startScale = t.lossyScale;
		Vector3 endScale = item.GetWorldScale();
		bool flipCoin = CoinFlipMotion.IsCoin( item );

		float scale = speedScale > 0.0001f ? speedScale : 1f;
		float duration = flipCoin ? Mathf.Max( snapDuration, CoinFlipMotion.DefaultDuration ) : Mathf.Max( snapDuration, CoinFlipMotion.DefaultItemArcDuration );
		duration = Mathf.Max( 0.04f, duration / scale );
		float arcHeight = arcHeightOverride > 0f
			? arcHeightOverride
			: flipCoin ? CoinFlipMotion.DefaultArcHeight : CoinFlipMotion.DefaultItemArcHeight;
		float spins = flipCoin ? CoinFlipMotion.DefaultSpins : 0f;
		float elapsed = 0f;

		while ( elapsed < duration )
		{
			if ( item == null )
				yield break;

			if ( requireReservedInSlot && !Slots[ slotIndex ].Items.Contains( item ) )
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
	}

	protected void RefreshSlotVisual( int slotIndex, bool animate )
	{
		RestackSlot( slotIndex );
		RefreshSlotCylinder( slotIndex, animate );
	}

	protected void RefreshDisplayCountAndPublish()
	{
		RefreshCountLabel();
		PublishChanged();
	}

	protected void TryMarkCompleteIfNeeded()
	{
		if ( _isComplete || !EvaluateComplete() )
			return;

		_isComplete = true;
		SetCompletedVisual( true );
		PublishCompleted();
		PlayCompleteFx();
	}

	protected bool TryPopSlotTopItem( int slotIndex, out TreasureItem item )
	{
		item = null;
		if ( Slots == null || slotIndex < 0 || slotIndex >= Slots.Length )
			return false;

		DisplaySlot slot = Slots[ slotIndex ];
		if ( slot.Count <= 0 )
			return false;

		int topIndex = slot.Count - 1;
		item = slot.Items[ topIndex ];
		slot.Items.RemoveAt( topIndex );
		return item != null;
	}

	protected bool TryPushSlotItem( int slotIndex, TreasureItem item )
	{
		if ( item == null || Slots == null || slotIndex < 0 || slotIndex >= Slots.Length )
			return false;

		Slots[ slotIndex ].Items.Add( item );
		if ( !_displayedItems.Contains( item ) )
			_displayedItems.Add( item );
		return true;
	}

	protected int DisplaySlotCapacity => Slots != null ? Slots.Length : 0;

	protected int MaxStackPerSlotLimit => maxStackPerSlot;

	protected static void PlayTreasurePlaceFeedback( TreasureItem item )
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

	void RestackSlot( int slotIndex )
	{
		if ( Slots == null || slotIndex < 0 || slotIndex >= Slots.Length )
			return;

		DisplaySlot slot = Slots[ slotIndex ];
		for ( int i = 0; i < slot.Items.Count; i++ )
		{
			TreasureItem member = slot.Items[ i ];
			if ( member == null || member.IsInFlight )
				continue;

			GetSlotWorldPose( slotIndex, i, member, out Vector3 pos, out Quaternion rot );
			member.EnterDisplayed( this, pos, rot );
		}

		RefreshSlotCylinder( slotIndex );
	}

	void RefreshSlotCylinder( int slotIndex )
	{
		RefreshSlotCylinder( slotIndex, animate: false );
	}

	void RefreshSlotCylinder( int slotIndex, bool animate )
	{
		if ( UsesInterleavedBarLayout )
			return;
		if ( Slots == null || slotIndex < 0 || slotIndex >= Slots.Length )
			return;

		DisplaySlot slot = Slots[ slotIndex ];
		Transform area = displayArea != null ? displayArea : transform;
		CoinStackCylinderVisual visual = slot.Cylinder;
		CoinColumnCylinderBinder.Bind(
			ref visual,
			area,
			slot.Items,
			snap: !animate,
			localPosition: slot.LocalBasePosition,
			localRotation: slot.LocalRotation,
			hostName: CoinColumnCylinderBinder.HostChildName + "_Typed_" + slotIndex );
		slot.Cylinder = visual;
		if ( animate )
			PlaySlotPunch( slot );
	}

	void PlaySlotPunch( DisplaySlot slot )
	{
		
	}

	protected virtual void GetSlotWorldPose( int slotIndex, int stackIndex, TreasureItem item, out Vector3 worldPos, out Quaternion worldRot )
	{
		if ( UsesInterleavedBarLayout )
		{
			if ( item != null )
				GoldBarStackLattice.CacheFromItem( item );
			GetSlotBaseWorldPose( slotIndex, out Vector3 contact, out Quaternion baseRot );
			TreasureDefinition definition = item != null ? item.Definition : acceptedTreasure;
			GoldBarStackLattice.TryGetWorldPose( stackIndex, definition, item, contact, baseRot, out worldPos, out worldRot );
			return;
		}

		Transform area = displayArea != null ? displayArea : transform;
		DisplaySlot slot = Slots[ slotIndex ];
		Vector3 local = slot.LocalBasePosition;
		local.y += GetStackHeightForIndex( slot, stackIndex, item );
		worldPos = area.TransformPoint( local );
		worldRot = area.rotation * slot.LocalRotation;
	}

	void GetSlotBaseWorldPose( int slotIndex, out Vector3 worldPos, out Quaternion worldRot )
	{
		Transform area = displayArea != null ? displayArea : transform;
		DisplaySlot slot = Slots[ slotIndex ];
		worldPos = area.TransformPoint( slot.LocalBasePosition );
		worldRot = area.rotation * slot.LocalRotation;
	}

	float GetSlotStackHeight( int slotIndex )
	{
		if ( Slots == null || slotIndex < 0 || slotIndex >= Slots.Length )
			return TreasureStackSpacing.FallbackStep;

		DisplaySlot slot = Slots[ slotIndex ];
		float height = 0f;
		for ( int i = 0; i < slot.Items.Count; i++ )
			height += TreasureStackSpacing.GetStep( slot.Items[ i ] );

		return Mathf.Max( TreasureStackSpacing.FallbackStep, height );
	}

	float GetSlotStackDiameter( int slotIndex, Vector3 placingScale )
	{
		float diameter = Mathf.Max( placingScale.x, placingScale.z );
		if ( Slots == null || slotIndex < 0 || slotIndex >= Slots.Length )
			return diameter;

		DisplaySlot slot = Slots[ slotIndex ];
		for ( int i = 0; i < slot.Items.Count; i++ )
		{
			TreasureItem member = slot.Items[ i ];
			if ( member == null )
				continue;

			Vector3 scale = member.GetWorldScale();
			diameter = Mathf.Max( diameter, Mathf.Max( scale.x, scale.z ) );
		}

		return diameter;
	}

	static void AppendSlotOutlineRenderers( DisplaySlot slot, List<Renderer> renderers )
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

	float GetStackHeightForIndex( DisplaySlot slot, int stackIndex, TreasureItem placing )
	{
		if ( slot == null )
			return TreasureStackSpacing.GetStep( placing ) * Mathf.Max( 0, stackIndex );

		return TreasureStackSpacing.GetOffsetForIndex( slot.Items, placing, stackIndex );
	}

	bool EvaluateComplete()
	{
		if ( Slots == null )
			return false;

		if ( !AllowsVerticalStack )
			return _currentCount >= SlotCount;

		if ( maxStackPerSlot > 0 )
		{
			for ( int i = 0; i < Slots.Length; i++ )
			{
				if ( Slots[ i ].Count < maxStackPerSlot )
					return false;
			}

			return true;
		}

		// Unlimited stack height: complete once every slot has at least one item.
		for ( int i = 0; i < Slots.Length; i++ )
		{
			if ( Slots[ i ].IsEmpty )
				return false;
		}

		return true;
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
		int capacity = SlotCount;
		Slots = new DisplaySlot[ capacity ];
		for ( int i = 0; i < capacity; i++ )
		{
			Slots[ i ] = new DisplaySlot
			{
				LocalBasePosition = ComputeSlotLocalPosition( i ),
				LocalRotation = GetSlotLocalRotation()
			};
		}

		_currentCount = 0;
		_isComplete = false;
		_displayedItems.Clear();
	}

	/// <summary>
	/// Slot centers on a single horizontal plane (local XZ). Row/column index never changes base Y.
	/// </summary>
	Vector3 ComputeSlotLocalPosition( int index )
	{
		return DisplayTableSlotLayout.GetSlotLocalPosition( index, rows, columns, slotSpacing, margin );
	}

	protected virtual Quaternion GetSlotLocalRotation()
	{
		return Quaternion.identity;
	}

	void EnsureDisplayArea()
	{
		if ( displayArea == null )
			displayArea = transform;
	}

	void EnsureCollider()
	{
		TableCollider = GetComponent<Collider>();
		if ( TableCollider == null )
			TableCollider = GetComponentInChildren<Collider>( true );

		if ( TableCollider == null )
		{
			Debug.LogError(
				GetType().Name + " on '" + name + "' requires a Collider on this object or a child.",
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
			new Color( 0.35f, 1f, 0.55f, 0.9f ),
			new Color( 0.35f, 1f, 0.55f, 0.35f ) );
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
	}
#endif

	void ApplyInteractionName()
	{
		if ( acceptedTreasure != null && !string.IsNullOrEmpty( acceptedTreasure.displayName ) )
			SetInteractionName( acceptedTreasure.displayName + " Display" );
		else if ( string.IsNullOrEmpty( InteractionName ) || InteractionName == "Interactable" )
			SetInteractionName( DefaultInteractionName );
	}

	void RefreshCountLabel()
	{
		if ( countLabel == null )
			return;

		countLabel.text = _currentCount + "/" + Capacity;
	}

	protected abstract void PublishChanged();

	protected abstract void PublishCompleted();

	void SetCompletedVisual( bool completed )
	{
		if ( completedHighlight != null )
			completedHighlight.SetActive( completed );
	}

	/// <summary>Hook for place FX / SFX (assets later).</summary>
	protected virtual void PlayPlaceFx()
	{
	}

	/// <summary>Hook for completion FX / SFX (assets later).</summary>
	protected virtual void PlayCompleteFx()
	{
	}

	static void NotifySortedDelta( TreasureDefinition definition, int delta )
	{
		TreasureCounterManager manager = TreasureCounterManager.Instance;
		if ( manager != null && definition != null )
			manager.NotifySortedDelta( definition, delta );
	}
}
