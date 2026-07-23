using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

using Nakama;

using Newtonsoft.Json;

using UnityEngine;

public struct FunkCloudLeaderboardData<T>
{
	public string LeaderboardId;
	public long Score;
	public T Data;
}

public class FunkCloudLeaderboards : MonoBehaviour
{
	private readonly Dictionary<string, (IApiLeaderboardRecordList Data, DateTime CacheTime)> _leaderboardCache = new();
	private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes( 5 );

	public void Init()
	{

	}

	public async Task<IApiLeaderboardRecord> AddLeaderboardRecord<T>( FunkCloudLeaderboardData<T> record )
	{
		if ( !FunkCloud.FunkUser.IsLoggedIn && GameInstance.gameDef.StatsRequireLogin )
		{
			return null;
		}

		ISession gameSession = FunkCloud.FunkUser.GetGameSession();

		if ( gameSession == null )
		{
			return null;
		}

		string metaData = JsonConvert.SerializeObject( record.Data );

		return await FunkCloud.FunkUser.GameClient.WriteLeaderboardRecordAsync( gameSession, record.LeaderboardId, record.Score, 0, metaData );
	}

	public async Task<IApiLeaderboardRecordList> GetLeaderboard( string leaderboardId, string cursor = null )
	{
		string cacheKey = $"{leaderboardId}:{cursor ?? "start"}";

		// Check if data is in cache and still valid
		if ( _leaderboardCache.TryGetValue( cacheKey, out (IApiLeaderboardRecordList Data, DateTime CacheTime) cacheEntry ) )
		{
			if ( DateTime.UtcNow - cacheEntry.CacheTime < _cacheDuration )
			{
				return cacheEntry.Data;
			}
		}

		ISession gameSession = FunkCloud.FunkUser.GetGameSession();

		if ( gameSession == null )
		{
			return null;
		}

		IApiLeaderboardRecordList result = await FunkCloud.FunkUser.GameClient.ListLeaderboardRecordsAsync( gameSession, leaderboardId, null, null, 20, cursor );

		_leaderboardCache[ cacheKey ] = (result, DateTime.UtcNow);

		return result;
	}

	public async Task<IApiLeaderboardRecordList> GetLeaderboardAroundPlayer( string leaderboardId, int limit = 1, string cursor = null )
	{
		if ( !FunkCloud.FunkUser.IsLoggedIn && GameInstance.gameDef.StatsRequireLogin )
		{
			return null;
		}

		ISession gameSession = FunkCloud.FunkUser.GetGameSession();

		if ( gameSession == null )
		{
			return null;
		}

		string cacheKey = $"{leaderboardId}:{gameSession.UserId}:{limit}:{cursor ?? "start"}";

		// Check if data is cached
		if ( _leaderboardCache.TryGetValue( cacheKey, out (IApiLeaderboardRecordList Data, DateTime CacheTime) cacheEntry ) )
		{
			if ( DateTime.UtcNow - cacheEntry.CacheTime < _cacheDuration )
			{
				return cacheEntry.Data;
			}
		}

		IApiLeaderboardRecordList result = await FunkCloud.FunkUser.GameClient.ListLeaderboardRecordsAroundOwnerAsync( gameSession, leaderboardId, gameSession.UserId, null, limit, cursor );

		// Cache the result
		_leaderboardCache[ cacheKey ] = (result, DateTime.UtcNow);
		return result;
	}

	public void ClearCache( string leaderboardId = null )
	{
		if ( string.IsNullOrEmpty( leaderboardId ) )
		{
			_leaderboardCache.Clear();
		}
		else
		{
			List<string> keysToRemove = new List<string>();

			foreach ( var key in _leaderboardCache.Keys )
			{
				if ( key.StartsWith( leaderboardId ) )
				{
					keysToRemove.Add( key );
				}
			}

			foreach ( var key in keysToRemove )
			{
				_leaderboardCache.Remove( key );
			}
		}
	}
}
