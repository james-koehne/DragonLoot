using System.Collections;
using System.Collections.Generic;

using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Player-made ground coin tower. Owns an ordered logical slot list; settled same-type runs
/// become cylinders (items despawned); short/mixed tops stay as live stacked items.
/// One shared capsule; kinematic and immovable. Thrown coins may still roll on the treasure
/// surface before joining via auto-stack.
/// </summary>
[RequireComponent( typeof( CapsuleCollider ) )]
public class GroundCoinStack : InteractableBase, ITreasureOwner, ITreasurePlacementTarget
{
	public const int DefaultMaxHeight = 1000;

	static readonly List<GroundCoinStack> All = new List<GroundCoinStack>( 64 );
	static readonly Collider[] MergeOverlap = new Collider[ 32 ];
	static readonly List<TreasureItem> TakeBuffer = new List<TreasureItem>( 64 );
	static readonly List<TreasureDefinition> MergeSlotBuffer = new List<TreasureDefinition>( 64 );

	static readonly List<TreasureDefinition> BindDefBuffer = new List<TreasureDefinition>( 32 );
	static readonly List<int> BindIndexMap = new List<int>( 32 );
	static readonly bool[] BindCoveredScratch = new bool[ 64 ];

	readonly List<TreasureDefinition> _slots = new List<TreasureDefinition>( 32 );
	readonly List<TreasureItem> _settledLive = new List<TreasureItem>( 32 );
	readonly List<TreasureItem> _inFlight = new List<TreasureItem>( 8 );
	readonly List<int> _inFlightSlotIndices = new List<int>( 8 );
	bool _absorbingNearby;

	CoinStackCylinderVisual _cylinderVisual;
	CapsuleCollider _capsule;
	bool[] _cylinderCovered = new bool[ 32 ];
	bool _taking;
	bool _destroying;
	bool _blockMergeAsTarget;
	bool _machineBuffer;
	bool _cartHosted;
	MinecartInteractable _cartHost;
	int _maxCountOverride;
	CoinSortingStation _machineStation;
	int _visualGeneration;
	float _variationSeed;
	bool _preferImperfectLod = true;

	[SerializeField]
	Feedbacks onLandFeedback;

	[SerializeField]
	Feedbacks onRemoveFeedback;

	public TreasureOwnerKind OwnerKind => TreasureOwnerKind.GroundCoinStack;

	public int SettledCount => _slots.Count;
	public int InFlightCount => _inFlight.Count;
	public int Count => _slots.Count;
	public bool HasInFlight => _inFlight.Count > 0 || _blockMergeAsTarget;
	public bool IsHomogeneous => TryGetHomogeneousDefinition( out _ );
	public bool IsMachineBuffer => _machineBuffer;
	public bool IsCartHosted => _cartHosted;
	public MinecartInteractable CartHost => _cartHost;
	public CoinSortingStation MachineStation => _machineStation;
	bool ExcludedFromWorldJoin => _machineBuffer || _cartHosted;
	public int MaxHeight
	{
		get
		{
			if ( _maxCountOverride > 0 )
				return _maxCountOverride;

			CoinStackVisualDefinition def = null;
			def = RuntimeDefinition.Resolve( ref def );
			if ( def != null && def.groundMaxStackHeight > 0 )
				return def.groundMaxStackHeight;
			return DefaultMaxHeight;
		}
	}

	public bool IsFull => Count >= MaxHeight;
	public Vector3 ContactPosition => transform.position;
	public float SettledHeight => MeasureSlotsHeight( _slots.Count );
	public float TotalHeight => SettledHeight;
	public float VariationSeed => _variationSeed > 0.0001f ? _variationSeed : 1f;

	int MinCoinsForPlayerCollision
	{
		get
		{
			CoinStackVisualDefinition def = null;
			def = RuntimeDefinition.Resolve( ref def );
			if ( def == null )
				return 0;
			return Mathf.Max( 0, def.minCoinsForPlayerCollision );
		}
	}

	/// <summary>XZ footprint radius used for gem push-apart and merge queries.</summary>
	public float FootprintRadius => ResolveDiameter() * 0.5f;

	public const float DefaultJoinRadius = 0.11f;

	/// <summary>
	/// XZ radius used to join/create near an existing stack (at least DefaultJoinRadius,
	/// scaled by coin diameter so place snap matches merge reach).
	/// </summary>
	public static float ResolveJoinRadius( TreasureDefinition definition )
	{
		if ( definition == null )
			return DefaultJoinRadius;

		float x = Mathf.Abs( definition.worldScale.x );
		float z = Mathf.Abs( definition.worldScale.z );
		float diameter = Mathf.Max( x, z );
		if ( diameter < 0.0001f )
			diameter = 0.2f;
		return Mathf.Max( DefaultJoinRadius, diameter * 1.25f );
	}

	public static GroundCoinStack FindStackForLooseCoin( TreasureItem coin, float radius = DefaultJoinRadius )
	{
		if ( coin == null )
			return null;

		if ( coin.Owner is GroundCoinStack owned )
			return owned;

		return FindNearest( coin.transform.position, radius );
	}

	public static IReadOnlyList<GroundCoinStack> ActiveStacks => All;

	public static bool IsGroundStackableCoin( TreasureDefinition definition )
	{
		return definition != null
			&& definition.category == TreasureCategory.Coin
			&& definition.canStack;
	}

	public static bool IsGroundStackableCoin( TreasureItem item )
	{
		return item != null && IsGroundStackableCoin( item.Definition );
	}

	public static GroundCoinStack CreateAt( Vector3 contactPosition, Quaternion rotation )
	{
		GameObject go = new GameObject( "GroundCoinStack" );
		go.transform.SetPositionAndRotation( contactPosition, TreasureOrientation.FlattenUpright( rotation ) );
		int layer = LayerMask.NameToLayer( "Collectable" );
		if ( layer >= 0 )
			go.layer = layer;
		GroundCoinStack stack = go.AddComponent<GroundCoinStack>();
		stack.EnsureCollider();
		stack.EnsureVariationSeed();
		return stack;
	}

	/// <summary>
	/// Creates a stack from a settled base coin plus an incoming coin (flight optional).
	/// Base is absorbed immediately; incoming can tween then settle.
	/// </summary>
	public static GroundCoinStack CreateFromPair(
		TreasureItem baseCoin,
		TreasureItem incoming,
		Vector3 incomingEndPos,
		Quaternion incomingEndRot,
		bool animateIncoming )
	{
		if ( baseCoin == null || incoming == null )
			return null;

		Vector3 contact = baseCoin.transform.position;
		Quaternion rot = TreasureOrientation.FlattenUpright( baseCoin.transform.rotation );
		GroundCoinStack stack = CreateAt( contact, rot );
		stack.AbsorbSettledImmediate( baseCoin );
		if ( animateIncoming )
			stack.BeginAppendFlight( incoming, incomingEndRot );
		else
			stack.AbsorbSettledImmediate( incoming );

		stack.TryMergeNearby();
		stack.AbsorbNearbyLooseCoins();
		return stack;
	}

