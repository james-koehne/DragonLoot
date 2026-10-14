using System.Collections.Generic;

using UnityEngine;

public sealed class GameSavePlayerContributor : IGameSaveContributor
{
	public string Key => "player";

	public void Capture( GameSaveData data )
	{
		if ( data == null )
			return;

		GameSavePlayerData playerData = data.player;
		if ( playerData == null )
		{
			playerData = new GameSavePlayerData();
			data.player = playerData;
		}

		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;
		if ( player == null )
			return;

		playerData.position = GameSaveVec3.From( player.transform.position );
		playerData.yaw = player.transform.eulerAngles.y;

		FirstPersonCameraController look = null;
		if ( GameMode.Instance != null && GameMode.Instance.cameraController != null )
			look = GameMode.Instance.cameraController.FirstPerson;
		playerData.cameraPitch = look != null ? look.Pitch : 0f;

		PlayerCarry carry = player.Carry;
		if ( carry == null )
			return;

		playerData.selectedBucket = (int)carry.SelectedBucket;
		carry.CaptureDefinitionIds(
			playerData.coinIds,
			playerData.gemIds,
			playerData.artifactIds,
			playerData.resourceIds,
			playerData.generalIds,
			playerData.junkIds );

		if ( playerData.abilityCooldowns == null )
			playerData.abilityCooldowns = new List<GameSaveCooldownEntry>();
		AbilitySystem abilities = AbilitySystem.Instance;
		if ( abilities != null )
			abilities.CaptureCooldownsForSave( playerData.abilityCooldowns );
		else
			playerData.abilityCooldowns.Clear();
	}

	public void Apply( GameSaveData data )
	{
		if ( data == null || data.player == null )
			return;

		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;
		if ( player == null )
			return;

		GameSavePlayerData playerData = data.player;
		Vector3 position = playerData.position.ToVector3();
		Quaternion rotation = Quaternion.Euler( 0f, playerData.yaw, 0f );
		player.TeleportTo( position, rotation );

		FirstPersonCameraController look = null;
		if ( GameMode.Instance != null && GameMode.Instance.cameraController != null )
			look = GameMode.Instance.cameraController.FirstPerson;
		if ( look != null )
			look.SetPitch( playerData.cameraPitch );

		PlayerCarry carry = player.Carry;
		if ( carry == null )
			return;

		carry.RestoreFromDefinitionIds(
			playerData.selectedBucket,
			playerData.coinIds,
			playerData.gemIds,
			playerData.artifactIds,
			playerData.resourceIds,
			playerData.generalIds,
			playerData.junkIds );

		AbilitySystem abilities = AbilitySystem.Ensure( player );
		if ( abilities != null )
			abilities.ApplyCooldownsFromSave( playerData.abilityCooldowns );
	}
}
