using System.Collections;
using System.Collections.Generic;

using FeedbackSystem;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Open display table: accepts any <see cref="TreasureItem"/>.
/// Slots sit on a horizontal XZ plane. Stackable treasure (<see cref="TreasureDefinition.canStack"/>)
/// can pile vertically in a slot (same definition); non-stackable treasure (gems) uses one item per slot.
/// </summary>
public class MixedDisplayTableInteractable : InteractableBase, ITreasureOwner, ITreasurePlacementTarget, ITreasureDisplayStackOwner
{
	public enum FillDirection
	{
		LeftRightTopBottom
	}

	class SlotStack
	{
		public Vector3 LocalBasePosition;
		public Quaternion LocalRotation;
		public readonly List<TreasureItem> Items = new List<TreasureItem>();
		public CoinStackCylinderVisual Cylinder;

		public bool IsEmpty => Items.Count == 0;
		public int Count => Items.Count;

		public TreasureItem Top => Items.Count > 0 ? Items[ Items.Count - 1 ] : null;
		public TreasureDefinition Definition => Items.Count > 0 && Items[ 0 ] != null ? Items[ 0 ].Definition : null;
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

	[SerializeField]
	FillDirection fillDirection = FillDirection.LeftRightTopBottom;

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

	[Header( "Feedback" )]
	[SerializeField]
	Text countLabel;

	[Header( "Editor" )]
	[SerializeField]
	bool drawLayoutGizmosAlways;

	Collider _collider;
	SlotStack[] _slots;
	readonly List<TreasureItem> _allItems = new List<TreasureItem>();
	int _itemCount;
	int _previewOutlineSlot = -1;
	Feedbacks _placeFeedbacks;

	public TreasureOwnerKind OwnerKind => TreasureOwnerKind.Table;

	public int ItemCount => _itemCount;
	public int SlotCount => DisplayTableSlotLayout.SlotCount( rows, columns );
	public IReadOnlyList<TreasureItem> DisplayedItems => _allItems;
	public Collider TableCollider => _collider;

	public int LayoutRows => rows;
	public int LayoutColumns => columns;
	public float LayoutSlotSpacing => slotSpacing;
	public float LayoutMargin => margin;
	public Transform DisplayArea => displayArea != null ? displayArea : transform;

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
		if ( selected == null || _slots == null )
			return false;

		for ( int s = 0; s < _slots.Length; s++ )
		{
			SlotStack slot = _slots[ s ];
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
		if ( selected == null || _slots == null )
			return false;

		for ( int s = 0; s < _slots.Length; s++ )
		{
			if ( _slots[ s ].Items.IndexOf( selected ) < 0 )
				continue;

			slotIndex = s;
			return true;
		}

		return false;
	}

	public int GetSlotCount( int slotIndex )
	{
		if ( _slots == null || slotIndex < 0 || slotIndex >= _slots.Length )
			return 0;

		return _slots[ slotIndex ].Count;
	}

	public void AppendSlotOutlineRenderers( TreasureItem selected, List<Renderer> renderers )
	{
		if ( !TryGetSlotIndex( selected, out int slotIndex ) )
			return;

		AppendSlotOutlineRenderers( _slots[ slotIndex ], renderers );
	}

	public bool TryConsumeSlotDefinitions(
		int slotIndex,
		List<TreasureDefinition> into,
		out Vector3 contact,
		out Quaternion rotation )
	{
		contact = transform.position;
		rotation = transform.rotation;
		if ( into == null || _slots == null || slotIndex < 0 || slotIndex >= _slots.Length )
			return false;

		SlotStack slot = _slots[ slotIndex ];
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

		RestackSlot( slotIndex );
		RefreshSlotCylinder( slotIndex );
		RefreshCountLabel();
		PublishChanged();
		return into.Count > 0;
	}

