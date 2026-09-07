using UnityEngine;

/// <summary>
/// Profile-backed chest open / skeleton-key claim progress.
/// </summary>
public static class ChestProgress
{
	/// <summary>
	/// Debug: skip key / lockpick requirements and allow opening locked chests on the ground.
	/// Default on while keys are still being tuned.
	/// </summary>
	public static bool DisableKeys { get; private set; } = true;

	public static void SetDisableKeys( bool disableKeys )
	{
		if ( DisableKeys == disableKeys )
			return;

		DisableKeys = disableKeys;
	}

	public static int ChestsOpened
	{
		get
		{
			ProfileSaveData save = GetSave();
			return save != null ? Mathf.Max( 0, save.chestsOpened ) : 0;
		}
	}

	public static bool SkeletonKeyClaimed
	{
		get
		{
			ProfileSaveData save = GetSave();
			return save != null && save.skeletonKeyClaimed;
		}
	}

	public static void RecordChestOpened()
	{
		ProfileSaveData save = GetSave();
		if ( save == null )
			return;

		save.chestsOpened = Mathf.Max( 0, save.chestsOpened ) + 1;
		Persist( save );
	}

	public static void MarkSkeletonKeyClaimed()
	{
		ProfileSaveData save = GetSave();
		if ( save == null )
			return;

		if ( save.skeletonKeyClaimed )
			return;

		save.skeletonKeyClaimed = true;
		Persist( save );
	}

	public static void SetChestsOpened( int count )
	{
		ProfileSaveData save = GetSave();
		if ( save == null )
			return;

		save.chestsOpened = Mathf.Max( 0, count );
		Persist( save );
	}

	public static void ResetSkeletonKeyClaimed()
	{
		ProfileSaveData save = GetSave();
		if ( save == null )
			return;

		save.skeletonKeyClaimed = false;
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
