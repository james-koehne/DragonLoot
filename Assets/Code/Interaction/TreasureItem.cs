using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Single physical treasure instance used for pile, hand, table/display, and world physics.
/// </summary>
[RequireComponent( typeof( Rigidbody ) )]
public class TreasureItem : MonoBehaviour
{
	[SerializeField]
	TreasureDefinition definition;

	static readonly int VariationSeedId = Shader.PropertyToID( "_VariationSeed" );
	static readonly int DirtStrengthId = Shader.PropertyToID( "_DirtStrength" );
	static MaterialPropertyBlock s_PropertyBlock;

	Rigidbody _body;
	Collider[] _colliders;
	Renderer[] _renderers;
	bool? _meshVisibilityState;
	TreasureItemState _state = TreasureItemState.Physics;
	ITreasureOwner _owner;
	TreasurePileVisual _originPile;
	bool _releasedViaAddressables;
	bool _distanceForcedSleep;
	bool _reclaiming;
	bool _inFlight;
	float _stableTimer;
	bool _usingContinuous;
	float _variationSeed;
	float _cleanProgress = 1f;

	public TreasureDefinition Definition => definition;
	public TreasureItemState State => _state;
	public bool ReleasedViaAddressables => _releasedViaAddressables;
	public ITreasureOwner Owner => _owner;
	public TreasurePileVisual OriginPile => _originPile;
	public TreasurePileVisual PileOwner => _owner as TreasurePileVisual;
	public CoinStackInteractable StackOwner => _owner as CoinStackInteractable;
	public Rigidbody Body => _body;
	public bool IsReclaiming => _reclaiming;
	/// <summary>True while a pickup/place tween owns this item's transform.</summary>
	public bool IsInFlight => _inFlight;

	/// <summary>0 = fully dirty, 1 = clean. Orthogonal to <see cref="TreasureItemState"/>.</summary>
	public float CleanProgress => _cleanProgress;

	public bool RequiresCleaning
	{
		get { return definition != null && definition.GetRequiresCleaning(); }
	}

	public bool IsClean
	{
		get { return !RequiresCleaning || _cleanProgress >= 0.999f; }
	}

	public bool IsDirty
	{
		get { return RequiresCleaning && _cleanProgress < 0.999f; }
	}

	/// <summary>
	/// Claims stack ownership during place flight without parenting (keeps tween free).
	/// Clears a stale PlayerCarry owner after the item was already removed from the hand.
	/// </summary>
	public void ClaimPendingStackOwner( ITreasureOwner stackOwner )
	{
		if ( stackOwner == null || _owner == stackOwner )
			return;

		ITreasureOwner previous = _owner;
		_owner = null;
		if ( previous != null )
		{
			if ( previous is PlayerCarry carry )
			{
				if ( carry.ContainsItem( this ) )
					previous.ReleaseTreasure( this );
			}
			else
				previous.ReleaseTreasure( this );
		}

		_owner = stackOwner;
	}

	public void BeginFlight()
	{
		_inFlight = true;
	}

	public void EndFlight()
	{
		_inFlight = false;
	}

	void Awake()
	{
		EnsureComponents();
		ApplyPhysicsFromDefinition();
		ApplyVariationSeed();
	}

	void OnEnable()
	{
		ApplyVariationSeed();
		ApplyDirtVisual();
		EnsureSparkleMaskContributor();
	}

	public void Bind( TreasureDefinition treasure, bool viaAddressables )
	{
		definition = treasure;
		_releasedViaAddressables = viaAddressables;
		_renderers = null;
		_meshVisibilityState = null;
		_variationSeed = 0f;
		if ( definition != null )
			definition.EnsurePhysicsDefaults();
		EnsureComponents();
		ApplyPhysicsFromDefinition();
		ApplyVisualOverrides();
		ResetCleanlinessFromDefinition();
		ApplyVariationSeed();
		ApplyDirtVisual();
		ApplyDisplayName();
		ApplyCollectableLayer();
		EnsureSparkleMaskContributor();
	}

	public void ResetCleanlinessFromDefinition()
	{
		_cleanProgress = RequiresCleaning ? 0f : 1f;
		ApplyDirtVisual();
	}

	public void SetDirty()
	{
		if ( !RequiresCleaning )
		{
			_cleanProgress = 1f;
			ApplyDirtVisual();
			return;
		}

		_cleanProgress = 0f;
		ApplyDirtVisual();
	}

	public void SetClean()
	{
		_cleanProgress = 1f;
		ApplyDirtVisual();
	}

	/// <summary>
	/// Adds cleaning progress in 0–1 units (not seconds). Refreshes dirt MPB.
	/// </summary>
	public void ApplyCleaning( float delta )
	{
		if ( !RequiresCleaning || delta <= 0f || _cleanProgress >= 1f )
			return;

		_cleanProgress = Mathf.Clamp01( _cleanProgress + delta );
		ApplyDirtVisual();
	}

