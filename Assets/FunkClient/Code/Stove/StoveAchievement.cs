#if STOVE_BUILD

using System;
using System.Collections.Generic;

using Stove.PCSDK;

using UnityEngine;

/// <summary>
/// STOVE achievement unlock/query aligned with <see cref="SteamAchievement"/>.
/// Uses PC SDK v3 <see cref="GameSupport"/> per
/// <see href="https://developers-beta.onstove.com/en/docs/Store/PCSDK3/PCSDK_v3_gamesupport">STOVE game support</see>.
/// Unlock state lives only in a per-session cache; callers can <see cref="RefreshAchievementStateFromPlatform"/> or rely on implicit single-achievement pulls on cache miss.
/// STOVE pairs a stat id (same as Steam API id) with a separate achievement id, typically <c>{statId}_1</c>, for <c>GameSupport_Achievement</c> queries.
/// </summary>
public static class StoveAchievement
{
	/// <summary>STOVE achievement-service id suffix on top of the shared stat / Steam achievement id.</summary>
	private const string StoveAchievementIdSuffix = "_1";

	private static readonly object CacheLock = new object();

	private static List<string> _knownAchievementIds = new List<string>();

	/// <summary>Achievement id → unlocked this session.</summary>
	private static Dictionary<string, bool> _unlockedCache = new Dictionary<string, bool>();

	private static readonly HashSet<string> _singleAchievementPullPending = new HashSet<string>();

	/// <summary>Set by <see cref="StoveManager"/> after <c>GameSupport_Initialize</c> succeeds.</summary>
	public static bool GameSupportReady { get; private set; }

	public static void SetKnownAchievementIds( IReadOnlyList<string> achievementApiIdsSorted )
	{
		_knownAchievementIds = achievementApiIdsSorted == null
			? new List<string>()
			: new List<string>( achievementApiIdsSorted );
	}

	/// <summary>Invoked from <see cref="StoveManager"/> after a successful <c>GameSupport_Initialize</c>.</summary>
	public static void OnGameSupportInitialized()
	{
		GameSupportReady = true;
		RefreshAchievementStateFromPlatform();
	}

	/// <summary>Invoked from <see cref="StoveManager"/> when shutting down Game Support.</summary>
	public static void OnGameSupportShutdown()
	{
		GameSupportReady = false;
		lock ( CacheLock )
		{
			_unlockedCache.Clear();
			_singleAchievementPullPending.Clear();
		}
	}

	public static bool IsAvailable => StoveManager.Instance != null && StoveManager.Instance.Initialized && GameSupportReady;

	/// <summary>Re-queries all achievements from STOVE (<c>GameSupport_AllAchievement</c>). Pump <c>Base.Base_RunCallback</c> for results.</summary>
	public static void RefreshAchievementStateFromPlatform()
	{
		if ( !IsAvailable )
			return;

		GameSupport.GameSupport_AllAchievement( OnAllAchievementFinished );
	}

	public static void UnlockAchievement( string id )
	{
		if ( !IsAvailable || string.IsNullOrEmpty( id ) )
			return;

		if ( GetIsAchievementUnlocked( id ) )
			return;

		string statId = id;
		GameSupport.GameSupport_ModifyStat( statId, 1, ( cb, val ) => OnModifyStatUnlockComplete( statId, cb, val ) );
	}

	public static void ClearAchievement( string id )
	{
		if ( !IsAvailable || string.IsNullOrEmpty( id ) )
			return;

		string statId = id;
		GameSupport.GameSupport_ModifyStat( statId, 0, ( cb, val ) => OnModifyStatClearComplete( statId, cb, val ) );
	}

