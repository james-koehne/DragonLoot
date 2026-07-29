using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Typed display surface: accepts one <see cref="TreasureDefinition"/> and snaps matching
/// carried items into a generated horizontal slot grid (Displayed state).
/// If the accepted treasure is stackable (<see cref="TreasureDefinition.canStack"/>), items
/// pile vertically in a slot; otherwise each slot holds one item (gems).
/// </summary>
[RequireComponent( typeof( Collider ) )]
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

	protected DisplaySlot[] Slots;
	readonly List<TreasureItem> _displayedItems = new List<TreasureItem>();
	int _currentCount;
	bool _isComplete;

	public abstract TreasureOwnerKind OwnerKind { get; }

	protected abstract TreasureCategory RequiredCategory { get; }

	protected abstract string DefaultInteractionName { get; }

	public TreasureDefinition AcceptedTreasure => acceptedTreasure;
	public int CurrentCount => _currentCount;
	public int SlotCount => rows * columns;
	public bool AllowsVerticalStack => acceptedTreasure != null && acceptedTreasure.canStack;

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
		if ( item == null )
			return false;

		if ( !TryResolveNearestSlot( item, in query, out int slotIndex, out int stackIndex, out bool valid ) )
			return false;

		GetSlotWorldPose( slotIndex, stackIndex, item, out Vector3 pos, out Quaternion rot );
		preview.Position = pos;
		preview.Rotation = rot;
		preview.Scale = item.GetWorldScale();
		// Slot occupancy alone is not enough — wrong treasure type must show as invalid.
		preview.IsValid = Accepts( item.Definition ) && valid;
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
		if ( item == null || Slots == null )
			return false;

		if ( !TryResolveAimedSlot( in query, out slotIndex ) )
			return false;

		if ( TryGetStackIndexForSlot( item, slotIndex, out stackIndex ) )
		{
			valid = true;
			return true;
		}

		if ( query.AutoFindValidSlot
			&& TryFindNearestValidSlot( item, in query, out slotIndex, out stackIndex ) )
		{
			valid = true;
			return true;
		}

		stackIndex = Slots[ slotIndex ].Count;
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
			{
				yield break;
			}

			if ( !Slots[ slotIndex ].Items.Contains( item ) )
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

		if ( Slots[ slotIndex ].Items.Contains( item ) )
		{
			GetSlotWorldPose( slotIndex, stackIndex, item, out endWorldPos, out endWorldRot );
			item.EnterDisplayed( this, endWorldPos, endWorldRot );
			RefreshSlotCylinder( slotIndex );
		}

		PlayPlaceFx();

		if ( !_isComplete && EvaluateComplete() )
		{
			_isComplete = true;
			SetCompletedVisual( true );
			PublishCompleted();
			PlayCompleteFx();
		}
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
		if ( Slots == null || slotIndex < 0 || slotIndex >= Slots.Length )
			return;

		DisplaySlot slot = Slots[ slotIndex ];
		Transform area = displayArea != null ? displayArea : transform;
		CoinStackCylinderVisual visual = slot.Cylinder;
		CoinColumnCylinderBinder.Bind(
			ref visual,
			area,
			slot.Items,
			snap: true,
			localPosition: slot.LocalBasePosition,
			localRotation: slot.LocalRotation,
			hostName: CoinColumnCylinderBinder.HostChildName + "_Typed_" + slotIndex );
		slot.Cylinder = visual;
	}

	void GetSlotWorldPose( int slotIndex, int stackIndex, TreasureItem item, out Vector3 worldPos, out Quaternion worldRot )
	{
		Transform area = displayArea != null ? displayArea : transform;
		DisplaySlot slot = Slots[ slotIndex ];
		Vector3 local = slot.LocalBasePosition;
		local.y += GetStackHeightForIndex( slot, stackIndex, item );
		worldPos = area.TransformPoint( local );
		worldRot = area.rotation * slot.LocalRotation;
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
		int row;
		int col;

		switch ( fillDirection )
		{
			default:
				row = index / columns;
				col = index % columns;
				break;
		}

		float width = ( columns - 1 ) * slotSpacing;
		float depth = ( rows - 1 ) * slotSpacing;
		float startX = -width * 0.5f;
		float startZ = depth * 0.5f;

		float x = startX + col * slotSpacing;
		float z = startZ - row * slotSpacing;
		return new Vector3( x, margin, z );
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

	/// <summary>Hook for place SFX / sparkle (assets later).</summary>
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
