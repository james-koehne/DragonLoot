using System.Collections;
using System.Collections.Generic;

using UnityEngine;

/// <summary>Hand rig a treasure is carried in. Chests, keys, and other non-artifact loot use General.</summary>
public enum CarryBucketKind
{
	Coin = 0,
	Gem = 1,
	Artifact = 2,
	General = 3
}

public class PlayerCarry : MonoBehaviour, ITreasureOwner
{
	public const int BucketCount = 4;

	struct CarriedEntry
	{
		public TreasureDefinition Definition;
		public TreasureItem Item;
		public int Token;
		public int ClusterId;
		public bool IsClusterAnchor;
		public bool PoseSettled;
	}

	/// <summary>One category rig: left-hand held stack plus the right-hand active item.</summary>
	class CategoryBucket
	{
		public CarryBucketKind Kind;
		public Transform RigRoot;
		public Transform HoldRoot;
		public Transform ActiveRoot;
		public readonly List<CarriedEntry> Held = new List<CarriedEntry>();
		public bool HasActive;
		public CarriedEntry Active;

		/// <summary>0 = parked at the category rest offset, 1 = fully in the active hand pose.</summary>
		public float SwapT;
		public bool Visible;

		public CoinStackCylinderVisual HeldCylinder;
		public readonly List<TreasureItem> CylinderBuffer = new List<TreasureItem>();

		public int Count => ( HasActive ? 1 : 0 ) + Held.Count;
	}

	const float PoseSettlePosEpsilon = 0.0001f;
	const float PoseSettleScaleEpsilon = 0.0001f;
	const float PoseSettleAngleEpsilon = 0.05f;
	const float SwapVisibleEpsilon = 0.0005f;

	readonly CategoryBucket[] _buckets = new CategoryBucket[ BucketCount ];
	readonly List<CarriedEntry> _cycleBuffer = new List<CarriedEntry>();
	readonly List<CarriedEntry> _drainBuffer = new List<CarriedEntry>();
	readonly List<int> _tweenAbortBuffer = new List<int>();
	readonly Dictionary<int, Coroutine> _holdTweens = new Dictionary<int, Coroutine>();

	// Scratch for affordability simulation, indexed by CarryBucketKind.
	readonly bool[] _simExclusive = new bool[ BucketCount ];
	readonly bool[] _simAny = new bool[ BucketCount ];
	readonly int[] _insertCursor = new int[ BucketCount ];
	readonly bool[] _bucketTouched = new bool[ BucketCount ];

	CarryDefinition _definition;
	PlayerController _player;
	FirstPersonCameraController _cameraLook;
	Transform _carryRigs;
	int _nextToken = 1;
	int _usedCapacity;
	float _bobPhase;
	float _swayPhase;
	Vector3 _smoothedMotionOffset;
	float _smoothedUprightPitch;
	float _cycleCooldown;
	CarryBucketKind _selected = CarryBucketKind.Coin;
	CarryBucketKind _lastPublishedHeldBucket = (CarryBucketKind)(-1);
	int _lastPublishedHeldCount = -1;
	readonly HashSet<string> _newTreasureIds = new HashSet<string>();
	readonly HashSet<string> _newCountScratch = new HashSet<string>();
	float _pouchViewElapsed;
	float _coinHandVariationSeed;

	CarryDefinition Definition => RuntimeDefinition.Resolve( ref _definition );

	public TreasureOwnerKind OwnerKind => TreasureOwnerKind.Player;

	public int UsedCapacity => _usedCapacity;
	public int UsedWeight => _usedCapacity;

	public int MaxCarryWeight
	{
		get
		{
			CarryDefinition def = Definition;
			return def != null ? Mathf.Max( 1, def.maxCarryWeight ) : 10;
		}
	}

	/// <summary>Legacy name — same as <see cref="MaxCarryWeight"/> (burden reference, not a pickup cap).</summary>
	public int MaxCapacity => MaxCarryWeight;

	/// <summary>0 = empty hands, 1 = at or above <see cref="MaxCarryWeight"/>. Weight is global across buckets.</summary>
	public float CarryBurden01 => Mathf.Clamp01( (float)_usedCapacity / MaxCarryWeight );

	/// <summary>Linear walk/sprint multiplier from carried weight; never below definition floor.</summary>
	public float MoveSpeedMultiplier
	{
		get
		{
			CarryDefinition def = Definition;
			float floor = def != null ? def.minBurdenedMoveSpeedScale : 1f;
			return Mathf.Lerp( 1f, floor, CarryBurden01 );
		}
	}

	/// <summary>Category currently shown in the active (right hand) pose.</summary>
	public CarryBucketKind SelectedBucket => _selected;

	public int Count => GetBucket( _selected ).Count;
	public int HeldCount => GetBucket( _selected ).Held.Count;
	public bool HasActive => GetBucket( _selected ).HasActive;
	public Transform HoldRoot => GetBucket( _selected ).HoldRoot;
	public Transform ActiveRoot => GetBucket( _selected ).ActiveRoot;

	/// <summary>Items across every category bucket.</summary>
	public int TotalCount
	{
		get
		{
			EnsureBuckets();
			int total = 0;
			for ( int i = 0; i < BucketCount; i++ )
				total += _buckets[ i ].Count;

			return total;
		}
	}

	/// <summary>Hold duration for whole-stack pickup (F) and place (R) for a given quantity.</summary>
	public float GetWholeStackHoldSeconds( int quantity )
	{
		CarryDefinition def = Definition;
		if ( def != null )
			return def.ResolveWholeStackHoldSeconds( quantity );
		return 5f;
	}

	/// <summary>Legacy accessor — uses selected-bucket count.</summary>
	public float WholeStackHoldSeconds
	{
		get
		{
			return GetWholeStackHoldSeconds( GetBucketCount( _selected ) );
		}
	}

	public static CarryBucketKind ResolveBucket( TreasureDefinition def )
	{
		if ( def == null )
			return CarryBucketKind.General;

		switch ( def.category )
		{
			case TreasureCategory.Coin:
				return CarryBucketKind.Coin;
			case TreasureCategory.Gem:
				return CarryBucketKind.Gem;
			case TreasureCategory.Artifact:
				return CarryBucketKind.Artifact;
			default:
				return CarryBucketKind.General;
		}
	}

	public int GetBucketCount( CarryBucketKind kind )
	{
		return GetBucket( kind ).Count;
	}

	public Transform GetHoldRoot( CarryBucketKind kind )
	{
		return GetBucket( kind ).HoldRoot;
	}

	public Transform GetActiveRoot( CarryBucketKind kind )
	{
		return GetBucket( kind ).ActiveRoot;
	}

	public bool TrySetSelectedBucket( CarryBucketKind kind )
	{
		EnsureBuckets();
		if ( _selected == kind )
			return false;

		_selected = kind;
		_pouchViewElapsed = 0f;

		CategoryBucket bucket = _buckets[ (int)kind ];
		if ( bucket.RigRoot != null && !bucket.Visible )
		{
			bucket.Visible = true;
			bucket.RigRoot.gameObject.SetActive( true );
		}

		RestackPoses();
		EventBus.Publish( new PouchChangedEvent
		{
			Bucket = kind,
			Carry = this
		} );
		PublishHeldCategoryChanged();
		return true;
	}

	public bool CycleSelectedBucket()
	{
		EnsureBuckets();
		int start = (int)_selected;
		for ( int step = 1; step < BucketCount; step++ )
		{
			CarryBucketKind next = (CarryBucketKind)( ( start + step ) % BucketCount );
			if ( GetBucketCount( next ) <= 0 )
				continue;

			return TrySetSelectedBucket( next );
		}

		return false;
	}

	/// <summary>
	/// Unique newly discovered types in this pouch that have not been viewed yet.
	/// Hidden on the selected pouch; cleared after staying on that pouch for
	/// <see cref="CarryDefinition.pouchNewItemAcknowledgeSeconds"/>.
	/// </summary>
	public int GetNewItemTypeCount( CarryBucketKind kind )
	{
		if ( kind == _selected || _newTreasureIds.Count == 0 )
			return 0;

		_newCountScratch.Clear();
		CategoryBucket bucket = GetBucket( kind );
		NoteNewDefinition( bucket.HasActive ? bucket.Active.Definition : null );
		for ( int i = 0; i < bucket.Held.Count; i++ )
			NoteNewDefinition( bucket.Held[ i ].Definition );

		return _newCountScratch.Count;
	}

	void AcknowledgeNewItems( CarryBucketKind kind )
	{
		if ( _newTreasureIds.Count == 0 )
			return;

		CategoryBucket bucket = GetBucket( kind );
		RemoveAcknowledged( bucket.HasActive ? bucket.Active.Definition : null );
		for ( int i = 0; i < bucket.Held.Count; i++ )
			RemoveAcknowledged( bucket.Held[ i ].Definition );
	}

	void TickNewItemDwell()
	{
		if ( _newTreasureIds.Count == 0 )
			return;

		CarryDefinition def = Definition;
		float need = def != null ? def.pouchNewItemAcknowledgeSeconds : 1.5f;
		_pouchViewElapsed += Time.deltaTime;
		if ( _pouchViewElapsed < need )
			return;

		AcknowledgeNewItems( _selected );
	}

	void RemoveAcknowledged( TreasureDefinition definition )
	{
		string id = TreasureId( definition );
		if ( string.IsNullOrEmpty( id ) )
			return;

		_newTreasureIds.Remove( id );
	}

	void NoteNewDefinition( TreasureDefinition definition )
	{
		string id = TreasureId( definition );
		if ( string.IsNullOrEmpty( id ) )
			return;
		if ( !_newTreasureIds.Contains( id ) )
			return;

		_newCountScratch.Add( id );
	}

	static string TreasureId( TreasureDefinition definition )
	{
		if ( definition == null )
			return null;
		if ( !string.IsNullOrEmpty( definition.id ) )
			return definition.id;
		return definition.name;
	}

	public bool IsHoldingCategory( TreasureCategory category )
	{
		EnsureBuckets();
		if ( !TryMapCategoryToBucket( category, out CarryBucketKind bucketKind ) )
			return false;
		if ( _selected != bucketKind )
			return false;
		return GetBucket( bucketKind ).Count > 0;
	}

	public static bool TryMapCategoryToBucket( TreasureCategory category, out CarryBucketKind bucket )
	{
		switch ( category )
		{
			case TreasureCategory.Coin:
				bucket = CarryBucketKind.Coin;
				return true;
			case TreasureCategory.Gem:
				bucket = CarryBucketKind.Gem;
				return true;
			case TreasureCategory.Artifact:
				bucket = CarryBucketKind.Artifact;
				return true;
			default:
				bucket = CarryBucketKind.General;
				return true;
		}
	}

	public static TreasureCategory MapBucketToCategory( CarryBucketKind bucket )
	{
		switch ( bucket )
		{
			case CarryBucketKind.Gem:
				return TreasureCategory.Gem;
			case CarryBucketKind.Artifact:
				return TreasureCategory.Artifact;
			case CarryBucketKind.General:
				return TreasureCategory.Chest;
			default:
				return TreasureCategory.Coin;
		}
	}

	void PublishHeldCategoryChanged()
	{
		EnsureBuckets();
		CategoryBucket bucket = GetBucket( _selected );
		int count = bucket != null ? bucket.Count : 0;
		if ( _selected == _lastPublishedHeldBucket && count == _lastPublishedHeldCount )
			return;

		_lastPublishedHeldBucket = _selected;
		_lastPublishedHeldCount = count;

		TreasureCategory category = MapBucketToCategory( _selected );
		if ( TryPeekActive( out TreasureDefinition activeDef, out _ ) && activeDef != null )
			category = activeDef.category;

		EventBus.Publish( new PlayerHeldCategoryChangedEvent
		{
			Category = category,
			IsHolding = count > 0,
			Carry = this
		} );
	}

	public bool ContainsItem( TreasureItem item )
	{
		if ( item == null )
			return false;

		EnsureBuckets();
		for ( int b = 0; b < BucketCount; b++ )
		{
			CategoryBucket bucket = _buckets[ b ];
			if ( bucket.HasActive && bucket.Active.Item == item )
				return true;

			for ( int i = 0; i < bucket.Held.Count; i++ )
			{
				if ( bucket.Held[ i ].Item == item )
					return true;
			}
		}

		return false;
	}

