using System.Collections;
using System.Collections.Generic;

using UnityEngine;

public class PlayerCarry : MonoBehaviour, ITreasureOwner
{
	struct CarriedEntry
	{
		public TreasureDefinition Definition;
		public TreasureItem Item;
		public int Token;
		public int ClusterId;
		public bool IsClusterAnchor;
	}

	readonly List<CarriedEntry> _held = new List<CarriedEntry>();
	readonly List<CarriedEntry> _cycleBuffer = new List<CarriedEntry>();
	readonly List<int> _tweenAbortBuffer = new List<int>();
	readonly List<TreasureItem> _heldCylinderBuffer = new List<TreasureItem>();

	CarryDefinition _definition;
	PlayerController _player;
	FirstPersonCameraController _cameraLook;
	Transform _holdRoot;
	Transform _activeRoot;
	CoinStackCylinderVisual _heldCylinder;
	int _nextToken = 1;
	int _usedCapacity;
	readonly Dictionary<int, Coroutine> _holdTweens = new Dictionary<int, Coroutine>();
	float _bobPhase;
	float _swayPhase;
	Vector3 _smoothedMotionOffset;
	float _smoothedUprightPitch;
	bool _hasActive;
	CarriedEntry _active;
	float _cycleCooldown;

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

	/// <summary>0 = empty hands, 1 = at or above <see cref="MaxCarryWeight"/>.</summary>
	public float CarryBurden01 => Mathf.Clamp01( (float)_usedCapacity / MaxCarryWeight );

	/// <summary>Linear walk/sprint multiplier from carried weight; never below definition floor.</summary>
	public float MoveSpeedMultiplier
	{
		get
		{
			CarryDefinition def = Definition;
			float floor = def != null ? def.minBurdenedMoveSpeedScale : 0.08f;
			return Mathf.Lerp( 1f, floor, CarryBurden01 );
		}
	}
	public int Count => ( _hasActive ? 1 : 0 ) + _held.Count;
	public int HeldCount => _held.Count;
	public bool HasActive => _hasActive;
	public Transform HoldRoot => _holdRoot;
	public Transform ActiveRoot => _activeRoot;

	public bool ContainsItem( TreasureItem item )
	{
		if ( item == null )
			return false;

		if ( _hasActive && _active.Item == item )
			return true;

		for ( int i = 0; i < _held.Count; i++ )
		{
			if ( _held[ i ].Item == item )
				return true;
		}

		return false;
	}

	public void Setup( PlayerController player, FirstPersonCameraController cameraLook )
	{
		_player = player;
		_cameraLook = cameraLook;

		Transform cameraTransform = cameraLook != null ? cameraLook.transform : null;
		if ( cameraTransform == null )
			return;

		LooseTreasureManager.EnsureExists();
		TreasureProximitySleep.SetPlayer( transform.root != null ? transform.root : transform );

		if ( _holdRoot == null )
		{
			GameObject rootGo = new GameObject( "HoldRoot" );
			_holdRoot = rootGo.transform;
		}

		if ( _activeRoot == null )
		{
			GameObject activeGo = new GameObject( "ActiveRoot" );
			_activeRoot = activeGo.transform;
		}

		_holdRoot.SetParent( cameraTransform, false );
		_activeRoot.SetParent( cameraTransform, false );
		_smoothedMotionOffset = Vector3.zero;
		_smoothedUprightPitch = 0f;
		ApplyHoldRootPose( immediate: true );
		RestackPoses();
	}

	void LateUpdate()
	{
		if ( _holdRoot == null && _activeRoot == null )
			return;

		ApplyHoldRootPose( immediate: false );
		TryCycleFromScroll();
		SmoothCarryPoses();
	}