	/// <summary>Find nearest ground stack within XZ radius whose contact is near <paramref name="worldPos"/>.</summary>
	public static GroundCoinStack FindNearest( Vector3 worldPos, float radius )
	{
		GroundCoinStack best = null;
		float bestSq = radius * radius;
		for ( int i = 0; i < All.Count; i++ )
		{
			GroundCoinStack stack = All[ i ];
			if ( stack == null || stack._destroying || stack.ExcludedFromWorldJoin || stack.IsFull )
				continue;

			Vector3 delta = stack.ContactPosition - worldPos;
			float sq = delta.x * delta.x + delta.z * delta.z;
			if ( sq > bestSq )
				continue;

			bestSq = sq;
			best = stack;
		}

		return best;
	}

	/// <summary>
	/// Find the nearest stack whose XZ contact lies within <paramref name="radius"/> of the aim ray segment.
	/// </summary>
	public static GroundCoinStack FindNearestAlongRay( Ray ray, float radius, float maxDistance )
	{
		GroundCoinStack best = null;
		float bestPerpSq = radius * radius;
		float maxDist = Mathf.Max( 0.01f, maxDistance );
		Vector3 origin = ray.origin;
		Vector3 dir = ray.direction;
		if ( dir.sqrMagnitude < 0.0001f )
			return null;
		dir.Normalize();

		for ( int i = 0; i < All.Count; i++ )
		{
			GroundCoinStack stack = All[ i ];
			if ( stack == null || stack._destroying || stack.ExcludedFromWorldJoin || stack.IsFull )
				continue;

			Vector3 contact = stack.ContactPosition;
			Vector3 to = contact - origin;
			float along = Vector3.Dot( to, dir );
			if ( along < -radius || along > maxDist + radius )
				continue;

			Vector3 closest = origin + dir * Mathf.Clamp( along, 0f, maxDist );
			Vector3 delta = contact - closest;
			float xzSq = delta.x * delta.x + delta.z * delta.z;
			if ( xzSq > bestPerpSq )
				continue;

			bestPerpSq = xzSq;
			best = stack;
		}

		return best;
	}

	/// <summary>
	/// Marks this stack as a machine-owned buffer (e.g. sorting hopper): no player take,
	/// excluded from world merges / FindNearest, persists when empty, optional capacity cap.
	/// </summary>
	public void ConfigureAsMachineBuffer( int maxCount = 0, CoinSortingStation station = null )
	{
		_machineBuffer = true;
		_maxCountOverride = maxCount > 0 ? maxCount : 0;
		_machineStation = station;
		SetInteractionName( "Hopper" );
		RefreshCollider();
	}

	/// <summary>
	/// Parent this stack to a minecart cargo slot: player take/place match world stacks,
	/// but it does not merge with floor piles or collide with the rider.
	/// </summary>
	public void ConfigureForMinecart( MinecartInteractable cart, int maxCount = 0 )
	{
		_cartHosted = true;
		_cartHost = cart;
		_maxCountOverride = maxCount > 0 ? maxCount : 0;
		SetInteractionName( "Coin Stack" );
		RefreshCollider();
	}

	public void DetachCartHost()
	{
		_cartHost = null;
		_cartHosted = true;
	}

	public void SetMaxCountOverride( int maxCount )
	{
		_maxCountOverride = maxCount > 0 ? maxCount : 0;
	}

	void OnEnable()
	{
		if ( !All.Contains( this ) )
			All.Add( this );
		EnsureCollider();
		EnsureVariationSeed();
		EnsureCountFeedback();
		SetInteractionName( "Coin Stack" );
		_preferImperfectLod = EvaluatePreferImperfectLod( forceNear: true );
	}

	void LateUpdate()
	{
		bool wantImperfect = EvaluatePreferImperfectLod( forceNear: false );
		if ( wantImperfect == _preferImperfectLod )
			return;

		_preferImperfectLod = wantImperfect;
		RefreshVisuals( snap: true );
	}

	bool EvaluatePreferImperfectLod( bool forceNear )
	{
		CoinStackVisualDefinition def = null;
		def = RuntimeDefinition.Resolve( ref def );
		if ( !CoinStackImperfectLayout.IsImperfectEnabled( def ) )
			return false;

		float maxDist = def.imperfectCylinderDistance;
		if ( maxDist <= 0.0001f )
			return true;

		if ( !TreasureProximitySleep.TryGetPlayerPosition( out Vector3 playerPos ) )
			return true;

		float distSq = ( ContactPosition - playerPos ).sqrMagnitude;
		if ( forceNear || _preferImperfectLod )
		{
			float leave = maxDist + Mathf.Max( 0f, def.imperfectCylinderDistanceHysteresis );
			return distSq <= leave * leave;
		}

		return distSq <= maxDist * maxDist;
	}

	void EnsureVariationSeed()
	{
		if ( _variationSeed > 0.0001f )
			return;

		// Mix instance id with contact XZ so nearby stacks rarely share the same pattern.
		int id = Mathf.Abs( GetInstanceID() );
		Vector3 p = transform.position;
		unchecked
		{
			int h = id;
			h ^= (int)( p.x * 738.56093f );
			h ^= (int)( p.z * 193.49663f );
			h ^= (int)( p.y * 83.492791f );
			if ( h < 0 )
				h = -h;
			_variationSeed = ( h % 99773 ) + 1 + ( ( h >> 3 ) & 1023 ) * 0.001f;
		}

		if ( _variationSeed < 0.0001f )
			_variationSeed = 1f;
	}

	public void ApplyCylinderVariationSeed()
	{
		EnsureVariationSeed();
		if ( _cylinderVisual != null )
			_cylinderVisual.SetVariationSeed( _variationSeed );
	}

	void OnDisable()
	{
		All.Remove( this );
	}

	void OnDestroy()
	{
		_destroying = true;
		All.Remove( this );
		ClearAllVisuals();
		NotifyCartHostDestroyed();
	}

	public void ReleaseTreasure( TreasureItem item )
	{
		Remove( item );
	}

	public void Remove( TreasureItem item )
	{
		if ( item == null )
			return;

		int flightIndex = _inFlight.IndexOf( item );
		if ( flightIndex >= 0 )
		{
			int slotIndex = flightIndex < _inFlightSlotIndices.Count ? _inFlightSlotIndices[ flightIndex ] : -1;
			ReleaseReservedSlot( slotIndex, item );
			return;
		}

		for ( int i = 0; i < _settledLive.Count; i++ )
		{
			if ( _settledLive[ i ] != item )
				continue;

			if ( i < _slots.Count )
				_slots.RemoveAt( i );
			_settledLive.RemoveAt( i );
			DecrementInFlightSlotIndicesAfter( i );
			RefreshVisuals( snap: false );
			RefreshCollider();
			if ( Count <= 0 )
				DestroyIfEmpty();
			else
				PlayRemoveFeedback();
			return;
		}
	}

	public bool CanAccept( TreasureDefinition definition )
	{
		return IsGroundStackableCoin( definition ) && !IsFull;
	}

	public bool CanPlace( TreasureItem item, in PlacementQuery query )
	{
		return item != null && CanAccept( item.Definition );
	}

