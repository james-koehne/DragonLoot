using UnityEngine;

public class DebugDoorsSection : DebugOverlaySection
{
	static DoorInteractable[] s_doors;
	static float s_doorsCacheTime = -999f;

	public string Title => "Doors";

	public void Draw()
	{
		bool unlockAll = GUILayout.Toggle( DoorProgress.UnlockAllDoors, "Unlock All Doors" );
		if ( unlockAll != DoorProgress.UnlockAllDoors )
		{
			DoorProgress.SetUnlockAllDoors( unlockAll );
			DebugOverlay.SetLastAction( unlockAll ? "Unlock all doors ON" : "Unlock all doors OFF" );
		}

		DoorInteractable[] doors = DebugOverlayGui.CachedFind( ref s_doors, ref s_doorsCacheTime );
		int count = doors != null ? doors.Length : 0;
		GUILayout.Label( $"Scene doors: {count}" );

		if ( doors == null )
			return;

		PlayerController player = DebugOverlay.GetPlayer();

		for ( int i = 0; i < doors.Length; i++ )
		{
			DoorInteractable door = doors[ i ];
			if ( door == null )
				continue;

			string id = string.IsNullOrEmpty( door.DoorId ) ? "(no id)" : door.DoorId;
			GUILayout.BeginHorizontal();
			GUILayout.Label( $"{id}: {door.State}", GUILayout.ExpandWidth( true ) );
			if ( GUILayout.Button( "Go", GUILayout.Width( 36f ) ) )
				TeleportNear( player, door );
			if ( GUILayout.Button( "Unlock", GUILayout.Width( 54f ) ) )
			{
				door.UnlockFromEvent();
				DebugOverlay.SetLastAction( "Unlocked door " + id );
			}
			GUILayout.EndHorizontal();
		}
	}

	static void TeleportNear( PlayerController player, DoorInteractable door )
	{
		if ( player == null || door == null )
		{
			DebugOverlay.SetLastAction( "Teleport failed (no player/door)" );
			return;
		}

		Vector3 pos = door.transform.position + door.transform.forward * 1.5f + Vector3.up * 0.1f;
		Quaternion rot = Quaternion.LookRotation( ( door.transform.position - pos ).normalized, Vector3.up );
		player.TeleportTo( pos, rot );
		DebugOverlay.SetLastAction( "Teleported near door " + ( string.IsNullOrEmpty( door.DoorId ) ? door.name : door.DoorId ) );
	}
}
