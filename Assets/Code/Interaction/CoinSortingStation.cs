using System;
using System.Collections;
using System.Collections.Generic;

using FeedbackSystem;

using UnityEngine;

/// <summary>
/// World coin sorter: hopper stack intake → timed process (bottom-first) → per-type output stacks.
/// Levels via <see cref="UpgradeSystem"/> / <see cref="CoinSortingStationDefinition"/>.
/// </summary>
public class CoinSortingStation : MonoBehaviour
{
	[Serializable]
	public struct ChuteBinding
	{
		public TreasureDefinition coin;
		public Transform chute;
	}

	static readonly List<CoinSortingStation> All = new List<CoinSortingStation>( 8 );
	static readonly List<TreasureDefinition> ConsumeScratch = new List<TreasureDefinition>( 64 );
	static readonly List<TreasureItem> CarryDumpScratch = new List<TreasureItem>( 64 );
	static readonly List<Collider> ColliderScratch = new List<Collider>( 16 );
	static readonly Collider[] OverlapScratch = new Collider[ 64 ];
	static readonly RaycastHit[] FloorHits = new RaycastHit[ 16 ];

	[SerializeField]
	CoinSortingHopper hopper;

	[SerializeField]
	CoinSortingCrankInteractable crank;

	[SerializeField]
	CoinSortingStationMoveInteractable moveInteractable;

	[SerializeField]
	List<ChuteBinding> chutes = new List<ChuteBinding>();

	[Tooltip( "World pose coins leave from when sorted. Null uses a child named Output, then the body face toward the chutes." )]
	[SerializeField]
	Transform sortedOutput;

	[Tooltip( "Optional override; null resolves CoinSortingStationDefinition via Addressables." )]
	[SerializeField]
	CoinSortingStationDefinition definitionOverride;

	[Tooltip( "World offset from hopper collider top for the intake stack contact." )]
	[SerializeField]
	Vector3 hopperStackOffset = new Vector3( 0f, 0.02f, 0f );

	[Tooltip( "Played after the sorter settles into a placed pose." )]
	[SerializeField]
	Feedbacks onPlacedFeedback;

	[Tooltip( "Played each time a coin is sorted, including automatic processing." )]
	[SerializeField]
	Feedbacks onSortedFeedback;

	[SerializeField]
	CoinSortingEnergyGauge energyGauge;

	[SerializeField]
	CoinSortingCrankSpin crankSpin;

	[Tooltip( "Played on each crank click (quarter-turn while holding)." )]
	[SerializeField]
	Feedbacks onCrankClickFeedback;

	[Tooltip( "Played once when the energy gauge hits full." )]
	[SerializeField]
	Feedbacks onGaugeFullFeedback;

	[Tooltip( "Looped rumble on the sorter body while coins are processing." )]
	[SerializeField]
	Feedbacks onSortingShakeFeedback;

	readonly Dictionary<TreasureDefinition, GroundCoinStack> _activeOutputByType =
		new Dictionary<TreasureDefinition, GroundCoinStack>();
	readonly Dictionary<TreasureDefinition, int> _fullStackIndexByType =
		new Dictionary<TreasureDefinition, int>();
	readonly Dictionary<TreasureDefinition, int> _pendingOutputByType =
		new Dictionary<TreasureDefinition, int>();
	readonly HashSet<TreasureDefinition> _missingChuteLogged = new HashSet<TreasureDefinition>();
	static readonly List<GroundCoinStack> BindStackScratch = new List<GroundCoinStack>( 16 );

	CoinSortingStationDefinition _definition;
	GroundCoinStack _hopperStack;
	Rigidbody _body;
	float _processAccumulator;
	float _crankActiveUntil;
	float _reserveSeconds;
	bool _reserveWasFull;
	bool _defaultLevelEnsured;
	bool _isRepositioning;
	bool _loggedMissingGauge;
	bool _loggedMissingCrankSpin;
	bool _sortingShakeActive;

	public static IReadOnlyList<CoinSortingStation> ActiveStations => All;

	public int BufferedCount => _hopperStack != null ? _hopperStack.Count : 0;

	public int StationLevel => ResolveStationLevel();

	public bool IsRepositioning => _isRepositioning;

	public bool RepositionEnabled
	{
		get
		{
			CoinSortingStationDefinition def = ResolveDefinition();
			return def != null && def.repositionEnabled;
		}
	}

	public CoinSortingStationDefinition Definition => ResolveDefinition();

	public Rigidbody Body => EnsureBody();

	public CoinSortingStationMoveInteractable MoveInteractable => moveInteractable;

	public int HopperCapacity
	{
		get
		{
			CoinSortingStationDefinition def = ResolveDefinition();
			if ( def == null )
				return 50;
			return def.ResolveHopperCapacity( StationLevel );
		}
	}

	public bool IsHopperFull => BufferedCount >= HopperCapacity;

	public bool HasRoomFor( int count )
	{
		if ( count <= 0 )
			return true;
		return BufferedCount + count <= HopperCapacity;
	}

	public int RemainingCapacity => Mathf.Max( 0, HopperCapacity - BufferedCount );

	public bool IsCrankActive => Time.time <= _crankActiveUntil;

	public float ReserveSeconds => _reserveSeconds;

	public float ReserveNormalized
	{
		get
		{
			CoinSortingStationDefinition def = ResolveDefinition();
			float max = def != null ? def.ResolveMaxReserveSeconds() : 8f;
			if ( max <= 0.0001f )
				return 0f;
			return Mathf.Clamp01( _reserveSeconds / max );
		}
	}

	public bool IsCranking
	{
		get
		{
			CoinSortingStationDefinition def = ResolveDefinition();
			return def != null && def.RequiresCrank( StationLevel ) && IsCrankActive;
		}
	}

	public bool IsReserveDischarging
	{
		get
		{
			return !IsCranking && _reserveSeconds > 0.0001f && BufferedCount > 0 && RequiresManualCrank();
		}
	}

	public bool IsProcessing
	{
		get
		{
			int level = StationLevel;
			if ( level < 1 || BufferedCount <= 0 )
				return false;

			CoinSortingStationDefinition def = ResolveDefinition();
			if ( def == null )
				return false;

			if ( def.IsAutomatic( level ) )
				return true;

			return def.RequiresCrank( level ) && _reserveSeconds > 0.0001f;
		}
	}

	public CoinSortingHopper Hopper => hopper;

	public CoinSortingCrankInteractable Crank => crank;

	public GroundCoinStack HopperStack => EnsureHopperStack();

	void OnEnable()
	{
		if ( !All.Contains( this ) )
			All.Add( this );
	}

	void OnDisable()
	{
		StopSortingShake();
		All.Remove( this );
	}

	void Awake()
	{
		ResolveDefinition();
		EnsureChildRefs();
		EnsureBodyPlacementCollider();
		EnsureBody();
		StripNestedRigidbodies();
		EnsureSettleFeedback();
		EnsureSortedFeedback();
		EnsureCrankAudio();
		BindVisuals();
		if ( hopper != null )
			hopper.BindStation( this );
		if ( crank != null )
			crank.BindStation( this );
		if ( moveInteractable != null )
			moveInteractable.BindStation( this );
		EnsureSortedOutput();
	}