	public int GetSlotCoinAppendCapacity( int slotIndex, TreasureDefinition probe )
	{
		if ( probe == null || probe.category != TreasureCategory.Coin || _slots == null )
			return 0;
		if ( !IsTableStackable( probe ) )
			return 0;
		if ( slotIndex < 0 || slotIndex >= _slots.Length )
			return 0;

		SlotStack slot = _slots[ slotIndex ];
		if ( slot.IsEmpty )
			return maxStackPerSlot > 0 ? maxStackPerSlot : int.MaxValue;

		if ( !CanStackOnto( probe, slot ) )
			return 0;

		int max = maxStackPerSlot > 0 ? maxStackPerSlot : int.MaxValue;
		return Mathf.Max( 0, max - slot.Count );
	}

	public bool TryGetSlotAppendPose( int slotIndex, out Vector3 contact, out Quaternion rotation )
	{
		contact = transform.position;
		rotation = transform.rotation;
		if ( _slots == null || slotIndex < 0 || slotIndex >= _slots.Length )
			return false;

		GetSlotBaseWorldPose( slotIndex, out contact, out rotation );
		float height = 0f;
		SlotStack slot = _slots[ slotIndex ];
		for ( int i = 0; i < slot.Items.Count; i++ )
			height += TreasureStackSpacing.GetStep( slot.Items[ i ] );
		contact += Vector3.up * height;
		return true;
	}

	public int TryAppendSlotDefinitions( int slotIndex, IReadOnlyList<TreasureDefinition> definitions )
	{
		if ( definitions == null || definitions.Count == 0 || _slots == null )
			return 0;
		if ( slotIndex < 0 || slotIndex >= _slots.Length )
			return 0;

		SlotStack slot = _slots[ slotIndex ];
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
			if ( !_allItems.Contains( visual ) )
				_allItems.Add( visual );
			_itemCount++;
			visual.EnterDisplayed( this, pos, rot );
			NotifySortedDelta( def, 1 );
			PlayTreasurePlaceFeedback( visual );
			added++;
		}

		if ( added <= 0 )
			return 0;

		RefreshSlotCylinder( slotIndex );
		RefreshCountLabel();
		PublishChanged();
		return added;
	}

