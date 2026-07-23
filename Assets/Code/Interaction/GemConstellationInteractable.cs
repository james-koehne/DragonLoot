using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

public enum GemConstellationAcceptanceMode
{
	SetGem,
	AnyGem,
	PerSlot
}

[Serializable]
public struct GemConstellationSlotEntry
{
	[Tooltip( "World anchor for this slot. Move in the scene to shape the constellation." )]
	public Transform anchor;

	[Tooltip( "Optional per-slot gem override. Used in SetGem/AnyGem modes; required in PerSlot mode." )]
	public TreasureDefinition overrideGem;

	[Tooltip( "Extra Euler rotation applied to gems in this socket, after the constellation gem rotation." )]
	public Vector3 gemRotationOffset;
}

[Serializable]
public struct GemConstellationConnectionPair
{
	public int slotA;
	public int slotB;

	public GemConstellationConnectionPair( int a, int b )
	{
		if ( a <= b )
		{
			slotA = a;
			slotB = b;
		}
		else
		{
			slotA = b;
			slotB = a;
		}
	}

	public bool Matches( int a, int b )
	{
		if ( a > b )
		{
			int t = a;
			a = b;
			b = t;
		}

		return slotA == a && slotB == b;
	}
}

public struct GemConstellationResolvedConnection
{
	public int SlotA;
	public int SlotB;
	public bool IsForced;
}

/// <summary>
/// Wall-mounted gem display: hand-placed slot anchors form a constellation shape.
/// Nodes auto-chain to the previous node in the list; extra links and exclusions are edited in the scene.
/// </summary>
[RequireComponent( typeof( Collider ) )]
public class GemConstellationInteractable : InteractableBase, ITreasureOwner, ITreasurePlacementTarget, ITreasureDisplayStackOwner
{
	[Header( "Acceptance" )]
	[SerializeField]
	GemConstellationAcceptanceMode acceptanceMode = GemConstellationAcceptanceMode.SetGem;

	[Tooltip( "Default gem for SetGem mode. Per-slot overrides take precedence when set." )]
	[SerializeField]
	TreasureDefinition defaultAcceptedGem;

	[Header( "Slots" )]
	[SerializeField]
	List<GemConstellationSlotEntry> slots = new List<GemConstellationSlotEntry>();

	[Header( "Connections" )]
	[Tooltip( "Extra links beyond the default chain (each new node auto-links to the previous node)." )]
	[SerializeField]
	List<GemConstellationConnectionPair> forcedConnections = new List<GemConstellationConnectionPair>();

	[SerializeField]
	List<GemConstellationConnectionPair> excludedConnections = new List<GemConstellationConnectionPair>();

	[Header( "Placement" )]
	[SerializeField]
	[Min( 0.05f )]
	float snapDuration = 0.25f;

	[SerializeField]
	[Min( 1f )]
	float bounceScale = 1.15f;

	[Tooltip( "Euler rotation applied to every gem relative to its slot anchor." )]
	[SerializeField]
	Vector3 gemSocketRotation;

	[Header( "Feedback" )]
	[SerializeField]
	Text countLabel;

	[SerializeField]
	GameObject completedHighlight;

	[SerializeField]
	GemConstellationLineVisual lineVisual;

	[Header( "Editor Gizmos" )]
	[SerializeField]
	bool drawGizmosAlways;

	readonly List<TreasureItem> _displayedItems = new List<TreasureItem>();
	readonly List<GemConstellationResolvedConnection> _resolvedConnections = new List<GemConstellationResolvedConnection>();
	TreasureItem[] _occupants;
	int _currentCount;
	bool _isComplete;

