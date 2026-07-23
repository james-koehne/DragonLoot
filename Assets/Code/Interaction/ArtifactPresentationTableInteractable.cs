using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Presentation table with hand-placed slots; each slot accepts one specific artifact definition.
/// Placements are permanent. Empty slots show a cyan hologram of the required artifact.
/// </summary>
[RequireComponent( typeof( Collider ) )]
public class ArtifactPresentationTableInteractable : InteractableBase, ITreasureOwner, ITreasurePlacementTarget, IPermanentTreasureDisplayOwner
{
	[Header( "Slots" )]
	[SerializeField]
	List<ArtifactPresentationSlotEntry> slots = new List<ArtifactPresentationSlotEntry>();

	[Header( "Placement" )]
	[SerializeField]
	[Min( 0.05f )]
	float snapDuration = 0.25f;

	[SerializeField]
	[Min( 1f )]
	float bounceScale = 1.15f;

	[Tooltip( "Euler rotation applied to every artifact relative to its slot anchor." )]
	[SerializeField]
	Vector3 socketRotation;

	[Header( "Feedback" )]
	[SerializeField]
	Text countLabel;

	[SerializeField]
	GameObject completedHighlight;

	[SerializeField]
	ArtifactPresentationSlotIndicators slotIndicators;

	[Header( "Editor Gizmos" )]
	[SerializeField]
	bool drawGizmosAlways;

	readonly List<TreasureItem> _displayedItems = new List<TreasureItem>();
	TreasureItem[] _occupants;
	int _currentCount;
	bool _isComplete;
	int _aimedSlotIndex = -1;
	bool _aimedSlotValid;
	int _aimFeedbackFrame = -1;

	public TreasureOwnerKind OwnerKind => TreasureOwnerKind.DisplayCabinet;
	public IReadOnlyList<ArtifactPresentationSlotEntry> Slots => slots;
	public int CurrentCount => _currentCount;
	public int SlotCount => slots != null ? slots.Count : 0;
	public int Capacity => SlotCount;
	public bool IsComplete => _isComplete;
	public IReadOnlyList<TreasureItem> DisplayedItems => _displayedItems;
	public int AimedSlotIndex => _aimedSlotIndex;
	public bool AimedSlotValid => _aimedSlotValid;
	public bool IsAimFeedbackFresh => _aimFeedbackFrame == Time.frameCount;
	public Vector3 SocketRotation => socketRotation;

	protected virtual void Reset()
	{
		SetInteractionName( "Artifact Presentation Table" );
	}

	protected virtual void Awake()
	{
		EnsureOccupants();
		SyncSlotVolumes();
		EnsureSlotIndicators();
		ApplyInteractionName();
		RefreshCountLabel();
		SetCompletedVisual( false );
	}

	protected virtual void OnValidate()
	{
		snapDuration = Mathf.Max( 0.05f, snapDuration );
		bounceScale = Mathf.Max( 1f, bounceScale );
		EnsureOccupants();
#if UNITY_EDITOR
		if ( !Application.isPlaying )
			UnityEditor.EditorApplication.delayCall += DeferredEditorSetup;
#endif
	}

#if UNITY_EDITOR
	void DeferredEditorSetup()
	{
		if ( this == null )
			return;

		if ( UnityEditor.PrefabUtility.IsPartOfPrefabAsset( this ) )
			return;

		if ( !gameObject.scene.IsValid() )
			return;

		EnsureSlotVolumes();
		if ( slotIndicators == null )
			slotIndicators = GetComponentInChildren<ArtifactPresentationSlotIndicators>();
		if ( slotIndicators != null )
			slotIndicators.Bind( this );
	}
#endif

	protected virtual void LateUpdate()
	{
		if ( _aimFeedbackFrame != Time.frameCount )
			ClearAimFeedback();
	}

	protected virtual void OnDestroy()
	{
		StopAllCoroutines();
	}

	public void ReleaseTreasure( TreasureItem item )
	{
	}

	public bool TryCollectPickupColumn(
		TreasureItem selected,
		List<TreasureItem> results,
		Ray aimRay,
		bool hasAimRay )
	{
		if ( results != null )
			results.Clear();
		return false;
	}

	public void Remove( TreasureItem item )
	{
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
		if ( item == null || !IsAvailable )
			return false;

		return TryResolveAimedSlot( item, in query, out _, out bool valid ) && valid;
	}