	void Start()
	{
		EnsureDefaultUpgradeLevel();
		EnsureHopperStack();
		BindOutputStacksInBounds();
	}

	void Update()
	{
		EnsureDefaultUpgradeLevel();
		SyncHopperStackCapacity();
		if ( !_isRepositioning )
		{
			TickReserve( Time.deltaTime );
			TickProcess( Time.deltaTime );
		}

		TickSortingShake();
	}

	void EnsureChildRefs()
	{
		if ( hopper == null )
			hopper = GetComponentInChildren<CoinSortingHopper>( true );
		if ( crank == null )
			crank = GetComponentInChildren<CoinSortingCrankInteractable>( true );
		if ( energyGauge == null )
			energyGauge = GetComponentInChildren<CoinSortingEnergyGauge>( true );
		if ( crankSpin == null )
			crankSpin = GetComponentInChildren<CoinSortingCrankSpin>( true );
		if ( moveInteractable == null )
			moveInteractable = GetComponentInChildren<CoinSortingStationMoveInteractable>( true );
		if ( moveInteractable == null )
		{
			Transform body = transform.Find( "Body" );
			GameObject host = body != null ? body.gameObject : gameObject;
			moveInteractable = host.GetComponent<CoinSortingStationMoveInteractable>();
			if ( moveInteractable == null )
				moveInteractable = host.AddComponent<CoinSortingStationMoveInteractable>();
		}

		EnsureSortedOutput();
	}

	Rigidbody EnsureBody()
	{
		if ( _body != null )
			return _body;

		_body = GetComponent<Rigidbody>();
		if ( _body == null )
			_body = gameObject.AddComponent<Rigidbody>();

		_body.isKinematic = true;
		_body.useGravity = false;
		_body.interpolation = RigidbodyInterpolation.Interpolate;
		_body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
		return _body;
	}

	void StripNestedRigidbodies()
	{
		Rigidbody[] bodies = GetComponentsInChildren<Rigidbody>( true );
		for ( int i = 0; i < bodies.Length; i++ )
		{
			Rigidbody nested = bodies[ i ];
			if ( nested == null || nested == _body || nested.gameObject == gameObject )
				continue;
			Destroy( nested );
		}
	}

	void EnsureSettleFeedback()
	{
		if ( onPlacedFeedback != null )
			return;

		onPlacedFeedback = GetComponent<Feedbacks>();
		if ( onPlacedFeedback == null )
			onPlacedFeedback = gameObject.AddComponent<Feedbacks>();

		if ( onPlacedFeedback.FeedbackList != null && onPlacedFeedback.FeedbackList.Count > 0 )
			return;

		onPlacedFeedback.AddFeedback( new PunchScaleFeedback
		{
			Target = transform,
			Punch = new Vector3( 0.06f, -0.08f, 0.06f ),
			Duration = 0.22f
		} );
		onPlacedFeedback.AddFeedback( new ShakeTransformFeedback
		{
			Target = transform,
			Duration = 0.18f,
			Strength = 0.035f
		} );
	}

	void EnsureSortedFeedback()
	{
		if ( onSortedFeedback == null )
		{
			Transform existing = transform.Find( "OnSortedFeedbacks" );
			GameObject host = existing != null ? existing.gameObject : new GameObject( "OnSortedFeedbacks" );
			if ( existing == null )
				host.transform.SetParent( transform, false );

			onSortedFeedback = host.GetComponent<Feedbacks>();
			if ( onSortedFeedback == null )
				onSortedFeedback = host.AddComponent<Feedbacks>();
		}

		onSortedFeedback.Initialize();
		if ( onSortedFeedback.FeedbackList != null && onSortedFeedback.FeedbackList.Count > 0 )
			return;

		Transform crankTarget = crank != null ? crank.transform : transform;
		onSortedFeedback.AddFeedback( new PunchRotationFeedback
		{
			Target = crankTarget,
			Punch = new Vector3( 80f, 0f, 0f ),
			Duration = 0.16f
		} );
		onSortedFeedback.AddFeedback( new PunchScaleFeedback
		{
			Target = crankTarget,
			Punch = new Vector3( 0.08f, 0.08f, 0.08f ),
			Duration = 0.16f
		} );
	}

	void EnsureCrankAudio()
	{
		if ( GetComponent<CoinSortingCrankAudio>() == null )
			gameObject.AddComponent<CoinSortingCrankAudio>();
	}

	void BindVisuals()
	{
		if ( energyGauge != null )
			energyGauge.BindStation( this );
		else if ( !_loggedMissingGauge )
		{
			_loggedMissingGauge = true;
			Debug.LogWarning( $"CoinSortingStation '{name}': EnergyGauge is missing on the prefab.", this );
		}

		if ( crankSpin != null )
			crankSpin.BindStation( this );
		else if ( !_loggedMissingCrankSpin )
		{
			_loggedMissingCrankSpin = true;
			Debug.LogWarning( $"CoinSortingStation '{name}': CrankSpin is missing on the prefab.", this );
		}

		if ( onCrankClickFeedback != null )
			onCrankClickFeedback.Initialize();
		if ( onGaugeFullFeedback != null )
			onGaugeFullFeedback.Initialize();
		if ( onSortingShakeFeedback != null )
			onSortingShakeFeedback.Initialize();
	}

	bool RequiresManualCrank()
	{
		CoinSortingStationDefinition def = ResolveDefinition();
		return def != null && def.RequiresCrank( StationLevel );
	}

	public void BeginRepositioning()
	{
		if ( !RepositionEnabled )
			return;

		_isRepositioning = true;
		StopSortingShake();
		EnsureBody();
	}

	public void EndRepositioning()
	{
		_isRepositioning = false;
	}

	/// <summary>
	/// Clears chute output bookkeeping so new emits create stacks at the current chute poses.
	/// Existing world stacks are left where they are.
	/// </summary>
	public void ClearOutputStackBindings()
	{
		_activeOutputByType.Clear();
		_fullStackIndexByType.Clear();
		_pendingOutputByType.Clear();
	}

	/// <summary>
	/// Bind homogeneous world stacks inside the station's physical bounds as chute outputs.
	/// Matching stacks of the same type merge into one bound stack.
	/// </summary>
	public void BindOutputStacksInBounds()
	{
		Bounds bounds = GetCombinedPhysicalBounds();
		Vector3 center = bounds.center;
		Vector3 extents = bounds.extents + new Vector3( 0.05f, 0.15f, 0.05f );
		int hits = Physics.OverlapBoxNonAlloc(
			center,
			extents,
			OverlapScratch,
			transform.rotation,
			Physics.DefaultRaycastLayers,
			QueryTriggerInteraction.Ignore );

		BindStackScratch.Clear();
		for ( int i = 0; i < hits; i++ )
		{
			Collider col = OverlapScratch[ i ];
			if ( col == null )
				continue;

			GroundCoinStack stack = col.GetComponentInParent<GroundCoinStack>();
			if ( stack == null || IsHopperStack( stack ) || stack.IsMachineBuffer || stack.Count <= 0 )
				continue;
			if ( !stack.TryGetHomogeneousDefinition( out TreasureDefinition homo ) || homo == null )
				continue;
			if ( FindChute( homo ) == null )
				continue;

			if ( !BindStackScratch.Contains( stack ) )
				BindStackScratch.Add( stack );
		}

		for ( int i = 0; i < BindStackScratch.Count; i++ )
		{
			GroundCoinStack stack = BindStackScratch[ i ];
			if ( stack == null || stack.Count <= 0 )
				continue;
			if ( !stack.TryGetHomogeneousDefinition( out TreasureDefinition homo ) || homo == null )
				continue;

			TreasureDefinition key = ResolveOutputKey( homo );
			if ( key == null )
				continue;

			if ( _activeOutputByType.TryGetValue( key, out GroundCoinStack existing )
				&& existing != null
				&& existing != stack
				&& existing.Count > 0 )
			{
				MergeBoundStacks( existing, stack );
				continue;
			}

			_activeOutputByType[ key ] = stack;
		}
	}