	public bool TryGetPlacementPreview( TreasureItem item, in PlacementQuery query, out PlacementPreview preview )
	{
		preview = default;
		if ( item == null )
			return false;

		Vector3 scale = item.GetWorldScale();
		preview.SetItemMesh(
			GetSlotWorldPosition( Count ),
			transform.rotation,
			scale,
			CanPlace( item, in query ) );
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

		if ( _machineBuffer )
		{
			if ( _machineStation == null )
				return false;

			if ( !carry.TryConsumeActive( out TreasureItem one ) || one == null )
				return false;

			if ( !CanAccept( one.Definition ) )
			{
				one.EnterPhysics( one.transform.position, one.transform.rotation );
				return false;
			}

			BeginAppendFlight( one, transform.rotation );
			return true;
		}

		if ( !carry.TryRemoveBottomCluster( out List<TreasureItem> cluster ) || cluster == null || cluster.Count == 0 )
			return false;

		for ( int i = 0; i < cluster.Count; i++ )
		{
			TreasureItem member = cluster[ i ];
			if ( member == null )
				continue;

			if ( !CanAccept( member.Definition ) )
			{
				member.EnterPhysics( member.transform.position, member.transform.rotation );
				continue;
			}

			BeginAppendFlight( member, transform.rotation );
		}

		NotifyCartHostCargoPlaced();
		return true;
	}

	/// <summary>Absorb a world-loose coin as the next settled slot (no flight).</summary>
	public bool TryAbsorbLooseImmediate( TreasureItem item )
	{
		if ( item == null || !CanAccept( item.Definition ) || item.IsInFlight )
			return false;

		AbsorbSettledImmediate( item );
		PlayLandFeedback();
		if ( !ExcludedFromWorldJoin )
		{
			AbsorbNearbyLooseCoins();
			TryMergeNearby();
		}
		return true;
	}

	/// <summary>
	/// Sweep nearby world-loose stackable coins into this stack with hop-then-arc flight.
	/// </summary>
	public void AbsorbNearbyLooseCoins( float radius = DefaultJoinRadius )
	{
		if ( _absorbingNearby || _destroying || ExcludedFromWorldJoin || IsFull || radius <= 0.0001f )
			return;

		_absorbingNearby = true;
		try
		{
			int hits = Physics.OverlapSphereNonAlloc(
				ContactPosition,
				radius,
				MergeOverlap,
				Physics.DefaultRaycastLayers,
				QueryTriggerInteraction.Ignore );

			for ( int i = 0; i < hits; i++ )
			{
				if ( IsFull )
					break;

				Collider col = MergeOverlap[ i ];
				if ( col == null )
					continue;

				TreasureItem candidate = col.GetComponentInParent<TreasureItem>();
				if ( candidate == null || !candidate.IsWorldLoose || candidate.IsInFlight || candidate.IsReclaiming )
					continue;

				if ( candidate.Owner is GroundCoinStack )
					continue;

				if ( !CanAccept( candidate.Definition ) )
					continue;

				Vector3 delta = candidate.transform.position - ContactPosition;
				float xzSq = delta.x * delta.x + delta.z * delta.z;
				if ( xzSq > radius * radius )
					continue;

				BeginAppendFlight( candidate, transform.rotation );
			}
		}
		finally
		{
			_absorbingNearby = false;
		}
	}

	public void BeginAppendFlight( TreasureItem item, Quaternion endWorldRot )
	{
		if ( item == null || item.Definition == null || !CanAccept( item.Definition ) )
			return;

		TreasureSurfaceWorld surfaceWorld = TreasureSurfaceWorld.Instance;
		if ( surfaceWorld != null && surfaceWorld.Simulator != null )
			surfaceWorld.Simulator.Unregister( item );

		int slotIndex = _slots.Count;
		_slots.Add( item.Definition );
		_settledLive.Add( null );
		_inFlight.Add( item );
		_inFlightSlotIndices.Add( slotIndex );

		Vector3 endWorldPos = GetSlotWorldPosition( slotIndex );
		if ( endWorldRot == default )
			endWorldRot = transform.rotation;

		item.ClaimPendingStackOwner( this );
		item.BeginFlight();
		RefreshCollider();

		bool flip = CoinFlipMotion.IsCoin( item );
		TreasureSurfaceDefinition surfaceDef = ResolveSurfaceDefinition();
		float duration;
		float hops;
		float riseFraction;
		float secondaryArc;
		float spins;
		Vector3 apexOffset = Vector3.zero;
		if ( flip && surfaceDef != null )
		{
			duration = Vary( surfaceDef.autoStackFlipDuration, surfaceDef.autoStackDurationVariance );
			hops = Vary( surfaceDef.autoStackHopHeight, surfaceDef.autoStackHopHeightVariance );
			riseFraction = Mathf.Clamp(
				Vary( surfaceDef.autoStackRiseFraction, surfaceDef.autoStackRiseFractionVariance ),
				0.05f,
				0.6f );
			secondaryArc = Vary( surfaceDef.autoStackSecondaryArcHeight, surfaceDef.autoStackHopHeightVariance * 0.5f );
			spins = Vary( surfaceDef.autoStackFlipSpins, surfaceDef.autoStackSpinVariance );
			float jitter = surfaceDef.autoStackApexJitter;
			if ( jitter > 0.0001f )
			{
				float angle = Random.Range( 0f, Mathf.PI * 2f );
				float radius = Random.Range( 0f, jitter );
				apexOffset = new Vector3( Mathf.Cos( angle ) * radius, 0f, Mathf.Sin( angle ) * radius );
			}
		}
		else if ( flip )
		{
			duration = Vary( CoinFlipMotion.DefaultDuration, 0.2f );
			hops = Vary( CoinFlipMotion.DefaultArcHeight, 0.3f );
			riseFraction = Vary( 0.35f, 0.2f );
			secondaryArc = 0.15f;
			spins = Vary( CoinFlipMotion.DefaultSpins, 0.25f );
		}
		else
		{
			duration = CoinFlipMotion.DefaultItemArcDuration;
			hops = 0f;
			riseFraction = 0.28f;
			secondaryArc = CoinFlipMotion.DefaultItemArcHeight;
			spins = 0f;
		}

		TreasureMotionHost.Run( AppendFlightRoutine(
			item,
			endWorldPos,
			endWorldRot,
			duration,
			useHopThenArc: flip,
			hopHeight: hops,
			riseFraction: riseFraction,
			secondaryArcHeight: secondaryArc,
			spins: spins,
			apexOffset: apexOffset ) );

		if ( !ExcludedFromWorldJoin )
		{
			TryMergeNearby();
			AbsorbNearbyLooseCoins();
		}

		PublishStackChanged( item.Definition );
	}

	static float Vary( float baseValue, float varianceFraction )
	{
		if ( varianceFraction <= 0.0001f || baseValue <= 0f )
			return baseValue;
		float scale = 1f + Random.Range( -varianceFraction, varianceFraction );
		return Mathf.Max( 0.05f, baseValue * scale );
	}

	static TreasureSurfaceDefinition ResolveSurfaceDefinition()
	{
		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		return world != null ? world.Definition : null;
	}

