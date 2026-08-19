using UnityEngine;

public class DebugDoorsSection : DebugOverlaySection
{
	public string Title => "Doors";

	public void Draw()
	{
		bool unlockAll = GUILayout.Toggle( DoorProgress.UnlockAllDoors, "Unlock All Doors" );
		if ( unlockAll != DoorProgress.UnlockAllDoors )
			DoorProgress.SetUnlockAllDoors( unlockAll );

		DoorInteractable[] doors = Object.FindObjectsByType<DoorInteractable>( FindObjectsInactive.Exclude, FindObjectsSortMode.None );
		int count = doors != null ? doors.Length : 0;
		GUILayout.Label( $"Scene doors: {count}" );

		if ( doors == null )
			return;

		for ( int i = 0; i < doors.Length; i++ )
		{
			DoorInteractable door = doors[ i ];
			if ( door == null )
				continue;

			string id = string.IsNullOrEmpty( door.DoorId ) ? "(no id)" : door.DoorId;
			GUILayout.Label( $"{id}: {door.State}" );
		}
	}
}