	TreasureDefinition ResolveOutputKey( TreasureDefinition definition )
	{
		if ( definition == null || chutes == null )
			return null;

		for ( int i = 0; i < chutes.Count; i++ )
		{
			ChuteBinding binding = chutes[ i ];
			if ( binding.coin == null )
				continue;
			if ( SameCoinType( binding.coin, definition ) )
				return binding.coin;
		}

		return definition;
	}

	void MergeBoundStacks( GroundCoinStack survivor, GroundCoinStack other )
	{
		if ( survivor == null || other == null || survivor == other )
			return;

		ConsumeScratch.Clear();
		if ( !other.TryConsumeAllDefinitions( ConsumeScratch ) )
			return;

		survivor.TryAppendDefinitions( ConsumeScratch );
		ConsumeScratch.Clear();
	}

	public void PlayPlacedFeedback()
	{
		EnsureSettleFeedback();
		if ( onPlacedFeedback != null )
			onPlacedFeedback.Play();
	}

	public void PlaySortedFeedback()
	{
		EnsureSortedFeedback();
		if ( onSortedFeedback != null )
			onSortedFeedback.Play();
	}

	public void CollectPhysicalColliders( List<Collider> destination )
	{
		if ( destination == null )
			return;

		destination.Clear();
		Collider[] colliders = GetComponentsInChildren<Collider>( true );
		for ( int i = 0; i < colliders.Length; i++ )
		{
			Collider col = colliders[ i ];
			if ( col == null || !col.enabled || col.isTrigger )
				continue;

			// Hopper buffer / output stacks are Collectable and move with hopper when parented;
			// skip Collectable so we validate machine body only.
			if ( col.gameObject.layer == PhysicsLayers.CollectableLayer )
				continue;

			destination.Add( col );
		}
	}

	public Bounds GetCombinedPhysicalBounds()
	{
		CollectPhysicalColliders( ColliderScratch );
		if ( ColliderScratch.Count == 0 )
			return new Bounds( transform.position, Vector3.one );

		Bounds bounds = ColliderScratch[ 0 ].bounds;
		for ( int i = 1; i < ColliderScratch.Count; i++ )
			bounds.Encapsulate( ColliderScratch[ i ].bounds );
		return bounds;
	}

	public void CollectOutlineRenderers( List<Renderer> destination )
	{
		if ( destination == null )
			return;

		destination.Clear();
		CoinSortingCrankInteractable crankRef = crank;
		Transform crankRoot = crankRef != null ? crankRef.transform : null;

		Renderer[] all = GetComponentsInChildren<Renderer>( true );
		for ( int i = 0; i < all.Length; i++ )
		{
			Renderer renderer = all[ i ];
			if ( renderer == null || !renderer.enabled )
				continue;

			if ( !( renderer is MeshRenderer ) && !( renderer is SkinnedMeshRenderer ) )
				continue;

			if ( renderer.sharedMaterial == null )
				continue;

			if ( renderer.GetComponentInParent<GroundCoinStack>() != null )
				continue;

			if ( renderer.GetComponentInParent<CoinSortingEnergyGauge>() != null )
				continue;

			int collectable = PhysicsLayers.CollectableLayer;
			if ( collectable >= 0 && renderer.gameObject.layer == collectable )
				continue;

			if ( crankRoot != null
				&& ( renderer.transform == crankRoot || renderer.transform.IsChildOf( crankRoot ) ) )
				continue;

			destination.Add( renderer );
		}
	}

	/// <summary>
	/// Validates a candidate root pose: walkable floor, slope, no obstruction overlap,
	/// explicit gold-pile reject, and operator clearance.
	/// </summary>
	public bool EvaluatePlacementPose(
		Vector3 rootPosition,
		Quaternion rootRotation,
		out Vector3 snappedPosition,
		out Quaternion snappedRotation,
		out RaycastHit floorHit )
	{
		snappedPosition = rootPosition;
		snappedRotation = rootRotation;
		floorHit = default;

		CoinSortingStationDefinition def = ResolveDefinition();
		float snapDist = def != null ? def.floorSnapDistance : 2.5f;
		float minUpDot = def != null ? def.minFloorUpDot : 0.7f;
		float shrink = def != null ? def.placementBoundsShrink : 0.1f;

		Bounds localBounds = GetLocalPhysicalBounds();
		Vector3 probeOrigin = rootPosition + rootRotation * new Vector3( 0f, localBounds.max.y + 0.05f, 0f );
		float probeLength = snapDist + localBounds.size.y + 0.5f;

		int hitCount = Physics.RaycastNonAlloc(
			probeOrigin,
			Vector3.down,
			FloorHits,
			probeLength,
			Physics.DefaultRaycastLayers,
			QueryTriggerInteraction.Ignore );

		if ( hitCount <= 0 )
			return false;

		bool foundFloor = false;
		float bestDist = float.MaxValue;
		for ( int i = 0; i < hitCount; i++ )
		{
			RaycastHit hit = FloorHits[ i ];
			if ( hit.collider == null )
				continue;
			if ( IsOwnCollider( hit.collider ) )
				continue;

			if ( hit.distance < bestDist )
			{
				bestDist = hit.distance;
				floorHit = hit;
				foundFloor = true;
			}
		}

		if ( !foundFloor )
			return false;

		if ( PlacementFloorSurface.IsHeightfieldPileCollider( floorHit.collider ) )
			return false;

		Vector3 normal = floorHit.normal.sqrMagnitude > 0.0001f
			? floorHit.normal.normalized
			: Vector3.up;
		float upDot = Vector3.Dot( normal, Vector3.up );
		if ( upDot < minUpDot )
			return false;

		if ( !PlacementFloorSurface.IsFloorCollider( floorHit.collider ) )
			return false;

		float bottomLocalY = localBounds.min.y;
		snappedPosition = floorHit.point - rootRotation * new Vector3( 0f, bottomLocalY, 0f );

		Quaternion upright = Quaternion.Euler( 0f, rootRotation.eulerAngles.y, 0f );
		float align = def != null ? def.surfaceAlignStrength : 0.25f;
		if ( align > 0.001f )
		{
			Quaternion fromTo = Quaternion.FromToRotation( Vector3.up, normal );
			Quaternion tilted = fromTo * upright;
			snappedRotation = Quaternion.Slerp( upright, tilted, align );
		}
		else
		{
			snappedRotation = upright;
		}

		if ( FootprintOverlapsGoldPile( snappedPosition, snappedRotation, localBounds, shrink ) )
			return false;

		if ( OverlapsObstruction( snappedPosition, snappedRotation, shrink ) )
			return false;

		if ( !HasOperationalClearance( snappedPosition, snappedRotation, def ) )
			return false;

		return true;
	}

