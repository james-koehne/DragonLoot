using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Primary interact for buried / revealed treasure chests.
/// Chests must be picked up from a gold pile and placed on the ground before unlock / open.
/// Unlock with a matching key (or skeleton key), or lockpick when hands cannot take the chest.
/// </summary>
[RequireComponent( typeof( TreasureItem ) )]
public class ChestInteractable : InteractableBase
{
	public const string LockpickAbilityId = "lockpick";
	const float DefaultLockpickDuration = 8f;
	const float LootScatterMinRadius = 1.1f;
	const float LootScatterMaxRadius = 2.0f;
	const float LootUpBias = 0.75f;
	const float LootLaunchHorizontalMin = 2.0f;
	const float LootLaunchHorizontalMax = 4.2f;
	const float LootLaunchUpMin = 4.5f;
	const float LootLaunchUpMax = 6.5f;

	[SerializeField]
	ChestDefinition chestDefinition;

	[SerializeField]
	ChestState state = ChestState.Locked;

	[Header( "Feedbacks" )]
	[SerializeField]
	Feedbacks onOpenFeedback;

	TreasureItem _item;
	float _lockpickEndsAt = -1f;
	bool _spawningLoot;

	public ChestState State => state;
	public ChestDefinition Definition => ResolveDefinition();
	public TreasureItem Item => ResolveItem();

	public float LockpickRemaining
	{
		get
		{
			if ( state != ChestState.Lockpicking || _lockpickEndsAt < 0f )
				return 0f;
			return Mathf.Max( 0f, _lockpickEndsAt - Time.time );
		}
	}

	void Awake()
	{
		ResolveItem();
		ApplyDefinitionDefaults();
		RefreshInteractionName();
	}

	void OnEnable()
	{
		ResolveItem();
		ApplyDefinitionDefaults();
		RefreshInteractionName();
	}

	void Update()
	{
		if ( state != ChestState.Lockpicking )
			return;

		if ( _lockpickEndsAt < 0f || Time.time < _lockpickEndsAt )
			return;

		CompleteLockpick();
	}

	public void BindDefinition( ChestDefinition definition )
	{
		if ( definition == null )
			return;

		chestDefinition = definition;
		ApplyDefinitionDefaults();
		RefreshInteractionName();
	}

	public override bool CanInteract( PlayerController player )
	{
		if ( !base.CanInteract( player ) || player == null )
			return false;

		TreasureItem item = Item;
		if ( item == null )
			return false;

		if ( IsBuried( item ) )
			return false;

		if ( item.State == TreasureItemState.Held
			|| item.State == TreasureItemState.Stacked
			|| item.IsReclaiming )
			return false;

		string label = ResolveChestLabel();

		// Still seated in a gold pile — carry out first; unlock/open only on the ground.
		if ( IsAttachedToPile( item ) )
		{
			if ( !CanPickup( player, item ) )
				return false;

			SetInteractionName( "Pick up " + label );
			return true;
		}

		switch ( state )
		{
			case ChestState.Locked:
				if ( ChestProgress.DisableKeys )
				{
					SetInteractionName( "Open " + label );
					return true;
				}
				if ( TryGetUsableKey( player, out _ ) )
				{
					SetInteractionName( "Unlock " + label );
					return true;
				}
				if ( CanStartLockpick( player ) )
				{
					SetInteractionName( "Lockpick " + label );
					return true;
				}
				if ( CanPickup( player, item ) )
				{
					SetInteractionName( "Pick up " + label );
					return true;
				}
				return false;
			case ChestState.Unlocked:
				SetInteractionName( "Open " + label );
				return true;
			case ChestState.Opened:
				if ( CanPickup( player, item ) )
				{
					SetInteractionName( "Pick up " + label );
					return true;
				}
				return false;
			default:
				return false;
		}
	}

	public override void Interact( PlayerController player )
	{
		if ( player == null || !CanInteract( player ) )
			return;

		TreasureItem item = Item;
		if ( item != null && IsAttachedToPile( item ) )
		{
			TryPickup( player );
			return;
		}

		switch ( state )
		{
			case ChestState.Locked:
				if ( ChestProgress.DisableKeys )
				{
					TryForceOpen();
					return;
				}
				if ( TryUnlockWithHeldKey( player ) )
					return;
				if ( CanStartLockpick( player ) )
				{
					TryBeginLockpick( player );
					return;
				}
				TryPickup( player );
				return;
			case ChestState.Unlocked:
				TryOpen();
				return;
			case ChestState.Opened:
				TryPickup( player );
				return;
		}
	}

	bool CanPickup( PlayerController player, TreasureItem item )
	{
		if ( player == null || item == null )
			return false;

		item.TryRepairPickupState();
		if ( item.State == TreasureItemState.Held
			|| item.State == TreasureItemState.Stacked
			|| item.IsReclaiming )
			return false;

		return player.CanReceiveTreasureItem( item );
	}

