using UnityEngine;

/// <summary>
/// Profile-backed door unlock (event-based) and open/closed pose persistence.
/// </summary>
public static class DoorProgress
{
	/// <summary>Session debug override. Default off. Does not persist.</summary>
	public static bool UnlockAllDoors { get; private set; }

	public static void SetUnlockAllDoors( bool unlockAll )
	{
		if ( UnlockAllDoors == unlockAll )
			return;

		UnlockAllDoors = unlockAll;
		DoorInteractable.RefreshAllUnlockState();
	}

	public static bool IsEventUnlocked( string doorId )
	{
		if ( string.IsNullOrEmpty( doorId ) )
			return false;

		ProfileSaveData save = GetSave();
		if ( save == null || save.eventUnlockedDoorIds == null )
			return false;

		return save.eventUnlockedDoorIds.Contains( doorId );
	}

	public static bool IsOpen( string doorId )
	{
		if ( string.IsNullOrEmpty( doorId ) )
			return false;

		ProfileSaveData save = GetSave();
		if ( save == null || save.openDoorIds == null )
			return false;

		return save.openDoorIds.Contains( doorId );
	}

	public static void MarkEventUnlocked( string doorId )
	{
		if ( string.IsNullOrEmpty( doorId ) )
			return;

		ProfileSaveData save = GetSave();
		if ( save == null )
			return;

		save.EnsureDoorProgress();
		if ( save.eventUnlockedDoorIds.Contains( doorId ) )
			return;

		save.eventUnlockedDoorIds.Add( doorId );
		Persist( save );
	}

	public static void MarkOpen( string doorId )
	{
		if ( string.IsNullOrEmpty( doorId ) )
			return;

		ProfileSaveData save = GetSave();
		if ( save == null )
			return;

		save.EnsureDoorProgress();
		if ( save.openDoorIds.Contains( doorId ) )
			return;

		save.openDoorIds.Add( doorId );
		Persist( save );
	}

	public static void MarkClosed( string doorId )
	{
		if ( string.IsNullOrEmpty( doorId ) )
			return;

		ProfileSaveData save = GetSave();
		if ( save == null )
			return;

		save.EnsureDoorProgress();
		if ( !save.openDoorIds.Remove( doorId ) )
			return;

		Persist( save );
	}

	static ProfileSaveData GetSave()
	{
		ProfileManager manager = ProfileManager.Instance;
		if ( manager == null )
			return null;
		return manager.ProfileSaveData;
	}

	static void Persist( ProfileSaveData save )
	{
		if ( save == null )
			return;

		ProfileManager manager = ProfileManager.Instance;
		if ( manager == null )
			return;

		manager.SaveCurrentStatsToProfile();
	}
}
