using System.Collections.Generic;

using UnityEngine;

public class DebugAbilitiesSection : DebugOverlaySection
{
	static string s_catalogFilter = string.Empty;

	public string Title => "Abilities";

	public void Draw()
	{
		PlayerController player = DebugOverlay.GetPlayer();
		AbilitySystem system = ResolveSystem( player );
		if ( system == null )
		{
			GUILayout.Label( "No ability system" );
			return;
		}

		IReadOnlyList<AbilityDefinition> catalog = system.GetCatalogAbilities();
		GUILayout.Label( $"Catalog: {catalog.Count}" );

		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Unlock All" ) )
			system.UnlockAll();
		if ( GUILayout.Button( "Reset Progress" ) )
			system.ResetProgress();
		GUILayout.EndHorizontal();

		GUILayout.Space( 6f );
		GUILayout.Label( "Slots" );
		for ( int slot = 0; slot < AbilitySystem.SlotCount; slot++ )
			DrawSlot( system, catalog, slot );

		GUILayout.Space( 6f );
		GUILayout.Label( "Catalog" );
		DebugOverlayGui.DrawSearchField( ref s_catalogFilter );
		string filter = s_catalogFilter != null ? s_catalogFilter.Trim() : string.Empty;
		for ( int i = 0; i < catalog.Count; i++ )
		{
			AbilityDefinition def = catalog[ i ];
			if ( def == null )
				continue;
			if ( !MatchesFilter( def, filter ) )
				continue;
			DrawAbilityRow( system, def );
		}
	}

	static bool MatchesFilter( AbilityDefinition def, string filter )
	{
		if ( string.IsNullOrEmpty( filter ) )
			return true;
		string name = def.ResolveDisplayName();
		if ( name != null && name.IndexOf( filter, System.StringComparison.OrdinalIgnoreCase ) >= 0 )
			return true;
		if ( !string.IsNullOrEmpty( def.id ) && def.id.IndexOf( filter, System.StringComparison.OrdinalIgnoreCase ) >= 0 )
			return true;
		return false;
	}

	static AbilitySystem ResolveSystem( PlayerController player )
	{
		if ( player != null && player.Abilities != null && player.Abilities.System != null )
			return player.Abilities.System;
		return AbilitySystem.Instance;
	}

	static void DrawSlot( AbilitySystem system, IReadOnlyList<AbilityDefinition> catalog, int slot )
	{
		string equippedId = system.GetEquippedAbilityId( slot );
		string equippedLabel = "(empty)";
		if ( !string.IsNullOrEmpty( equippedId ) && system.TryGetDefinition( equippedId, out AbilityDefinition equipped ) )
			equippedLabel = equipped.ResolveDisplayName();
		else if ( !string.IsNullOrEmpty( equippedId ) )
			equippedLabel = equippedId;

		GUILayout.BeginVertical( GUI.skin.box );
		GUILayout.BeginHorizontal();
		GUILayout.Label( $"Slot {slot + 1}: {equippedLabel}" );
		GUI.enabled = !string.IsNullOrEmpty( equippedId );
		if ( GUILayout.Button( "Clear", GUILayout.Width( 50f ) ) )
			system.UnequipSlot( slot );
		GUI.enabled = true;
		GUILayout.EndHorizontal();

		for ( int i = 0; i < catalog.Count; i++ )
		{
			AbilityDefinition def = catalog[ i ];
			if ( def == null || string.IsNullOrEmpty( def.id ) )
				continue;
			if ( def.isPassive )
				continue;
			if ( !system.IsUnlocked( def.id ) )
				continue;

			bool isEquippedHere = equippedId == def.id;
			GUI.enabled = !isEquippedHere;
			if ( GUILayout.Button( isEquippedHere ? $"Equipped: {def.ResolveDisplayName()}" : $"Equip {def.ResolveDisplayName()}" ) )
				system.EquipAbility( slot, def.id );
			GUI.enabled = true;
		}

		GUILayout.EndVertical();
	}