	bool TryPickup( PlayerController player )
	{
		TreasureItem item = Item;
		if ( player == null || item == null || !CanPickup( player, item ) )
			return false;

		TreasurePileVisual pile = item.PileOwner;
		if ( pile != null )
		{
			if ( !pile.TryBeginSteal( item ) )
				return false;
		}

		if ( !player.TryReceiveTreasureItem( item ) )
		{
			if ( pile != null )
				pile.CancelSteal( item );
			return false;
		}

		if ( pile != null )
			pile.CompleteSteal( item );
		return true;
	}

	public bool TryBeginLockpick( PlayerController player )
	{
		if ( state != ChestState.Locked || ChestProgress.DisableKeys )
			return false;

		TreasureItem item = Item;
		if ( item == null || IsBuried( item ) || IsAttachedToPile( item ) )
			return false;

		if ( !CanStartLockpick( player ) )
			return false;

		float duration = ResolveLockpickDuration();
		state = ChestState.Lockpicking;
		_lockpickEndsAt = Time.time + Mathf.Max( 0.05f, duration );
		RefreshInteractionName();
		return true;
	}

	public bool TryForceUnlock()
	{
		if ( state == ChestState.Opened )
			return false;

		state = ChestState.Unlocked;
		_lockpickEndsAt = -1f;
		RefreshInteractionName();
		return true;
	}

	public bool TryForceOpen()
	{
		if ( state == ChestState.Opened )
			return false;

		state = ChestState.Unlocked;
		_lockpickEndsAt = -1f;
		return TryOpen();
	}

	bool TryOpen()
	{
		if ( state != ChestState.Unlocked )
			return false;

		state = ChestState.Opened;
		_lockpickEndsAt = -1f;
		RefreshInteractionName();
		ChestProgress.RecordChestOpened();
		EventBus.Publish( new ChestOpenedEvent { Chest = this } );
		PlayOpenFeedback();
		SpawnLootAsync();
		return true;
	}

	void PlayOpenFeedback()
	{
		if ( onOpenFeedback == null )
			return;

		FeedbackContext context = new FeedbackContext();
		context.Source = gameObject;
		context.Target = gameObject;
		context.Position = transform.position;
		onOpenFeedback.Play( context );
	}

#if UNITY_EDITOR
	public void EditorSetOpenFeedback( Feedbacks value )
	{
		onOpenFeedback = value;
	}
#endif

	bool TryUnlockWithHeldKey( PlayerController player )
	{
		if ( ChestProgress.DisableKeys )
			return false;

		if ( !TryGetUsableKey( player, out TreasureItem keyItem ) )
			return false;

		TreasureDefinition keyDef = keyItem.Definition;
		bool consume = keyDef == null || !keyDef.isSkeletonKey;

		if ( consume )
		{
			PlayerCarry carry = player.Carry;
			if ( carry == null || !carry.TryConsumeActive( out TreasureItem consumed ) || consumed != keyItem )
				return false;

			TreasureItemFactory.Despawn( consumed );
		}

		state = ChestState.Unlocked;
		_lockpickEndsAt = -1f;
		RefreshInteractionName();
		return true;
	}

	bool TryGetUsableKey( PlayerController player, out TreasureItem keyItem )
	{
		keyItem = null;
		if ( player == null || player.Carry == null )
			return false;

		if ( !player.Carry.TryPeekActive( out TreasureDefinition def, out TreasureItem item ) )
			return false;

		if ( def == null || def.category != TreasureCategory.Key || item == null )
			return false;

		ChestDefinition chest = Definition;
		KeyType required = chest != null ? chest.keyType : KeyType.Iron;
		if ( def.isSkeletonKey || def.keyType == required )
		{
			keyItem = item;
			return true;
		}

		return false;
	}

	static bool CanStartLockpick( PlayerController player )
	{
		if ( player == null )
			return false;

		AbilitySystem system = null;
		if ( player.Abilities != null )
			system = player.Abilities.System;
		if ( system == null )
			system = AbilitySystem.Instance;

		return system != null && system.IsUnlocked( LockpickAbilityId );
	}

	float ResolveLockpickDuration()
	{
		ChestDefinition chest = Definition;
		if ( chest != null && chest.lockpickDuration > 0.01f )
			return chest.lockpickDuration;

		AbilitySystem system = AbilitySystem.Instance;
		if ( system != null
			&& system.TryGetDefinition( LockpickAbilityId, out AbilityDefinition ability )
			&& ability != null
			&& ability.behaviour is LockpickAbilityBehaviour lockpick )
		{
			if ( lockpick.defaultDuration > 0.01f )
				return lockpick.defaultDuration;
		}

		return DefaultLockpickDuration;
	}

