using System.Collections;
using System.Collections.Generic;

using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Movable treasure container: grid cargo bed, track-constrained push, unload-point dump.
/// Scene setup: Rigidbody (kinematic) + colliders on this object; optional <see cref="cargoRoot"/>;
/// assign <see cref="MinecartDefinition"/>. Create via Dragon Loot → Create Minecart Setup.
/// </summary>
[RequireComponent( typeof( Collider ) )]
[RequireComponent( typeof( Rigidbody ) )]
public class MinecartInteractable : InteractableBase, ITreasureOwner, ITreasurePlacementTarget, IFeedbackIntensity
{
	sealed class CargoStack
	{
		public int OriginX;
		public int OriginY;
		public Vector2Int Footprint;
		public TreasureDefinition Definition;
		public readonly List<TreasureItem> Items = new List<TreasureItem>();
		public GroundCoinStack CoinPile;

		public int Count
		{
			get
			{
				if ( CoinPile != null )
					return CoinPile.Count;

				return Items.Count;
			}
		}

		public bool IsCoinStack => CoinPile != null;
	}

	static readonly List<CargoStack> UnloadBuffer = new List<CargoStack>( 32 );
	static readonly List<MinecartInteractable> All = new List<MinecartInteractable>( 8 );

	[SerializeField]
	MinecartDefinition definition;

	[Tooltip( "Local parent for cargo poses. Defaults to this transform." )]
	[SerializeField]
	Transform cargoRoot;

	[SerializeField]
	MinecartTrack track;

	[SerializeField]
	Transform[] wheels;

	[SerializeField]
	Feedbacks onPushFeedbacks;

	[SerializeField]
	Feedbacks onShoveFeedbacks;

	[SerializeField]
	Feedbacks onLoadFeedbacks;

	[SerializeField]
	Feedbacks onUnloadFeedbacks;

	[SerializeField]
	Feedbacks onTakeOutFeedbacks;

	[SerializeField]
	Feedbacks onStopFeedbacks;

	[Tooltip( "Seat for drive carts. Defaults to a point above the cart origin." )]
	[SerializeField]
	Transform seat;

	[SerializeField]
	Feedbacks onDriveEnterFeedbacks;

	[SerializeField]
	Feedbacks onDriveMoveFeedbacks;

	[SerializeField]
	Feedbacks onDriveBrakeFeedbacks;

	[SerializeField]
	Feedbacks onDriveReverseFeedbacks;

	[SerializeField]
	Feedbacks onDriveExitFeedbacks;

	[SerializeField]
	Feedbacks onHopStartFeedbacks;

	[SerializeField]
	Feedbacks onHopLandFeedbacks;

	[Tooltip( "Editor-authored cars locked behind this lead. Rebuilt on play." )]
	[SerializeField]
	List<MinecartInteractable> authoredFollowers = new List<MinecartInteractable>( 4 );

	[SerializeField]
	MinecartInteractable consistLead;

	[Header( "Cargo Layout" )]
	[SerializeField]
	[Min( 1 )]
	int columns = 4;

	[SerializeField]
	[Min( 1 )]
	int rows = 3;

	[SerializeField]
	[Min( 0.01f )]
	float slotSpacing = 0.22f;

	[SerializeField]
	[Min( 0f )]
	float margin = 0.05f;

	[SerializeField]
	[Min( 0 )]
	int maxStackPerCell = 0;

	[Header( "Placement" )]
	[SerializeField]
	[Min( 0.05f )]
	float snapDuration = 0.25f;

	[SerializeField]
	[Min( 1f )]
	float bounceScale = 1.15f;

	[Header( "Editor" )]
	[SerializeField]
	bool drawLayoutGizmosAlways;

	Rigidbody _body;
	CargoStack[ , ] _cellStacks;
	readonly List<CargoStack> _stacks = new List<CargoStack>();
	readonly List<TreasureItem> _allItems = new List<TreasureItem>();
	float _distanceAlongTrack;
	bool _bound;
	float _coastSpeed;
	bool _holdPush;
	bool _riderPresent;
	readonly List<MinecartInteractable> _followers = new List<MinecartInteractable>( 8 );
	float _alongSpeed;
	bool _wasMoving;
	float _stopFeedbackReadyTime;
	bool _recalling;
	float _recallDistance;
	MinecartCallPost _recallPost;
	float _driveSpeed;
	bool _drivePowered;
	bool _driveBraking;
	int _speedWriteFrame;
	bool _autoMoving;
	float _autoTargetDistance;
	MinecartTrack _autoTargetTrack;
	MinecartAutoController _autoOwner;
	readonly List<MinecartNetworkPathfinder.RouteLeg> _autoRoute = new List<MinecartNetworkPathfinder.RouteLeg>( 8 );
	int _autoRouteIndex;
	bool _autoForcedExitValid;
	int _autoForcedJunctionId = -1;
	MinecartTrack _autoForcedExitTrack;
	float _autoForcedExitDistance;
	int _autoForcedExitTravelSign;
	bool _hopping;
	float _hopElapsed;
	float _hopArcDuration;
	float _hopHeight;
	float _hopLiftY;
	float _hopRemainingClear;
	float _hopDescendElapsed;
	int _hopTravelSign = 1;
	MinecartInteractable _hopBlocker;

	const float StopSpeedEpsilon = 0.04f;
	const float HopHeightScaleMax = 3f;
	const float StopFeedbackDebounce = 0.18f;
	const float UnboundBindRetry = 0.5f;

	float _unbindRetryTimer;
	Transform _visualRoot;
	float _visualBaseLocalY;
	float _cargoBaseLocalY;
	bool _hopBaseCached;
	Collider _ownCollider;
	bool _junctionSteerValid;
	float _junctionSteer; // -1 left, +1 right
	int _committedJunctionId = -1;
	bool _hasCommittedExit;
	MinecartTrack _committedExitTrack;
	float _committedExitDistance;
	int _committedExitTravelSign;
	int _suppressJunctionId = -1;
	bool _junctionRiding;
	MinecartJunctionRidePath _activeRidePath;
	float _rideDistanceAlong;
	int _rideFacingPolarity = 1; // transform.forward = pathTangent * polarity during rides
	int _trackFacingSign = 1; // transform.forward = EvaluateTangent * sign on tracks
	// Consist trail: rear-hitch path through a junction until followers clear the exit.
	// Polarity is locked at ride start so cars stay on the lead's rear (never swap sides mid-turn).
	MinecartJunctionRidePath _consistRidePath;
	float _consistRideProgress;
	float _consistExitTravel;
	int _consistRidePolarity = 1;
	static readonly List<MinecartJunctionExit> JunctionExitBuffer = new List<MinecartJunctionExit>( 8 );
	static readonly List<MinecartJunctionExit> JunctionExitExtraBuffer = new List<MinecartJunctionExit>( 8 );

	public static IReadOnlyList<MinecartInteractable> ActiveCarts => All;

	public TreasureOwnerKind OwnerKind => TreasureOwnerKind.Minecart;

	public MinecartDefinition Definition => definition;

	public MinecartTrack BoundTrack => track;

	public float DistanceAlongTrack => _distanceAlongTrack;

	public int ItemCount
	{
		get
		{
			int n = _allItems.Count;
			for ( int i = 0; i < _stacks.Count; i++ )
			{
				CargoStack stack = _stacks[ i ];
				if ( stack != null && stack.CoinPile != null )
					n += stack.CoinPile.Count;
			}

			return n;
		}
	}

	public int SlotCount => DisplayTableSlotLayout.SlotCount( GridRows, GridColumns );

	public int LayoutRows => GridRows;

	public int LayoutColumns => GridColumns;

	public float LayoutSlotSpacing => CellSpacing;

	public float LayoutMargin => margin;

	public Transform DisplayArea => cargoRoot != null ? cargoRoot : transform;

	public IReadOnlyList<TreasureItem> StoredItems => _allItems;

	public float PushAttachRadius => definition != null ? definition.pushAttachRadius : 2.5f;

	public float PushHoldThreshold => definition != null ? definition.pushHoldThreshold : 0.18f;

	public float ShoveSpeed => definition != null ? definition.shoveSpeed : 7f;

	public float ShoveDrag => definition != null ? definition.shoveDrag : 8f;

	public float MouseSteerScale => definition != null ? definition.mouseSteerScale : 0.25f;

	public float UnloadScatterRadius => definition != null ? definition.unloadScatterRadius : 0.35f;

	public float BlockingHalfLength => definition != null ? Mathf.Max( 0.1f, definition.blockingHalfLength ) : 0.85f;

	public float RideHeight => definition != null ? definition.rideHeight : 0.15f;

	public float WheelRadius => definition != null ? Mathf.Max( 0.05f, definition.wheelRadius ) : 0.21f;

	public float TrackSnapRadius => definition != null ? definition.trackSnapRadius : 4f;

	public float JunctionApproachRadius => definition != null ? Mathf.Max( 0.1f, definition.junctionApproachRadius ) : 2.5f;

	public float JunctionSteerMinAlign => definition != null ? Mathf.Clamp01( definition.junctionLookMinAlign ) : 0.15f;

	public string DebugJunctionCommitLabel
	{
		get
		{
			MinecartInteractable lead = ConsistLead;
			if ( !lead._hasCommittedExit || lead._committedExitTrack == null )
				return "Junction: none";

			string steer = !lead._junctionSteerValid
				? "straight"
				: ( lead._junctionSteer < 0f ? "left" : "right" );
			return $"Junction #{lead._committedJunctionId} → {lead._committedExitTrack.name} sign {lead._committedExitTravelSign} ({steer})";
		}
	}

	public bool AttachPlayerWhenStanding => !IsDriveCart && ( definition == null || definition.attachPlayerWhenStanding );

	public float RiderSpeedMultiplier => definition != null ? Mathf.Max( 0.1f, definition.riderSpeedMultiplier ) : 2f;

	public bool IsDriveCart => definition != null && definition.kind == MinecartKind.Drive;

	public bool CargoEnabled => !IsDriveCart;

	/// <summary>Unattached cargo lead that can be summoned by stations.</summary>
	public bool IsAutoEligible
	{
		get
		{
			if ( !CargoEnabled || !IsConsistLead || IsHoldPushing || _drivePowered )
				return false;

			for ( int i = 0; i < _followers.Count; i++ )
			{
				MinecartInteractable follower = _followers[ i ];
				if ( follower != null && follower.IsDriveCart )
					return false;
			}

			return true;
		}
	}

	/// <summary>Cargo-only consist — player cannot shove / hold-push; interact sends the cart.</summary>
	public bool RejectsPlayerPush
	{
		get
		{
			MinecartInteractable lead = ConsistLead;
			if ( lead.IsDriveCart )
				return false;

			for ( int i = 0; i < lead._followers.Count; i++ )
			{
				MinecartInteractable follower = lead._followers[ i ];
				if ( follower != null && follower.IsDriveCart )
					return false;
			}

			return lead.GetComponent<MinecartAutoController>() != null;
		}
	}

	/// <summary>True when no 1×1 footprint cell is free (cart cannot accept more cargo).</summary>
	public bool IsCargoFull
	{
		get
		{
			if ( !CargoEnabled || _cellStacks == null )
				return true;

			for ( int y = 0; y < GridRows; y++ )
			{
				for ( int x = 0; x < GridColumns; x++ )
				{
					if ( _cellStacks[ x, y ] == null )
						return false;

					CargoStack stack = _cellStacks[ x, y ];
					if ( stack != null && stack.OriginX == x && stack.OriginY == y && stack.CoinPile != null && !stack.CoinPile.IsFull )
						return false;
				}
			}

			return true;
		}
	}

	public Transform Seat => seat;

	public MinecartInteractable ConsistLead => consistLead != null ? consistLead : this;

	public bool IsConsistLead => consistLead == null;

	public int FollowerCount
	{
		get
		{
			if ( !Application.isPlaying && _followers.Count <= 0 && authoredFollowers != null )
				return authoredFollowers.Count;

			return _followers.Count;
		}
	}

	public float ConsistSpacing => BlockingHalfLength * 2f;

	public float RecallSpeed => definition != null ? Mathf.Max( 0.1f, definition.recallSpeed ) : 8f;

	public float RecallStopDistance => definition != null ? Mathf.Max( 0.02f, definition.recallStopDistance ) : 0.2f;

	public float DriveMaxSpeed => definition != null ? Mathf.Max( 0.1f, definition.driveMaxSpeed ) : 10f;

	public float DriveAcceleration => definition != null ? Mathf.Max( 0.1f, definition.driveAcceleration ) : 12f;

	public float DriveBrake => definition != null ? Mathf.Max( 0.1f, definition.driveBrake ) : 18f;

	public bool IsRecalling => ConsistLead._recalling;

	public bool IsAutoMoving => ConsistLead._autoMoving;

	public bool IsHopping => ConsistLead._hopping;

	/// <summary>Lead visual lift in world Y for seat / orbit while hopping.</summary>
	public float HopLiftY => ConsistLead._hopLiftY;

	/// <summary>Along-track travel sign locked when the hop began.</summary>
	public int HopTravelSign => ConsistLead._hopTravelSign;

	public bool HasTransferInFlight
	{
		get
		{
			for ( int i = 0; i < _allItems.Count; i++ )
			{
				TreasureItem item = _allItems[ i ];
				if ( item != null && item.IsInFlight )
					return true;
			}

			for ( int s = 0; s < _stacks.Count; s++ )
			{
				CargoStack stack = _stacks[ s ];
				if ( stack != null && stack.CoinPile != null && stack.CoinPile.HasInFlight )
					return true;
			}

			return false;
		}
	}

	public bool IsHoldPushing => ConsistLead._holdPush;

	public float HopHeight => definition != null ? Mathf.Max( 0.05f, definition.hopHeight ) : 0.75f;

	public float HopDuration => definition != null ? Mathf.Max( 0.1f, definition.hopDuration ) : 0.45f;

	public float HopStaggerDelay => definition != null ? Mathf.Max( 0f, definition.hopStaggerDelay ) : 0.06f;

	public float HopHeightPerExtraCar => definition != null ? Mathf.Max( 0f, definition.hopHeightPerExtraCar ) : 0.12f;

	public float HopBlockerScale => definition != null ? Mathf.Max( 0f, definition.hopBlockerScale ) : 0.25f;

	public float AlongTrackSpeed => ConsistLead._alongSpeed;

	/// <summary>Commanded drive speed along the track (not the last-frame measured travel).</summary>
	public float DriveSpeed => ConsistLead._driveSpeed;

	public bool IsJunctionRiding => ConsistLead._junctionRiding;

	public float FeedbackIntensity
	{
		get
		{
			if ( !IsConsistLead )
				return 0f;

			float refSpeed = ResolveMoveRefSpeed();
			if ( refSpeed < 0.01f )
				return 0f;

			return Mathf.Clamp01( Mathf.Abs( _alongSpeed ) / refSpeed );
		}
	}

	public bool FeedbackBraking => IsConsistLead && _driveBraking;

	float RiderMotionScale => _riderPresent && AttachPlayerWhenStanding ? RiderSpeedMultiplier : 1f;

	int GridColumns => Mathf.Max( 1, columns );

	int GridRows => Mathf.Max( 1, rows );

	float CellSpacing => Mathf.Max( 0.01f, slotSpacing );

	int MaxStackPerCell => Mathf.Max( 0, maxStackPerCell );

	void Reset()
	{
		SetInteractionName( "Minecart" );
		if ( definition == null )
			return;

		columns = Mathf.Max( 1, definition.gridColumns );
		rows = Mathf.Max( 1, definition.gridRows );
		slotSpacing = Mathf.Max( 0.01f, definition.cellSpacing );
		maxStackPerCell = Mathf.Max( 0, definition.maxStackPerCell );
	}

	void Awake()
	{
		_body = GetComponent<Rigidbody>();
		if ( _body != null )
		{
			_body.isKinematic = true;
			_body.useGravity = false;
			_body.interpolation = RigidbodyInterpolation.Interpolate;
		}

		EnsureCargoRoot();
		RebuildGrid();
		_visualRoot = transform.Find( "VisualRoot" );
		CacheHopBaseLocalY();
		_ownCollider = GetComponent<Collider>();
		if ( IsDriveCart )
			DisableDriveCargo();
		else if ( GetComponent<MinecartAutoController>() == null )
			gameObject.AddComponent<MinecartAutoController>();
		if ( string.IsNullOrEmpty( InteractionName ) || InteractionName == "Interactable" )
			SetInteractionName( IsDriveCart ? "Drive Minecart" : "Minecart" );
	}

