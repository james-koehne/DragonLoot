using System.Collections;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Physical coin tower: place matching carried coins onto the stack; take the entire stack
/// (capacity-limited from the top). Settled coins use a cylinder visual; real TreasureItems
/// exist only during place tweens and take handoffs.
/// </summary>
public class CoinStackInteractable : StackInteractable, ITreasureOwner, ITreasurePlacementTarget
{
	[SerializeField]
	Transform stackRoot;

	[SerializeField]
	CoinStackCylinderVisual cylinderVisual;

	[SerializeField]
	CapsuleCollider stackCollider;

	[SerializeField]
	[Tooltip( "0 = unlimited." )]
	[Min( 0 )]
	int maxStackHeight = 0;

	[SerializeField]
	[Min( 0.05f )]
	float placementDuration = 0.18f;

	[SerializeField]
	AnimationCurve placementEasing = AnimationCurve.EaseInOut( 0f, 0f, 1f, 1f );

	[SerializeField]
	[Min( 0f )]
	float placementYawJitterDegrees = 12f;

	[SerializeField]
	[Min( 0f )]
	float placementTiltJitterDegrees = 0f;

	[SerializeField]
	[Min( 0f )]
	float landingBounceHeight = 0.012f;

	readonly List<TreasureItem> _inFlight = new List<TreasureItem>();
	static readonly List<TreasureItem> TakeSpawnBuffer = new List<TreasureItem>();
	int _settledCount;
	bool _taking;
	bool _initializedVisual;

	public TreasureOwnerKind OwnerKind => TreasureOwnerKind.CoinStack;

	public Transform StackRoot => stackRoot != null ? stackRoot : transform;
	public int CoinCount => _settledCount + _inFlight.Count;
	public int SettledCount => _settledCount;
	public float CurrentStackHeight => GetThickness() * CoinCount;
	public float SettledStackHeight => GetThickness() * _settledCount;
	public int MaxStackHeight => maxStackHeight;
	public bool IsFull => maxStackHeight > 0 && CoinCount >= maxStackHeight;
	public Vector3 StackTopMarker => GetSlotWorldPosition( CoinCount );

	protected override void Reset()
	{
		EnsureFallbackName( "Coin Stack" );
	}

	protected override void Awake()
	{
		EnsureFallbackName( "Coin Stack" );
		base.Awake();
		EnsureVisualAndCollider();
		_settledCount = Mathf.Max( 0, RemainingCount );
		RefreshVisualAndCollider( snap: true );
		_initializedVisual = true;
	}

	void Start()
	{
		if ( !_initializedVisual )
		{
			EnsureVisualAndCollider();
			_settledCount = Mathf.Max( 0, RemainingCount );
			RefreshVisualAndCollider( snap: true );
			_initializedVisual = true;
		}
	}

	public void ReleaseTreasure( TreasureItem item )
	{
		Remove( item );
	}

	public void Remove( TreasureItem item )
	{
		if ( item == null )
			return;

		if ( !_inFlight.Remove( item ) )
			return;

		SyncCountsFromLogic();
		RefreshVisualAndCollider( snap: false );
		NotifySortedDelta( item.Definition, -1 );
		EventBus.Publish( new TreasureRemovedEvent
		{
			Target = this,
			Item = item,
			Definition = item.Definition
		} );
	}

	public override void InitializeCount( int count )
	{
		base.InitializeCount( count );
		_settledCount = Mathf.Max( 0, count );
		if ( maxStackHeight > 0 )
			_settledCount = Mathf.Min( _settledCount, maxStackHeight );

		_inFlight.Clear();
		SyncCountsFromLogic();

		if ( isActiveAndEnabled && Application.isPlaying )
		{
			EnsureVisualAndCollider();
			RefreshVisualAndCollider( snap: true );
			_initializedVisual = true;
		}
	}

	public override bool CanInteract( PlayerController player )
	{
		if ( !IsAvailable || player == null || _taking )
			return false;

		return CanTakeFromIndex( player, 0 );
	}

