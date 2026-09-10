using System;
using System.Collections;
using System.Collections.Generic;

using FeedbackSystem;

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

	[Header( "Start Fill" )]
	[Tooltip( "When enabled, some slots are pre-filled with gems on play." )]
	[SerializeField]
	bool fillSlotsOnStart;

	[Tooltip( "Chance used to pick how many already-connected slots to pre-fill." )]
	[SerializeField]
	[Range( 0f, 1f )]
	float slotFillChance = 0.4f;

	[Tooltip( "Guarantee at least this many filled slots when enough eligible nodes exist." )]
	[SerializeField]
	[Min( 0 )]
	int minFilledSlots;

	[Tooltip( "0 = no cap. Otherwise filled slots are clamped to this count." )]
	[SerializeField]
	[Min( 0 )]
	int maxFilledSlots;

	[Tooltip( "Gems to pick from for random fill. Empty uses each slot's accepted gem (override or default)." )]
	[SerializeField]
	List<TreasureDefinition> randomGemPool = new List<TreasureDefinition>();

	[Tooltip( "0 = non-deterministic. Non-zero seeds which slots fill and which gems are picked." )]
	[SerializeField]
	int fillSeed;

	[Header( "Placement" )]
	[SerializeField]
	[Min( 0.05f )]
	float snapDuration = 0.25f;

	[SerializeField]
	[Min( 0.05f )]
	float approachDuration = 0.18f;

	[SerializeField]
	[Min( 0f )]
	float approachHold = 0.06f;

	[SerializeField]
	[Min( 0.05f )]
	float placeSpinDuration = 0.28f;

	[SerializeField]
	[Min( 0.05f )]
	float approachDistance = 0.35f;

	[SerializeField]
	[Min( 0f )]
	float placeSpins = 1.1f;

	[SerializeField]
	[Min( 1f )]
	float bounceScale = 1.15f;

	[Tooltip( "Euler rotation applied to every gem relative to its slot anchor." )]
	[SerializeField]
	Vector3 gemSocketRotation;

	[Header( "Connected Glow" )]
	[SerializeField]
	[Min( 0.1f )]
	float connectedGlowLerpSpeed = 4f;

	[Header( "Complete Ignition" )]
	[SerializeField]
	[Min( 0f )]
	float cascadeStagger = 0.1f;

	[SerializeField]
	[Min( 0.05f )]
	float ignitionBoostDuration = 1.2f;

	[SerializeField]
	[Min( 1f )]
	float ignitionPeakGlow = 1.35f;

	[Tooltip( "Idle glow after the flare settles. 1 = same as a connected gem." )]
	[SerializeField]
	[Min( 1f )]
	float completedGlowBaseline = 1f;

	[Tooltip( "Added on top of the idle baseline as a 0–1 breathe wave. Larger = more obvious pulse." )]
	[SerializeField]
	[Min( 0f )]
	float breatheAmplitude = 0.22f;

	[SerializeField]
	[Min( 0.05f )]
	float breatheSpeed = 0.7f;

	[SerializeField]
	[Min( 0.05f )]
	float completeSurgeDuration = 1.8f;

	[Header( "Feedback" )]
	[SerializeField]
	Text countLabel;

	[SerializeField]
	GameObject completedHighlight;

	[SerializeField]
	GemConstellationLineVisual lineVisual;

	[SerializeField]
	Feedbacks placeFeedbacks;

	[SerializeField]
	Feedbacks connectionFeedbacks;

	[SerializeField]
	Feedbacks completeFeedbacks;

	[Header( "Editor Gizmos" )]
	[SerializeField]
	bool drawGizmosAlways;

	readonly List<TreasureItem> _displayedItems = new List<TreasureItem>();
	readonly List<GemConstellationResolvedConnection> _resolvedConnections = new List<GemConstellationResolvedConnection>();
	readonly List<bool> _edgeLit = new List<bool>();
	TreasureItem[] _occupants;
	float[] _glowCurrent;
	float[] _glowBoost;
	int _currentCount;
	bool _isComplete;
	bool _completeBreatheActive;
	Coroutine _completeIgnitionRoutine;

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
		SyncEdgeLitState( playConnectionFeedback: false );
	}

	protected virtual void Start()
	{
		TryFillSlotsOnStart();
	}

	protected virtual void Update()
	{
		TickConnectedGlow();
	}

	protected virtual void OnValidate()
	{
		snapDuration = Mathf.Max( 0.05f, snapDuration );
		approachDuration = Mathf.Max( 0.05f, approachDuration );
		approachHold = Mathf.Max( 0f, approachHold );
		placeSpinDuration = Mathf.Max( 0.05f, placeSpinDuration );
		approachDistance = Mathf.Max( 0.05f, approachDistance );
		placeSpins = Mathf.Max( 0f, placeSpins );
		bounceScale = Mathf.Max( 1f, bounceScale );
		connectedGlowLerpSpeed = Mathf.Max( 0.1f, connectedGlowLerpSpeed );
		cascadeStagger = Mathf.Max( 0f, cascadeStagger );
		ignitionBoostDuration = Mathf.Max( 0.05f, ignitionBoostDuration );
		ignitionPeakGlow = Mathf.Max( 1f, ignitionPeakGlow );
		completedGlowBaseline = Mathf.Max( 1f, completedGlowBaseline );
		breatheAmplitude = Mathf.Max( 0f, breatheAmplitude );
		breatheSpeed = Mathf.Max( 0.05f, breatheSpeed );
		completeSurgeDuration = Mathf.Max( 0.05f, completeSurgeDuration );
		slotFillChance = Mathf.Clamp01( slotFillChance );
		minFilledSlots = Mathf.Max( 0, minFilledSlots );
		maxFilledSlots = Mathf.Max( 0, maxFilledSlots );
		if ( maxFilledSlots > 0 && maxFilledSlots < minFilledSlots )
			maxFilledSlots = minFilledSlots;
		NormalizeConnectionPairs( forcedConnections );
		NormalizeConnectionPairs( excludedConnections );
		EnsureOccupants();
		RebuildConnections();
	}

	protected virtual void OnDestroy()
	{
		StopAllCoroutines();
	}

	void TryFillSlotsOnStart()
	{
		if ( !fillSlotsOnStart )
			return;

		EnsureOccupants();
		int count = SlotCount;
		if ( count <= 0 || slots == null )
			return;

		System.Random rng = fillSeed != 0 ? new System.Random( fillSeed ) : null;
		List<int> eligible = new List<int>( count );
		for ( int i = 0; i < count; i++ )
		{
			if ( IsSlotOccupied( i ) )
				continue;
			if ( slots[ i ].anchor == null )
				continue;
			if ( !HasStartFillCandidate( i ) )
				continue;

			eligible.Add( i );
		}

		if ( eligible.Count == 0 )
		{
			Debug.LogWarning(
				"Gem constellation '" + name + "' start fill is enabled but no eligible slots/gems were found. Assign Random Gem Pool or accepted gems.",
				this );
			return;
		}

		int targetCount = ResolveStartFillCount( eligible.Count, rng );
		List<int> chosen = new List<int>( targetCount );
		TryPickConnectedStartFillSlots( eligible, targetCount, rng, chosen );

		int added = 0;
		for ( int i = 0; i < chosen.Count; i++ )
		{
			int slotIndex = chosen[ i ];
			TreasureDefinition gem = PickStartFillGem( slotIndex, rng );
			if ( gem == null )
				continue;
			if ( TryPlaceStartFillGem( slotIndex, gem ) )
				added++;
		}

		if ( added <= 0 )
			return;

		RefreshCountLabel();
		PublishChanged();
		RefreshLineVisual();
		SyncEdgeLitState( playConnectionFeedback: false );
		if ( !_isComplete && EvaluateComplete() )
		{
			_isComplete = true;
			SetCompletedVisual( true );
			PublishCompleted( fromPlayer: false );
			// Start-fill: breathe only — skip noisy ignition cascade on load.
			BeginCompleteBreathe();
		}
	}

	bool HasStartFillCandidate( int slotIndex )
	{
		if ( randomGemPool != null )
		{
			for ( int i = 0; i < randomGemPool.Count; i++ )
			{
				if ( AcceptsForSlot( slotIndex, randomGemPool[ i ] ) )
					return true;
			}
		}

		if ( slots == null || slotIndex < 0 || slotIndex >= slots.Count )
			return false;

		TreasureDefinition slotOverride = slots[ slotIndex ].overrideGem;
		if ( slotOverride != null && AcceptsForSlot( slotIndex, slotOverride ) )
			return true;

		return defaultAcceptedGem != null && AcceptsForSlot( slotIndex, defaultAcceptedGem );
	}

	TreasureDefinition PickStartFillGem( int slotIndex, System.Random rng )
	{
		List<TreasureDefinition> candidates = new List<TreasureDefinition>( 8 );
		if ( randomGemPool != null )
		{
			for ( int i = 0; i < randomGemPool.Count; i++ )
			{
				TreasureDefinition gem = randomGemPool[ i ];
				if ( gem == null || gem.category != TreasureCategory.Gem )
					continue;
				if ( !AcceptsForSlot( slotIndex, gem ) )
					continue;
				if ( !candidates.Contains( gem ) )
					candidates.Add( gem );
			}
		}

		if ( candidates.Count > 0 )
			return candidates[ NextRange( rng, 0, candidates.Count ) ];

		if ( slots != null && slotIndex >= 0 && slotIndex < slots.Count )
		{
			TreasureDefinition slotOverride = slots[ slotIndex ].overrideGem;
			if ( slotOverride != null && AcceptsForSlot( slotIndex, slotOverride ) )
				return slotOverride;
		}

		if ( defaultAcceptedGem != null && AcceptsForSlot( slotIndex, defaultAcceptedGem ) )
			return defaultAcceptedGem;

		return null;
	}

	bool TryPlaceStartFillGem( int slotIndex, TreasureDefinition definition )
	{
		if ( definition == null || _occupants == null )
			return false;
		if ( slotIndex < 0 || slotIndex >= _occupants.Length )
			return false;
		if ( IsSlotOccupied( slotIndex ) )
			return false;
		if ( !AcceptsForSlot( slotIndex, definition ) )
			return false;

		GetSlotWorldPose( slotIndex, out Vector3 pos, out Quaternion rot );
		TreasureItem item = TreasureItemFactory.SpawnSync( definition, pos, rot );
		if ( item == null )
			return false;

		_occupants[ slotIndex ] = item;
		if ( !_displayedItems.Contains( item ) )
			_displayedItems.Add( item );
		_currentCount++;
		item.EnterDisplayed( this, GetSlotParent( slotIndex ), pos, rot );
		NotifySortedDelta( definition, 1 );
		return true;
	}

	int ResolveStartFillCount( int eligibleCount, System.Random rng )
	{
		int minFill = Mathf.Max( 0, minFilledSlots );
		int maxFill = maxFilledSlots > 0 ? Mathf.Min( maxFilledSlots, eligibleCount ) : eligibleCount;
		minFill = Mathf.Min( minFill, maxFill );

		int fromChance = 0;
		float chance = Mathf.Clamp01( slotFillChance );
		for ( int i = 0; i < eligibleCount; i++ )
		{
			if ( NextFloat( rng ) <= chance )
				fromChance++;
		}

		return Mathf.Clamp( fromChance, minFill, maxFill );
	}

	void TryPickConnectedStartFillSlots( List<int> eligible, int targetCount, System.Random rng, List<int> into )
	{
		if ( into == null )
			return;

		into.Clear();
		if ( eligible == null || eligible.Count == 0 || targetCount <= 0 )
			return;

		RebuildConnections();

		int slotCount = SlotCount;
		bool[] eligibleMask = new bool[ slotCount ];
		for ( int i = 0; i < eligible.Count; i++ )
		{
			int slotIndex = eligible[ i ];
			if ( slotIndex >= 0 && slotIndex < slotCount )
				eligibleMask[ slotIndex ] = true;
		}

		List<int>[] adj = new List<int>[ slotCount ];
		for ( int i = 0; i < slotCount; i++ )
			adj[ i ] = new List<int>( 4 );

		for ( int i = 0; i < _resolvedConnections.Count; i++ )
		{
			GemConstellationResolvedConnection edge = _resolvedConnections[ i ];
			if ( edge.SlotA < 0 || edge.SlotB < 0 || edge.SlotA >= slotCount || edge.SlotB >= slotCount )
				continue;
			if ( !eligibleMask[ edge.SlotA ] || !eligibleMask[ edge.SlotB ] )
				continue;

			adj[ edge.SlotA ].Add( edge.SlotB );
			adj[ edge.SlotB ].Add( edge.SlotA );
		}

		List<int> starts = new List<int>( eligible.Count );
		for ( int i = 0; i < eligible.Count; i++ )
			starts.Add( eligible[ i ] );
		Shuffle( starts, rng );

		List<int> component = new List<int>( eligible.Count );
		int bestStart = starts[ 0 ];
		int bestSize = 0;
		for ( int i = 0; i < starts.Count; i++ )
		{
			int start = starts[ i ];
			CollectConnectedSlots( start, adj, component );
			if ( component.Count > bestSize )
			{
				bestStart = start;
				bestSize = component.Count;
			}

			if ( component.Count >= targetCount )
			{
				bestStart = start;
				bestSize = component.Count;
				break;
			}
		}

		GrowConnectedSlots( bestStart, adj, Mathf.Min( targetCount, bestSize ), rng, into );
	}

	static void CollectConnectedSlots( int start, List<int>[] adj, List<int> into )
	{
		into.Clear();
		if ( adj == null || start < 0 || start >= adj.Length )
			return;

		into.Add( start );
		int cursor = 0;
		while ( cursor < into.Count )
		{
			int node = into[ cursor ];
			cursor++;
			List<int> neighbors = adj[ node ];
			if ( neighbors == null )
				continue;

			for ( int i = 0; i < neighbors.Count; i++ )
			{
				int next = neighbors[ i ];
				if ( !into.Contains( next ) )
					into.Add( next );
			}
		}
	}

	static void GrowConnectedSlots( int start, List<int>[] adj, int targetCount, System.Random rng, List<int> into )
	{
		into.Clear();
		if ( adj == null || start < 0 || start >= adj.Length || targetCount <= 0 )
			return;

		into.Add( start );
		List<int> frontier = new List<int>( 8 );
		AppendUnchosenNeighbors( start, adj, into, frontier );

		while ( into.Count < targetCount && frontier.Count > 0 )
		{
			int pick = NextRange( rng, 0, frontier.Count );
			int node = frontier[ pick ];
			frontier.RemoveAt( pick );
			if ( into.Contains( node ) )
				continue;

			into.Add( node );
			AppendUnchosenNeighbors( node, adj, into, frontier );
		}
	}

	static void AppendUnchosenNeighbors( int node, List<int>[] adj, List<int> chosen, List<int> frontier )
	{
		if ( adj == null || node < 0 || node >= adj.Length || chosen == null || frontier == null )
			return;

		List<int> neighbors = adj[ node ];
		if ( neighbors == null )
			return;

		for ( int i = 0; i < neighbors.Count; i++ )
		{
			int next = neighbors[ i ];
			if ( chosen.Contains( next ) || frontier.Contains( next ) )
				continue;

			frontier.Add( next );
		}
	}

	static int NextRange( System.Random rng, int minInclusive, int maxExclusive )
	{
		if ( maxExclusive <= minInclusive )
			return minInclusive;
		if ( rng != null )
			return rng.Next( minInclusive, maxExclusive );
		return UnityEngine.Random.Range( minInclusive, maxExclusive );
	}

	static float NextFloat( System.Random rng )
	{
		if ( rng != null )
			return ( float )rng.NextDouble();
		return UnityEngine.Random.value;
	}

	static void Shuffle( List<int> list, System.Random rng )
	{
		if ( list == null )
			return;

		for ( int i = list.Count - 1; i > 0; i-- )
		{
			int j = NextRange( rng, 0, i + 1 );
			int tmp = list[ i ];
			list[ i ] = list[ j ];
			list[ j ] = tmp;
		}
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

	public bool TryGetSlotIndex( TreasureItem selected, out int slotIndex )
	{
		slotIndex = -1;
		if ( selected == null || _occupants == null )
			return false;

		for ( int i = 0; i < _occupants.Length; i++ )
		{
			if ( _occupants[ i ] != selected )
				continue;

			slotIndex = i;
			return true;
		}

		return false;
	}

	public int GetSlotCount( int slotIndex )
	{
		if ( _occupants == null || slotIndex < 0 || slotIndex >= _occupants.Length )
			return 0;

		return _occupants[ slotIndex ] != null ? 1 : 0;
	}

	public void AppendSlotOutlineRenderers( TreasureItem selected, List<Renderer> renderers )
	{
		if ( selected == null || renderers == null )
			return;

		HoverOutlineTargetUtility.AppendEnabledMeshRenderers( selected.gameObject, renderers );
	}

	public bool TryConsumeSlotDefinitions(
		int slotIndex,
		List<TreasureDefinition> into,
		out Vector3 contact,
		out Quaternion rotation )
	{
		contact = transform.position;
		rotation = transform.rotation;
		return false;
	}

	public int GetSlotCoinAppendCapacity( int slotIndex, TreasureDefinition probe )
	{
		return 0;
	}

	public bool TryGetSlotAppendPose( int slotIndex, out Vector3 contact, out Quaternion rotation )
	{
		contact = transform.position;
		rotation = transform.rotation;
		return false;
	}

	public int TryAppendSlotDefinitions( int slotIndex, IReadOnlyList<TreasureDefinition> definitions )
	{
		return 0;
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
			if ( _glowCurrent != null && i < _glowCurrent.Length )
				_glowCurrent[ i ] = 0f;
			if ( _glowBoost != null && i < _glowBoost.Length )
				_glowBoost[ i ] = 0f;
			item.SetConnectedGlow( 0f );
			_displayedItems.Remove( item );
			_currentCount = Mathf.Max( 0, _currentCount - 1 );
			StopCompleteIgnition();
			_isComplete = false;
			_completeBreatheActive = false;
			if ( lineVisual != null )
				lineVisual.StopCompleteEffects();
			SetCompletedVisual( false );
			RefreshCountLabel();
			PublishChanged();
			NotifySortedDelta( item.Definition, -1 );
			RefreshLineVisual();
			SyncEdgeLitState( playConnectionFeedback: false );
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

	/// <summary>
	/// Required gem for a slot. Null means any gem (AnyGem with no override).
	/// </summary>
	public TreasureDefinition GetRequiredGem( int slotIndex )
	{
		if ( slots == null || slotIndex < 0 || slotIndex >= slots.Count )
			return null;

		TreasureDefinition slotOverride = slots[ slotIndex ].overrideGem;
		if ( slotOverride != null )
			return slotOverride;

		switch ( acceptanceMode )
		{
			case GemConstellationAcceptanceMode.SetGem:
				return defaultAcceptedGem;
			case GemConstellationAcceptanceMode.AnyGem:
				return null;
			case GemConstellationAcceptanceMode.PerSlot:
				return null;
			default:
				return null;
		}
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
		AppendConnectionPairs( forcedConnections, count, markForced: true );
	}

	void AppendConnectionPairs( List<GemConstellationConnectionPair> pairs, int slotCount, bool markForced )
	{
		if ( pairs == null )
			return;

		for ( int i = 0; i < pairs.Count; i++ )
		{
			GemConstellationConnectionPair pair = pairs[ i ];
			if ( pair.slotA < 0 || pair.slotB < 0 || pair.slotA >= slotCount || pair.slotB >= slotCount )
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
				IsForced = markForced
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

		Vector3 approachPos = endWorldPos - transform.forward * approachDistance;
		Quaternion approachRot = ResolveApproachRotation( startRot, endWorldRot );

		float flyDuration = Mathf.Max( approachDuration, 0.05f );
		float flyElapsed = 0f;
		float flyArc = CoinFlipMotion.DefaultItemArcHeight;

		while ( flyElapsed < flyDuration )
		{
			if ( item == null )
				yield break;

			flyElapsed += Time.deltaTime;
			float u = Mathf.Clamp01( flyElapsed / flyDuration );
			float ease = CoinFlipMotion.SmoothStep( u );

			t.position = CoinFlipMotion.EvaluateArcPosition( startPos, approachPos, u, flyArc );
			t.rotation = Quaternion.Slerp( startRot, approachRot, ease );
			item.ApplyDesiredWorldScale( Vector3.Lerp( startScale, endScale, ease ) );

			yield return null;
		}

		if ( item == null )
			yield break;

		t.position = approachPos;
		t.rotation = approachRot;
		item.ApplyDesiredWorldScale( endScale );

		if ( approachHold > 0.0001f )
		{
			float holdElapsed = 0f;
			while ( holdElapsed < approachHold )
			{
				if ( item == null )
					yield break;

				holdElapsed += Time.deltaTime;
				t.position = approachPos;
				t.rotation = approachRot;
				yield return null;
			}
		}

		if ( item == null )
			yield break;

		GetSlotWorldPose( slotIndex, out endWorldPos, out endWorldRot );
		Quaternion spinStartRot = t.rotation;
		Vector3 spinStartScale = endScale;
		float spinDuration = Mathf.Max( placeSpinDuration, snapDuration * 0.5f );
		float spinElapsed = 0f;
		float spinArc = CoinFlipMotion.DefaultItemArcHeight * 0.5f;
		float spins = placeSpins;
		Vector3 screwAxis = transform.forward;
		if ( screwAxis.sqrMagnitude < 0.0001f )
			screwAxis = Vector3.forward;
		else
			screwAxis.Normalize();

		while ( spinElapsed < spinDuration )
		{
			if ( item == null )
				yield break;

			spinElapsed += Time.deltaTime;
			float u = Mathf.Clamp01( spinElapsed / spinDuration );
			float ease = CoinFlipMotion.SmoothStep( u );
			float bounce = 1f + ( bounceScale - 1f ) * Mathf.Sin( u * Mathf.PI );

			t.position = CoinFlipMotion.EvaluateArcPosition( approachPos, endWorldPos, u, spinArc );
			t.rotation = EvaluateScrewRotation( spinStartRot, endWorldRot, screwAxis, u, spins );
			item.ApplyDesiredWorldScale( Vector3.Lerp( spinStartScale, endScale, ease ) * bounce );

			yield return null;
		}

		if ( item == null )
			yield break;

		item.EndFlight();

		if ( _occupants[ slotIndex ] == item )
		{
			GetSlotWorldPose( slotIndex, out endWorldPos, out endWorldRot );
			item.EnterDisplayed( this, GetSlotParent( slotIndex ), endWorldPos, endWorldRot );
			TreasureInteractSfx.PlayPlace( item.Definition, endWorldPos );
			CoinGemInteractFeedback.PlayPlace( item );
			PlayPlaceFx( endWorldPos );
		}

		RefreshLineVisual();
		SyncEdgeLitState( playConnectionFeedback: true );

		if ( !_isComplete && EvaluateComplete() )
		{
			_isComplete = true;
			SetCompletedVisual( true );
			PublishCompleted( fromPlayer: true );
			StartCompleteIgnition();
		}
	}

	Quaternion ResolveApproachRotation( Quaternion startRot, Quaternion endWorldRot )
	{
		// Approach sits on local -Z; face along +Z (into the slot) so the screw-in reads cleanly.
		Vector3 forward = transform.forward;
		if ( forward.sqrMagnitude < 0.0001f )
			return Quaternion.Slerp( startRot, endWorldRot, 0.5f );

		Vector3 up = Vector3.up;
		if ( Mathf.Abs( Vector3.Dot( forward.normalized, up ) ) > 0.95f )
			up = transform.up;

		Quaternion faceIntoSlot = Quaternion.LookRotation( forward.normalized, up );
		return Quaternion.Slerp( faceIntoSlot, endWorldRot, 0.35f );
	}

	static Quaternion EvaluateScrewRotation( Quaternion start, Quaternion end, Vector3 axis, float u, float spins )
	{
		float ease = CoinFlipMotion.SmoothStep( u );
		Quaternion baseRot = Quaternion.Slerp( start, end, ease );
		float angle = spins * 360f * Mathf.Clamp01( u );
		return Quaternion.AngleAxis( angle, axis ) * baseRot;
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

		EnsureGlowArray( count );
	}

	void EnsureGlowArray( int count )
	{
		if ( _glowCurrent == null || _glowCurrent.Length != count )
		{
			float[] next = new float[ count ];
			if ( _glowCurrent != null )
			{
				int copy = Mathf.Min( _glowCurrent.Length, count );
				for ( int i = 0; i < copy; i++ )
					next[ i ] = _glowCurrent[ i ];
			}

			_glowCurrent = next;
		}

		if ( _glowBoost == null || _glowBoost.Length != count )
		{
			float[] nextBoost = new float[ count ];
			if ( _glowBoost != null )
			{
				int copy = Mathf.Min( _glowBoost.Length, count );
				for ( int i = 0; i < copy; i++ )
					nextBoost[ i ] = _glowBoost[ i ];
			}

			_glowBoost = nextBoost;
		}
	}

	void TickConnectedGlow()
	{
		if ( _occupants == null || _occupants.Length == 0 )
			return;

		EnsureGlowArray( _occupants.Length );
		float step = Time.deltaTime * connectedGlowLerpSpeed;
		float boostDecay = ignitionBoostDuration > 0.0001f
			? Time.deltaTime / ignitionBoostDuration
			: 1f;
		float breathe = 0f;
		if ( _completeBreatheActive )
		{
			float wave = 0.5f + 0.5f * Mathf.Sin( Time.time * breatheSpeed );
			breathe = breatheAmplitude * wave;
		}

		for ( int i = 0; i < _occupants.Length; i++ )
		{
			TreasureItem item = _occupants[ i ];
			bool connected = item != null && IsSlotConnectedLit( i );
			float connectedAmount = connected ? 1f : 0f;
			float baseline = ( _isComplete && connected ) ? completedGlowBaseline : 1f;

			if ( _glowBoost[ i ] > 0f )
				_glowBoost[ i ] = Mathf.Max( 0f, _glowBoost[ i ] - ( ignitionPeakGlow - completedGlowBaseline ) * boostDecay );

			float boost = connected ? _glowBoost[ i ] : 0f;
			float breatheTerm = ( _completeBreatheActive && connected ) ? breathe : 0f;
			float target = Mathf.Clamp( connectedAmount * baseline + boost + breatheTerm, 0f, 2f );

			float current = _glowCurrent[ i ];
			float next = Mathf.MoveTowards( current, target, step );
			if ( Mathf.Abs( next - current ) < 0.0001f && ( item == null || Mathf.Abs( item.ConnectedGlow - next ) < 0.0001f ) )
				continue;

			_glowCurrent[ i ] = next;
			if ( item != null )
				item.SetConnectedGlow( next );
		}
	}

	void StartCompleteIgnition()
	{
		StopCompleteIgnition();
		_completeIgnitionRoutine = StartCoroutine( PlayCompleteIgnitionRoutine() );
	}

	void StopCompleteIgnition()
	{
		if ( _completeIgnitionRoutine != null )
		{
			StopCoroutine( _completeIgnitionRoutine );
			_completeIgnitionRoutine = null;
		}

		if ( _glowBoost != null )
		{
			for ( int i = 0; i < _glowBoost.Length; i++ )
				_glowBoost[ i ] = 0f;
		}
	}

	void BeginCompleteBreathe()
	{
		if ( _glowBoost != null )
		{
			for ( int i = 0; i < _glowBoost.Length; i++ )
				_glowBoost[ i ] = 0f;
		}

		_completeBreatheActive = true;
		if ( lineVisual != null )
			lineVisual.SetCompleteBreathe( true );
	}

	IEnumerator PlayCompleteIgnitionRoutine()
	{
		EnsureGlowArray( SlotCount );
		PlayCompleteFx();

		if ( lineVisual != null )
			lineVisual.PlayCompleteSurge( completeSurgeDuration );

		float peakBoost = Mathf.Max( 0f, ignitionPeakGlow - completedGlowBaseline );
		int count = SlotCount;
		float cascadeElapsed = 0f;
		for ( int i = 0; i < count; i++ )
		{
			if ( _occupants != null && i < _occupants.Length && _occupants[ i ] != null )
			{
				_glowBoost[ i ] = peakBoost;
				float peak = Mathf.Clamp( completedGlowBaseline + peakBoost, 0f, 2f );
				_glowCurrent[ i ] = peak;
				_occupants[ i ].SetConnectedGlow( peak );
			}

			if ( cascadeStagger > 0.0001f && i < count - 1 )
			{
				yield return new WaitForSeconds( cascadeStagger );
				cascadeElapsed += cascadeStagger;
			}
		}

		// Hold the flare until the line surge has fully eased back to base, then breathe.
		float remainingSurge = Mathf.Max( 0f, completeSurgeDuration - cascadeElapsed );
		float settleWait = Mathf.Max( ignitionBoostDuration, remainingSurge );
		if ( settleWait > 0.0001f )
			yield return new WaitForSeconds( settleWait );

		BeginCompleteBreathe();
		_completeIgnitionRoutine = null;
	}

	bool IsSlotConnectedLit( int slotIndex )
	{
		if ( _occupants == null || slotIndex < 0 || slotIndex >= _occupants.Length )
			return false;
		if ( _occupants[ slotIndex ] == null )
			return false;

		for ( int i = 0; i < _resolvedConnections.Count; i++ )
		{
			GemConstellationResolvedConnection edge = _resolvedConnections[ i ];
			if ( edge.SlotA != slotIndex && edge.SlotB != slotIndex )
				continue;
			if ( IsSlotOccupied( edge.SlotA ) && IsSlotOccupied( edge.SlotB ) )
				return true;
		}

		return false;
	}

	void SyncEdgeLitState( bool playConnectionFeedback )
	{
		EnsureEdgeLitCount( _resolvedConnections.Count );

		for ( int i = 0; i < _resolvedConnections.Count; i++ )
		{
			GemConstellationResolvedConnection edge = _resolvedConnections[ i ];
			bool lit = IsSlotOccupied( edge.SlotA ) && IsSlotOccupied( edge.SlotB );
			bool wasLit = i < _edgeLit.Count && _edgeLit[ i ];

			if ( playConnectionFeedback && lit && !wasLit )
			{
				GetSlotWorldPose( edge.SlotA, out Vector3 a, out _ );
				GetSlotWorldPose( edge.SlotB, out Vector3 b, out _ );
				PlayConnectionFx( ( a + b ) * 0.5f );
			}

			if ( i < _edgeLit.Count )
				_edgeLit[ i ] = lit;
		}
	}

	void EnsureEdgeLitCount( int count )
	{
		while ( _edgeLit.Count < count )
			_edgeLit.Add( false );

		while ( _edgeLit.Count > count )
			_edgeLit.RemoveAt( _edgeLit.Count - 1 );
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

	void PublishCompleted( bool fromPlayer )
	{
		EventBus.Publish( new GemConstellationCompletedEvent
		{
			Constellation = this,
			FromPlayer = fromPlayer
		} );
	}

	protected virtual void PlayPlaceFx()
	{
		PlayPlaceFx( transform.position );
	}

	protected virtual void PlayPlaceFx( Vector3 worldPos )
	{
		PlayFeedback( placeFeedbacks, worldPos );
	}

	protected virtual void PlayConnectionFx( Vector3 worldPos )
	{
		PlayFeedback( connectionFeedbacks, worldPos );
	}

	protected virtual void PlayCompleteFx()
	{
		PlayFeedback( completeFeedbacks, transform.position );
	}

	void PlayFeedback( Feedbacks feedbacks, Vector3 worldPos )
	{
		if ( feedbacks == null )
			return;

		FeedbackContext context = new FeedbackContext();
		context.Source = gameObject;
		context.Target = gameObject;
		context.Position = worldPos;
		feedbacks.Play( context );
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