	void Awake()
	{
		EnsureBuckets();
	}

	void EnsureBuckets()
	{
		if ( _buckets[ 0 ] != null )
			return;

		for ( int i = 0; i < BucketCount; i++ )
			_buckets[ i ] = new CategoryBucket { Kind = (CarryBucketKind)i };

		CategoryBucket selected = _buckets[ (int)_selected ];
		selected.SwapT = 1f;
		selected.Visible = true;
	}

	CategoryBucket GetBucket( CarryBucketKind kind )
	{
		EnsureBuckets();
		return _buckets[ (int)kind ];
	}

	CategoryBucket GetBucketFor( TreasureDefinition definition )
	{
		return GetBucket( ResolveBucket( definition ) );
	}

	public void Setup( PlayerController player, FirstPersonCameraController cameraLook )
	{
		_player = player;
		_cameraLook = cameraLook;
		EnsureBuckets();

		Transform cameraTransform = cameraLook != null ? cameraLook.transform : null;
		if ( cameraTransform == null )
			return;

		LooseTreasureManager.EnsureExists();
		TreasureProximitySleep.SetPlayer( transform.root != null ? transform.root : transform );

		if ( _carryRigs == null )
		{
			GameObject rigsGo = new GameObject( "CarryRigs" );
			_carryRigs = rigsGo.transform;
		}

		_carryRigs.SetParent( cameraTransform, false );
		_carryRigs.localPosition = Vector3.zero;
		_carryRigs.localRotation = Quaternion.identity;
		_carryRigs.localScale = Vector3.one;

		for ( int i = 0; i < BucketCount; i++ )
			EnsureBucketRig( _buckets[ i ] );

		_smoothedMotionOffset = Vector3.zero;
		_smoothedUprightPitch = 0f;

		CarryDefinition carryDef = Definition;
		int poolSize = carryDef != null ? carryDef.coinVisualPoolSize : 24;
		TreasureItemFactory.SetCoinVisualPoolCapacity( poolSize );

		ApplyHoldRootPose( immediate: true );
		RestackPoses();
	}

	void EnsureBucketRig( CategoryBucket bucket )
	{
		if ( bucket == null || _carryRigs == null )
			return;

		if ( bucket.RigRoot == null )
		{
			GameObject rigGo = new GameObject( GetRigName( bucket.Kind ) );
			bucket.RigRoot = rigGo.transform;
		}

		bucket.RigRoot.SetParent( _carryRigs, false );

		if ( bucket.HoldRoot == null )
		{
			GameObject holdGo = new GameObject( "HoldRoot" );
			bucket.HoldRoot = holdGo.transform;
		}

		bucket.HoldRoot.SetParent( bucket.RigRoot, false );

		if ( bucket.ActiveRoot == null )
		{
			GameObject activeGo = new GameObject( "ActiveRoot" );
			bucket.ActiveRoot = activeGo.transform;
		}

		bucket.ActiveRoot.SetParent( bucket.RigRoot, false );

		bucket.Visible = bucket.Kind == _selected || bucket.SwapT > SwapVisibleEpsilon;
		bucket.RigRoot.gameObject.SetActive( bucket.Visible );
	}

	static string GetRigName( CarryBucketKind kind )
	{
		switch ( kind )
		{
			case CarryBucketKind.Coin:
				return "CoinRig";
			case CarryBucketKind.Gem:
				return "GemRig";
			case CarryBucketKind.General:
				return "GeneralRig";
			default:
				return "ArtifactRig";
		}
	}

	void LateUpdate()
	{
		EnsureBuckets();
		EnsureSelectedPouchValid();
		TickNewItemDwell();
		if ( _carryRigs == null )
			return;

		UpdateCategorySwap();
		ApplyHoldRootPose( immediate: false );
		TryCycleFromScroll();
		SmoothCarryPoses();
		RefreshHeldCoinCylinder();
		PublishHeldCategoryChanged();
	}

	/// <summary>
	/// Drives each rig between rest and active poses. Both rigs stay visible during a swap;
	/// the outgoing rig is hidden only once its animation has finished.
	/// </summary>
	void UpdateCategorySwap()
	{
		CarryDefinition def = Definition;
		float duration = def != null ? Mathf.Max( 0.05f, def.categorySwapDuration ) : 0.35f;
		float step = Time.deltaTime / duration;

		for ( int i = 0; i < BucketCount; i++ )
		{
			CategoryBucket bucket = _buckets[ i ];
			bool isSelected = bucket.Kind == _selected;
			bucket.SwapT = Mathf.MoveTowards( bucket.SwapT, isSelected ? 1f : 0f, step );

			bool visible = isSelected || bucket.SwapT > SwapVisibleEpsilon;
			if ( visible == bucket.Visible )
				continue;

			bucket.Visible = visible;
			if ( bucket.RigRoot != null )
				bucket.RigRoot.gameObject.SetActive( visible );
		}
	}

	void TryCycleFromScroll()
	{
		if ( _player != null && !_player.GameplayInputEnabled )
			return;

		if ( _player != null )
		{
			PlayerSorterReposition sorter = _player.SorterReposition;
			if ( sorter != null && sorter.IsBusy )
				return;
		}

		if ( _cycleCooldown > 0f )
			_cycleCooldown -= Time.deltaTime;

		if ( Count <= 1 )
			return;

		GameInput input = InputController.Instance != null ? InputController.Instance.GameInput : null;
		if ( input == null || input.ScrollWheel == null )
			return;

		Vector2 scroll = input.ScrollWheel.ReadValue<Vector2>();
		CarryDefinition def = Definition;
		float threshold = def != null ? def.itemCycleScrollThreshold : 0.1f;
		if ( Mathf.Abs( scroll.y ) < threshold )
			return;

		if ( _cycleCooldown > 0f )
			return;

		float cycleSpeed = def != null ? def.itemCycleSpeed : 14f;
		_cycleCooldown = 1f / Mathf.Max( 0.1f, cycleSpeed );
		CycleActive( scroll.y > 0f ? -1 : 1 );
	}

	/// <summary>
	/// Rotates which item of the selected category is Active. Relative ring order is preserved.
	/// For coins, demoted Active meshes merge into the left-hand definition stack.
	/// </summary>
	public bool CycleActive( int direction )
	{
		CategoryBucket bucket = GetBucket( _selected );
		if ( !bucket.HasActive || bucket.Held.Count == 0 || direction == 0 )
			return false;

		_cycleBuffer.Clear();
		_cycleBuffer.Add( bucket.Active );
		for ( int i = 0; i < bucket.Held.Count; i++ )
			_cycleBuffer.Add( bucket.Held[ i ] );

		int n = _cycleBuffer.Count;
		int steps = direction % n;
		if ( steps < 0 )
			steps += n;
		if ( steps == 0 )
			return false;

		CarriedEntry newActive = _cycleBuffer[ steps ];
		bucket.Held.Clear();
		for ( int i = 1; i < n; i++ )
		{
			int src = ( steps + i ) % n;
			bucket.Held.Add( _cycleBuffer[ src ] );
		}

		bucket.Active = newActive;
		bucket.HasActive = true;

		bool promoteFromHeldTop = direction < 0;
		if ( bucket.Kind == CarryBucketKind.Coin )
			CompactCoinHeldToDefinitions( bucket );

		if ( bucket.HasActive && bucket.Active.Item != null )
		{
			bucket.Active.Item.SetMeshVisible( true );
			CarryDefinition def = Definition;
			float duration = def != null ? def.activeRefillTweenDuration : 0.22f;
			StartHoldTween( bucket.Active.Item, bucket.Active.Token, duration );
		}
		else if ( bucket.HasActive && bucket.Active.Definition != null )
		{
			if ( bucket.Kind == CarryBucketKind.Coin )
			{
				CarriedEntry active = bucket.Active;
				Transform hold = bucket.HoldRoot;
				Vector3 pos = hold != null ? hold.position : transform.position;
				Quaternion rot = hold != null ? hold.rotation : transform.rotation;
				if ( hold != null && promoteFromHeldTop )
				{
					float height = GetHeldCoinCylinderHeight( bucket );
					height += GetHeldStackStep( active.Definition );
					pos += hold.up * height;
				}

				TreasureItem rented = TreasureItemFactory.RentVisualCoin( active.Definition, pos, rot );
				if ( rented != null )
				{
					rented.BeginHold( this );
					rented.SetMeshVisible( true );
					rented.SetHeldShadows( enabled: false );
					rented.SetHeldLighting( enabled: true );
					active.Item = rented;
					bucket.Active = active;
					rented.transform.SetParent( bucket.ActiveRoot, true );
					CarryDefinition def = Definition;
					float duration = def != null ? def.activeRefillTweenDuration : 0.22f;
					StartHoldTween( rented, active.Token, duration );
				}
				else
				{
					SpawnHeldItemAsync( active.Definition, active.Token );
				}
			}
			else
			{
				SpawnHeldItemAsync( bucket.Active.Definition, bucket.Active.Token );
			}
		}

		RestackPoses();
		return true;
	}

	float EnsureCoinHandVariationSeed()
	{
		if ( _coinHandVariationSeed > 0.0001f )
			return _coinHandVariationSeed;

		int id = Mathf.Abs( GetInstanceID() );
		_coinHandVariationSeed = ( id % 9973 ) + 1;
		return _coinHandVariationSeed;
	}

	public float CoinHandVariationSeed => EnsureCoinHandVariationSeed();

	/// <summary>
	/// Removes Active + held coins as logical definitions without spawning individuals.
	/// </summary>
	public bool TryExtractAllCoinDefinitions(
		out List<TreasureDefinition> definitions,
		out Vector3 startWorldPos,
		out Quaternion startWorldRot )
	{
		definitions = new List<TreasureDefinition>();
		startWorldPos = transform.position;
		startWorldRot = transform.rotation;

		CategoryBucket bucket = GetBucket( CarryBucketKind.Coin );
		if ( bucket.Count == 0 )
			return false;

		CaptureCoinExtractPose( bucket, out startWorldPos, out startWorldRot );

		if ( bucket.HasActive && bucket.Active.Definition != null )
		{
			definitions.Add( bucket.Active.Definition );
			if ( bucket.Active.Item != null )
			{
				AbortHoldTween( bucket.Active.Token, snapToHand: false, bucket.Active.Item );
				TreasureItemFactory.Despawn( bucket.Active.Item );
			}

			_usedCapacity = Mathf.Max( 0, _usedCapacity - GetCost( bucket.Active.Definition ) );
		}

		for ( int i = 0; i < bucket.Held.Count; i++ )
		{
			CarriedEntry entry = bucket.Held[ i ];
			if ( entry.Definition == null )
				continue;

			definitions.Add( entry.Definition );
			if ( entry.Item != null )
			{
				AbortHoldTween( entry.Token, snapToHand: false, entry.Item );
				TreasureItemFactory.Despawn( entry.Item );
			}

			_usedCapacity = Mathf.Max( 0, _usedCapacity - GetCost( entry.Definition ) );
		}

		bucket.HasActive = false;
		bucket.Active = default;
		bucket.Held.Clear();
		CoinColumnCylinderBinder.ClearAndDestroy( ref bucket.HeldCylinder, null );
		RestackPoses();
		return definitions.Count > 0;
	}