	public override void Interact( PlayerController player )
	{
		if ( player == null || _taking )
			return;

		TryTakeFromIndexUp( player, 0 );
	}

	public bool CanPlace( PlayerController player )
	{
		if ( player == null || IsFull || Treasure == null )
			return false;

		PlayerCarry carry = player.Carry;
		if ( carry == null || !carry.TryPeekActive( out TreasureDefinition definition, out _ ) )
			return false;

		return MatchesAccepted( definition );
	}

	public bool CanPlace( TreasureItem item, in PlacementQuery query )
	{
		if ( item == null || !IsAvailable || IsFull || Treasure == null )
			return false;

		return MatchesAccepted( item.Definition );
	}

	public bool TryGetPlacementPreview( TreasureItem item, in PlacementQuery query, out PlacementPreview preview )
	{
		preview = default;
		if ( item == null )
			return false;

		Transform root = StackRoot;
		int slotIndex = CoinCount;
		preview.Position = GetSlotWorldPosition( slotIndex );
		preview.Rotation = root.rotation;
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

		return TryPlaceFromCarry( player );
	}

	public bool CanTake( PlayerController player )
	{
		return CanTakeFromIndex( player, 0 );
	}

	public bool CanTakeFromIndex( PlayerController player, int startIndex )
	{
		if ( player == null || Treasure == null )
			return false;

		startIndex = Mathf.Clamp( startIndex, 0, Mathf.Max( 0, CoinCount - 1 ) );
		int available = CoinCount - startIndex;
		if ( available <= 0 )
			return false;

		PlayerCarry carry = player.Carry;
		if ( carry == null || carry.CountAffordableUnits( Treasure, available ) <= 0 )
			return false;

		if ( _inFlight.Count > 0 )
		{
			int topIndex = _settledCount + _inFlight.Count - 1;
			if ( topIndex >= startIndex )
			{
				TreasureItem top = _inFlight[ _inFlight.Count - 1 ];
				return top != null && player.CanReceiveTreasureItem( top );
			}
		}

		if ( _settledCount <= startIndex )
			return false;

		return carry.CanAdd( Treasure );
	}

	public bool TryTakeFromIndexUp( PlayerController player, int startIndex )
	{
		if ( !CanTakeFromIndex( player, startIndex ) )
			return false;

		startIndex = Mathf.Clamp( startIndex, 0, Mathf.Max( 0, CoinCount - 1 ) );
		int available = CoinCount - startIndex;
		PlayerCarry carry = player.Carry;
		if ( carry == null || Treasure == null )
			return false;

		int takeCount = carry.CountAffordableUnits( Treasure, available );
		if ( takeCount <= 0 )
			return false;

		int taken = 0;
		while ( taken < takeCount && CoinCount > startIndex )
		{
			if ( _inFlight.Count > 0 )
			{
				int topIndex = _settledCount + _inFlight.Count - 1;
				if ( topIndex < startIndex )
					break;

				if ( !TryTakeInFlightTop( player ) )
					break;

				taken++;
				continue;
			}

			break;
		}

		int remaining = takeCount - taken;
		if ( remaining <= 0 )
			return taken > 0;

		if ( _taking )
			return taken > 0;

		_taking = true;
		TryTakeSettledRangeAsync( player, startIndex, remaining );
		return true;
	}

	public bool TryTakeTop( PlayerController player )
	{
		return TryTakeFromIndexUp( player, 0 );
	}

