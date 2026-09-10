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
public class MinecartInteractable : InteractableBase, ITreasureOwner, ITreasurePlacementTarget
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

	const float StopSpeedEpsilon = 0.04f;
	const float StopFeedbackDebounce = 0.18f;
	const float UnboundBindRetry = 0.5f;

	float _unbindRetryTimer;
	Transform _visualRoot;
	Collider _ownCollider;

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

	public bool AttachPlayerWhenStanding => !IsDriveCart && ( definition == null || definition.attachPlayerWhenStanding );

	public float RiderSpeedMultiplier => definition != null ? Mathf.Max( 0.1f, definition.riderSpeedMultiplier ) : 2f;

	public bool IsDriveCart => definition != null && definition.kind == MinecartKind.Drive;

	public bool CargoEnabled => !IsDriveCart;

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

	public bool IsHoldPushing => ConsistLead._holdPush;

	public float AlongTrackSpeed => ConsistLead._alongSpeed;

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
		_ownCollider = GetComponent<Collider>();
		if ( IsDriveCart )
			DisableDriveCargo();
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
	}

	void OnDisable()
	{
		All.Remove( this );
		_riderPresent = false;
		CancelRecall( arrived: false );
		DetachFromConsist();
		FinishInFlightSnaps();
		StopAllCoroutines();
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

		bool simulating = _recalling || _drivePowered || Mathf.Abs( _coastSpeed ) >= StopSpeedEpsilon;
		if ( !simulating )
		{
			TickStopFeedback();
			return;
		}

		TickRecall( Time.deltaTime );
		TickDrive( Time.deltaTime );
		TickCoast( Time.deltaTime );
		TickStopFeedback();
	}

	void EnsureCargoRoot()
	{
		if ( cargoRoot == null )
			cargoRoot = transform;
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

		NotifyCargoPlaced();
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
			_coastSpeed = 0f;
			_driveSpeed = 0f;
			_drivePowered = false;
		}
	}

	public bool TryGetTrackTangent( out Vector3 tangent )
	{
		tangent = transform.forward;
		if ( !EnsureBoundTrack() )
			return false;

		Vector3 position;
		Vector3 up;
		if ( !track.Evaluate( _distanceAlongTrack, out position, out tangent, out up ) )
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

		float next;
		if ( !track.TryAdvance( _distanceAlongTrack, signedDelta, this, out next ) )
			return false;

		float traveled = next - _distanceAlongTrack;
		if ( track.IsClosed )
		{
			float length = track.Length;
			if ( traveled > length * 0.5f )
				traveled -= length;
			if ( traveled < -length * 0.5f )
				traveled += length;
		}

		_distanceAlongTrack = next;
		ApplyTrackPose( runtime: true );
		SpinWheels( traveled );
		SnapFollowers();
		return Mathf.Abs( traveled ) > 0.00001f;
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

		if ( powered )
			CancelRecall( arrived: false );

		_drivePowered = powered;
		_driveSpeed = signedSpeed;
		if ( powered )
			_coastSpeed = 0f;
		else if ( Mathf.Abs( signedSpeed ) >= StopSpeedEpsilon )
			_coastSpeed = signedSpeed;
		else
		{
			_driveSpeed = 0f;
			_coastSpeed = 0f;
		}
	}

	public bool BeginRecall( float targetDistance, MinecartCallPost post )
	{
		MinecartInteractable lead = ConsistLead;
		if ( lead != this )
			return lead.BeginRecall( targetDistance, post );

		if ( !EnsureBoundTrack() )
			return false;

		_recalling = true;
		_recallDistance = targetDistance;
		_recallPost = post;
		_holdPush = false;
		_drivePowered = false;
		_coastSpeed = 0f;
		return true;
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
		if ( follower.BoundTrack != track )
			follower.BindToTrack( track, _distanceAlongTrack - ConsistSpacing * ( _followers.Count + 1 ) );

		follower.consistLead = this;
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
			follower.consistLead = null;

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
			return;
		}

		for ( int i = _followers.Count - 1; i >= 0; i-- )
		{
			MinecartInteractable follower = _followers[ i ];
			if ( follower != null )
				follower.consistLead = null;
		}

		_followers.Clear();
	}

	public void BindToTrack( MinecartTrack nextTrack, float distance )
	{
		track = nextTrack;
		_distanceAlongTrack = nextTrack != null ? nextTrack.WrapDistance( distance ) : distance;
		_bound = nextTrack != null && nextTrack.IsUsable;
		if ( _bound )
			ApplyTrackPose( runtime: Application.isPlaying );
	}

	public Vector3 ResolveSeatWorldPosition()
	{
		if ( seat != null )
			return seat.position;

		return transform.position + transform.up * ( RideHeight + 0.55f );
	}

	public Quaternion ResolveSeatWorldRotation()
	{
		if ( seat != null )
			return seat.rotation;

		return transform.rotation;
	}

	public void NotifyCargoPlaced()
	{
		PlayFeedback( onLoadFeedbacks );
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
		float step = dir * RecallSpeed * dt;
		if ( Mathf.Abs( step ) > Mathf.Abs( sep ) )
			step = sep;

		_alongSpeed = dir * RecallSpeed;
		if ( !TryPushAlong( step ) )
			_alongSpeed = 0f;
	}

	void TickDrive( float dt )
	{
		if ( !_drivePowered || _holdPush || _recalling )
			return;

		_alongSpeed = _driveSpeed;
		if ( Mathf.Abs( _driveSpeed ) < StopSpeedEpsilon )
			return;

		if ( !TryPushAlong( _driveSpeed * dt ) )
		{
			_alongSpeed = 0f;
			_driveSpeed = 0f;
		}
	}

	void TickCoast( float dt )
	{
		if ( _holdPush || _recalling || _drivePowered )
			return;

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

		_coastSpeed = Mathf.MoveTowards( _coastSpeed, 0f, ShoveDrag * dt );
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
		if ( !EnsureBoundTrack() || _followers.Count <= 0 )
			return;

		float spacing = ConsistSpacing;
		for ( int i = 0; i < _followers.Count; i++ )
		{
			MinecartInteractable follower = _followers[ i ];
			if ( follower == null )
				continue;

			float dist = track.WrapDistance( _distanceAlongTrack - spacing * ( i + 1 ) );
			if ( Mathf.Abs( follower.DistanceAlongTrack - dist ) < 0.00001f )
				continue;

			follower.SetDistanceAlongTrack( dist );
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

	public void PlayDriveBrakeFeedback()
	{
		PlayFeedback( onDriveBrakeFeedbacks );
	}

	public void PlayDriveReverseFeedback()
	{
		PlayFeedback( onDriveReverseFeedbacks );
	}

	public void SetDriveMoveLoop( bool playing )
	{
		if ( onDriveMoveFeedbacks == null )
			return;

		if ( playing )
			onDriveMoveFeedbacks.Play();
		else
			onDriveMoveFeedbacks.Stop();
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

		_bound = track != null && track.IsUsable;
		if ( _bound && snapPose )
			ApplyTrackPose( runtime: false );
	}

	bool EnsureBoundTrack()
	{
		if ( _bound && track != null && track.IsUsable )
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

		position += up * RideHeight;
		Quaternion rotation = Quaternion.LookRotation( tangent, up );
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
		if ( hitItem != null && hitItem.Owner == this && TryGetSlotIndex( hitItem, out slotIndex ) )
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