	/// <summary>
	/// Removes Active plus the contiguous same-type run from held bottom→top.
	/// Stops at the first held coin that does not match Active. Remaining held coins stay.
	/// </summary>
	public bool TryExtractActiveConnectedCoinDefinitions(
		out List<TreasureDefinition> definitions,
		out Vector3 startWorldPos,
		out Quaternion startWorldRot )
	{
		definitions = new List<TreasureDefinition>();
		startWorldPos = transform.position;
		startWorldRot = transform.rotation;

		CategoryBucket bucket = GetBucket( CarryBucketKind.Coin );
		if ( !bucket.HasActive || bucket.Active.Definition == null )
			return false;

		TreasureDefinition activeDef = bucket.Active.Definition;
		CaptureCoinExtractPose( bucket, out startWorldPos, out startWorldRot );

		definitions.Add( activeDef );
		if ( bucket.Active.Item != null )
		{
			AbortHoldTween( bucket.Active.Token, snapToHand: false, bucket.Active.Item );
			TreasureItemFactory.Despawn( bucket.Active.Item );
		}

		_usedCapacity = Mathf.Max( 0, _usedCapacity - GetCost( activeDef ) );
		bucket.HasActive = false;
		bucket.Active = default;

		int connectedHeld = 0;
		for ( int i = 0; i < bucket.Held.Count; i++ )
		{
			if ( bucket.Held[ i ].Definition != activeDef )
				break;
			connectedHeld++;
		}

		for ( int i = 0; i < connectedHeld; i++ )
		{
			CarriedEntry entry = bucket.Held[ i ];
			definitions.Add( entry.Definition );
			if ( entry.Item != null )
			{
				AbortHoldTween( entry.Token, snapToHand: false, entry.Item );
				TreasureItemFactory.Despawn( entry.Item );
			}

			_usedCapacity = Mathf.Max( 0, _usedCapacity - GetCost( entry.Definition ) );
		}

		if ( connectedHeld > 0 )
			bucket.Held.RemoveRange( 0, connectedHeld );

		CoinColumnCylinderBinder.ClearAndDestroy( ref bucket.HeldCylinder, null );
		PromoteFromHeld( bucket );
		RestackPoses();
		return definitions.Count > 0;
	}

	/// <summary>
	/// Active coin definition plus contiguous same-type held coins from the bottom.
	/// </summary>
	public int CountActiveConnectedCoinDefinitions()
	{
		CategoryBucket bucket = GetBucket( CarryBucketKind.Coin );
		if ( !bucket.HasActive || bucket.Active.Definition == null )
			return 0;

		TreasureDefinition activeDef = bucket.Active.Definition;
		int count = 1;
		for ( int i = 0; i < bucket.Held.Count; i++ )
		{
			if ( bucket.Held[ i ].Definition != activeDef )
				break;
			count++;
		}

		return count;
	}

	/// <summary>Active coin definition even when the Active mesh is still pending spawn.</summary>
	public bool TryGetActiveCoinDefinition( out TreasureDefinition definition )
	{
		definition = null;
		CategoryBucket bucket = GetBucket( CarryBucketKind.Coin );
		if ( !bucket.HasActive || bucket.Active.Definition == null )
			return false;

		definition = bucket.Active.Definition;
		return true;
	}

	void CaptureCoinExtractPose( CategoryBucket bucket, out Vector3 startWorldPos, out Quaternion startWorldRot )
	{
		startWorldPos = transform.position;
		startWorldRot = transform.rotation;

		Transform hold = bucket.HoldRoot;
		if ( hold != null )
		{
			startWorldPos = hold.position;
			startWorldRot = hold.rotation;
			return;
		}

		if ( bucket.ActiveRoot != null )
		{
			startWorldPos = bucket.ActiveRoot.position;
			startWorldRot = bucket.ActiveRoot.rotation;
		}
	}

	/// <summary>
	/// Active then held bottom→top coin definitions without removing them from carry.
	/// </summary>
	public bool TryCollectCoinDefinitions( List<TreasureDefinition> into )
	{
		if ( into == null )
			return false;

		into.Clear();
		CategoryBucket bucket = GetBucket( CarryBucketKind.Coin );
		if ( bucket.Count == 0 )
			return false;

		if ( bucket.HasActive && bucket.Active.Definition != null )
			into.Add( bucket.Active.Definition );

		for ( int i = 0; i < bucket.Held.Count; i++ )
		{
			TreasureDefinition def = bucket.Held[ i ].Definition;
			if ( def != null )
				into.Add( def );
		}

		return into.Count > 0;
	}

	public bool BucketHasMatching( CarryBucketKind kind, System.Predicate<TreasureDefinition> match )
	{
		if ( match == null )
			return false;

		CategoryBucket bucket = GetBucket( kind );
		if ( bucket.Count == 0 )
			return false;

		if ( bucket.HasActive && bucket.Active.Definition != null && match( bucket.Active.Definition ) )
			return true;

		for ( int i = 0; i < bucket.Held.Count; i++ )
		{
			TreasureDefinition def = bucket.Held[ i ].Definition;
			if ( def != null && match( def ) )
				return true;
		}

		return false;
	}

	/// <summary>True when every entry shares the same <see cref="TreasureDefinition"/> reference.</summary>
	public static bool AreCoinDefinitionsUniform(
		IReadOnlyList<TreasureDefinition> definitions,
		out TreasureDefinition uniform )
	{
		uniform = null;
		if ( definitions == null || definitions.Count == 0 )
			return false;

		uniform = definitions[ 0 ];
		if ( uniform == null )
			return false;

		for ( int i = 1; i < definitions.Count; i++ )
		{
			if ( definitions[ i ] != uniform )
				return false;
		}

		return true;
	}

	/// <summary>
	/// Consumes up to <paramref name="maxCount"/> coin definitions (Active then held bottom→top)
	/// without spawning individuals. Remaining coins stay in the coin bucket.
	/// </summary>
	public int TryConsumeCoinDefinitions( int maxCount, List<TreasureDefinition> into )
	{
		if ( into == null || maxCount <= 0 )
			return 0;

		CategoryBucket bucket = GetBucket( CarryBucketKind.Coin );
		if ( bucket.Count <= 0 )
			return 0;

		_drainBuffer.Clear();
		if ( bucket.HasActive )
			_drainBuffer.Add( bucket.Active );
		for ( int i = 0; i < bucket.Held.Count; i++ )
			_drainBuffer.Add( bucket.Held[ i ] );

		int take = Mathf.Min( maxCount, _drainBuffer.Count );
		if ( take <= 0 )
		{
			_drainBuffer.Clear();
			return 0;
		}

		for ( int i = 0; i < _drainBuffer.Count; i++ )
		{
			CarriedEntry entry = _drainBuffer[ i ];
			if ( entry.Item == null )
				continue;
			AbortHoldTween( entry.Token, snapToHand: false, entry.Item );
			TreasureItemFactory.Despawn( entry.Item );
		}

		for ( int i = 0; i < take; i++ )
		{
			TreasureDefinition def = _drainBuffer[ i ].Definition;
			if ( def != null )
			{
				into.Add( def );
				_usedCapacity = Mathf.Max( 0, _usedCapacity - GetCost( def ) );
			}
		}

		bucket.HasActive = false;
		bucket.Active = default;
		bucket.Held.Clear();

		for ( int i = take; i < _drainBuffer.Count; i++ )
		{
			CarriedEntry entry = _drainBuffer[ i ];
			entry.Item = null;
			entry.PoseSettled = true;
			InsertEntry( bucket, entry, bucket.Held.Count, allowActive: false );
		}

		_drainBuffer.Clear();
		PromoteFromHeld( bucket );
		RestackPoses();
		return take;
	}

	/// <summary>
	/// Makes <paramref name="item"/> the Active (right-hand) coin, demoting any previous Active
	/// into the left-hand definition stack. Starts a hold tween from the item's current pose.
	/// </summary>
	public bool TryReceiveActiveCoinFromWorld( TreasureItem item )
	{
		if ( item == null || item.Definition == null || item.Definition.category != TreasureCategory.Coin )
			return false;

		CategoryBucket bucket = GetBucket( CarryBucketKind.Coin );
		if ( bucket.HoldRoot == null || bucket.ActiveRoot == null )
			return false;

		if ( ContainsItem( item ) )
			return false;

		if ( !CanAdd( item.Definition ) )
			return false;

		DemoteActiveCoinToHeldDefinitions( bucket );

		int cost = GetCost( item.Definition );
		int token = _nextToken++;
		_usedCapacity += cost;

		item.BeginHold( this );
		item.SetMeshVisible( true );
		item.SetHeldShadows( enabled: false );
		item.SetHeldLighting( enabled: true );

		CarriedEntry entry = new CarriedEntry
		{
			Definition = item.Definition,
			Item = item,
			Token = token,
			ClusterId = 0,
			IsClusterAnchor = false,
			PoseSettled = false
		};

		bucket.Active = entry;
		bucket.HasActive = true;
		item.transform.SetParent( bucket.ActiveRoot, true );
		NotifyIfNewlyDiscovered( item.Definition );

		CarryDefinition def = Definition;
		float duration = def != null ? def.coinFlipDuration : 0.32f;
		if ( duration < 0.05f )
			duration = def != null ? def.holdTweenDuration : 0.2f;
		StartHoldTween( item, token, duration, playPickupFeedback: true );

		TrySetSelectedBucket( CarryBucketKind.Coin );
		RestackPoses();
		return true;
	}

	void DemoteActiveCoinToHeldDefinitions( CategoryBucket bucket )
	{
		if ( bucket == null || !bucket.HasActive )
			return;

		CarriedEntry active = bucket.Active;
		TreasureItem visual = active.Item;
		bool flightInProgress = visual != null
			&& ( _holdTweens.ContainsKey( active.Token ) || visual.IsInFlight );

		bucket.HasActive = false;
		bucket.Active = default;

		if ( active.Definition == null )
		{
			if ( visual != null )
			{
				AbortHoldTween( active.Token, snapToHand: false, visual );
				TreasureItemFactory.Despawn( visual );
			}

			return;
		}

		if ( flightInProgress )
		{
			// Keep the live mesh + token so HoldTweenRoutine soft-retargets to HoldRoot
			// and finishes the full pickup flight instead of cutting off.
			active.PoseSettled = false;
			InsertEntry( bucket, active, 0, allowActive: false );
			return;
		}

		if ( visual != null )
		{
			AbortHoldTween( active.Token, snapToHand: false, visual );
			TreasureItemFactory.Despawn( visual );
		}

		active.Item = null;
		active.PoseSettled = true;
		InsertEntry( bucket, active, 0, allowActive: false );
	}

	/// <summary>
	/// Left-hand coins are always logical definitions + one cylinder. Despawn any live held meshes
	/// that are not mid-flight (in-flight demotes finish their pickup animation first).
	/// </summary>
	void CompactCoinHeldToDefinitions( CategoryBucket bucket )
	{
		if ( bucket == null || bucket.Kind != CarryBucketKind.Coin )
			return;

		for ( int i = 0; i < bucket.Held.Count; i++ )
		{
			CarriedEntry entry = bucket.Held[ i ];
			if ( entry.Item == null )
				continue;

			if ( _holdTweens.ContainsKey( entry.Token )
				|| ( entry.Item != null && entry.Item.IsInFlight ) )
				continue;

			AbortHoldTween( entry.Token, snapToHand: false, entry.Item );
			TreasureItemFactory.Despawn( entry.Item );
			entry.Item = null;
			entry.PoseSettled = true;
			bucket.Held[ i ] = entry;
		}
	}

	public void ReleaseTreasure( TreasureItem item )
	{
		if ( item == null )
			return;

		EnsureBuckets();
		for ( int b = 0; b < BucketCount; b++ )
		{
			CategoryBucket bucket = _buckets[ b ];

			if ( bucket.HasActive && bucket.Active.Item == item )
			{
				CarriedEntry activeEntry = bucket.Active;
				AbortHoldTween( activeEntry.Token, snapToHand: false, item );
				_usedCapacity = Mathf.Max( 0, _usedCapacity - GetCost( activeEntry.Definition ) );
				bucket.HasActive = false;
				bucket.Active = default;
				item.SetMeshVisible( true );
				item.EndFlight();
				PromoteFromHeld( bucket );
				RestackPoses();
				return;
			}

			for ( int i = 0; i < bucket.Held.Count; i++ )
			{
				if ( bucket.Held[ i ].Item != item )
					continue;

				CarriedEntry entry = bucket.Held[ i ];
				bucket.Held.RemoveAt( i );
				_usedCapacity = Mathf.Max( 0, _usedCapacity - GetCost( entry.Definition ) );
				AbortHoldTween( entry.Token, snapToHand: false, item );
				item.SetMeshVisible( true );
				item.EndFlight();
				RestackPoses();
				return;
			}
		}
	}

	public int GetCost( TreasureDefinition treasure )
	{
		if ( treasure == null )
			return 1;

		return Mathf.Max( 1, treasure.weight );
	}