	public bool TryPlaceFromCarry( PlayerController player )
	{
		if ( !CanPlace( player ) )
			return false;

		PlayerCarry carry = player.Carry;
		if ( carry == null || !carry.TryConsumeActive( out TreasureItem item ) || item == null )
			return false;

		if ( !MatchesAccepted( item.Definition ) )
		{
			item.EnterPhysics( item.transform.position, item.transform.rotation );
			return false;
		}

		int slotIndex = CoinCount;
		Vector3 localPos = GetSlotLocalPosition( slotIndex );
		Quaternion localRot = GetPlacementLocalRotation();

		_inFlight.Add( item );
		SyncCountsFromLogic();
		NotifySortedDelta( item.Definition, 1 );

		item.BeginFlight();
		StartCoroutine( PlaceTweenRoutine( item, localPos, localRot ) );
		return true;
	}

	bool TryTakeInFlightTop( PlayerController player )
	{
		int topIndex = _inFlight.Count - 1;
		TreasureItem item = _inFlight[ topIndex ];
		_inFlight.RemoveAt( topIndex );
		SyncCountsFromLogic();
		RefreshVisualAndCollider( snap: false );

		if ( !player.TryReceiveTreasureItem( item ) )
		{
			ReattachInFlightImmediate( item );
			return false;
		}

		NotifySortedDelta( item.Definition, -1 );
		EventBus.Publish( new TreasureRemovedEvent
		{
			Target = this,
			Item = item,
			Definition = item.Definition
		} );
		return true;
	}

	async void TryTakeSettledRangeAsync( PlayerController player, int startIndex, int takeCount )
	{
		TakeSpawnBuffer.Clear();
		if ( player == null || Treasure == null || takeCount <= 0 )
		{
			_taking = false;
			return;
		}

		Transform root = StackRoot;
		Quaternion worldRot = root.rotation * GetPlacementLocalRotation();

		for ( int n = 0; n < takeCount; n++ )
		{
			if ( _settledCount <= startIndex )
				break;

			_settledCount--;
			SyncCountsFromLogic();
			RefreshVisualAndCollider( snap: false );

			int topIndex = _settledCount;
			Vector3 worldPos = GetSlotWorldPosition( topIndex );

			TreasureItem item = await TreasureItemFactory.SpawnAsync( Treasure, worldPos, worldRot, null );
			if ( item == null )
			{
				_settledCount++;
				SyncCountsFromLogic();
				RefreshVisualAndCollider( snap: false );
				break;
			}

			item.ApplyWorldScale();
			TakeSpawnBuffer.Insert( 0, item );
		}

		if ( TakeSpawnBuffer.Count == 0 )
		{
			_taking = false;
			return;
		}

		if ( !player.TryReceiveSupportStack( TakeSpawnBuffer ) )
		{
			for ( int i = 0; i < TakeSpawnBuffer.Count; i++ )
			{
				TreasureItem failed = TakeSpawnBuffer[ i ];
				if ( failed != null )
					TreasureItemFactory.Despawn( failed );
			}

			_settledCount += TakeSpawnBuffer.Count;
			SyncCountsFromLogic();
			RefreshVisualAndCollider( snap: false );
			TakeSpawnBuffer.Clear();
			_taking = false;
			return;
		}

		for ( int i = 0; i < TakeSpawnBuffer.Count; i++ )
		{
			TreasureItem removed = TakeSpawnBuffer[ i ];
			if ( removed == null )
				continue;

			NotifySortedDelta( removed.Definition, -1 );
			EventBus.Publish( new TreasureRemovedEvent
			{
				Target = this,
				Item = removed,
				Definition = removed.Definition
			} );
		}

		TakeSpawnBuffer.Clear();
		_taking = false;

		if ( CoinCount <= 0 )
			RefreshVisualAndCollider( snap: true );
	}

	protected override void OnEmptied()
	{
		// Keep empty stacks active so they can be rebuilt.
	}

	protected virtual void PlayPlacementFx()
	{
		// SFX / sparkle hook — filled in a later block.
	}