	public void SetOriginPile( TreasurePileVisual pile )
	{
		_originPile = pile;
	}

	public void SetReclaiming( bool reclaiming )
	{
		_reclaiming = reclaiming;
	}

	public void OnSpawned()
	{
	}

	public void OnDespawned()
	{
		GemPyramidRegistry.NotifyRemoved( this );
		LooseTreasureManager.Unregister( this );
		TreasureProximitySleep.Unregister( this );
		UnregisterFromSurface();
		_owner = null;
		_originPile = null;
		_reclaiming = false;
		_inFlight = false;
		_renderers = null;
		_meshVisibilityState = null;
		_variationSeed = 0f;
		_cleanProgress = 1f;
	}

	/// <summary>True when loose in the world (physics or surface rolling).</summary>
	public bool IsWorldLoose
	{
		get
		{
			return _state == TreasureItemState.Physics || _state == TreasureItemState.SurfaceRolling;
		}
	}

	public static bool UsesSurfaceSimulation( TreasureDefinition def )
	{
		if ( def == null )
			return true;

		// Artifacts use real Rigidbody physics; coins/gems/other props stay on TreasureSurface.
		return def.category != TreasureCategory.Artifact;
	}

	void UnregisterFromSurface()
	{
		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		if ( world != null && world.Simulator != null )
			world.Simulator.Unregister( this );
	}

	public void EnterPile( ITreasureOwner pileOwner )
	{
		GemPyramidRegistry.NotifyRemoved( this );
		LeavePreviousOwner();
		LooseTreasureManager.Unregister( this );
		TreasureProximitySleep.Unregister( this );
		UnregisterFromSurface();

		_owner = pileOwner;
		_state = TreasureItemState.InPile;
		_distanceForcedSleep = false;
		_reclaiming = false;

		TreasurePileVisual pileVisual = pileOwner as TreasurePileVisual;
		if ( pileVisual != null )
			_originPile = pileVisual;

		ApplyPileCollisionLayer();
		ApplyWorldScale();
		ClearRigidbodyConstraints();
		SetPhysicsMode( kinematic: true, detectCollisions: true, collidersEnabled: true );
		SyncRigidbodyToTransform();
		ForceSleep();
	}

	/// <summary>Legacy no-arg pile enter (owner already assigned via Set / EnterPile).</summary>
	public void EnterPile()
	{
		EnterPile( _owner );
	}

	public void BeginHold( ITreasureOwner playerOwner )
	{
		_inFlight = false;
		GemPyramidRegistry.NotifyRemoved( this );
		LeavePreviousOwner();
		LooseTreasureManager.Unregister( this );
		TreasureProximitySleep.Unregister( this );
		UnregisterFromSurface();
		CoinColumnCylinderBinder.StripFromItem( this );

		_owner = playerOwner;
		_state = TreasureItemState.Held;
		_distanceForcedSleep = false;
		_reclaiming = false;
		transform.SetParent( null, true );
		ClearRigidbodyConstraints();
		SetPhysicsMode( kinematic: true, detectCollisions: false, collidersEnabled: false );
		SyncRigidbodyToTransform();
		SetMeshVisible( true );
	}

	public void BeginHold()
	{
		BeginHold( null );
	}

	public void EnterPhysics( Vector3 worldPosition, Quaternion worldRotation, Vector3 velocity )
	{
		if ( UsesSurfaceSimulation( definition ) )
		{
			EnterSurface( worldPosition, worldRotation, velocity );
			return;
		}

		_inFlight = false;
		LeavePreviousOwner();
		TreasureProximitySleep.Unregister( this );
		UnregisterFromSurface();
		CoinColumnCylinderBinder.StripFromItem( this );

		_owner = null;
		_state = TreasureItemState.Physics;
		_reclaiming = false;
		transform.SetParent( null, true );
		transform.SetPositionAndRotation( worldPosition, worldRotation );
		ApplyCollectableLayer();
		ApplyWorldScale();
		ApplyPhysicsFromDefinition();
		SetPhysicsMode( kinematic: false, detectCollisions: true, collidersEnabled: true );
		ApplyPhysicsConstraints();
		SyncRigidbodyToTransform();
		SetMeshVisible( true );

		if ( _body != null )
		{
			_body.linearVelocity = velocity;
			_body.angularVelocity = Vector3.zero;
			float sleep = definition != null ? definition.sleepThreshold : 0.01f;
			_body.sleepThreshold = Mathf.Max( 0.001f, sleep );
			if ( IsCoinDefinition() )
			{
				_body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
				_usingContinuous = true;
			}
		}

		_stableTimer = 0f;
		LooseTreasureManager.Register( this );
		TreasureProximitySleep.RegisterPhysics( this );

		bool nearby = TreasureProximitySleep.ShouldSimulate( worldPosition );
		_distanceForcedSleep = !nearby;
		if ( nearby )
		{
			if ( _body != null )
				_body.WakeUp();
		}
		else
			ForceSleep();
	}

