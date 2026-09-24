using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Presentation table with hand-placed slots. Each slot can require its own artifact, or every
/// slot can share one required type. Placements are permanent. Empty slots show a cyan hologram.
/// </summary>
public class ArtifactPresentationTableInteractable : InteractableBase, ITreasureOwner, ITreasurePlacementTarget, IPermanentTreasureDisplayOwner
{
	[Header( "Slots" )]
	[Tooltip( "When enabled, every slot uses Shared Required Artifact. Per-slot Required Artifact is ignored." )]
	[SerializeField]
	bool sameArtifactForAllSlots;

	[Tooltip( "Artifact accepted by every slot when Same Artifact For All Slots is enabled." )]
	[SerializeField]
	TreasureDefinition sharedRequiredArtifact;

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

	[Tooltip( "Optional objects that start inactive and are activated when every slot is filled." )]
	[SerializeField]
	List<GameObject> activateOnComplete = new List<GameObject>();

	[Tooltip( "Optional complete FX (lit + SoftShaft). Activated with the display; color can be overridden below." )]
	[SerializeField]
	DisplayCompleteEffect completeEffect;

	[Tooltip( "When enabled, pushes Complete Effect Color into the complete effect on activation." )]
	[SerializeField]
	bool overrideCompleteEffectColor;

	[SerializeField]
	[ColorUsage( true, true )]
	Color completeEffectColor = new Color( 1f, 0.72f, 0.28f, 1f );

	[Tooltip( "When false, hologram slot indicators are not bound or refreshed (artifacts + anchors only)." )]
	[SerializeField]
	bool showSlotIndicators = true;

	[SerializeField]
	ArtifactPresentationSlotIndicators slotIndicators;

	[Header( "Editor Gizmos" )]
	[SerializeField]
	bool drawGizmosAlways;

	const int SlotAimRayBufferSize = 32;

	static readonly RaycastHit[] SlotAimRayHits = new RaycastHit[ SlotAimRayBufferSize ];

	Collider _collider;
	readonly List<TreasureItem> _displayedItems = new List<TreasureItem>();
	TreasureItem[] _occupants;
	int _currentCount;
	bool _isComplete;
	int _aimedSlotIndex = -1;
	bool _aimedSlotValid;
	int _aimFeedbackFrame = -1;

	public TreasureOwnerKind OwnerKind => TreasureOwnerKind.DisplayCabinet;
	public bool SameArtifactForAllSlots => sameArtifactForAllSlots;
	public TreasureDefinition SharedRequiredArtifact => sharedRequiredArtifact;
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
	public Collider TableCollider => _collider;
	public bool ShowSlotIndicators => showSlotIndicators;

	protected virtual void Reset()
	{
		SetInteractionName( "Artifact Presentation Table" );
	}

	protected virtual void Awake()
	{
		EnsureCollider();
		EnsureOccupants();
		RebuildSlotAimVolumes( createMissing: true );
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
		if ( showSlotIndicators )
		{
			if ( slotIndicators == null )
				slotIndicators = GetComponentInChildren<ArtifactPresentationSlotIndicators>();
			if ( slotIndicators != null )
				slotIndicators.Bind( this );
		}
		else if ( slotIndicators != null )
			slotIndicators.ClearForDisabledHolograms();
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
		bool hasAimRay,
		bool hasHitWorldY = false,
		float hitWorldY = 0f )
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

