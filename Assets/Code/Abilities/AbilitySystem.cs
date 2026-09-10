using System;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Session ability unlock / equip / cooldown / activate hub.
/// Owned by <see cref="PlayerAbilities"/>; also exposed as <see cref="Instance"/> for debug and unlock callers.
/// </summary>
public sealed class AbilitySystem
{
	public const int SlotCount = ProfileSaveData.AbilitySlotCount;

	public static AbilitySystem Instance { get; private set; }

	readonly Dictionary<string, bool> _unlocked = new Dictionary<string, bool>();
	readonly Dictionary<string, float> _cooldownRemaining = new Dictionary<string, float>();
	readonly List<string> _cooldownScratch = new List<string>();
	readonly string[] _equippedSlots = new string[ SlotCount ];

	AbilityCatalogDefinition _catalog;
	PlayerController _player;
	bool _initialized;

	public AbilityCatalogDefinition Catalog => _catalog;
	public int EquippedSlotCount => SlotCount;

	public static AbilitySystem Ensure( PlayerController player )
	{
		if ( Instance == null )
			Instance = new AbilitySystem();

		Instance.BindPlayer( player );
		Instance.Initialize();
		return Instance;
	}

	public static void ClearInstanceIfOwner( PlayerController player )
	{
		if ( Instance != null && Instance._player == player )
		{
			Instance._player = null;
			Instance = null;
		}
	}

	void BindPlayer( PlayerController player )
	{
		_player = player;
	}

	void Initialize()
	{
		if ( !_initialized )
		{
			LoadFromProfile();
			_initialized = true;
		}

		if ( _catalog == null )
			_catalog = GameInstance.GetDefinition<AbilityCatalogDefinition>();
	}

	public void Tick( float deltaTime )
	{
		if ( deltaTime <= 0f || _cooldownRemaining.Count == 0 )
			return;

		_cooldownScratch.Clear();
		foreach ( KeyValuePair<string, float> pair in _cooldownRemaining )
			_cooldownScratch.Add( pair.Key );

		for ( int i = 0; i < _cooldownScratch.Count; i++ )
		{
			string id = _cooldownScratch[ i ];
			float remaining = _cooldownRemaining[ id ] - deltaTime;
			if ( remaining <= 0f )
				_cooldownRemaining.Remove( id );
			else
				_cooldownRemaining[ id ] = remaining;
		}
	}

	public IReadOnlyList<AbilityDefinition> GetCatalogAbilities()
	{
		if ( _catalog == null || _catalog.abilities == null )
			return Array.Empty<AbilityDefinition>();
		return _catalog.abilities;
	}

	public bool TryGetDefinition( string id, out AbilityDefinition definition )
	{
		definition = _catalog != null ? _catalog.GetById( id ) : null;
		return definition != null;
	}

	public bool IsUnlocked( string id )
	{
		if ( string.IsNullOrEmpty( id ) )
			return false;
		return _unlocked.TryGetValue( id, out bool unlocked ) && unlocked;
	}

	public bool UnlockAbility( string id )
	{
		if ( string.IsNullOrEmpty( id ) )
			return false;
		if ( !TryGetDefinition( id, out AbilityDefinition definition ) )
			return false;
		if ( IsUnlocked( id ) )
			return false;

		_unlocked[ id ] = true;
		PersistProgress();
		EventBus.Publish( new AbilityUnlockedEvent
		{
			AbilityId = id,
			Definition = definition
		} );
		return true;
	}

	public bool LockAbility( string id )
	{
		if ( string.IsNullOrEmpty( id ) )
			return false;
		if ( !IsUnlocked( id ) )
			return false;

		_unlocked[ id ] = false;
		UnequipAbilityEverywhere( id );
		_cooldownRemaining.Remove( id );
		PersistProgress();
		return true;
	}

	public void UnlockAll()
	{
		IReadOnlyList<AbilityDefinition> abilities = GetCatalogAbilities();
		bool changed = false;
		for ( int i = 0; i < abilities.Count; i++ )
		{
			AbilityDefinition def = abilities[ i ];
			if ( def == null || string.IsNullOrEmpty( def.id ) )
				continue;
			if ( IsUnlocked( def.id ) )
				continue;
			_unlocked[ def.id ] = true;
			changed = true;
		}

		if ( changed )
			PersistProgress();
	}

	public void ResetProgress()
	{
		_unlocked.Clear();
		_cooldownRemaining.Clear();
		for ( int i = 0; i < SlotCount; i++ )
			_equippedSlots[ i ] = null;
		PersistProgress();
	}

	public string GetEquippedAbilityId( int slot )
	{
		if ( slot < 0 || slot >= SlotCount )
			return null;
		return _equippedSlots[ slot ];
	}

	public bool EquipAbility( int slot, string id )
	{
		if ( slot < 0 || slot >= SlotCount )
			return false;
		if ( string.IsNullOrEmpty( id ) )
			return UnequipSlot( slot );
		if ( !IsUnlocked( id ) )
			return false;
		if ( !TryGetDefinition( id, out AbilityDefinition definition ) )
			return false;
		if ( definition.isPassive )
			return false;

		UnequipAbilityEverywhere( id );
		_equippedSlots[ slot ] = id;
		PersistProgress();
		return true;
	}