	/// <summary>
	/// Free-motion on the treasure surface (no Rigidbody dynamics).
	/// </summary>
	public void EnterSurface( Vector3 worldPosition, Quaternion worldRotation, Vector3 velocity )
	{
		EnterSurface( worldPosition, worldRotation, velocity, snapToSeat: true );
	}

	/// <param name="snapToSeat">
	/// When false, keep the authored Y so the simulator can fall with gravity/flow
	/// (used when gems release from a gold pile above the stamped surface).
	/// </param>
	public void EnterSurface(
		Vector3 worldPosition,
		Quaternion worldRotation,
		Vector3 velocity,
		bool snapToSeat )
	{
		EnterSurface( worldPosition, worldRotation, velocity, snapToSeat, fromRest: false );
	}

	/// <param name="fromRest">
	/// When true, surface flow acceleration ramps up from zero (pile detach).
	/// </param>
	public void EnterSurface(
		Vector3 worldPosition,
		Quaternion worldRotation,
		Vector3 velocity,
		bool snapToSeat,
		bool fromRest )
	{
		_inFlight = false;
		LeavePreviousOwner();
		TreasureProximitySleep.Unregister( this );
		UnregisterFromSurface();

		_owner = null;
		_state = TreasureItemState.SurfaceRolling;
		_reclaiming = false;
		_distanceForcedSleep = false;
		transform.SetParent( null, true );
		transform.SetPositionAndRotation( worldPosition, worldRotation );
		ApplyCollectableLayer();
		ApplyWorldScale();
		ClearRigidbodyConstraints();
		SetPhysicsMode( kinematic: true, detectCollisions: true, collidersEnabled: true );
		SyncRigidbodyToTransform();
		ForceSleep();
		SetMeshVisible( true );

		LooseTreasureManager.Register( this );

		TreasureSurfaceWorld world = TreasureSurfaceWorld.EnsureExists();
		if ( world.TryGetChunkCoord( worldPosition, out TreasureChunkCoord coord ) )
			world.EnsureChunkLoaded( coord );

		if ( snapToSeat
			&& world.Sampler != null
			&& world.Sampler.TrySample( worldPosition, out TreasureSurfaceSample sample ) )
		{
			// Stable lift for gems/artifacts so throw land → sim seat → settle use the same Y.
			bool useStableSeat = definition != null && definition.category != TreasureCategory.Coin;
			float contactY = useStableSeat
				? sample.Height + TreasureSurfaceSeat.GetStableContactLift( this )
				: TreasureSurfaceSeat.GetContactY( this, sample, worldRotation );
			float seatTolerance = 0.12f;
			if ( definition != null )
			{
				seatTolerance = Mathf.Max(
					seatTolerance,
					TreasureStackSpacing.GetStep( definition ) + 0.06f );
			}

			float lift = Mathf.Max( 0f, contactY - sample.Height );
			if ( worldPosition.y <= contactY + seatTolerance + lift * 0.25f )
			{
				Vector3 seated = worldPosition;
				seated.y = contactY;
				transform.position = seated;
				SyncRigidbodyToTransform();
			}
		}

		world.Simulator.Register( this, velocity, fromRest );
	}

	public void EnterSurface( Vector3 worldPosition, Quaternion worldRotation )
	{
		EnterSurface( worldPosition, worldRotation, Vector3.zero );
	}

	public void EnterPhysics( Vector3 worldPosition, Quaternion worldRotation )
	{
		EnterPhysics( worldPosition, worldRotation, Vector3.zero );
	}

	/// <summary>
	/// Places loose treasure in the world as a settled body (surface sleep / kinematic).
	/// </summary>
	public void EnterSettledPhysics( Vector3 worldPosition, Quaternion worldRotation )
	{
		if ( UsesSurfaceSimulation( definition ) )
		{
			EnterSurface( worldPosition, worldRotation, Vector3.zero );
			TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
			if ( world != null && world.Simulator != null )
				world.Simulator.ForceSleep( this );
			return;
		}

		_inFlight = false;
		LeavePreviousOwner();
		TreasureProximitySleep.Unregister( this );
		UnregisterFromSurface();

		_owner = null;
		_state = TreasureItemState.Physics;
		_reclaiming = false;
		_distanceForcedSleep = false;
		transform.SetParent( null, true );
		transform.SetPositionAndRotation( worldPosition, worldRotation );
		ApplyCollectableLayer();
		ApplyWorldScale();
		ApplyPhysicsFromDefinition();
		SetPhysicsMode( kinematic: true, detectCollisions: true, collidersEnabled: true );
		ApplyPhysicsConstraints();
		SyncRigidbodyToTransform();

		if ( definition != null && definition.category == TreasureCategory.Artifact )
			TrySnapArtifactToTreasureSurface();

		ForceSleep();
		SetMeshVisible( true );

		LooseTreasureManager.Register( this );
		TreasureProximitySleep.RegisterPhysics( this );
	}