	Bounds GetLocalPhysicalBounds()
	{
		CollectPhysicalColliders( ColliderScratch );
		if ( ColliderScratch.Count == 0 )
			return new Bounds( Vector3.up * 0.6f, new Vector3( 1.6f, 1.2f, 1.2f ) );

		Bounds world = ColliderScratch[ 0 ].bounds;
		for ( int i = 1; i < ColliderScratch.Count; i++ )
			world.Encapsulate( ColliderScratch[ i ].bounds );

		Vector3 localCenter = transform.InverseTransformPoint( world.center );
		Vector3 lossy = transform.lossyScale;
		Vector3 localSize = new Vector3(
			SafeDiv( world.size.x, Mathf.Abs( lossy.x ) ),
			SafeDiv( world.size.y, Mathf.Abs( lossy.y ) ),
			SafeDiv( world.size.z, Mathf.Abs( lossy.z ) ) );
		return new Bounds( localCenter, localSize );
	}

	bool FootprintOverlapsGoldPile(
		Vector3 rootPosition,
		Quaternion rootRotation,
		Bounds localBounds,
		float shrink )
	{
		Vector3 worldCenter = rootPosition + rootRotation * localBounds.center;
		Vector3 halfExtents = Vector3.Scale( localBounds.extents, AbsVec( transform.lossyScale ) );
		halfExtents = ShrinkExtents( halfExtents, shrink );

		int hits = Physics.OverlapBoxNonAlloc(
			worldCenter,
			halfExtents,
			OverlapScratch,
			rootRotation,
			Physics.DefaultRaycastLayers,
			QueryTriggerInteraction.Collide );

		for ( int i = 0; i < hits; i++ )
		{
			Collider other = OverlapScratch[ i ];
			if ( other == null || IsOwnCollider( other ) )
				continue;

			if ( PlacementFloorSurface.IsHeightfieldPileCollider( other ) )
				return true;

			TreasurePileVisual pile = other.GetComponentInParent<TreasurePileVisual>();
			if ( pile != null
				&& ( pile.ContainsWorldPointXZ( worldCenter ) || pile.HasPileSurfaceAt( worldCenter ) ) )
				return true;
		}

		// Corner samples against any nearby heightfield even when physics misses the mound mesh.
		Vector3[] samples =
		{
			localBounds.center,
			new Vector3( localBounds.min.x, localBounds.min.y, localBounds.min.z ),
			new Vector3( localBounds.max.x, localBounds.min.y, localBounds.min.z ),
			new Vector3( localBounds.min.x, localBounds.min.y, localBounds.max.z ),
			new Vector3( localBounds.max.x, localBounds.min.y, localBounds.max.z ),
		};

		for ( int s = 0; s < samples.Length; s++ )
		{
			Vector3 world = rootPosition + rootRotation * samples[ s ];
			if ( SampleHitsGoldPile( world ) )
				return true;
		}

		return false;
	}

	static bool SampleHitsGoldPile( Vector3 world )
	{
		int hits = Physics.OverlapSphereNonAlloc(
			world,
			0.08f,
			OverlapScratch,
			Physics.DefaultRaycastLayers,
			QueryTriggerInteraction.Collide );

		for ( int i = 0; i < hits; i++ )
		{
			Collider other = OverlapScratch[ i ];
			if ( other == null )
				continue;
			if ( PlacementFloorSurface.IsHeightfieldPileCollider( other ) )
				return true;
		}

		return false;
	}

	bool OverlapsObstruction( Vector3 rootPosition, Quaternion rootRotation, float shrink )
	{
		CollectPhysicalColliders( ColliderScratch );
		Matrix4x4 current = Matrix4x4.TRS( transform.position, transform.rotation, Vector3.one );
		Matrix4x4 target = Matrix4x4.TRS( rootPosition, rootRotation, Vector3.one );
		Matrix4x4 delta = target * current.inverse;

		for ( int i = 0; i < ColliderScratch.Count; i++ )
		{
			Collider col = ColliderScratch[ i ];
			if ( col == null )
				continue;

			if ( !TryGetColliderWorldBox( col, delta, out Vector3 center, out Vector3 halfExtents, out Quaternion orientation ) )
				continue;

			halfExtents = ShrinkExtents( halfExtents, shrink );

			int hits = Physics.OverlapBoxNonAlloc(
				center,
				halfExtents,
				OverlapScratch,
				orientation,
				Physics.DefaultRaycastLayers,
				QueryTriggerInteraction.Ignore );

			for ( int h = 0; h < hits; h++ )
			{
				Collider other = OverlapScratch[ h ];
				if ( other == null || IsOwnCollider( other ) )
					continue;
				if ( IsPlayerCollider( other ) )
					continue;
				// Loose coins / stacks shouldn't block machine placement.
				if ( other.GetComponentInParent<GroundCoinStack>() != null
					|| other.GetComponentInParent<TreasureItem>() != null )
					continue;
				return true;
			}
		}

		return false;
	}

	bool HasOperationalClearance(
		Vector3 rootPosition,
		Quaternion rootRotation,
		CoinSortingStationDefinition def )
	{
		Vector3 size = def != null ? def.operationalClearanceSize : new Vector3( 0.85f, 1.6f, 0.6f );
		Vector3 localCenter = def != null ? def.operationalClearanceCenter : new Vector3( 0f, 0.85f, 0.95f );
		float shrink = def != null ? def.placementBoundsShrink : 0.1f;
		Vector3 half = ShrinkExtents( size * 0.5f, shrink * 0.5f );
		Vector3 center = rootPosition + rootRotation * localCenter;

		int hits = Physics.OverlapBoxNonAlloc(
			center,
			half,
			OverlapScratch,
			rootRotation,
			Physics.DefaultRaycastLayers,
			QueryTriggerInteraction.Ignore );

		for ( int i = 0; i < hits; i++ )
		{
			Collider other = OverlapScratch[ i ];
			if ( other == null || IsOwnCollider( other ) )
				continue;
			if ( IsPlayerCollider( other ) )
				continue;
			if ( other.GetComponentInParent<GroundCoinStack>() != null
				|| other.GetComponentInParent<TreasureItem>() != null )
				continue;
			return false;
		}

		return true;
	}

	static Vector3 ShrinkExtents( Vector3 halfExtents, float shrink )
	{
		if ( shrink <= 0f )
			return halfExtents;

		return new Vector3(
			Mathf.Max( 0.05f, halfExtents.x - shrink ),
			Mathf.Max( 0.05f, halfExtents.y - shrink ),
			Mathf.Max( 0.05f, halfExtents.z - shrink ) );
	}