	public bool CanAdd( TreasureDefinition treasure )
	{
		if ( treasure == null )
			return false;

		return PassesMixingRules( treasure, GetBucketFor( treasure ) );
	}

	public bool CanAdd( TreasureItem item )
	{
		if ( item == null )
			return false;

		return CanAdd( item.Definition );
	}

	/// <summary>
	/// How many units of the same definition still fit in carry (for multi-pull pile interacts).
	/// </summary>
	public int CountAffordableUnits( TreasureDefinition treasure, int maxUnits )
	{
		if ( treasure == null || maxUnits <= 0 )
			return 0;

		ResetAffordabilitySim();

		int bucketIndex = (int)ResolveBucket( treasure );
		int count = 0;
		for ( int i = 0; i < maxUnits; i++ )
		{
			if ( !SimulateAdd( treasure, bucketIndex ) )
				break;

			count++;
		}

		return count;
	}

	public bool CanAddAll( IReadOnlyList<TreasureItem> items )
	{
		if ( items == null || items.Count == 0 )
			return false;

		ResetAffordabilitySim();
		for ( int i = 0; i < items.Count; i++ )
		{
			TreasureItem item = items[ i ];
			if ( item == null )
				return false;

			TreasureDefinition def = item.Definition;
			if ( def == null )
				return false;

			if ( !SimulateAdd( def, (int)ResolveBucket( def ) ) )
				return false;
		}

		return true;
	}

	/// <summary>
	/// How many items from the start of a bottom-to-top list can still be added (mixing rules only).
	/// </summary>
	public int CountAffordablePrefix( IReadOnlyList<TreasureItem> orderedBottomToTop )
	{
		if ( orderedBottomToTop == null || orderedBottomToTop.Count == 0 )
			return 0;

		ResetAffordabilitySim();
		int count = 0;
		for ( int i = 0; i < orderedBottomToTop.Count; i++ )
		{
			TreasureItem item = orderedBottomToTop[ i ];
			if ( item == null )
				break;

			TreasureDefinition def = item.Definition;
			if ( def == null )
				break;

			if ( !SimulateAdd( def, (int)ResolveBucket( def ) ) )
				break;

			count++;
		}

		return count;
	}

	/// <summary>
	/// How many items from the end of a bottom-to-top list (the top of a stack) can still be added.
	/// </summary>
	public int CountAffordableSuffix( IReadOnlyList<TreasureItem> orderedBottomToTop )
	{
		if ( orderedBottomToTop == null || orderedBottomToTop.Count == 0 )
			return 0;

		ResetAffordabilitySim();
		int count = 0;
		for ( int i = orderedBottomToTop.Count - 1; i >= 0; i-- )
		{
			TreasureItem item = orderedBottomToTop[ i ];
			if ( item == null )
				break;

			TreasureDefinition def = item.Definition;
			if ( def == null )
				break;

			if ( !SimulateAdd( def, (int)ResolveBucket( def ) ) )
				break;

			count++;
		}

		return count;
	}

	/// <summary>
	/// How many definitions from the end of a bottom-to-top slot list (from top down to startIndex)
	/// can still be added (mixing rules only).
	/// </summary>
	public int CountAffordableDefinitionSuffix( IReadOnlyList<TreasureDefinition> orderedBottomToTop, int startIndex )
	{
		if ( orderedBottomToTop == null || orderedBottomToTop.Count == 0 )
			return 0;

		startIndex = Mathf.Clamp( startIndex, 0, orderedBottomToTop.Count - 1 );
		ResetAffordabilitySim();

		int count = 0;
		for ( int i = orderedBottomToTop.Count - 1; i >= startIndex; i-- )
		{
			TreasureDefinition def = orderedBottomToTop[ i ];
			if ( def == null )
				break;

			if ( !SimulateAdd( def, (int)ResolveBucket( def ) ) )
				break;

			count++;
		}

		return count;
	}

	void ResetAffordabilitySim()
	{
		EnsureBuckets();
		for ( int i = 0; i < BucketCount; i++ )
		{
			CategoryBucket bucket = _buckets[ i ];
			_simAny[ i ] = bucket.Count > 0;
			_simExclusive[ i ] = HasExclusiveCarried( bucket );
		}
	}

	/// <summary>Advances the per-bucket mixing simulation; false when the add would be rejected.</summary>
	bool SimulateAdd( TreasureDefinition def, int bucketIndex )
	{
		if ( _simExclusive[ bucketIndex ] )
			return false;

		if ( def.exclusiveCarry && _simAny[ bucketIndex ] )
			return false;

		if ( def.exclusiveCarry )
			_simExclusive[ bucketIndex ] = true;

		_simAny[ bucketIndex ] = true;
		return true;
	}

	/// <summary>Exclusive-carry applies inside a single bucket; categories never block each other.</summary>
	bool PassesMixingRules( TreasureDefinition treasure, CategoryBucket bucket )
	{
		if ( treasure == null || bucket == null )
			return false;

		if ( bucket.Count == 0 )
			return true;

		if ( treasure.exclusiveCarry )
			return false;

		return !HasExclusiveCarried( bucket );
	}

	static bool HasExclusiveCarried( CategoryBucket bucket )
	{
		if ( bucket.HasActive && bucket.Active.Definition != null && bucket.Active.Definition.exclusiveCarry )
			return true;

		for ( int i = 0; i < bucket.Held.Count; i++ )
		{
			TreasureDefinition def = bucket.Held[ i ].Definition;
			if ( def != null && def.exclusiveCarry )
				return true;
		}

		return false;
	}

	public bool TryAdd( TreasureDefinition treasure )
	{
		if ( !CanAdd( treasure ) )
			return false;

		CategoryBucket bucket = GetBucketFor( treasure );
		if ( bucket.HoldRoot == null || bucket.ActiveRoot == null )
			return false;

		int token = _nextToken++;
		_usedCapacity += GetCost( treasure );

		CarriedEntry entry = new CarriedEntry
		{
			Definition = treasure,
			Item = null,
			Token = token,
			ClusterId = 0,
			IsClusterAnchor = false
		};

		InsertEntry( bucket, entry, 0, allowActive: true );
		NotifyIfNewlyDiscovered( treasure );
		AutoSelectIfIdle( bucket );
		RestackPoses();
		SpawnHeldItemAsync( treasure, token );
		return true;
	}

	/// <summary>
	/// Batch-add definitions. Spawns only each bucket's active mesh; deeper stack entries spawn on promote.
	/// </summary>
	public int TryAddMany( IReadOnlyList<TreasureDefinition> definitions )
	{
		if ( definitions == null || definitions.Count == 0 )
			return 0;

		EnsureBuckets();
		int added = 0;
		for ( int i = 0; i < definitions.Count; i++ )
		{
			TreasureDefinition treasure = definitions[ i ];
			if ( treasure == null || !CanAdd( treasure ) )
				break;

			if ( !AddDefinitionEntry( treasure ) )
				break;

			added++;
		}

		if ( added <= 0 )
			return 0;

		SpawnPendingActives();
		RestackPoses();
		return added;
	}

	public int TryAddMany( TreasureDefinition treasure, int count )
	{
		if ( treasure == null || count <= 0 )
			return 0;

		EnsureBuckets();
		int added = 0;
		for ( int i = 0; i < count; i++ )
		{
			if ( !CanAdd( treasure ) )
				break;

			if ( !AddDefinitionEntry( treasure ) )
				break;

			added++;
		}

		if ( added <= 0 )
			return 0;

		SpawnPendingActives();
		RestackPoses();
		return added;
	}

	/// <summary>Active when the bucket is empty, otherwise the bottom of that bucket's held stack.</summary>
	bool AddDefinitionEntry( TreasureDefinition treasure )
	{
		CategoryBucket bucket = GetBucketFor( treasure );
		if ( bucket.HoldRoot == null || bucket.ActiveRoot == null )
			return false;

		int token = _nextToken++;
		_usedCapacity += GetCost( treasure );

		CarriedEntry entry = new CarriedEntry
		{
			Definition = treasure,
			Item = null,
			Token = token,
			ClusterId = 0,
			IsClusterAnchor = false
		};

		InsertEntry( bucket, entry, 0, allowActive: true );
		NotifyIfNewlyDiscovered( treasure );
		AutoSelectIfIdle( bucket );
		return true;
	}

	void SpawnPendingActives()
	{
		for ( int i = 0; i < BucketCount; i++ )
		{
			CategoryBucket bucket = _buckets[ i ];
			if ( bucket.HasActive && bucket.Active.Item == null && bucket.Active.Definition != null )
				SpawnHeldItemAsync( bucket.Active.Definition, bucket.Active.Token );
		}
	}

	public bool TryAddExisting( TreasureItem item )
	{
		if ( item == null || !CanAdd( item ) )
			return false;

		CategoryBucket bucket = GetBucketFor( item.Definition );
		if ( bucket.HoldRoot == null || bucket.ActiveRoot == null )
			return false;

		if ( ContainsItem( item ) )
			return false;

		// Always land in Active; push previous Active into left held bottom.
		if ( bucket.HasActive )
		{
			if ( bucket.Kind == CarryBucketKind.Coin )
				DemoteActiveCoinToHeldDefinitions( bucket );
			else
				DemoteActiveToHeldBottom( bucket );
		}

		if ( !AddExistingEntry( bucket, item, 0, allowActive: true, tweenDuration: -1f, out _ ) )
			return false;

		ForceSelectBucket( bucket );
		// Shifted items must restack; entries with running hold tweens are skipped.
		RestackPoses();
		return true;
	}

	/// <summary>
	/// Moves current Active into held bottom (index 0), keeping live meshes for gems/artifacts.
	/// </summary>
	void DemoteActiveToHeldBottom( CategoryBucket bucket )
	{
		if ( bucket == null || !bucket.HasActive )
			return;

		CarriedEntry active = bucket.Active;
		bucket.HasActive = false;
		bucket.Active = default;

		if ( active.Definition == null )
		{
			if ( active.Item != null )
			{
				AbortHoldTween( active.Token, snapToHand: false, active.Item );
				TreasureItemFactory.Despawn( active.Item );
			}

			return;
		}

		active.PoseSettled = false;
		InsertEntry( bucket, active, 0, allowActive: false );

		if ( active.Item == null )
			return;

		CarryDefinition def = Definition;
		float duration = def != null ? def.activeRefillTweenDuration : 0.22f;
		if ( !_holdTweens.ContainsKey( active.Token ) )
		{
			active.Item.transform.SetParent( bucket.HoldRoot, true );
			StartHoldTween( active.Item, active.Token, duration );
		}
	}

	/// <summary>
	/// Adds a support stack (bottom-to-top). With an empty bucket the aimed/bottom item becomes Active
	/// and the rest fill the held stack in order; otherwise the whole batch inserts at the held bottom.
	/// </summary>
	public bool TryAddSupportStack( IReadOnlyList<TreasureItem> orderedBottomToTop )
	{
		if ( orderedBottomToTop == null || orderedBottomToTop.Count == 0 )
			return false;

		EnsureBuckets();
		ClearInsertCursors();

		bool any = false;
		for ( int i = 0; i < orderedBottomToTop.Count; i++ )
		{
			TreasureItem item = orderedBottomToTop[ i ];
			if ( item == null )
				continue;

			if ( !CanAdd( item ) )
				break;

			if ( ContainsItem( item ) )
				continue;

			CategoryBucket bucket = GetBucketFor( item.Definition );
			if ( bucket.HoldRoot == null || bucket.ActiveRoot == null )
				break;

			int index = (int)bucket.Kind;
			if ( !AddExistingEntry( bucket, item, _insertCursor[ index ], allowActive: true, tweenDuration: -1f, out bool becameActive ) )
				break;

			if ( !becameActive )
				_insertCursor[ index ]++;

			_bucketTouched[ index ] = true;
			any = true;
		}

		if ( !any )
			return false;

		for ( int i = 0; i < BucketCount; i++ )
		{
			if ( _bucketTouched[ i ] )
				AutoSelectIfIdle( _buckets[ i ] );
		}

		RestackPoses();
		return true;
	}