	/// <summary>
	/// Moves an already-loose surface coin without tearing down column visuals on the stack bottom.
	/// </summary>
	public void ApplySettledWorldPose( Vector3 worldPosition, Quaternion worldRotation )
	{
		if ( _state == TreasureItemState.SurfaceRolling && !_inFlight && !_reclaiming )
		{
			transform.SetPositionAndRotation( worldPosition, worldRotation );
			ApplyWorldScale();
			SyncRigidbodyToTransform();
			ForceSleep();
			return;
		}

		EnterSettledPhysics( worldPosition, worldRotation );
	}

	/// <summary>
	/// Freezes a dynamic Physics body in place so stacks cannot be shoved by other loot.
	/// </summary>
	public void SettlePhysicsInPlace()
	{
		if ( _state == TreasureItemState.SurfaceRolling )
		{
			TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
			if ( world != null && world.Simulator != null )
				world.Simulator.ForceSleep( this );
			ForceSleep();
			return;
		}

		if ( _state != TreasureItemState.Physics || _body == null || _reclaiming )
			return;

		if ( _body.isKinematic )
			return;

		ApplyPhysicsFromDefinition();
		SetPhysicsMode( kinematic: true, detectCollisions: true, collidersEnabled: true );
		ApplyPhysicsConstraints();
		SyncRigidbodyToTransform();
		ForceSleep();
		_distanceForcedSleep = false;
	}

	/// <summary>Deprecated alias for EnterPhysics.</summary>
	public void ReleaseToWorld( Vector3 worldPosition, Quaternion worldRotation )
	{
		EnterPhysics( worldPosition, worldRotation, Vector3.zero );
	}

	/// <summary>Deprecated alias for EnterPhysics with release velocity.</summary>
	public void ReleaseToWorld( Vector3 worldPosition, Quaternion worldRotation, Vector3 releaseVelocity )
	{
		EnterPhysics( worldPosition, worldRotation, releaseVelocity );
	}

	public void EnterDisplayed( ITreasureOwner displayOwner, Vector3 worldPosition, Quaternion worldRotation )
	{
		EnterDisplayed( displayOwner, null, worldPosition, worldRotation );
	}

	public void EnterDisplayed(
		ITreasureOwner displayOwner,
		Transform displayParent,
		Vector3 worldPosition,
		Quaternion worldRotation )
	{
		_inFlight = false;
		if ( _owner != displayOwner )
		{
			LeavePreviousOwner();
			LooseTreasureManager.Unregister( this );
			TreasureProximitySleep.Unregister( this );
			UnregisterFromSurface();
			_owner = displayOwner;
		}

		_state = TreasureItemState.Displayed;
		_reclaiming = false;

		Transform parent = displayParent;
		if ( parent == null )
		{
			MonoBehaviour displayBehaviour = displayOwner as MonoBehaviour;
			if ( displayBehaviour != null )
				parent = displayBehaviour.transform;
		}

		transform.SetParent( parent, true );
		transform.SetPositionAndRotation( worldPosition, worldRotation );
		ApplyCollectableLayer();
		ApplyWorldScale();
		ClearRigidbodyConstraints();
		SetPhysicsMode( kinematic: true, detectCollisions: true, collidersEnabled: true );
		SyncRigidbodyToTransform();
		ForceSleep();
		SetMeshVisible( true );
	}

	public void EnterStacked( ITreasureOwner stackOwner, Transform stackRoot, Vector3 localPosition, Quaternion localRotation )
	{
		_inFlight = false;

		// Re-asserting the same owner must not ReleaseTreasure — that removes the slot and can DestroyStack.
		if ( _owner != stackOwner )
		{
			LeavePreviousOwner();
			LooseTreasureManager.Unregister( this );
			TreasureProximitySleep.Unregister( this );
			UnregisterFromSurface();
			_owner = stackOwner;
		}

		_state = TreasureItemState.Stacked;
		_distanceForcedSleep = false;
		_reclaiming = false;

		Transform parent = stackRoot;
		if ( parent == null )
		{
			MonoBehaviour stackBehaviour = stackOwner as MonoBehaviour;
			if ( stackBehaviour != null )
				parent = stackBehaviour.transform;
		}

		transform.SetParent( parent, false );
		transform.localPosition = localPosition;
		transform.localRotation = localRotation;
		ApplyCollectableLayer();
		ApplyWorldScale();
		ClearRigidbodyConstraints();
		// Kinematic + no collision impulses keeps stacks from toppling when bumped.
		SetPhysicsMode( kinematic: true, detectCollisions: false, collidersEnabled: true );
		SyncRigidbodyToTransform();
		ForceSleep();
	}