	void Start()
	{
		BindTrack( snapPose: true );
		RebuildAuthoredConsist();
	}

	void OnEnable()
	{
		if ( !All.Contains( this ) )
			All.Add( this );
		TryStartMoveLoop();
	}

	void OnDisable()
	{
		All.Remove( this );
		_riderPresent = false;
		_driveBraking = false;
		EndHop( playLandFeedback: false );
		ClearJunctionLook();
		ClearJunctionCommit();
		StopMoveLoop();
		CancelRecall( arrived: false );
		CancelAutoMove( arrived: false );
		DetachFromConsist();
		FinishInFlightSnaps();
		StopAllCoroutines();
	}

	void LateUpdate()
	{
		if ( !IsConsistLead )
			return;

		// Hold-push only writes speed on successful TryPushAlong frames.
		if ( _holdPush && Time.frameCount != _speedWriteFrame )
			_alongSpeed = 0f;

		TickHopVisual( Time.deltaTime );
	}

	void OnValidate()
	{
		EnsureCargoRoot();
		columns = Mathf.Max( 1, columns );
		rows = Mathf.Max( 1, rows );
		slotSpacing = Mathf.Max( 0.01f, slotSpacing );
		margin = Mathf.Max( 0f, margin );
		maxStackPerCell = Mathf.Max( 0, maxStackPerCell );
		snapDuration = Mathf.Max( 0.05f, snapDuration );
		bounceScale = Mathf.Max( 1f, bounceScale );
	}

	void Update()
	{
		if ( !IsConsistLead )
			return;

		if ( !_bound )
		{
			_unbindRetryTimer -= Time.deltaTime;
			if ( _unbindRetryTimer <= 0f )
			{
				_unbindRetryTimer = UnboundBindRetry;
				EnsureBoundTrack();
			}
		}

		TickHop( Time.deltaTime );

		bool simulating = _recalling || _autoMoving || _drivePowered || _hopping
			|| Mathf.Abs( _coastSpeed ) >= StopSpeedEpsilon;
		if ( !simulating )
		{
			if ( !_holdPush )
				_alongSpeed = 0f;
			TickStopFeedback();
			return;
		}

		EnsureHopCoastSeed();
		TickRecall( Time.deltaTime );
		TickAutoMove( Time.deltaTime );
		TickDrive( Time.deltaTime );
		TickCoast( Time.deltaTime );
		TickStopFeedback();
	}

	void EnsureCargoRoot()
	{
		if ( cargoRoot == null )
			cargoRoot = transform;
	}

	void CacheHopBaseLocalY()
	{
		if ( _visualRoot == null )
			_visualRoot = transform.Find( "VisualRoot" );

		EnsureCargoRoot();
		_visualBaseLocalY = _visualRoot != null ? _visualRoot.localPosition.y : 0f;
		_cargoBaseLocalY = cargoRoot != null && cargoRoot != transform ? cargoRoot.localPosition.y : 0f;
		_hopBaseCached = true;
	}

	void DisableDriveCargo()
	{
		if ( cargoRoot != null && cargoRoot != transform )
			cargoRoot.gameObject.SetActive( false );
	}

	void RebuildGrid()
	{
		int cols = GridColumns;
		int rows = GridRows;
		_cellStacks = new CargoStack[ cols, rows ];
		_stacks.Clear();
		_allItems.Clear();
	}

	public override bool CanInteract( PlayerController player )
	{
		return IsAvailable && player != null;
	}

	public override void Interact( PlayerController player )
	{
		if ( player == null )
			return;

		if ( IsDriveCart )
		{
			PlayerMinecartDrive drive = player.MinecartDrive;
			if ( drive != null && drive.IsDriving )
				return;

			if ( WantsDriveEnter( player ) )
			{
				if ( drive != null )
					drive.TryToggle( this );
				return;
			}
		}

		if ( RejectsPlayerPush )
		{
			MinecartAutoController auto = ConsistLead.GetComponent<MinecartAutoController>();
			if ( auto != null )
				auto.TryPlayerSend();
			return;
		}

		PlayerMinecartPush push = player.MinecartPush;
		if ( push != null )
			push.BeginPress( this );
	}

	/// <summary>
	/// Drive carts: side (and top) aim enters; front/back aim shoves / hold-pushes.
	/// </summary>
	public bool WantsDriveEnter( PlayerController player )
	{
		if ( !IsDriveCart )
			return false;

		RaycastHit hit;
		if ( player != null
			&& player.Interaction != null
			&& player.Interaction.TryGetLastHit( out hit )
			&& IsOwnCollider( hit.collider ) )
			return IsDriveEnterFace( hit );

		Vector3 reference = player != null ? player.transform.position : transform.position + transform.right;
		return IsDriveEnterFromWorldPoint( reference );
	}

	public void AppendOutlineRenderers( List<Renderer> destination )
	{
		if ( destination == null )
			return;

		Transform root = _visualRoot != null ? _visualRoot : transform;
		if ( root == null )
			return;

		Renderer[] renderers = root.GetComponentsInChildren<Renderer>( true );
		for ( int i = 0; i < renderers.Length; i++ )
		{
			Renderer renderer = renderers[ i ];
			if ( renderer == null || !renderer.enabled )
				continue;
			if ( !( renderer is MeshRenderer ) && !( renderer is SkinnedMeshRenderer ) )
				continue;
			if ( renderer.sharedMaterial == null )
				continue;
			if ( cargoRoot != null && cargoRoot != transform && renderer.transform.IsChildOf( cargoRoot ) )
				continue;
			if ( renderer.GetComponentInParent<TreasureItem>() != null )
				continue;
			if ( renderer.GetComponentInParent<GroundCoinStack>() != null )
				continue;

			destination.Add( renderer );
		}
	}

	bool IsOwnCollider( Collider collider )
	{
		if ( collider == null )
			return false;

		return collider.transform == transform || collider.transform.IsChildOf( transform );
	}

	bool IsDriveEnterFace( RaycastHit hit )
	{
		Vector3 localNormal = transform.InverseTransformDirection( hit.normal );
		float ax = Mathf.Abs( localNormal.x );
		float ay = Mathf.Abs( localNormal.y );
		float az = Mathf.Abs( localNormal.z );
		if ( ax + ay + az < 0.0001f )
			return IsDriveEnterFromWorldPoint( hit.point );

		return az < ax || az < ay;
	}

	bool IsDriveEnterFromWorldPoint( Vector3 worldPoint )
	{
		Vector3 local = transform.InverseTransformPoint( worldPoint );
		BoxCollider box = _ownCollider as BoxCollider;
		if ( box != null )
			local -= box.center;

		return Mathf.Abs( local.x ) >= Mathf.Abs( local.z );
	}

	public void ReleaseTreasure( TreasureItem item )
	{
		Remove( item );
	}

	public bool CanPlace( TreasureItem item, in PlacementQuery query )
	{
		if ( !CargoEnabled )
			return false;
		if ( item == null || !IsAvailable || item.Definition == null )
			return false;

		PlayerCarry carry = query.Player != null ? query.Player.Carry : null;
		if ( carry == null || carry.Count <= 0 )
			return false;

		int ox;
		int oy;
		int stackIndex;
		bool valid;
		if ( !TryResolvePlaceOrigin( item, in query, out ox, out oy, out stackIndex, out valid ) || !valid )
			return false;

		if ( !GroundCoinStack.IsGroundStackableCoin( item ) )
			return true;

		GroundCoinStack pile = FindCoinPile( ox, oy );
		return pile == null || pile.CanPlace( item, in query );
	}

	public bool TryGetPlacementPreview( TreasureItem item, in PlacementQuery query, out PlacementPreview preview )
	{
		preview = default;
		if ( !CargoEnabled || item == null || item.Definition == null )
			return false;

		int ox;
		int oy;
		int stackIndex;
		bool valid;
		if ( !TryResolvePlaceOrigin( item, in query, out ox, out oy, out stackIndex, out valid ) )
		{
			GetCellWorldPose( 0, 0, 0, item, item.Definition, out Vector3 fallbackPos, out Quaternion fallbackRot );
			preview.SetItemMesh( fallbackPos, fallbackRot, item.GetWorldScale(), false );
			return true;
		}

		if ( GroundCoinStack.IsGroundStackableCoin( item ) )
		{
			GroundCoinStack pile = FindCoinPile( ox, oy );
			if ( pile != null )
				return pile.TryGetPlacementPreview( item, in query, out preview );

			GetSlotContactPose( ox, oy, out Vector3 emptyPos, out Quaternion emptyRot );
			preview.SetItemMesh( emptyPos, emptyRot, item.GetWorldScale(), valid );
			return true;
		}

		GetCellWorldPose( ox, oy, stackIndex, item, item.Definition, out Vector3 pos, out Quaternion rot );
		preview.SetItemMesh( pos, rot, item.GetWorldScale(), valid );
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

		int ox;
		int oy;
		int stackIndex;
		bool valid;
		if ( !TryResolvePlaceOrigin( item, in query, out ox, out oy, out stackIndex, out valid ) || !valid )
			return false;

		if ( GroundCoinStack.IsGroundStackableCoin( item ) )
			return TryPlaceCoinOnSlot( item, in query, ox, oy );

		if ( carry.ContainsItem( item ) && !carry.TryDetachItem( item ) )
			return false;

		if ( !CommitItem( item, ox, oy, animate: true ) )
			return false;

		NotifyCargoPlaced( fromPlayer: true );
		return true;
	}

	bool TryPlaceCoinOnSlot( TreasureItem item, in PlacementQuery query, int ox, int oy )
	{
		GroundCoinStack pile = GetOrCreateCoinStack( ox, oy );
		if ( pile == null )
			return false;

		if ( !pile.TryPlace( item, in query ) )
			return false;

		EventBus.Publish( new MinecartCargoLoadedEvent { Cart = this, Item = item } );
		return true;
	}

	/// <summary>Accept an already-detached world item into the cart (tests / future loaders).</summary>
	public bool TryAcceptWorldItem( TreasureItem item )
	{
		if ( item == null || item.Definition == null )
			return false;

		if ( !CanAcceptDefinition( item.Definition, out int ox, out int oy, out _ ) )
			return false;

		if ( GroundCoinStack.IsGroundStackableCoin( item ) )
		{
			GroundCoinStack pile = GetOrCreateCoinStack( ox, oy );
			if ( pile == null )
				return false;

			pile.BeginAppendFlight( item, pile.transform.rotation );
			EventBus.Publish( new MinecartCargoLoadedEvent { Cart = this, Item = item } );
			return true;
		}

		return CommitItem( item, ox, oy, animate: true );
	}

	bool CommitItem( TreasureItem item, int ox, int oy, bool animate )
	{
		if ( item == null || item.Definition == null )
			return false;

		if ( GroundCoinStack.IsGroundStackableCoin( item ) )
			return false;

		TreasureDefinition def = item.Definition;
		Vector2Int footprint = def.GetCartGridSize();
		CargoStack stack = _cellStacks[ ox, oy ];
		if ( stack == null )
		{
			stack = new CargoStack
			{
				OriginX = ox,
				OriginY = oy,
				Footprint = footprint,
				Definition = def
			};
			_stacks.Add( stack );
			MarkFootprint( ox, oy, footprint, stack );
		}

		stack.Items.Add( item );
		if ( !_allItems.Contains( item ) )
			_allItems.Add( item );

		int stackIndex = stack.Items.Count - 1;
		EventBus.Publish( new MinecartCargoLoadedEvent { Cart = this, Item = item } );
		if ( animate && isActiveAndEnabled )
		{
			StartCoroutine( SnapIntoSlotRoutine( item, ox, oy, stackIndex ) );
			return true;
		}

		GetCellWorldPose( ox, oy, stackIndex, item, def, out Vector3 pos, out Quaternion rot );
		item.EnterDisplayed( this, cargoRoot, pos, rot );
		return true;
	}

	public void Remove( TreasureItem item )
	{
		if ( item == null )
			return;

		for ( int s = 0; s < _stacks.Count; s++ )
		{
			CargoStack stack = _stacks[ s ];
			if ( stack.CoinPile != null )
				continue;

			int index = stack.Items.IndexOf( item );
			if ( index < 0 )
				continue;

			stack.Items.RemoveAt( index );
			_allItems.Remove( item );
			PlayFeedback( onTakeOutFeedbacks );

			if ( stack.Items.Count == 0 )
			{
				ClearFootprint( stack );
				_stacks.RemoveAt( s );
			}
			else
			{
				RestackVisuals( stack );
			}

			return;
		}
	}

	/// <summary>
	/// Clears cargo occupancy and returns items. Items still own this cart until
	/// <see cref="TreasureItem.EnterSurface"/> / physics transfer calls <see cref="ReleaseTreasure"/>.
	/// </summary>
	public void ExtractAllCargo( List<TreasureItem> destination )
	{
		if ( destination == null )
			return;

		destination.Clear();
		UnloadBuffer.Clear();
		UnloadBuffer.AddRange( _stacks );

		for ( int s = 0; s < UnloadBuffer.Count; s++ )
		{
			CargoStack stack = UnloadBuffer[ s ];
			if ( stack.CoinPile != null )
			{
				GroundCoinStack pile = stack.CoinPile;
				stack.CoinPile = null;
				pile.DetachCartHost();
				pile.ExtractAllAsWorldItems( destination );
			}
			else
			{
				for ( int i = 0; i < stack.Items.Count; i++ )
				{
					TreasureItem item = stack.Items[ i ];
					if ( item != null )
						destination.Add( item );
				}
			}

			ClearFootprint( stack );
		}

		_stacks.Clear();
		_allItems.Clear();
		UnloadBuffer.Clear();
		if ( destination.Count > 0 )
			PlayFeedback( onUnloadFeedbacks );
	}

	/// <summary>Detaches one cargo item (top of a stack or top coin) for station transfer.</summary>
	public bool TryExtractOneItem( out TreasureItem item )
	{
		item = null;
		if ( !CargoEnabled )
			return false;

		for ( int s = _stacks.Count - 1; s >= 0; s-- )
		{
			CargoStack stack = _stacks[ s ];
			if ( stack == null )
				continue;

			if ( stack.CoinPile != null )
			{
				if ( !stack.CoinPile.TryExtractTopAsWorldItem( out item ) || item == null )
					continue;

				if ( stack.CoinPile.Count <= 0 )
				{
					stack.CoinPile = null;
					ClearFootprint( stack );
					_stacks.RemoveAt( s );
				}

				PlayFeedback( onTakeOutFeedbacks );
				return true;
			}

			if ( stack.Items.Count <= 0 )
				continue;

			int last = stack.Items.Count - 1;
			item = stack.Items[ last ];
			stack.Items.RemoveAt( last );
			_allItems.Remove( item );
			PlayFeedback( onTakeOutFeedbacks );

			if ( stack.Items.Count == 0 )
			{
				ClearFootprint( stack );
				_stacks.RemoveAt( s );
			}
			else
				RestackVisuals( stack );

			if ( item != null )
				item.transform.SetParent( null, true );

			return item != null;
		}

		return false;
	}

	public bool CanAcceptDefinitionForTransfer( TreasureDefinition def )
	{
		return CanAcceptDefinition( def, out _, out _, out _ );
	}

	public void ClearCoast()
	{
		MinecartInteractable lead = ConsistLead;
		if ( lead != this )
		{
			lead.ClearCoast();
			return;
		}

		_coastSpeed = 0f;
		_alongSpeed = 0f;
		_driveSpeed = 0f;
		_drivePowered = false;
		_driveBraking = false;
	}

	public void SetRiderPresent( bool present )
	{
		_riderPresent = present;
	}

	public void SetHoldPush( bool held )
	{
		MinecartInteractable lead = ConsistLead;
		if ( lead != this )
		{
			lead.SetHoldPush( held );
			return;
		}

		_holdPush = held;
		if ( held )
		{
			CancelRecall( arrived: false );
			CancelAutoMove( arrived: false );
			_coastSpeed = 0f;
			_driveSpeed = 0f;
			_drivePowered = false;
		}
		else
			_alongSpeed = 0f;
	}

	public bool TryGetTrackTangent( out Vector3 tangent )
	{
		tangent = transform.forward;
		if ( _junctionRiding && _activeRidePath != null )
		{
			Vector3 position;
			Vector3 pathTangent;
			if ( _activeRidePath.TryEvaluate( _rideDistanceAlong, out position, out pathTangent )
			     && pathTangent.sqrMagnitude > 0.0001f )
			{
				// Path travel direction (not nose polarity) so W/S resolve against motion along the curve.
				tangent = pathTangent.normalized;
				return true;
			}

			return tangent.sqrMagnitude > 0.0001f;
		}

		if ( !EnsureBoundTrack() )
			return false;

		Vector3 positionOnTrack;
		Vector3 up;
		if ( !track.Evaluate( _distanceAlongTrack, out positionOnTrack, out tangent, out up ) )
			return false;

		return true;
	}