	/// <summary>
	/// Merges a whole stack into the bottom of its category's held stack as a block.
	/// Buckets left without an Active promote their new bottom item into the right hand.
	/// </summary>
	public bool TryAbsorbAtHeldBottom( IReadOnlyList<TreasureItem> orderedBottomToTop )
	{
		if ( orderedBottomToTop == null || orderedBottomToTop.Count == 0 )
			return false;

		EnsureBuckets();
		ClearInsertCursors();

		CarryDefinition carryDef = Definition;
		float absorbDuration = carryDef != null ? carryDef.wholeStackAbsorbTweenDuration : 0.28f;

		bool any = false;
		for ( int i = 0; i < orderedBottomToTop.Count; i++ )
		{
			TreasureItem item = orderedBottomToTop[ i ];
			if ( item == null )
				continue;

			if ( !CanAdd( item ) )
				break;

			if ( ContainsItem( item ) )
				continue;

			CategoryBucket bucket = GetBucketFor( item.Definition );
			if ( bucket.HoldRoot == null || bucket.ActiveRoot == null )
				break;

			int index = (int)bucket.Kind;
			if ( !AddExistingEntry( bucket, item, _insertCursor[ index ], allowActive: false, absorbDuration, out _ ) )
				break;

			_insertCursor[ index ]++;
			_bucketTouched[ index ] = true;
			any = true;
		}

		if ( !any )
			return false;

		for ( int i = 0; i < BucketCount; i++ )
		{
			if ( !_bucketTouched[ i ] )
				continue;

			CategoryBucket bucket = _buckets[ i ];
			PromoteFromHeld( bucket );
			AutoSelectIfIdle( bucket );
		}

		RestackPoses();
		return true;
	}

	/// <summary>
	/// Merges logical definitions (no spawned meshes) into the bottom of <paramref name="bucketKind"/>.
	/// When <paramref name="promoteIfEmpty"/> is true and Active is empty, promotes held[0].
	/// </summary>
	public bool TryAbsorbDefinitionsAtHeldBottom(
		IReadOnlyList<TreasureDefinition> orderedBottomToTop,
		CarryBucketKind bucketKind,
		bool promoteIfEmpty = true )
	{
		if ( orderedBottomToTop == null || orderedBottomToTop.Count == 0 )
			return false;

		CategoryBucket bucket = GetBucket( bucketKind );
		if ( bucket.HoldRoot == null || bucket.ActiveRoot == null )
			return false;

		int insertAt = 0;
		bool any = false;
		for ( int i = 0; i < orderedBottomToTop.Count; i++ )
		{
			TreasureDefinition def = orderedBottomToTop[ i ];
			if ( def == null )
				continue;

			if ( !PassesMixingRules( def, bucket ) )
				break;

			int token = _nextToken++;
			_usedCapacity += GetCost( def );

			CarriedEntry entry = new CarriedEntry
			{
				Definition = def,
				Item = null,
				Token = token,
				ClusterId = 0,
				IsClusterAnchor = false
			};

			InsertEntry( bucket, entry, insertAt, allowActive: false );
			NotifyIfNewlyDiscovered( def );
			insertAt++;
			any = true;
		}

		if ( !any )
			return false;

		if ( promoteIfEmpty )
			PromoteFromHeld( bucket );

		AutoSelectIfIdle( bucket );
		RestackPoses();
		return true;
	}

	/// <summary>
	/// Empties the selected category in dump order (Active first, then held bottom→top).
	/// </summary>
	public bool TryRemoveAllFromSelected( out List<TreasureItem> items )
	{
		// Snapshot — callers keep this list across async place motions.
		items = new List<TreasureItem>();

		CategoryBucket bucket = GetBucket( _selected );
		if ( bucket.Count == 0 )
			return false;

		_drainBuffer.Clear();
		if ( bucket.HasActive )
			_drainBuffer.Add( bucket.Active );

		for ( int i = 0; i < bucket.Held.Count; i++ )
			_drainBuffer.Add( bucket.Held[ i ] );

		bool firstWasActive = bucket.HasActive;
		bucket.HasActive = false;
		bucket.Active = default;
		bucket.Held.Clear();

		for ( int i = 0; i < _drainBuffer.Count; i++ )
		{
			CarriedEntry entry = _drainBuffer[ i ];
			bool activeParent = firstWasActive && i == 0;
			TreasureItem live = ResolveLiveItem( bucket, entry, activeParent );
			if ( live == null )
			{
				_usedCapacity = Mathf.Max( 0, _usedCapacity - GetCost( entry.Definition ) );
				AbortHoldTween( entry.Token, snapToHand: false, null );
				continue;
			}

			DetachResolvedEntry( entry, live );
			items.Add( live );
		}

		_drainBuffer.Clear();
		RestackPoses();
		return items.Count > 0;
	}

	void ClearInsertCursors()
	{
		for ( int i = 0; i < BucketCount; i++ )
		{
			_insertCursor[ i ] = 0;
			_bucketTouched[ i ] = false;
		}
	}

	/// <summary>
	/// Places <paramref name="entry"/> in the bucket. An empty Active slot claims the entry when
	/// <paramref name="allowActive"/>; otherwise it inserts into the held stack without demoting Active.
	/// </summary>
	bool InsertEntry( CategoryBucket bucket, CarriedEntry entry, int heldInsertIndex, bool allowActive )
	{
		if ( allowActive && !bucket.HasActive )
		{
			bucket.Active = entry;
			bucket.HasActive = true;
			return true;
		}

		bucket.Held.Insert( Mathf.Clamp( heldInsertIndex, 0, bucket.Held.Count ), entry );
		return false;
	}

	bool AddExistingEntry(
		CategoryBucket bucket,
		TreasureItem item,
		int heldInsertIndex,
		bool allowActive,
		float tweenDuration,
		out bool becameActive )
	{
		becameActive = false;
		if ( bucket == null || item == null )
			return false;

		int token = _nextToken++;
		_usedCapacity += GetCost( item.Definition );

		item.BeginHold( this );
		item.SetMeshVisible( true );
		item.SetHeldShadows( enabled: false );
		item.SetHeldLighting( enabled: true );

		CarriedEntry entry = new CarriedEntry
		{
			Definition = item.Definition,
			Item = item,
			Token = token,
			ClusterId = 0,
			IsClusterAnchor = false
		};

		becameActive = InsertEntry( bucket, entry, heldInsertIndex, allowActive );
		item.transform.SetParent( GetItemParent( bucket, becameActive ), true );
		NotifyIfNewlyDiscovered( item.Definition );
		StartHoldTween( item, token, tweenDuration, playPickupFeedback: true );

		// Coin left-hand merges still fly fully; HoldTweenRoutine collapses the mesh on arrival.
		return true;
	}

	void NotifyIfNewlyDiscovered( TreasureDefinition definition )
	{
		if ( definition == null )
			return;

		if ( !TreasureDiscoveryProgress.TryMarkDiscovered( definition ) )
			return;

		if ( definition.suppressDiscoveryPopup )
			return;

		if ( ResolveBucket( definition ) != _selected )
		{
			string id = TreasureId( definition );
			if ( !string.IsNullOrEmpty( id ) )
				_newTreasureIds.Add( id );
		}

		EventBus.Publish( new TreasureDiscoveredEvent { Treasure = definition } );
		DiscoveryToastUI.NotifyNewTreasure( definition );
	}

	/// <summary>General pouch is HUD-only while it has items; snap off it when emptied.</summary>
	void EnsureSelectedPouchValid()
	{
		if ( _selected != CarryBucketKind.General )
			return;
		if ( GetBucketCount( CarryBucketKind.General ) > 0 )
			return;

		for ( int i = 0; i < (int)CarryBucketKind.General; i++ )
		{
			CarryBucketKind kind = (CarryBucketKind)i;
			if ( GetBucketCount( kind ) <= 0 )
				continue;

			TrySetSelectedBucket( kind );
			return;
		}

		TrySetSelectedBucket( CarryBucketKind.Coin );
	}

	float GetHeldCoinCylinderHeight( CategoryBucket bucket )
	{
		if ( bucket == null || bucket.Held.Count == 0 )
			return 0f;

		CarryDefinition def = Definition;
		int visualMax = def != null ? Mathf.Max( 1, def.heldVisualMaxCoins ) : 40;
		int limit = Mathf.Min( bucket.Held.Count, visualMax );
		float height = 0f;
		for ( int i = 0; i < limit; i++ )
			height += GetHeldStackStep( bucket.Held[ i ].Definition, def );
		return height;
	}

	/// <summary>Keeps an empty selection from hiding a pickup that landed in another category.</summary>
	void AutoSelectIfIdle( CategoryBucket bucket )
	{
		ForceSelectBucket( bucket );
	}

	/// <summary>Always switches pouch to the destination category on pickup.</summary>
	void ForceSelectBucket( CategoryBucket bucket )
	{
		if ( bucket == null || bucket.Kind == _selected )
			return;

		if ( bucket.Count == 0 )
			return;

		TrySetSelectedBucket( bucket.Kind );
	}

	void PromoteFromHeld( CategoryBucket bucket )
	{
		if ( bucket.HasActive || bucket.Held.Count == 0 )
			return;

		CarriedEntry entry = bucket.Held[ 0 ];
		bucket.Held.RemoveAt( 0 );
		entry.PoseSettled = false;
		bucket.Active = entry;
		bucket.HasActive = true;

		if ( entry.Item == null )
		{
			if ( entry.Definition == null )
				return;

			if ( bucket.Kind == CarryBucketKind.Coin )
			{
				Transform hold = bucket.HoldRoot;
				Vector3 pos = hold != null ? hold.position : transform.position;
				Quaternion rot = hold != null ? hold.rotation : transform.rotation;
				TreasureItem rented = TreasureItemFactory.RentVisualCoin( entry.Definition, pos, rot );
				if ( rented == null )
				{
					SpawnHeldItemAsync( entry.Definition, entry.Token );
					return;
				}

				rented.BeginHold( this );
				rented.SetMeshVisible( true );
				rented.SetHeldShadows( enabled: false );
				rented.SetHeldLighting( enabled: true );
				entry.Item = rented;
				bucket.Active = entry;
				rented.transform.SetParent( bucket.ActiveRoot, true );

				CarryDefinition def = Definition;
				float duration = def != null ? def.activeRefillTweenDuration : 0.22f;
				StartHoldTween( rented, entry.Token, duration );
				return;
			}

			SpawnHeldItemAsync( entry.Definition, entry.Token );
			return;
		}

		// Cylinder-covered coins hide their own mesh while in the left hand.
		entry.Item.SetMeshVisible( true );

		CarryDefinition carryDef = Definition;
		float tweenDuration = carryDef != null ? carryDef.activeRefillTweenDuration : 0.22f;
		StartHoldTween( entry.Item, entry.Token, tweenDuration );
	}

	public bool TryPeekActive( out TreasureDefinition definition )
	{
		return TryPeekActive( out definition, out _ );
	}

	public bool TryPeekActive( out TreasureItem item )
	{
		return TryPeekActive( out _, out item );
	}

	public bool TryPeekActive( out TreasureDefinition definition, out TreasureItem item )
	{
		definition = null;
		item = null;

		CategoryBucket bucket = GetBucket( _selected );
		if ( !bucket.HasActive )
			return false;

		// Pending async spawns reserve capacity but are not placeable until bound.
		if ( bucket.Active.Item == null )
			return false;

		definition = bucket.Active.Definition;
		item = bucket.Active.Item;
		return true;
	}

	/// <summary>Legacy alias — placeable item is Active, not stack bottom.</summary>
	public bool TryPeekBottom( out TreasureDefinition definition )
	{
		return TryPeekActive( out definition );
	}

	public bool TryPeekBottom( out TreasureItem item )
	{
		return TryPeekActive( out item );
	}

	public bool TryPeekBottom( out TreasureDefinition definition, out TreasureItem item )
	{
		return TryPeekActive( out definition, out item );
	}

	public bool TryPeekTop( out TreasureDefinition definition )
	{
		return TryPeekTop( out definition, out _ );
	}

	public bool TryPeekTop( out TreasureItem item )
	{
		return TryPeekTop( out _, out item );
	}