	IEnumerator AppendFlightRoutine(
		TreasureItem item,
		Vector3 endPos,
		Quaternion endRot,
		float duration,
		bool useHopThenArc,
		float hopHeight,
		float riseFraction,
		float secondaryArcHeight,
		float spins,
		Vector3 apexOffset )
	{
		List<TreasureItem> cluster = new List<TreasureItem>( 1 ) { item };
		Vector3[] ends = { endPos };
		Quaternion[] rots = { endRot };
		yield return CoinFlipMotion.AnimateWorldFlips(
			cluster,
			ends,
			rots,
			duration,
			hopHeight,
			spins,
			useHopThenArc,
			hopHeight,
			riseFraction,
			secondaryArcHeight,
			apexOffset );

		if ( item == null || _destroying )
			yield break;

		int flightIndex = _inFlight.IndexOf( item );
		if ( flightIndex < 0 )
			yield break;

		int slotIndex = flightIndex < _inFlightSlotIndices.Count
			? _inFlightSlotIndices[ flightIndex ]
			: -1;

		_inFlight.RemoveAt( flightIndex );
		if ( flightIndex < _inFlightSlotIndices.Count )
			_inFlightSlotIndices.RemoveAt( flightIndex );

		item.EndFlight();

		if ( item.State == TreasureItemState.Held && item.Owner is PlayerCarry carry && carry.ContainsItem( item ) )
		{
			ReleaseReservedSlot( slotIndex, item );
			RefreshCollider();
			yield break;
		}

		// Block merge-as-target across finalize so RefreshVisuals sees a settled slot.
		_blockMergeAsTarget = true;
		FinalizeSlot( slotIndex, item, playCoinPlaceFeedback: true );
		_blockMergeAsTarget = false;

		TreasureInteractSfx.PlayPlace( item.Definition, endPos );

		TryMergeNearby();
		AbsorbNearbyLooseCoins();
	}

	void FinalizeSlot( int slotIndex, TreasureItem item )
	{
		FinalizeSlot( slotIndex, item, playCoinPlaceFeedback: false );
	}

	void FinalizeSlot( int slotIndex, TreasureItem item, bool playCoinPlaceFeedback )
	{
		if ( item == null || item.Definition == null )
			return;

		if ( slotIndex < 0 || slotIndex >= _slots.Count )
		{
			// Stale index — append as a new settled slot rather than orphaning the coin.
			slotIndex = _slots.Count;
			_slots.Add( item.Definition );
			_settledLive.Add( null );
		}

		_slots[ slotIndex ] = item.Definition;
		while ( _settledLive.Count <= slotIndex )
			_settledLive.Add( null );

		_settledLive[ slotIndex ] = item;

		if ( playCoinPlaceFeedback )
			CoinGemInteractFeedback.PlayPlace( item );

		RefreshVisuals( snap: true );
		RefreshCollider();

		// If the cylinder absorbed this slot, keep the live coin briefly so place scale-pop can finish.
		if ( slotIndex < _cylinderCovered.Length && _cylinderCovered[ slotIndex ]
			&& slotIndex < _settledLive.Count
			&& _settledLive[ slotIndex ] != null )
		{
			TreasureItem live = _settledLive[ slotIndex ];
			if ( playCoinPlaceFeedback && live != null )
				TreasureMotionHost.Run( DelayedDespawnCoveredLive( slotIndex, live ) );
			else
				DespawnCoveredLive( slotIndex );
		}

		if ( !ExcludedFromWorldJoin )
			AbsorbNearbyLooseCoins();

		PlayLandFeedback();
	}

	IEnumerator DelayedDespawnCoveredLive( int slotIndex, TreasureItem expected )
	{
		float wait = 0.22f;
		CarryDefinition carryDef = null;
		carryDef = RuntimeDefinition.Resolve( ref carryDef );
		_ = carryDef;
		yield return new WaitForSeconds( wait );

		if ( _destroying )
			yield break;

		if ( slotIndex < 0 || slotIndex >= _settledLive.Count )
			yield break;

		if ( _settledLive[ slotIndex ] != expected )
			yield break;

		if ( slotIndex < _cylinderCovered.Length && _cylinderCovered[ slotIndex ] )
			DespawnCoveredLive( slotIndex );
	}

	public void PlayLandFeedback()
	{
		EnsureCountFeedback();
		if ( onRemoveFeedback != null )
			onRemoveFeedback.Stop();
		if ( onLandFeedback != null )
			onLandFeedback.Play();
	}

	public void PlayRemoveFeedback()
	{
		EnsureCountFeedback();
		if ( onLandFeedback != null )
			onLandFeedback.Stop();
		if ( onRemoveFeedback != null )
			onRemoveFeedback.Play();
	}

	void EnsureCountFeedback()
	{
		if ( onLandFeedback == null )
			onLandFeedback = CreatePunchFeedback(
				"OnAddFeedbacks",
				new Vector3( 0.07f, -0.09f, 0.07f ),
				0.18f );

		if ( onRemoveFeedback == null )
			onRemoveFeedback = CreatePunchFeedback(
				"OnRemoveFeedbacks",
				new Vector3( -0.045f, 0.05f, -0.045f ),
				0.14f );
	}

	Feedbacks CreatePunchFeedback( string childName, Vector3 punch, float duration )
	{
		Transform existing = transform.Find( childName );
		GameObject go = existing != null ? existing.gameObject : new GameObject( childName );
		if ( existing == null )
			go.transform.SetParent( transform, false );

		Feedbacks feedbacks = go.GetComponent<Feedbacks>();
		if ( feedbacks == null )
			feedbacks = go.AddComponent<Feedbacks>();

		feedbacks.Initialize();
		if ( feedbacks.FeedbackList != null && feedbacks.FeedbackList.Count > 0 )
			return feedbacks;

		feedbacks.AddFeedback( new PunchScaleFeedback
		{
			Target = transform,
			Punch = punch,
			Duration = duration
		} );
		return feedbacks;
	}

	void DespawnCoveredLive( int index )
	{
		if ( index < 0 || index >= _settledLive.Count )
			return;

		TreasureItem live = _settledLive[ index ];
		if ( live == null )
			return;

		_settledLive[ index ] = null;
		live.transform.SetParent( null, true );
		TreasureItemFactory.Despawn( live );
	}

	void ReleaseReservedSlot( int slotIndex, TreasureItem item )
	{
		int flightIndex = _inFlight.IndexOf( item );
		if ( flightIndex >= 0 )
		{
			_inFlight.RemoveAt( flightIndex );
			if ( flightIndex < _inFlightSlotIndices.Count )
				_inFlightSlotIndices.RemoveAt( flightIndex );
		}

		if ( slotIndex < 0 || slotIndex >= _slots.Count )
		{
			RefreshCollider();
			return;
		}

		_slots.RemoveAt( slotIndex );
		if ( slotIndex < _settledLive.Count )
			_settledLive.RemoveAt( slotIndex );

		DecrementInFlightSlotIndicesAfter( slotIndex );
		RefreshCollider();
		if ( Count <= 0 )
			DestroyIfEmpty();
	}