	public bool SharesConsistWith( MinecartInteractable other )
	{
		if ( other == null )
			return false;

		return ConsistLead == other.ConsistLead;
	}

	public bool TryPushAlong( float signedDelta )
	{
		MinecartInteractable lead = ConsistLead;
		if ( lead != this )
			return lead.TryPushAlong( signedDelta );

		float scale = ConsistRiderMotionScale();
		signedDelta *= scale;
		if ( !EnsureBoundTrack() || Mathf.Abs( signedDelta ) < 0.00001f )
			return false;

		float remaining = signedDelta;
		float totalDistance = 0f;
		float consistExitDelta = 0f;
		const int maxHops = 4;
		for ( int hop = 0; hop < maxHops && Mathf.Abs( remaining ) > 0.00001f; hop++ )
		{
			if ( _junctionRiding )
			{
				float rideTraveled;
				float leftover;
				if ( !AdvanceJunctionRide( remaining, out rideTraveled, out leftover ) )
					break;

				totalDistance += rideTraveled;
				remaining = leftover;
				continue;
			}

			int travelSign = remaining >= 0f ? 1 : -1;
			UpdateJunctionCommit( travelSign );

			float step = remaining;
			bool transferred;
			float unusedLeftover;
			float junctionDistance;
			if ( TryPrepareJunctionTransfer( travelSign, ref step, out transferred, out unusedLeftover, out junctionDistance ) )
			{
				float absRemaining = Mathf.Abs( remaining );
				if ( Mathf.Abs( step ) > 0.00001f )
				{
					float next;
					if ( !TryAdvanceWithHop( step, out next ) )
					{
						// Blocked before the junction — keep unused delta out of this frame only.
						break;
					}

					float traveled = Mathf.Abs( MeasureTraveled( _distanceAlongTrack, next ) );
					_distanceAlongTrack = next;
					totalDistance += traveled;
					absRemaining = Mathf.Max( 0f, absRemaining - traveled );
				}

				float leftToJunction = Mathf.Abs( track.SignedAlong( _distanceAlongTrack, junctionDistance ) );
				// Snap onto the bake cut when approach-to-port let us overshoot the ride entry.
				const float SnapOntoCutEpsilon = 1.25f;
				if ( transferred && leftToJunction > 0.05f && leftToJunction <= SnapOntoCutEpsilon )
				{
					_distanceAlongTrack = junctionDistance;
					leftToJunction = 0f;
				}

				if ( transferred && leftToJunction <= 0.05f )
				{
					if ( !TryBeginJunctionRide( travelSign ) )
					{
						// No ride path — continue on arrival track with leftover distance.
						remaining = travelSign * absRemaining;
						break;
					}

					remaining = travelSign * absRemaining;
					continue;
				}

				// Not at the cut yet (partial advance) — do not drop leftover speed.
				remaining = travelSign * absRemaining;
				break;
			}

			if ( TryClampAtBlockedThrough( travelSign, ref remaining, out float blockedTravel ) )
			{
				float blockedAbs = Mathf.Abs( blockedTravel );
				totalDistance += blockedAbs;
				if ( IsConsistTrailOnExit() )
					consistExitDelta += blockedAbs;
				remaining = 0f;
				break;
			}

			float nextPos;
			if ( !TryAdvanceWithHop( remaining, out nextPos ) )
				break;

			float moved = Mathf.Abs( MeasureTraveled( _distanceAlongTrack, nextPos ) );
			_distanceAlongTrack = nextPos;
			totalDistance += moved;
			if ( IsConsistTrailOnExit() )
				consistExitDelta += moved;
			remaining = 0f;
		}

		if ( totalDistance < 0.00001f )
			return false;

		if ( _junctionRiding )
			ApplyJunctionRidePose();
		else
			ApplyTrackPose( runtime: true );

		float motionSign = _driveSpeed >= 0f ? 1f : -1f;
		if ( Mathf.Abs( _driveSpeed ) < StopSpeedEpsilon )
			motionSign = signedDelta >= 0f ? 1f : -1f;

		SpinWheels( motionSign * totalDistance );
		if ( consistExitDelta > 0.00001f )
			TickConsistRideTrailAfterMove( consistExitDelta );
		SnapFollowers();

		if ( _hopping && totalDistance > 0.00001f )
			_hopRemainingClear = Mathf.Max( 0f, _hopRemainingClear - totalDistance );

		float dt = Time.deltaTime;
		if ( dt > 0.00001f )
			_alongSpeed = motionSign * ( totalDistance / dt );
		_speedWriteFrame = Time.frameCount;

		return true;
	}

	bool TryAdvanceWithHop( float signedDelta, out float nextPos )
	{
		nextPos = _distanceAlongTrack;
		if ( track == null )
			return false;

		int travelSign = signedDelta >= 0f ? 1 : -1;
		if ( !_hopping )
		{
			if ( track.WouldClampAgainstCarts( _distanceAlongTrack, signedDelta, this ) )
				BeginOrExtendHop( travelSign );
		}
		else
			BeginOrExtendHop( travelSign );

		return track.TryAdvance( _distanceAlongTrack, signedDelta, this, out nextPos );
	}

	void BeginOrExtendHop( int travelSign )
	{
		if ( travelSign == 0 )
			travelSign = 1;

		bool firstStart = !_hopping;
		if ( firstStart )
		{
			_hopping = true;
			_hopElapsed = 0f;
			_hopDescendElapsed = 0f;
			_hopArcDuration = HopDuration;
			_hopRemainingClear = 0f;
			_hopLiftY = 0f;
			_hopTravelSign = travelSign;
			PlayFeedback( onHopStartFeedbacks );
		}

		_hopTravelSign = travelSign;
		RefreshHopClearance();
		if ( firstStart )
			ApplyStaggeredHopVisuals();
	}

	void RefreshHopClearance()
	{
		MinecartInteractable blocker;
		float clearMeters;
		if ( !TryComputeHopClearance( _hopTravelSign, out blocker, out clearMeters ) )
			return;

		_hopBlocker = blocker;
		_hopRemainingClear = Mathf.Max( _hopRemainingClear, clearMeters );
		_hopHeight = ComputeHopHeight( blocker );
	}

	float ComputeHopHeight( MinecartInteractable blocker )
	{
		float scale = 1f + HopHeightPerExtraCar * FollowerCount;
		if ( blocker != null && BlockingHalfLength > 0.0001f )
			scale += HopBlockerScale * ( blocker.BlockingHalfLength / BlockingHalfLength );

		scale = Mathf.Clamp( scale, 1f, HopHeightScaleMax );
		return HopHeight * scale;
	}

	bool TryComputeHopClearance( int travelSign, out MinecartInteractable blocker, out float remainingClear )
	{
		blocker = null;
		remainingClear = 0f;
		if ( track == null || travelSign == 0 )
			return false;

		int faceAlong = _trackFacingSign >= 0 ? 1 : -1;
		int hitchAlong = -faceAlong;
		float behindExtent = BlockingHalfLength;
		if ( hitchAlong != travelSign )
			behindExtent += FollowerCount * ConsistSpacing;

		float best = float.MaxValue;
		MinecartInteractable bestBlocker = null;
		IReadOnlyList<MinecartInteractable> carts = ActiveCarts;
		for ( int i = 0; i < carts.Count; i++ )
		{
			MinecartInteractable other = carts[ i ];
			if ( other == null || other == this || other.BoundTrack != track )
				continue;

			if ( SharesConsistWith( other ) )
				continue;

			float sep = track.SignedAlong( _distanceAlongTrack, other.DistanceAlongTrack );
			if ( travelSign > 0 && sep <= 0.0001f )
				continue;
			if ( travelSign < 0 && sep >= -0.0001f )
				continue;

			float otherHalf = other.BlockingHalfLength;
			float target = other.DistanceAlongTrack + travelSign * ( otherHalf + behindExtent );
			float remaining = travelSign * track.SignedAlong( _distanceAlongTrack, target );
			if ( remaining <= 0.0001f )
				continue;

			if ( remaining < best )
			{
				best = remaining;
				bestBlocker = other;
			}
		}

		if ( bestBlocker == null )
			return false;

		blocker = bestBlocker;
		remainingClear = best;
		return true;
	}

	void TickHop( float dt )
	{
		if ( !_hopping )
			return;

		_hopElapsed += Mathf.Max( 0f, dt );

		// Chain-extend if a new/non-consist cart still lies ahead along hop travel.
		if ( !_junctionRiding )
			RefreshHopClearance();

		if ( _hopRemainingClear > 0.0001f )
			_hopDescendElapsed = 0f;
		else
			_hopDescendElapsed += Mathf.Max( 0f, dt );

		float halfArc = Mathf.Max( 0.0001f, _hopArcDuration * 0.5f );
		float descendDone = halfArc + HopStaggerDelay * FollowerCount;
		bool arcsDone = _hopRemainingClear <= 0.0001f && _hopDescendElapsed >= descendDone;
		if ( arcsDone )
			EndHop( playLandFeedback: true );
	}

	void EndHop( bool playLandFeedback )
	{
		if ( !_hopping && _hopLiftY <= 0.0001f )
		{
			_hopElapsed = 0f;
			_hopDescendElapsed = 0f;
			_hopRemainingClear = 0f;
			_hopBlocker = null;
			_hopLiftY = 0f;
			ApplyHopVisual( this, 0f );
			for ( int i = 0; i < _followers.Count; i++ )
				ApplyHopVisual( _followers[ i ], 0f );
			return;
		}

		bool wasHopping = _hopping;
		_hopping = false;
		_hopElapsed = 0f;
		_hopDescendElapsed = 0f;
		_hopRemainingClear = 0f;
		_hopBlocker = null;
		_hopLiftY = 0f;
		ApplyHopVisual( this, 0f );
		for ( int i = 0; i < _followers.Count; i++ )
			ApplyHopVisual( _followers[ i ], 0f );

		if ( wasHopping && playLandFeedback )
			PlayFeedback( onHopLandFeedbacks );
	}

	void TickHopVisual( float dt )
	{
		if ( !_hopping )
			return;

		ApplyStaggeredHopVisuals();
	}

	void ApplyStaggeredHopVisuals()
	{
		float arc = Mathf.Max( 0.0001f, _hopArcDuration );
		float stagger = HopStaggerDelay;
		_hopLiftY = EvaluateHopLift( 0, arc, stagger );
		ApplyHopVisual( this, _hopLiftY );
		for ( int i = 0; i < _followers.Count; i++ )
			ApplyHopVisual( _followers[ i ], EvaluateHopLift( i + 1, arc, stagger ) );
	}

	float EvaluateHopLift( int carIndex, float arcDuration, float staggerDelay )
	{
		float half = Mathf.Max( 0.0001f, arcDuration * 0.5f );
		float localAscend = _hopElapsed - carIndex * staggerDelay;
		if ( localAscend <= 0f )
			return 0f;

		// Ascend 0 → peak over the first half of the arc.
		if ( localAscend < half )
		{
			float t = Mathf.Clamp01( localAscend / half );
			return Mathf.Sin( t * Mathf.PI * 0.5f ) * _hopHeight;
		}

		// Hold peak until the consist has cleared blockers (chain-extend never touches ground).
		if ( _hopRemainingClear > 0.0001f )
			return _hopHeight;

		// Staggered descend after clearance.
		float localDescend = _hopDescendElapsed - carIndex * staggerDelay;
		if ( localDescend <= 0f )
			return _hopHeight;

		float d = Mathf.Clamp01( localDescend / half );
		return Mathf.Cos( d * Mathf.PI * 0.5f ) * _hopHeight;
	}

	static void ApplyHopVisual( MinecartInteractable cart, float lift )
	{
		if ( cart == null )
			return;

		if ( !cart._hopBaseCached )
			cart.CacheHopBaseLocalY();

		Transform visual = cart._visualRoot;
		if ( visual == null )
			visual = cart.transform.Find( "VisualRoot" );
		if ( visual != null )
		{
			Vector3 local = visual.localPosition;
			local.y = cart._visualBaseLocalY + lift;
			visual.localPosition = local;
		}

		Transform cargo = cart.cargoRoot;
		if ( cargo != null && cargo != cart.transform && ( visual == null || cargo != visual ) )
		{
			Vector3 cargoLocal = cargo.localPosition;
			cargoLocal.y = cart._cargoBaseLocalY + lift;
			cargo.localPosition = cargoLocal;
		}
	}

	void EnsureHopCoastSeed()
	{
		if ( !_hopping || _drivePowered || _recalling || _autoMoving )
			return;

		// Guarantee coast has a signed seed so TickCoast can accelerate to max.
		if ( Mathf.Abs( _coastSpeed ) >= StopSpeedEpsilon )
			return;

		if ( Mathf.Abs( _alongSpeed ) >= StopSpeedEpsilon )
			_coastSpeed = _alongSpeed;
		else
			_coastSpeed = _hopTravelSign * Mathf.Max( StopSpeedEpsilon * 4f, DriveMaxSpeed * 0.05f );
	}

	float MeasureTraveled( float from, float to )
	{
		float traveled = to - from;
		if ( track != null && track.IsClosed )
		{
			float length = track.Length;
			if ( traveled > length * 0.5f )
				traveled -= length;
			if ( traveled < -length * 0.5f )
				traveled += length;
		}

		return traveled;
	}

	public void SetJunctionSteer( float lateral )
	{
		// Require a clear A/D press — camera look / stick noise must not pick a branch.
		if ( Mathf.Abs( lateral ) < 0.35f )
		{
			ClearJunctionSteer();
			return;
		}

		_junctionSteer = lateral < 0f ? -1f : 1f;
		_junctionSteerValid = true;
	}

	public void ClearJunctionSteer()
	{
		_junctionSteerValid = false;
		_junctionSteer = 0f;
	}

	/// <summary>Legacy no-op — junctions use A/D relative to the cart, never camera look.</summary>
	public void SetJunctionLook( Vector3 flatLook )
	{
		ClearJunctionSteer();
	}

	public void ClearJunctionLook()
	{
		ClearJunctionSteer();
	}

	void ClearJunctionCommit()
	{
		bool abortingRide = _junctionRiding;
		_committedJunctionId = -1;
		_hasCommittedExit = false;
		_committedExitTrack = null;
		_committedExitDistance = 0f;
		_committedExitTravelSign = 1;
		ClearJunctionRide();
		if ( abortingRide )
			ClearConsistRideTrail();
	}

	void FinishJunctionHandoff( int junctionId, int exitTravelSign )
	{
		// Keep moving along the exit rail…
		RemapSpeedAfterJunctionTransfer( exitTravelSign );
		// …but W/S stay cart-local: +Z nose = _trackFacingSign after BindToTrack.
		// Latching to exitTravelSign flipped accel/reverse whenever nose opposed the exit sign.
		int noseAlongTrack = _trackFacingSign >= 0 ? 1 : -1;
		NotifyDriverJunctionTravelSign( noseAlongTrack );
		_suppressJunctionId = junctionId;
		_committedJunctionId = -1;
		_hasCommittedExit = false;
		_committedExitTrack = null;
		_committedExitDistance = 0f;
		_committedExitTravelSign = 1;
	}

	void NotifyDriverJunctionTravelSign( int exitTravelSign )
	{
		if ( GameMode.Instance == null || GameMode.Instance.Player == null )
			return;

		PlayerMinecartDrive drive = GameMode.Instance.Player.MinecartDrive;
		if ( drive == null || !drive.IsDriving )
			return;

		MinecartInteractable active = drive.ActiveCart;
		if ( active == null || !active.SharesConsistWith( this ) )
			return;

		drive.NotifyJunctionTravelSign( exitTravelSign );
	}

	void ClearJunctionRide()
	{
		_junctionRiding = false;
		_activeRidePath = null;
		_rideDistanceAlong = 0f;
		_rideFacingPolarity = 1;
	}