	public bool TryPeekTop( out TreasureDefinition definition, out TreasureItem item )
	{
		definition = null;
		item = null;

		CategoryBucket bucket = GetBucket( _selected );
		if ( bucket.Held.Count > 0 )
		{
			CarriedEntry entry = bucket.Held[ bucket.Held.Count - 1 ];
			if ( entry.Item == null )
				return false;

			definition = entry.Definition;
			item = entry.Item;
			return true;
		}

		return TryPeekActive( out definition, out item );
	}

	public bool TryConsumeActive( out TreasureItem item )
	{
		item = null;

		CategoryBucket bucket = GetBucket( _selected );
		if ( !bucket.HasActive )
			return false;

		CarriedEntry entry = bucket.Active;
		item = ResolveLiveItem( bucket, entry, activeParent: true );
		if ( item == null )
			return false;

		AbortHoldTween( entry.Token, snapToHand: false, item );
		bucket.HasActive = false;
		bucket.Active = default;
		_usedCapacity = Mathf.Max( 0, _usedCapacity - GetCost( entry.Definition ) );

		item.SetMeshVisible( true );
		item.SetHeldShadows( enabled: true );
		item.SetHeldLighting( enabled: false );
		item.transform.SetParent( null, true );
		item.BeginFlight();

		PromoteFromHeld( bucket );
		RestackPoses();
		return true;
	}

	/// <summary>Legacy alias — consume Active.</summary>
	public bool TryRemoveBottom( out TreasureItem item )
	{
		return TryConsumeActive( out item );
	}

	public bool TryRemoveTop( out TreasureItem item )
	{
		item = null;

		CategoryBucket bucket = GetBucket( _selected );
		if ( bucket.Held.Count > 0 )
		{
			int index = bucket.Held.Count - 1;
			CarriedEntry entry = bucket.Held[ index ];
			item = ResolveLiveItem( bucket, entry, activeParent: false );
			if ( item == null )
				return false;

			bucket.Held.RemoveAt( index );
			DetachResolvedEntry( entry, item );
			RestackPoses();
			return true;
		}

		return TryConsumeActive( out item );
	}

	public bool TryRemoveTopCluster( out List<TreasureItem> items )
	{
		// Snapshot — callers keep this list across async place/throw motions.
		items = new List<TreasureItem>( 1 );

		if ( !TryRemoveTop( out TreasureItem single ) || single == null )
			return false;

		items.Add( single );
		return true;
	}

	public bool TryRemoveBottomCluster( out List<TreasureItem> items )
	{
		// Snapshot — callers keep this list across async place/throw motions.
		items = new List<TreasureItem>( 1 );

		if ( !TryConsumeActive( out TreasureItem single ) || single == null )
			return false;

		items.Add( single );
		return true;
	}

	/// <summary>
	/// Snapshot of the selected category in dump/place order: Active first, then held bottom→top.
	/// </summary>
	public void CopyCarriedItemsInOrder( List<TreasureItem> buffer )
	{
		CopyBucketItemsInOrder( _selected, buffer );
	}

	/// <summary>
	/// Snapshot of one category in dump/place order: Active first, then held bottom→top.
	/// </summary>
	public void CopyBucketItemsInOrder( CarryBucketKind kind, List<TreasureItem> buffer )
	{
		if ( buffer == null )
			return;

		buffer.Clear();

		CategoryBucket bucket = GetBucket( kind );
		if ( bucket.HasActive && bucket.Active.Item != null )
			buffer.Add( bucket.Active.Item );

		for ( int i = 0; i < bucket.Held.Count; i++ )
		{
			TreasureItem heldItem = bucket.Held[ i ].Item;
			if ( heldItem != null )
				buffer.Add( heldItem );
		}
	}

	/// <summary>
	/// Groups held definitions in the bucket for HUD summary (includes coin definition-only slots).
	/// </summary>
	public void BuildBucketSummary( CarryBucketKind kind, List<TreasureDefinition> uniqueDefs, List<int> counts )
	{
		if ( uniqueDefs == null || counts == null )
			return;

		uniqueDefs.Clear();
		counts.Clear();

		CategoryBucket bucket = GetBucket( kind );
		if ( bucket.HasActive )
			AccumulateSummary( bucket.Active.Definition, uniqueDefs, counts );

		for ( int i = 0; i < bucket.Held.Count; i++ )
			AccumulateSummary( bucket.Held[ i ].Definition, uniqueDefs, counts );
	}

	static void AccumulateSummary( TreasureDefinition definition, List<TreasureDefinition> uniqueDefs, List<int> counts )
	{
		if ( definition == null )
			return;

		for ( int i = 0; i < uniqueDefs.Count; i++ )
		{
			if ( uniqueDefs[ i ] == definition )
			{
				counts[ i ]++;
				return;
			}
		}

		uniqueDefs.Add( definition );
		counts.Add( 1 );
	}

	/// <summary>Coin-bucket snapshot for the sorting hopper.</summary>
	public void CopyCoinsInOrder( List<TreasureItem> buffer )
	{
		CopyBucketItemsInOrder( CarryBucketKind.Coin, buffer );
	}

	/// <summary>
	/// Removes a specific carried item for transfer to another owner (place / dump).
	/// </summary>
	public bool TryDetachItem( TreasureItem item )
	{
		if ( item == null )
			return false;

		EnsureBuckets();
		for ( int b = 0; b < BucketCount; b++ )
		{
			CategoryBucket bucket = _buckets[ b ];

			if ( bucket.HasActive && bucket.Active.Item == item )
			{
				if ( bucket.Kind == _selected )
					return TryConsumeActive( out _ );

				CarriedEntry activeEntry = bucket.Active;
				bucket.HasActive = false;
				bucket.Active = default;
				DetachResolvedEntry( activeEntry, item );
				PromoteFromHeld( bucket );
				RestackPoses();
				return true;
			}

			for ( int i = 0; i < bucket.Held.Count; i++ )
			{
				if ( bucket.Held[ i ].Item != item )
					continue;

				CarriedEntry entry = bucket.Held[ i ];
				TreasureItem live = ResolveLiveItem( bucket, entry, activeParent: false );
				if ( live == null )
					return false;

				bucket.Held.RemoveAt( i );
				DetachResolvedEntry( entry, live );
				RestackPoses();
				return true;
			}
		}

		return false;
	}

	TreasureItem ResolveLiveItem( CategoryBucket bucket, CarriedEntry entry, bool activeParent )
	{
		TreasureItem item = entry.Item;
		if ( item != null )
			return item;

		if ( entry.Definition == null )
			return null;

		Transform parent = GetItemParent( bucket, activeParent );
		Vector3 pos = parent != null ? parent.position : transform.position;
		Quaternion rot = parent != null ? parent.rotation : Quaternion.identity;
		item = TreasureItemFactory.SpawnFallback( entry.Definition, pos, rot, null );
		if ( item != null )
			item.BeginHold( this );

		return item;
	}

	void DetachResolvedEntry( CarriedEntry entry, TreasureItem item )
	{
		_usedCapacity = Mathf.Max( 0, _usedCapacity - GetCost( entry.Definition ) );
		AbortHoldTween( entry.Token, snapToHand: false, item );

		item.SetMeshVisible( true );
		item.SetHeldShadows( enabled: true );
		item.SetHeldLighting( enabled: false );
		item.transform.SetParent( null, true );
		item.BeginFlight();
	}

	public void Clear()
	{
		EnsureBuckets();
		AbortAllHoldTweens( snapToHand: false );

		for ( int b = 0; b < BucketCount; b++ )
		{
			CategoryBucket bucket = _buckets[ b ];

			if ( bucket.HasActive && bucket.Active.Item != null )
				TreasureItemFactory.Despawn( bucket.Active.Item );

			for ( int i = 0; i < bucket.Held.Count; i++ )
			{
				if ( bucket.Held[ i ].Item != null )
					TreasureItemFactory.Despawn( bucket.Held[ i ].Item );
			}

			bucket.HasActive = false;
			bucket.Active = default;
			bucket.Held.Clear();
			bucket.CylinderBuffer.Clear();
			CoinColumnCylinderBinder.ClearAndDestroy( ref bucket.HeldCylinder, null );
		}

		_usedCapacity = 0;
	}

	async void SpawnHeldItemAsync( TreasureDefinition treasure, int token )
	{
		Transform spawnParent = null;
		if ( TryFindEntry( token, out CategoryBucket spawnBucket, out bool spawnActive, out _ ) )
			spawnParent = GetItemParent( spawnBucket, spawnActive );

		Vector3 spawnPos = spawnParent != null ? spawnParent.position : transform.position;
		Quaternion spawnRot = spawnParent != null ? spawnParent.rotation : Quaternion.identity;
		TreasureItem item = await TreasureItemFactory.SpawnAsync( treasure, spawnPos, spawnRot, null );

		if ( this == null )
		{
			if ( item != null )
				TreasureItemFactory.Despawn( item );
			return;
		}

		if ( !TryFindEntry( token, out CategoryBucket bucket, out bool isActive, out int heldIndex ) )
		{
			if ( item != null )
				TreasureItemFactory.Despawn( item );
			return;
		}

		if ( item == null )
		{
			RemovePendingEntry( token );
			return;
		}

		item.BeginHold( this );
		item.ApplyHeldScale();
		item.SetHeldShadows( enabled: false );
		item.SetHeldLighting( enabled: true );

		if ( isActive )
		{
			CarriedEntry entry = bucket.Active;
			entry.Item = item;
			bucket.Active = entry;
			AttachImmediate( bucket, item, active: true, heldIndex: -1 );
		}
		else
		{
			CarriedEntry entry = bucket.Held[ heldIndex ];
			entry.Item = item;
			bucket.Held[ heldIndex ] = entry;
			AttachImmediate( bucket, item, active: false, heldIndex: heldIndex );
		}
	}

	void RemovePendingEntry( int token )
	{
		if ( !TryFindEntry( token, out CategoryBucket bucket, out bool isActive, out int heldIndex ) )
			return;

		if ( isActive )
		{
			_usedCapacity = Mathf.Max( 0, _usedCapacity - GetCost( bucket.Active.Definition ) );
			bucket.HasActive = false;
			bucket.Active = default;
			PromoteFromHeld( bucket );
			RestackPoses();
			return;
		}

		CarriedEntry entry = bucket.Held[ heldIndex ];
		bucket.Held.RemoveAt( heldIndex );
		_usedCapacity = Mathf.Max( 0, _usedCapacity - GetCost( entry.Definition ) );
		RestackPoses();
	}

	void StartHoldTween( TreasureItem item, int token, float durationOverride, bool playPickupFeedback = false )
	{
		if ( _holdTweens.TryGetValue( token, out Coroutine existing ) && existing != null )
			StopCoroutine( existing );

		if ( item != null )
			item.BeginFlight();

		if ( playPickupFeedback && item != null )
			TreasureInteractSfx.PlayPickup( item );

		Coroutine routine = StartCoroutine( HoldTweenRoutine( item, token, durationOverride, playPickupFeedback ) );
		_holdTweens[ token ] = routine;
	}

	void AbortHoldTween( int token, bool snapToHand )
	{
		AbortHoldTween( token, snapToHand, null );
	}

	void AbortHoldTween( int token, bool snapToHand, TreasureItem knownItem )
	{
		if ( !_holdTweens.TryGetValue( token, out Coroutine routine ) )
		{
			if ( !snapToHand && knownItem != null )
				knownItem.EndFlight();
			return;
		}

		if ( routine != null )
			StopCoroutine( routine );
		_holdTweens.Remove( token );

		TreasureItem item = knownItem;
		if ( item == null && TryFindEntry( token, out CategoryBucket bucket, out bool isActive, out int heldIndex ) )
			item = isActive ? bucket.Active.Item : bucket.Held[ heldIndex ].Item;

		if ( item == null )
			return;

		if ( snapToHand && TryFindEntry( token, out CategoryBucket snapBucket, out bool snapActive, out int snapHeldIndex ) )
		{
			item.EndFlight();
			AttachImmediate( snapBucket, item, snapActive, snapHeldIndex );
			return;
		}

		item.EndFlight();
	}