	static bool TryGetColliderWorldBox(
		Collider col,
		Matrix4x4 worldDelta,
		out Vector3 center,
		out Vector3 halfExtents,
		out Quaternion orientation )
	{
		center = Vector3.zero;
		halfExtents = Vector3.one * 0.5f;
		orientation = Quaternion.identity;

		BoxCollider box = col as BoxCollider;
		if ( box != null )
		{
			Transform t = box.transform;
			Vector3 worldCenter = t.TransformPoint( box.center );
			center = worldDelta.MultiplyPoint3x4( worldCenter );
			Vector3 lossy = t.lossyScale;
			halfExtents = Vector3.Scale( box.size, AbsVec( lossy ) ) * 0.5f;
			orientation = worldDelta.rotation * t.rotation;
			return true;
		}

		SphereCollider sphere = col as SphereCollider;
		if ( sphere != null )
		{
			Transform t = sphere.transform;
			Vector3 worldCenter = t.TransformPoint( sphere.center );
			center = worldDelta.MultiplyPoint3x4( worldCenter );
			float maxScale = Mathf.Max( Mathf.Abs( t.lossyScale.x ), Mathf.Abs( t.lossyScale.y ), Mathf.Abs( t.lossyScale.z ) );
			halfExtents = Vector3.one * ( sphere.radius * maxScale );
			orientation = Quaternion.identity;
			return true;
		}

		Bounds b = col.bounds;
		center = worldDelta.MultiplyPoint3x4( b.center );
		halfExtents = b.extents;
		orientation = worldDelta.rotation;
		return true;
	}

	public bool IsOwnCollider( Collider collider )
	{
		if ( collider == null )
			return false;
		return collider.transform == transform || collider.transform.IsChildOf( transform );
	}

	static bool IsPlayerCollider( Collider collider )
	{
		if ( collider == null )
			return false;
		int playerLayer = PhysicsLayers.PlayerLayer;
		if ( playerLayer >= 0 && collider.gameObject.layer == playerLayer )
			return true;
		return collider.GetComponentInParent<PlayerController>() != null
			|| collider.GetComponentInParent<CharacterController>() != null;
	}

	static Vector3 AbsVec( Vector3 v )
	{
		return new Vector3( Mathf.Abs( v.x ), Mathf.Abs( v.y ), Mathf.Abs( v.z ) );
	}

	static float SafeDiv( float a, float b )
	{
		return Mathf.Abs( b ) < 0.0001f ? a : a / b;
	}

	/// <summary>
	/// Body visuals often have no collider (setup strips the primitive one). Restore a raycast
	/// collider so aiming anywhere on the station (except the crank) can deposit into the hopper.
	/// </summary>
	void EnsureBodyPlacementCollider()
	{
		Transform body = transform.Find( "Body" );
		if ( body == null )
			return;

		if ( body.GetComponent<Collider>() != null )
			return;

		BoxCollider box = body.gameObject.AddComponent<BoxCollider>();
		box.isTrigger = false;

		Renderer renderer = body.GetComponent<Renderer>();
		if ( renderer != null )
		{
			Bounds local = renderer.localBounds;
			box.center = local.center;
			box.size = local.size;
		}
		else
		{
			box.center = Vector3.zero;
			box.size = Vector3.one;
		}
	}

	/// <summary>
	/// Maps a ray hit on this station to the hopper placement target, ignoring the crank.
	/// </summary>
	public static CoinSortingHopper ResolveHopperPlacementFromCollider( Collider collider )
	{
		if ( collider == null )
			return null;

		if ( collider.GetComponentInParent<CoinSortingCrankInteractable>() != null )
			return null;

		CoinSortingStation station = collider.GetComponentInParent<CoinSortingStation>();
		if ( station == null )
			return null;

		return station.hopper != null ? station.hopper : station.GetComponentInChildren<CoinSortingHopper>( true );
	}

	CoinSortingStationDefinition ResolveDefinition()
	{
		if ( definitionOverride != null )
		{
			_definition = definitionOverride;
			return _definition;
		}

		return RuntimeDefinition.Resolve( ref _definition );
	}

	void EnsureDefaultUpgradeLevel()
	{
		if ( _defaultLevelEnsured )
			return;

		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;
		if ( player == null )
			return;

		UpgradeSystem system = UpgradeSystem.Ensure( player );
		if ( system == null )
			return;

		CoinSortingStationDefinition def = ResolveDefinition();
		string id = def != null
			? def.ResolveUpgradeId()
			: CoinSortingStationDefinition.DefaultUpgradeId;

		if ( !system.TryGetDefinition( id, out _ ) )
		{
			_defaultLevelEnsured = true;
			return;
		}

		if ( !system.IsUnlocked( id ) || system.GetUpgradeLevel( id ) < 1 )
			system.SetUpgradeLevel( id, 1 );

		_defaultLevelEnsured = true;
	}

	int ResolveStationLevel()
	{
		EnsureDefaultUpgradeLevel();
		UpgradeSystem system = UpgradeSystem.Instance;
		if ( system == null )
			return 1;

		CoinSortingStationDefinition def = ResolveDefinition();
		string id = def != null
			? def.ResolveUpgradeId()
			: CoinSortingStationDefinition.DefaultUpgradeId;

		int level = system.GetUpgradeLevel( id );
		return level < 1 ? 1 : level;
	}

	public void NotifyCrankPulse()
	{
		if ( _isRepositioning )
			return;

		CoinSortingStationDefinition def = ResolveDefinition();
		float grace = def != null ? def.crankHoldGrace : 0.2f;
		bool wasCranking = IsCranking;
		_crankActiveUntil = Time.time + Mathf.Max( 0.05f, grace );

		if ( !wasCranking && crankSpin != null )
			crankSpin.NotifyChargeStarted();

		if ( !wasCranking )
			NotifyCrankClick();
	}

	public void NotifyCrankClick()
	{
		if ( _isRepositioning || !RequiresManualCrank() )
			return;

		CoinSortingCrankAudio crankAudio = GetComponent<CoinSortingCrankAudio>();
		if ( crankAudio != null )
			crankAudio.NotifyPulse();

		if ( onCrankClickFeedback != null )
			onCrankClickFeedback.Play();

		if ( energyGauge != null )
			energyGauge.PlayChargePulse();
	}

	GroundCoinStack EnsureHopperStack()
	{
		if ( _hopperStack != null )
			return _hopperStack;

		Transform anchor = hopper != null ? hopper.transform : transform;
		Vector3 contact = ResolveHopperStackContact();
		Quaternion rot = TreasureOrientation.FlattenUpright( anchor.rotation );
		_hopperStack = GroundCoinStack.CreateAt( contact, rot );
		_hopperStack.name = "HopperCoinStack";
		_hopperStack.transform.SetParent( anchor, true );
		_hopperStack.ConfigureAsMachineBuffer( HopperCapacity, this );
		return _hopperStack;
	}

	Vector3 ResolveHopperStackContact()
	{
		if ( hopper != null )
		{
			Collider col = hopper.GetComponent<Collider>();
			if ( col != null )
			{
				Bounds bounds = col.bounds;
				return new Vector3( bounds.center.x, bounds.max.y, bounds.center.z ) + hopperStackOffset;
			}

			return hopper.transform.position + hopperStackOffset;
		}

		return transform.position + Vector3.up + hopperStackOffset;
	}

