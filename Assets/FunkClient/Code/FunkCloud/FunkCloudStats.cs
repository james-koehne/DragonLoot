using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using Nakama;

using Newtonsoft.Json;

using UnityEngine;

public class FunkCloudStats : MonoBehaviour
{
	public struct LoadResponse
	{
		public enum LoadResponseType
		{
			Failed,
			Success,
			NotFound
		}

		public LoadResponseType response;
		public IGameStats result;
	}

	public struct SyncResponse
	{
		public bool success;
		public IGameStats result;
	}

	public void Init()
	{

	}

	/// <summary>Correlation string for logs when debugging multi-account / Steam user switches.</summary>
	private static string DebugStatsSessionLine( ISession session )
	{
		if ( session == null )
			return "session=null";

		return $"userId={session.UserId} username={session.Username}";
	}

	/// <summary>Union unlock flags and max upgrade levels from <paramref name="secondary"/> into <paramref name="primary"/> when both are <see cref="ProfileSaveData"/>.</summary>
	/// <returns><c>true</c> if <paramref name="primary"/> was modified.</returns>
	private static bool MergeProfileSaveUnlockProgressIfApplicable( IGameStats primary, IGameStats secondary )
	{
		if ( primary is ProfileSaveData p && secondary is ProfileSaveData s )
			return p.MergeBooleanUnlockProgressFrom( s );

		return false;
	}

	private static bool TryGetComparableStatsUpdateTime( IGameStats stats, out DateTimeOffset parsed )
	{
		parsed = default;

		if ( stats == null )
			return false;

		string s = stats.GetUpdateTime();

		if ( string.IsNullOrEmpty( s ) || s == "INVALID" )
			return false;

		return DateTimeOffset.TryParse( s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed );
	}

	public async Task SaveStatsAsync<T>( T statsData )
	{
		if ( !FunkCloud.FunkUser.IsLoggedIn && GameInstance.gameDef.StatsRequireLogin )
		{
			Debug.Log( $"FunkCloudStats::SaveStatsAsync skip — StatsRequireLogin && !IsLoggedIn (IsLoggedIn={FunkCloud.FunkUser.IsLoggedIn})" );
			return;
		}

		ISession gameSession = FunkCloud.FunkUser.GetGameSession();

		if ( gameSession == null )
		{
			Debug.Log( "FunkCloudStats::SaveStatsAsync skip — gameSession is null" );
			return;
		}

		string metaDataJson = JsonConvert.SerializeObject( statsData );

		WriteStorageObject[] writeObjects = new[] {
			new WriteStorageObject
			{
				Collection = "game_stats",
				Key = "stats",
				Value = metaDataJson
			}
		};

		//Debug.Log( $"FunkCloudStats::SaveStatsAsync writing collection=game_stats key=stats jsonChars={metaDataJson.Length} {DebugStatsSessionLine( gameSession )}" );
		await FunkCloud.FunkUser.GameClient.WriteStorageObjectsAsync( gameSession, writeObjects );
		//Debug.Log( $"FunkCloudStats::SaveStatsAsync write finished {DebugStatsSessionLine( gameSession )}" );
	}

	public void SaveStats<T>( T statsData )
	{
		_ = SaveStatsAsync( statsData );
	}