	void AbortAllHoldTweens( bool snapToHand )
	{
		if ( _holdTweens.Count == 0 )
			return;

		_tweenAbortBuffer.Clear();
		foreach ( KeyValuePair<int, Coroutine> pair in _holdTweens )
			_tweenAbortBuffer.Add( pair.Key );

		for ( int i = 0; i < _tweenAbortBuffer.Count; i++ )
			AbortHoldTween( _tweenAbortBuffer[ i ], snapToHand );
	}

	IEnumerator HoldTweenRoutine( TreasureItem item, int token, float durationOverride, bool playPickupFeedback )
	{
		if ( item == null || !TryFindEntry( token, out CategoryBucket bucket, out bool isActive, out int heldIndex ) )
		{
			_holdTweens.Remove( token );
			if ( item != null )
				item.EndFlight();
			yield break;
		}

		Transform parent = GetItemParent( bucket, isActive );
		if ( parent == null )
		{
			_holdTweens.Remove( token );
			item.EndFlight();
			yield break;
		}

		Transform t = item.transform;
		t.SetParent( parent, true );

		Vector3 startLocalPos = t.localPosition;
		Quaternion startLocalRot = t.localRotation;
		Vector3 startLocalScale = t.localScale;

		CarriedEntry entry = isActive ? bucket.Active : bucket.Held[ heldIndex ];
		bool keepWorldScale = entry.IsClusterAnchor || entry.ClusterId != 0;
		Vector3 endLocalPos = GetLocalPose( bucket, isActive, heldIndex );
		Vector3 endLocalScale = keepWorldScale ? item.GetWorldScale() : item.GetHeldScale();

		CarryDefinition def = Definition;
		bool hasOverride = durationOverride > 0.0001f;
		// Pickup flights always use the full coin flip; hand refills use a short arc.
		bool flipCoin = CoinFlipMotion.IsCoin( item )
			&& ( playPickupFeedback || !hasOverride );

		float duration;
		float arcHeight;
		float spins = 0f;
		if ( flipCoin )
		{
			float configured = def != null ? def.coinFlipDuration : CoinFlipMotion.DefaultDuration;
			if ( configured < 0.05f )
				configured = def != null ? def.holdTweenDuration : 0.2f;
			// Prefer the longer of override vs configured so an early demote still finishes a full flip.
			duration = Mathf.Max( 0.05f, hasOverride ? Mathf.Max( durationOverride, configured ) : configured );
			arcHeight = def != null ? def.coinFlipArcHeight : CoinFlipMotion.DefaultArcHeight;
			spins = def != null ? def.coinFlipSpins : CoinFlipMotion.DefaultSpins;
		}
		else
		{
			float configured = hasOverride ? durationOverride : ( def != null ? def.holdTweenDuration : 0.2f );
			duration = Mathf.Max( 0.05f, configured );
			arcHeight = def != null ? def.itemArcHeight : CoinFlipMotion.DefaultItemArcHeight;
		}

		float elapsed = 0f;
		float originalDuration = duration;

		while ( elapsed < duration )
		{
			if ( !TryFindEntry( token, out bucket, out isActive, out heldIndex ) )
			{
				// Still flying into hand — do not EndFlight; place/consume owns the item.
				_holdTweens.Remove( token );
				yield break;
			}

			Transform desiredParent = GetItemParent( bucket, isActive );
			if ( desiredParent != null && t.parent != desiredParent )
			{
				// Soft retarget between hands — keep remaining flight time so pickups aren't cut short.
				t.SetParent( desiredParent, true );
				startLocalPos = t.localPosition;
				startLocalRot = t.localRotation;
				startLocalScale = t.localScale;
				float remaining = Mathf.Max( 0.05f, originalDuration - elapsed );
				duration = remaining;
				elapsed = 0f;
				originalDuration = duration;
				if ( CoinFlipMotion.IsCoin( item ) && playPickupFeedback )
				{
					flipCoin = true;
					arcHeight = def != null ? def.coinFlipArcHeight : CoinFlipMotion.DefaultArcHeight;
					spins = def != null ? def.coinFlipSpins : CoinFlipMotion.DefaultSpins;
				}
				else
				{
					flipCoin = false;
					float itemArc = def != null ? def.itemArcHeight : CoinFlipMotion.DefaultItemArcHeight;
					arcHeight = Mathf.Min( arcHeight, itemArc );
				}
			}

			elapsed += Time.deltaTime;
			float u = Mathf.Clamp01( elapsed / duration );

			endLocalPos = GetLocalPose( bucket, isActive, heldIndex );
			entry = isActive ? bucket.Active : bucket.Held[ heldIndex ];
			keepWorldScale = entry.IsClusterAnchor || entry.ClusterId != 0;
			endLocalScale = keepWorldScale ? item.GetWorldScale() : item.GetHeldScale();
			Quaternion endLocalRot = item.GetHeldLocalRotation( isActive );

			if ( flipCoin )
			{
				CoinFlipMotion.EvaluateLocalFlip(
					startLocalPos,
					startLocalRot,
					endLocalPos,
					endLocalRot,
					u,
					arcHeight,
					spins,
					out Vector3 localPos,
					out Quaternion localRot );
				t.localPosition = localPos;
				t.localRotation = localRot;
			}
			else
			{
				t.localPosition = CoinFlipMotion.EvaluateLocalArc( startLocalPos, endLocalPos, u, arcHeight );
				t.localRotation = Quaternion.Slerp( startLocalRot, endLocalRot, CoinFlipMotion.SmoothStep( u ) );
			}

			float scaleEase = CoinFlipMotion.SmoothStep( u );
			t.localScale = Vector3.Lerp( startLocalScale, endLocalScale, scaleEase );

			yield return null;
		}

		_holdTweens.Remove( token );

		if ( !TryFindEntry( token, out bucket, out isActive, out heldIndex ) )
			yield break;

		item.EndFlight();

		// Coin left-hand finish: collapse mesh into the cylinder definition stack.
		if ( bucket.Kind == CarryBucketKind.Coin && !isActive )
		{
			if ( playPickupFeedback )
				CoinGemInteractFeedback.PlayPickup( item );

			TreasureItemFactory.Despawn( item );
			if ( heldIndex >= 0 && heldIndex < bucket.Held.Count )
			{
				CarriedEntry held = bucket.Held[ heldIndex ];
				held.Item = null;
				held.PoseSettled = true;
				bucket.Held[ heldIndex ] = held;
			}

			RestackPoses();
			yield break;
		}

		AttachImmediate( bucket, item, isActive, heldIndex );
		if ( playPickupFeedback )
		{
			SetPoseSettled( bucket, isActive, heldIndex, settled: true );
			CoinGemInteractFeedback.PlayPickup( item );
		}
	}

	bool TryFindEntry( int token, out CategoryBucket bucket, out bool isActive, out int heldIndex )
	{
		bucket = null;
		isActive = false;
		heldIndex = -1;

		EnsureBuckets();
		for ( int b = 0; b < BucketCount; b++ )
		{
			CategoryBucket candidate = _buckets[ b ];
			if ( candidate.HasActive && candidate.Active.Token == token )
			{
				bucket = candidate;
				isActive = true;
				return true;
			}

			for ( int i = 0; i < candidate.Held.Count; i++ )
			{
				if ( candidate.Held[ i ].Token != token )
					continue;

				bucket = candidate;
				heldIndex = i;
				return true;
			}
		}

		return false;
	}

	void AttachImmediate( CategoryBucket bucket, TreasureItem item, bool active, int heldIndex )
	{
		Transform parent = GetItemParent( bucket, active );
		if ( item == null || parent == null )
			return;

		Transform t = item.transform;
		t.SetParent( parent, false );
		ApplyPose( t, GetLocalPose( bucket, active, heldIndex ), item.GetHeldLocalRotation( active ) );
		item.SetHeldShadows( enabled: false );
		item.SetHeldLighting( enabled: true );

		CarriedEntry entry = active ? bucket.Active : bucket.Held[ heldIndex ];
		if ( entry.ClusterId != 0 && entry.IsClusterAnchor )
			item.ApplyWorldScale();
		else if ( entry.ClusterId == 0 )
			item.ApplyHeldScale();

		item.SyncRigidbodyToTransform();
	}

	void RestackPoses()
	{
		// Keep parents correct; slot motion is eased in SmoothCarryPoses so
		// in-flight pickups are never snapped / aborted by a restack.
		EnsureBuckets();
		for ( int b = 0; b < BucketCount; b++ )
		{
			CategoryBucket bucket = _buckets[ b ];
			InvalidatePoseSettled( bucket );
			EnsureCarryParents( bucket );
		}
	}

	static void InvalidatePoseSettled( CategoryBucket bucket )
	{
		if ( bucket.HasActive )
			bucket.Active.PoseSettled = false;

		for ( int i = 0; i < bucket.Held.Count; i++ )
		{
			CarriedEntry entry = bucket.Held[ i ];
			if ( !entry.PoseSettled )
				continue;

			entry.PoseSettled = false;
			bucket.Held[ i ] = entry;
		}
	}

	void EnsureCarryParents( CategoryBucket bucket )
	{
		if ( bucket.HasActive && bucket.Active.Item != null )
		{
			Transform parent = GetItemParent( bucket, active: true );
			Transform t = bucket.Active.Item.transform;
			if ( parent != null && t.parent != parent && !_holdTweens.ContainsKey( bucket.Active.Token ) )
				t.SetParent( parent, true );
		}

		for ( int i = 0; i < bucket.Held.Count; i++ )
		{
			CarriedEntry entry = bucket.Held[ i ];
			if ( entry.Item == null )
				continue;
			if ( _holdTweens.ContainsKey( entry.Token ) )
				continue;
			if ( entry.ClusterId != 0 && !entry.IsClusterAnchor )
				continue;

			Transform parent = GetItemParent( bucket, active: false );
			Transform t = entry.Item.transform;
			if ( parent != null && t.parent != parent )
				t.SetParent( parent, true );
		}
	}

	void SmoothCarryPoses()
	{
		CarryDefinition def = Definition;
		float speed = def != null ? def.stackPoseSmoothSpeed : 16f;
		float dt = Time.deltaTime;
		float t = speed <= 0.01f ? 1f : 1f - Mathf.Exp( -speed * dt );
		Vector3 heldBase = def != null ? def.heldStackOffset : Vector3.zero;

		for ( int b = 0; b < BucketCount; b++ )
		{
			CategoryBucket bucket = _buckets[ b ];
			if ( !bucket.Visible && bucket.Count == 0 )
				continue;

			if ( bucket.HasActive
				&& bucket.Active.Item != null
				&& !bucket.Active.PoseSettled
				&& !_holdTweens.ContainsKey( bucket.Active.Token ) )
			{
				SmoothItemTowardPose( bucket, bucket.Active.Item, active: true, heldIndex: -1, Vector3.zero, t );
			}

			// Single pass: accumulate stack height while easing each anchor (O(n)).
			float y = 0f;
			for ( int i = 0; i < bucket.Held.Count; i++ )
			{
				CarriedEntry entry = bucket.Held[ i ];
				if ( entry.Item == null )
					continue;
				if ( entry.ClusterId != 0 && !entry.IsClusterAnchor )
					continue;

				if ( !_holdTweens.ContainsKey( entry.Token ) && !entry.PoseSettled )
				{
					Vector3 targetPos = heldBase + GetHeldStackHorizontalOffset( bucket, i ) + new Vector3( 0f, y, 0f );
					SmoothItemTowardPose( bucket, entry.Item, active: false, heldIndex: i, targetPos, t );
				}

				y += GetHeldStackStep( entry.Definition, def );
			}
		}
	}

