#if STEAM_BUILD

using System.Collections.Generic;

using UnityEngine;

using Steamworks;

/// <summary>
/// Steam achievement unlock/clear and query. Requires SteamManager to be initialized.
/// </summary>
public static class SteamAchievement
{
	/// <summary>Returns true if Steam is available and achievements can be used.</summary>
	public static bool IsAvailable => SteamManager.Instance != null && SteamManager.Instance.Initialized;

	/// <summary>Unlocks an achievement by ID and stores stats.</summary>
	public static void UnlockAchievement( string id )
	{
		if ( !IsAvailable || string.IsNullOrEmpty( id ) )
			return;
		SteamUserStats.SetAchievement( id );
		SteamUserStats.StoreStats();
		Debug.Log( "SteamAchievement: Unlocked achievement: " + id );
	}

	/// <summary>Clears a single achievement. For debug use only.</summary>
	public static void ClearAchievement( string id )
	{
		if ( !IsAvailable || string.IsNullOrEmpty( id ) )
			return;
		SteamUserStats.ClearAchievement( id );
	}

	/// <summary>Resets all stats and achievements. For debug use only.</summary>
	public static void DebugClearAllAchievements()
	{
		if ( !IsAvailable )
			return;
		SteamUserStats.ResetAllStats( true );
		SteamUserStats.StoreStats();
		Debug.Log( "SteamAchievement: Cleared all achievements." );
	}

	/// <summary>Returns whether the given achievement is unlocked for the current user.</summary>
	public static bool GetIsAchievementUnlocked( string id )
	{
		if ( !IsAvailable || string.IsNullOrEmpty( id ) )
			return false;
		return SteamUserStats.GetAchievement( id, out bool achieved ) && achieved;
	}

	/// <summary>Returns all achievement IDs that are unlocked for the current user.</summary>
	public static IReadOnlyList<string> GetUnlockedAchievementIds()
	{
		var list = new List<string>();
		if ( !IsAvailable )
			return list;
		uint num = SteamUserStats.GetNumAchievements();
		for ( uint i = 0; i < num; i++ )
		{
			string name = SteamUserStats.GetAchievementName( i );
			if ( !string.IsNullOrEmpty( name ) && SteamUserStats.GetAchievement( name, out bool achieved ) && achieved )
				list.Add( name );
		}
		return list;
	}

	/// <summary>Returns the total number of achievements defined for the game.</summary>
	public static uint GetAchievementCount()
	{
		if ( !IsAvailable )
			return 0;
		return SteamUserStats.GetNumAchievements();
	}

	/// <summary>Returns the achievement ID at the given index (0 to GetAchievementCount()-1).</summary>
	public static string GetAchievementName( uint index )
	{
		if ( !IsAvailable )
			return null;
		return SteamUserStats.GetAchievementName( index );
	}
}

#else

using System.Collections.Generic;

using UnityEngine;

/// <summary>Stub when <c>STEAM_BUILD</c> is not defined.</summary>
public static class SteamAchievement
{
	public static bool IsAvailable => false;

	public static void UnlockAchievement( string id ) { }

	public static void ClearAchievement( string id ) { }

	public static void DebugClearAllAchievements() { }

	public static bool GetIsAchievementUnlocked( string id ) => false;

	public static IReadOnlyList<string> GetUnlockedAchievementIds() => new List<string>();

	public static uint GetAchievementCount() => 0;

	public static string GetAchievementName( uint index ) => null;
}

#endif