	public TreasureOwnerKind OwnerKind => TreasureOwnerKind.DisplayCabinet;
	public GemConstellationAcceptanceMode AcceptanceMode => acceptanceMode;
	public TreasureDefinition DefaultAcceptedGem => defaultAcceptedGem;
	public IReadOnlyList<GemConstellationSlotEntry> Slots => slots;
	public IReadOnlyList<GemConstellationResolvedConnection> ResolvedConnections => _resolvedConnections;
	public IReadOnlyList<GemConstellationConnectionPair> ForcedConnections => forcedConnections;
	public IReadOnlyList<GemConstellationConnectionPair> ExcludedConnections => excludedConnections;
	public int CurrentCount => _currentCount;
	public int SlotCount => slots != null ? slots.Count : 0;
	public int Capacity => SlotCount;
	public bool IsComplete => _isComplete;
	public IReadOnlyList<TreasureItem> DisplayedItems => _displayedItems;

	protected virtual void Reset()
	{
		SetInteractionName( "Gem Constellation" );
	}

	protected virtual void Awake()
	{
		EnsureOccupants();
		EnsureLineVisual();
		ApplyInteractionName();
		RebuildConnections();
		RefreshCountLabel();
		SetCompletedVisual( false );
		RefreshLineVisual();
	}

	protected virtual void OnValidate()
	{
		snapDuration = Mathf.Max( 0.05f, snapDuration );
		bounceScale = Mathf.Max( 1f, bounceScale );
		NormalizeConnectionPairs( forcedConnections );
		NormalizeConnectionPairs( excludedConnections );
		EnsureOccupants();
		RebuildConnections();
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
		bool hasAimRay )
	{
		if ( results == null )
			return false;

		results.Clear();
		if ( selected == null || _occupants == null )
			return false;

		for ( int i = 0; i < _occupants.Length; i++ )
		{
			if ( _occupants[ i ] != selected )
				continue;

			results.Add( selected );
			return true;
		}

		return false;
	}