	void DecrementInFlightSlotIndicesAfter( int removedIndex )
	{
		for ( int i = 0; i < _inFlightSlotIndices.Count; i++ )
		{
			if ( _inFlightSlotIndices[ i ] > removedIndex )
				_inFlightSlotIndices[ i ]--;
		}
	}

	bool IsSlotInFlight( int slotIndex )
	{
		for ( int i = 0; i < _inFlightSlotIndices.Count; i++ )
		{
			if ( _inFlightSlotIndices[ i ] == slotIndex )
				return true;
		}

		return false;
	}

	public void AbsorbSettledImmediate( TreasureItem item )
	{
		if ( item == null || item.Definition == null )
			return;

		int slotIndex = _slots.Count;
		_slots.Add( item.Definition );
		while ( _settledLive.Count <= slotIndex )
			_settledLive.Add( null );

		Vector3 localPos = GetSlotLocalPosition( slotIndex );
		item.EnterStacked( this, transform, localPos, Quaternion.identity );
		_settledLive[ slotIndex ] = item;
		RefreshVisuals( snap: true );
		RefreshCollider();
		PublishStackChanged( item.Definition );
	}

	void PublishStackChanged( TreasureDefinition topCoin )
	{
		if ( _machineBuffer || _destroying )
			return;

		EventBus.Publish( new CoinStackChangedEvent
		{
			Stack = this,
			Count = _slots.Count,
			TopCoin = topCoin
		} );
	}

	/// <summary>
	/// Append one logical coin of <paramref name="definition"/> without a live item
	/// (cylinder / individual visuals come from <see cref="RefreshVisuals"/>).
	/// Used by machine output (coin sorting station).
	/// </summary>
	public bool TryAppendDefinition( TreasureDefinition definition )
	{
		if ( !IsGroundStackableCoin( definition ) || _destroying || IsFull )
			return false;

		_slots.Add( definition );
		_settledLive.Add( null );
		RefreshVisuals( snap: true );
		RefreshCollider();
		PlayLandFeedback();
		PublishStackChanged( definition );
		return true;
	}

	/// <summary>
	/// Append many logical coins without spawning individuals. Returns how many were accepted.
	/// </summary>
	public int TryAppendDefinitions( IReadOnlyList<TreasureDefinition> definitions )
	{
		if ( definitions == null || definitions.Count == 0 || _destroying )
			return 0;

		int added = 0;
		for ( int i = 0; i < definitions.Count; i++ )
		{
			TreasureDefinition def = definitions[ i ];
			if ( !IsGroundStackableCoin( def ) || IsFull )
				break;

			_slots.Add( def );
			_settledLive.Add( null );
			added++;
		}

		if ( added <= 0 )
			return 0;

		RefreshVisuals( snap: false );
		RefreshCollider();
		if ( !ExcludedFromWorldJoin )
			TryMergeNearby();
		PlayLandFeedback();
		if ( !ExcludedFromWorldJoin && added > 0 )
			PublishStackChanged( definitions[ definitions.Count - 1 ] );

		NotifyCartHostCargoPlaced();
		return added;
	}

	/// <summary>Peek the bottom (oldest) logical slot without removing it.</summary>
	public bool TryPeekBottomDefinition( out TreasureDefinition definition )
	{
		definition = null;
		if ( _destroying || _slots.Count <= 0 )
			return false;

		definition = _slots[ 0 ];
		return definition != null;
	}

	/// <summary>
	/// Remove and return the bottom (oldest) logical slot. Despawns any live mesh for that slot.
	/// Machine buffers persist when emptied; world stacks destroy.
	/// </summary>
	public bool TryConsumeBottomDefinition( out TreasureDefinition definition )
	{
		definition = null;
		if ( _destroying || _slots.Count <= 0 )
			return false;

		if ( IsSlotInFlight( 0 ) )
			return false;

		definition = _slots[ 0 ];
		if ( definition == null )
			return false;

		DespawnCoveredLive( 0 );
		_slots.RemoveAt( 0 );
		if ( _settledLive.Count > 0 )
			_settledLive.RemoveAt( 0 );
		DecrementInFlightSlotIndicesAfter( 0 );
		RefreshVisuals( snap: true );
		RefreshCollider();
		if ( Count <= 0 )
		{
			if ( _machineBuffer )
				PlayRemoveFeedback();
			DestroyIfEmpty();
		}
		else
			PlayRemoveFeedback();
		return true;
	}

	/// <summary>
	/// Copies every logical slot into <paramref name="into"/> (including mixed stacks),
	/// despawns live / in-flight items, and destroys this stack. No world reclaim.
	/// </summary>
	public bool TryConsumeAllDefinitions( System.Collections.Generic.List<TreasureDefinition> into )
	{
		if ( into == null || _destroying )
			return false;

		for ( int i = 0; i < _slots.Count; i++ )
		{
			if ( _slots[ i ] != null )
				into.Add( _slots[ i ] );
		}

		_destroying = true;

		for ( int i = 0; i < _inFlight.Count; i++ )
		{
			TreasureItem flight = _inFlight[ i ];
			if ( flight == null )
				continue;
			flight.EndFlight();
			TreasureItemFactory.Despawn( flight );
		}

		_inFlight.Clear();
		_inFlightSlotIndices.Clear();
		ClearAllVisuals();
		_slots.Clear();
		_settledLive.Clear();
		All.Remove( this );
		Destroy( gameObject );
		return true;
	}

	/// <summary>
	/// Debug-only: fill an empty stack with <paramref name="count"/> copies of one definition
	/// without spawning live items first (cylinders / individuals come from RefreshVisuals).
	/// </summary>
	public bool DebugFillHomogeneous( TreasureDefinition definition, int count )
	{
		if ( !IsGroundStackableCoin( definition ) || _destroying || Count > 0 )
			return false;

		int clamped = Mathf.Clamp( count, 1, MaxHeight );
		_slots.Capacity = Mathf.Max( _slots.Capacity, clamped );
		_settledLive.Capacity = Mathf.Max( _settledLive.Capacity, clamped );
		for ( int i = 0; i < clamped; i++ )
		{
			_slots.Add( definition );
			_settledLive.Add( null );
		}

		RefreshVisuals( snap: true );
		RefreshCollider();
		return true;
	}

	/// <summary>
	/// Debug-only: fill an empty stack from an ordered definition list (mixed stacks).
	/// Live meshes are spawned only for slots not covered by cylinders.
	/// </summary>
	public bool DebugFillSlots( IReadOnlyList<TreasureDefinition> definitions )
	{
		if ( definitions == null || definitions.Count == 0 || _destroying || Count > 0 )
			return false;

		int clamped = Mathf.Min( definitions.Count, MaxHeight );
		for ( int i = 0; i < clamped; i++ )
		{
			if ( !IsGroundStackableCoin( definitions[ i ] ) )
				return false;
		}

		_slots.Capacity = Mathf.Max( _slots.Capacity, clamped );
		_settledLive.Capacity = Mathf.Max( _settledLive.Capacity, clamped );
		for ( int i = 0; i < clamped; i++ )
		{
			_slots.Add( definitions[ i ] );
			_settledLive.Add( null );
		}

		RefreshVisuals( snap: true );
		RefreshCollider();
		return true;
	}