	static void DrawAbilityRow( AbilitySystem system, AbilityDefinition def )
	{
		string id = def.id;
		AbilityStatus status = system.GetStatus( id );
		float cooldown = system.GetCooldownRemaining( id );
		int slot = system.FindEquippedSlot( id );

		GUILayout.BeginVertical( GUI.skin.box );
		GUILayout.Label( $"{def.ResolveDisplayName()} ({id})" );
		GUILayout.Label( $"Status: {status}" + ( slot >= 0 ? $"  Slot {slot + 1}" : string.Empty ) + ( def.isPassive ? "  [passive]" : string.Empty ) );
		if ( cooldown > 0f )
			GUILayout.Label( $"Cooldown: {cooldown:0.00}s" );

		GUILayout.Label( $"Initial source: {DescribeInitialSource( id, def )}" );

		GUILayout.BeginHorizontal();
		AbilityInitialUnlockOverride current = GetOverride( id );
		string overrideLabel = OverrideButtonLabel( current );
		if ( GUILayout.Button( overrideLabel ) )
		{
			AbilityInitialUnlockOverride next = CycleOverride( current );
			SetOverride( id, next );
			system.ReloadFromProfile();
			DebugOverlay.SetLastAction( $"Ability '{id}' initial override → {next}" );
		}
		GUILayout.EndHorizontal();

		GUILayout.BeginHorizontal();
		GUI.enabled = !system.IsUnlocked( id );
		if ( GUILayout.Button( "Unlock" ) )
			system.UnlockAbility( id );
		GUI.enabled = system.IsUnlocked( id );
		if ( GUILayout.Button( "Lock" ) )
			system.LockAbility( id );
		GUI.enabled = true;
		GUILayout.EndHorizontal();

		GUILayout.EndVertical();
	}

	static string DescribeInitialSource( string id, AbilityDefinition def )
	{
		if ( HasProfileEntry( id ) )
			return "Profile";
		if ( DebugDefinition.TryGetAbilityInitialUnlockOverride( id, out _ ) )
			return "Override";
		return def.startsUnlocked ? "Catalog (unlocked)" : "Catalog (locked)";
	}

	static bool HasProfileEntry( string id )
	{
		if ( string.IsNullOrEmpty( id ) )
			return false;
		ProfileManager profileManager = ProfileManager.Instance;
		if ( profileManager == null || profileManager.ProfileSaveData == null )
			return false;
		ProfileSaveData save = profileManager.ProfileSaveData;
		save.EnsureProgressDictionaries();
		return save.unlockedAbilities != null
			&& save.unlockedAbilities.TryGetValue( id, out bool unlocked )
			&& unlocked;
	}

	static AbilityInitialUnlockOverride GetOverride( string id )
	{
		DebugDefinition def = DebugDefinition.GetRuntime();
		if ( def == null )
			return AbilityInitialUnlockOverride.Unset;
		return def.GetAbilityInitialUnlockOverride( id );
	}

	static void SetOverride( string id, AbilityInitialUnlockOverride state )
	{
		DebugDefinition def = DebugDefinition.GetRuntime();
		if ( def == null )
			return;
		def.SetAbilityInitialUnlockOverride( id, state );
	}

	static AbilityInitialUnlockOverride CycleOverride( AbilityInitialUnlockOverride current )
	{
		switch ( current )
		{
			case AbilityInitialUnlockOverride.Unset:
				return AbilityInitialUnlockOverride.ForceLocked;
			case AbilityInitialUnlockOverride.ForceLocked:
				return AbilityInitialUnlockOverride.ForceUnlocked;
			default:
				return AbilityInitialUnlockOverride.Unset;
		}
	}

	static string OverrideButtonLabel( AbilityInitialUnlockOverride state )
	{
		switch ( state )
		{
			case AbilityInitialUnlockOverride.ForceLocked:
				return "Initial: Force Locked";
			case AbilityInitialUnlockOverride.ForceUnlocked:
				return "Initial: Force Unlocked";
			default:
				return "Initial: Default";
		}
	}
}
