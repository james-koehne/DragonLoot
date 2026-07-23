using System;
using System.Collections.Generic;
using System.Globalization;

using Nakama;

using UnityEngine;
using UnityEngine.Events;

[Serializable]
public class ProfileSaveData : IGameStats
{
	public string UpdateTime;

	/// <summary>Nakama game user id that last wrote this blob when online; used to avoid applying another account's local save on the same machine.</summary>
	public string CloudProfileOwnerUserId;

	public ProfileSaveData()
	{
		UpdateTime = "INVALID";
	}

	public string GetUpdateTime()
	{
		return UpdateTime;
	}

	private void EnsureProgressDictionaries()
	{

	}

	public bool MergeBooleanUnlockProgressFrom( ProfileSaveData other )
	{
		return false;
	}
}

public class ProfileManager : MonoBehaviour
{
	public static ProfileManager Instance { get; private set; }

	/// <summary>Legacy single-slot profile (offline / shared device). Online accounts use <see cref="GetCloudScopedProfileStorageKey"/>.</summary>
	public const string LegacyProfileSaveDataKey = "ProfileSaveData";

	private const string LastAuthenticatedCloudUserIdKey = "ProfileSaveDataLastAuthenticatedCloudUserId";

	public UnityEvent OnGameClose = new UnityEvent();

	private ProfileSaveData profileSaveData;
	public ProfileSaveData ProfileSaveData => profileSaveData;

	private bool profileStatsFoundInvalidValue = false;

	/// <summary>Per–Nakama-user local cache when authenticated; otherwise the legacy shared key.</summary>
	public static string GetCloudScopedProfileStorageKey( string nakamaGameUserId )
	{
		return $"{LegacyProfileSaveDataKey}_{nakamaGameUserId}";
	}

	private bool TryGetActiveCloudScopedProfileStorageKey( out string key )
	{
		key = null;
		if ( !GameInstance.gameDef.FunkBackendEnabled || !FunkCloud.FunkUser.IsAuthenticated )
			return false;

		ISession session = FunkCloud.FunkUser.GetGameSession();
		if ( session == null || string.IsNullOrEmpty( session.UserId ) )
			return false;

		key = GetCloudScopedProfileStorageKey( session.UserId );
		return true;
	}

	private string GetProfileLoadStorageKey()
	{
		if ( TryGetActiveCloudScopedProfileStorageKey( out string scoped ) )
			return scoped;

		return LegacyProfileSaveDataKey;
	}

	private void StampCloudProfileOwnerIfOnline( ProfileSaveData data )
	{
		if ( data == null )
			return;

		if ( GameInstance.gameDef.FunkBackendEnabled && FunkCloud.FunkUser.IsAuthenticated && FunkCloud.FunkUser.GetGameSession() != null )
			data.CloudProfileOwnerUserId = FunkCloud.FunkUser.GetGameUserID();
	}

	private void RefreshLastAuthenticatedCloudUserPref()
	{
		if ( GameInstance.gameDef.FunkBackendEnabled && FunkCloud.FunkUser.IsAuthenticated && FunkCloud.FunkUser.GetGameSession() != null )
			ProfileData.SaveSetting( LastAuthenticatedCloudUserIdKey, FunkCloud.FunkUser.GetGameUserID() );
	}

	/// <summary>Whether the legacy shared save may be used to seed this Nakama user's first cloud profile.</summary>
	private bool ShouldAdoptLegacyProfileForCurrentCloudUser( ProfileSaveData legacy )
	{
		if ( legacy == null )
			return false;

		string current = FunkCloud.FunkUser.GetGameUserID();
		if ( string.IsNullOrEmpty( current ) )
			return false;

		if ( !string.IsNullOrEmpty( legacy.CloudProfileOwnerUserId ) )
			return legacy.CloudProfileOwnerUserId == current;

		string lastAuth = ProfileData.GetSetting<string>( LastAuthenticatedCloudUserIdKey );
		if ( !string.IsNullOrEmpty( lastAuth ) )
			return lastAuth == current;

		// No owner stamp and no prior authenticated session recorded — first-time migration from pre-scoped local saves.
		return true;
	}