	/// <summary>
	/// Recovers orphaned / broken pickup states (held with no owner, disabled colliders, etc.).
	/// </summary>
	public bool TryRepairPickupState()
	{
		if ( _reclaiming )
			return false;

		if ( _state == TreasureItemState.Held )
		{
			if ( _inFlight )
				return false;

			PlayerCarry carry = _owner as PlayerCarry;
			if ( carry == null || !carry.ContainsItem( this ) )
				{
					if ( UsesSurfaceSimulation( definition ) )
						EnterSurface( transform.position, transform.rotation, Vector3.zero );
					else
						EnterPhysics( transform.position, transform.rotation, Vector3.zero );
					return true;
				}
		}

		if ( _state == TreasureItemState.Physics || _state == TreasureItemState.SurfaceRolling )
		{
			RefreshColliderCache();
			bool anyEnabled = false;
			if ( _colliders != null )
			{
				for ( int i = 0; i < _colliders.Length; i++ )
				{
					if ( _colliders[ i ] != null && _colliders[ i ].enabled )
					{
						anyEnabled = true;
						break;
					}
				}
			}

			if ( !anyEnabled || ( _body != null && !_body.detectCollisions ) )
			{
				bool kinematic = _state == TreasureItemState.SurfaceRolling;
				SetPhysicsMode( kinematic: kinematic, detectCollisions: true, collidersEnabled: true );
				if ( !kinematic )
					ApplyPhysicsConstraints();
				EnsureCollidersEnabledForPickup();
				return true;
			}
		}

		return false;
	}

	public void ApplyProximitySimulation( bool shouldSimulate )
	{
		if ( _state != TreasureItemState.Physics || _body == null || _body.isKinematic )
			return;

		if ( !shouldSimulate )
		{
			if ( !_body.IsSleeping() )
				ForceSleep();
			_distanceForcedSleep = true;
			return;
		}

		if ( _distanceForcedSleep )
		{
			_distanceForcedSleep = false;
			_body.WakeUp();
		}
	}

	public Vector3 GetWorldScale()
	{
		if ( definition != null )
			return definition.worldScale;
		return Vector3.one * 0.35f;
	}

	public Vector3 GetHeldScale()
	{
		if ( definition != null )
			return definition.heldScale;
		return Vector3.one * 0.08f;
	}

	public Quaternion GetHeldLocalRotation( bool active )
	{
		if ( definition != null )
			return definition.GetHeldLocalRotation( active );
		return Quaternion.identity;
	}

	public void ApplyWorldScale()
	{
		ApplyDesiredWorldScale( GetWorldScale() );
	}

	public void ApplyHeldScale()
	{
		transform.localScale = GetHeldScale();
	}

	/// <summary>
	/// Sets localScale so lossyScale matches <paramref name="desiredLossy"/>, preserving
	/// XYZ proportions under a non-uniform parent.
	/// </summary>
	public void ApplyDesiredWorldScale( Vector3 desiredLossy )
	{
		if ( transform.parent == null )
		{
			transform.localScale = desiredLossy;
			return;
		}

		Vector3 parentLossy = transform.parent.lossyScale;
		transform.localScale = new Vector3(
			SafeDivScale( desiredLossy.x, parentLossy.x ),
			SafeDivScale( desiredLossy.y, parentLossy.y ),
			SafeDivScale( desiredLossy.z, parentLossy.z ) );
	}

	static float SafeDivScale( float a, float b )
	{
		return Mathf.Abs( b ) < 0.0001f ? a : a / b;
	}

	public void EnsureComponents()
	{
		if ( _body == null )
			_body = GetComponent<Rigidbody>();
		if ( _body == null )
			_body = gameObject.AddComponent<Rigidbody>();

		RefreshColliderCache();
		if ( definition != null && definition.category == TreasureCategory.Gem )
			EnsureGemSphereCollider();
		else if ( _colliders == null || _colliders.Length == 0 )
		{
			SphereCollider sphere = gameObject.AddComponent<SphereCollider>();
			sphere.radius = 0.5f;
			_colliders = new Collider[] { sphere };
		}

		if ( GetComponent<TreasureItemInteractable>() == null )
			gameObject.AddComponent<TreasureItemInteractable>();

		if ( definition != null && definition.category == TreasureCategory.Chest )
		{
			ChestInteractable chest = GetComponent<ChestInteractable>();
			if ( chest == null )
				chest = gameObject.AddComponent<ChestInteractable>();
			if ( definition.chestDefinition != null )
				chest.BindDefinition( definition.chestDefinition );
		}

		_body.sleepThreshold = 0.01f;
		if ( definition != null )
			_body.sleepThreshold = Mathf.Max( 0.001f, definition.sleepThreshold );
		ApplyCollectableLayer();
	}