	void SyncHopperStackCapacity()
	{
		if ( _hopperStack == null )
			return;
		_hopperStack.SetMaxCountOverride( HopperCapacity );
	}

	public bool IsHopperStack( GroundCoinStack stack )
	{
		return stack != null && stack == _hopperStack;
	}

	/// <summary>Enqueue a single coin definition if capacity allows (appends onto hopper stack).</summary>
	public bool TryEnqueue( TreasureDefinition definition )
	{
		if ( !GroundCoinStack.IsGroundStackableCoin( definition ) )
			return false;
		if ( IsHopperFull )
			return false;

		GroundCoinStack stack = EnsureHopperStack();
		return stack.TryAppendDefinition( definition );
	}

	/// <summary>Enqueue as many defs as capacity allows; returns number accepted.</summary>
	public int TryEnqueueRange( IReadOnlyList<TreasureDefinition> definitions )
	{
		if ( definitions == null || definitions.Count == 0 )
			return 0;

		int accepted = 0;
		for ( int i = 0; i < definitions.Count; i++ )
		{
			if ( !TryEnqueue( definitions[ i ] ) )
				break;
			accepted++;
		}

		return accepted;
	}

	/// <summary>
	/// Dump as many stackable coins from <paramref name="carry"/> as hopper capacity allows.
	/// Uses logical coin definitions (Active + left held), not only live meshes.
	/// </summary>
	public bool TryDumpCarryIntoHopper( PlayerCarry carry )
	{
		return TryDumpCarryIntoHopperAnimated( carry );
	}

	/// <summary>
	/// Consumes fitting coin defs from carry, flies the stack cylinder into the hopper, then appends.
	/// </summary>
	public bool TryDumpCarryIntoHopperAnimated( PlayerCarry carry )
	{
		if ( _isRepositioning )
			return false;
		if ( carry == null || carry.GetBucketCount( CarryBucketKind.Coin ) <= 0 )
			return false;

		int room = RemainingCapacity;
		if ( room <= 0 )
			return false;

		Vector3 startPos = carry.transform.position;
		Quaternion startRot = carry.transform.rotation;
		float seed = carry.CoinHandVariationSeed;
		Transform hold = carry.GetHoldRoot( CarryBucketKind.Coin );
		if ( hold != null )
		{
			startPos = hold.position;
			startRot = hold.rotation;
		}

		ConsumeScratch.Clear();
		int taken = carry.TryConsumeCoinDefinitions( room, ConsumeScratch );
		if ( taken <= 0 )
		{
			ConsumeScratch.Clear();
			return false;
		}

		List<TreasureDefinition> flying = new List<TreasureDefinition>( ConsumeScratch.Count );
		for ( int i = 0; i < ConsumeScratch.Count; i++ )
			flying.Add( ConsumeScratch[ i ] );
		ConsumeScratch.Clear();

		GroundCoinStack stack = EnsureHopperStack();
		Vector3 endPos = stack.ContactPosition + Vector3.up * stack.SettledHeight;
		Quaternion endRot = stack.transform.rotation;
		CarryDefinition carryDef = null;
		carryDef = RuntimeDefinition.Resolve( ref carryDef );
		float duration = carryDef != null ? carryDef.wholeStackAbsorbTweenDuration : 0.28f;

		CoinStackFlight.FlyToWorld(
			flying,
			startPos,
			startRot,
			seed,
			endPos,
			endRot,
			duration,
			() =>
			{
				if ( stack == null )
					return;

				stack.TryAppendDefinitions( flying );
				PlayHopperCoinPlaceFeedback( stack, flying );
				CoinStackInteractSfx.PlayStackPlace( endPos );
			} );

		if ( taken >= 2 )
		{
			EventBus.Publish( new CoinSorterStackLoadedEvent
			{
				Station = this,
				CoinCount = taken
			} );
		}

		return true;
	}

	static void PlayHopperCoinPlaceFeedback( GroundCoinStack stack, List<TreasureDefinition> definitions )
	{
		if ( stack == null || definitions == null || definitions.Count == 0 )
			return;

		TreasureDefinition last = definitions[ definitions.Count - 1 ];
		if ( last == null )
			return;

		Vector3 pos = stack.ContactPosition + Vector3.up * stack.SettledHeight;
		Quaternion rot = stack.transform.rotation;
		TreasureItem fx = TreasureItemFactory.RentVisualCoin( last, pos, rot );
		if ( fx == null )
			return;

		fx.SetMeshVisible( true );
		CoinGemInteractFeedback.PlayPlace( fx );
		TreasureMotionHost.Run( ReturnHopperPlaceFx( fx ) );
	}

	static System.Collections.IEnumerator ReturnHopperPlaceFx( TreasureItem fx )
	{
		yield return new WaitForSeconds( 0.22f );
		if ( fx != null )
			TreasureItemFactory.ReturnVisualCoin( fx );
	}

	public bool TryAbsorbLooseCoin( TreasureItem item )
	{
		if ( item == null || !GroundCoinStack.IsGroundStackableCoin( item ) )
			return false;
		if ( !item.IsWorldLoose || item.IsInFlight )
			return false;
		if ( IsHopperFull )
			return false;

		GroundCoinStack stack = EnsureHopperStack();
		return stack.TryAbsorbLooseImmediate( item );
	}

	public bool TryAbsorbStack( GroundCoinStack stack )
	{
		if ( stack == null || IsHopperStack( stack ) || IsHopperFull )
			return false;

		ConsumeScratch.Clear();
		if ( !stack.TryConsumeAllDefinitions( ConsumeScratch ) )
		{
			ConsumeScratch.Clear();
			return false;
		}

		int accepted = TryEnqueueRange( ConsumeScratch );
		ConsumeScratch.Clear();
		if ( accepted >= 2 )
		{
			EventBus.Publish( new CoinSorterStackLoadedEvent
			{
				Station = this,
				CoinCount = accepted
			} );
		}
		return accepted > 0;
	}

	/// <summary>
	/// Absorbs a stack only when the entire slot count fits; otherwise leaves the stack alone.
	/// </summary>
	public bool TryAbsorbStackIfFits( GroundCoinStack stack )
	{
		if ( stack == null || stack.Count <= 0 || IsHopperStack( stack ) )
			return false;
		if ( !HasRoomFor( stack.Count ) )
			return false;
		return TryAbsorbStack( stack );
	}

	void TickReserve( float dt )
	{
		if ( dt <= 0f )
			return;

		CoinSortingStationDefinition def = ResolveDefinition();
		if ( def == null || !def.RequiresCrank( StationLevel ) )
		{
			_reserveSeconds = 0f;
			_reserveWasFull = false;
			return;
		}

		float max = def.ResolveMaxReserveSeconds();
		if ( IsCranking )
		{
			float multiplier = Mathf.Max( 0.01f, def.crankToSortMultiplier );
			_reserveSeconds = Mathf.Min( max, _reserveSeconds + dt * multiplier );
			bool nowFull = _reserveSeconds >= max - 0.0001f;
			if ( nowFull && !_reserveWasFull )
				PlayGaugeFullFeedback();
			_reserveWasFull = nowFull;
		}
		else if ( _reserveSeconds < max * 0.98f )
		{
			_reserveWasFull = false;
		}

		if ( IsProcessing )
			_reserveSeconds = Mathf.Max( 0f, _reserveSeconds - dt );
	}