	IEnumerator PlaceTweenRoutine( TreasureItem item, Vector3 endLocalPos, Quaternion endLocalRot )
	{
		if ( item == null )
			yield break;

		Transform root = StackRoot;
		Transform t = item.transform;
		t.SetParent( null, true );

		item.ApplyWorldScale();
		Rigidbody body = item.Body;
		if ( body != null )
		{
			body.isKinematic = true;
			body.detectCollisions = false;
			body.useGravity = false;
			body.constraints = RigidbodyConstraints.None;
			body.linearVelocity = Vector3.zero;
			body.angularVelocity = Vector3.zero;
		}

		Vector3 startPos = t.position;
		Quaternion startRot = t.rotation;
		Vector3 startScale = t.localScale;
		Vector3 endWorldPos = root.TransformPoint( endLocalPos );
		Quaternion endWorldRot = root.rotation * endLocalRot;
		Vector3 endScale = item.GetWorldScale();
		bool flipCoin = CoinFlipMotion.IsCoin( item );

		float duration = Mathf.Max( 0.05f, flipCoin ? Mathf.Max( placementDuration, CoinFlipMotion.DefaultDuration ) : placementDuration );
		float elapsed = 0f;

		try
		{
			while ( elapsed < duration )
			{
				if ( item == null )
					yield break;

				// Taken mid-flight — stop animating; pickup tween owns the transform.
				if ( !_inFlight.Contains( item ) )
					yield break;

				if ( item.State == TreasureItemState.Held || item.IsWorldLoose )
				{
					if ( _inFlight.Remove( item ) )
					{
						SyncCountsFromLogic();
						RefreshVisualAndCollider( snap: false );
						NotifySortedDelta( item.Definition, -1 );
					}

					yield break;
				}

				elapsed += Time.deltaTime;
				float u = Mathf.Clamp01( elapsed / duration );

				if ( flipCoin )
				{
					t.position = CoinFlipMotion.EvaluateArcPosition( startPos, endWorldPos, u, CoinFlipMotion.DefaultArcHeight, root.up );
					t.rotation = CoinFlipMotion.EvaluateFlipRotation( startRot, endWorldRot, startPos, endWorldPos, u, CoinFlipMotion.DefaultSpins );
					t.localScale = Vector3.Lerp( startScale, endScale, CoinFlipMotion.SmoothStep( u ) );
				}
				else
				{
					float eased = placementEasing != null && placementEasing.keys.Length > 0
						? Mathf.Clamp01( placementEasing.Evaluate( u ) )
						: SmoothStep( u );

					float bounce = 0f;
					if ( landingBounceHeight > 0.0001f && u > 0.7f )
					{
						float b = ( u - 0.7f ) / 0.3f;
						bounce = Mathf.Sin( b * Mathf.PI ) * landingBounceHeight * ( 1f - b );
					}

					Vector3 pos = Vector3.Lerp( startPos, endWorldPos, eased );
					pos += root.up * bounce;

					t.position = pos;
					t.rotation = Quaternion.Slerp( startRot, endWorldRot, eased );
					t.localScale = Vector3.Lerp( startScale, endScale, eased );
				}

				yield return null;
			}
		}
		finally
		{
			if ( item != null )
			{
				item.EndFlight();
				if ( _inFlight.Contains( item ) )
				{
					_inFlight.Remove( item );
					_settledCount++;
					if ( maxStackHeight > 0 )
						_settledCount = Mathf.Min( _settledCount, maxStackHeight );

					SyncCountsFromLogic();
					RefreshVisualAndCollider( snap: false );
					PlayPlacementFx();
					TreasureItemFactory.Despawn( item );
				}
			}
		}
	}

	void ReattachInFlightImmediate( TreasureItem item )
	{
		if ( item == null )
			return;

		_inFlight.Add( item );
		SyncCountsFromLogic();
		RefreshVisualAndCollider( snap: false );

		int slotIndex = CoinCount - 1;
		item.BeginFlight();
		item.EnterStacked( this, StackRoot, GetSlotLocalPosition( slotIndex ), GetPlacementLocalRotation() );
		item.EndFlight();
	}

