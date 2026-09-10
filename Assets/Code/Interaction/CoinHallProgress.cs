using UnityEngine;

/// <summary>
/// Profile-backed Coin Hall expansion level persistence (highest unlocked level per hall id).
/// </summary>
public static class CoinHallProgress
{
	public static int GetLevel( string hallId )
	{
		if ( string.IsNullOrEmpty( hallId ) )
			return 0;

		ProfileSaveData save = GetSave();
		if ( save == null )
			return 0;

		save.EnsureCoinHallProgress();
		if ( !save.coinHallExpansionLevels.TryGetValue( hallId, out int level ) )
			return 0;

		return level < 0 ? 0 : level;
	}

	public static void SetLevel( string hallId, int level )
	{
		if ( string.IsNullOrEmpty( hallId ) )
			return;

		if ( level < 0 )
			level = 0;

		ProfileSaveData save = GetSave();
		if ( save == null )
			return;

		save.EnsureCoinHallProgress();
		if ( save.coinHallExpansionLevels.TryGetValue( hallId, out int existing ) && existing == level )
			return;

		save.coinHallExpansionLevels[ hallId ] = level;
		Persist( save );
	}

	/// <summary>Raises the saved level if <paramref name="level"/> is higher; never lowers.</summary>
	public static void MarkReached( string hallId, int level )
	{
		if ( string.IsNullOrEmpty( hallId ) )
			return;

		if ( level < 0 )
			level = 0;

		int current = GetLevel( hallId );
		if ( level <= current )
			return;

		SetLevel( hallId, level );
	}

	public static void Clear( string hallId )
	{
		if ( string.IsNullOrEmpty( hallId ) )
			return;

		ProfileSaveData save = GetSave();
		if ( save == null )
			return;

		save.EnsureCoinHallProgress();
		if ( !save.coinHallExpansionLevels.Remove( hallId ) )
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