	/// <summary>
	/// Gems use a single sphere for accurate aim picking (replaces mesh/capsule colliders).
	/// </summary>
	public void EnsureGemSphereCollider()
	{
		RefreshColliderCache();
		float radius = definition != null ? Mathf.Max( 0.05f, definition.pickupRadius ) : 0.35f;

		SphereCollider sphere = null;
		if ( _colliders != null )
		{
			for ( int i = 0; i < _colliders.Length; i++ )
			{
				Collider col = _colliders[ i ];
				if ( col == null )
					continue;

				SphereCollider asSphere = col as SphereCollider;
				if ( asSphere != null && col.gameObject == gameObject )
				{
					sphere = asSphere;
					continue;
				}

				if ( Application.isPlaying )
					Object.Destroy( col );
				else
					Object.DestroyImmediate( col );
			}
		}

		if ( sphere == null )
			sphere = gameObject.AddComponent<SphereCollider>();

		sphere.radius = radius;
		sphere.center = Vector3.zero;
		RefreshColliderCache();
	}

	void LeavePreviousOwner()
	{
		ITreasureOwner previous = _owner;
		if ( previous == null )
			return;

		_owner = null;
		previous.ReleaseTreasure( this );
	}

	public void SetHeldShadows( bool enabled )
	{
		EnsureRendererCache();
		if ( _renderers == null )
			return;

		ShadowCastingMode mode = enabled ? ShadowCastingMode.On : ShadowCastingMode.Off;
		for ( int i = 0; i < _renderers.Length; i++ )
		{
			if ( _renderers[ i ] != null )
				_renderers[ i ].shadowCastingMode = mode;
		}
	}

	/// <summary>
	/// Shows or hides mesh renderers without touching colliders (used when a column cylinder replaces bulk visuals).
	/// </summary>
	public void SetMeshVisible( bool visible )
	{
		if ( _meshVisibilityState.HasValue && _meshVisibilityState.Value == visible )
			return;

		EnsureRendererCache();
		if ( _renderers == null )
			return;

		_meshVisibilityState = visible;
		for ( int i = 0; i < _renderers.Length; i++ )
		{
			if ( _renderers[ i ] != null )
				_renderers[ i ].enabled = visible;
		}
	}

	void EnsureRendererCache()
	{
		if ( _renderers != null && _renderers.Length > 0 )
			return;

		Renderer[] all = GetComponentsInChildren<Renderer>( true );
		if ( all == null || all.Length == 0 )
		{
			_renderers = all;
			return;
		}

		int kept = 0;
		for ( int i = 0; i < all.Length; i++ )
		{
			Renderer r = all[ i ];
			if ( r != null && !CoinColumnCylinderBinder.IsBinderVisualRenderer( r.transform, transform ) )
				kept++;
		}

		if ( kept == all.Length )
		{
			_renderers = all;
			return;
		}

		Renderer[] filtered = new Renderer[ kept ];
		int write = 0;
		for ( int i = 0; i < all.Length; i++ )
		{
			Renderer r = all[ i ];
			if ( r == null || CoinColumnCylinderBinder.IsBinderVisualRenderer( r.transform, transform ) )
				continue;

			filtered[ write ] = r;
			write++;
		}

		_renderers = filtered;
	}

	void ApplyCollectableLayer()
	{
		int layer = LayerMask.NameToLayer( "Collectable" );
		if ( layer < 0 )
			return;

		SetLayerRecursive( gameObject, layer );
	}

	/// <summary>
	/// Pile props stay on Collectable (player walks through) unless the definition opts into
	/// solid player collision for large obstacles (Default layer).
	/// </summary>
	void ApplyPileCollisionLayer()
	{
		if ( definition != null && definition.collideWithPlayerOnPile )
		{
			SetLayerRecursive( gameObject, 0 );
			return;
		}

		ApplyCollectableLayer();
	}

	static void SetLayerRecursive( GameObject go, int layer )
	{
		if ( go == null )
			return;

		go.layer = layer;
		Transform t = go.transform;
		for ( int i = 0; i < t.childCount; i++ )
			SetLayerRecursive( t.GetChild( i ).gameObject, layer );
	}

	void ApplyVisualOverrides()
	{
		if ( definition == null )
			return;

		MeshFilter filter = GetComponent<MeshFilter>();
		if ( filter != null && definition.meshOverride != null )
			filter.sharedMesh = definition.meshOverride;

		Renderer renderer = GetComponent<Renderer>();
		if ( renderer != null && definition.materialOverride != null )
			renderer.sharedMaterial = definition.materialOverride;
	}

	void EnsureSparkleMaskContributor()
	{
		TreasureSparkleMaskContributor contributor = GetComponent<TreasureSparkleMaskContributor>();
		if ( contributor == null )
			contributor = gameObject.AddComponent<TreasureSparkleMaskContributor>();

		TreasureSparkleDefinition.SparkleSourceKind kind = TreasureSparkleDefinition.SparkleSourceKind.Artifact;
		if ( definition != null )
			kind = TreasureSparkleDefinition.KindFromCategory( definition.category );
		contributor.SetKind( kind );
		contributor.RefreshRegistration();
	}