	/// <summary>Debug-only: destroy this stack and clear visuals.</summary>
	public void DebugDespawn()
	{
		DestroyStack();
	}

	public override bool UsesPickupInteract => true;

	public override bool CanInteract( PlayerController player )
	{
		if ( _machineBuffer )
			return false;
		if ( !IsAvailable || player == null || _taking || HasInFlight || Count <= 0 )
			return false;

		return CanTakeFromIndex( player, ResolvePickupStartIndex( player ) );
	}

	public override void Interact( PlayerController player )
	{
		if ( player == null || _taking || HasInFlight )
			return;

		int start = ResolvePickupStartIndex( player );
		TryTakeSingleAtIndex( player, start );
	}

	int ResolvePickupStartIndex( PlayerController player )
	{
		if ( SettledCount <= 0 )
			return 0;

		if ( player == null || player.Interaction == null )
			return Mathf.Max( 0, SettledCount - 1 );

		float bottomY = transform.position.y;
		float topY = bottomY + SettledHeight;
		float aimY = transform.position.y;
		bool haveAimY = false;

		// Prefer the raycast surface hit on this stack. Closest-approach-to-axis reads low when
		// looking down at a thick capsule, which picks one coin below the crosshair.
		if ( player.Interaction.TryGetLastHit( out RaycastHit hit ) && hit.collider != null )
		{
			GroundCoinStack hitStack = hit.collider.GetComponentInParent<GroundCoinStack>();
			if ( hitStack == this )
			{
				aimY = hit.point.y;
				haveAimY = true;
			}
		}

		if ( !haveAimY )
		{
			if ( !player.Interaction.TryGetAimRay( out Ray ray ) )
				return Mathf.Max( 0, SettledCount - 1 );

			aimY = ray.origin.y;
			Vector3 dir = ray.direction;
			float a = dir.x * dir.x + dir.z * dir.z;
			if ( a >= 0.0001f )
			{
				float dx = transform.position.x - ray.origin.x;
				float dz = transform.position.z - ray.origin.z;
				float t = ( dx * dir.x + dz * dir.z ) / a;
				if ( t < 0f )
					t = 0f;
				aimY = ( ray.origin + dir * t ).y;
			}
		}

		if ( topY > bottomY + 0.0001f )
			aimY = Mathf.Clamp( aimY, bottomY, topY );

		float stacked = 0f;
		int last = _slots.Count - 1;
		for ( int i = 0; i < _slots.Count; i++ )
		{
			float step = TreasureStackSpacing.GetStep( _slots[ i ] );
			float yMin = bottomY + stacked;
			float yMax = yMin + step;
			bool inside = i == last
				? aimY >= yMin && aimY <= yMax
				: aimY >= yMin && aimY < yMax;
			if ( inside )
				return i;
			stacked += step;
		}

		return Mathf.Max( 0, SettledCount - 1 );
	}

	bool CanTakeFromIndex( PlayerController player, int startIndex )
	{
		if ( player == null || SettledCount <= 0 )
			return false;

		startIndex = Mathf.Clamp( startIndex, 0, SettledCount - 1 );
		PlayerCarry carry = player.Carry;
		if ( carry == null )
			return false;

		TreasureDefinition def = _slots[ startIndex ];
		return def != null && carry.CanAdd( def );
	}

	/// <summary>LMB: take only the aimed coin into the right hand.</summary>
	public bool TryTakeSingleAtIndex( PlayerController player, int index )
	{
		if ( player == null || player.Carry == null || _taking || HasInFlight )
			return false;
		if ( SettledCount <= 0 )
			return false;

		index = Mathf.Clamp( index, 0, SettledCount - 1 );
		if ( !CanTakeFromIndex( player, index ) )
			return false;

		_taking = true;
		TreasureDefinition aimedDef = _slots[ index ];
		Vector3 aimPos = GetSlotWorldPosition( index );
		Quaternion rot = transform.rotation;

		DespawnCoveredLive( index );
		_slots.RemoveAt( index );
		if ( index < _settledLive.Count )
			_settledLive.RemoveAt( index );
		DecrementInFlightSlotIndicesAfter( index );

		RefreshVisuals( snap: false );
		RefreshCollider();

		PlayerCarry carry = player.Carry;
		carry.TrySetSelectedBucket( CarryBucketKind.Coin );

		TreasureItem aimedCoin = TreasureItemFactory.RentVisualCoin( aimedDef, aimPos, rot );
		bool receivedActive = aimedCoin != null && carry.TryReceiveActiveCoinFromWorld( aimedCoin );
		if ( !receivedActive && aimedCoin != null )
			TreasureItemFactory.ReturnVisualCoin( aimedCoin );

		_taking = false;
		if ( Count <= 0 )
			DestroyIfEmpty();
		else
			PlayRemoveFeedback();
		return receivedActive;
	}

	public bool TryTakeFromIndexUp( PlayerController player, int startIndex )
	{
		// Legacy name — LMB uses single-coin take only.
		return TryTakeSingleAtIndex( player, startIndex );
	}

	public void TryMergeNearby()
	{
		if ( _destroying || ExcludedFromWorldJoin || Count <= 0 )
			return;

		float radius = ResolveMergeRadius();
		int hits = Physics.OverlapSphereNonAlloc(
			ContactPosition,
			radius,
			MergeOverlap,
			Physics.DefaultRaycastLayers,
			QueryTriggerInteraction.Ignore );

		for ( int i = 0; i < hits; i++ )
		{
			Collider col = MergeOverlap[ i ];
			if ( col == null )
				continue;

			GroundCoinStack other = col.GetComponentInParent<GroundCoinStack>();
			if ( other == null || other == this || other._destroying || other.ExcludedFromWorldJoin )
				continue;

			// Allow merge while this stack still has in-flight coins so spam-created twins collapse.
			if ( other.HasInFlight || other._blockMergeAsTarget )
				continue;

			Vector3 delta = other.ContactPosition - ContactPosition;
			float xzSq = delta.x * delta.x + delta.z * delta.z;
			if ( xzSq > radius * radius )
				continue;

			int thisSettled = CountSettledLiveItems();
			int otherSettled = other.CountSettledLiveItems();

			// Prefer the larger settled stack as the survivor so a newly placed single-coin
			// stack does not yank an existing tower onto the place contact.
			if ( otherSettled > thisSettled )
			{
				// Do not merge while we still have in-flight — DestroyStack would dump mid-arc coins.
				if ( HasInFlight )
					continue;

				other.MergeFrom( this );
				other.AbsorbNearbyLooseCoins();
				return;
			}

			// Absorb the other into this (keep this contact position).
			MergeFrom( other );
			AbsorbNearbyLooseCoins();
			break;
		}
	}

	int CountSettledLiveItems()
	{
		int n = 0;
		for ( int i = 0; i < _slots.Count; i++ )
		{
			if ( IsSlotInFlight( i ) )
				continue;
			if ( _slots[ i ] != null )
				n++;
		}

		return n;
	}

