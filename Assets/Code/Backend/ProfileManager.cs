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

	public const int AbilitySlotCount = 4;

	/// <summary>Unlocked ability ids (true = unlocked). Missing / false = locked.</summary>
	public Dictionary<string, bool> unlockedAbilities;

	/// <summary>Equipped ability ids per hotbar slot. Null/empty entry = vacant. Length should be <see cref="AbilitySlotCount"/>.</summary>
	public string[] equippedAbilityIds;

	/// <summary>Unlocked upgrade ids (true = unlocked). Missing / false = locked.</summary>
	public Dictionary<string, bool> unlockedUpgrades;

	/// <summary>Applied upgrade levels by id. Missing / 0 = unlocked but not purchased (or locked).</summary>
	public Dictionary<string, int> upgradeLevels;

	/// <summary>Lifetime of chests opened this profile (drives skeleton key display case unlock).</summary>
	public int chestsOpened;

	/// <summary>True after the skeleton key has been claimed from its display case.</summary>
	public bool skeletonKeyClaimed;

	/// <summary>Active linear quest id (null when catalog complete or not started).</summary>
	public string activeQuestId;

	/// <summary>Step index within <see cref="activeQuestId"/>.</summary>
	public int activeStepIndex;

	/// <summary>Completed quest ids in catalog order.</summary>
	public List<string> completedQuestIds;

	public ProfileSaveData()
	{
		UpdateTime = "INVALID";
		EnsureProgressDictionaries();
	}

	public string GetUpdateTime()
	{
		return UpdateTime;
	}

	public void EnsureProgressDictionaries()
	{
		if ( unlockedAbilities == null )
			unlockedAbilities = new Dictionary<string, bool>();

		if ( equippedAbilityIds == null || equippedAbilityIds.Length != AbilitySlotCount )
		{
			string[] next = new string[ AbilitySlotCount ];
			if ( equippedAbilityIds != null )
			{
				int copy = Math.Min( equippedAbilityIds.Length, next.Length );
				for ( int i = 0; i < copy; i++ )
					next[ i ] = equippedAbilityIds[ i ];
			}
			equippedAbilityIds = next;
		}

		if ( unlockedUpgrades == null )
			unlockedUpgrades = new Dictionary<string, bool>();

		if ( upgradeLevels == null )
			upgradeLevels = new Dictionary<string, int>();

		EnsureQuestProgress();
	}

	public void EnsureQuestProgress()
	{
		if ( completedQuestIds == null )
			completedQuestIds = new List<string>();
		if ( activeStepIndex < 0 )
			activeStepIndex = 0;
	}

	/// <summary>
	/// Union unlock flags and take max upgrade levels from <paramref name="other"/> into this profile.
	/// Slot loadout stays on the primary.
	/// </summary>
	public bool MergeBooleanUnlockProgressFrom( ProfileSaveData other )
	{
		if ( other == null )
			return false;

		EnsureProgressDictionaries();
		other.EnsureProgressDictionaries();

		bool changed = false;
		foreach ( KeyValuePair<string, bool> pair in other.unlockedAbilities )
		{
			if ( !pair.Value )
				continue;
			if ( unlockedAbilities.TryGetValue( pair.Key, out bool existing ) && existing )
				continue;
			unlockedAbilities[ pair.Key ] = true;
			changed = true;
		}

		foreach ( KeyValuePair<string, bool> pair in other.unlockedUpgrades )
		{
			if ( !pair.Value )
				continue;
			if ( unlockedUpgrades.TryGetValue( pair.Key, out bool existing ) && existing )
				continue;
			unlockedUpgrades[ pair.Key ] = true;
			changed = true;
		}

		foreach ( KeyValuePair<string, int> pair in other.upgradeLevels )
		{
			int incoming = pair.Value;
			if ( incoming <= 0 )
				continue;
			if ( upgradeLevels.TryGetValue( pair.Key, out int existing ) && existing >= incoming )
				continue;
			upgradeLevels[ pair.Key ] = incoming;
			changed = true;
		}

		if ( other.chestsOpened > chestsOpened )
		{
			chestsOpened = other.chestsOpened;
			changed = true;
		}

		if ( other.skeletonKeyClaimed && !skeletonKeyClaimed )
		{
			skeletonKeyClaimed = true;
			changed = true;
		}

		if ( MergeQuestProgressFrom( other ) )
			changed = true;

		return changed;
	}

	/// <summary>Keeps the furthest linear quest progress between two saves.</summary>
	public bool MergeQuestProgressFrom( ProfileSaveData other )
	{
		if ( other == null )
			return false;

		EnsureQuestProgress();
		other.EnsureQuestProgress();

		bool changed = false;
		if ( other.completedQuestIds != null )
		{
			for ( int i = 0; i < other.completedQuestIds.Count; i++ )
			{
				string id = other.completedQuestIds[ i ];
				if ( string.IsNullOrEmpty( id ) )
					continue;
				if ( completedQuestIds.Contains( id ) )
					continue;
				completedQuestIds.Add( id );
				changed = true;
			}
		}

		int selfScore = ComputeQuestProgressScore( this );
		int otherScore = ComputeQuestProgressScore( other );
		if ( otherScore > selfScore )
		{
			activeQuestId = other.activeQuestId;
			activeStepIndex = other.activeStepIndex;
			changed = true;
		}

		return changed;
	}

	static int ComputeQuestProgressScore( ProfileSaveData save )
	{
		if ( save == null )
			return 0;

		int completed = save.completedQuestIds != null ? save.completedQuestIds.Count : 0;
		int step = Mathf.Max( 0, save.activeStepIndex );
		return completed * 1000 + step;
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
		if ( profileSaveData != null )
			profileSaveData.EnsureProgressDictionaries();

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