	void TickSortingShake()
	{
		bool want = !_isRepositioning && IsProcessing;
		if ( want == _sortingShakeActive )
			return;

		if ( want )
			StartSortingShake();
		else
			StopSortingShake();
	}

	void StartSortingShake()
	{
		_sortingShakeActive = true;
		if ( onSortingShakeFeedback != null )
			onSortingShakeFeedback.Play();
	}

	void StopSortingShake()
	{
		_sortingShakeActive = false;
		if ( onSortingShakeFeedback != null )
			onSortingShakeFeedback.Stop();
	}

	void PlayGaugeFullFeedback()
	{
		if ( onGaugeFullFeedback != null )
			onGaugeFullFeedback.Play();
		if ( energyGauge != null )
			energyGauge.PlayFullPop();
	}

	void TickProcess( float dt )
	{
		if ( dt <= 0f || !IsProcessing )
		{
			if ( !IsProcessing )
				_processAccumulator = 0f;
			return;
		}

		CoinSortingStationDefinition def = ResolveDefinition();
		float rate = def != null
			? def.ResolveCoinsPerSecond( StationLevel )
			: 4f;

		_processAccumulator += rate * dt;
		GroundCoinStack hopperStack = EnsureHopperStack();
		while ( _processAccumulator >= 1f && hopperStack != null && hopperStack.Count > 0 )
		{
			if ( !hopperStack.TryConsumeBottomDefinition( out TreasureDefinition next ) )
				break;

			if ( next == null || !EmitOne( next ) )
			{
				if ( next != null )
					hopperStack.TryAppendDefinition( next );
				break;
			}

			_processAccumulator -= 1f;
		}

		if ( BufferedCount == 0 )
			_processAccumulator = 0f;
	}

	/// <summary>Debug: force process one buffered coin if any.</summary>
	public bool DebugForceProcessOne()
	{
		GroundCoinStack hopperStack = EnsureHopperStack();
		if ( hopperStack == null || hopperStack.Count <= 0 )
			return false;

		if ( !hopperStack.TryPeekBottomDefinition( out TreasureDefinition next ) )
			return false;
		if ( !EmitOne( next ) )
			return false;

		return hopperStack.TryConsumeBottomDefinition( out _ );
	}

	bool EmitOne( TreasureDefinition definition )
	{
		if ( definition == null )
			return false;

		Transform chute = FindChute( definition );
		if ( chute == null )
		{
			if ( _missingChuteLogged.Add( definition ) )
				Debug.LogWarning(
					$"CoinSortingStation '{name}': no chute for coin '{definition.name}'. Add a ChuteBinding.",
					this );
			return false;
		}

		GroundCoinStack stack = GetOrCreateOutputStack( definition, chute );
		if ( stack == null )
			return false;

		int pending = 0;
		TreasureDefinition key = ResolveOutputKey( definition );
		if ( key != null && _pendingOutputByType.TryGetValue( key, out int pendingStored ) )
			pending = pendingStored;

		int logicalCount = stack.Count + pending;
		if ( stack.IsFull || logicalCount >= stack.MaxHeight )
		{
			BumpFullStackIndex( definition );
			stack = GetOrCreateOutputStack( definition, chute );
			if ( stack == null )
				return false;

			pending = 0;
			key = ResolveOutputKey( definition );
			if ( key != null && _pendingOutputByType.TryGetValue( key, out pendingStored ) )
				pending = pendingStored;
			logicalCount = stack.Count + pending;
			if ( stack.IsFull || logicalCount >= stack.MaxHeight )
				return false;
		}

		if ( key != null )
			_pendingOutputByType[ key ] = pending + 1;

		_activeOutputByType[ definition ] = stack;
		if ( key != null && key != definition )
			_activeOutputByType[ key ] = stack;

		Vector3 startPos = ResolveSortedOutputWorldPos( chute );
		Quaternion startRot = ResolveSortedOutputWorldRot();

		float pendingHeight = 0f;
		for ( int i = 0; i < pending; i++ )
			pendingHeight += TreasureStackSpacing.GetStep( definition );

		Vector3 endPos = stack.ContactPosition + Vector3.up * ( stack.SettledHeight + pendingHeight );
		Quaternion endRot = stack.transform.rotation;

		CoinSortingStationDefinition def = ResolveDefinition();
		float duration = def != null ? Mathf.Max( 0.05f, def.sortedCoinFlightDuration ) : 0.28f;
		float arcHeight = def != null ? Mathf.Max( 0f, def.sortedCoinFlightArcHeight ) : 0.35f;

		GroundCoinStack captureStack = stack;
		TreasureDefinition captureDef = definition;
		TreasureDefinition captureKey = key;
		TreasureMotionHost.Run( FlySortedCoinRoutine(
			captureDef,
			startPos,
			startRot,
			endPos,
			endRot,
			duration,
			arcHeight,
			() => CompleteSortedFlight( captureStack, captureDef, captureKey ) ) );

		EventBus.Publish( new CoinSorterUsedEvent
		{
			Station = this,
			Coin = definition
		} );

		PlaySortedFeedback();
		return true;
	}

	IEnumerator FlySortedCoinRoutine(
		TreasureDefinition definition,
		Vector3 startPos,
		Quaternion startRot,
		Vector3 endPos,
		Quaternion endRot,
		float duration,
		float arcHeight,
		Action onArrived )
	{
		if ( definition == null )
		{
			if ( onArrived != null )
				onArrived();
			yield break;
		}

		TreasureItem visual = TreasureItemFactory.RentVisualCoin( definition, startPos, startRot );
		if ( visual == null )
		{
			if ( onArrived != null )
				onArrived();
			yield break;
		}

		visual.SetMeshVisible( true );
		visual.ApplyWorldScale();
		visual.BeginHold();
		visual.BeginFlight();

		duration = Mathf.Max( 0.05f, duration );
		float spins = CoinFlipMotion.DefaultSpins;
		float elapsed = 0f;
		Transform t = visual.transform;

		while ( elapsed < duration )
		{
			if ( visual == null )
				break;

			elapsed += Time.deltaTime;
			float u = Mathf.Clamp01( elapsed / duration );
			t.position = CoinFlipMotion.EvaluateArcPosition( startPos, endPos, u, arcHeight );
			t.rotation = CoinFlipMotion.EvaluateFlipRotation( startRot, endRot, startPos, endPos, u, spins );
			yield return null;
		}

		if ( visual != null )
		{
			visual.EndFlight();
			TreasureItemFactory.ReturnVisualCoin( visual );
		}

		if ( onArrived != null )
			onArrived();
	}

	void EnsureSortedOutput()
	{
		if ( sortedOutput != null )
			return;

		Transform named = transform.Find( "Output" );
		if ( named == null )
			named = transform.Find( "SortedOutput" );
		if ( named != null )
			sortedOutput = named;
	}