	public bool TryGetPlacementPreview( TreasureItem item, in PlacementQuery query, out PlacementPreview preview )
	{
		preview = default;
		ClearAimFeedback();

		if ( item == null )
			return false;

		if ( !TryResolveAimedSlot( item, in query, out int slotIndex, out bool valid ) )
		{
			preview.Position = transform.position;
			preview.Rotation = transform.rotation;
			preview.Scale = item.GetWorldScale();
			preview.IsValid = false;
			return true;
		}

		GetSlotWorldPose( slotIndex, out Vector3 pos, out Quaternion rot );
		preview.Position = pos;
		preview.Rotation = rot;
		preview.Scale = item.GetWorldScale();
		preview.IsValid = valid;

		_aimedSlotIndex = slotIndex;
		_aimedSlotValid = valid;
		_aimFeedbackFrame = Time.frameCount;
		return true;
	}

	public void ClearAimFeedback()
	{
		_aimedSlotIndex = -1;
		_aimedSlotValid = false;
	}

	public bool TryPlace( TreasureItem item, in PlacementQuery query )
	{
		if ( item == null )
			return false;

		if ( !TryResolveAimedSlot( item, in query, out int slotIndex, out bool valid ) || !valid )
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

		_occupants[ slotIndex ] = removed;
		if ( !_displayedItems.Contains( removed ) )
			_displayedItems.Add( removed );
		_currentCount++;
		RefreshCountLabel();
		PublishChanged();
		ClearAimFeedback();

		if ( slotIndicators != null )
			slotIndicators.RefreshSlot( slotIndex, true );

		StartCoroutine( SnapIntoSlotRoutine( removed, slotIndex ) );
		return true;
	}

	public bool IsSlotOccupied( int slotIndex )
	{
		if ( _occupants == null || slotIndex < 0 || slotIndex >= _occupants.Length )
			return false;

		return _occupants[ slotIndex ] != null;
	}

	public TreasureDefinition GetRequiredArtifact( int slotIndex )
	{
		if ( slots == null || slotIndex < 0 || slotIndex >= slots.Count )
			return null;

		return slots[ slotIndex ].requiredArtifact;
	}

	public void GetSlotWorldPose( int slotIndex, out Vector3 worldPos, out Quaternion worldRot )
	{
		worldPos = transform.position;
		worldRot = transform.rotation;

		if ( slots == null || slotIndex < 0 || slotIndex >= slots.Count )
			return;

		Transform anchor = slots[ slotIndex ].anchor;
		Vector3 offset = slots[ slotIndex ].rotationOffset;
		ArtifactPresentationDisplayPose.GetItemRootWorldPose(
			anchor,
			socketRotation,
			offset,
			out worldPos,
			out worldRot );
	}

	public Quaternion GetSocketWorldRotation( int slotIndex )
	{
		GetSlotWorldPose( slotIndex, out _, out Quaternion worldRot );
		return worldRot;
	}

	public void ApplyDisplayedItemPose( TreasureItem item, int slotIndex )
	{
		if ( item == null || slots == null || slotIndex < 0 || slotIndex >= slots.Count )
			return;

		Transform anchor = GetSlotParent( slotIndex );
		Vector3 offset = slots[ slotIndex ].rotationOffset;
		ArtifactPresentationDisplayPose.ParentItemToSlotAnchor(
			item.transform,
			anchor,
			socketRotation,
			offset,
			item.Definition );
	}

	Transform GetSlotParent( int slotIndex )
	{
		if ( slots == null || slotIndex < 0 || slotIndex >= slots.Count )
			return transform;

		Transform anchor = slots[ slotIndex ].anchor;
		return anchor != null ? anchor : transform;
	}

	bool IsAvailable => !_isComplete || HasEmptySlot();

	bool HasEmptySlot()
	{
		if ( _occupants == null )
			return false;

		for ( int i = 0; i < _occupants.Length; i++ )
		{
			if ( _occupants[ i ] == null )
				return true;
		}

		return false;
	}

	bool TryResolveAimedSlot( TreasureItem item, in PlacementQuery query, out int slotIndex, out bool valid )
	{
		slotIndex = -1;
		valid = false;
		if ( item == null || _occupants == null || slots == null )
			return false;

		if ( !TryResolveAimedSlotIndex( in query, out slotIndex ) )
		{
			if ( !query.AutoFindValidSlot || !TryFindNearestValidSlot( item, in query, out slotIndex ) )
				return false;

			valid = true;
			return true;
		}

		valid = !IsSlotOccupied( slotIndex ) && AcceptsForSlot( slotIndex, item.Definition );
		if ( valid || !query.AutoFindValidSlot )
			return true;

		if ( !TryFindNearestValidSlot( item, in query, out slotIndex ) )
			return true;

		valid = true;
		return true;
	}

