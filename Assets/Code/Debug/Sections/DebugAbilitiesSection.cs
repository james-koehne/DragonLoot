using System.Collections.Generic;

using UnityEngine;

public class DebugAbilitiesSection : DebugOverlaySection
{
	static Vector2 s_listScroll;

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
		s_listScroll = GUILayout.BeginScrollView( s_listScroll, GUILayout.MaxHeight( 220f ) );
		for ( int i = 0; i < catalog.Count; i++ )
		{
			AbilityDefinition def = catalog[ i ];
			if ( def == null )
				continue;
			DrawAbilityRow( system, def );
		}
		GUILayout.EndScrollView();
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
		GUILayout.Label( $"Status: {status}" + ( slot >= 0 ? $"  Slot {slot + 1}" : string.Empty ) );
		if ( cooldown > 0f )
			GUILayout.Label( $"Cooldown: {cooldown:0.00}s" );

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
}