	public void Remove( TreasureItem item )
	{
		if ( item == null || _occupants == null )
			return;

		for ( int i = 0; i < _occupants.Length; i++ )
		{
			if ( _occupants[ i ] != item )
				continue;

			_occupants[ i ] = null;
			_displayedItems.Remove( item );
			_currentCount = Mathf.Max( 0, _currentCount - 1 );
			_isComplete = false;
			SetCompletedVisual( false );
			RefreshCountLabel();
			PublishChanged();
			NotifySortedDelta( item.Definition, -1 );
			RefreshLineVisual();
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
		if ( item == null || !IsAvailable )
			return false;

		return TryResolveTargetSlot( item, in query, out _ );
	}

	public bool TryGetPlacementPreview( TreasureItem item, in PlacementQuery query, out PlacementPreview preview )
	{
		preview = default;
		if ( item == null )
			return false;

		if ( !TryResolveTargetSlot( item, in query, out int slotIndex ) )
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
		preview.IsValid = CanPlace( item, in query );
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

		if ( !TryResolveTargetSlot( item, in query, out int slotIndex ) )
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
		NotifySortedDelta( removed.Definition, 1 );

		StartCoroutine( SnapIntoSlotRoutine( removed, slotIndex ) );
		return true;
	}

	public bool IsSlotOccupied( int slotIndex )
	{
		if ( _occupants == null || slotIndex < 0 || slotIndex >= _occupants.Length )
			return false;

		return _occupants[ slotIndex ] != null;
	}

	public void GetSlotWorldPose( int slotIndex, out Vector3 worldPos, out Quaternion worldRot )
	{
		worldPos = transform.position;
		worldRot = GetSocketWorldRotation( slotIndex );

		if ( slots == null || slotIndex < 0 || slotIndex >= slots.Count )
			return;

		Transform anchor = slots[ slotIndex ].anchor;
		if ( anchor == null )
			return;

		worldPos = anchor.position;
	}

	Quaternion GetSocketWorldRotation( int slotIndex )
	{
		Quaternion baseRot = transform.rotation;
		Vector3 slotOffset = Vector3.zero;

		if ( slots != null && slotIndex >= 0 && slotIndex < slots.Count )
		{
			Transform anchor = slots[ slotIndex ].anchor;
			if ( anchor != null )
				baseRot = anchor.rotation;

			slotOffset = slots[ slotIndex ].gemRotationOffset;
		}

		return baseRot * Quaternion.Euler( gemSocketRotation ) * Quaternion.Euler( slotOffset );
	}

	Transform GetSlotParent( int slotIndex )
	{
		if ( slots == null || slotIndex < 0 || slotIndex >= slots.Count )
			return transform;

		Transform anchor = slots[ slotIndex ].anchor;
		return anchor != null ? anchor : transform;
	}

	public void RebuildConnections()
	{
		_resolvedConnections.Clear();
		int count = SlotCount;
		if ( count < 2 )
			return;

		// Default chain: each node links to the previous node in the list (last placed).
		for ( int i = 1; i < count; i++ )
		{
			int prev = i - 1;
			if ( IsPairExcluded( prev, i ) )
				continue;

			_resolvedConnections.Add( new GemConstellationResolvedConnection
			{
				SlotA = prev,
				SlotB = i,
				IsForced = IsPairForced( prev, i )
			} );
		}

		// Extra manual links (non-chain or reinforced).
		if ( forcedConnections == null )
			return;

		for ( int i = 0; i < forcedConnections.Count; i++ )
		{
			GemConstellationConnectionPair pair = forcedConnections[ i ];
			if ( pair.slotA < 0 || pair.slotB < 0 || pair.slotA >= count || pair.slotB >= count )
				continue;
			if ( pair.slotA == pair.slotB )
				continue;
			if ( IsPairExcluded( pair.slotA, pair.slotB ) )
				continue;
			if ( HasResolvedConnection( pair.slotA, pair.slotB ) )
				continue;

			_resolvedConnections.Add( new GemConstellationResolvedConnection
			{
				SlotA = pair.slotA,
				SlotB = pair.slotB,
				IsForced = true
			} );
		}
	}

	bool HasResolvedConnection( int slotA, int slotB )
	{
		for ( int i = 0; i < _resolvedConnections.Count; i++ )
		{
			GemConstellationResolvedConnection edge = _resolvedConnections[ i ];
			if ( ( edge.SlotA == slotA && edge.SlotB == slotB )
				|| ( edge.SlotA == slotB && edge.SlotB == slotA ) )
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>True when slots are neighbors in list order (default auto-chain).</summary>
	public bool IsChainNeighbor( int slotA, int slotB )
	{
		return Mathf.Abs( slotA - slotB ) == 1;
	}

	public void ToggleExcludedConnection( int slotA, int slotB )
	{
		GemConstellationConnectionPair pair = new GemConstellationConnectionPair( slotA, slotB );
		int index = FindPairIndex( excludedConnections, pair );
		if ( index >= 0 )
			excludedConnections.RemoveAt( index );
		else
		{
			RemovePair( forcedConnections, pair );
			excludedConnections.Add( pair );
		}

		RebuildConnections();
		RefreshLineVisual();
	}

	public void ToggleForcedConnection( int slotA, int slotB )
	{
		GemConstellationConnectionPair pair = new GemConstellationConnectionPair( slotA, slotB );
		int index = FindPairIndex( forcedConnections, pair );
		if ( index >= 0 )
			forcedConnections.RemoveAt( index );
		else
		{
			RemovePair( excludedConnections, pair );
			forcedConnections.Add( pair );
		}

		RebuildConnections();
		RefreshLineVisual();
	}

	public bool IsPairExcluded( int slotA, int slotB )
	{
		return ContainsPair( excludedConnections, slotA, slotB );
	}

	public bool IsPairForced( int slotA, int slotB )
	{
		return ContainsPair( forcedConnections, slotA, slotB );
	}

	public float GetSlotDistance( int slotA, int slotB )
	{
		GetSlotWorldPose( slotA, out Vector3 a, out _ );
		GetSlotWorldPose( slotB, out Vector3 b, out _ );
		return Vector3.Distance( a, b );
	}

	bool TryResolveTargetSlot( TreasureItem item, in PlacementQuery query, out int slotIndex )
	{
		slotIndex = -1;
		if ( item == null || _occupants == null || slots == null )
			return false;

		float bestDistSq = float.MaxValue;
		Vector3 reference = query.HasHit ? query.Hit.point : transform.position;

		for ( int i = 0; i < slots.Count; i++ )
		{
			if ( IsSlotOccupied( i ) )
				continue;

			if ( !AcceptsForSlot( i, item.Definition ) )
				continue;

			GetSlotWorldPose( i, out Vector3 worldPos, out _ );
			float distSq = ( worldPos - reference ).sqrMagnitude;
			if ( distSq < bestDistSq )
			{
				bestDistSq = distSq;
				slotIndex = i;
			}
		}

		return slotIndex >= 0;
	}

	bool AcceptsForSlot( int slotIndex, TreasureDefinition definition )
	{
		if ( definition == null || definition.category != TreasureCategory.Gem )
			return false;

		if ( slots == null || slotIndex < 0 || slotIndex >= slots.Count )
			return false;

		TreasureDefinition slotOverride = slots[ slotIndex ].overrideGem;

		switch ( acceptanceMode )
		{
			case GemConstellationAcceptanceMode.SetGem:
				if ( slotOverride != null )
					return definition == slotOverride;
				return defaultAcceptedGem != null && definition == defaultAcceptedGem;

			case GemConstellationAcceptanceMode.AnyGem:
				if ( slotOverride != null )
					return definition == slotOverride;
				return true;

			case GemConstellationAcceptanceMode.PerSlot:
				return slotOverride != null && definition == slotOverride;

			default:
				return false;
		}
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
			GetSlotWorldPose( slotIndex, out endWorldPos, out endWorldRot );
			item.EnterDisplayed( this, GetSlotParent( slotIndex ), endWorldPos, endWorldRot );
		}

		PlayPlaceFx();
		RefreshLineVisual();

		if ( !_isComplete && EvaluateComplete() )
		{
			_isComplete = true;
			SetCompletedVisual( true );
			PublishCompleted();
			PlayCompleteFx();
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

	void EnsureLineVisual()
	{
		if ( lineVisual == null )
			lineVisual = GetComponentInChildren<GemConstellationLineVisual>();

		if ( lineVisual != null )
			lineVisual.Bind( this );
	}

	void RefreshLineVisual()
	{
		if ( lineVisual == null )
			return;

		lineVisual.Rebuild( _resolvedConnections );
	}

	void ApplyInteractionName()
	{
		if ( acceptanceMode == GemConstellationAcceptanceMode.SetGem
			&& defaultAcceptedGem != null
			&& !string.IsNullOrEmpty( defaultAcceptedGem.displayName ) )
		{
			SetInteractionName( defaultAcceptedGem.displayName + " Constellation" );
		}
		else if ( string.IsNullOrEmpty( InteractionName ) || InteractionName == "Interactable" )
		{
			SetInteractionName( "Gem Constellation" );
		}
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
		EventBus.Publish( new GemConstellationChangedEvent
		{
			Constellation = this,
			Count = CurrentCount,
			Capacity = Capacity
		} );
	}

	void PublishCompleted()
	{
		EventBus.Publish( new GemConstellationCompletedEvent
		{
			Constellation = this
		} );
	}

	protected virtual void PlayPlaceFx()
	{
	}

	protected virtual void PlayCompleteFx()
	{
	}

	static void NormalizeConnectionPairs( List<GemConstellationConnectionPair> pairs )
	{
		if ( pairs == null )
			return;

		for ( int i = 0; i < pairs.Count; i++ )
			pairs[ i ] = new GemConstellationConnectionPair( pairs[ i ].slotA, pairs[ i ].slotB );
	}

	static bool ContainsPair( List<GemConstellationConnectionPair> pairs, int a, int b )
	{
		if ( pairs == null )
			return false;

		for ( int i = 0; i < pairs.Count; i++ )
		{
			if ( pairs[ i ].Matches( a, b ) )
				return true;
		}

		return false;
	}

	static int FindPairIndex( List<GemConstellationConnectionPair> pairs, GemConstellationConnectionPair pair )
	{
		if ( pairs == null )
			return -1;

		for ( int i = 0; i < pairs.Count; i++ )
		{
			if ( pairs[ i ].Matches( pair.slotA, pair.slotB ) )
				return i;
		}

		return -1;
	}

	static void RemovePair( List<GemConstellationConnectionPair> pairs, GemConstellationConnectionPair pair )
	{
		int index = FindPairIndex( pairs, pair );
		if ( index >= 0 )
			pairs.RemoveAt( index );
	}

	static void NotifySortedDelta( TreasureDefinition definition, int delta )
	{
		TreasureCounterManager manager = TreasureCounterManager.Instance;
		if ( manager != null && definition != null )
			manager.NotifySortedDelta( definition, delta );
	}

#if UNITY_EDITOR
	void OnDrawGizmosSelected()
	{
		DrawConstellationGizmos( selectedOnly: true );
	}

	void OnDrawGizmos()
	{
		if ( drawGizmosAlways )
			DrawConstellationGizmos( selectedOnly: false );
	}

	void DrawConstellationGizmos( bool selectedOnly )
	{
		if ( slots == null || slots.Count == 0 )
			return;

		for ( int i = 0; i < slots.Count; i++ )
		{
			GetSlotWorldPose( i, out Vector3 pos, out _ );
			Gizmos.color = IsSlotOccupied( i )
				? new Color( 0.2f, 1f, 0.55f, selectedOnly ? 0.95f : 0.55f )
				: new Color( 0.55f, 0.75f, 1f, selectedOnly ? 0.85f : 0.45f );
			Gizmos.DrawSphere( pos, 0.035f );
		}

		if ( _resolvedConnections.Count == 0 )
			RebuildConnections();

		for ( int i = 0; i < _resolvedConnections.Count; i++ )
		{
			GemConstellationResolvedConnection edge = _resolvedConnections[ i ];
			GetSlotWorldPose( edge.SlotA, out Vector3 a, out _ );
			GetSlotWorldPose( edge.SlotB, out Vector3 b, out _ );

			if ( edge.IsForced )
				Gizmos.color = new Color( 1f, 0.55f, 0.15f, selectedOnly ? 0.95f : 0.6f );
			else
				Gizmos.color = new Color( 0.35f, 0.85f, 1f, selectedOnly ? 0.85f : 0.5f );

			Gizmos.DrawLine( a, b );
		}

		DrawExcludedGizmos( selectedOnly );
	}

	void DrawExcludedGizmos( bool selectedOnly )
	{
		if ( excludedConnections == null || excludedConnections.Count == 0 || slots == null )
			return;

		Gizmos.color = new Color( 1f, 0.25f, 0.25f, selectedOnly ? 0.55f : 0.35f );
		for ( int i = 0; i < excludedConnections.Count; i++ )
		{
			GemConstellationConnectionPair pair = excludedConnections[ i ];
			if ( pair.slotA < 0 || pair.slotB < 0 || pair.slotA >= slots.Count || pair.slotB >= slots.Count )
				continue;

			GetSlotWorldPose( pair.slotA, out Vector3 a, out _ );
			GetSlotWorldPose( pair.slotB, out Vector3 b, out _ );
			Vector3 mid = ( a + b ) * 0.5f;
			Gizmos.DrawLine( a, b );
			float size = 0.025f;
			Gizmos.DrawLine( mid + Vector3.up * size, mid - Vector3.up * size );
			Gizmos.DrawLine( mid + Vector3.right * size, mid - Vector3.right * size );
		}
	}
#endif
}
