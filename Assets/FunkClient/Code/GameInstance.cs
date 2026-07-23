using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Newtonsoft.Json;

using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

public static class GameInstance
{
	public static GameDefinition gameDef;
	public static GameInstanceDefinition gameInstanceDef;

	public static LocalGameData LocalGameData;

	/// <summary>PlayerPrefs key used for <see cref="LocalGameData"/> (base name from <see cref="GameInstanceDefinition.GameLocalDataName"/> plus Steam id when available).</summary>
	public static string LocalGameDataStorageKey { get; private set; }

	private static string lastSavedLocalGameDataJson;
	private static bool isFunkClientLoaded = false;

	public static Dictionary<string, ScriptableObject> _definitions = new Dictionary<string, ScriptableObject>();
	public static bool _definitionsLoaded = false;
	public static bool WasFirstLoad = false;

	private const string BaseSceneAddress = "Assets/Scenes/Game.unity";
	private static bool CatchAllExceptions = true;
	private static bool LoadGame = true;

	public static volatile float CachedTimeScale = 1f;

	/// <summary>Mirror of <see cref="Time.time"/> (scaled). Refreshed on the main thread; safe to read from background threads.</summary>
	public static volatile float CachedTime = 0f;

	[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.BeforeSceneLoad )]
	private static void OnRuntimeLoad()
	{
		try
		{

#if UNITY_EDITOR
			// Don't load the game if we are in a scene that has "Test" in the name
			if ( SceneManager.sceneCount >= 1 )
			{
				Scene currentScene = SceneManager.GetActiveScene();

				if ( !currentScene.name.IsNullOrEmpty() && currentScene.name.Contains( "Test" ) )
				{
					LoadGame = false;
				}
			}
#endif

			if ( LoadGame )
			{
				CachedTimeScale = Time.timeScale;
				CachedTime = Time.time;
				Setup();
			}
		}
		catch ( Exception ex )
		{
			Debug.LogError( "OnRuntimeLoad() Error: " + ex.Message );
			Debug.LogError( "Stack trace:\n" + ex.StackTrace );
		}
	}

	private static void HandleLog( string logString, string stackTrace, LogType type )
	{
		if ( type == LogType.Exception )
		{
			Debug.LogError( "Global exception: " + logString + "\n" + stackTrace );
		}
	}

	private static void OnUnhandledException( object sender, UnhandledExceptionEventArgs e )
	{
		Debug.LogError( $"Unhandled Exception: {e.ExceptionObject}" );
	}

	private static void OnUnobservedTaskException( object sender, UnobservedTaskExceptionEventArgs e )
	{
		Debug.LogError( $"Unobserved Task Exception: {e.Exception}" );
		e.SetObserved(); // Prevent the process from being terminated
	}

	private static async void Setup()
	{
		if ( CatchAllExceptions )
		{
			AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
			TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
			//Application.logMessageReceived += HandleLog;
		}

		await loadBaseSceneFromConfig();

		await loadDefinitionAssets();

		gameInstanceDef = GetDefinition<GameInstanceDefinition>();
		gameDef = GetDefinition<GameDefinition>( gameInstanceDef.gameDefName );

		Debug.Log( "FunkClient: Using " + gameInstanceDef.gameDefName + " settings" );

		await ProfileData.Setup();

		if ( !isFunkClientLoaded && gameDef.FunkBackendEnabled )
		{
			await loadFunkClient();
		}

		resolveLocalGameDataStorageKey();

		string legacyLocalKey = gameInstanceDef.GameLocalDataName;

		LocalGameData = ProfileData.GetSetting<LocalGameData>( LocalGameDataStorageKey );

		bool migratedFromLegacy = false;
		if ( LocalGameData == null && !string.Equals( LocalGameDataStorageKey, legacyLocalKey, StringComparison.Ordinal ) )
		{
			LocalGameData = ProfileData.GetSetting<LocalGameData>( legacyLocalKey );
			migratedFromLegacy = LocalGameData != null;
		}

		if ( LocalGameData == null )
		{
			LocalGameData = new LocalGameData();
			ProfileData.SaveSetting( LocalGameDataStorageKey, LocalGameData );
		}

		LocalGameData.Setup();

		if ( migratedFromLegacy )
		{
			ProfileData.SaveSetting( LocalGameDataStorageKey, LocalGameData );
			lastSavedLocalGameDataJson = JsonConvert.SerializeObject( LocalGameData );
		}

		WasFirstLoad = LocalGameData.FirstLoad;
		LocalGameData.FirstLoad = false;

		await loadGame();

		//SteamCloud.DEBUG_LOG_JSON = GameMode.Instance.DebugDefinition.debugLogging;

#if STEAM_BUILD
		if ( gameDef.SteamEnabled )
		{
			SteamCloud.SyncWithSteam<LocalGameData>();
		}
#endif

		SaveLocalGameData();
	}

	public static async Task RestartGame()
	{
		await loadBaseSceneFromConfig();

		await loadGame();
	}

	private static async Task loadBaseSceneFromConfig()
	{
		await LoadSceneByAddress( BaseSceneAddress );
	}

	private static async Task LoadSceneByAddress( string sceneAddress )
	{
		AsyncOperation handle = SceneManager.LoadSceneAsync( sceneAddress, LoadSceneMode.Single );

		TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

		handle.completed += ( asyncOperation ) => tcs.SetResult( true );

		await tcs.Task;
	}

	private static async Task loadFunkClient()
	{
		AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync( gameInstanceDef.funkClientRef );

		await handle.Task;

		if ( handle.Status == AsyncOperationStatus.Succeeded )
		{
			await TaskEx.WaitUntil( () => FunkClient.Instance != null && FunkClient.Instance.IsFullyLoaded );

			isFunkClientLoaded = true;
		}
		else
		{
			Debug.LogError( $"Failed to load FunkClient prefab" );
		}
	}

	private static async Task loadGame()
	{
		if ( GameMode.Instance == null )
		{
			AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync( gameInstanceDef.gameModeRef );

			await handle.Task;

			if ( handle.Status != AsyncOperationStatus.Succeeded )
			{
				Debug.LogError( $"Failed to load GameMode prefab" );
			}
		}
	}

	public static void SaveLocalGameData()
	{
		if ( LocalGameData == null )
			return;

		if ( string.IsNullOrEmpty( LocalGameDataStorageKey ) )
			resolveLocalGameDataStorageKey();

		string currentJson = JsonConvert.SerializeObject( LocalGameData );
		if ( currentJson == lastSavedLocalGameDataJson )
			return;

		ProfileData.SaveSetting( LocalGameDataStorageKey, LocalGameData );

#if STEAM_BUILD
		if ( gameDef.SteamEnabled )
		{
			SteamCloud.SyncWithSteam<LocalGameData>();
		}
#endif

		lastSavedLocalGameDataJson = currentJson;
	}

	private static void resolveLocalGameDataStorageKey()
	{
		string baseName = gameInstanceDef != null ? gameInstanceDef.GameLocalDataName : "LocalGameData";

		LocalGameDataStorageKey = baseName;
#if STEAM_BUILD
		if ( gameDef != null && gameDef.SteamEnabled
			&& SteamManager.Instance != null && SteamManager.Instance.Initialized
			&& SteamManager.Instance.PlayerId.m_SteamID != 0 )
			LocalGameDataStorageKey = $"{baseName}_{SteamManager.Instance.PlayerId.m_SteamID}";
#endif
#if STOVE_BUILD
		if ( gameDef != null && gameDef.StoveEnabled
			&& StoveManager.Instance != null && StoveManager.Instance.Initialized
			&& StoveManager.Instance.GameUserId != 0 )
			LocalGameDataStorageKey = $"{baseName}_{StoveManager.Instance.GameUserId}";
#endif
	}

	public static void ClearProfile()
	{
		LocalGameData.ClearProfile();

		SaveLocalGameData();
	}

	private static TaskCompletionSource<bool> definitionsLoaded;

	private static async Task loadDefinitionAssets()
	{
		definitionsLoaded = new TaskCompletionSource<bool>();
		_definitions = new Dictionary<string, ScriptableObject>();

		Addressables.LoadAssetsAsync<ScriptableObject>( "Definition", null ).Completed += onDefinitionsLoaded;

		await definitionsLoaded.Task;
	}

	// Callback function called when assets are loaded
	private static void onDefinitionsLoaded( AsyncOperationHandle<IList<ScriptableObject>> obj )
	{
		if ( obj.Status == AsyncOperationStatus.Succeeded )
		{
			IList<ScriptableObject> assets = obj.Result;

			// Do something with the loaded assets
			foreach ( ScriptableObject asset in assets )
			{
				_definitions.Add( asset.name, asset );
			}

			_definitionsLoaded = true;
			definitionsLoaded.SetResult( true );
		}
		else
		{
			definitionsLoaded.SetException( obj.OperationException );
			Debug.LogError( "Failed to load assets: " + obj.OperationException );
		}
	}

	public static T GetDefinition<T>() where T : ScriptableObject
	{
		if ( _definitions.TryGetValue( typeof( T ).Name, out ScriptableObject scriptableObject ) )
		{
			return (T)scriptableObject;
		}

		return null;
	}

	public static T GetDefinition<T>( string nameOverride ) where T : ScriptableObject
	{
		if ( _definitions.TryGetValue( nameOverride, out ScriptableObject scriptableObject ) )
		{
			return (T)scriptableObject;
		}

		return null;
	}

	public static void SetTimeScale( float value )
	{
		Time.timeScale = value;
		CachedTimeScale = value;
	}

	public static void RefreshCachedTimeScaleFromUnity()
	{
		CachedTimeScale = Time.timeScale;
		CachedTime = Time.time;
	}
}