	public void Remove( TreasureItem item )
	{
		if ( item == null || _slots == null )
			return;

		for ( int s = 0; s < _slots.Length; s++ )
		{
			SlotStack slot = _slots[ s ];
			int index = slot.Items.IndexOf( item );
			if ( index < 0 )
				continue;

			slot.Items.RemoveAt( index );
			_allItems.Remove( item );
			_itemCount = Mathf.Max( 0, _itemCount - 1 );
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
		if ( item == null || !IsAvailable || item.Definition == null )
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
		if ( item == null )
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
		if ( renderers == null || _slots == null || _previewOutlineSlot < 0 || _previewOutlineSlot >= _slots.Length )
			return;

		AppendSlotOutlineRenderers( _slots[ _previewOutlineSlot ], renderers );
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

		_slots[ slotIndex ].Items.Add( removed );
		if ( !_allItems.Contains( removed ) )
			_allItems.Add( removed );
		_itemCount++;
		RefreshCountLabel();
		PublishChanged();
		NotifySortedDelta( removed.Definition, 1 );

		StartCoroutine( SnapIntoSlotRoutine( removed, slotIndex, stackIndex ) );
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
	/// True when at least one carried coin type can append onto this table (mixed stacks split by type).
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
			if ( def == null )
				continue;

			bool seen = false;
			for ( int j = 0; j < i; j++ )
			{
				if ( definitions[ j ] == def )
				{
					seen = true;
					break;
				}
			}

			if ( seen )
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
		if ( definition == null || !IsAvailable || !IsTableStackable( definition ) || _slots == null )
			return false;

		if ( query.HasHit
			&& TryResolveAimedSlot( in query, out int aimedSlot )
			&& GetSlotCoinAppendCapacity( aimedSlot, definition ) > 0 )
		{
			slotIndex = aimedSlot;
			return TryGetSlotAppendPose( slotIndex, out contact, out rotation );
		}

		Vector3 reference = GetSlotSearchReference( in query );
		float bestDistSq = float.MaxValue;
		int bestSlot = -1;

		for ( int i = 0; i < _slots.Length; i++ )
		{
			if ( GetSlotCoinAppendCapacity( i, definition ) <= 0 )
				continue;

			if ( !TryGetSlotDistanceSq( reference, i, out float distSq ) )
				continue;

			if ( distSq >= bestDistSq )
				continue;

			bestDistSq = distSq;
			bestSlot = i;
		}

		if ( bestSlot < 0 )
			return false;

		slotIndex = bestSlot;
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
		if ( item == null || item.Definition == null || _slots == null )
			return false;

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
		if ( slotIndex < 0 || slotIndex >= _slots.Length )
			return false;

		stackIndex = _slots[ slotIndex ].Count;
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
		if ( item == null || item.Definition == null || _slots == null )
			return false;

		Vector3 reference = GetSlotSearchReference( in query );
		float bestDistSq = float.MaxValue;

		for ( int i = 0; i < _slots.Length; i++ )
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
		if ( _slots == null || slotIndex < 0 || slotIndex >= _slots.Length )
			return false;

		Transform area = displayArea != null ? displayArea : transform;
		Vector3 local = area.InverseTransformPoint( worldPoint );
		local.y = 0f;
		Vector3 slotLocal = _slots[ slotIndex ].LocalBasePosition;
		slotLocal.y = 0f;
		distSq = ( slotLocal - local ).sqrMagnitude;
		return true;
	}

	bool TryResolveAimedSlot( in PlacementQuery query, out int slotIndex )
	{
		slotIndex = -1;
		if ( !query.HasHit || query.Hit.collider == null || _slots == null )
			return false;

		TreasureItem hitItem = query.Hit.collider.GetComponentInParent<TreasureItem>();
		if ( hitItem != null && TryFindSlotContaining( hitItem, out slotIndex ) )
			return true;

		return TryFindNearestSlot( query.Hit.point, out slotIndex, out _ );
	}

	bool TryGetStackIndexForSlot( TreasureItem item, int slotIndex, out int stackIndex )
	{
		stackIndex = 0;
		if ( item == null || item.Definition == null || _slots == null
			|| slotIndex < 0 || slotIndex >= _slots.Length )
			return false;

		SlotStack slot = _slots[ slotIndex ];
		if ( slot.IsEmpty )
		{
			stackIndex = 0;
			return true;
		}

		if ( !CanStackOnto( item.Definition, slot ) )
			return false;

		stackIndex = slot.Count;
		return true;
	}

	bool TryFindSlotContaining( TreasureItem item, out int slotIndex )
	{
		slotIndex = -1;
		if ( item == null || _slots == null )
			return false;

		for ( int i = 0; i < _slots.Length; i++ )
		{
			if ( _slots[ i ].Items.IndexOf( item ) < 0 )
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
		if ( _slots == null )
			return false;

		Transform area = displayArea != null ? displayArea : transform;
		Vector3 local = area.InverseTransformPoint( worldPoint );
		local.y = 0f;

		for ( int i = 0; i < _slots.Length; i++ )
		{
			Vector3 slotLocal = _slots[ i ].LocalBasePosition;
			slotLocal.y = 0f;
			float d = ( slotLocal - local ).sqrMagnitude;
			if ( d >= distSq )
				continue;

			distSq = d;
			slotIndex = i;
		}

		return slotIndex >= 0;
	}

	bool CanStackOnto( TreasureDefinition placing, SlotStack slot )
	{
		if ( placing == null || slot == null || slot.IsEmpty )
			return false;

		if ( !IsTableStackable( placing ) )
			return false;

		TreasureDefinition occupied = slot.Definition;
		if ( occupied == null || !IsTableStackable( occupied ) )
			return false;

		if ( placing != occupied )
			return false;

		if ( maxStackPerSlot > 0 && slot.Count >= maxStackPerSlot )
			return false;

		return true;
	}

	static bool IsTableStackable( TreasureDefinition definition )
	{
		return definition != null && definition.canStack;
	}

	IEnumerator SnapIntoSlotRoutine( TreasureItem item, int slotIndex, int stackIndex )
	{
		if ( item == null || _slots == null || slotIndex < 0 || slotIndex >= _slots.Length )
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

			if ( !_slots[ slotIndex ].Items.Contains( item ) )
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

		if ( _slots[ slotIndex ].Items.Contains( item ) )
		{
			GetSlotWorldPose( slotIndex, stackIndex, item, out endWorldPos, out endWorldRot );
			item.EnterDisplayed( this, endWorldPos, endWorldRot );
			RefreshSlotCylinder( slotIndex );
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

	void RestackSlot( int slotIndex )
	{
		if ( _slots == null || slotIndex < 0 || slotIndex >= _slots.Length )
			return;

		SlotStack slot = _slots[ slotIndex ];
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
		if ( _slots == null || slotIndex < 0 || slotIndex >= _slots.Length )
			return;

		SlotStack slot = _slots[ slotIndex ];
		Transform area = displayArea != null ? displayArea : transform;
		CoinStackCylinderVisual visual = slot.Cylinder;
		CoinColumnCylinderBinder.Bind(
			ref visual,
			area,
			slot.Items,
			snap: false,
			localPosition: slot.LocalBasePosition,
			localRotation: slot.LocalRotation,
			hostName: CoinColumnCylinderBinder.HostChildName + "_Mixed_" + slotIndex );
		slot.Cylinder = visual;
	}

	void GetSlotWorldPose( int slotIndex, int stackIndex, TreasureItem item, out Vector3 worldPos, out Quaternion worldRot )
	{
		Transform area = displayArea != null ? displayArea : transform;
		SlotStack slot = _slots[ slotIndex ];
		Vector3 local = slot.LocalBasePosition;
		local.y += GetStackHeightForIndex( slot, stackIndex, item );
		worldPos = area.TransformPoint( local );
		worldRot = area.rotation * slot.LocalRotation;
	}

	void GetSlotBaseWorldPose( int slotIndex, out Vector3 worldPos, out Quaternion worldRot )
	{
		Transform area = displayArea != null ? displayArea : transform;
		SlotStack slot = _slots[ slotIndex ];
		worldPos = area.TransformPoint( slot.LocalBasePosition );
		worldRot = area.rotation * slot.LocalRotation;
	}

	float GetSlotStackHeight( int slotIndex )
	{
		if ( _slots == null || slotIndex < 0 || slotIndex >= _slots.Length )
			return TreasureStackSpacing.FallbackStep;

		SlotStack slot = _slots[ slotIndex ];
		float height = 0f;
		for ( int i = 0; i < slot.Items.Count; i++ )
			height += TreasureStackSpacing.GetStep( slot.Items[ i ] );

		return Mathf.Max( TreasureStackSpacing.FallbackStep, height );
	}

	float GetSlotStackDiameter( int slotIndex, Vector3 placingScale )
	{
		float diameter = Mathf.Max( placingScale.x, placingScale.z );
		if ( _slots == null || slotIndex < 0 || slotIndex >= _slots.Length )
			return diameter;

		SlotStack slot = _slots[ slotIndex ];
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

	float GetStackHeightForIndex( SlotStack slot, int stackIndex, TreasureItem placing )
	{
		if ( slot == null )
			return TreasureStackSpacing.GetStep( placing ) * Mathf.Max( 0, stackIndex );

		return TreasureStackSpacing.GetOffsetForIndex( slot.Items, placing, stackIndex );
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
		_slots = new SlotStack[ capacity ];
		for ( int i = 0; i < capacity; i++ )
		{
			_slots[ i ] = new SlotStack
			{
				LocalBasePosition = ComputeSlotLocalPosition( i ),
				LocalRotation = Quaternion.identity
			};
		}

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