	bool TryFindNearestValidSlot( TreasureItem item, in PlacementQuery query, out int slotIndex )
	{
		slotIndex = -1;
		if ( item == null || slots == null || slots.Count == 0 )
			return false;

		Vector3 reference = query.HasHit ? query.Hit.point : transform.position;
		float bestDistSq = float.MaxValue;

		for ( int i = 0; i < slots.Count; i++ )
		{
			if ( IsSlotOccupied( i ) || !AcceptsForSlot( i, item.Definition ) )
				continue;

			if ( !TryGetSlotPlaneDistanceSq( reference, i, out float distSq ) )
				continue;
			if ( distSq >= bestDistSq )
				continue;

			bestDistSq = distSq;
			slotIndex = i;
		}

		return slotIndex >= 0;
	}

	bool TryResolveAimedSlotIndex( in PlacementQuery query, out int slotIndex )
	{
		slotIndex = -1;
		if ( !query.HasHit || slots == null || slots.Count == 0 )
			return false;

		TreasureItem hitItem = query.Hit.collider.GetComponentInParent<TreasureItem>();
		if ( hitItem != null && TryFindSlotContaining( hitItem, out slotIndex ) )
			return true;

		if ( ArtifactPresentationSlotVolume.TryResolveSlotIndex( query.Hit.collider, out slotIndex ) )
			return true;

		return TryFindNearestSlotOnTablePlane( query.Hit.point, out slotIndex, out _ );
	}

	bool TryFindNearestSlotOnTablePlane( Vector3 worldPoint, out int slotIndex, out float distSq )
	{
		slotIndex = -1;
		distSq = float.MaxValue;
		if ( slots == null || slots.Count == 0 )
			return false;

		Vector3 local = transform.InverseTransformPoint( worldPoint );
		local.y = 0f;

		for ( int i = 0; i < slots.Count; i++ )
		{
			Transform anchor = slots[ i ].anchor;
			if ( anchor == null )
				continue;

			Vector3 slotLocal = transform.InverseTransformPoint( anchor.position );
			slotLocal.y = 0f;
			float d = ( slotLocal - local ).sqrMagnitude;
			if ( d >= distSq )
				continue;

			distSq = d;
			slotIndex = i;
		}

		return slotIndex >= 0;
	}

	bool TryGetSlotPlaneDistanceSq( Vector3 worldPoint, int slotIndex, out float distSq )
	{
		distSq = float.MaxValue;
		if ( slots == null || slotIndex < 0 || slotIndex >= slots.Count )
			return false;

		Transform anchor = slots[ slotIndex ].anchor;
		if ( anchor == null )
			return false;

		Vector3 local = transform.InverseTransformPoint( worldPoint );
		local.y = 0f;
		Vector3 slotLocal = transform.InverseTransformPoint( anchor.position );
		slotLocal.y = 0f;
		distSq = ( slotLocal - local ).sqrMagnitude;
		return true;
	}

	void SyncSlotVolumes()
	{
		if ( slots == null )
			return;

		for ( int i = 0; i < slots.Count; i++ )
		{
			Transform anchor = slots[ i ].anchor;
			if ( anchor == null )
				continue;

			ArtifactPresentationSlotVolume volume = anchor.GetComponent<ArtifactPresentationSlotVolume>();
			if ( volume != null )
				volume.Configure( this, i );
		}
	}

#if UNITY_EDITOR
	void EnsureSlotVolumes()
	{
		if ( slots == null )
			return;

		const float volumeSize = 0.22f;
		const float volumeHeight = 0.06f;

		for ( int i = 0; i < slots.Count; i++ )
		{
			Transform anchor = slots[ i ].anchor;
			if ( anchor == null )
				continue;

			BoxCollider box = anchor.GetComponent<BoxCollider>();
			if ( box == null )
			{
				box = anchor.gameObject.AddComponent<BoxCollider>();
				box.center = Vector3.zero;
				box.size = new Vector3( volumeSize, volumeHeight, volumeSize );
			}

			ArtifactPresentationSlotVolume volume = anchor.GetComponent<ArtifactPresentationSlotVolume>();
			if ( volume == null )
				volume = anchor.gameObject.AddComponent<ArtifactPresentationSlotVolume>();
			volume.Configure( this, i );
		}
	}
#endif

	bool TryFindSlotContaining( TreasureItem item, out int slotIndex )
	{
		slotIndex = -1;
		if ( item == null || _occupants == null )
			return false;

		for ( int i = 0; i < _occupants.Length; i++ )
		{
			if ( _occupants[ i ] != item )
				continue;

			slotIndex = i;
			return true;
		}

		return false;
	}

	bool AcceptsForSlot( int slotIndex, TreasureDefinition definition )
	{
		if ( definition == null || definition.category != TreasureCategory.Artifact )
			return false;

		if ( slots == null || slotIndex < 0 || slotIndex >= slots.Count )
			return false;

		TreasureDefinition required = slots[ slotIndex ].requiredArtifact;
		return required != null && definition == required;
	}

