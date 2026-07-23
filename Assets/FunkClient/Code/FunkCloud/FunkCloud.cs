using System;
using System.Net.NetworkInformation;

using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class FunkCloud : MonoBehaviour
{
	private static bool _isInstanced = false;

	public static string AppName => Application.productName;
	public static bool Connected => Application.internetReachability != NetworkReachability.NotReachable && NetworkInterface.GetIsNetworkAvailable();
	public static bool IsConnected => GameInstance.gameDef.FunkBackendEnabled && FunkSocket.IsConnected;

	public static GameObject GameObject { get; private set; }
	public static FunkCloud Instance { get; private set; }

	public static FunkCloudUser FunkUser { get; private set; }
	public static FunkCloudSocket FunkSocket { get; private set; }
	public static FunkCloudMatchmaking FunkMatchmaking { get; private set; }
	public static FunkCloudNotifications FunkNotifications { get; private set; }
	public static FunkCloudLeaderboards FunkLeaderboards { get; private set; }
	public static FunkCloudStats FunkStats { get; private set; }

	private void Awake()
	{
		Debug.Log( "FunkClient: Loaded" );

		if ( _isInstanced )
		{
			Debug.LogError( $"FunkCloud manager already exists!" );
			Destroy( this );
			return;
		}

		GameObject = gameObject;
		Instance = this;

		DontDestroyOnLoad( gameObject );

		_isInstanced = true;

		if ( GameInstance.gameDef.FunkBackendEnabled )
		{
			setupFunkBackend();
		}
	}

	private async void setupFunkBackend()
	{
		FunkUser = gameObject.AddComponent<FunkCloudUser>();
		FunkSocket = gameObject.AddComponent<FunkCloudSocket>();
		FunkNotifications = gameObject.AddComponent<FunkCloudNotifications>();
		FunkLeaderboards = gameObject.AddComponent<FunkCloudLeaderboards>();
		FunkStats = gameObject.AddComponent<FunkCloudStats>();

		if ( GameInstance.gameDef.FunkBackendOverrideClasses )
		{
			// Overridable classes spawned in
			AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync( GameInstance.gameDef.FunkBackendAssetRef );

			await handle.Task;

			if ( handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null )
			{
				DontDestroyOnLoad( handle.Result );

				handle.Result.transform.SetParent( gameObject.transform );

				FunkMatchmaking = handle.Result.GetComponent<FunkCloudMatchmaking>();
			}
			else
			{
				Debug.LogError( $"Failed to load FunkBackendAssetRef" );
			}
		}
		else
		{
			FunkMatchmaking = gameObject.AddComponent<FunkCloudMatchmaking>();
		}

		FunkUser.Init();
		FunkSocket.Init();
		FunkMatchmaking.Init();
		FunkNotifications.Init();
		FunkLeaderboards.Init();

		//FunkUser.Authenticate();
	}

	private void OnApplicationQuit()
	{
		_isInstanced = false;

		if ( GameInstance.gameDef.FunkBackendEnabled )
		{
			FunkMatchmaking.LeaveCurrentMatch();
		}
	}

	private void OnDestroy()
	{
		if ( _isInstanced )
		{
			Debug.LogError( $"{gameObject.name} (FunkCloud manager) has been destroyed during run time. FunkCloud functions might become unstable. Was this intentional?" );
			_isInstanced = false;
		}

		GameObject = null;
	}
}