	void MergeFrom( GroundCoinStack other )
	{
		if ( other == null || other == this )
			return;

		MergeSlotBuffer.Clear();
		for ( int i = 0; i < other._slots.Count; i++ )
			MergeSlotBuffer.Add( other._slots[ i ] );

		// Capture live items before other clears.
		List<TreasureItem> otherLive = new List<TreasureItem>( other._settledLive.Count );
		for ( int i = 0; i < other._settledLive.Count; i++ )
			otherLive.Add( other._settledLive[ i ] );

		other._slots.Clear();
		other._settledLive.Clear();
		other.ClearAllVisuals();
		other.DestroyStack();

		for ( int i = 0; i < MergeSlotBuffer.Count; i++ )
		{
			if ( IsFull )
				break;

			TreasureDefinition def = MergeSlotBuffer[ i ];
			TreasureItem live = i < otherLive.Count ? otherLive[ i ] : null;
			_slots.Add( def );
			if ( live != null )
			{
				Vector3 localPos = GetSlotLocalPosition( _slots.Count - 1 );
				live.EnterStacked( this, transform, localPos, Quaternion.identity );
				_settledLive.Add( live );
			}
			else
				_settledLive.Add( null );
		}

		for ( int i = MergeSlotBuffer.Count; i < otherLive.Count; i++ )
		{
			if ( otherLive[ i ] != null )
				TreasureItemFactory.Despawn( otherLive[ i ] );
		}

		RefreshVisuals( snap: true );
		RefreshCollider();
	}

	void RefreshVisuals( bool snap )
	{
		_visualGeneration++;
		EnsureCylinderCoverageBuffer();
		for ( int i = 0; i < _cylinderCovered.Length; i++ )
			_cylinderCovered[ i ] = false;

		// Cylinder only from settled slots — in-flight reservations must not hide/despawn coins early.
		BindDefBuffer.Clear();
		BindIndexMap.Clear();
		for ( int i = 0; i < _slots.Count; i++ )
		{
			if ( IsSlotInFlight( i ) )
				continue;

			BindIndexMap.Add( i );
			BindDefBuffer.Add( _slots[ i ] );
		}

		bool[] bindCovered = BindCoveredScratch;
		if ( bindCovered.Length < BindDefBuffer.Count )
			bindCovered = new bool[ Mathf.Max( 64, BindDefBuffer.Count ) ];

		for ( int i = 0; i < bindCovered.Length; i++ )
			bindCovered[ i ] = false;

		CoinColumnCylinderBinder.BindDefinitions(
			ref _cylinderVisual,
			transform,
			BindDefBuffer,
			snap,
			bindCovered,
			useHeldScale: false,
			variationSeed: VariationSeed,
			preferImperfect: _preferImperfectLod );
		ApplyCylinderVariationSeed();

		for ( int b = 0; b < BindIndexMap.Count; b++ )
		{
			if ( b < bindCovered.Length && bindCovered[ b ] )
				_cylinderCovered[ BindIndexMap[ b ] ] = true;
		}

		for ( int i = 0; i < _slots.Count; i++ )
		{
			if ( IsSlotInFlight( i ) )
				continue;

			bool covered = i < _cylinderCovered.Length && _cylinderCovered[ i ];
			TreasureItem live = i < _settledLive.Count ? _settledLive[ i ] : null;

			if ( covered )
			{
				if ( live != null )
					DespawnCoveredLive( i );
				continue;
			}

			if ( live != null )
			{
				Vector3 localPos = GetSlotLocalPosition( i );
				live.EnterStacked( this, transform, localPos, Quaternion.identity );
				live.SetMeshVisible( true );
				continue;
			}

			SpawnIndividualForSlot( i, _visualGeneration );
		}

		// Trim live list length to slots.
		while ( _settledLive.Count > _slots.Count )
		{
			int last = _settledLive.Count - 1;
			if ( _settledLive[ last ] != null )
			{
				TreasureItem orphan = _settledLive[ last ];
				_settledLive[ last ] = null;
				orphan.transform.SetParent( null, true );
				TreasureItemFactory.Despawn( orphan );
			}

			_settledLive.RemoveAt( last );
		}

		while ( _settledLive.Count < _slots.Count )
			_settledLive.Add( null );
	}

	async void SpawnIndividualForSlot( int index, int generation )
	{
		if ( index < 0 || index >= _slots.Count )
			return;
		if ( index < _cylinderCovered.Length && _cylinderCovered[ index ] )
			return;
		if ( index < _settledLive.Count && _settledLive[ index ] != null )
			return;
		if ( IsSlotInFlight( index ) )
			return;

		TreasureDefinition def = _slots[ index ];
		Vector3 worldPos = GetSlotWorldPosition( index );
		TreasureItem item = await TreasureItemFactory.SpawnAsync( def, worldPos, transform.rotation, null );
		if ( item == null || _destroying || generation != _visualGeneration )
		{
			if ( item != null )
				TreasureItemFactory.Despawn( item );
			return;
		}

		if ( index >= _slots.Count || _slots[ index ] != def || IsSlotInFlight( index ) )
		{
			TreasureItemFactory.Despawn( item );
			return;
		}

		if ( index < _cylinderCovered.Length && _cylinderCovered[ index ] )
		{
			TreasureItemFactory.Despawn( item );
			return;
		}

		while ( _settledLive.Count <= index )
			_settledLive.Add( null );

		if ( _settledLive[ index ] != null )
		{
			TreasureItemFactory.Despawn( item );
			return;
		}

		item.EnterStacked( this, transform, GetSlotLocalPosition( index ), Quaternion.identity );
		item.SetMeshVisible( true );
		_settledLive[ index ] = item;
	}

	void EnsureCylinderCoverageBuffer()
	{
		if ( _cylinderCovered == null || _cylinderCovered.Length < _slots.Count )
			_cylinderCovered = new bool[ Mathf.Max( 32, _slots.Count ) ];
	}

	void EnsureCollider()
	{
		if ( _capsule == null )
			_capsule = GetComponent<CapsuleCollider>();
		if ( _capsule == null )
			_capsule = gameObject.AddComponent<CapsuleCollider>();

		_capsule.direction = 1;
		_capsule.isTrigger = false;
	}

	void RefreshCollider()
	{
		EnsureCollider();
		if ( _capsule == null )
			return;

		float height = Mathf.Max( TreasureStackSpacing.FallbackStep, TotalHeight );
		float diameter = ResolveDiameter();
		float radius = diameter * 0.5f;

		_capsule.enabled = Count > 0;
		_capsule.radius = radius;
		// Extend by radius past top and bottom so the cylindrical body (not the
		// hemispherical caps) spans the full coin stack for reliable selection.
		_capsule.height = Mathf.Max( height + radius * 2f, radius * 2f );
		// Pivot is contact (bottom); center stays on the coin mid-height.
		_capsule.center = new Vector3( 0f, height * 0.5f, 0f );
		ApplyPlayerCollisionLayer();
	}

	void ApplyPlayerCollisionLayer()
	{
		int minCoins = MinCoinsForPlayerCollision;
		bool collide = !ExcludedFromWorldJoin && minCoins > 0 && Count >= minCoins;
		PhysicsLayers.SetRootCollidesWithPlayer( gameObject, collide );
	}