	public async Task<LoadResponse> LoadStats<T>() where T : IGameStats
	{
		if ( !FunkCloud.FunkUser.IsLoggedIn && GameInstance.gameDef.StatsRequireLogin )
		{
			Debug.Log( $"FunkCloudStats::LoadStats<{typeof( T ).Name}> failed — StatsRequireLogin && !IsLoggedIn" );
			return new LoadResponse() { response = LoadResponse.LoadResponseType.Failed, result = null };
		}

		ISession gameSession = FunkCloud.FunkUser.GetGameSession();

		if ( gameSession == null )
		{
			Debug.Log( $"FunkCloudStats::LoadStats<{typeof( T ).Name}> failed — gameSession is null" );
			return new LoadResponse() { response = LoadResponse.LoadResponseType.Failed, result = null };
		}

		//Debug.Log( $"FunkCloudStats::LoadStats<{typeof( T ).Name}> reading storage {DebugStatsSessionLine( gameSession )}" );

		try
		{
			IApiStorageObjects result = await FunkCloud.FunkUser.GameClient.ReadStorageObjectsAsync( gameSession, new[] {
				new StorageObjectId {
					Collection = "game_stats",
					Key = "stats",
					UserId = gameSession.UserId
				}
			} );

			if ( result.Objects != null && result.Objects.Count() > 0 )
			{
				IApiStorageObject storageObject = result.Objects.FirstOrDefault();

				if ( storageObject != null && !string.IsNullOrEmpty( storageObject.Value ) )
				{
					try
					{
						//Debug.Log( $"FunkCloudStats::LoadStats<{typeof( T ).Name}> success valueChars={storageObject.Value.Length} {DebugStatsSessionLine( gameSession )}" );
						return new LoadResponse() { response = LoadResponse.LoadResponseType.Success, result = JsonConvert.DeserializeObject<T>( storageObject.Value ) };
					}
					catch ( JsonException jsonEx )
					{
						Debug.Log( "FunkCloudStats::LoadStats() corrupt or invalid JSON: " + jsonEx.Message + " " + DebugStatsSessionLine( gameSession ) );
						return new LoadResponse() { response = LoadResponse.LoadResponseType.Failed, result = null };
					}
				}

				Debug.Log( $"FunkCloudStats::LoadStats<{typeof( T ).Name}> empty value after read {DebugStatsSessionLine( gameSession )}" );
			}
			else
			{
				Debug.Log( $"FunkCloudStats::LoadStats<{typeof( T ).Name}> NotFound (no objects) {DebugStatsSessionLine( gameSession )}" );
				return new LoadResponse() { response = LoadResponse.LoadResponseType.NotFound, result = null };
			}
		}
		catch ( ApiResponseException apiException )
		{
			Debug.Log( "FunkCloudStats::LoadStats() ApiResponseException = " + apiException.Message + " " + DebugStatsSessionLine( gameSession ) );
			return new LoadResponse() { response = LoadResponse.LoadResponseType.Failed, result = null };
		}

		Debug.Log( $"FunkCloudStats::LoadStats<{typeof( T ).Name}> failed (fallthrough) {DebugStatsSessionLine( gameSession )}" );
		return new LoadResponse() { response = LoadResponse.LoadResponseType.Failed, result = null };
	}