		return TryResolvePlacementSlot( item, in query, out _, out bool valid ) && valid;
	}

	public bool TryGetPlacementPreview( TreasureItem item, in PlacementQuery query, out PlacementPreview preview )
	{
		preview = default;
		ClearAimFeedback();

		if ( item == null )
			return false;

		Vector3 scale = item.GetWorldScale();

		if ( _isComplete )
		{
			preview.SetSuppressed( transform.position, transform.rotation, scale, false );
			return true;
		}

		if ( !TryResolvePlacementSlot( item, in query, out int aimedSlot, out bool feedbackValid ) )
		{
			preview.SetSuppressed( transform.position, transform.rotation, scale, false );
			return true;
		}

		GetSlotWorldPose( aimedSlot, out Vector3 pos, out Quaternion rot );
		// Show the held-item valid/invalid ghost; slot indicators hide the cyan hologram for this slot.
		preview.SetItemMesh( pos, rot, scale, feedbackValid );

		_aimedSlotIndex = aimedSlot;
		_aimedSlotValid = feedbackValid;
		_aimFeedbackFrame = Time.frameCount;
		if ( showSlotIndicators && slotIndicators != null )
			slotIndicators.RefreshAimFeedback();
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

		if ( !TryResolvePlacementSlot( item, in query, out int slotIndex, out bool valid ) || !valid )
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
		SetSlotAimColliderEnabled( slotIndex, false );

		if ( showSlotIndicators && slotIndicators != null )
			slotIndicators.RefreshSlot( slotIndex, true );

		StartCoroutine( SnapIntoSlotRoutine( removed, slotIndex ) );
		return true;
	}

	void SetSlotAimColliderEnabled( int slotIndex, bool enabled )
	{
		if ( slots == null || slotIndex < 0 || slotIndex >= slots.Count )
			return;

		Transform anchor = slots[ slotIndex ].anchor;
		if ( anchor == null )
			return;

		ArtifactPresentationSlotVolume volume = anchor.GetComponent<ArtifactPresentationSlotVolume>();
		if ( volume != null )
			volume.SetAimCollidersEnabled( enabled );
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

		if ( sameArtifactForAllSlots )
			return sharedRequiredArtifact;

		return slots[ slotIndex ].requiredArtifact;
	}

	/// <summary>
	/// True when this slot has no prerequisite, or the prerequisite slot is occupied.
	/// </summary>
	public bool IsSlotPrerequisiteMet( int slotIndex )
	{
		if ( slots == null || slotIndex < 0 || slotIndex >= slots.Count )
			return false;

		ArtifactPresentationSlotEntry entry = slots[ slotIndex ];
		if ( !entry.requirePrerequisiteSlot )
			return true;

		int prereq = entry.prerequisiteSlotIndex;
		if ( prereq < 0 || prereq == slotIndex || prereq >= slots.Count )
			return true;

		return IsSlotOccupied( prereq );
	}

	public int GetPrerequisiteSlotIndex( int slotIndex )
	{
		if ( slots == null || slotIndex < 0 || slotIndex >= slots.Count )
			return -1;

		ArtifactPresentationSlotEntry entry = slots[ slotIndex ];
		if ( !entry.requirePrerequisiteSlot )
			return -1;
		return entry.prerequisiteSlotIndex;
	}

	/// <summary>
	/// Label for the artifact that must be placed before this slot unlocks, or null if unlocked.
	/// </summary>
	public TreasureDefinition GetBlockingPrerequisiteArtifact( int slotIndex )
	{
		if ( IsSlotPrerequisiteMet( slotIndex ) )
			return null;

		int prereq = GetPrerequisiteSlotIndex( slotIndex );
		if ( prereq < 0 )
			return null;
		return GetRequiredArtifact( prereq );
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

	bool EvaluateSlotForItem( int slotIndex, TreasureItem item )
	{
		if ( item == null || IsSlotOccupied( slotIndex ) )
			return false;
		if ( !IsSlotPrerequisiteMet( slotIndex ) )
			return false;
		if ( !item.IsClean )
			return false;
		return AcceptsForSlot( slotIndex, item.Definition );
	}

	bool TryResolvePlacementSlot( TreasureItem item, in PlacementQuery query, out int slotIndex, out bool valid )
	{
		slotIndex = -1;
		valid = false;
		if ( item == null || _occupants == null || slots == null )
			return false;

		if ( !TryResolveAimedSlotIndex( item, in query, out slotIndex ) )
			return false;

		if ( EvaluateSlotForItem( slotIndex, item ) )
		{
			valid = true;
			return true;
		}

		// Aiming at any slot / the stand: snap to the matching empty slot when one exists.
		// Invalid only when this artifact cannot go in any available slot.
		if ( TryFindMatchingValidSlot( item, in query, out int matchingSlot ) )
		{
			slotIndex = matchingSlot;
			valid = true;
			return true;
		}

		valid = false;
		return true;
	}

	bool TryResolveAimedSlotIndex( TreasureItem item, in PlacementQuery query, out int slotIndex )
	{
		slotIndex = -1;
		if ( slots == null || slots.Count == 0 )
			return false;

		// Scan every collider along the aim ray — the single placement hit can be table geometry
		// behind a slot mesh, and NonAlloc order is undefined.
		if ( TryResolveAimedSlotFromRay( in query, out slotIndex ) )
			return true;

		if ( !query.HasHit || query.Hit.collider == null )
			return false;

		TreasureItem hitItem = query.Hit.collider.GetComponentInParent<TreasureItem>();
		if ( hitItem != null && TryFindSlotContaining( hitItem, out slotIndex ) )
			return true;

		if ( ArtifactPresentationSlotVolume.TryResolveSlotIndex( query.Hit.collider, out slotIndex ) )
			return true;

		// Aiming at table base / non-slot geometry: snap preview/place to the matching empty slot.
		if ( !IsHitOnThisTable( query.Hit.collider ) )
			return false;

		if ( item != null && TryFindMatchingValidSlot( item, in query, out slotIndex ) )
			return true;

		return TryFindNearestEmptySlot( query.Hit.point, out slotIndex );
	}

	bool IsHitOnThisTable( Collider hitCollider )
	{
		if ( hitCollider == null )
			return false;

		ArtifactPresentationTableInteractable table =
			hitCollider.GetComponentInParent<ArtifactPresentationTableInteractable>();
		return table == this;
	}

	bool TryFindMatchingValidSlot( TreasureItem item, in PlacementQuery query, out int slotIndex )
	{
		slotIndex = -1;
		if ( item == null || slots == null || _occupants == null )
			return false;

		Vector3 reference = query.HasHit ? query.Hit.point : transform.position;
		float bestDistSq = float.MaxValue;

		for ( int i = 0; i < slots.Count; i++ )
		{
			if ( !EvaluateSlotForItem( i, item ) )
				continue;

			GetSlotWorldPose( i, out Vector3 slotPos, out _ );
			float distSq = ( slotPos - reference ).sqrMagnitude;
			if ( distSq >= bestDistSq )
				continue;

			bestDistSq = distSq;
			slotIndex = i;
		}

		return slotIndex >= 0;
	}

	bool TryFindNearestEmptySlot( Vector3 worldPoint, out int slotIndex )
	{
		slotIndex = -1;
		if ( slots == null || _occupants == null )
			return false;

		float bestDistSq = float.MaxValue;
		for ( int i = 0; i < slots.Count; i++ )
		{
			if ( IsSlotOccupied( i ) )
				continue;

			GetSlotWorldPose( i, out Vector3 slotPos, out _ );
			float distSq = ( slotPos - worldPoint ).sqrMagnitude;
			if ( distSq >= bestDistSq )
				continue;

			bestDistSq = distSq;
			slotIndex = i;
		}

		return slotIndex >= 0;
	}

	bool TryResolveAimedSlotFromRay( in PlacementQuery query, out int slotIndex )
	{
		slotIndex = -1;
		PlayerController player = query.Player;
		if ( player == null )
			return false;

		PlayerInteraction interaction = player.Interaction;
		if ( interaction == null || !interaction.TryGetAimRay( out Ray ray ) )
			return false;

		float range = query.InteractRange > 0.001f ? query.InteractRange : interaction.PlacementAimRange;
		LayerMask mask = interaction.InteractMask;
		int hitCount = Physics.RaycastNonAlloc(
			ray,
			SlotAimRayHits,
			range,
			mask,
			QueryTriggerInteraction.Ignore );

		RaycastHit[] processHits = SlotAimRayHits;
		if ( hitCount >= SlotAimRayBufferSize )
		{
			processHits = Physics.RaycastAll( ray, range, mask, QueryTriggerInteraction.Ignore );
			hitCount = processHits.Length;
		}

		Transform playerRoot = player.transform;
		float bestDist = float.MaxValue;
		int bestSlot = -1;

		for ( int i = 0; i < hitCount; i++ )
		{
			RaycastHit hit = processHits[ i ];
			if ( hit.collider == null )
				continue;
			if ( PlayerInteraction.IsPlayerOwnedHit( hit.collider, playerRoot ) )
				continue;
			if ( !ArtifactPresentationSlotVolume.TryResolveSlotIndex( hit.collider, out int candidate ) )
				continue;

			ArtifactPresentationSlotVolume volume = hit.collider.GetComponentInParent<ArtifactPresentationSlotVolume>();
			if ( volume == null || volume.Table != this )
				continue;
			if ( hit.distance >= bestDist )
				continue;

			bestDist = hit.distance;
			bestSlot = candidate;
		}

		if ( bestSlot < 0 )
			return false;

		slotIndex = bestSlot;
		return true;
	}

	void RebuildSlotAimVolumes( bool createMissing )
	{
		if ( slots == null )
			return;

		for ( int i = 0; i < slots.Count; i++ )
		{
			Transform anchor = slots[ i ].anchor;
			if ( anchor == null )
				continue;

			ArtifactPresentationSlotVolume volume = anchor.GetComponent<ArtifactPresentationSlotVolume>();
			if ( volume == null )
			{
				if ( !createMissing )
					continue;
				volume = anchor.gameObject.AddComponent<ArtifactPresentationSlotVolume>();
			}

			volume.Configure( this, i );
			volume.RebuildAimCollider( GetRequiredArtifact( i ), socketRotation, slots[ i ].rotationOffset );
			volume.SetAimCollidersEnabled( !IsSlotOccupied( i ) );
		}
	}

#if UNITY_EDITOR
	void EnsureSlotVolumes()
	{
		RebuildSlotAimVolumes( createMissing: true );
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

		TreasureDefinition required = GetRequiredArtifact( slotIndex );
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
			TreasureInteractSfx.PlayPlace( item.Definition, endWorldPos );
		}

		if ( showSlotIndicators && slotIndicators != null )
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

	void EnsureCollider()
	{
		_collider = GetComponent<Collider>();
		if ( _collider == null )
			_collider = GetComponentInChildren<Collider>( true );

		if ( _collider == null )
		{
			Debug.LogError(
				"ArtifactPresentationTableInteractable on '" + name + "' requires a Collider on this object or a child.",
				this );
		}
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
		if ( !showSlotIndicators )
		{
			if ( slotIndicators != null )
				slotIndicators.ClearForDisabledHolograms();
			return;
		}

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

		if ( activateOnComplete != null )
		{
			for ( int i = 0; i < activateOnComplete.Count; i++ )
			{
				GameObject go = activateOnComplete[ i ];
				if ( go != null )
					go.SetActive( completed );
			}
		}

		ApplyCompleteEffect( completed );
	}

	void ApplyCompleteEffect( bool completed )
	{
		if ( completeEffect == null )
			return;

		if ( completed && overrideCompleteEffectColor )
			completeEffect.Setup( completeEffectColor );

		GameObject effectGo = completeEffect.gameObject;
		if ( effectGo != null )
			effectGo.SetActive( completed );
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