	private void SaveProfileToDisk( ProfileSaveData data )
	{
		if ( data == null )
			return;

		StampCloudProfileOwnerIfOnline( data );

		if ( TryGetActiveCloudScopedProfileStorageKey( out string scopedKey ) )
		{
			ProfileData.SaveSetting( scopedKey, data, true );
			// Keep legacy slot in sync so offline mode still sees the last account that saved on this install.
			ProfileData.SaveSetting( LegacyProfileSaveDataKey, data, true );
		}
		else
			ProfileData.SaveSetting( LegacyProfileSaveDataKey, data, true );
	}

	[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.SubsystemRegistration )]
	private static void Init()
	{
		Instance = null;
	}

	private void Awake()
	{
		if ( Instance != null && Instance != this )
		{
			Destroy( this );
			return;
		}

		Instance = this;
	}

	private void OnEnable()
	{
		FunkCloudUser.OnUserAuthenticated.AddListener( ( s ) => OnUserAuthenticated( s ) );

		OnInit();
	}

	private void OnDisable()
	{
		FunkCloudUser.OnUserAuthenticated.RemoveListener( ( s ) => OnUserAuthenticated( s ) );
	}

	private void OnUserAuthenticated( ISession session )
	{
		OnInit();
	}

	private async void OnInit()
	{
		if ( !GameInstance.gameDef.FunkBackendEnabled || !FunkCloud.FunkUser.IsAuthenticated || !FunkCloud.Connected )
		{
			// LOAD FROM LOCAL DATA (shared legacy slot — offline has no Nakama user id)
			ProfileSaveData profileData = ProfileData.GetSetting<ProfileSaveData>( LegacyProfileSaveDataKey, true );

			if ( profileData == null )
			{
				profileData = CreateNewBaseProfileSaveData();

				ProfileData.SaveSetting( LegacyProfileSaveDataKey, profileData, true );
			}

			profileSaveData = profileData;

			CheckForInvalidValues();

			if ( GameMode.Instance.CoreDefinition.DebugDefinition.debugLogging )
			{
				Debug.Log( $"BBMProfileManager: Loaded local profile" );
			}
		}
		else
		{
			FunkCloudStats.LoadResponse loadResponse = await FunkCloud.FunkStats.LoadStats<ProfileSaveData>();

			if ( GameMode.Instance.CoreDefinition.DebugDefinition.debugLogging )
			{
				Debug.Log( $"BBMProfileManager: Load cloud stats = {loadResponse.response.ToString()}" );
			}

			if ( loadResponse.response == FunkCloudStats.LoadResponse.LoadResponseType.Success )
			{
				profileSaveData = loadResponse.result as ProfileSaveData;

				// Server is source of truth; only merge in local progress from this Nakama user's scoped cache (not the shared legacy slot).
				if ( TryGetActiveCloudScopedProfileStorageKey( out string scopedKey ) )
				{
					ProfileSaveData localScoped = ProfileData.GetSetting<ProfileSaveData>( scopedKey, true );

					if ( localScoped != null )
					{
						FunkCloudStats.SyncResponse result = await FunkCloud.FunkStats.SyncStats<ProfileSaveData>( localScoped );

						if ( result.success && result.result != null )
						{
							profileSaveData = result.result as ProfileSaveData;

							if ( GameMode.Instance.CoreDefinition.DebugDefinition.debugLogging )
							{
								Debug.Log( $"BBMProfileManager: Sync with cloud success" );
							}
						}
					}
				}

				CheckForInvalidValues();
				SaveProfileToDisk( profileSaveData );
				RefreshLastAuthenticatedCloudUserPref();
			}
			else if ( loadResponse.response == FunkCloudStats.LoadResponse.LoadResponseType.NotFound )
			{
				ProfileSaveData scopedLocal = null;
				if ( TryGetActiveCloudScopedProfileStorageKey( out string scopedKey ) )
					scopedLocal = ProfileData.GetSetting<ProfileSaveData>( scopedKey, true );

				ProfileSaveData legacyLocal = ProfileData.GetSetting<ProfileSaveData>( LegacyProfileSaveDataKey, true );

				if ( scopedLocal != null )
				{
					profileSaveData = scopedLocal;
				}
				else if ( legacyLocal != null && ShouldAdoptLegacyProfileForCurrentCloudUser( legacyLocal ) )
				{
					profileSaveData = legacyLocal;

					if ( GameMode.Instance.CoreDefinition.DebugDefinition.debugLogging )
					{
						Debug.Log( "BBMProfileManager: No cloud save — seeding from local profile tied to this account (or unclaimed legacy on first install)." );
					}
				}
				else
				{
					profileSaveData = CreateNewBaseProfileSaveData();
				}

				CheckForInvalidValues();
				StampCloudProfileOwnerIfOnline( profileSaveData );

				await FunkCloud.FunkStats.SaveStatsAsync( profileSaveData );

				SaveProfileToDisk( profileSaveData );

				CheckForInvalidValues();
				RefreshLastAuthenticatedCloudUserPref();
			}
			else if ( loadResponse.response == FunkCloudStats.LoadResponse.LoadResponseType.Failed )
			{
				// Prefer this user's scoped cache when authenticated; fall back to legacy.
				ProfileSaveData profileData = ProfileData.GetSetting<ProfileSaveData>( GetProfileLoadStorageKey(), true );

				if ( profileData == null )
					profileData = ProfileData.GetSetting<ProfileSaveData>( LegacyProfileSaveDataKey, true );

				if ( profileData == null )
				{
					profileData = CreateNewBaseProfileSaveData();

					SaveProfileToDisk( profileData );
				}

				profileSaveData = profileData;

				CheckForInvalidValues();

				if ( GameMode.Instance.CoreDefinition.DebugDefinition.debugLogging )
				{
					Debug.Log( $"BBMProfileManager: Not connected or not valid session or not logged in (if required)" );
				}

				RefreshLastAuthenticatedCloudUserPref();
			}
		}

		onFinishedInit();
	}

	protected virtual void onFinishedInit()
	{

	}

	private void OnApplicationQuit()
	{
		OnGameClose.Invoke();

		SaveCurrentStatsToProfile();
	}

	public async void SaveCurrentStatsToProfile( bool overwrite = false )
	{
		// save current stats to profile
		profileSaveData.UpdateTime = FunkCloudUtils.GetISONowTime();

		if ( !GameInstance.gameDef.FunkBackendEnabled || !FunkCloud.FunkUser.IsAuthenticated || !FunkCloud.Connected )
		{
			SaveProfileToDisk( profileSaveData );
		}
		else
		{
			SaveProfileToDisk( profileSaveData );

			if ( overwrite )
			{
				await FunkCloud.FunkStats.SaveStatsAsync<ProfileSaveData>( profileSaveData );

				SaveProfileToDisk( profileSaveData );
			}
			else
			{
				FunkCloudStats.SyncResponse result = await FunkCloud.FunkStats.SyncStats<ProfileSaveData>( profileSaveData );

				if ( result.success && result.result != null )
				{
					profileSaveData = result.result as ProfileSaveData;
					SaveProfileToDisk( profileSaveData );
				}
			}
		}
	}

	private void CheckForInvalidValues()
	{
		if ( profileSaveData.UpdateTime == "INVALID" )
		{
			profileSaveData.UpdateTime = FunkCloudUtils.GetISOEpochTime();
			profileStatsFoundInvalidValue = true;
		}

		// save if found invalid
		if ( profileStatsFoundInvalidValue == true )
		{
			FunkCloud.FunkStats.SaveStats<ProfileSaveData>( profileSaveData );

			SaveProfileToDisk( profileSaveData );

			profileStatsFoundInvalidValue = false;
		}
	}

	private ProfileSaveData CreateNewBaseProfileSaveData()
	{
		ProfileSaveData newProfileSaveData = new ProfileSaveData();

		newProfileSaveData.UpdateTime = FunkCloudUtils.GetISONowTime();

		StampCloudProfileOwnerIfOnline( newProfileSaveData );

		return newProfileSaveData;
	}

	public void ClearProfile()
	{
		profileSaveData = CreateNewBaseProfileSaveData();

		onFinishedInit();

		CheckForInvalidValues();

		SaveProfileToDisk( profileSaveData );

		SaveCurrentStatsToProfile( true );
	}
}