	public static void DebugClearAllAchievements()
	{
		if ( !IsAvailable )
			return;

		lock ( CacheLock )
		{
			_unlockedCache.Clear();
			_singleAchievementPullPending.Clear();
		}

		foreach ( string id in _knownAchievementIds )
		{
			string statId = id;
			GameSupport.GameSupport_ModifyStat( statId, 0, ( cb, val ) =>
			{
				if ( cb.result.resultCode != (uint)Base.BaseSDKResultCode.SUCCESS )
					Debug.LogWarning( $"StoveAchievement: ModifyStat debug clear '{statId}' failed resultCode={cb.result.resultCode}" );
			} );
		}

		Debug.Log( "StoveAchievement: Debug clear queued for all known stats (session cache cleared)." );

		RefreshAchievementStateFromPlatform();
	}

	/// <summary>
	/// Returns cached unlock state. On a miss, queues <c>GameSupport_Achievement</c> once (deduped) so the cache fills after the next callback pump; returns false until then.
	/// </summary>
	public static bool GetIsAchievementUnlocked( string id )
	{
		if ( !IsAvailable || string.IsNullOrEmpty( id ) )
			return false;

		lock ( CacheLock )
		{
			if ( _unlockedCache.TryGetValue( id, out bool cached ) )
				return cached;
		}

		RequestSingleAchievementIfNeeded( id );
		return false;
	}

	public static IReadOnlyList<string> GetUnlockedAchievementIds()
	{
		var list = new List<string>();
		if ( !IsAvailable )
			return list;

		foreach ( string id in _knownAchievementIds )
			RequestSingleAchievementIfNeeded( id );

		lock ( CacheLock )
		{
			foreach ( string id in _knownAchievementIds )
			{
				if ( _unlockedCache.TryGetValue( id, out bool u ) && u )
					list.Add( id );
			}
		}

		return list;
	}

	public static uint GetAchievementCount()
	{
		if ( !IsAvailable )
			return 0;
		return (uint)_knownAchievementIds.Count;
	}

	public static string GetAchievementName( uint index )
	{
		if ( !IsAvailable || index >= _knownAchievementIds.Count )
			return null;
		return _knownAchievementIds[ (int)index ];
	}

	private static string ToStoveAchievementQueryId( string gameAchievementId )
	{
		if ( string.IsNullOrEmpty( gameAchievementId ) )
			return gameAchievementId;
		return gameAchievementId + StoveAchievementIdSuffix;
	}

	private static string ToGameAchievementCacheKey( string stoveAchievementIdFromApi )
	{
		if ( string.IsNullOrEmpty( stoveAchievementIdFromApi ) )
			return stoveAchievementIdFromApi;
		if ( stoveAchievementIdFromApi.EndsWith( StoveAchievementIdSuffix, StringComparison.Ordinal ) )
			return stoveAchievementIdFromApi.Substring( 0, stoveAchievementIdFromApi.Length - StoveAchievementIdSuffix.Length );
		return stoveAchievementIdFromApi;
	}

	private static void RequestSingleAchievementIfNeeded( string achievementId )
	{
		lock ( CacheLock )
		{
			if ( _unlockedCache.ContainsKey( achievementId ) )
				return;

			if ( _singleAchievementPullPending.Contains( achievementId ) )
				return;

			_singleAchievementPullPending.Add( achievementId );
		}

		string queryId = ToStoveAchievementQueryId( achievementId );
		GameSupport.GameSupport_Achievement( queryId, ( cb, achievement ) => OnSingleAchievementFinished( achievementId, cb, achievement ) );
	}

	private static void OnSingleAchievementFinished( string requestedGameAchievementId, Base.CallbackResult cb, GameSupport.StovePCAchievement achievement )
	{
		lock ( CacheLock )
		{
			_singleAchievementPullPending.Remove( requestedGameAchievementId );

			if ( cb.result.resultCode != (uint)Base.BaseSDKResultCode.SUCCESS )
			{
				Debug.LogWarning( $"StoveAchievement: GameSupport_Achievement '{ToStoveAchievementQueryId( requestedGameAchievementId )}' (game id '{requestedGameAchievementId}') failed resultCode={cb.result.resultCode} errorMessage={cb.errorMessage}" );
				return;
			}

			string key = string.IsNullOrEmpty( achievement.achievementId )
				? requestedGameAchievementId
				: ToGameAchievementCacheKey( achievement.achievementId );
			_unlockedCache[ key ] = IsAchievementStructUnlocked( achievement );
		}
	}