	void SmoothItemTowardPose( CategoryBucket bucket, TreasureItem item, bool active, int heldIndex, Vector3 targetPos, float t )
	{
		Transform parent = GetItemParent( bucket, active );
		if ( item == null || parent == null )
			return;

		CarriedEntry entry = active ? bucket.Active : bucket.Held[ heldIndex ];
		if ( entry.PoseSettled )
			return;

		Transform visual = item.transform;
		if ( visual.parent != parent )
			visual.SetParent( parent, true );

		Quaternion targetRot = item.GetHeldLocalRotation( active );
		Vector3 targetScale = item.GetHeldScale();
		if ( entry.ClusterId != 0 && entry.IsClusterAnchor )
			targetScale = item.GetWorldScale();

		if ( IsPoseNear( visual, targetPos, targetRot, targetScale ) )
		{
			SnapPose( visual, targetPos, targetRot, targetScale );
			SetPoseSettled( bucket, active, heldIndex, settled: true );
			return;
		}

		visual.localPosition = Vector3.Lerp( visual.localPosition, targetPos, t );
		visual.localRotation = Quaternion.Slerp( visual.localRotation, targetRot, t );
		visual.localScale = Vector3.Lerp( visual.localScale, targetScale, t );

		if ( IsPoseNear( visual, targetPos, targetRot, targetScale ) )
		{
			SnapPose( visual, targetPos, targetRot, targetScale );
			SetPoseSettled( bucket, active, heldIndex, settled: true );
		}
	}

	static void SetPoseSettled( CategoryBucket bucket, bool active, int heldIndex, bool settled )
	{
		if ( active )
		{
			bucket.Active.PoseSettled = settled;
			return;
		}

		CarriedEntry entry = bucket.Held[ heldIndex ];
		entry.PoseSettled = settled;
		bucket.Held[ heldIndex ] = entry;
	}

	static bool IsPoseNear( Transform visual, Vector3 targetPos, Quaternion targetRot, Vector3 targetScale )
	{
		if ( ( visual.localPosition - targetPos ).sqrMagnitude > PoseSettlePosEpsilon * PoseSettlePosEpsilon )
			return false;
		if ( ( visual.localScale - targetScale ).sqrMagnitude > PoseSettleScaleEpsilon * PoseSettleScaleEpsilon )
			return false;
		return Quaternion.Angle( visual.localRotation, targetRot ) <= PoseSettleAngleEpsilon;
	}

	static void SnapPose( Transform visual, Vector3 targetPos, Quaternion targetRot, Vector3 targetScale )
	{
		visual.localPosition = targetPos;
		visual.localRotation = targetRot;
		visual.localScale = targetScale;
	}

	static Transform GetItemParent( CategoryBucket bucket, bool active )
	{
		if ( bucket == null )
			return null;

		return active ? bucket.ActiveRoot : bucket.HoldRoot;
	}

	Vector3 GetLocalPose( CategoryBucket bucket, bool active, int heldIndex )
	{
		// Active sits at ActiveRoot origin (root is already screen-centered).
		if ( active )
			return Vector3.zero;

		CarryDefinition def = Definition;
		Vector3 heldBase = def != null ? def.heldStackOffset : Vector3.zero;
		float y = 0f;
		for ( int i = 0; i < heldIndex && i < bucket.Held.Count; i++ )
		{
			CarriedEntry entry = bucket.Held[ i ];
			if ( entry.ClusterId != 0 && !entry.IsClusterAnchor )
				continue;

			y += GetHeldStackStep( entry.Definition, def );
		}

		return heldBase + GetHeldStackHorizontalOffset( bucket, heldIndex ) + new Vector3( 0f, y, 0f );
	}

	Vector3 GetHeldStackHorizontalOffset( CategoryBucket bucket, int heldIndex )
	{
		CarryDefinition def = Definition;
		float spread = def != null ? def.heldStackHorizontalSpread : 0.012f;
		if ( spread <= 0.0001f || heldIndex < 0 )
			return Vector3.zero;

		TreasureDefinition definition = heldIndex < bucket.Held.Count ? bucket.Held[ heldIndex ].Definition : null;
		uint hash = (uint)( heldIndex + 1 ) * 73856093u;
		if ( definition != null && !string.IsNullOrEmpty( definition.id ) )
			hash ^= (uint)definition.id.GetHashCode();

		float nx = ( ( hash & 0xFFFFu ) / 65535f ) * 2f - 1f;
		float nz = ( ( ( hash >> 16 ) & 0xFFFFu ) / 65535f ) * 2f - 1f;
		return new Vector3( nx * spread, 0f, nz * spread );
	}

	float GetHeldStackStep( TreasureDefinition definition )
	{
		return GetHeldStackStep( definition, Definition );
	}

	float GetHeldStackStep( TreasureDefinition definition, CarryDefinition carryDef )
	{
		float step = TreasureStackSpacing.GetHeldStep( definition );
		float padding = carryDef != null ? carryDef.stackPadding : 0.01f;
		float fallback = carryDef != null ? carryDef.fallbackStackStep : 0.04f;
		if ( step < 0.0001f )
			step = fallback;

		return step + padding;
	}

	/// <summary>
	/// Left-hand coins are always one merged cylinder driven by logical definitions.
	/// The right-hand Active coin stays a live mesh.
	/// </summary>
	void RefreshHeldCoinCylinder()
	{
		CategoryBucket coins = _buckets[ (int)CarryBucketKind.Coin ];
		if ( coins.HoldRoot == null )
			return;

		CompactCoinHeldToDefinitions( coins );

		CarryDefinition def = Definition;
		int visualMax = def != null ? Mathf.Max( 1, def.heldVisualMaxCoins ) : 40;
		Vector3 heldBase = def != null ? def.heldStackOffset : Vector3.zero;

		int limit = Mathf.Min( coins.Held.Count, visualMax );
		List<TreasureDefinition> defs = new List<TreasureDefinition>( limit );
		for ( int i = 0; i < limit; i++ )
		{
			TreasureDefinition slotDef = coins.Held[ i ].Definition;
			if ( slotDef != null )
				defs.Add( slotDef );
		}

		if ( defs.Count >= CoinColumnCylinderBinder.MinCountForCylinder )
		{
			bool[] covered = null;
			CoinColumnCylinderBinder.BindDefinitions( ref coins.HeldCylinder, coins.HoldRoot, defs, snap: false, covered, useHeldScale: true );
			if ( coins.HeldCylinder != null )
			{
				coins.HeldCylinder.SetVariationSeed( EnsureCoinHandVariationSeed() );
				Transform host = coins.HeldCylinder.transform.parent != null
					? coins.HeldCylinder.transform.parent
					: coins.HeldCylinder.transform;
				host.localPosition = heldBase;
				host.localRotation = Quaternion.identity;
			}
		}
		else
		{
			coins.CylinderBuffer.Clear();
			CoinColumnCylinderBinder.Clear( ref coins.HeldCylinder, coins.CylinderBuffer );
		}
	}

	void ApplyPose( Transform visual, Vector3 localPose, Quaternion localRotation )
	{
		if ( visual == null )
			return;

		visual.localPosition = localPose;
		visual.localRotation = localRotation;
	}

	void ApplyHoldRootPose( bool immediate )
	{
		CarryDefinition def = Definition;
		Vector3 holdOffset = def != null ? def.holdLocalOffset : new Vector3( 0.25f, -0.2f, 0.45f );
		Vector3 holdEuler = def != null ? def.holdLocalEuler : Vector3.zero;
		Vector3 activeOffset = def != null ? def.activeItemOffset : new Vector3( 0f, -0.12f, 0.55f );
		Vector3 activeEuler = def != null ? def.activeLocalEuler : Vector3.zero;
		Vector3 restOffset = def != null ? def.categoryRestOffset : new Vector3( 0f, -0.35f, -0.2f );
		Vector3 restEuler = def != null ? def.categoryRestEuler : new Vector3( 25f, 0f, 0f );

		float dt = Time.deltaTime;
		float moveSpeed = _player != null ? _player.PlanarSpeed : 0f;
		Vector3 localMove = _player != null ? _player.LocalPlanarVelocity : Vector3.zero;
		float bobFullSpeed = def != null ? def.bobFullSpeed : 4f;
		float move01 = Mathf.Clamp01( moveSpeed / Mathf.Max( 0.01f, bobFullSpeed ) );
		float idleBob = def != null ? def.idleBobScale : 0.15f;
		float bobWeight = Mathf.Lerp( idleBob, 1f, move01 );

		float bobAmp = def != null ? def.bobAmplitude : 0.025f;
		float bobFreq = def != null ? def.bobFrequency : 8f;
		_bobPhase += dt * bobFreq * Mathf.Lerp( 0.35f, 1f, move01 );
		float bobY = Mathf.Sin( _bobPhase ) * bobAmp * bobWeight;

		Vector3 swayAmp = def != null ? def.swayAmplitude : new Vector3( 0.02f, 0.01f, 0.015f );
		float swayFreq = def != null ? def.swayFrequency : 1.6f;
		_swayPhase += dt * swayFreq;
		Vector3 sway = new Vector3(
			Mathf.Sin( _swayPhase ) * swayAmp.x,
			Mathf.Cos( _swayPhase * 0.7f ) * swayAmp.y,
			Mathf.Sin( _swayPhase * 1.3f ) * swayAmp.z ) * bobWeight;

		float strafeSway = def != null ? def.strafeSway : 0.04f;
		float moveSway = def != null ? def.moveSway : 0.03f;
		Vector3 moveOffset = new Vector3(
			Mathf.Clamp( localMove.x, -1f, 1f ) * strafeSway,
			0f,
			Mathf.Clamp( localMove.z, -1f, 1f ) * moveSway );

		Vector3 targetMotion = new Vector3( 0f, bobY, 0f ) + sway + moveOffset;
		float motionSmooth = def != null ? def.handMotionSmoothSpeed : 12f;
		if ( immediate || motionSmooth <= 0.01f )
			_smoothedMotionOffset = targetMotion;
		else
			_smoothedMotionOffset = Vector3.Lerp( _smoothedMotionOffset, targetMotion, 1f - Mathf.Exp( -motionSmooth * dt ) );

		float cameraPitch = _cameraLook != null ? _cameraLook.Pitch : 0f;
		float uprightFavor = def != null ? def.uprightPitchFavor : 0.65f;
		float targetUpright = -cameraPitch * uprightFavor;
		float uprightSmooth = def != null ? def.uprightSmoothSpeed : 10f;
		if ( immediate || uprightSmooth <= 0.01f )
			_smoothedUprightPitch = targetUpright;
		else
			_smoothedUprightPitch = Mathf.Lerp( _smoothedUprightPitch, targetUpright, 1f - Mathf.Exp( -uprightSmooth * dt ) );

		Vector3 uprightEuler = new Vector3( _smoothedUprightPitch, 0f, 0f );
		Quaternion restRotation = Quaternion.Euler( restEuler );

		for ( int b = 0; b < BucketCount; b++ )
		{
			CategoryBucket bucket = _buckets[ b ];
			if ( bucket.RigRoot == null || !bucket.Visible )
				continue;

			// swapT 0 parks the rig at the category rest offset, 1 is the full active hand pose.
			float ease = CoinFlipMotion.SmoothStep( Mathf.Clamp01( bucket.SwapT ) );
			bucket.RigRoot.localPosition = Vector3.Lerp( restOffset, Vector3.zero, ease );
			bucket.RigRoot.localRotation = Quaternion.Slerp( restRotation, Quaternion.identity, ease );
			bucket.RigRoot.localScale = Vector3.one;

			if ( bucket.HoldRoot != null )
			{
				bucket.HoldRoot.localPosition = holdOffset + _smoothedMotionOffset;
				bucket.HoldRoot.localRotation = Quaternion.Euler( holdEuler + uprightEuler );
				bucket.HoldRoot.localScale = Vector3.one;
			}

			if ( bucket.ActiveRoot != null )
			{
				// Lighter motion on the screen-center Active so it stays readable.
				bucket.ActiveRoot.localPosition = activeOffset + _smoothedMotionOffset * 0.35f;
				bucket.ActiveRoot.localRotation = Quaternion.Euler( activeEuler + uprightEuler * 0.5f );
				bucket.ActiveRoot.localScale = Vector3.one;
			}
		}
	}

	void OnDestroy()
	{
		TreasureProximitySleep.ClearPlayer( transform.root != null ? transform.root : transform );
		Clear();

		if ( _carryRigs != null )
		{
			Destroy( _carryRigs.gameObject );
			_carryRigs = null;
		}

		for ( int i = 0; i < BucketCount; i++ )
		{
			CategoryBucket bucket = _buckets[ i ];
			if ( bucket == null )
				continue;

			bucket.RigRoot = null;
			bucket.HoldRoot = null;
			bucket.ActiveRoot = null;
			bucket.HeldCylinder = null;
		}
	}
}