	/// <summary>
	/// Pins DragonLoot/Coin instance variation to a stable per-item seed so moving/jittering
	/// transforms do not rehash tint/smoothness every frame.
	/// </summary>
	void ApplyVariationSeed()
	{
		if ( definition == null || definition.category != TreasureCategory.Coin )
			return;

		EnsureRendererCache();
		if ( _renderers == null || _renderers.Length == 0 )
			return;

		if ( _variationSeed <= 0f )
			_variationSeed = ( Mathf.Abs( GetInstanceID() ) % 9973 ) + 1;

		if ( s_PropertyBlock == null )
			s_PropertyBlock = new MaterialPropertyBlock();

		for ( int i = 0; i < _renderers.Length; i++ )
		{
			Renderer renderer = _renderers[ i ];
			if ( renderer == null )
				continue;

			renderer.GetPropertyBlock( s_PropertyBlock );
			s_PropertyBlock.SetFloat( VariationSeedId, _variationSeed );
			renderer.SetPropertyBlock( s_PropertyBlock );
		}
	}

	void ApplyDirtVisual()
	{
		EnsureRendererCache();
		if ( _renderers == null || _renderers.Length == 0 )
			return;

		float dirtStrength = 0f;
		if ( RequiresCleaning )
		{
			float maxDirt = 1.35f;
			TreasureCleaningDefinition cleaning = RuntimeDefinition.Resolve(
				ref s_cleaningDefinitionCache );
			if ( cleaning != null )
				maxDirt = cleaning.maxDirtStrength;
			dirtStrength = ( 1f - Mathf.Clamp01( _cleanProgress ) ) * maxDirt;
		}

		if ( s_PropertyBlock == null )
			s_PropertyBlock = new MaterialPropertyBlock();

		for ( int i = 0; i < _renderers.Length; i++ )
		{
			Renderer renderer = _renderers[ i ];
			if ( renderer == null )
				continue;

			if ( !RendererSupportsDirt( renderer ) )
				continue;

			renderer.GetPropertyBlock( s_PropertyBlock );
			s_PropertyBlock.SetFloat( DirtStrengthId, dirtStrength );
			renderer.SetPropertyBlock( s_PropertyBlock );
		}
	}

	static bool RendererSupportsDirt( Renderer renderer )
	{
		Material[] mats = renderer.sharedMaterials;
		if ( mats == null || mats.Length == 0 )
		{
			Material single = renderer.sharedMaterial;
			return MaterialSupportsDirt( single );
		}

		for ( int i = 0; i < mats.Length; i++ )
		{
			if ( MaterialSupportsDirt( mats[ i ] ) )
				return true;
		}

		return false;
	}

	static bool MaterialSupportsDirt( Material material )
	{
		if ( material == null )
			return false;
		if ( material.HasProperty( DirtStrengthId ) )
			return true;
		return material.shader != null && material.shader.name == "DragonLoot/Artifact";
	}

	static TreasureCleaningDefinition s_cleaningDefinitionCache;

	void ApplyPhysicsFromDefinition()
	{
		if ( _body == null )
			return;

		float mass = 0.1f;
		float linearDrag = 0.5f;
		float angDrag = 0.5f;

		if ( definition != null )
		{
			mass = definition.rigidbodyMass > 0.01f ? definition.rigidbodyMass : definition.GetDefaultMass();
			linearDrag = definition.drag;
			angDrag = definition.angularDrag;
		}

		_body.mass = mass;
		_body.linearDamping = linearDrag;
		_body.angularDamping = angDrag;
		_body.collisionDetectionMode = CollisionDetectionMode.Discrete;
		_body.interpolation = _body.isKinematic
			? RigidbodyInterpolation.None
			: RigidbodyInterpolation.Interpolate;
	}

	void SetPhysicsMode( bool kinematic, bool detectCollisions, bool collidersEnabled )
	{
		if ( _body == null )
			EnsureComponents();

		RefreshColliderCache();

		_body.isKinematic = kinematic;
		_body.detectCollisions = detectCollisions;
		_body.useGravity = !kinematic;
		// Interpolated kinematic bodies lag behind a camera/hold parent by a frame.
		_body.interpolation = kinematic
			? RigidbodyInterpolation.None
			: RigidbodyInterpolation.Interpolate;

		if ( _colliders != null )
		{
			for ( int i = 0; i < _colliders.Length; i++ )
			{
				if ( _colliders[ i ] != null )
					_colliders[ i ].enabled = collidersEnabled;
			}
		}

		if ( kinematic )
			ForceSleep();
	}

	void RefreshColliderCache()
	{
		_colliders = GetComponentsInChildren<Collider>( true );
	}

	void ApplyPhysicsConstraints()
	{
		if ( _body == null )
			return;

		// Coins must be free to topple flat; upright balancing is handled in FixedUpdate.
		_body.constraints = RigidbodyConstraints.None;
	}

	void FixedUpdate()
	{
		if ( _state != TreasureItemState.Physics || _body == null || _body.isKinematic || _reclaiming )
			return;

		if ( _distanceForcedSleep )
			return;

		TickCoinTopple();
		TickPhysicsStabilization();
	}