	float ResolveDiameter()
	{
		TreasureDefinition def = null;
		if ( _slots.Count > 0 )
			def = _slots[ 0 ];

		if ( def == null )
			return 0.2f;

		float x = Mathf.Abs( def.worldScale.x );
		float z = Mathf.Abs( def.worldScale.z );
		float d = Mathf.Max( x, z );
		return d > 0.0001f ? d : 0.2f;
	}

	float ResolveMergeRadius()
	{
		return Mathf.Max( DefaultJoinRadius, ResolveDiameter() * 1.25f );
	}

	float GetOffsetForIndex( int index )
	{
		float height = 0f;
		for ( int i = 0; i < index && i < _slots.Count; i++ )
			height += TreasureStackSpacing.GetStep( _slots[ i ] );
		return height;
	}

	float MeasureSlotsHeight( int count )
	{
		float height = 0f;
		int n = Mathf.Min( count, _slots.Count );
		for ( int i = 0; i < n; i++ )
			height += TreasureStackSpacing.GetStep( _slots[ i ] );
		return height;
	}

	public Vector3 GetSlotLocalPosition( int index )
	{
		float y = GetOffsetForIndex( index );
		Vector2 xz = ResolveSlotLocalXz( index );
		return new Vector3( xz.x, y, xz.y );
	}

	public Vector3 GetSlotWorldPosition( int index )
	{
		return transform.TransformPoint( GetSlotLocalPosition( index ) );
	}

	Vector2 ResolveSlotLocalXz( int index )
	{
		if ( !_preferImperfectLod )
			return Vector2.zero;

		CoinStackVisualDefinition def = null;
		def = RuntimeDefinition.Resolve( ref def );
		if ( !CoinStackImperfectLayout.IsImperfectEnabled( def ) )
			return Vector2.zero;

		int layoutCount = Mathf.Max( _slots.Count, index + 1 );
		if ( !CoinStackImperfectLayout.TryGetLocalXz(
			def,
			VariationSeed,
			layoutCount,
			index,
			_slots,
			out Vector2 xz ) )
		{
			return Vector2.zero;
		}

		def.GetMeshReferenceSize( out float refDiameter, out _ );
		refDiameter = Mathf.Max( 0.0001f, refDiameter );
		float scaleXZ = ( ResolveDiameter() * def.diameterScale ) / refDiameter;
		return xz * scaleXZ;
	}

	public bool TryGetHomogeneousDefinition( out TreasureDefinition definition )
	{
		definition = null;
		if ( _slots.Count == 0 )
			return false;

		definition = _slots[ 0 ];
		for ( int i = 1; i < _slots.Count; i++ )
		{
			if ( !SameType( definition, _slots[ i ] ) )
			{
				definition = null;
				return false;
			}
		}

		return definition != null;
	}

	static bool SameType( TreasureDefinition a, TreasureDefinition b )
	{
		if ( a == b )
			return true;
		if ( a == null || b == null )
			return false;
		if ( !string.IsNullOrEmpty( a.id ) && a.id == b.id )
			return true;
		return false;
	}

	void ClearAllVisuals()
	{
		for ( int i = 0; i < _settledLive.Count; i++ )
		{
			if ( _settledLive[ i ] != null )
				TreasureItemFactory.Despawn( _settledLive[ i ] );
			_settledLive[ i ] = null;
		}

		CoinColumnCylinderBinder.ClearAndDestroy( ref _cylinderVisual, null );
	}

	void NotifyCartHostCargoPlaced()
	{
		if ( !_cartHosted || _cartHost == null )
			return;

		_cartHost.NotifyCargoPlaced();
	}

	void NotifyCartHostDestroyed()
	{
		if ( _cartHost == null )
			return;

		MinecartInteractable cart = _cartHost;
		_cartHost = null;
		cart.NotifyHostedCoinStackDestroyed( this );
	}

	/// <summary>
	/// Releases every logical coin as a world <see cref="TreasureItem"/> and destroys this stack.
	/// Used by minecart unload. Does not reclaim in-flight coins as physics drops.
	/// </summary>
	public void ExtractAllAsWorldItems( List<TreasureItem> into )
	{
		if ( into == null || _destroying )
			return;

		_destroying = true;
		_cartHost = null;

		bool[] taken = null;
		if ( _slots.Count > 0 )
			taken = new bool[ _slots.Count ];

		for ( int i = 0; i < _inFlight.Count; i++ )
		{
			TreasureItem flight = _inFlight[ i ];
			int slotIndex = i < _inFlightSlotIndices.Count ? _inFlightSlotIndices[ i ] : -1;
			if ( flight == null )
				continue;

			flight.EndFlight();
			flight.transform.SetParent( null, true );
			into.Add( flight );
			if ( taken != null && slotIndex >= 0 && slotIndex < taken.Length )
				taken[ slotIndex ] = true;
		}

		_inFlight.Clear();
		_inFlightSlotIndices.Clear();

		for ( int i = 0; i < _slots.Count; i++ )
		{
			if ( taken != null && taken[ i ] )
				continue;

			TreasureItem live = i < _settledLive.Count ? _settledLive[ i ] : null;
			if ( live != null )
			{
				_settledLive[ i ] = null;
				live.transform.SetParent( null, true );
				into.Add( live );
				continue;
			}

			TreasureDefinition def = _slots[ i ];
			if ( def == null )
				continue;

			TreasureItem spawned = TreasureItemFactory.SpawnSync( def, GetSlotWorldPosition( i ), transform.rotation, null );
			if ( spawned != null )
				into.Add( spawned );
		}

		ClearAllVisuals();
		_slots.Clear();
		_settledLive.Clear();
		All.Remove( this );
		Destroy( gameObject );
	}

	void DestroyIfEmpty()
	{
		if ( Count > 0 )
			return;

		if ( _machineBuffer )
		{
			ClearAllVisuals();
			RefreshCollider();
			return;
		}

		DestroyStack();
	}

	void DestroyStack()
	{
		if ( _destroying )
			return;

		_destroying = true;

		List<TreasureItem> reclaim = null;
		if ( _inFlight.Count > 0 )
		{
			reclaim = new List<TreasureItem>( _inFlight.Count );
			for ( int i = 0; i < _inFlight.Count; i++ )
			{
				if ( _inFlight[ i ] != null )
					reclaim.Add( _inFlight[ i ] );
			}
		}

		ClearAllVisuals();
		_slots.Clear();
		_settledLive.Clear();
		_inFlight.Clear();
		_inFlightSlotIndices.Clear();
		All.Remove( this );

		if ( reclaim != null )
		{
			for ( int i = 0; i < reclaim.Count; i++ )
			{
				TreasureItem orphan = reclaim[ i ];
				if ( orphan == null )
					continue;

				orphan.EndFlight();
				Vector3 pos = orphan.transform.position;
				Quaternion rot = TreasureOrientation.FlattenUpright( orphan.transform.rotation );
				orphan.EnterSettledPhysics( pos, rot );
			}
		}

		Destroy( gameObject );
	}
}
