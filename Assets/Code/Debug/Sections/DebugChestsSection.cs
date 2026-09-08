using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class DebugChestsSection : DebugOverlaySection
{
	static string s_lastStatus = "";

	public string Title => "Chests";

	public void Draw()
	{
		bool disableKeys = GUILayout.Toggle( ChestProgress.DisableKeys, "Disable Keys (always open)" );
		if ( disableKeys != ChestProgress.DisableKeys )
			ChestProgress.SetDisableKeys( disableKeys );

		GUILayout.Label( $"Chests opened: {ChestProgress.ChestsOpened}" );
		GUILayout.Label( $"Skeleton claimed: {ChestProgress.SkeletonKeyClaimed}" );

		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "+1 Opened" ) )
		{
			ChestProgress.RecordChestOpened();
			s_lastStatus = $"Chests opened = {ChestProgress.ChestsOpened}";
		}
		if ( GUILayout.Button( "Set 0" ) )
		{
			ChestProgress.SetChestsOpened( 0 );
			s_lastStatus = "Chests opened = 0";
		}
		if ( GUILayout.Button( "Set 3" ) )
		{
			ChestProgress.SetChestsOpened( 3 );
			s_lastStatus = "Chests opened = 3";
		}
		GUILayout.EndHorizontal();

		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Reset Skeleton Claim" ) )
		{
			ChestProgress.ResetSkeletonKeyClaimed();
			s_lastStatus = "Skeleton claim cleared";
		}
		GUILayout.EndHorizontal();

		PlayerController player = DebugOverlay.GetPlayer();
		AbilitySystem system = ResolveSystem( player );

		GUILayout.Space( 4f );
		GUILayout.Label( "Lockpick ability" );
		GUILayout.BeginHorizontal();
		GUI.enabled = system != null && !system.IsUnlocked( ChestInteractable.LockpickAbilityId );
		if ( GUILayout.Button( "Unlock Lockpick" ) )
		{
			if ( system != null && system.UnlockAbility( ChestInteractable.LockpickAbilityId ) )
				s_lastStatus = "Lockpick unlocked";
			else
				s_lastStatus = "Lockpick unlock failed (missing catalog entry?)";
		}
		GUI.enabled = system != null && system.IsUnlocked( ChestInteractable.LockpickAbilityId );
		if ( GUILayout.Button( "Equip Slot 1" ) )
		{
			if ( system != null && system.EquipAbility( 0, ChestInteractable.LockpickAbilityId ) )
				s_lastStatus = "Lockpick equipped to slot 1";
			else
				s_lastStatus = "Equip failed";
		}
		GUI.enabled = true;
		GUILayout.EndHorizontal();

		GUILayout.Space( 4f );
		GUILayout.Label( "Focused chest" );
		ChestInteractable focused = ResolveFocusedChest( player );
		if ( focused == null )
		{
			GUILayout.Label( "(none)" );
		}
		else
		{
			GUILayout.Label( $"State: {focused.State}" );
			if ( focused.State == ChestState.Lockpicking )
				GUILayout.Label( $"Remaining: {focused.LockpickRemaining:0.00}s" );

			GUILayout.BeginHorizontal();
			if ( GUILayout.Button( "Unlock" ) )
			{
				focused.TryForceUnlock();
				s_lastStatus = "Forced unlock";
			}
			if ( GUILayout.Button( "Open" ) )
			{
				focused.TryForceOpen();
				s_lastStatus = "Forced open";
			}
			if ( GUILayout.Button( "Lockpick" ) )
			{
				bool ok = focused.TryBeginLockpick( player );
				s_lastStatus = ok ? "Lockpick started" : "Lockpick failed";
			}
			GUILayout.EndHorizontal();
		}

		GUILayout.Space( 4f );
		if ( GUILayout.Button( "Spawn Locked Iron Chest" ) )
			SpawnChestAsync( player, "IronChest" );
		if ( GUILayout.Button( "Spawn Barrel" ) )
			SpawnChestAsync( player, "Barrel_01" );
		if ( GUILayout.Button( "Spawn Crate" ) )
			SpawnChestAsync( player, "Crate_01" );
		if ( GUILayout.Button( "Spawn Skeleton Display Case" ) )
			SpawnDisplayCaseAsync( player );

		GUILayout.Space( 4f );
		GUILayout.Label( "Grant keys via Carry → filter Key" );

		if ( !string.IsNullOrEmpty( s_lastStatus ) )
			GUILayout.Label( s_lastStatus );
	}

	static AbilitySystem ResolveSystem( PlayerController player )
	{
		if ( player != null && player.Abilities != null && player.Abilities.System != null )
			return player.Abilities.System;
		return AbilitySystem.Instance;
	}

	static ChestInteractable ResolveFocusedChest( PlayerController player )
	{
		if ( player == null || player.Interaction == null )
			return null;
		return ChestInteractable.ResolveFromInteractable( player.Interaction.Current );
	}

	async void SpawnDisplayCaseAsync( PlayerController player )
	{
		Vector3 pos = player != null && player.transform != null
			? player.transform.position + player.transform.forward * 2f + Vector3.up * 0.45f
			: Vector3.up;

		AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(
			"Treasure/SkeletonKeyDisplayCase",
			pos,
			Quaternion.identity );
		await handle.Task;

		if ( handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null )
		{
			SpawnFallbackDisplayCase( pos );
			return;
		}

		s_lastStatus = "Spawned skeleton key display case";
	}

	async void SpawnChestAsync( PlayerController player, string chestId )
	{
		TreasureDefinition def = FindTreasureDefinition( chestId );
		if ( def == null )
		{
			s_lastStatus = $"Missing {chestId} definition (open Unity to run installer)";
			return;
		}

		Vector3 pos = player != null && player.transform != null
			? player.transform.position + player.transform.forward * 2f + Vector3.up * 0.2f
			: Vector3.up;

		TreasureItem item = await TreasureItemFactory.SpawnAsync( def, pos, Quaternion.identity, null );
		if ( item == null )
		{
			s_lastStatus = "Chest spawn failed";
			return;
		}

		item.EnterSettledPhysics( pos, Quaternion.identity );

		s_lastStatus = $"Spawned {def.displayName}";
	}

	static void SpawnFallbackDisplayCase( Vector3 pos )
	{
		TreasureDefinition skeleton = FindTreasureDefinition( "SkeletonKey" );
		if ( skeleton == null )
			skeleton = FindSkeletonKeyDefinition();

		GameObject root = GameObject.CreatePrimitive( PrimitiveType.Cube );
		root.name = "SkeletonKeyDisplayCase";
		root.transform.position = pos;
		root.transform.localScale = new Vector3( 0.6f, 0.9f, 0.6f );

		SkeletonKeyDisplayCase interactable = root.AddComponent<SkeletonKeyDisplayCase>();
		if ( skeleton != null )
			interactable.Configure( skeleton, 3 );

		s_lastStatus = skeleton != null
			? "Spawned fallback display case"
			: "Spawned fallback display case (no SkeletonKey def yet)";
	}

	static TreasureDefinition FindTreasureDefinition( string id )
	{
		if ( string.IsNullOrEmpty( id ) )
			return null;

		TreasureDefinition[] all = Resources.FindObjectsOfTypeAll<TreasureDefinition>();
		if ( all == null )
			return null;

		for ( int i = 0; i < all.Length; i++ )
		{
			TreasureDefinition def = all[ i ];
			if ( def == null )
				continue;
			if ( def.id == id )
				return def;
		}

		return null;
	}

	static TreasureDefinition FindSkeletonKeyDefinition()
	{
		TreasureDefinition[] all = Resources.FindObjectsOfTypeAll<TreasureDefinition>();
		if ( all == null )
			return null;

		for ( int i = 0; i < all.Length; i++ )
		{
			TreasureDefinition def = all[ i ];
			if ( def == null )
				continue;
			if ( def.isSkeletonKey || def.id == "SkeletonKey" )
				return def;
		}

		return null;
	}
}