	void SyncCountsFromLogic()
	{
		int count = CoinCount;
		SetRemainingCount( count );
		if ( count > TotalCount )
			SetTotalCount( count );
	}

	void RefreshVisualAndCollider( bool snap )
	{
		EnsureVisualAndCollider();

		if ( cylinderVisual != null )
		{
			if ( snap )
				cylinderVisual.SnapToCount( Treasure, _settledCount );
			else
				cylinderVisual.SetStack( Treasure, _settledCount );
		}

		SyncStackCollider();
	}

	void EnsureVisualAndCollider()
	{
		Transform root = StackRoot;

		if ( cylinderVisual == null )
			cylinderVisual = root.GetComponent<CoinStackCylinderVisual>();
		if ( cylinderVisual == null )
			cylinderVisual = GetComponent<CoinStackCylinderVisual>();
		if ( cylinderVisual == null )
			cylinderVisual = root.gameObject.AddComponent<CoinStackCylinderVisual>();

		if ( stackCollider == null )
			stackCollider = root.GetComponent<CapsuleCollider>();
		if ( stackCollider == null && root != transform )
			stackCollider = GetComponent<CapsuleCollider>();
		if ( stackCollider == null )
			stackCollider = root.gameObject.AddComponent<CapsuleCollider>();

		stackCollider.direction = 1; // Y-axis
		stackCollider.isTrigger = false;
	}

	void SyncStackCollider()
	{
		if ( stackCollider == null )
			return;

		float thickness = GetThickness();
		float height = Mathf.Max( thickness, SettledStackHeight );
		if ( _settledCount <= 0 && _inFlight.Count <= 0 )
			height = thickness;

		float diameter = 0.2f;
		if ( Treasure != null )
		{
			float x = Mathf.Abs( Treasure.worldScale.x );
			float z = Mathf.Abs( Treasure.worldScale.z );
			diameter = Mathf.Max( x, z );
			if ( diameter < 0.0001f )
				diameter = 0.2f;
		}

		float radius = diameter * 0.5f;
		bool hasCoins = CoinCount > 0;
		stackCollider.enabled = hasCoins;
		stackCollider.radius = radius;
		// Extend by radius past top and bottom so the cylindrical body (not the
		// hemispherical caps) spans the full coin stack for reliable selection.
		stackCollider.height = Mathf.Max( height + radius * 2f, radius * 2f );
		stackCollider.center = new Vector3( 0f, height * 0.5f, 0f );
	}

	static void NotifySortedDelta( TreasureDefinition definition, int delta )
	{
		TreasureCounterManager manager = TreasureCounterManager.Instance;
		if ( manager != null && definition != null )
			manager.NotifySortedDelta( definition, delta );
	}

	bool MatchesAccepted( TreasureDefinition definition )
	{
		if ( Treasure == null || definition == null || !definition.canStack || !Treasure.canStack )
			return false;

		if ( Treasure == definition )
			return true;

		if ( !string.IsNullOrEmpty( Treasure.id ) && Treasure.id == definition.id )
			return true;

		return false;
	}

	float GetThickness()
	{
		return TreasureStackSpacing.GetStep( Treasure );
	}

	Vector3 GetSlotLocalPosition( int index )
	{
		return Vector3.up * ( GetThickness() * index );
	}

	Vector3 GetSlotWorldPosition( int index )
	{
		Transform root = StackRoot;
		return root.TransformPoint( GetSlotLocalPosition( index ) );
	}

	Quaternion GetPlacementLocalRotation()
	{
		float yaw = Random.Range( -placementYawJitterDegrees, placementYawJitterDegrees );
		float tiltX = Random.Range( -placementTiltJitterDegrees, placementTiltJitterDegrees );
		float tiltZ = Random.Range( -placementTiltJitterDegrees, placementTiltJitterDegrees );
		return Quaternion.Euler( tiltX, yaw, tiltZ );
	}

	static float SmoothStep( float u )
	{
		return u * u * ( 3f - 2f * u );
	}
}