	bool TryBeginJunctionRide( int intoTravelSign )
	{
		MinecartJunctionGraph graph = MinecartJunctionGraph.FindActive();
		if ( graph == null || !_hasCommittedExit || _committedExitTrack == null )
			return false;

		if ( !_committedExitTrack.IsTravelReady )
			return false;

		float arrivalPort;
		if ( !graph.TryGetPortDistance( track, _committedJunctionId, _distanceAlongTrack, out arrivalPort ) )
			arrivalPort = _distanceAlongTrack;

		MinecartJunctionRidePath path;
		if ( !graph.TryFindRidePath( _committedJunctionId, track, arrivalPort, intoTravelSign, _committedExitTrack, _committedExitDistance, _committedExitTravelSign, out path )
		     && !graph.TryFindRidePath( _committedJunctionId, track, arrivalPort, -intoTravelSign, _committedExitTrack, _committedExitDistance, _committedExitTravelSign, out path ) )
			return false;

		if ( path.toTrack != null && !path.toTrack.IsTravelReady )
			return false;

		_junctionRiding = true;
		_activeRidePath = path;
		_rideDistanceAlong = 0f;
		_rideFacingPolarity = ResolvePathFacingPolarity( path, transform.forward );
		BeginConsistRideTrail( path, _rideFacingPolarity );

		// W/S = cart +Z (nose). Do not latch to along-track drive sign — that flips controls
		// when the nose is opposite the rail tangent.
		int noseAlongTrack = _trackFacingSign >= 0 ? 1 : -1;
		NotifyDriverJunctionTravelSign( noseAlongTrack );
		return true;
	}

	static int ResolvePathFacingPolarity( MinecartJunctionRidePath path, Vector3 currentFacing )
	{
		if ( path == null )
			return 1;

		Vector3 position;
		Vector3 pathTangent;
		if ( !path.TryEvaluate( 0f, out position, out pathTangent ) || pathTangent.sqrMagnitude < 0.0001f )
			return 1;

		currentFacing.y = 0f;
		pathTangent.y = 0f;
		if ( currentFacing.sqrMagnitude < 0.0001f || pathTangent.sqrMagnitude < 0.0001f )
			return 1;

		return Vector3.Dot( pathTangent.normalized, currentFacing.normalized ) >= 0f ? 1 : -1;
	}

	bool AdvanceJunctionRide( float signedDelta, out float traveled, out float leftover )
	{
		traveled = 0f;
		leftover = 0f;
		if ( !_junctionRiding || _activeRidePath == null )
			return false;

		float absDelta = Mathf.Abs( signedDelta );
		float pathLen = Mathf.Max( 0.0001f, _activeRidePath.length );
		float room = Mathf.Max( 0f, pathLen - _rideDistanceAlong );
		float step = Mathf.Min( absDelta, room );
		_rideDistanceAlong += step;
		traveled = step;
		_consistRideProgress = _rideDistanceAlong;

		if ( _rideDistanceAlong < pathLen - 0.001f )
		{
			leftover = 0f;
			return true;
		}

		MinecartTrack exitTrack = _activeRidePath.toTrack;
		float exitDistance = _activeRidePath.toDistance;
		int exitSign = _activeRidePath.outTravelSign;
		int handoffJunctionId = _committedJunctionId;
		Vector3 rideFacing = ResolveRideFacing( _activeRidePath, pathLen );
		_consistRideProgress = pathLen;
		_consistExitTravel = 0f;
		ClearJunctionRide();
		BindToTrack( exitTrack, exitDistance, rideFacing );
		FinishJunctionHandoff( handoffJunctionId, exitSign );
		float leftoverMag = Mathf.Max( 0f, absDelta - step );
		leftover = exitSign * leftoverMag;
		return true;
	}

	Vector3 ResolveRideFacing( MinecartJunctionRidePath path, float distanceAlong )
	{
		Vector3 facing = transform.forward;
		if ( path == null )
			return facing.sqrMagnitude > 0.0001f ? facing.normalized : Vector3.forward;

		Vector3 position;
		Vector3 pathTangent;
		if ( !path.TryEvaluate( distanceAlong, out position, out pathTangent ) || pathTangent.sqrMagnitude < 0.0001f )
			return facing.sqrMagnitude > 0.0001f ? facing.normalized : Vector3.forward;

		facing = pathTangent * _rideFacingPolarity;
		if ( facing.sqrMagnitude < 0.0001f )
			return transform.forward.sqrMagnitude > 0.0001f ? transform.forward.normalized : Vector3.forward;

		facing.Normalize();
		return facing;
	}

	void ApplyJunctionRidePose()
	{
		if ( _activeRidePath == null )
			return;

		Vector3 position;
		Vector3 pathTangent;
		if ( !_activeRidePath.TryEvaluate( _rideDistanceAlong, out position, out pathTangent ) )
			return;

		Vector3 facing = pathTangent.sqrMagnitude > 0.0001f
			? pathTangent * _rideFacingPolarity
			: transform.forward;
		if ( facing.sqrMagnitude < 0.0001f )
			facing = Vector3.forward;

		facing.Normalize();
		Vector3 up = Vector3.up;
		position += up * RideHeight;
		// Follow the junction path like a spline — polarity locked at ride start so S never spins the body.
		Quaternion rotation = Quaternion.LookRotation( facing, up );
		transform.SetPositionAndRotation( position, rotation );
		if ( _body != null )
		{
			_body.position = position;
			_body.rotation = rotation;
		}
	}

	void UpdateJunctionCommit( int travelSign )
	{
		MinecartJunctionGraph graph = MinecartJunctionGraph.FindActive();
		if ( graph == null || track == null )
		{
			ClearJunctionCommit();
			_suppressJunctionId = -1;
			return;
		}

		float approach = JunctionApproachRadius;
		if ( _suppressJunctionId >= 0 && graph.IsOutsideApproach( track, _distanceAlongTrack, _suppressJunctionId, approach ) )
			_suppressJunctionId = -1;

		if ( _committedJunctionId >= 0 && graph.IsOutsideApproach( track, _distanceAlongTrack, _committedJunctionId, approach ) )
			ClearJunctionCommit();

		int junctionIndex;
		float junctionDistance;
		if ( !graph.TryFindApproachJunction( track, _distanceAlongTrack, approach, out junctionIndex, out junctionDistance ) )
			return;

		// Just traversed this junction — ignore until we leave the approach zone.
		if ( junctionIndex == _suppressJunctionId )
			return;

		if ( _committedJunctionId == junctionIndex && _hasCommittedExit )
		{
			// Sticky latch: keep the last pick when A/D is released. Re-committing every
			// frame with no_steer was snapping steered branches back to through-continue
			// on the transfer frame. Still allow A/D to change the branch while held.
			if ( !_junctionRiding && _junctionSteerValid )
				CommitJunctionExit( graph, junctionIndex, travelSign );
			return;
		}

		CommitJunctionExit( graph, junctionIndex, travelSign );
	}

	void CommitJunctionExit( MinecartJunctionGraph graph, int junctionIndex, int travelSign )
	{
		int sign = travelSign >= 0 ? 1 : -1;
		graph.BuildExits( junctionIndex, track, _distanceAlongTrack, sign, JunctionExitBuffer );

		// Auto routes may need opposite-approach exits when bake intoTravelSign is skewed.
		// Player steer must not — those exits resolve to ride paths whose fromDistance is
		// behind travel, so TryPrepareJunctionTransfer never transfers.
		if ( _autoMoving )
		{
			graph.BuildExits( junctionIndex, track, _distanceAlongTrack, -sign, JunctionExitExtraBuffer );
			for ( int i = 0; i < JunctionExitExtraBuffer.Count; i++ )
			{
				MinecartJunctionExit exit = JunctionExitExtraBuffer[ i ];
				bool exists = false;
				for ( int j = 0; j < JunctionExitBuffer.Count; j++ )
				{
					MinecartJunctionExit existing = JunctionExitBuffer[ j ];
					if ( existing.track != exit.track )
						continue;
					if ( Mathf.Abs( existing.distance - exit.distance ) > 0.05f )
						continue;
					if ( existing.travelSign != exit.travelSign )
						continue;
					exists = true;
					break;
				}

				if ( !exists )
					JunctionExitBuffer.Add( exit );
			}
		}

		if ( JunctionExitBuffer.Count == 0 )
		{
			// Auto routes may pick a ridePath edge that BuildExits missed (distance/sign bake skew).
			// Still commit the forced exit so the cart can begin the ride instead of clamping mid-junction.
			if ( _autoMoving && _autoForcedExitValid && _autoForcedJunctionId == junctionIndex
			     && _autoForcedExitTrack != null && _autoForcedExitTrack.IsTravelReady )
			{
				_committedJunctionId = junctionIndex;
				_hasCommittedExit = true;
				_committedExitTrack = _autoForcedExitTrack;
				_committedExitDistance = _autoForcedExitDistance;
				_committedExitTravelSign = _autoForcedExitTravelSign >= 0 ? 1 : -1;
				return;
			}

			// No legal branch (disabled corners / through). Keep approach so we can clamp at the cut.
			_committedJunctionId = junctionIndex;
			_hasCommittedExit = false;
			_committedExitTrack = null;
			_committedExitDistance = 0f;
			_committedExitTravelSign = sign;
			return;
		}

		MinecartJunctionExit chosen = default;
		bool picked = _autoMoving && _autoForcedExitValid && _autoForcedJunctionId == junctionIndex
			&& TryPickForcedAutoExit( out chosen );
		if ( !picked && _autoMoving && _autoForcedExitValid && _autoForcedJunctionId == junctionIndex
		     && _autoForcedExitTrack != null && _autoForcedExitTrack.IsTravelReady )
		{
			chosen = new MinecartJunctionExit
			{
				track = _autoForcedExitTrack,
				distance = _autoForcedExitDistance,
				travelSign = _autoForcedExitTravelSign >= 0 ? 1 : -1
			};
			picked = true;
		}

		if ( !picked )
		{
			if ( !_junctionSteerValid )
			{
				if ( !TryFindContinueExit( graph, junctionIndex, track, _distanceAlongTrack, sign, out chosen )
				     && !TryPickTravelAlignedExit( graph, junctionIndex, sign, out chosen ) )
					chosen = JunctionExitBuffer[ 0 ];
			}
			else if ( !TryPickSteerExit( graph, junctionIndex, sign, _junctionSteer, out chosen ) )
			{
				if ( !TryFindContinueExit( graph, junctionIndex, track, _distanceAlongTrack, sign, out chosen )
				     && !TryPickTravelAlignedExit( graph, junctionIndex, sign, out chosen ) )
					chosen = JunctionExitBuffer[ 0 ];
			}
		}

		_committedJunctionId = junctionIndex;
		_hasCommittedExit = true;
		_committedExitTrack = chosen.track;
		_committedExitDistance = chosen.distance;
		_committedExitTravelSign = chosen.travelSign >= 0 ? 1 : -1;
	}

	bool TryPickForcedAutoExit( out MinecartJunctionExit chosen )
	{
		chosen = default;
		if ( !_autoForcedExitValid || _autoForcedExitTrack == null )
			return false;

		int best = -1;
		float bestSep = float.MaxValue;
		for ( int i = 0; i < JunctionExitBuffer.Count; i++ )
		{
			MinecartJunctionExit exit = JunctionExitBuffer[ i ];
			if ( exit.track != _autoForcedExitTrack )
				continue;

			if ( exit.travelSign != _autoForcedExitTravelSign && exit.travelSign != -_autoForcedExitTravelSign )
			{
				// Still accept matching track with either sign if distances match.
			}

			float sep = exit.track != null
				? Mathf.Abs( exit.track.SignedAlong( exit.distance, _autoForcedExitDistance ) )
				: float.MaxValue;
			if ( sep >= bestSep )
				continue;

			bestSep = sep;
			best = i;
		}

		if ( best < 0 )
			return false;

		chosen = JunctionExitBuffer[ best ];
		return true;
	}

	bool TryPickSteerExit( MinecartJunctionGraph graph, int junctionIndex, int travelSign, float steerSide, out MinecartJunctionExit chosen )
	{
		chosen = default;
		// A/D are relative to the minecart body only — never camera / look direction.
		Vector3 cartRight = transform.right;
		cartRight.y = 0f;
		if ( cartRight.sqrMagnitude < 0.0001f )
			return false;

		cartRight.Normalize();
		float side = steerSide < 0f ? -1f : 1f;
		Vector3 desired = cartRight * side;

		Vector3 travel;
		Vector3 up;
		Vector3 position;
		if ( !track.Evaluate( _distanceAlongTrack, out position, out travel, out up ) )
			travel = transform.forward;
		travel.y = 0f;
		if ( travel.sqrMagnitude > 0.0001f )
			travel = travel.normalized * ( travelSign >= 0 ? 1f : -1f );
		else
			travel = transform.forward;

		float arrivalPort = _distanceAlongTrack;
		if ( graph != null )
			graph.TryGetPortDistance( track, junctionIndex, _distanceAlongTrack, out arrivalPort );

		int bestIndex = -1;
		float bestScore = float.NegativeInfinity;
		float continueScore = float.NegativeInfinity;
		bool hasContinue = false;
		for ( int i = 0; i < JunctionExitBuffer.Count; i++ )
		{
			MinecartJunctionExit exit = JunctionExitBuffer[ i ];
			Vector3 leave = ResolveExitLeaveDirection( graph, junctionIndex, arrivalPort, travelSign, exit, travel );
			leave.y = 0f;
			if ( leave.sqrMagnitude < 0.0001f )
				continue;

			float score = Vector3.Dot( desired, leave.normalized );
			bool isContinue = exit.track == track
				&& graph != null
				&& graph.IsSamePortExit( track, junctionIndex, _distanceAlongTrack, exit.distance )
				&& exit.travelSign == ( travelSign >= 0 ? 1 : -1 );
			if ( isContinue )
			{
				hasContinue = true;
				if ( score > continueScore )
					continueScore = score;
			}

			if ( score <= bestScore )
				continue;

			bestScore = score;
			bestIndex = i;
		}

		if ( bestIndex < 0 )
			return false;

		// Prefer any exit that clearly leans toward the steered cart side over through/continue.
		float minAlign = JunctionSteerMinAlign;
		if ( hasContinue && bestScore > continueScore + 0.05f )
			minAlign = Mathf.Min( minAlign, 0.02f );

		if ( bestScore < minAlign )
			return false;

		chosen = JunctionExitBuffer[ bestIndex ];
		return true;
	}

	/// <summary>
	/// When A/D is not held (or steer pick fails), prefer the exit most aligned with travel —
	/// never camera look.
	/// </summary>
	bool TryPickTravelAlignedExit( MinecartJunctionGraph graph, int junctionIndex, int travelSign, out MinecartJunctionExit chosen )
	{
		chosen = default;
		Vector3 travel;
		Vector3 up;
		Vector3 position;
		if ( !track.Evaluate( _distanceAlongTrack, out position, out travel, out up ) )
			return false;

		travel.y = 0f;
		if ( travel.sqrMagnitude < 0.0001f )
			return false;

		travel = travel.normalized * ( travelSign >= 0 ? 1f : -1f );

		float arrivalPort = _distanceAlongTrack;
		if ( graph != null )
			graph.TryGetPortDistance( track, junctionIndex, _distanceAlongTrack, out arrivalPort );

		int bestIndex = -1;
		float bestScore = float.NegativeInfinity;
		for ( int i = 0; i < JunctionExitBuffer.Count; i++ )
		{
			MinecartJunctionExit exit = JunctionExitBuffer[ i ];
			Vector3 leave = ResolveExitLeaveDirection( graph, junctionIndex, arrivalPort, travelSign, exit, travel );
			leave.y = 0f;
			if ( leave.sqrMagnitude < 0.0001f )
				continue;

			float score = Vector3.Dot( travel, leave.normalized );
			if ( score <= bestScore )
				continue;

			bestScore = score;
			bestIndex = i;
		}

		if ( bestIndex < 0 )
			return false;

		chosen = JunctionExitBuffer[ bestIndex ];
		return true;
	}