	void CompleteLockpick()
	{
		if ( state != ChestState.Lockpicking )
			return;

		state = ChestState.Unlocked;
		_lockpickEndsAt = -1f;
		RefreshInteractionName();
	}

	async void SpawnLootAsync()
	{
		if ( _spawningLoot )
			return;

		_spawningLoot = true;
		try
		{
			ChestDefinition chest = Definition;
			if ( chest == null || chest.contents == null || chest.contents.Length == 0 )
				return;

			Transform self = transform;
			Vector3 origin = self.position + Vector3.up * LootUpBias;

			for ( int i = 0; i < chest.contents.Length; i++ )
			{
				ChestContentEntry entry = chest.contents[ i ];
				if ( entry.treasure == null || entry.count <= 0 )
					continue;

				int count = entry.count;
				for ( int n = 0; n < count; n++ )
				{
					float angle = Random.Range( 0f, Mathf.PI * 2f );
					float radius = Random.Range( LootScatterMinRadius, LootScatterMaxRadius );
					Vector3 planarDir = new Vector3( Mathf.Cos( angle ), 0f, Mathf.Sin( angle ) );

					// Start just outside the chest volume, then launch farther out.
					Vector3 pos = origin + planarDir * ( radius * 0.35f ) + Vector3.up * ( 0.04f * n );
					Quaternion rot = Quaternion.Euler( 0f, Random.Range( 0f, 360f ), 0f );

					TreasureItem spawned = await TreasureItemFactory.SpawnAsync( entry.treasure, pos, rot, null );
					if ( spawned == null )
						continue;

					float horizontal = Random.Range( LootLaunchHorizontalMin, LootLaunchHorizontalMax );
					float up = Random.Range( LootLaunchUpMin, LootLaunchUpMax );
					Vector3 velocity = planarDir * horizontal + Vector3.up * up;
					spawned.EnterPhysics( pos, rot, velocity );
				}
			}
		}
		finally
		{
			_spawningLoot = false;
		}
	}

	void ApplyDefinitionDefaults()
	{
		ChestDefinition chest = Definition;
		if ( chest == null )
			return;

		if ( state == ChestState.Locked && !chest.startsLocked )
			state = ChestState.Unlocked;
	}

	ChestDefinition ResolveDefinition()
	{
		if ( chestDefinition != null )
			return chestDefinition;

		TreasureItem item = Item;
		if ( item != null && item.Definition != null )
			chestDefinition = item.Definition.chestDefinition;

		return chestDefinition;
	}

	TreasureItem ResolveItem()
	{
		if ( _item != null )
			return _item;

		_item = GetComponent<TreasureItem>();
		if ( _item == null )
			_item = GetComponentInParent<TreasureItem>();
		return _item;
	}

	void RefreshInteractionName()
	{
		string label = ResolveChestLabel();

		switch ( state )
		{
			case ChestState.Locked:
				SetInteractionName( "Unlock " + label );
				break;
			case ChestState.Lockpicking:
				SetInteractionName( "Lockpicking " + label );
				break;
			case ChestState.Unlocked:
				SetInteractionName( "Open " + label );
				break;
			case ChestState.Opened:
				SetInteractionName( label + " (Opened)" );
				break;
			default:
				SetInteractionName( label );
				break;
		}
	}

	string ResolveChestLabel()
	{
		ChestDefinition chest = Definition;
		return chest != null ? chest.ResolveDisplayName() : "Chest";
	}

	static bool IsBuried( TreasureItem item )
	{
		if ( item == null )
			return false;

		TreasurePileVisual origin = item.OriginPile;
		if ( origin != null && origin.IsTreasureBuried( item ) )
			return true;

		TreasurePileVisual ownerPile = item.PileOwner;
		if ( ownerPile != null && ownerPile.IsTreasureBuried( item ) )
			return true;

		return false;
	}

	/// <summary>True while the chest is still owned by / seated in a gold pile.</summary>
	static bool IsAttachedToPile( TreasureItem item )
	{
		if ( item == null )
			return false;

		if ( item.PileOwner != null )
			return true;

		return item.State == TreasureItemState.InPile;
	}

	/// <summary>Resolves a chest interactable from a focused interactable or collider chain.</summary>
	public static ChestInteractable ResolveFromInteractable( IInteractable interactable )
	{
		ChestInteractable chest = interactable as ChestInteractable;
		if ( chest != null )
			return chest;

		TreasureItemInteractable itemInteractable = interactable as TreasureItemInteractable;
		if ( itemInteractable == null || itemInteractable.Item == null )
			return null;

		return ResolveFromItem( itemInteractable.Item );
	}

	public static ChestInteractable ResolveFromItem( TreasureItem item )
	{
		if ( item == null )
			return null;

		ChestInteractable chest = item.GetComponent<ChestInteractable>();
		if ( chest != null )
			return chest;

		return item.GetComponentInChildren<ChestInteractable>( true );
	}
}