	private static void OnAllAchievementFinished( Base.CallbackResult cb, GameSupport.StovePCAchievement[] achievements )
	{
		if ( cb.result.resultCode != (uint)Base.BaseSDKResultCode.SUCCESS || achievements == null )
		{
			Debug.LogWarning( $"StoveAchievement: GameSupport_AllAchievement failed resultCode={cb.result.resultCode} errorMessage={cb.errorMessage}" );
			return;
		}

		lock ( CacheLock )
		{
			foreach ( GameSupport.StovePCAchievement a in achievements )
			{
				if ( string.IsNullOrEmpty( a.achievementId ) )
					continue;

				string key = ToGameAchievementCacheKey( a.achievementId );
				_unlockedCache[ key ] = IsAchievementStructUnlocked( a );
			}
		}
	}

	private static bool IsAchievementStructUnlocked( GameSupport.StovePCAchievement a )
	{
		if ( !string.IsNullOrEmpty( a.status ) )
		{
			string s = a.status.ToUpperInvariant();
			if ( s.Contains( "ACHIEV" ) || s.Contains( "COMPLETE" ) || s.Contains( "CLEAR" ) )
				return true;
			if ( s.Contains( "INCOMPLETE" ) || ( s.Contains( "PROGRESS" ) && !s.Contains( "COMPLETE" ) ) )
				return false;
		}

		if ( a.condition.goalValue > 0 )
			return a.value >= a.condition.goalValue;

		return a.value >= 1;
	}

	private static void OnModifyStatUnlockComplete( string statId, Base.CallbackResult cb, GameSupport.StovePCModifyStatValue val )
	{
		if ( cb.result.resultCode != (uint)Base.BaseSDKResultCode.SUCCESS )
		{
			Debug.LogWarning( $"StoveAchievement: ModifyStat unlock '{statId}' failed resultCode={cb.result.resultCode} errorMessage={cb.errorMessage}" );
			return;
		}

		if ( !string.IsNullOrEmpty( val.errorMessage ) )
			Debug.LogWarning( $"StoveAchievement: ModifyStat '{statId}': {val.errorMessage}" );

		bool unlocked = val.updated || val.currentValue >= 1;

		lock ( CacheLock )
		{
			_unlockedCache[ statId ] = unlocked;
		}

		Debug.Log( $"StoveAchievement: ModifyStat unlock '{statId}' currentValue={val.currentValue} updated={val.updated}" );
	}

	private static void OnModifyStatClearComplete( string statId, Base.CallbackResult cb, GameSupport.StovePCModifyStatValue val )
	{
		if ( cb.result.resultCode != (uint)Base.BaseSDKResultCode.SUCCESS )
		{
			Debug.LogWarning( $"StoveAchievement: ModifyStat clear '{statId}' failed resultCode={cb.result.resultCode}" );
			return;
		}

		lock ( CacheLock )
		{
			_unlockedCache.Remove( statId );
		}

		RequestSingleAchievementIfNeeded( statId );
	}
}

#else

using System.Collections.Generic;

using UnityEngine;

/// <summary>Stub when <c>STOVE_BUILD</c> is not defined.</summary>
public static class StoveAchievement
{
	public static bool GameSupportReady { get; private set; }

	public static void SetKnownAchievementIds( IReadOnlyList<string> achievementApiIdsSorted ) { }

	public static void OnGameSupportInitialized() { }

	public static void OnGameSupportShutdown() { }

	public static bool IsAvailable => false;

	public static void RefreshAchievementStateFromPlatform() { }

	public static void UnlockAchievement( string id ) { }

	public static void ClearAchievement( string id ) { }

	public static void DebugClearAllAchievements() { }

	public static bool GetIsAchievementUnlocked( string id ) => false;

	public static IReadOnlyList<string> GetUnlockedAchievementIds() => new List<string>();

	public static uint GetAchievementCount() => 0;

	public static string GetAchievementName( uint index ) => null;
}

#endif