	Vector3 ResolveExitLeaveDirection(
		MinecartJunctionGraph graph,
		int junctionIndex,
		float arrivalPort,
		int travelSign,
		MinecartJunctionExit exit,
		Vector3 travelFallback )
	{
		Vector3 leave = exit.worldTangent;
		if ( graph == null || exit.track == null )
			return leave.sqrMagnitude > 0.0001f ? leave : travelFallback;

		// Same-port through stays on the spline — use port tangent.
		if ( exit.track == track && graph.IsSamePortExit( track, junctionIndex, _distanceAlongTrack, exit.distance ) )
			return leave.sqrMagnitude > 0.0001f ? leave : travelFallback;

		MinecartJunctionRidePath path;
		if ( !graph.TryFindRidePath( junctionIndex, track, arrivalPort, travelSign, exit.track, exit.distance, exit.travelSign, out path )
		     && !graph.TryFindRidePath( junctionIndex, track, arrivalPort, -travelSign, exit.track, exit.distance, exit.travelSign, out path ) )
			return leave.sqrMagnitude > 0.0001f ? leave : travelFallback;

		if ( path == null || path.worldPoints == null || path.worldPoints.Count < 2 )
			return leave.sqrMagnitude > 0.0001f ? leave : travelFallback;

		// Overall bend of the ride (arrival → exit) matches A/D intent better than the exit-port tangent alone.
		Vector3 chord = path.worldPoints[ path.worldPoints.Count - 1 ] - path.worldPoints[ 0 ];
		chord.y = 0f;
		if ( chord.sqrMagnitude > 0.0001f )
			return chord;

		Vector3 midPos;
		Vector3 midTan;
		float mid = Mathf.Max( 0.0001f, path.length ) * 0.35f;
		if ( path.TryEvaluate( mid, out midPos, out midTan ) && midTan.sqrMagnitude > 0.0001f )
			return midTan;

		return leave.sqrMagnitude > 0.0001f ? leave : travelFallback;
	}

	static bool TryFindContinueExit( MinecartJunctionGraph graph, int junctionIndex, MinecartTrack currentTrack, float arrivalDistanceAlong, int travelSign, out MinecartJunctionExit exit )
	{
		exit = default;
		int sign = travelSign >= 0 ? 1 : -1;
		float arrivalPort = arrivalDistanceAlong;
		if ( graph != null )
			graph.TryGetPortDistance( currentTrack, junctionIndex, arrivalDistanceAlong, out arrivalPort );

		MinecartJunctionExit fallback = default;
		bool hasFallback = false;
		for ( int i = 0; i < JunctionExitBuffer.Count; i++ )
		{
			MinecartJunctionExit candidate = JunctionExitBuffer[ i ];
			if ( candidate.track != currentTrack )
				continue;

			if ( candidate.travelSign != sign )
				continue;

			if ( currentTrack != null
			     && Mathf.Abs( currentTrack.SignedAlong( arrivalPort, candidate.distance ) ) <= 0.4f )
			{
				exit = candidate;
				return true;
			}

			if ( !hasFallback )
			{
				fallback = candidate;
				hasFallback = true;
			}
		}

		if ( !hasFallback )
			return false;

		exit = fallback;
		return true;
	}

	bool TryPrepareJunctionTransfer( int travelSign, ref float step, out bool transferred, out float leftoverAfterTransfer, out float junctionDistance )
	{
		transferred = false;
		leftoverAfterTransfer = 0f;
		junctionDistance = 0f;
		if ( !_hasCommittedExit || _committedExitTrack == null )
			return false;

		MinecartJunctionGraph graph = MinecartJunctionGraph.FindActive();
		if ( graph == null )
			return false;

		// Same-port through continue stays on the spline — no ride transfer.
		if ( _committedExitTrack == track
		     && graph.IsSamePortExit( track, _committedJunctionId, _distanceAlongTrack, _committedExitDistance ) )
			return false;

		float arrivalPortDistance;
		if ( !graph.TryGetPortDistance( track, _committedJunctionId, _distanceAlongTrack, out arrivalPortDistance ) )
			arrivalPortDistance = _distanceAlongTrack;

		MinecartJunctionRidePath ridePath;
		if ( !graph.TryFindRidePath( _committedJunctionId, track, arrivalPortDistance, travelSign, _committedExitTrack, _committedExitDistance, _committedExitTravelSign, out ridePath )
		     && !graph.TryFindRidePath( _committedJunctionId, track, arrivalPortDistance, -travelSign, _committedExitTrack, _committedExitDistance, _committedExitTravelSign, out ridePath ) )
			return false;

		junctionDistance = ridePath.fromDistance;

		float toJunction = track.SignedAlong( _distanceAlongTrack, junctionDistance );
		float absToJunction = Mathf.Abs( toJunction );
		// Auto carts often arrive exactly on the leg target (sep≈0). Still allow the transfer
		// when we are on top of the ride entry instead of requiring a remaining approach delta.
		if ( _autoMoving && _autoForcedExitValid && absToJunction <= 0.05f )
		{
			step = 0f;
			leftoverAfterTransfer = 0f;
			transferred = true;
			return true;
		}

		// Approach is measured to the port center, but ride entry is the bake cut (~2.5m
		// out). Carts often enter the approach zone already slightly past the cut — snap on.
		const float SnapPastEpsilon = 1.25f;
		bool ahead = toJunction * travelSign > 0.00001f;
		bool snapPast = !ahead && absToJunction <= SnapPastEpsilon;
		if ( !ahead && !snapPast )
			return false;

		if ( snapPast )
		{
			float absStep = Mathf.Abs( step );
			step = 0f;
			leftoverAfterTransfer = travelSign * absStep;
			transferred = true;
			return true;
		}

		float absRemaining = Mathf.Abs( step );
		if ( absToJunction > absRemaining + 0.0001f )
			return false;

		step = toJunction;
		float leftoverMag = Mathf.Max( 0f, absRemaining - absToJunction );
		// Keep arrival travel sign through the ride; AdvanceJunctionRide remaps leftover to exit sign on handoff.
		leftoverAfterTransfer = travelSign * leftoverMag;
		transferred = true;
		return true;
	}

	/// <summary>
	/// When no legal exit is committed (disabled through / corners), stop at the junction cut instead of rolling across.
	/// </summary>
	bool TryClampAtBlockedThrough( int travelSign, ref float remaining, out float traveled )
	{
		traveled = 0f;
		if ( _hasCommittedExit || _committedJunctionId < 0 || track == null )
			return false;

		MinecartJunctionGraph graph = MinecartJunctionGraph.FindActive();
		if ( graph == null )
			return false;

		float junctionDistance;
		if ( !graph.TryGetPortDistance( track, _committedJunctionId, _distanceAlongTrack, out junctionDistance ) )
			return false;

		// If same-track through is allowed, do not clamp — normal advance continues.
		if ( graph.IsSameTrackThroughAllowed( _committedJunctionId, track ) )
			return false;

		float toJunction = track.SignedAlong( _distanceAlongTrack, junctionDistance );
		if ( toJunction * travelSign <= 0.00001f )
			return false;

		float absRemaining = Mathf.Abs( remaining );
		float absToJunction = Mathf.Abs( toJunction );
		float step = absToJunction <= absRemaining + 0.0001f ? toJunction : remaining;
		float next;
		if ( !track.TryAdvance( _distanceAlongTrack, step, this, out next ) )
			return true;

		traveled = MeasureTraveled( _distanceAlongTrack, next );
		_distanceAlongTrack = next;
		remaining = 0f;
		return true;
	}

	void RemapSpeedAfterJunctionTransfer( int exitTravelSign )
	{
		int sign = exitTravelSign >= 0 ? 1 : -1;
		float mag = Mathf.Max( Mathf.Abs( _driveSpeed ), Mathf.Abs( _coastSpeed ) );
		if ( mag < StopSpeedEpsilon )
			mag = Mathf.Abs( _alongSpeed );
		if ( mag < StopSpeedEpsilon )
			return;

		if ( Mathf.Abs( _driveSpeed ) > StopSpeedEpsilon || _drivePowered )
			_driveSpeed = sign * mag;
		if ( Mathf.Abs( _coastSpeed ) > StopSpeedEpsilon )
			_coastSpeed = sign * mag;
		_alongSpeed = sign * mag;
	}

	float ConsistRiderMotionScale()
	{
		float scale = RiderMotionScale;
		for ( int i = 0; i < _followers.Count; i++ )
		{
			MinecartInteractable follower = _followers[ i ];
			if ( follower == null )
				continue;

			float other = follower.RiderMotionScale;
			if ( other > scale )
				scale = other;
		}

		return scale;
	}

	public bool TryShove( Vector3 planarFacing )
	{
		MinecartInteractable lead = ConsistLead;
		if ( lead != this )
			return lead.TryShove( planarFacing );

		if ( RejectsPlayerPush )
			return false;

		planarFacing.y = 0f;
		if ( planarFacing.sqrMagnitude < 0.0001f )
			return false;

		planarFacing.Normalize();
		Vector3 tangent;
		if ( !TryGetTrackTangent( out tangent ) )
			return false;

		float align = Vector3.Dot( planarFacing, tangent );
		if ( Mathf.Abs( align ) < 0.2f )
			return false;

		CancelRecall( arrived: false );
		_drivePowered = false;
		_coastSpeed = Mathf.Sign( align ) * ShoveSpeed;
		_alongSpeed = _coastSpeed;
		PlayFeedback( onShoveFeedbacks );
		return true;
	}

	public void SetDriveSpeed( float signedSpeed, bool powered )
	{
		MinecartInteractable lead = ConsistLead;
		if ( lead != this )
		{
			lead.SetDriveSpeed( signedSpeed, powered );
			return;
		}

		bool wasPowered = _drivePowered;
		if ( powered )
		{
			CancelRecall( arrived: false );
			CancelAutoMove( arrived: false );
			_drivePowered = true;
			_driveSpeed = signedSpeed;
			_coastSpeed = 0f;
			return;
		}

		// Unpowered: seed coast once on throttle release, then let TickCoast decay.
		// Re-seeding every frame from a stale drive speed prevented coast-to-stop.
		_drivePowered = false;
		if ( wasPowered )
		{
			float seed = Mathf.Abs( signedSpeed ) >= StopSpeedEpsilon ? signedSpeed : _driveSpeed;
			_coastSpeed = Mathf.Abs( seed ) >= StopSpeedEpsilon ? seed : 0f;
			_driveSpeed = 0f;
			return;
		}

		_driveSpeed = 0f;
		if ( Mathf.Abs( signedSpeed ) < StopSpeedEpsilon && Mathf.Abs( _coastSpeed ) < StopSpeedEpsilon )
			_coastSpeed = 0f;
	}

	public bool BeginRecall( float targetDistance, MinecartCallPost post )
	{
		MinecartInteractable lead = ConsistLead;
		if ( lead != this )
			return lead.BeginRecall( targetDistance, post );

		if ( !EnsureBoundTrack() )
			return false;

		CancelAutoMove( arrived: false );
		_recalling = true;
		_recallDistance = targetDistance;
		_recallPost = post;
		_holdPush = false;
		_drivePowered = false;
		_coastSpeed = 0f;
		return true;
	}

	public bool BeginAutoMove( float targetDistance, MinecartAutoController owner )
	{
		MinecartTrack targetTrack = owner != null && owner.TargetStation != null
			? owner.TargetStation.BoundTrack
			: track;
		return BeginAutoMove( targetTrack, targetDistance, owner, null );
	}

	public bool BeginAutoMove(
		MinecartTrack targetTrack,
		float targetDistance,
		MinecartAutoController owner,
		List<MinecartNetworkPathfinder.RouteLeg> route )
	{
		MinecartInteractable lead = ConsistLead;
		if ( lead != this )
			return lead.BeginAutoMove( targetTrack, targetDistance, owner, route );

		if ( !EnsureBoundTrack() || !IsAutoEligible || targetTrack == null )
			return false;

		CancelRecall( arrived: false );
		_autoMoving = true;
		_autoTargetTrack = targetTrack;
		_autoTargetDistance = targetDistance;
		_autoOwner = owner;
		_holdPush = false;
		_drivePowered = false;
		_coastSpeed = 0f;
		_autoRoute.Clear();
		_autoRouteIndex = 0;
		ClearAutoForcedExit();

		if ( route != null && route.Count > 0 )
		{
			_autoRoute.AddRange( route );
		}
		else if ( !MinecartNetworkPathfinder.TryFindRoute( track, _distanceAlongTrack, targetTrack, targetDistance, _autoRoute, out _ ) )
		{
			if ( track != targetTrack )
			{
				_autoMoving = false;
				_autoOwner = null;
				_autoTargetTrack = null;
				return false;
			}

			float sep = track.SignedAlong( _distanceAlongTrack, targetDistance );
			_autoRoute.Add( new MinecartNetworkPathfinder.RouteLeg
			{
				Track = track,
				TargetDistance = targetDistance,
				TravelSign = sep >= 0f ? 1 : -1,
				JunctionIndex = -1
			} );
		}

		return true;
	}

	public void CancelAutoMove( bool arrived )
	{
		if ( !IsConsistLead )
		{
			if ( consistLead != null )
				consistLead.CancelAutoMove( arrived );
			return;
		}

		bool was = _autoMoving;
		MinecartAutoController owner = _autoOwner;
		_autoMoving = false;
		_autoOwner = null;
		_autoTargetTrack = null;
		_autoRoute.Clear();
		_autoRouteIndex = 0;
		ClearAutoForcedExit();
		if ( !was || owner == null )
			return;

		if ( arrived )
			owner.NotifyArrived( this );
		else
			owner.NotifyTravelCancelled( this );
	}

	/// <summary>
	/// Stop auto travel without cancelling the inbound reservation; used when the player loads cargo mid-route.
	/// </summary>
	public void PauseAutoMove()
	{
		if ( !IsConsistLead )
		{
			if ( consistLead != null )
				consistLead.PauseAutoMove();
			return;
		}

		_autoMoving = false;
		_autoOwner = null;
		_autoTargetTrack = null;
		_autoRoute.Clear();
		_autoRouteIndex = 0;
		ClearAutoForcedExit();
		_alongSpeed = 0f;
		_coastSpeed = 0f;
	}

	void ClearAutoForcedExit()
	{
		_autoForcedExitValid = false;
		_autoForcedJunctionId = -1;
		_autoForcedExitTrack = null;
		_autoForcedExitDistance = 0f;
		_autoForcedExitTravelSign = 1;
	}

	void ApplyAutoRouteForcedExit( MinecartNetworkPathfinder.RouteLeg leg )
	{
		if ( !leg.TransfersAtJunction )
		{
			ClearAutoForcedExit();
			return;
		}

		_autoForcedExitValid = true;
		_autoForcedJunctionId = leg.JunctionIndex;
		_autoForcedExitTrack = leg.ExitTrack;
		_autoForcedExitDistance = leg.ExitDistance;
		_autoForcedExitTravelSign = leg.ExitTravelSign >= 0 ? 1 : -1;
	}

	public void CancelRecall( bool arrived )
	{
		if ( !IsConsistLead )
		{
			if ( consistLead != null )
				consistLead.CancelRecall( arrived );
			return;
		}

		bool was = _recalling;
		MinecartCallPost post = _recallPost;
		_recalling = false;
		_recallPost = null;
		if ( !was || post == null )
			return;

		if ( arrived )
			post.NotifyCartArrived( this );
		else
			post.NotifyRecallCancelled( this );
	}

	public bool TryAttachFollower( MinecartInteractable follower )
	{
		if ( follower == null || follower == this )
			return false;

		MinecartInteractable lead = ConsistLead;
		if ( lead != this )
			return lead.TryAttachFollower( follower );

		if ( !Application.isPlaying )
			RebuildAuthoredConsist();

		if ( !EnsureBoundTrack() )
			return false;

		follower.DetachFromConsist();
		follower.consistLead = this;
		follower.CancelAutoMove( arrived: false );
		follower.StopMoveLoop();
		if ( !_followers.Contains( follower ) )
			_followers.Add( follower );

		RegisterAuthoredFollower( follower );
		SnapFollowers();
		return true;
	}

	public bool TryDetachLastFollower( out MinecartInteractable follower )
	{
		follower = null;
		MinecartInteractable lead = ConsistLead;
		if ( lead != this )
			return lead.TryDetachLastFollower( out follower );

		if ( !Application.isPlaying )
			RebuildAuthoredConsist();

		if ( _followers.Count <= 0 )
			return false;

		int last = _followers.Count - 1;
		follower = _followers[ last ];
		_followers.RemoveAt( last );
		if ( authoredFollowers != null && follower != null )
			authoredFollowers.Remove( follower );

		if ( follower != null )
		{
			follower.consistLead = null;
			follower.TryStartMoveLoop();
		}

		return follower != null;
	}

	public bool TryRemoveLastFollower()
	{
		MinecartInteractable follower;
		if ( !TryDetachLastFollower( out follower ) )
			return false;

		if ( follower != null )
		{
			if ( Application.isPlaying )
				Destroy( follower.gameObject );
			else
				DestroyImmediate( follower.gameObject );
		}

		return true;
	}

	public void DetachFromConsist()
	{
		if ( consistLead != null )
		{
			MinecartInteractable lead = consistLead;
			consistLead = null;
			lead._followers.Remove( this );
			if ( lead.authoredFollowers != null )
				lead.authoredFollowers.Remove( this );
			TryStartMoveLoop();
			return;
		}

		for ( int i = _followers.Count - 1; i >= 0; i-- )
		{
			MinecartInteractable follower = _followers[ i ];
			if ( follower != null )
			{
				follower.consistLead = null;
				follower.TryStartMoveLoop();
			}
		}

		_followers.Clear();
	}