	Vector3 ResolveSortedOutputWorldPos( Transform chute )
	{
		EnsureSortedOutput();
		if ( sortedOutput != null )
			return sortedOutput.position;

		Transform body = transform.Find( "Body" );
		Vector3 origin = body != null ? body.position : transform.position + transform.up * 0.5f;
		Renderer bodyRenderer = body != null ? body.GetComponent<Renderer>() : null;
		if ( bodyRenderer != null )
			origin = bodyRenderer.bounds.center;

		Vector3 chutePos = chute != null ? chute.position : origin - transform.forward;
		Vector3 planar = chutePos - origin;
		planar.y = 0f;
		if ( planar.sqrMagnitude < 0.0001f )
			planar = -transform.forward;
		else
			planar.Normalize();

		float extent = 0.4f;
		if ( bodyRenderer != null )
			extent = Mathf.Max( bodyRenderer.bounds.extents.x, bodyRenderer.bounds.extents.z );

		return origin + planar * extent;
	}

	Quaternion ResolveSortedOutputWorldRot()
	{
		EnsureSortedOutput();
		if ( sortedOutput != null )
			return sortedOutput.rotation;
		return transform.rotation;
	}

	void CompleteSortedFlight( GroundCoinStack stack, TreasureDefinition definition, TreasureDefinition key )
	{
		if ( key != null && _pendingOutputByType.TryGetValue( key, out int pending ) )
		{
			pending = Mathf.Max( 0, pending - 1 );
			if ( pending <= 0 )
				_pendingOutputByType.Remove( key );
			else
				_pendingOutputByType[ key ] = pending;
		}

		if ( stack == null || definition == null )
			return;

		if ( stack.IsFull || !stack.TryAppendDefinition( definition ) )
		{
			BumpFullStackIndex( definition );
			Transform chute = FindChute( definition );
			if ( chute == null )
				return;

			GroundCoinStack created = GetOrCreateOutputStack( definition, chute );
			if ( created != null )
				created.TryAppendDefinition( definition );
			return;
		}

		_activeOutputByType[ definition ] = stack;
		if ( key != null )
			_activeOutputByType[ key ] = stack;
	}

	Transform FindChute( TreasureDefinition definition )
	{
		if ( definition == null || chutes == null )
			return null;

		for ( int i = 0; i < chutes.Count; i++ )
		{
			ChuteBinding binding = chutes[ i ];
			if ( binding.coin == null || binding.chute == null )
				continue;
			if ( SameCoinType( binding.coin, definition ) )
				return binding.chute;
		}

		return null;
	}

	Vector3 ResolveChuteStackContact( Transform chute, float lateralOffset )
	{
		Vector3 pos = chute.position + chute.right * lateralOffset;
		Renderer marker = chute.GetComponentInChildren<Renderer>();
		if ( marker != null )
			pos.y = marker.bounds.max.y;
		else
			pos += Vector3.up * ( Mathf.Abs( chute.lossyScale.y ) * 0.5f );
		return pos;
	}

	GroundCoinStack GetOrCreateOutputStack( TreasureDefinition definition, Transform chute )
	{
		TreasureDefinition key = ResolveOutputKey( definition );
		if ( key == null )
			key = definition;

		if ( TryGetActiveOutput( key, definition, out GroundCoinStack existing ) )
		{
			if ( IsUsableOutputStack( existing ) && !existing.IsFull )
				return existing;

			if ( existing != null && existing.IsFull )
				BumpFullStackIndex( definition );
			else
			{
				_activeOutputByType.Remove( definition );
				if ( key != null )
					_activeOutputByType.Remove( key );
			}
		}

		int index = 0;
		if ( _fullStackIndexByType.TryGetValue( definition, out int stored ) )
			index = stored;
		else if ( key != null && _fullStackIndexByType.TryGetValue( key, out stored ) )
			index = stored;

		CoinSortingStationDefinition def = ResolveDefinition();
		float lateral = def != null ? def.fullStackLateralOffset : 0.35f;
		Vector3 pos = ResolveChuteStackContact( chute, lateral * index );
		Quaternion rot = TreasureOrientation.FlattenUpright( chute.rotation );

		GroundCoinStack nearest = GroundCoinStack.FindNearest( pos, lateral * 0.45f );
		if ( IsUsableOutputStack( nearest )
			&& !nearest.IsFull
			&& nearest.TryGetHomogeneousDefinition( out TreasureDefinition homo )
			&& SameCoinType( homo, definition ) )
		{
			_activeOutputByType[ definition ] = nearest;
			if ( key != null )
				_activeOutputByType[ key ] = nearest;
			return nearest;
		}

		GroundCoinStack created = GroundCoinStack.CreateAt( pos, rot );
		_activeOutputByType[ definition ] = created;
		if ( key != null )
			_activeOutputByType[ key ] = created;
		return created;
	}

	bool TryGetActiveOutput( TreasureDefinition key, TreasureDefinition definition, out GroundCoinStack stack )
	{
		if ( key != null && _activeOutputByType.TryGetValue( key, out stack ) && IsUsableOutputStack( stack ) )
			return true;
		if ( definition != null && _activeOutputByType.TryGetValue( definition, out stack ) && IsUsableOutputStack( stack ) )
			return true;
		stack = null;
		return false;
	}

	bool IsUsableOutputStack( GroundCoinStack stack )
	{
		if ( stack == null )
			return false;
		if ( IsHopperStack( stack ) || stack.IsMachineBuffer )
			return false;
		return true;
	}

	void BumpFullStackIndex( TreasureDefinition definition )
	{
		int index = 0;
		if ( _fullStackIndexByType.TryGetValue( definition, out int stored ) )
			index = stored;
		_fullStackIndexByType[ definition ] = index + 1;
		_activeOutputByType.Remove( definition );
	}

	static bool SameCoinType( TreasureDefinition a, TreasureDefinition b )
	{
		if ( a == b )
			return true;
		if ( a == null || b == null )
			return false;
		if ( !string.IsNullOrEmpty( a.id ) && a.id == b.id )
			return true;
		return false;
	}

#if UNITY_EDITOR
	public void EditorSetChutes( List<ChuteBinding> bindings )
	{
		chutes = bindings != null ? bindings : new List<ChuteBinding>();
	}

	public void EditorSetHopper( CoinSortingHopper value )
	{
		hopper = value;
	}

	public void EditorSetCrank( CoinSortingCrankInteractable value )
	{
		crank = value;
	}

	public void EditorSetMoveInteractable( CoinSortingStationMoveInteractable value )
	{
		moveInteractable = value;
	}

	public void EditorSetSortedFeedback( Feedbacks value )
	{
		onSortedFeedback = value;
	}

	public void EditorSetEnergyGauge( CoinSortingEnergyGauge value )
	{
		energyGauge = value;
	}

	public void EditorSetCrankSpin( CoinSortingCrankSpin value )
	{
		crankSpin = value;
	}

	public void EditorSetCrankClickFeedback( Feedbacks value )
	{
		onCrankClickFeedback = value;
	}

	public void EditorSetGaugeFullFeedback( Feedbacks value )
	{
		onGaugeFullFeedback = value;
	}

	public void EditorSetSortingShakeFeedback( Feedbacks value )
	{
		onSortingShakeFeedback = value;
	}

	public void EditorSetSortedOutput( Transform value )
	{
		sortedOutput = value;
	}
#endif
}
