using System;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Session upgrade unlock / level hub. Stores progression only; gameplay systems query levels at runtime.
/// </summary>
public sealed class UpgradeSystem
{
	public static UpgradeSystem Instance { get; private set; }

	readonly Dictionary<string, bool> _unlocked = new Dictionary<string, bool>();
	readonly Dictionary<string, int> _levels = new Dictionary<string, int>();

	UpgradeCatalogDefinition _catalog;
	PlayerController _player;
	bool _initialized;

	public UpgradeCatalogDefinition Catalog => _catalog;

	public static UpgradeSystem Ensure( PlayerController player )
	{
		if ( Instance == null )
			Instance = new UpgradeSystem();

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
			_catalog = GameInstance.GetDefinition<UpgradeCatalogDefinition>();
	}

	public IReadOnlyList<UpgradeDefinition> GetCatalogUpgrades()
	{
		if ( _catalog == null || _catalog.upgrades == null )
			return Array.Empty<UpgradeDefinition>();
		return _catalog.upgrades;
	}

	public bool TryGetDefinition( string id, out UpgradeDefinition definition )
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

	public int GetMaxLevel( string id )
	{
		if ( !TryGetDefinition( id, out UpgradeDefinition definition ) )
			return 0;
		return definition.ResolveMaxLevel();
	}

	/// <summary>Applied level for <paramref name="id"/>. Returns 0 when locked, unknown, or not yet purchased.</summary>
	public int GetUpgradeLevel( string id )
	{
		if ( string.IsNullOrEmpty( id ) )
			return 0;
		if ( !IsUnlocked( id ) )
			return 0;
		if ( !_levels.TryGetValue( id, out int level ) )
			return 0;
		int max = GetMaxLevel( id );
		if ( max <= 0 )
			return 0;
		return Mathf.Clamp( level, 0, max );
	}

	public UpgradeStatus GetStatus( string id )
	{
		if ( string.IsNullOrEmpty( id ) || !TryGetDefinition( id, out _ ) )
			return UpgradeStatus.Locked;

		if ( !IsUnlocked( id ) )
			return UpgradeStatus.Locked;

		int level = GetUpgradeLevel( id );
		int max = GetMaxLevel( id );
		if ( max > 0 && level >= max )
			return UpgradeStatus.Maxed;

		return UpgradeStatus.Unlocked;
	}

	public bool UnlockUpgrade( string id )
	{
		if ( string.IsNullOrEmpty( id ) )
			return false;
		if ( !TryGetDefinition( id, out _ ) )
			return false;
		if ( IsUnlocked( id ) )
			return false;

		_unlocked[ id ] = true;
		if ( !_levels.ContainsKey( id ) )
			_levels[ id ] = 0;
		PersistProgress();
		return true;
	}

	public void UnlockAll()
	{
		IReadOnlyList<UpgradeDefinition> upgrades = GetCatalogUpgrades();
		bool changed = false;
		for ( int i = 0; i < upgrades.Count; i++ )
		{
			UpgradeDefinition def = upgrades[ i ];
			if ( def == null || string.IsNullOrEmpty( def.id ) )
				continue;
			if ( IsUnlocked( def.id ) )
				continue;
			_unlocked[ def.id ] = true;
			if ( !_levels.ContainsKey( def.id ) )
				_levels[ def.id ] = 0;
			changed = true;
		}

		if ( changed )
			PersistProgress();
	}

	/// <summary>Unlocks if needed, clamps to <c>0..maxLevel</c>, and persists.</summary>
	public bool SetUpgradeLevel( string id, int level )
	{
		if ( string.IsNullOrEmpty( id ) )
			return false;
		if ( !TryGetDefinition( id, out UpgradeDefinition definition ) )
			return false;

		int max = definition.ResolveMaxLevel();
		int clamped = Mathf.Clamp( level, 0, max );
		bool changed = false;

		if ( !IsUnlocked( id ) )
		{
			_unlocked[ id ] = true;
			changed = true;
		}

		if ( !_levels.TryGetValue( id, out int existing ) || existing != clamped )
		{
			_levels[ id ] = clamped;
			changed = true;
		}

		if ( changed )
			PersistProgress();

		return changed;
	}

	public bool PurchaseNextLevel( string id )
	{
		if ( string.IsNullOrEmpty( id ) )
			return false;
		if ( !TryGetDefinition( id, out UpgradeDefinition definition ) )
			return false;

		if ( !IsUnlocked( id ) )
			return false;

		int max = definition.ResolveMaxLevel();
		int current = GetUpgradeLevel( id );
		if ( current >= max )
			return false;

		return SetUpgradeLevel( id, current + 1 );
	}

	public bool MaxUpgrade( string id )
	{
		if ( string.IsNullOrEmpty( id ) )
			return false;
		if ( !TryGetDefinition( id, out UpgradeDefinition definition ) )
			return false;

		return SetUpgradeLevel( id, definition.ResolveMaxLevel() );
	}

	public bool ResetUpgrade( string id )
	{
		if ( string.IsNullOrEmpty( id ) )
			return false;
		if ( !TryGetDefinition( id, out _ ) )
			return false;

		bool changed = false;
		if ( IsUnlocked( id ) )
		{
			_unlocked[ id ] = false;
			changed = true;
		}

		if ( _levels.ContainsKey( id ) )
		{
			_levels.Remove( id );
			changed = true;
		}

		if ( changed )
			PersistProgress();

		return changed;
	}

	public void ResetAllProgress()
	{
		_unlocked.Clear();
		_levels.Clear();
		PersistProgress();
	}

	void LoadFromProfile()
	{
		_unlocked.Clear();
		_levels.Clear();

		ProfileManager profileManager = ProfileManager.Instance;
		if ( profileManager == null || profileManager.ProfileSaveData == null )
			return;

		ProfileSaveData save = profileManager.ProfileSaveData;
		save.EnsureProgressDictionaries();

		if ( save.unlockedUpgrades != null )
		{
			foreach ( KeyValuePair<string, bool> pair in save.unlockedUpgrades )
			{
				if ( pair.Value )
					_unlocked[ pair.Key ] = true;
			}
		}

		if ( save.upgradeLevels != null )
		{
			foreach ( KeyValuePair<string, int> pair in save.upgradeLevels )
			{
				if ( pair.Value < 0 )
					continue;
				_levels[ pair.Key ] = pair.Value;
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

		save.unlockedUpgrades.Clear();
		foreach ( KeyValuePair<string, bool> pair in _unlocked )
		{
			if ( pair.Value )
				save.unlockedUpgrades[ pair.Key ] = true;
		}

		save.upgradeLevels.Clear();
		foreach ( KeyValuePair<string, int> pair in _levels )
		{
			if ( pair.Value < 0 )
				continue;
			save.upgradeLevels[ pair.Key ] = pair.Value;
		}

		profileManager.SaveCurrentStatsToProfile();
	}
}