	public void BindToTrack( MinecartTrack nextTrack, float distance )
	{
		BindToTrack( nextTrack, distance, transform.forward );
	}

	public void BindToTrack( MinecartTrack nextTrack, float distance, Vector3 preferredFacing )
	{
		track = nextTrack;
		_distanceAlongTrack = nextTrack != null ? nextTrack.WrapDistance( distance ) : distance;
		_bound = nextTrack != null && nextTrack.IsTravelReady;
		if ( _bound )
		{
			SyncTrackFacingSign( preferredFacing );
			ApplyTrackPose( runtime: Application.isPlaying );
		}
	}

	void SyncTrackFacingSign( Vector3 preferredFacing )
	{
		_trackFacingSign = 1;
		if ( track == null )
			return;

		Vector3 position;
		Vector3 tangent;
		Vector3 up;
		if ( !track.Evaluate( _distanceAlongTrack, out position, out tangent, out up ) || tangent.sqrMagnitude < 0.0001f )
			return;

		preferredFacing.y = 0f;
		tangent.y = 0f;
		if ( preferredFacing.sqrMagnitude < 0.0001f || tangent.sqrMagnitude < 0.0001f )
			return;

		_trackFacingSign = Vector3.Dot( tangent.normalized, preferredFacing.normalized ) >= 0f ? 1 : -1;
	}

	public Vector3 ResolveSeatWorldPosition()
	{
		float lift = HopLiftY;
		if ( seat != null )
			return seat.position + Vector3.up * lift;

		return transform.position + transform.up * ( RideHeight + 0.55f ) + Vector3.up * lift;
	}

	public Quaternion ResolveSeatWorldRotation()
	{
		if ( seat != null )
			return seat.rotation;

		return transform.rotation;
	}

	public void NotifyCargoPlaced( bool fromPlayer = false )
	{
		PlayFeedback( onLoadFeedbacks );
		if ( !fromPlayer )
			return;

		MinecartAutoController auto = ConsistLead.GetComponent<MinecartAutoController>();
		if ( auto != null )
			auto.NotifyPlayerCargoAdded();
	}

	void TickRecall( float dt )
	{
		if ( !_recalling || _holdPush || _drivePowered )
			return;

		if ( !EnsureBoundTrack() )
		{
			CancelRecall( arrived: false );
			return;
		}

		float sep = track.SignedAlong( _distanceAlongTrack, _recallDistance );
		if ( Mathf.Abs( sep ) <= RecallStopDistance )
		{
			_alongSpeed = 0f;
			CancelRecall( arrived: true );
			return;
		}

		float dir = Mathf.Sign( sep );
		float speed = RecallSpeed;
		if ( _hopping )
		{
			dir = _hopTravelSign;
			speed = DriveMaxSpeed;
		}

		float step = dir * speed * dt;
		if ( !_hopping && Mathf.Abs( step ) > Mathf.Abs( sep ) )
			step = sep;

		_alongSpeed = dir * speed;
		if ( !TryPushAlong( step ) )
			_alongSpeed = 0f;
	}

	void TickAutoMove( float dt )
	{
		if ( !_autoMoving || _holdPush || _drivePowered || _recalling )
			return;

		if ( !EnsureBoundTrack() || !IsAutoEligible || _autoTargetTrack == null )
		{
			CancelAutoMove( arrived: false );
			return;
		}

		AdvanceAutoRouteAfterJunction();

		if ( track == _autoTargetTrack )
		{
			float goalSep = track.SignedAlong( _distanceAlongTrack, _autoTargetDistance );
			if ( Mathf.Abs( goalSep ) <= RecallStopDistance )
			{
				_alongSpeed = 0f;
				CancelAutoMove( arrived: true );
				return;
			}
		}

		if ( _autoRouteIndex < 0 || _autoRouteIndex >= _autoRoute.Count )
		{
			// Route exhausted but not at goal — try repath once.
			_autoRoute.Clear();
			if ( !MinecartNetworkPathfinder.TryFindRoute( track, _distanceAlongTrack, _autoTargetTrack, _autoTargetDistance, _autoRoute, out _ )
			     || _autoRoute.Count == 0 )
			{
				CancelAutoMove( arrived: false );
				return;
			}

			_autoRouteIndex = 0;
		}

		MinecartNetworkPathfinder.RouteLeg leg = _autoRoute[ _autoRouteIndex ];
		ApplyAutoRouteForcedExit( leg );

		float moveSpeed = _hopping ? DriveMaxSpeed : RecallSpeed;
		int hopDir = _hopTravelSign;

		// Already on a junction ride — keep advancing along the curve until handoff.
		if ( _junctionRiding )
		{
			int rideSign = _hopping ? hopDir : ( leg.TravelSign >= 0 ? 1 : -1 );
			float rideStep = rideSign * moveSpeed * dt;
			_alongSpeed = rideSign * moveSpeed;
			if ( !TryPushAlong( rideStep ) )
				_alongSpeed = 0f;
			return;
		}

		float targetDistance = leg.TargetDistance;
		if ( track == _autoTargetTrack && _autoRouteIndex >= _autoRoute.Count - 1 )
			targetDistance = _autoTargetDistance;

		bool transferLeg = leg.TransfersAtJunction && track == leg.Track;
		float sep = track != null ? track.SignedAlong( _distanceAlongTrack, targetDistance ) : 0f;
		int travelSign = Mathf.Abs( sep ) > 0.0001f ? ( sep >= 0f ? 1 : -1 ) : ( leg.TravelSign >= 0 ? 1 : -1 );
		if ( _hopping )
			travelSign = hopDir;

		if ( transferLeg )
			EnsureAutoForcedJunctionCommit( leg, travelSign );

		// At (or past) the junction entry on a transfer leg — begin the ride instead of idling at sep≈0.
		if ( transferLeg && Mathf.Abs( sep ) <= Mathf.Max( 0.15f, RecallStopDistance ) )
		{
			if ( !_hasCommittedExit || _committedExitTrack == null
			     || ( _autoForcedExitTrack != null && _committedExitTrack != _autoForcedExitTrack ) )
				EnsureAutoForcedJunctionCommit( leg, travelSign );

			if ( TryBeginJunctionRide( travelSign ) || TryBeginJunctionRide( -travelSign ) )
			{
				float rideStep = travelSign * moveSpeed * dt;
				_alongSpeed = travelSign * moveSpeed;
				if ( !TryPushAlong( rideStep ) )
					_alongSpeed = 0f;
				return;
			}
		}

		float step = travelSign * moveSpeed * dt;
		// On transfer legs, never clamp the final approach to a zero step — that freezes the cart
		// mid-junction before TryPrepareJunctionTransfer / ride begin can run.
		if ( !_hopping && !transferLeg && Mathf.Abs( step ) > Mathf.Abs( sep ) && track == leg.Track )
			step = sep;
		else if ( !_hopping && transferLeg && Mathf.Abs( sep ) > 0.0001f && Mathf.Abs( step ) > Mathf.Abs( sep ) )
			step = sep;

		_alongSpeed = travelSign * moveSpeed;
		if ( !TryPushAlong( step ) )
			_alongSpeed = 0f;
	}

	void EnsureAutoForcedJunctionCommit( MinecartNetworkPathfinder.RouteLeg leg, int travelSign )
	{
		if ( !leg.TransfersAtJunction || leg.ExitTrack == null || track == null )
			return;

		if ( !leg.ExitTrack.IsTravelReady )
			return;

		_autoForcedExitValid = true;
		_autoForcedJunctionId = leg.JunctionIndex;
		_autoForcedExitTrack = leg.ExitTrack;
		_autoForcedExitDistance = leg.ExitDistance;
		_autoForcedExitTravelSign = leg.ExitTravelSign >= 0 ? 1 : -1;

		_committedJunctionId = leg.JunctionIndex;
		_hasCommittedExit = true;
		_committedExitTrack = leg.ExitTrack;
		_committedExitDistance = leg.ExitDistance;
		_committedExitTravelSign = leg.ExitTravelSign >= 0 ? 1 : -1;
		_suppressJunctionId = -1;

		// Also refresh via normal commit so BuildExits-picked geometry can refine distance/sign.
		UpdateJunctionCommit( travelSign );
		if ( !_hasCommittedExit || _committedExitTrack != leg.ExitTrack )
		{
			_committedJunctionId = leg.JunctionIndex;
			_hasCommittedExit = true;
			_committedExitTrack = leg.ExitTrack;
			_committedExitDistance = leg.ExitDistance;
			_committedExitTravelSign = leg.ExitTravelSign >= 0 ? 1 : -1;
		}
	}

	void AdvanceAutoRouteAfterJunction()
	{
		if ( _autoRouteIndex < 0 || _autoRouteIndex >= _autoRoute.Count )
			return;

		MinecartNetworkPathfinder.RouteLeg leg = _autoRoute[ _autoRouteIndex ];
		if ( !leg.TransfersAtJunction || leg.ExitTrack == null )
			return;

		if ( track != leg.ExitTrack )
			return;

		_autoRouteIndex++;
		ClearAutoForcedExit();
	}

	void TickDrive( float dt )
	{
		if ( !_drivePowered || _holdPush || _recalling || _autoMoving )
			return;

		if ( _hopping )
		{
			float target = _hopTravelSign * DriveMaxSpeed;
			_driveSpeed = Mathf.MoveTowards( _driveSpeed, target, DriveAcceleration * Mathf.Max( 0f, dt ) );
			_driveBraking = false;
		}

		_alongSpeed = _driveSpeed;
		if ( Mathf.Abs( _driveSpeed ) < StopSpeedEpsilon )
			return;

		if ( !TryPushAlong( _driveSpeed * dt ) )
		{
			// Blocked this frame (end of track, junction clamp, etc.) — keep commanded
			// drive speed so reverse/forward can recover at full strength next frame.
			_alongSpeed = 0f;
		}
	}

	void TickCoast( float dt )
	{
		if ( _recalling || _autoMoving || _drivePowered )
			return;

		// Hold-push owns motion unless mid-hop — hop cannot stall on a cart.
		if ( _holdPush && !_hopping )
			return;

		if ( _hopping )
		{
			float target = _hopTravelSign * DriveMaxSpeed;
			float seed = Mathf.Abs( _coastSpeed ) >= StopSpeedEpsilon ? _coastSpeed : _alongSpeed;
			_coastSpeed = Mathf.MoveTowards( seed, target, DriveAcceleration * Mathf.Max( 0f, dt ) );
			_alongSpeed = _coastSpeed;
			if ( Mathf.Abs( _coastSpeed ) < StopSpeedEpsilon )
				return;

			if ( !TryPushAlong( _coastSpeed * dt ) )
				_alongSpeed = 0f;
			return;
		}

		if ( Mathf.Abs( _coastSpeed ) < StopSpeedEpsilon )
		{
			_coastSpeed = 0f;
			_alongSpeed = 0f;
			return;
		}

		bool moved = TryPushAlong( _coastSpeed * dt );
		if ( !moved )
		{
			_coastSpeed = 0f;
			_alongSpeed = 0f;
			return;
		}

		float drag = IsDriveCart ? DriveBrake : ShoveDrag;
		_coastSpeed = Mathf.MoveTowards( _coastSpeed, 0f, drag * dt );
		_alongSpeed = _coastSpeed;
	}

	void TickStopFeedback()
	{
		float speed = Mathf.Abs( _alongSpeed );
		bool moving = speed >= StopSpeedEpsilon;
		if ( _wasMoving && !moving && Time.time >= _stopFeedbackReadyTime )
			PlayFeedback( onStopFeedbacks );

		if ( moving )
			_stopFeedbackReadyTime = Time.time + StopFeedbackDebounce;

		_wasMoving = moving;
	}

	void SnapFollowers()
	{
		if ( _followers.Count <= 0 )
			return;

		float spacing = ConsistSpacing;
		for ( int i = 0; i < _followers.Count; i++ )
		{
			MinecartInteractable follower = _followers[ i ];
			if ( follower == null )
				continue;

			// Hitch distance behind the lead's rear (opposite nose) — same side always, like a physical trailer.
			PoseFollowerAtRearHitch( follower, spacing * ( i + 1 ) );
		}
	}

	void PoseFollowerAtRearHitch( MinecartInteractable follower, float hitchBehind )
	{
		if ( hitchBehind < 0.0001f )
			return;

		if ( _consistRidePath != null )
			PoseFollowerOnConsistTrail( follower, hitchBehind );
		else
			PoseFollowerOnTrackBehindLead( follower, hitchBehind );
	}

	/// <summary>
	/// Place a follower opposite the lead's forward along the bound track (rear hitch).
	/// </summary>
	void PoseFollowerOnTrackBehindLead( MinecartInteractable follower, float hitchBehind )
	{
		if ( !EnsureBoundTrack() || follower == null )
			return;

		Vector3 position;
		Vector3 tangent;
		Vector3 up;
		if ( !track.Evaluate( _distanceAlongTrack, out position, out tangent, out up ) || tangent.sqrMagnitude < 0.0001f )
			return;

		int faceAlong = ResolveFaceAlongTrackSign( transform.forward, tangent );
		float dist = track.WrapDistance( _distanceAlongTrack - faceAlong * hitchBehind );

		Vector3 followerPos;
		Vector3 followerTan;
		Vector3 followerUp;
		if ( !track.Evaluate( dist, out followerPos, out followerTan, out followerUp ) )
			return;

		Vector3 facing = ResolveTrackFacingFromTangent( followerTan, faceAlong );
		follower.BindToTrack( track, dist, facing );
	}

	void BeginConsistRideTrail( MinecartJunctionRidePath path, int polarity )
	{
		_consistRidePath = path;
		_consistRideProgress = 0f;
		_consistExitTravel = 0f;
		// Lock hitch/facing polarity for the whole junction so cars don't swap sides mid-turn.
		_consistRidePolarity = polarity >= 0 ? 1 : -1;
		if ( _followers.Count <= 0 )
			ClearConsistRideTrail();
	}

	void ClearConsistRideTrail()
	{
		_consistRidePath = null;
		_consistRideProgress = 0f;
		_consistExitTravel = 0f;
		_consistRidePolarity = 1;
	}

	void TickConsistRideTrailAfterMove( float exitTraveled )
	{
		if ( _consistRidePath == null )
			return;

		if ( _followers.Count <= 0 )
		{
			ClearConsistRideTrail();
			return;
		}

		_consistExitTravel += Mathf.Max( 0f, exitTraveled );
		float clearAfter = ConsistSpacing * _followers.Count + 0.35f;
		if ( _consistExitTravel >= clearAfter )
			ClearConsistRideTrail();
	}

	bool IsConsistTrailOnExit()
	{
		if ( _consistRidePath == null || _junctionRiding )
			return false;

		float pathLen = Mathf.Max( 0.0001f, _consistRidePath.length );
		return _consistRideProgress >= pathLen - 0.001f;
	}

	/// <summary>
	/// Walk the junction trail and place the follower at rear-hitch distance behind the lead.
	/// Hitch is opposite locked consist polarity along path progress (same geometric rear as the lead nose).
	/// </summary>
	void PoseFollowerOnConsistTrail( MinecartInteractable follower, float hitchBehind )
	{
		MinecartJunctionRidePath path = _consistRidePath;
		if ( path == null || follower == null )
			return;

		int polarity = _consistRidePolarity >= 0 ? 1 : -1;
		float pathLen = Mathf.Max( 0.0001f, path.length );
		float leadProgress = _consistRideProgress + _consistExitTravel;
		// Rear hitch: opposite nose along path. Nose = pathTangent * polarity → behind decreases progress when polarity +1.
		float followerProgress = leadProgress - polarity * hitchBehind;

		if ( followerProgress <= 0.0001f )
		{
			MinecartTrack fromTrack = path.fromTrack;
			if ( fromTrack == null || !fromTrack.IsTravelReady )
				return;

			float back = -followerProgress;
			int intoSign = path.intoTravelSign >= 0 ? 1 : -1;
			float dist = fromTrack.WrapDistance( path.fromDistance - intoSign * back );
			// Face the same way as the lead did on approach (into junction, with locked polarity).
			Vector3 facing = ResolveTrackFacing( fromTrack, dist, intoSign * polarity );
			follower.BindToTrack( fromTrack, dist, facing );
			return;
		}

		if ( followerProgress < pathLen - 0.001f )
		{
			ApplyFollowerRidePose( follower, path, followerProgress, polarity );
			return;
		}

		MinecartTrack exitTrack = path.toTrack;
		if ( exitTrack == null || !exitTrack.IsTravelReady )
			return;

		float past = followerProgress - pathLen;
		int exitSign = path.outTravelSign >= 0 ? 1 : -1;
		float distExit = exitTrack.WrapDistance( path.toDistance + exitSign * past );
		Vector3 exitFacing = ResolveTrackFacing( exitTrack, distExit, exitSign * polarity );
		follower.BindToTrack( exitTrack, distExit, exitFacing );
	}