	bool IsCoinDefinition()
	{
		return definition != null && definition.category == TreasureCategory.Coin;
	}

	void TickCoinTopple()
	{
		if ( !IsCoinDefinition() )
			return;

		float maxUpright = definition != null ? definition.maxUprightAngle : 35f;
		float strength = definition != null ? definition.autoToppleStrength : 2.5f;
		if ( strength <= 0.01f )
			return;

		Vector3 up = transform.up;
		float angle = Vector3.Angle( up, Vector3.up );
		float faceTilt = Mathf.Min( angle, 180f - angle );
		float uprightFromFlat = 90f - faceTilt;
		if ( uprightFromFlat < maxUpright )
			return;

		Vector3 tipAxis = Vector3.Cross( up, Vector3.up );
		if ( tipAxis.sqrMagnitude < 0.0001f )
			tipAxis = Vector3.Cross( transform.forward, Vector3.up );
		if ( tipAxis.sqrMagnitude < 0.0001f )
			return;

		_body.AddTorque( tipAxis.normalized * strength, ForceMode.Acceleration );
	}

	void TickPhysicsStabilization()
	{
		float delay = definition != null ? definition.physicsStabilizationDelay : 0.35f;
		float speed = _body.linearVelocity.magnitude;
		float spin = _body.angularVelocity.magnitude;
		float sleepThresh = definition != null ? definition.sleepThreshold : 0.01f;
		float settleSpeed = Mathf.Max( 0.02f, sleepThresh * 4f );

		if ( speed > settleSpeed || spin > settleSpeed * 8f )
		{
			_stableTimer = 0f;
			return;
		}

		_stableTimer += Time.fixedDeltaTime;
		if ( _stableTimer < delay )
			return;

		_body.linearVelocity = Vector3.zero;
		_body.angularVelocity = Vector3.zero;

		if ( definition != null && definition.category == TreasureCategory.Artifact )
			TrySnapArtifactToTreasureSurface();

		if ( _usingContinuous )
		{
			_body.collisionDetectionMode = CollisionDetectionMode.Discrete;
			_usingContinuous = false;
		}

		EnsureCollidersEnabledForPickup();
		_body.Sleep();
		_stableTimer = 0f;
	}

	/// <summary>
	/// When an artifact has nearly stopped, snap Y to surface contact and pull XZ
	/// back onto traversable if it settled outside the painted area.
	/// </summary>
	void TrySnapArtifactToTreasureSurface()
	{
		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		if ( world == null || world.Sampler == null || !world.IsInitialized )
			return;

		TreasureSurfaceDefinition surfaceDef = world.Definition;
		float snapDist = surfaceDef != null ? surfaceDef.artifactSurfaceSnapDistance : 0.07f;
		float seatLift = TreasureSurfaceSeat.GetContactLift( this, transform.rotation, Vector3.up );
		Vector3 pos = transform.position;

		bool needXz = true;
		if ( world.Sampler.TrySample( pos, out TreasureSurfaceSample sample ) && sample.Traversable )
		{
			needXz = false;
			float contactY = sample.Height + seatLift;
			if ( Mathf.Abs( pos.y - contactY ) > snapDist )
			{
				pos.y = contactY;
				transform.position = pos;
				SyncRigidbodyToTransform();
			}
		}

		if ( needXz )
		{
			if ( !world.TryFindNearestTraversable( pos, out Vector3 dest, out TreasureSurfaceSample recovered, preferStable: true ) )
				return;

			dest.y = recovered.Height + seatLift;
			transform.SetPositionAndRotation( dest, transform.rotation );
			SyncRigidbodyToTransform();
		}
	}

	void EnsureCollidersEnabledForPickup()
	{
		RefreshColliderCache();
		if ( _colliders == null )
			return;

		for ( int i = 0; i < _colliders.Length; i++ )
		{
			if ( _colliders[ i ] != null )
				_colliders[ i ].enabled = true;
		}

		if ( _body != null )
			_body.detectCollisions = true;

		ApplyCollectableLayer();
	}

	void ClearRigidbodyConstraints()
	{
		if ( _body != null )
			_body.constraints = RigidbodyConstraints.None;
	}

	public void SyncRigidbodyToTransform()
	{
		if ( _body == null )
			return;

		_body.position = transform.position;
		_body.rotation = transform.rotation;
	}

	void ForceSleep()
	{
		if ( _body == null )
			return;

		_body.linearVelocity = Vector3.zero;
		_body.angularVelocity = Vector3.zero;
		_body.Sleep();
	}

	void ApplyDisplayName()
	{
		TreasureItemInteractable interactable = GetComponent<TreasureItemInteractable>();
		if ( interactable == null )
			interactable = GetComponentInChildren<TreasureItemInteractable>( true );
		if ( interactable == null )
			return;

		if ( definition != null && !string.IsNullOrEmpty( definition.displayName ) )
			interactable.SetDisplayName( definition.displayName );
	}
}