	IEnumerator SnapIntoSlotRoutine( TreasureItem item, int slotIndex )
	{
		if ( item == null || _occupants == null || slotIndex < 0 || slotIndex >= _occupants.Length )
			yield break;

		item.BeginFlight();
		GetSlotWorldPose( slotIndex, out Vector3 endWorldPos, out Quaternion endWorldRot );

		Transform t = item.transform;
		Vector3 startPos = t.position;
		Quaternion startRot = t.rotation;
		Vector3 startScale = t.lossyScale;
		Vector3 endScale = item.GetWorldScale();

		float duration = Mathf.Max( snapDuration, CoinFlipMotion.DefaultItemArcDuration );
		float arcHeight = CoinFlipMotion.DefaultItemArcHeight;
		float elapsed = 0f;

		while ( elapsed < duration )
		{
			if ( item == null )
				yield break;

			elapsed += Time.deltaTime;
			float u = Mathf.Clamp01( elapsed / duration );
			float ease = CoinFlipMotion.SmoothStep( u );
			float bounce = 1f + ( bounceScale - 1f ) * Mathf.Sin( u * Mathf.PI );

			t.position = CoinFlipMotion.EvaluateArcPosition( startPos, endWorldPos, u, arcHeight );
			t.rotation = Quaternion.Slerp( startRot, endWorldRot, ease );
			item.ApplyDesiredWorldScale( Vector3.Lerp( startScale, endScale, ease ) * bounce );

			yield return null;
		}

		if ( item == null )
			yield break;

		item.EndFlight();

		if ( _occupants[ slotIndex ] == item )
		{
			Transform anchor = GetSlotParent( slotIndex );
			GetSlotWorldPose( slotIndex, out endWorldPos, out endWorldRot );
			item.EnterDisplayed( this, anchor, endWorldPos, endWorldRot );
			ApplyDisplayedItemPose( item, slotIndex );
		}

		if ( slotIndicators != null )
			slotIndicators.RefreshSlot( slotIndex, true );

		if ( !_isComplete && EvaluateComplete() )
		{
			_isComplete = true;
			SetCompletedVisual( true );
			PublishCompleted();
		}
	}

	bool EvaluateComplete()
	{
		if ( _occupants == null )
			return false;

		for ( int i = 0; i < _occupants.Length; i++ )
		{
			if ( _occupants[ i ] == null )
				return false;
		}

		return _occupants.Length > 0;
	}

	void EnsureOccupants()
	{
		int count = SlotCount;
		if ( _occupants == null || _occupants.Length != count )
		{
			TreasureItem[] next = new TreasureItem[ count ];
			if ( _occupants != null )
			{
				int copy = Mathf.Min( _occupants.Length, count );
				for ( int i = 0; i < copy; i++ )
					next[ i ] = _occupants[ i ];
			}

			_occupants = next;
		}
	}

	void EnsureSlotIndicators()
	{
		if ( slotIndicators == null )
			slotIndicators = GetComponentInChildren<ArtifactPresentationSlotIndicators>();

		if ( slotIndicators != null )
			slotIndicators.Bind( this );
	}

	void ApplyInteractionName()
	{
		SetInteractionName( "Artifact Presentation Table" );
	}

	void RefreshCountLabel()
	{
		if ( countLabel == null )
			return;

		countLabel.text = _currentCount + "/" + Capacity;
	}

	void SetCompletedVisual( bool completed )
	{
		if ( completedHighlight != null )
			completedHighlight.SetActive( completed );
	}

	void PublishChanged()
	{
		EventBus.Publish( new ArtifactPresentationTableChangedEvent
		{
			Table = this,
			Count = CurrentCount,
			Capacity = Capacity
		} );
	}

	void PublishCompleted()
	{
		EventBus.Publish( new ArtifactPresentationTableCompletedEvent
		{
			Table = this
		} );
	}

#if UNITY_EDITOR
	protected virtual void OnDrawGizmos()
	{
		if ( !drawGizmosAlways && !UnityEditor.Selection.Contains( gameObject ) )
			return;

		DrawSlotGizmos();
	}

	protected virtual void OnDrawGizmosSelected()
	{
		if ( drawGizmosAlways )
			return;

		DrawSlotGizmos();
	}

	void DrawSlotGizmos()
	{
		if ( slots == null )
			return;

		for ( int i = 0; i < slots.Count; i++ )
		{
			Transform anchor = slots[ i ].anchor;
			if ( anchor == null )
				continue;

			bool occupied = Application.isPlaying && IsSlotOccupied( i );
			Gizmos.color = occupied ? new Color( 0.2f, 0.85f, 0.35f, 0.85f ) : new Color( 0.2f, 0.95f, 1f, 0.85f );
			Gizmos.DrawWireSphere( anchor.position, 0.06f );
		}
	}
#endif
}