	static int ResolveFaceAlongTrackSign( Vector3 forward, Vector3 trackTangent )
	{
		forward.y = 0f;
		trackTangent.y = 0f;
		if ( forward.sqrMagnitude < 0.0001f || trackTangent.sqrMagnitude < 0.0001f )
			return 1;

		return Vector3.Dot( forward.normalized, trackTangent.normalized ) >= 0f ? 1 : -1;
	}

	static Vector3 ResolveTrackFacingFromTangent( Vector3 tangent, int faceAlong )
	{
		tangent.y = 0f;
		if ( tangent.sqrMagnitude < 0.0001f )
			return Vector3.forward;

		tangent.Normalize();
		int sign = faceAlong >= 0 ? 1 : -1;
		return tangent * sign;
	}

	static Vector3 ResolveTrackFacing( MinecartTrack track, float distance, int faceAlong )
	{
		Vector3 position;
		Vector3 tangent;
		Vector3 up;
		if ( track == null || !track.Evaluate( distance, out position, out tangent, out up ) || tangent.sqrMagnitude < 0.0001f )
			return Vector3.forward;

		return ResolveTrackFacingFromTangent( tangent, faceAlong );
	}

	void ApplyFollowerRidePose( MinecartInteractable follower, MinecartJunctionRidePath path, float rideDistance, int polarity )
	{
		Vector3 position;
		Vector3 pathTangent;
		if ( path == null || !path.TryEvaluate( rideDistance, out position, out pathTangent ) )
			return;

		int face = polarity >= 0 ? 1 : -1;
		Vector3 facing = pathTangent.sqrMagnitude > 0.0001f
			? pathTangent.normalized * face
			: follower.transform.forward;
		if ( facing.sqrMagnitude < 0.0001f )
			facing = Vector3.forward;
		else
			facing.Normalize();

		Vector3 up = Vector3.up;
		position += up * follower.RideHeight;
		Quaternion rotation = Quaternion.LookRotation( facing, up );

		Vector3 previous = follower.transform.position;
		follower.ApplyWorldPose( position, rotation );
		float wheelTravel = Vector3.Distance( previous, position );
		if ( wheelTravel > 0.00001f )
			follower.SpinWheels( wheelTravel );

		// Bookkeeping stays on arrival until the follower finishes the curve.
		if ( path.fromTrack != null )
		{
			follower.track = path.fromTrack;
			follower._distanceAlongTrack = path.fromDistance;
			follower._bound = path.fromTrack.IsTravelReady;
			follower._trackFacingSign = face;
		}
	}

	void ApplyWorldPose( Vector3 position, Quaternion rotation )
	{
		transform.SetPositionAndRotation( position, rotation );
		if ( _body != null )
		{
			_body.position = position;
			_body.rotation = rotation;
		}
	}

	void SetDistanceAlongTrack( float distance )
	{
		float previous = _distanceAlongTrack;
		_distanceAlongTrack = distance;
		ApplyTrackPose( runtime: true );
		float traveled = distance - previous;
		if ( track != null && track.IsClosed )
		{
			float length = track.Length;
			if ( traveled > length * 0.5f )
				traveled -= length;
			if ( traveled < -length * 0.5f )
				traveled += length;
		}

		SpinWheels( traveled );
	}

	void RebuildAuthoredConsist()
	{
		if ( !IsConsistLead || authoredFollowers == null || authoredFollowers.Count <= 0 )
			return;

		_followers.Clear();
		for ( int i = 0; i < authoredFollowers.Count; i++ )
		{
			MinecartInteractable follower = authoredFollowers[ i ];
			if ( follower == null || follower == this )
				continue;

			if ( follower.consistLead != null && follower.consistLead != this )
				follower.consistLead._followers.Remove( follower );

			follower.consistLead = this;
			follower.StopMoveLoop();
			if ( !_followers.Contains( follower ) )
				_followers.Add( follower );
		}

		SnapFollowers();
	}

	void RegisterAuthoredFollower( MinecartInteractable follower )
	{
		if ( follower == null || Application.isPlaying )
			return;

		if ( authoredFollowers == null )
			authoredFollowers = new List<MinecartInteractable>( 4 );

		if ( !authoredFollowers.Contains( follower ) )
			authoredFollowers.Add( follower );
	}

	public void SetPushMoving( bool moving )
	{
		if ( moving )
			PlayFeedback( onPushFeedbacks );
		else if ( onPushFeedbacks != null )
			onPushFeedbacks.Stop();
	}

	public void EditorSnapToTrack( MinecartTrack nextTrack, float distance )
	{
		track = nextTrack;
		_distanceAlongTrack = distance;
		_bound = nextTrack != null;
		ApplyTrackPose( runtime: false );
	}

	public void EditorSetDefinition( MinecartDefinition value )
	{
		definition = value;
	}

	public void EditorSetCargoRoot( Transform value )
	{
		cargoRoot = value;
	}

	public void EditorSetWheels( Transform[] value )
	{
		wheels = value;
	}

	public void EditorSetFeedbacks( Feedbacks push, Feedbacks shove, Feedbacks load, Feedbacks unload, Feedbacks takeOut, Feedbacks stop = null )
	{
		onPushFeedbacks = push;
		onShoveFeedbacks = shove;
		onLoadFeedbacks = load;
		onUnloadFeedbacks = unload;
		onTakeOutFeedbacks = takeOut;
		onStopFeedbacks = stop;
	}

	public void EditorSetSeat( Transform value )
	{
		seat = value;
	}

	public void EditorSetStopFeedbacks( Feedbacks stop )
	{
		onStopFeedbacks = stop;
	}

	public void EditorSetDriveFeedbacks( Feedbacks enter, Feedbacks move, Feedbacks brake, Feedbacks reverse, Feedbacks exit )
	{
		onDriveEnterFeedbacks = enter;
		onDriveMoveFeedbacks = move;
		onDriveBrakeFeedbacks = brake;
		onDriveReverseFeedbacks = reverse;
		onDriveExitFeedbacks = exit;
	}

	public void EditorSetHopFeedbacks( Feedbacks hopStart, Feedbacks hopLand )
	{
		onHopStartFeedbacks = hopStart;
		onHopLandFeedbacks = hopLand;
	}

	public void SetDriveSeatCollidersEnabled( bool enabled )
	{
		Collider[] colliders = GetComponentsInChildren<Collider>( true );
		for ( int i = 0; i < colliders.Length; i++ )
		{
			Collider col = colliders[ i ];
			if ( col == null || col.isTrigger )
				continue;

			col.enabled = enabled;
		}
	}

	public void PlayDriveEnterFeedback()
	{
		PlayFeedback( onDriveEnterFeedbacks );
	}

	public void PlayDriveExitFeedback()
	{
		PlayFeedback( onDriveExitFeedbacks );
	}

	public void PlayDriveReverseFeedback()
	{
		PlayFeedback( onDriveReverseFeedbacks );
	}

	public void SetDriveBraking( bool braking )
	{
		MinecartInteractable lead = ConsistLead;
		if ( lead != this )
		{
			lead.SetDriveBraking( braking );
			return;
		}

		_driveBraking = braking;
		if ( braking && onDriveBrakeFeedbacks != null && !onDriveBrakeFeedbacks.IsPlaying )
			onDriveBrakeFeedbacks.Play();
	}

	void TryStartMoveLoop()
	{
		if ( !IsConsistLead )
			return;

		if ( onDriveMoveFeedbacks != null && !onDriveMoveFeedbacks.IsPlaying )
			onDriveMoveFeedbacks.Play();

		if ( onDriveBrakeFeedbacks != null && !onDriveBrakeFeedbacks.IsPlaying )
			onDriveBrakeFeedbacks.Play();
	}

	void StopMoveLoop()
	{
		if ( onDriveMoveFeedbacks != null )
			onDriveMoveFeedbacks.Stop();
		if ( onDriveBrakeFeedbacks != null )
			onDriveBrakeFeedbacks.Stop();
	}

	float ResolveMoveRefSpeed()
	{
		if ( IsDriveCart )
			return DriveMaxSpeed;

		return Mathf.Max( ShoveSpeed, RecallSpeed );
	}

	public void EditorSetLayout( int layoutColumns, int layoutRows, float spacing, float layoutMargin, int maxStack )
	{
		columns = Mathf.Max( 1, layoutColumns );
		rows = Mathf.Max( 1, layoutRows );
		slotSpacing = Mathf.Max( 0.01f, spacing );
		margin = Mathf.Max( 0f, layoutMargin );
		maxStackPerCell = Mathf.Max( 0, maxStack );
	}

	void BindTrack( bool snapPose )
	{
		if ( track == null )
		{
			MinecartTrack nearest;
			float distance;
			if ( MinecartTrack.TryGetNearest( transform.position, TrackSnapRadius, out nearest, out distance ) )
			{
				track = nearest;
				_distanceAlongTrack = distance;
			}
		}
		else
		{
			_distanceAlongTrack = track.GetNearestDistance( transform.position );
		}

		_bound = track != null && track.IsTravelReady;
		if ( _bound && snapPose )
			ApplyTrackPose( runtime: false );
	}

	bool EnsureBoundTrack()
	{
		if ( _bound && track != null && track.IsTravelReady )
			return true;

		BindTrack( snapPose: true );
		return _bound;
	}

	void ApplyTrackPose( bool runtime )
	{
		if ( track == null )
			return;

		Vector3 position;
		Vector3 tangent;
		Vector3 up;
		if ( !track.Evaluate( _distanceAlongTrack, out position, out tangent, out up ) )
			return;

		Vector3 facing = tangent * _trackFacingSign;
		if ( facing.sqrMagnitude < 0.0001f )
			facing = tangent;

		position += up * RideHeight;
		Quaternion rotation = Quaternion.LookRotation( facing, up );
		transform.SetPositionAndRotation( position, rotation );
		if ( runtime )
		{
			if ( _body != null )
			{
				_body.position = position;
				_body.rotation = rotation;
			}
		}
	}

	void SpinWheels( float signedTravel )
	{
		if ( wheels == null || Mathf.Abs( signedTravel ) < 0.00001f )
			return;

		float angle = signedTravel / WheelRadius * Mathf.Rad2Deg;
		for ( int i = 0; i < wheels.Length; i++ )
		{
			Transform wheel = wheels[ i ];
			if ( wheel == null )
				continue;

			wheel.Rotate( Vector3.right, angle, Space.Self );
		}
	}

	static void PlayFeedback( Feedbacks feedbacks )
	{
		if ( feedbacks != null )
			feedbacks.Play();
	}

	bool CanAcceptDefinition( TreasureDefinition def, out int originX, out int originY, out int stackIndex )
	{
		originX = 0;
		originY = 0;
		stackIndex = 0;
		if ( !CargoEnabled || def == null )
			return false;

		return TryFindPlacement( def, out originX, out originY, out stackIndex );
	}

	bool TryFindPlacement( TreasureDefinition def, out int originX, out int originY, out int stackIndex )
	{
		originX = 0;
		originY = 0;
		stackIndex = 0;
		if ( def == null || _cellStacks == null )
			return false;

		Vector2Int footprint = def.GetCartGridSize();
		int cols = GridColumns;
		int rowCount = GridRows;

		if ( def.canStack )
		{
			for ( int y = 0; y <= rowCount - footprint.y; y++ )
			{
				for ( int x = 0; x <= cols - footprint.x; x++ )
				{
					CargoStack existing = _cellStacks[ x, y ];
					if ( existing == null )
						continue;

					if ( existing.OriginX != x || existing.OriginY != y )
						continue;

					if ( existing.Footprint != footprint )
						continue;

					if ( !CanAddToStack( existing, def ) )
						continue;

					originX = x;
					originY = y;
					stackIndex = existing.Count;
					return true;
				}
			}
		}

		for ( int y = 0; y <= rowCount - footprint.y; y++ )
		{
			for ( int x = 0; x <= cols - footprint.x; x++ )
			{
				if ( !IsRectangleFree( x, y, footprint ) )
					continue;

				originX = x;
				originY = y;
				stackIndex = 0;
				return true;
			}
		}

		return false;
	}

	bool CanAddToStack( CargoStack stack, TreasureDefinition placing )
	{
		if ( stack == null || placing == null || !placing.canStack )
			return false;

		int max = MaxStackPerCell;
		if ( max > 0 && stack.Count >= max )
			return false;

		if ( GroundCoinStack.IsGroundStackableCoin( placing ) )
			return stack.CoinPile != null && stack.CoinPile.CanAccept( placing );

		if ( stack.CoinPile != null )
			return false;

		if ( stack.Definition != placing )
			return false;

		return stack.Definition != null && stack.Definition.canStack;
	}

	bool IsRectangleFree( int ox, int oy, Vector2Int footprint )
	{
		int cols = GridColumns;
		int rows = GridRows;
		if ( ox < 0 || oy < 0 || ox + footprint.x > cols || oy + footprint.y > rows )
			return false;

		for ( int y = 0; y < footprint.y; y++ )
		{
			for ( int x = 0; x < footprint.x; x++ )
			{
				if ( _cellStacks[ ox + x, oy + y ] != null )
					return false;
			}
		}

		return true;
	}

	void MarkFootprint( int ox, int oy, Vector2Int footprint, CargoStack stack )
	{
		for ( int y = 0; y < footprint.y; y++ )
		{
			for ( int x = 0; x < footprint.x; x++ )
				_cellStacks[ ox + x, oy + y ] = stack;
		}
	}

	void ClearFootprint( CargoStack stack )
	{
		if ( stack == null || _cellStacks == null )
			return;

		for ( int y = 0; y < stack.Footprint.y; y++ )
		{
			for ( int x = 0; x < stack.Footprint.x; x++ )
			{
				int cx = stack.OriginX + x;
				int cy = stack.OriginY + y;
				if ( cx < 0 || cy < 0 || cx >= GridColumns || cy >= GridRows )
					continue;

				if ( _cellStacks[ cx, cy ] == stack )
					_cellStacks[ cx, cy ] = null;
			}
		}
	}

	void RestackVisuals( CargoStack stack )
	{
		if ( stack == null )
			return;

		for ( int i = 0; i < stack.Items.Count; i++ )
		{
			TreasureItem item = stack.Items[ i ];
			if ( item == null || item.IsInFlight )
				continue;

			GetCellWorldPose( stack.OriginX, stack.OriginY, i, item, stack.Definition, out Vector3 pos, out Quaternion rot );
			item.EnterDisplayed( this, cargoRoot, pos, rot );
		}
	}

	void GetCellWorldPose(
		int ox,
		int oy,
		int stackIndex,
		TreasureItem item,
		TreasureDefinition def,
		out Vector3 worldPos,
		out Quaternion worldRot )
	{
		EnsureCargoRoot();
		if ( def == null && item != null )
			def = item.Definition;

		Vector2Int footprint = def != null ? def.GetCartGridSize() : Vector2Int.one;
		Vector3 local = DisplayTableSlotLayout.GetSlotLocalPosition( SlotIndex( ox, oy ), GridRows, GridColumns, CellSpacing, margin );
		if ( footprint.x > 1 || footprint.y > 1 )
		{
			Vector3 far = DisplayTableSlotLayout.GetSlotLocalPosition(
				SlotIndex( ox + footprint.x - 1, oy + footprint.y - 1 ),
				GridRows,
				GridColumns,
				CellSpacing,
				margin );
			local = ( local + far ) * 0.5f;
		}

		if ( CoinColumnCylinderBinder.IsCoin( def ) )
			local.y = margin;
		else
		{
			float thickness = def != null ? def.GetStackThickness() : 0.04f;
			local.y = margin + stackIndex * thickness;
		}

		worldPos = cargoRoot.TransformPoint( local );
		worldRot = cargoRoot.rotation;
	}