	public async Task<SyncResponse> SyncStats<T>( T localGameStats ) where T : IGameStats
	{
		if ( !FunkCloud.FunkUser.IsLoggedIn && GameInstance.gameDef.StatsRequireLogin )
		{
			Debug.Log( $"FunkCloudStats::SyncStats<{typeof( T ).Name}> abort — StatsRequireLogin && !IsLoggedIn" );
			return new SyncResponse() { success = false, result = null };
		}

		ISession gameSession = FunkCloud.FunkUser.GetGameSession();

		if ( gameSession == null )
		{
			Debug.Log( $"FunkCloudStats::SyncStats<{typeof( T ).Name}> abort — gameSession is null" );
			return new SyncResponse() { success = false, result = null };
		}

		//string localTimePreview = localGameStats?.GetUpdateTime() ?? "(null local stats)";
		//Debug.Log( $"FunkCloudStats::SyncStats<{typeof( T ).Name}> start localUpdateTime=\"{localTimePreview}\" {DebugStatsSessionLine( gameSession )}" );

		if ( localGameStats == null )
		{
			Debug.Log( $"FunkCloudStats::SyncStats<{typeof( T ).Name}> abort — localGameStats is null {DebugStatsSessionLine( gameSession )}" );
			return new SyncResponse() { success = false, result = null };
		}

		try
		{
			IApiStorageObjects result = await FunkCloud.FunkUser.GameClient.ReadStorageObjectsAsync( gameSession, new[] {
				new StorageObjectId {
					Collection = "game_stats",
					Key = "stats",
					UserId = gameSession.UserId
				}
			} );

			if ( result.Objects == null || result.Objects.Count() == 0 )
			{
				Debug.Log( $"FunkCloudStats::SyncStats<{typeof( T ).Name}> no cloud objects (treat as sync failure) {DebugStatsSessionLine( gameSession )}" );
				return new SyncResponse() { success = false, result = null };
			}

			IApiStorageObject storageObject = result.Objects.FirstOrDefault();

			if ( storageObject == null || string.IsNullOrEmpty( storageObject.Value ) )
			{
				Debug.Log( $"FunkCloudStats::SyncStats<{typeof( T ).Name}> empty storage object {DebugStatsSessionLine( gameSession )}" );
				return new SyncResponse() { success = false, result = null };
			}

			IGameStats cloudGameStats;
			try
			{
				cloudGameStats = JsonConvert.DeserializeObject<T>( storageObject.Value );
			}
			catch ( JsonException jsonEx )
			{
				Debug.Log( "FunkCloudStats::SyncStats() corrupt cloud JSON: " + jsonEx.Message );
				return new SyncResponse() { success = false, result = null };
			}

			if ( cloudGameStats == null )
			{
				Debug.Log( $"FunkCloudStats::SyncStats<{typeof( T ).Name}> deserialize returned null {DebugStatsSessionLine( gameSession )}" );
				return new SyncResponse() { success = false, result = null };
			}

			bool cloudTimeOk = TryGetComparableStatsUpdateTime( cloudGameStats, out DateTimeOffset cloudUpdateTime );
			bool localTimeOk = TryGetComparableStatsUpdateTime( localGameStats, out DateTimeOffset localUpdateTime );

			if ( cloudTimeOk && localTimeOk )
			{
				if ( cloudUpdateTime > localUpdateTime )
				{
					//Debug.Log( $"FunkCloudStats::SyncStats<{typeof( T ).Name}> chose CLOUD (newer). cloud={cloudUpdateTime:o} local={localUpdateTime:o} {DebugStatsSessionLine( gameSession )}" );
					if ( MergeProfileSaveUnlockProgressIfApplicable( cloudGameStats, localGameStats ) )
						await SaveStatsAsync( (T)cloudGameStats );

					return new SyncResponse() { success = true, result = cloudGameStats };
				}

				//Debug.Log( $"FunkCloudStats::SyncStats<{typeof( T ).Name}> chose LOCAL (newer or tie) — uploading. cloud={cloudUpdateTime:o} local={localUpdateTime:o} {DebugStatsSessionLine( gameSession )}" );
				MergeProfileSaveUnlockProgressIfApplicable( localGameStats, cloudGameStats );
				await SaveStatsAsync( localGameStats );
				return new SyncResponse() { success = true, result = localGameStats };
			}

			if ( cloudTimeOk && !localTimeOk )
			{
				//Debug.Log( $"FunkCloudStats::SyncStats<{typeof( T ).Name}> local timestamp unusable — keep CLOUD (server source of truth). cloud={cloudUpdateTime:o} {DebugStatsSessionLine( gameSession )}" );
				if ( MergeProfileSaveUnlockProgressIfApplicable( cloudGameStats, localGameStats ) )
					await SaveStatsAsync( (T)cloudGameStats );

				return new SyncResponse() { success = true, result = cloudGameStats };
			}

			if ( !cloudTimeOk && localTimeOk )
			{
				Debug.Log( $"FunkCloudStats::SyncStats<{typeof( T ).Name}> cloud timestamp unusable — upload LOCAL. local={localUpdateTime:o} {DebugStatsSessionLine( gameSession )}" );
				MergeProfileSaveUnlockProgressIfApplicable( localGameStats, cloudGameStats );
				await SaveStatsAsync( localGameStats );
				return new SyncResponse() { success = true, result = localGameStats };
			}

			Debug.Log( $"FunkCloudStats::SyncStats<{typeof( T ).Name}> both timestamps unusable — keep CLOUD without upload. {DebugStatsSessionLine( gameSession )}" );
			if ( MergeProfileSaveUnlockProgressIfApplicable( cloudGameStats, localGameStats ) )
				await SaveStatsAsync( (T)cloudGameStats );

			return new SyncResponse() { success = true, result = cloudGameStats };
		}
		catch ( ApiResponseException apiEx )
		{
			Debug.Log( "FunkCloudStats::SyncStats() ApiResponseException: " + apiEx.Message + " " + DebugStatsSessionLine( gameSession ) );
			return new SyncResponse() { success = false, result = null };
		}
	}
}