	void TryCycleFromScroll()
	{
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
	/// Rotates which carried item is Active. Relative order of the ring is preserved.
	/// </summary>
	public bool CycleActive( int direction )
	{
		if ( !_hasActive || _held.Count == 0 || direction == 0 )
			return false;

		_cycleBuffer.Clear();
		_cycleBuffer.Add( _active );
		for ( int i = 0; i < _held.Count; i++ )
			_cycleBuffer.Add( _held[ i ] );

		int n = _cycleBuffer.Count;
		int steps = direction % n;
		if ( steps < 0 )
			steps += n;
		if ( steps == 0 )
			return false;

		CarriedEntry newActive = _cycleBuffer[ steps ];
		_held.Clear();
		for ( int i = 1; i < n; i++ )
		{
			int src = ( steps + i ) % n;
			_held.Add( _cycleBuffer[ src ] );
		}

		_active = newActive;
		_hasActive = true;
		if ( _active.Item == null && _active.Definition != null )
			SpawnHeldItemAsync( _active.Definition, _active.Token );
		RestackPoses();
		return true;
	}

	public void ReleaseTreasure( TreasureItem item )
	{
		if ( item == null )
			return;

		if ( _hasActive && _active.Item == item )
		{
			AbortHoldTween( _active.Token, snapToHand: false, item );
			_usedCapacity = Mathf.Max( 0, _usedCapacity - GetCost( _active.Definition ) );
			_hasActive = false;
			_active = default;
			item.EndFlight();
			PromoteFromHeld();
			RestackPoses();
			return;
		}

		for ( int i = 0; i < _held.Count; i++ )
		{
			if ( _held[ i ].Item != item )
				continue;

			CarriedEntry entry = _held[ i ];
			_held.RemoveAt( i );
			_usedCapacity = Mathf.Max( 0, _usedCapacity - GetCost( entry.Definition ) );
			AbortHoldTween( entry.Token, snapToHand: false, item );
			item.EndFlight();
			RestackPoses();
			return;
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

		if ( !PassesMixingRules( treasure ) )
			return false;

		return PassesMixingRules( treasure );
	}

	/// <summary>
	/// How many units of the same definition still fit in carry (for multi-pull pile interacts).
	/// </summary>
	public int CountAffordableUnits( TreasureDefinition treasure, int maxUnits )
	{
		if ( treasure == null || maxUnits <= 0 )
			return 0;

		int count = 0;
		bool wouldHaveExclusive = HasExclusiveCarried();
		bool wouldHaveAny = Count > 0;

		for ( int i = 0; i < maxUnits; i++ )
		{
			if ( treasure.exclusiveCarry && ( wouldHaveAny || count > 0 ) )
				break;

			if ( wouldHaveExclusive )
				break;

			count++;
			if ( treasure.exclusiveCarry )
				wouldHaveExclusive = true;

			wouldHaveAny = true;
		}

		return count;
	}

	public bool CanAdd( TreasureItem item )
	{
		if ( item == null )
			return false;

		return CanAdd( item.Definition );
	}

	public bool CanAddAll( IReadOnlyList<TreasureItem> items )
	{
		if ( items == null || items.Count == 0 )
			return false;

		bool wouldHaveExclusive = HasExclusiveCarried();
		bool wouldHaveAny = Count > 0;
		for ( int i = 0; i < items.Count; i++ )
		{
			TreasureItem item = items[ i ];
			if ( item == null )
				return false;

			TreasureDefinition def = item.Definition;
			if ( def == null )
				return false;

			if ( def.exclusiveCarry && ( wouldHaveAny || items.Count > 1 ) )
				return false;

			if ( wouldHaveExclusive )
				return false;

			if ( def.exclusiveCarry )
				wouldHaveExclusive = true;

			wouldHaveAny = true;
		}

		return true;
	}

	bool PassesMixingRules( TreasureDefinition treasure )
	{
		if ( treasure == null )
			return false;

		if ( Count == 0 )
			return true;

		if ( treasure.exclusiveCarry )
			return false;

		if ( HasExclusiveCarried() )
			return false;

		return true;
	}

	bool HasExclusiveCarried()
	{
		if ( _hasActive && _active.Definition != null && _active.Definition.exclusiveCarry )
			return true;

		for ( int i = 0; i < _held.Count; i++ )
		{
			TreasureDefinition def = _held[ i ].Definition;
			if ( def != null && def.exclusiveCarry )
				return true;
		}

		return false;
	}

	/// <summary>
	/// How many items from the start of a bottom-to-top list can still be added (mixing rules only).
	/// </summary>
	public int CountAffordablePrefix( IReadOnlyList<TreasureItem> orderedBottomToTop )
	{
		if ( orderedBottomToTop == null || orderedBottomToTop.Count == 0 )
			return 0;

		int count = 0;
		bool wouldHaveExclusive = HasExclusiveCarried();
		bool wouldHaveAny = Count > 0;
		for ( int i = 0; i < orderedBottomToTop.Count; i++ )
		{
			TreasureItem item = orderedBottomToTop[ i ];
			if ( item == null )
				break;

			TreasureDefinition def = item.Definition;
			if ( def == null )
				break;

			if ( def.exclusiveCarry && ( wouldHaveAny || count > 0 ) )
				break;

			if ( wouldHaveExclusive )
				break;

			count++;
			if ( def.exclusiveCarry )
				wouldHaveExclusive = true;
			wouldHaveAny = true;
		}

		return count;
	}

	/// <summary>
	/// How many items from the end of a bottom-to-top list (the top of a stack) can still be added (mixing rules only).
	/// </summary>
	public int CountAffordableSuffix( IReadOnlyList<TreasureItem> orderedBottomToTop )
	{
		if ( orderedBottomToTop == null || orderedBottomToTop.Count == 0 )
			return 0;

		int count = 0;
		bool wouldHaveExclusive = HasExclusiveCarried();
		bool wouldHaveAny = Count > 0;
		for ( int i = orderedBottomToTop.Count - 1; i >= 0; i-- )
		{
			TreasureItem item = orderedBottomToTop[ i ];
			if ( item == null )
				break;

			TreasureDefinition def = item.Definition;
			if ( def == null )
				break;

			if ( def.exclusiveCarry && ( wouldHaveAny || count > 0 ) )
				break;

			if ( wouldHaveExclusive )
				break;

			count++;
			if ( def.exclusiveCarry )
				wouldHaveExclusive = true;
			wouldHaveAny = true;
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
		int count = 0;
		bool wouldHaveExclusive = HasExclusiveCarried();
		bool wouldHaveAny = Count > 0;
		for ( int i = orderedBottomToTop.Count - 1; i >= startIndex; i-- )
		{
			TreasureDefinition def = orderedBottomToTop[ i ];
			if ( def == null )
				break;

			if ( def.exclusiveCarry && ( wouldHaveAny || count > 0 ) )
				break;

			if ( wouldHaveExclusive )
				break;

			count++;
			if ( def.exclusiveCarry )
				wouldHaveExclusive = true;
			wouldHaveAny = true;
		}

		return count;
	}

	public bool TryAdd( TreasureDefinition treasure )
	{
		if ( !CanAdd( treasure ) )
			return false;

		if ( _holdRoot == null || _activeRoot == null )
			return false;

		int cost = GetCost( treasure );
		int token = _nextToken++;
		_usedCapacity += cost;

		CarriedEntry entry = new CarriedEntry
		{
			Definition = treasure,
			Item = null,
			Token = token,
			ClusterId = 0,
			IsClusterAnchor = false
		};

		InsertAsActive( entry );
		RestackPoses();
		SpawnHeldItemAsync( treasure, token );
		return true;
	}

	/// <summary>
	/// Batch-add definitions. Spawns only the active held mesh; deeper stack entries spawn on promote.
	/// </summary>
	public int TryAddMany( IReadOnlyList<TreasureDefinition> definitions )
	{
		if ( definitions == null || definitions.Count == 0 )
			return 0;

		if ( _holdRoot == null || _activeRoot == null )
			return 0;

		int added = 0;
		for ( int i = 0; i < definitions.Count; i++ )
		{
			TreasureDefinition treasure = definitions[ i ];
			if ( treasure == null || !CanAdd( treasure ) )
				break;

			int cost = GetCost( treasure );
			int token = _nextToken++;
			_usedCapacity += cost;

			CarriedEntry entry = new CarriedEntry
			{
				Definition = treasure,
				Item = null,
				Token = token,
				ClusterId = 0,
				IsClusterAnchor = false
			};

			InsertAsActive( entry );
			added++;
		}

		if ( added <= 0 )
			return 0;

		if ( _hasActive && _active.Item == null && _active.Definition != null )
			SpawnHeldItemAsync( _active.Definition, _active.Token );

		RestackPoses();
		return added;
	}

	public int TryAddMany( TreasureDefinition treasure, int count )
	{
		if ( treasure == null || count <= 0 )
			return 0;

		int added = 0;
		for ( int i = 0; i < count; i++ )
		{
			if ( !CanAdd( treasure ) )
				break;

			int cost = GetCost( treasure );
			int token = _nextToken++;
			_usedCapacity += cost;

			CarriedEntry entry = new CarriedEntry
			{
				Definition = treasure,
				Item = null,
				Token = token,
				ClusterId = 0,
				IsClusterAnchor = false
			};

			InsertAsActive( entry );
			added++;
		}

		if ( added <= 0 )
			return 0;

		if ( _hasActive && _active.Item == null && _active.Definition != null )
			SpawnHeldItemAsync( _active.Definition, _active.Token );

		RestackPoses();
		return added;
	}

	public bool TryAddExisting( TreasureItem item )
	{
		if ( item == null || !CanAdd( item ) )
			return false;

		if ( _holdRoot == null || _activeRoot == null )
			return false;

		if ( ContainsItem( item ) )
			return false;

		int cost = GetCost( item.Definition );
		int token = _nextToken++;
		_usedCapacity += cost;

		item.BeginHold( this );
		item.transform.SetParent( GetItemParent( active: true ), true );
		item.SetHeldShadows( enabled: false );

		CarriedEntry entry = new CarriedEntry
		{
			Definition = item.Definition,
			Item = item,
			Token = token,
			ClusterId = 0,
			IsClusterAnchor = false
		};

		InsertAsActive( entry );
		StartHoldTween( item, token );
		// Demoted items must restack; Active is skipped while its hold tween runs.
		RestackPoses();
		return true;
	}

	/// <summary>
	/// Adds a support stack (bottom-to-top). Newest (top) becomes Active; older items demote into held.
	/// </summary>
	public bool TryAddSupportStack( IReadOnlyList<TreasureItem> orderedBottomToTop )
	{
		if ( orderedBottomToTop == null || orderedBottomToTop.Count == 0 )
			return false;

		if ( _holdRoot == null || _activeRoot == null )
			return false;

		bool any = false;
		for ( int i = 0; i < orderedBottomToTop.Count; i++ )
		{
			TreasureItem item = orderedBottomToTop[ i ];
			if ( item == null )
				break;

			if ( !TryAddExisting( item ) )
				break;

			any = true;
		}

		if ( any )
			RestackPoses();

		return any;
	}

	void InsertAsActive( CarriedEntry entry )
	{
		if ( _hasActive )
			_held.Insert( 0, _active );

		_active = entry;
		_hasActive = true;
	}

	void PromoteFromHeld()
	{
		if ( _hasActive || _held.Count == 0 )
			return;

		_active = _held[ 0 ];
		_held.RemoveAt( 0 );
		_hasActive = true;

		if ( _active.Item == null && _active.Definition != null )
			SpawnHeldItemAsync( _active.Definition, _active.Token );
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
		if ( !_hasActive )
			return false;

		// Pending async spawns reserve capacity but are not placeable until bound.
		if ( _active.Item == null )
			return false;

		definition = _active.Definition;
		item = _active.Item;
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
		if ( _held.Count > 0 )
		{
			CarriedEntry entry = _held[ _held.Count - 1 ];
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
		if ( !_hasActive )
			return false;

		CarriedEntry entry = _active;
		item = ResolveLiveItem( entry, activeParent: true );
		if ( item == null )
			return false;

		AbortHoldTween( entry.Token, snapToHand: false, item );
		_hasActive = false;
		_active = default;
		_usedCapacity = Mathf.Max( 0, _usedCapacity - GetCost( entry.Definition ) );

		item.SetHeldShadows( enabled: true );
		item.transform.SetParent( null, true );
		item.BeginFlight();

		PromoteFromHeld();
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
		if ( _held.Count > 0 )
		{
			int index = _held.Count - 1;
			CarriedEntry entry = _held[ index ];
			item = ResolveLiveItem( entry, activeParent: false );
			if ( item == null )
				return false;

			_held.RemoveAt( index );
			DetachResolvedEntry( entry, item );
			RestackPoses();
			return true;
		}

		return TryConsumeActive( out item );
	}

	TreasureItem ResolveLiveItem( CarriedEntry entry, bool activeParent )
	{
		TreasureItem item = entry.Item;
		if ( item != null )
			return item;

		if ( entry.Definition == null )
			return null;

		Transform parent = GetItemParent( activeParent );
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

		item.SetHeldShadows( enabled: true );
		item.transform.SetParent( null, true );
		item.BeginFlight();
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

	public void Clear()
	{
		AbortAllHoldTweens( snapToHand: false );
		if ( _hasActive && _active.Item != null )
			TreasureItemFactory.Despawn( _active.Item );

		for ( int i = 0; i < _held.Count; i++ )
		{
			if ( _held[ i ].Item != null )
				TreasureItemFactory.Despawn( _held[ i ].Item );
		}

		_hasActive = false;
		_active = default;
		_held.Clear();
		_usedCapacity = 0;
		_heldCylinderBuffer.Clear();
		CoinColumnCylinderBinder.ClearAndDestroy( ref _heldCylinder, null );
	}

	async void SpawnHeldItemAsync( TreasureDefinition treasure, int token )
	{
		Transform spawnParent = GetItemParent( active: true );
		Vector3 spawnPos = spawnParent != null ? spawnParent.position : transform.position;
		Quaternion spawnRot = spawnParent != null ? spawnParent.rotation : Quaternion.identity;
		TreasureItem item = await TreasureItemFactory.SpawnAsync( treasure, spawnPos, spawnRot, null );

		if ( this == null )
		{
			if ( item != null )
				TreasureItemFactory.Despawn( item );
			return;
		}

		if ( !TryFindEntry( token, out bool isActive, out int heldIndex ) )
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

		if ( isActive )
		{
			CarriedEntry entry = _active;
			entry.Item = item;
			_active = entry;
			AttachImmediate( item, active: true, heldIndex: -1 );
		}
		else
		{
			CarriedEntry entry = _held[ heldIndex ];
			entry.Item = item;
			_held[ heldIndex ] = entry;
			AttachImmediate( item, active: false, heldIndex: heldIndex );
		}
	}

	void RemovePendingEntry( int token )
	{
		if ( _hasActive && _active.Token == token )
		{
			_usedCapacity = Mathf.Max( 0, _usedCapacity - GetCost( _active.Definition ) );
			_hasActive = false;
			_active = default;
			PromoteFromHeld();
			RestackPoses();
			return;
		}

		for ( int i = 0; i < _held.Count; i++ )
		{
			if ( _held[ i ].Token != token )
				continue;

			CarriedEntry entry = _held[ i ];
			_held.RemoveAt( i );
			_usedCapacity = Mathf.Max( 0, _usedCapacity - GetCost( entry.Definition ) );
			RestackPoses();
			return;
		}
	}

	void StartHoldTween( TreasureItem item, int token )
	{
		if ( _holdTweens.TryGetValue( token, out Coroutine existing ) && existing != null )
			StopCoroutine( existing );

		if ( item != null )
			item.BeginFlight();

		Coroutine routine = StartCoroutine( HoldTweenRoutine( item, token ) );
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
		if ( item == null && TryFindEntry( token, out bool isActive, out int heldIndex ) )
			item = isActive ? _active.Item : _held[ heldIndex ].Item;

		if ( item == null )
			return;

		if ( snapToHand && TryFindEntry( token, out bool snapActive, out int snapHeldIndex ) )
		{
			item.EndFlight();
			AttachImmediate( item, snapActive, snapHeldIndex );
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

	IEnumerator HoldTweenRoutine( TreasureItem item, int token )
	{
		Transform parent = GetItemParent( active: true );
		if ( item == null || parent == null )
		{
			_holdTweens.Remove( token );
			if ( item != null )
				item.EndFlight();
			yield break;
		}

		if ( !TryFindEntry( token, out bool isActive, out int heldIndex ) )
		{
			_holdTweens.Remove( token );
			item.EndFlight();
			yield break;
		}

		parent = GetItemParent( isActive );
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

		CarriedEntry entry = isActive ? _active : _held[ heldIndex ];
		bool keepWorldScale = entry.IsClusterAnchor || entry.ClusterId != 0;
		Vector3 endLocalPos = GetLocalPose( isActive, heldIndex );
		Vector3 endLocalScale = keepWorldScale ? item.GetWorldScale() : item.GetHeldScale();
		bool flipCoin = CoinFlipMotion.IsCoin( item );

		CarryDefinition def = Definition;
		float duration;
		float arcHeight = 0f;
		float spins = 0f;
		if ( flipCoin )
		{
			float configured = def != null ? def.coinFlipDuration : CoinFlipMotion.DefaultDuration;
			if ( configured < 0.05f )
				configured = def != null ? def.holdTweenDuration : 0.2f;
			duration = Mathf.Max( 0.05f, configured );
			arcHeight = def != null ? def.coinFlipArcHeight : CoinFlipMotion.DefaultArcHeight;
			spins = def != null ? def.coinFlipSpins : CoinFlipMotion.DefaultSpins;
		}
		else
		{
			duration = Mathf.Max( 0.05f, def != null ? def.holdTweenDuration : 0.2f );
			arcHeight = def != null ? def.itemArcHeight : CoinFlipMotion.DefaultItemArcHeight;
		}
		float elapsed = 0f;

		while ( elapsed < duration )
		{
			if ( !TryFindEntry( token, out isActive, out heldIndex ) )
			{
				// Still flying into hand — do not EndFlight; place/consume owns the item.
				_holdTweens.Remove( token );
				yield break;
			}

			Transform desiredParent = GetItemParent( isActive );
			if ( desiredParent != null && t.parent != desiredParent )
			{
				// Soft retarget into the held stack — never abort / replay the full flip.
				t.SetParent( desiredParent, true );
				startLocalPos = t.localPosition;
				startLocalRot = t.localRotation;
				startLocalScale = t.localScale;
				float remaining = Mathf.Max( 0.05f, duration - elapsed );
				float retargetDuration = def != null ? def.holdTweenDuration : 0.2f;
				duration = Mathf.Min( remaining, retargetDuration );
				elapsed = 0f;
				flipCoin = false;
				float itemArc = def != null ? def.itemArcHeight : CoinFlipMotion.DefaultItemArcHeight;
				arcHeight = Mathf.Min( arcHeight, itemArc );
			}

			elapsed += Time.deltaTime;
			float u = Mathf.Clamp01( elapsed / duration );

			endLocalPos = GetLocalPose( isActive, heldIndex );
			entry = isActive ? _active : _held[ heldIndex ];
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

		if ( !TryFindEntry( token, out isActive, out heldIndex ) )
			yield break;

		item.EndFlight();
		AttachImmediate( item, isActive, heldIndex );
	}

	bool TryFindEntry( int token, out bool isActive, out int heldIndex )
	{
		isActive = false;
		heldIndex = -1;

		if ( _hasActive && _active.Token == token )
		{
			isActive = true;
			return true;
		}

		for ( int i = 0; i < _held.Count; i++ )
		{
			if ( _held[ i ].Token != token )
				continue;

			heldIndex = i;
			return true;
		}

		return false;
	}

	void AttachImmediate( TreasureItem item, bool active, int heldIndex )
	{
		Transform parent = GetItemParent( active );
		if ( item == null || parent == null )
			return;

		Transform t = item.transform;
		t.SetParent( parent, false );
		ApplyPose( t, GetLocalPose( active, heldIndex ), item.GetHeldLocalRotation( active ) );
		item.SetHeldShadows( enabled: false );

		CarriedEntry entry = active ? _active : _held[ heldIndex ];
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
		EnsureCarryParents();
	}

	void EnsureCarryParents()
	{
		if ( _hasActive && _active.Item != null )
		{
			Transform parent = GetItemParent( active: true );
			Transform t = _active.Item.transform;
			if ( parent != null && t.parent != parent && !_holdTweens.ContainsKey( _active.Token ) )
				t.SetParent( parent, true );
		}

		for ( int i = 0; i < _held.Count; i++ )
		{
			CarriedEntry entry = _held[ i ];
			if ( entry.Item == null )
				continue;
			if ( _holdTweens.ContainsKey( entry.Token ) )
				continue;
			if ( entry.ClusterId != 0 && !entry.IsClusterAnchor )
				continue;

			Transform parent = GetItemParent( active: false );
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

		if ( _hasActive && _active.Item != null && !_holdTweens.ContainsKey( _active.Token ) )
			SmoothItemTowardPose( _active.Item, active: true, heldIndex: -1, t );

		for ( int i = 0; i < _held.Count; i++ )
		{
			CarriedEntry entry = _held[ i ];
			if ( entry.Item == null )
				continue;
			if ( _holdTweens.ContainsKey( entry.Token ) )
				continue;
			if ( entry.ClusterId != 0 && !entry.IsClusterAnchor )
				continue;

			SmoothItemTowardPose( entry.Item, active: false, heldIndex: i, t );
		}
	}

	void SmoothItemTowardPose( TreasureItem item, bool active, int heldIndex, float t )
	{
		Transform parent = GetItemParent( active );
		if ( item == null || parent == null )
			return;

		Transform visual = item.transform;
		if ( visual.parent != parent )
			visual.SetParent( parent, true );

		Vector3 targetPos = GetLocalPose( active, heldIndex );
		Quaternion targetRot = item.GetHeldLocalRotation( active );
		Vector3 targetScale = item.GetHeldScale();

		CarriedEntry entry = active ? _active : _held[ heldIndex ];
		if ( entry.ClusterId != 0 && entry.IsClusterAnchor )
			targetScale = item.GetWorldScale();

		visual.localPosition = Vector3.Lerp( visual.localPosition, targetPos, t );
		visual.localRotation = Quaternion.Slerp( visual.localRotation, targetRot, t );
		visual.localScale = Vector3.Lerp( visual.localScale, targetScale, t );
	}

	Transform GetItemParent( bool active )
	{
		return active ? _activeRoot : _holdRoot;
	}

	Vector3 GetLocalPose( bool active, int heldIndex )
	{
		// Active sits at ActiveRoot origin (root is already screen-centered).
		if ( active )
			return Vector3.zero;

		CarryDefinition def = Definition;
		Vector3 heldBase = def != null ? def.heldStackOffset : Vector3.zero;
		float y = 0f;
		for ( int i = 0; i < heldIndex && i < _held.Count; i++ )
		{
			CarriedEntry entry = _held[ i ];
			if ( entry.ClusterId != 0 && !entry.IsClusterAnchor )
				continue;

			y += GetHeldStackStep( entry.Definition, def );
		}

		return heldBase + GetHeldStackHorizontalOffset( heldIndex ) + new Vector3( 0f, y, 0f );
	}

	Vector3 GetHeldStackHorizontalOffset( int heldIndex )
	{
		CarryDefinition def = Definition;
		float spread = def != null ? def.heldStackHorizontalSpread : 0.012f;
		if ( spread <= 0.0001f || heldIndex < 0 )
			return Vector3.zero;

		TreasureDefinition definition = heldIndex < _held.Count ? _held[ heldIndex ].Definition : null;
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
		float step = TreasureStackSpacing.GetStep( definition );
		if ( definition != null )
		{
			float worldY = Mathf.Abs( definition.worldScale.y );
			float heldY = Mathf.Abs( definition.heldScale.y );
			if ( worldY > 0.0001f && heldY > 0.0001f )
				step *= heldY / worldY;
		}

		float padding = carryDef != null ? carryDef.stackPadding : 0.01f;
		float fallback = carryDef != null ? carryDef.fallbackStackStep : 0.04f;
		if ( step < 0.0001f )
			step = fallback;

		return step + padding;
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

		if ( _holdRoot != null )
		{
			_holdRoot.localPosition = holdOffset + _smoothedMotionOffset;
			_holdRoot.localRotation = Quaternion.Euler( holdEuler + uprightEuler );
			_holdRoot.localScale = Vector3.one;
		}

		if ( _activeRoot != null )
		{
			// Lighter motion on the screen-center Active so it stays readable.
			_activeRoot.localPosition = activeOffset + _smoothedMotionOffset * 0.35f;
			_activeRoot.localRotation = Quaternion.Euler( activeEuler + uprightEuler * 0.5f );
			_activeRoot.localScale = Vector3.one;
		}
	}

	void OnDestroy()
	{
		TreasureProximitySleep.ClearPlayer( transform.root != null ? transform.root : transform );
		Clear();

		if ( _holdRoot != null )
		{
			Destroy( _holdRoot.gameObject );
			_holdRoot = null;
		}

		if ( _activeRoot != null )
		{
			Destroy( _activeRoot.gameObject );
			_activeRoot = null;
		}
	}
}