	void FinishInFlightSnaps()
	{
		for ( int s = 0; s < _stacks.Count; s++ )
		{
			CargoStack stack = _stacks[ s ];
			if ( stack == null )
				continue;

			for ( int i = 0; i < stack.Items.Count; i++ )
			{
				TreasureItem item = stack.Items[ i ];
				if ( item == null || !item.IsInFlight )
					continue;

				item.EndFlight();
				GetCellWorldPose( stack.OriginX, stack.OriginY, i, item, item.Definition, out Vector3 pos, out Quaternion rot );
				item.EnterDisplayed( this, cargoRoot, pos, rot );
			}
		}
	}

	IEnumerator SnapIntoSlotRoutine( TreasureItem item, int ox, int oy, int stackIndex )
	{
		if ( item == null )
			yield break;

		item.BeginFlight();
		GetCellWorldPose( ox, oy, stackIndex, item, item.Definition, out Vector3 endWorldPos, out Quaternion endWorldRot );

		Transform t = item.transform;
		Vector3 startPos = t.position;
		Quaternion startRot = t.rotation;
		Vector3 startScale = t.lossyScale;
		Vector3 endScale = item.GetWorldScale();
		bool flipCoin = CoinFlipMotion.IsCoin( item );

		float duration = flipCoin
			? Mathf.Max( snapDuration, CoinFlipMotion.DefaultDuration )
			: Mathf.Max( snapDuration, CoinFlipMotion.DefaultItemArcDuration );
		float arcHeight = flipCoin ? CoinFlipMotion.DefaultArcHeight : CoinFlipMotion.DefaultItemArcHeight;
		float spins = flipCoin ? CoinFlipMotion.DefaultSpins : 0f;
		float elapsed = 0f;

		while ( elapsed < duration )
		{
			if ( item == null )
				yield break;

			if ( !_allItems.Contains( item ) )
			{
				item.EndFlight();
				yield break;
			}

			GetCellWorldPose( ox, oy, stackIndex, item, item.Definition, out endWorldPos, out endWorldRot );
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
		if ( !_allItems.Contains( item ) )
			yield break;

		GetCellWorldPose( ox, oy, stackIndex, item, item.Definition, out endWorldPos, out endWorldRot );
		item.EnterDisplayed( this, cargoRoot, endWorldPos, endWorldRot );
		PlayTreasurePlaceFeedback( item );
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

	Vector3 GetSlotLocalBase( int ox, int oy )
	{
		return DisplayTableSlotLayout.GetSlotLocalPosition( SlotIndex( ox, oy ), GridRows, GridColumns, CellSpacing, margin );
	}

	int SlotIndex( int ox, int oy )
	{
		return oy * GridColumns + ox;
	}

	bool TryGetSlotOrigin( int slotIndex, out int ox, out int oy )
	{
		ox = 0;
		oy = 0;
		int cols = GridColumns;
		int count = SlotCount;
		if ( slotIndex < 0 || slotIndex >= count )
			return false;

		ox = slotIndex % cols;
		oy = slotIndex / cols;
		return true;
	}

	CargoStack GetOriginStack( int slotIndex )
	{
		int ox;
		int oy;
		if ( !TryGetSlotOrigin( slotIndex, out ox, out oy ) || _cellStacks == null )
			return null;

		CargoStack stack = _cellStacks[ ox, oy ];
		if ( stack == null )
			return null;

		if ( stack.OriginX != ox || stack.OriginY != oy )
			return null;

		return stack;
	}

	bool TryGetSlotIndex( TreasureItem selected, out int slotIndex )
	{
		slotIndex = -1;
		if ( selected == null )
			return false;

		for ( int s = 0; s < _stacks.Count; s++ )
		{
			CargoStack stack = _stacks[ s ];
			if ( stack.Items.IndexOf( selected ) < 0 )
				continue;

			slotIndex = SlotIndex( stack.OriginX, stack.OriginY );
			return true;
		}

		return false;
	}

	void GetSlotContactPose( int ox, int oy, out Vector3 contact, out Quaternion rotation )
	{
		EnsureCargoRoot();
		contact = cargoRoot.TransformPoint( GetSlotLocalBase( ox, oy ) );
		rotation = TreasureOrientation.FlattenUpright( cargoRoot.rotation );
	}

	GroundCoinStack FindCoinPile( int ox, int oy )
	{
		CargoStack stack = GetOriginStack( SlotIndex( ox, oy ) );
		return stack != null ? stack.CoinPile : null;
	}

	public GroundCoinStack FindHostedCoinStackAt( Vector3 worldPoint )
	{
		if ( !CargoEnabled )
			return null;

		int slotIndex;
		if ( !TryResolveAimedSlot( worldPoint, out slotIndex ) )
			return null;

		int ox;
		int oy;
		RemapSlotToOrigin( slotIndex, out ox, out oy );
		return FindCoinPile( ox, oy );
	}

	public GroundCoinStack GetOrCreateCoinStackAt( Vector3 worldPoint )
	{
		if ( !CargoEnabled )
			return null;

		int slotIndex;
		if ( !TryResolveAimedSlot( worldPoint, out slotIndex ) )
			return null;

		int ox;
		int oy;
		RemapSlotToOrigin( slotIndex, out ox, out oy );
		return GetOrCreateCoinStack( ox, oy );
	}

	public bool TryResolveCoinStackPlace(
		TreasureItem probe,
		in PlacementQuery query,
		out GroundCoinStack stack,
		out Vector3 contact,
		out Quaternion rotation,
		out bool placeValid )
	{
		stack = null;
		contact = transform.position;
		rotation = transform.rotation;
		placeValid = false;
		if ( !CargoEnabled || probe == null || !GroundCoinStack.IsGroundStackableCoin( probe ) )
			return false;

		int ox;
		int oy;
		int stackIndex;
		bool originValid;
		if ( !TryResolvePlaceOrigin( probe, in query, out ox, out oy, out stackIndex, out originValid ) )
			return false;

		stack = FindCoinPile( ox, oy );
		if ( stack != null )
		{
			contact = stack.ContactPosition;
			rotation = stack.transform.rotation;
			placeValid = originValid && !stack.IsFull;
			return true;
		}

		GetSlotContactPose( ox, oy, out contact, out rotation );
		placeValid = originValid;
		return true;
	}

	GroundCoinStack GetOrCreateCoinStack( int ox, int oy )
	{
		if ( !CargoEnabled )
			return null;

		if ( _cellStacks == null || ox < 0 || oy < 0 || ox >= GridColumns || oy >= GridRows )
			return null;

		CargoStack existing = _cellStacks[ ox, oy ];
		if ( existing != null )
		{
			if ( existing.OriginX != ox || existing.OriginY != oy )
				return null;

			return existing.CoinPile;
		}

		if ( !IsRectangleFree( ox, oy, Vector2Int.one ) )
			return null;

		EnsureCargoRoot();
		GetSlotContactPose( ox, oy, out Vector3 world, out Quaternion rot );
		GroundCoinStack pile = GroundCoinStack.CreateAt( world, rot );
		pile.name = "CartCoinStack";
		pile.transform.SetParent( cargoRoot, true );
		pile.ConfigureForMinecart( this, MaxStackPerCell );

		CargoStack stack = new CargoStack
		{
			OriginX = ox,
			OriginY = oy,
			Footprint = Vector2Int.one,
			Definition = null,
			CoinPile = pile
		};
		_stacks.Add( stack );
		MarkFootprint( ox, oy, Vector2Int.one, stack );
		return pile;
	}

	public void NotifyHostedCoinStackDestroyed( GroundCoinStack pile )
	{
		if ( pile == null )
			return;

		for ( int s = 0; s < _stacks.Count; s++ )
		{
			CargoStack stack = _stacks[ s ];
			if ( stack == null || stack.CoinPile != pile )
				continue;

			stack.CoinPile = null;
			ClearFootprint( stack );
			_stacks.RemoveAt( s );
			return;
		}
	}

	bool TryResolvePlaceOrigin(
		TreasureItem item,
		in PlacementQuery query,
		out int originX,
		out int originY,
		out int stackIndex,
		out bool valid )
	{
		originX = 0;
		originY = 0;
		stackIndex = 0;
		valid = false;
		if ( item == null || item.Definition == null || _cellStacks == null )
			return false;

		TreasureDefinition def = item.Definition;
		if ( TryResolveAimedOrigin( in query, out originX, out originY ) )
		{
			if ( TryGetStackIndexForOrigin( def, originX, originY, out stackIndex ) )
			{
				valid = true;
				return true;
			}

			if ( query.AutoFindValidSlot && TryFindNearestValidOrigin( def, GetSlotSearchReference( in query ), out originX, out originY, out stackIndex ) )
			{
				valid = true;
				return true;
			}

			stackIndex = GetDisplayStackIndex( originX, originY );
			return true;
		}

		if ( TryFindNearestValidOrigin( def, GetSlotSearchReference( in query ), out originX, out originY, out stackIndex ) )
		{
			valid = true;
			return true;
		}

		return false;
	}

	bool TryResolveAimedOrigin( in PlacementQuery query, out int originX, out int originY )
	{
		originX = 0;
		originY = 0;
		if ( !query.HasHit || query.Hit.collider == null )
			return false;

		TreasureItem hitItem = query.Hit.collider.GetComponentInParent<TreasureItem>();
		int slotIndex;
		if ( hitItem != null && (Object)hitItem.Owner == this && TryGetSlotIndex( hitItem, out slotIndex ) )
		{
			RemapSlotToOrigin( slotIndex, out originX, out originY );
			return true;
		}

		GroundCoinStack hitPile = query.Hit.collider.GetComponentInParent<GroundCoinStack>();
		if ( hitPile != null )
		{
			for ( int s = 0; s < _stacks.Count; s++ )
			{
				CargoStack cargo = _stacks[ s ];
				if ( cargo == null || cargo.CoinPile != hitPile )
					continue;

				originX = cargo.OriginX;
				originY = cargo.OriginY;
				return true;
			}
		}

		if ( !TryResolveAimedSlot( query.Hit.point, out slotIndex ) )
			return false;

		RemapSlotToOrigin( slotIndex, out originX, out originY );
		return true;
	}

	void RemapSlotToOrigin( int slotIndex, out int originX, out int originY )
	{
		if ( !TryGetSlotOrigin( slotIndex, out originX, out originY ) || _cellStacks == null )
			return;

		CargoStack stack = _cellStacks[ originX, originY ];
		if ( stack == null )
			return;

		originX = stack.OriginX;
		originY = stack.OriginY;
	}

	Vector3 GetSlotSearchReference( in PlacementQuery query )
	{
		if ( query.HasHit )
			return query.Hit.point;

		EnsureCargoRoot();
		return cargoRoot.position;
	}

	bool TryFindNearestValidOrigin(
		TreasureDefinition def,
		Vector3 worldPoint,
		out int originX,
		out int originY,
		out int stackIndex )
	{
		originX = 0;
		originY = 0;
		stackIndex = 0;
		if ( def == null || _cellStacks == null )
			return false;

		EnsureCargoRoot();
		Vector3 local = cargoRoot.InverseTransformPoint( worldPoint );
		local.y = 0f;
		Vector2Int footprint = def.GetCartGridSize();
		int cols = GridColumns;
		int rowCount = GridRows;
		float best = float.MaxValue;
		bool found = false;

		for ( int y = 0; y <= rowCount - footprint.y; y++ )
		{
			for ( int x = 0; x <= cols - footprint.x; x++ )
			{
				int candidateStack;
				if ( !TryGetStackIndexForOrigin( def, x, y, out candidateStack ) )
					continue;

				Vector3 slotLocal = DisplayTableSlotLayout.GetSlotLocalPosition(
					SlotIndex( x, y ),
					GridRows,
					GridColumns,
					CellSpacing,
					margin );
				slotLocal.y = 0f;
				float d = ( slotLocal - local ).sqrMagnitude;
				if ( d >= best )
					continue;

				best = d;
				originX = x;
				originY = y;
				stackIndex = candidateStack;
				found = true;
			}
		}

		return found;
	}

	bool TryGetStackIndexForOrigin( TreasureDefinition def, int originX, int originY, out int stackIndex )
	{
		stackIndex = 0;
		if ( def == null || _cellStacks == null )
			return false;

		if ( originX < 0 || originY < 0 || originX >= GridColumns || originY >= GridRows )
			return false;

		CargoStack stack = _cellStacks[ originX, originY ];
		if ( stack != null )
		{
			if ( stack.OriginX != originX || stack.OriginY != originY )
				return false;

			if ( !CanAddToStack( stack, def ) )
				return false;

			stackIndex = stack.Count;
			return true;
		}

		if ( !IsRectangleFree( originX, originY, def.GetCartGridSize() ) )
			return false;

		stackIndex = 0;
		return true;
	}

	int GetDisplayStackIndex( int originX, int originY )
	{
		CargoStack stack = GetOriginStack( SlotIndex( originX, originY ) );
		return stack != null ? stack.Count : 0;
	}

	public bool TryGetAimedCargoInteractable( Ray ray, Vector3 worldPoint, out InteractableBase interactable )
	{
		interactable = null;
		int slotIndex;
		if ( !TryResolveAimedSlot( worldPoint, out slotIndex ) )
			return false;

		EnsureCargoRoot();
		Vector3 localHit = cargoRoot.InverseTransformPoint( worldPoint );
		localHit.y = 0f;
		Vector3 slotLocal = GetSlotLocalBase( slotIndex % GridColumns, slotIndex / GridColumns );
		slotLocal.y = 0f;
		float maxDist = CellSpacing * 0.55f;
		if ( ( slotLocal - localHit ).sqrMagnitude > maxDist * maxDist )
			return false;

		int ox;
		int oy;
		RemapSlotToOrigin( slotIndex, out ox, out oy );
		CargoStack stack = GetOriginStack( SlotIndex( ox, oy ) );
		if ( stack == null || stack.Count <= 0 )
			return false;

		if ( stack.CoinPile != null )
		{
			interactable = stack.CoinPile;
			return true;
		}

		int index = 0;
		if ( stack.Items.Count > 0 )
			index = CoinColumnPickup.ResolveIndexFromAimRay( stack.Items, ray, true, worldPoint.y );

		index = Mathf.Clamp( index, 0, stack.Items.Count - 1 );
		TreasureItem item = stack.Items[ index ];
		if ( item == null )
			item = stack.Items[ 0 ];

		if ( item == null )
			return false;

		TreasureItemInteractable itemInteractable = item.GetComponent<TreasureItemInteractable>();
		if ( itemInteractable == null )
			itemInteractable = item.GetComponentInChildren<TreasureItemInteractable>( true );

		interactable = itemInteractable;
		return interactable != null;
	}

	public bool TryResolveTreasureFromCollider( Collider collider, out TreasureItem item )
	{
		item = null;
		if ( collider == null )
			return false;

		TreasureItem onCollider = collider.GetComponentInParent<TreasureItem>();
		if ( onCollider != null && _allItems.Contains( onCollider ) )
		{
			item = onCollider;
			return true;
		}

		return false;
	}

	bool TryResolveAimedSlot( Vector3 worldPoint, out int slotIndex )
	{
		slotIndex = -1;
		EnsureCargoRoot();
		Vector3 local = cargoRoot.InverseTransformPoint( worldPoint );
		local.y = 0f;
		float best = float.MaxValue;
		int count = SlotCount;
		for ( int i = 0; i < count; i++ )
		{
			Vector3 slotLocal = DisplayTableSlotLayout.GetSlotLocalPosition( i, GridRows, GridColumns, CellSpacing, margin );
			slotLocal.y = 0f;
			float d = ( slotLocal - local ).sqrMagnitude;
			if ( d >= best )
				continue;

			best = d;
			slotIndex = i;
		}

		return slotIndex >= 0;
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
		if ( !CargoEnabled )
			return;

		Transform area = DisplayArea;
		DisplayTableSlotLayout.DrawLayoutGizmos(
			area,
			GridRows,
			GridColumns,
			CellSpacing,
			margin,
			new Color( 0.95f, 0.7f, 0.2f, 0.9f ),
			new Color( 0.95f, 0.7f, 0.2f, 0.35f ) );
	}

#if UNITY_EDITOR
	public void DrawLayoutSceneHandles()
	{
		if ( !CargoEnabled )
			return;

		Transform area = DisplayArea;
		if ( area == null )
			return;

		UnityEditor.Handles.color = new Color( 1f, 0.85f, 0.2f, 0.95f );
		int count = SlotCount;
		for ( int i = 0; i < count; i++ )
		{
			Vector3 world = area.TransformPoint(
				DisplayTableSlotLayout.GetSlotLocalPosition( i, GridRows, GridColumns, CellSpacing, margin ) );
			UnityEditor.Handles.Label( world + area.up * 0.05f, i.ToString() );
		}
	}
#endif
}
