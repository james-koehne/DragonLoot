using System;

using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class FunkClient : MonoBehaviour
{
	public static FunkClient Instance;

#if DISCORD_ENABLED
	public DiscordController discordController;
#endif
	public FunkBotClient funkBotClient;

#if STEAM_BUILD
	public SteamManager Steam;
#endif
#if STOVE_BUILD
	public StoveManager Stove;
#endif

	public bool IsFullyLoaded { get; private set; }

#if STEAM_BUILD
	private bool steamManagerLoaded = false;
#endif
#if STOVE_BUILD
	private bool stoveManagerLoaded = false;
#endif
	private bool awakeFinished = false;

	public bool IsBotConnected()
	{
		if ( funkBotClient != null )
		{
			return funkBotClient.IsConnectedAndAuthorised;
		}

		return false;
	}

	public void connectToBot()
	{
		if ( funkBotClient != null )
		{
			funkBotClient.connectToBot();
		}
	}

	private void Awake()
	{
		Instance = this;

		if ( GameInstance.gameDef.FunkBotEnabled )
		{
			funkBotClient = gameObject.AddComponent<FunkBotClient>();
		}

		switch ( GameInstance.gameDef.platform )
		{
			case GameDefinition.PlatformType.Standalone:
#if DISCORD_ENABLED
				discordController = gameObject.AddComponent<DiscordController>();
#endif
				break;

			case GameDefinition.PlatformType.WebGL:
#if DISCORD_ENABLED
				discordController = gameObject.AddComponent<DiscordController>();
#endif
				break;

			default:
				break;
		}

#if STEAM_BUILD
		if ( GameInstance.gameDef.SteamEnabled )
			spawnSteamManager();
		else
			steamManagerLoaded = true;
#elif STOVE_BUILD
		if ( GameInstance.gameDef.StoveEnabled )
			spawnStoveManager();
		else
			stoveManagerLoaded = true;
#else
		// No first-party store SDK in this build configuration.
#endif

		awakeFinished = true;
		TryMarkFullyLoaded();
	}

	private void OnDestroy()
	{
		if ( funkBotClient != null )
		{
			funkBotClient.close();
		}
	}

	public void CheckDisplayName()
	{
#if STEAM_BUILD
		if ( GameInstance.gameDef.SteamEnabled && FunkCloud.FunkUser.PrivacyPolicyAccepted )
		{
			_ = FunkCloud.FunkUser.UpdateDisplayName( Steam.PlayerName );
			return;
		}
#endif
#if STOVE_BUILD
		if ( GameInstance.gameDef.StoveEnabled && FunkCloud.FunkUser.PrivacyPolicyAccepted && Stove != null )
		{
			_ = FunkCloud.FunkUser.UpdateDisplayName( Stove.PlayerName );
			return;
		}
#endif
		FunkCloud.FunkUser.SetRandomDisplayName();
	}

#if STEAM_BUILD
	private async void spawnSteamManager()
	{
		AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync( GameInstance.gameDef.SteamAssetRef );

		await handle.Task;

		if ( handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null )
		{
			handle.Result.transform.SetParent( gameObject.transform );

			Steam = handle.Result.GetComponent<SteamManager>();
		}
		else
		{
			Debug.LogError( $"Failed to load SteamManager" );
		}

		steamManagerLoaded = true;
		TryMarkFullyLoaded();
	}
#endif

#if STOVE_BUILD
	private async void spawnStoveManager()
	{
		AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync( GameInstance.gameDef.StoveAssetRef );

		await handle.Task;

		if ( handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null )
		{
			handle.Result.transform.SetParent( gameObject.transform );

			Stove = handle.Result.GetComponent<StoveManager>();
		}
		else
		{
			Debug.LogError( $"Failed to load StoveManager" );
		}

		stoveManagerLoaded = true;
		TryMarkFullyLoaded();
	}
#endif

	private void TryMarkFullyLoaded()
	{
		if ( IsFullyLoaded )
			return;

		bool platformReady = true;
#if STEAM_BUILD
		platformReady = steamManagerLoaded;
#elif STOVE_BUILD
		platformReady = stoveManagerLoaded;
#endif

		if ( awakeFinished && platformReady )
		{
			IsFullyLoaded = true;
		}
	}
}