	public bool UnequipSlot( int slot )
	{
		if ( slot < 0 || slot >= SlotCount )
			return false;
		if ( string.IsNullOrEmpty( _equippedSlots[ slot ] ) )
			return false;

		_equippedSlots[ slot ] = null;
		PersistProgress();
		return true;
	}

	public float GetCooldownRemaining( string id )
	{
		if ( string.IsNullOrEmpty( id ) )
			return 0f;
		return _cooldownRemaining.TryGetValue( id, out float remaining ) ? Mathf.Max( 0f, remaining ) : 0f;
	}

	public int FindEquippedSlot( string id )
	{
		if ( string.IsNullOrEmpty( id ) )
			return -1;
		for ( int i = 0; i < SlotCount; i++ )
		{
			if ( _equippedSlots[ i ] == id )
				return i;
		}
		return -1;
	}

	public AbilityStatus GetStatus( string id )
	{
		if ( string.IsNullOrEmpty( id ) || !TryGetDefinition( id, out AbilityDefinition definition ) )
			return AbilityStatus.Locked;

		if ( !IsUnlocked( id ) )
			return AbilityStatus.Locked;

		if ( GetCooldownRemaining( id ) > 0f )
			return AbilityStatus.OnCooldown;

		int slot = FindEquippedSlot( id );
		if ( slot < 0 )
			return AbilityStatus.Unlocked;

		if ( definition.enabled )
			return AbilityStatus.Ready;

		return AbilityStatus.Equipped;
	}

	public bool TryActivateSlot( int slot )
	{
		if ( slot < 0 || slot >= SlotCount )
			return false;

		string id = _equippedSlots[ slot ];
		if ( string.IsNullOrEmpty( id ) )
			return false;

		if ( !TryGetDefinition( id, out AbilityDefinition definition ) )
			return false;

		if ( !IsUnlocked( id ) )
			return false;

		if ( definition.isPassive )
			return false;

		if ( !definition.enabled )
			return false;

		if ( GetCooldownRemaining( id ) > 0f )
			return false;

		AbilityBehaviour behaviour = definition.behaviour;
		if ( behaviour == null )
		{
			Debug.LogWarning( $"[Ability] '{id}' has no behaviour assigned." );
			return false;
		}

		AbilityActivationContext context = new AbilityActivationContext( definition, _player, slot );
		if ( !behaviour.TryActivate( in context ) )
			return false;

		float cooldown = Mathf.Max( 0f, definition.cooldown );
		if ( cooldown > 0f )
			_cooldownRemaining[ id ] = cooldown;

		return true;
	}

	void UnequipAbilityEverywhere( string id )
	{
		for ( int i = 0; i < SlotCount; i++ )
		{
			if ( _equippedSlots[ i ] == id )
				_equippedSlots[ i ] = null;
		}
	}

	void LoadFromProfile()
	{
		_unlocked.Clear();
		_cooldownRemaining.Clear();
		for ( int i = 0; i < SlotCount; i++ )
			_equippedSlots[ i ] = null;

		ProfileManager profileManager = ProfileManager.Instance;
		if ( profileManager == null || profileManager.ProfileSaveData == null )
			return;

		ProfileSaveData save = profileManager.ProfileSaveData;
		save.EnsureProgressDictionaries();

		if ( save.unlockedAbilities != null )
		{
			foreach ( KeyValuePair<string, bool> pair in save.unlockedAbilities )
			{
				if ( pair.Value )
					_unlocked[ pair.Key ] = true;
			}
		}

		if ( save.equippedAbilityIds != null )
		{
			int count = Mathf.Min( SlotCount, save.equippedAbilityIds.Length );
			for ( int i = 0; i < count; i++ )
			{
				string id = save.equippedAbilityIds[ i ];
				if ( string.IsNullOrEmpty( id ) )
					continue;
				if ( !IsUnlocked( id ) )
					continue;
				if ( !TryGetDefinition( id, out AbilityDefinition equippedDef ) )
					continue;
				if ( equippedDef.isPassive )
					continue;
				UnequipAbilityEverywhere( id );
				_equippedSlots[ i ] = id;
			}
		}
	}

	void PersistProgress()
	{
		ProfileManager profileManager = ProfileManager.Instance;
		if ( profileManager == null || profileManager.ProfileSaveData == null )
			return;

		ProfileSaveData save = profileManager.ProfileSaveData;
		save.EnsureProgressDictionaries();

		save.unlockedAbilities.Clear();
		foreach ( KeyValuePair<string, bool> pair in _unlocked )
		{
			if ( pair.Value )
				save.unlockedAbilities[ pair.Key ] = true;
		}

		if ( save.equippedAbilityIds == null || save.equippedAbilityIds.Length != SlotCount )
			save.equippedAbilityIds = new string[ SlotCount ];

		for ( int i = 0; i < SlotCount; i++ )
			save.equippedAbilityIds[ i ] = _equippedSlots[ i ];

		profileManager.SaveCurrentStatsToProfile();
	}
}
