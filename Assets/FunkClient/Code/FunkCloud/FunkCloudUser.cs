using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Nakama;
using System.Threading.Tasks;
using System;
using System.Linq;
using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text;
using Nakama.TinyJson;

public enum FunkAuthenticationType
{
	None,
	Anonymous,
	Email,
	Steam,
	Stove
}

public class FunkCloudUser : MonoBehaviour
{
	public const string RpcGetInviteID = "RpcGetInvite";
	public const string RpcFindByUsernameID = "RpcFindByUsername";

	private const string UniqueIdKey = "unique_device_id";
	private const float DefaultRefreshCheckInterval = 5.0f;
	private const string MasterPrefix = "Master_";

	private const float BackoffBase = 0.5f;
	private const float BackoffMax = 16f;
	private const float BackoffJitter = 0.25f;


	public static UnityEvent<ISession> OnUserAuthenticated = new UnityEvent<ISession>();
	public static UnityEvent<ISession> OnUserLoggedIn = new UnityEvent<ISession>();
	public static UnityEvent OnUserLoggedOut = new UnityEvent();

	public bool CanAuthenticate = false;
	public bool PrivacyPolicyAccepted = true;

	public bool ShouldAuthenticate => authType == FunkAuthenticationType.None && !authenticating && !IsAuthenticated;
	public bool IsAuthenticated => gameSession != null;
	public bool IsLoggedIn => authType == FunkAuthenticationType.Email && masterSession != null;
	public bool IsAnonymous => authType == FunkAuthenticationType.Anonymous;

	public Client MasterClient { get; private set; }
	public Client GameClient { get; private set; }

	private ISession masterSession;
	private IApiAccount masterAccount;
	private ISession gameSession;
	private IApiAccount gameAccount;

	private ISession prevMasterSession;
	private ISession prevGameSession;

	private static OfflineSession _offlineSession;

	private FunkAuthenticationType authType;
	private bool authenticating = false;
	private float _lastAuthenticationAttempt = 0.0f;

	private readonly int RefreshBeforeExpirySeconds = 60;
	private float _refreshCheckInterval = DefaultRefreshCheckInterval;
	private float _nextRefreshCheck = 0f;

	private int _reconnectAttempts = 0;

	public void Init()
	{
		OnUserAuthenticated.AddListener( handleUserAuthenticated );

		FunkCloud.FunkSocket.OnSocketConnected.AddListener( handleSocketConnected );
		FunkCloud.FunkSocket.OnSocketDisconnected.AddListener( handleSocketDisconnected );
		FunkCloud.FunkSocket.OnSocketGracefulReconnect.AddListener( handleGracefulReconnection );

		_lastAuthenticationAttempt = 0.0f;
	}

	public void handleSocketConnected()
	{
		_lastAuthenticationAttempt = 0.0f;
	}

	public void handleGracefulReconnection()
	{
		_lastAuthenticationAttempt = 0.0f;
	}

	private void handleSocketDisconnected()
	{
		authType = FunkAuthenticationType.None;

		prevMasterSession = masterSession;
		prevGameSession = gameSession;

		masterSession = null;
		gameSession = null;

		authenticating = false;

		OnUserLoggedOut.Invoke();
	}

	void Update()
	{
		if ( CanAuthenticate && ShouldAuthenticate && ThreadSafeTime.Now > 1.0f && ThreadSafeTime.Now >= _lastAuthenticationAttempt )
		{
			tryAuthenticate();
		}

		if ( authType != FunkAuthenticationType.None && IsAuthenticated && Time.realtimeSinceStartup >= _nextRefreshCheck )
		{
			_nextRefreshCheck = Time.realtimeSinceStartup + _refreshCheckInterval;
			_ = EnsureSessionsRefreshedIfNeeded();
		}
	}

	private void tryAuthenticate()
	{
		_ = Authenticate();

		_lastAuthenticationAttempt = ThreadSafeTime.Now + UnityEngine.Random.Range( 1.0f, 2.0f ) + CalculateBackoff( _reconnectAttempts );
	}

	private void handleUserAuthenticated( ISession session )
	{
		FunkClient.Instance.connectToBot();

		FunkClient.Instance.CheckDisplayName();
	}

	private string GetOrCreateUID()
	{
		string uniqueIdSettingKey = GetUniqueIdSettingKey();
		string uniqueId = ProfileData.GetSetting<string>( uniqueIdSettingKey );

		if ( string.IsNullOrEmpty( uniqueId ) )
		{
			uniqueId = Guid.NewGuid().ToString();
			ProfileData.SaveSetting( uniqueIdSettingKey, uniqueId );
			Debug.Log( $"FunkCloudUser::GetOrCreateUID - generated new uid and saved'" );
		}
		else
		{
			Debug.Log( $"FunkCloudUser::GetOrCreateUID - using existing uid" );
		}

		return uniqueId;
	}

	private void ClearUID()
	{
		ProfileData.SaveSetting( GetUniqueIdSettingKey(), "" );
	}

	private string GetUniqueIdSettingKey()
	{
#if STEAM_BUILD
		if ( GameInstance.gameDef != null && GameInstance.gameDef.SteamEnabled && SteamManager.Instance != null && SteamManager.Instance.PlayerId.m_SteamID != 0 )
			return $"{UniqueIdKey}_steam_{SteamManager.Instance.PlayerId.m_SteamID}";
#endif
#if STOVE_BUILD
		if ( GameInstance.gameDef != null && GameInstance.gameDef.StoveEnabled && StoveManager.Instance != null && StoveManager.Instance.GameUserId != 0 )
			return $"{UniqueIdKey}_stove_{StoveManager.Instance.GameUserId}";
#endif
		return UniqueIdKey;
	}

	private void EnsureClientsCreated()
	{
		if ( MasterClient != null && GameClient != null )
			return;

#if UNITY_WEBGL && !UNITY_EDITOR
		IHttpAdapter adapter = UnityWebRequestAdapter.Instance;
#else
		IHttpAdapter adapter = HttpRequestAdapter.WithGzip();
#endif

		Uri masterUri = new Uri( GameInstance.gameDef.masterBackendScheme + "://" + GameInstance.gameDef.masterBackendHost );
		MasterClient = new Client( masterUri, GameInstance.gameDef.masterBackendServerKey, adapter );

		GameDefinition.PlatformGameDefinition platformGameDef = GameInstance.gameDef.GetPlatformGameDefinition( GameInstance.gameDef.platform );
		Uri gameUri = new Uri( platformGameDef.backendScheme + "://" + platformGameDef.backendHost );
		GameClient = new Client( gameUri, platformGameDef.backendServerKey, adapter );
	}

	public async Task Authenticate()
	{
		try
		{
			authenticating = true;

			EnsureClientsCreated();

			FunkAuthenticationType authMode = GameInstance.gameDef.authMode;

			if ( !PrivacyPolicyAccepted )
			{
				authMode = FunkAuthenticationType.Anonymous;
			}

			switch ( authMode )
			{
				case FunkAuthenticationType.Anonymous:
				{
					await LoginAnonymous();
					break;
				}
				case FunkAuthenticationType.Email:
				{
					string email = "";
					string password = "";

					if ( GameInstance.gameDef.testEmailLogin )
					{
						email = GameInstance.gameDef.testEmailEmail;
						password = GameInstance.gameDef.testEmailPassword;
					}

					if ( GameInstance.gameDef.autoLogin && !string.IsNullOrEmpty( email ) && !string.IsNullOrEmpty( password ) )
					{
						await LoginUser( email, password );
					}
					else
					{
						await LoginAnonymous();
					}
					break;
				}
				case FunkAuthenticationType.Steam:
				{
					await LoginSteam();
					break;
				}
				case FunkAuthenticationType.Stove:
				{
					await LoginStove();
					break;
				}
			}
		}
		catch ( Exception ex )
		{
			authenticating = false;
			Debug.LogError( $"FunkCloudUser::Authenticate: {ex.Message}" );
		}
		finally
		{
			authenticating = false;
		}
	}

	private float CalculateBackoff( int attempts )
	{
		float raw = BackoffBase * Mathf.Pow( 2f, Mathf.Max( 0, attempts - 1 ) );
		float capped = Mathf.Min( raw, BackoffMax );
		float jitter = UnityEngine.Random.Range( 0f, BackoffJitter );
		return capped + jitter;
	}

	public void SaveSession()
	{
		if ( masterSession == null || gameSession == null )
			return;

		string loginTokens = $"{masterSession.AuthToken}|{masterSession.RefreshToken}|{gameSession.AuthToken}|{gameSession.RefreshToken}";
		_ = SecureStorageManager.StoreSecureString( SecureClientDataKey(), loginTokens );
	}

	public async Task<bool> TryRestoreSession()
	{
		try
		{
			EnsureClientsCreated();

			string combinedTokens = await SecureStorageManager.RetrieveSecureString( SecureClientDataKey() );
			if ( string.IsNullOrEmpty( combinedTokens ) )
			{
				throw new Exception( "No tokens found, can't restore session" );
			}

			// Split the combined tokens back into individual tokens
			var tokens = combinedTokens.Split( '|' );
			if ( tokens.Length != 4 )
			{
				throw new Exception( "Invalid token format, can't restore session" );
			}

			string authToken = tokens[ 0 ];
			string refreshToken = tokens[ 1 ];
			string gameAuthToken = tokens[ 2 ];
			string gameRefreshToken = tokens[ 3 ];

			masterSession = Session.Restore( authToken, refreshToken );
			gameSession = Session.Restore( gameAuthToken, gameRefreshToken );

			if ( masterSession.IsExpired || masterSession.IsRefreshExpired || gameSession.IsExpired || gameSession.IsRefreshExpired )
			{
				throw new Exception( "Master or game session is expired, can't restore session" );
			}

			await MasterClient.SessionRefreshAsync( masterSession );
			await GameClient.SessionRefreshAsync( gameSession );

			return true;
		}
		catch ( System.Exception )
		{
			masterSession = null;
			gameSession = null;

			//Debug.Log( "TryRestoreSession Error: " + ex.Message );

			SecureStorageManager.ClearSecureString( SecureClientDataKey() );

			return false;
		}
	}

	private string SecureClientDataKey()
	{
		return Application.platform.ToString() + "_client_data_" + GetOrCreateUID();
	}

	private async Task LoginAnonymous()
	{
		Debug.Log( $"FunkCloudUser::LoginAnonymous -> Start Auth" );

		// Anonymous login using device UID
		try
		{
			ISession session = await GameClient.AuthenticateDeviceAsync( GetOrCreateUID() );
			IApiAccount account = await GameClient.GetAccountAsync( session );

			if ( GameMode.Instance.DebugDefinition.debugBackend )
			{
				Debug.Log( $"FunkCloudUser::LoginAnonymous ->  Username:{session.Username} UserID:{session.UserId} Vars:{session.Vars.ToJson()}" );
			}

			authType = FunkAuthenticationType.Anonymous;
			gameSession = session;
			gameAccount = account;

			MainThreadDispatcher.RunOnMainThread( () => OnUserAuthenticated.Invoke( session ) );

			_reconnectAttempts = 0;
		}
		catch ( Nakama.ApiResponseException ex )
		{
			gameSession = null;
			authenticating = false;

			_reconnectAttempts++;

			Debug.LogError( $"FunkCloudUser::LoginAnonymous: {ex.StatusCode}:{ex.Message}" );
		}
		catch ( Exception ex )
		{
			authenticating = false;
			Debug.LogError( $"FunkCloudUser::Authenticate: {ex.Message}" );
		}
	}

	public async Task LogoutUser()
	{
		authType = FunkAuthenticationType.None;

		ISession sessionToLogoutMaster = masterSession;
		ISession sessionToLogoutGame = gameSession;

		prevMasterSession = masterSession;
		prevGameSession = gameSession;

		masterSession = null;
		gameSession = null;

		authenticating = false;

		OnUserLoggedOut.Invoke();

		try
		{
			if ( MasterClient != null && sessionToLogoutMaster != null )
			{
				await MasterClient.SessionLogoutAsync( sessionToLogoutMaster );
			}

			if ( GameClient != null && sessionToLogoutGame != null )
			{
				await GameClient.SessionLogoutAsync( sessionToLogoutGame );
			}
		}
		catch ( Exception ex )
		{
			Debug.LogWarning( $"FunkCloudUser::LogoutUser - logout API failed: {ex.Message}" );
		}

		await SecureStorageManager.StoreSecureString( SecureClientDataKey(), "" );

		_lastAuthenticationAttempt = 0.0f;
	}

	public static Guid GuidFromString( string seed )
	{
		if ( string.IsNullOrEmpty( seed ) )
			throw new ArgumentNullException( nameof( seed ), "Seed for GuidFromString cannot be null or empty." );

		using ( MD5 md5 = MD5.Create() )
		{
			byte[] hash = md5.ComputeHash( Encoding.UTF8.GetBytes( seed ) );
			return new Guid( hash );
		}
	}

	public async Task<FunkCloudUtils.Response> LoginSteam()
	{
#if STEAM_BUILD
		try
		{
			Guid steamGuid = GuidFromString( MasterPrefix + SteamManager.Instance.PlayerId.m_SteamID.ToString() );

			Debug.Log( $"FunkCloudUser::LoginSteam: User={steamGuid}" );

			ISession session = await MasterClient.AuthenticateDeviceAsync( steamGuid.ToString() );
			IApiAccount account = await MasterClient.GetAccountAsync( session );

			masterSession = session;
			masterAccount = account;

			if ( GameMode.Instance.DebugDefinition.debugBackend )
			{
				Debug.Log( $"FunkCloudUser::LoginSteam -> Username:{masterSession.Username} UserID:{masterSession.UserId} Vars:{masterSession.Vars.ToJson()}" );
			}

			return await LoginSteamIntoGame();
		}
		catch ( Nakama.ApiResponseException ex )
		{
			masterSession = null;
			authenticating = false;

			_reconnectAttempts++;

			Debug.LogError( $"FunkCloudUser::LoginSteam: {ex.StatusCode}:{ex.Message}" );

			return new FunkCloudUtils.Response() { success = false, result = "Login failed" };
		}
		catch ( Exception ex )
		{
			authenticating = false;
			Debug.LogError( $"FunkCloudUser::LoginSteam: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}" );

			_reconnectAttempts++;

			return new FunkCloudUtils.Response() { success = false, result = "Login failed" };
		}
#else
		Debug.LogError( "FunkCloudUser::LoginSteam: STEAM_BUILD not defined." );
		authenticating = false;
		return await Task.FromResult( new FunkCloudUtils.Response() { success = false, result = "Steam not available in this build" } );
#endif
	}

	private async Task<FunkCloudUtils.Response> LoginSteamIntoGame()
	{
#if STEAM_BUILD
		try
		{
			string ticket = SteamManager.Instance.GetSteamAuthTicket();
			ISession session = await GameClient.AuthenticateSteamAsync( ticket );
			IApiAccount account = await GameClient.GetAccountAsync( session );

			gameSession = session;
			gameAccount = account;

			if ( GameMode.Instance.DebugDefinition.debugBackend )
			{
				Debug.Log( $"FunkCloudUser::LoginSteamGame -> Username:{gameSession.Username} UserID:{gameSession.UserId} Vars:{gameSession.Vars.ToJson()}" );
			}

			MainThreadDispatcher.RunOnMainThread( () =>
			{
				OnUserAuthenticated.Invoke( session );
				OnUserLoggedIn.Invoke( session );
			} );

			authType = FunkAuthenticationType.Steam;

			_reconnectAttempts = 0;

			await SaveUserIDToStorage( session.UserId );

			string uid = GetOrCreateUID();
			bool deviceLinked = false;
			List<IApiAccountDevice> accountDevices = ( account.Devices != null ) ? account.Devices.ToList() : new List<IApiAccountDevice>();

			for ( int i = 0; i < accountDevices.Count; i++ )
			{
				if ( accountDevices[ i ].Id == uid )
				{
					deviceLinked = true;
				}
			}

			if ( !deviceLinked )
			{
				Debug.Log( $"FunkCloudUser::LoginSteamIntoGame: Linking device: {uid}" );

				try
				{
					await GameClient.LinkDeviceAsync( session, uid );
					Debug.Log( $"FunkCloudUser::LoginSteamIntoGame: Linked device: {uid}" );
				}
				catch ( ApiResponseException apiEx ) when ( apiEx.GrpcStatusCode == 6 )
				{
					// Device ID already in use - clear and link with a new UID
					Debug.Log( $"FunkCloudUser::LoginSteamIntoGame: Device ID already in use (uid:{uid}), clearing and linking with new UID" );
					ClearUID();
					string newUid = GetOrCreateUID();
					Debug.Log( $"FunkCloudUser::LoginSteamIntoGame: Linking device with new UID: {newUid}" );
					await GameClient.LinkDeviceAsync( session, newUid );
				}
			}

			return new FunkCloudUtils.Response { success = true };
		}
		catch ( ApiResponseException apiException )
		{
			Debug.LogError( $"FunkCloudUser::LoginSteamIntoGame: uid:{GetOrCreateUID()} {apiException.Message}" );
			return new FunkCloudUtils.Response { success = false, result = "Login failed" };
		}
		catch ( Exception ex )
		{
			Debug.LogError( $"FunkCloudUser::LoginSteamIntoGame: {ex.Message}" );
			return new FunkCloudUtils.Response { success = false, result = "Login failed" };
		}
#else
		return await Task.FromResult( new FunkCloudUtils.Response { success = false, result = "Steam not available in this build" } );
#endif
	}

	public async Task<FunkCloudUtils.Response> LoginStove()
	{
#if STOVE_BUILD
		try
		{
			if ( StoveManager.Instance == null || !StoveManager.Instance.Initialized || StoveManager.Instance.GameUserId == 0 )
			{
				Debug.LogError( "FunkCloudUser::LoginStove: StoveManager not ready or invalid GameUserId." );
				return new FunkCloudUtils.Response() { success = false, result = "Login failed" };
			}

			Guid stoveGuid = GuidFromString( MasterPrefix + StoveManager.Instance.GameUserId.ToString() );

			Debug.Log( $"FunkCloudUser::LoginStove: User={stoveGuid}" );

			ISession session = await MasterClient.AuthenticateDeviceAsync( stoveGuid.ToString() );
			IApiAccount account = await MasterClient.GetAccountAsync( session );

			masterSession = session;
			masterAccount = account;

			if ( GameMode.Instance.DebugDefinition.debugBackend )
			{
				Debug.Log( $"FunkCloudUser::LoginStove -> Username:{masterSession.Username} UserID:{masterSession.UserId} Vars:{masterSession.Vars.ToJson()}" );
			}

			return await LoginStoveIntoGame();
		}
		catch ( Nakama.ApiResponseException ex )
		{
			masterSession = null;
			authenticating = false;

			_reconnectAttempts++;

			Debug.LogError( $"FunkCloudUser::LoginStove: {ex.StatusCode}:{ex.Message}" );

			return new FunkCloudUtils.Response() { success = false, result = "Login failed" };
		}
		catch ( Exception ex )
		{
			authenticating = false;
			Debug.LogError( $"FunkCloudUser::LoginStove: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}" );

			_reconnectAttempts++;

			return new FunkCloudUtils.Response() { success = false, result = "Login failed" };
		}
#else
		Debug.LogError( "FunkCloudUser::LoginStove: STOVE_BUILD not defined." );
		authenticating = false;
		return await Task.FromResult( new FunkCloudUtils.Response() { success = false, result = "Stove not available in this build" } );
#endif
	}

	private async Task<FunkCloudUtils.Response> LoginStoveIntoGame()
	{
#if STOVE_BUILD
		try
		{
			string customId = StoveManager.Instance.GameUserId.ToString();
			string username = StoveManager.Instance.PlayerName;
			if ( string.IsNullOrWhiteSpace( username ) )
				username = null;

			Dictionary<string, string> vars = null;
			if ( StoveManager.Instance.TryGetAccessToken( out string accessToken ) )
			{
				vars = new Dictionary<string, string>
				{
					{ "stove_access_token", accessToken }
				};
			}

			ISession session = await GameClient.AuthenticateCustomAsync( customId, username, true, vars );
			IApiAccount account = await GameClient.GetAccountAsync( session );

			gameSession = session;
			gameAccount = account;

			if ( GameMode.Instance.DebugDefinition.debugBackend )
			{
				Debug.Log( $"FunkCloudUser::LoginStoveIntoGame -> Username:{gameSession.Username} UserID:{gameSession.UserId} Vars:{gameSession.Vars.ToJson()}" );
			}

			MainThreadDispatcher.RunOnMainThread( () =>
			{
				OnUserAuthenticated.Invoke( session );
				OnUserLoggedIn.Invoke( session );
			} );

			authType = FunkAuthenticationType.Stove;

			_reconnectAttempts = 0;

			await SaveUserIDToStorage( session.UserId );

			string uid = GetOrCreateUID();
			bool deviceLinked = false;
			List<IApiAccountDevice> accountDevices = ( account.Devices != null ) ? account.Devices.ToList() : new List<IApiAccountDevice>();

			for ( int i = 0; i < accountDevices.Count; i++ )
			{
				if ( accountDevices[ i ].Id == uid )
				{
					deviceLinked = true;
				}
			}

			if ( !deviceLinked )
			{
				Debug.Log( $"FunkCloudUser::LoginStoveIntoGame: Linking device: {uid}" );

				try
				{
					await GameClient.LinkDeviceAsync( session, uid );
					Debug.Log( $"FunkCloudUser::LoginStoveIntoGame: Linked device: {uid}" );
				}
				catch ( ApiResponseException apiEx ) when ( apiEx.GrpcStatusCode == 6 )
				{
					Debug.Log( $"FunkCloudUser::LoginStoveIntoGame: Device ID already in use (uid:{uid}), clearing and linking with new UID" );
					ClearUID();
					string newUid = GetOrCreateUID();
					Debug.Log( $"FunkCloudUser::LoginStoveIntoGame: Linking device with new UID: {newUid}" );
					await GameClient.LinkDeviceAsync( session, newUid );
				}
			}

			return new FunkCloudUtils.Response { success = true };
		}
		catch ( ApiResponseException apiException )
		{
			Debug.LogError( $"FunkCloudUser::LoginStoveIntoGame: uid:{GetOrCreateUID()} {apiException.Message}" );
			return new FunkCloudUtils.Response { success = false, result = "Login failed" };
		}
		catch ( Exception ex )
		{
			Debug.LogError( $"FunkCloudUser::LoginStoveIntoGame: {ex.Message}" );
			return new FunkCloudUtils.Response { success = false, result = "Login failed" };
		}
#else
		return await Task.FromResult( new FunkCloudUtils.Response { success = false, result = "Stove not available in this build" } );
#endif
	}

	public async Task<FunkCloudUtils.Response> LoginUser( string user, string password )
	{
		string username = null;

		// Login with email and password, don't create an account
		try
		{
			// Removed for now, should add username login later as well as email.
			// Will just need to not display username by default in profile and have an ability to set/display your display name
			/*bool isEmail = FunkCloudUtils.IsValidEmailRegex( user );

			if ( !isEmail )
			{
				username = user;
				user = "";
			}*/

			ISession session = await MasterClient.AuthenticateEmailAsync( user, password, username, GameInstance.gameDef.testEmailLogin );
			IApiAccount account = await MasterClient.GetAccountAsync( session );

			authType = FunkAuthenticationType.Email;
			masterSession = session;
			masterAccount = account;

			return await LoginEmailIntoGame( user, password );
		}
		catch ( Nakama.ApiResponseException ex )
		{
			masterSession = null;
			authenticating = false;

			Debug.LogError( $"FunkCloudUser::LoginUser: {ex.StatusCode}:{ex.Message}" );

			return new FunkCloudUtils.Response() { success = false, result = "Login failed" };
		}
		catch ( Exception ex )
		{
			authenticating = false;
			Debug.LogError( $"FunkCloudUser::Authenticate: {ex.Message}" );

			return new FunkCloudUtils.Response() { success = false, result = "Login failed" };
		}
	}

	private async Task<FunkCloudUtils.Response> LoginEmailIntoGame( string email, string password )
	{
		Dictionary<string, string> mapping = await GetGameUserIdMap();
		if ( mapping == null || !mapping.ContainsKey( GameInstance.gameDef.gameBackendName ) )
			return await RegisterGameAccount( masterSession.Username, email, password );

		return await LoginGameEmail( email, password );
	}

	private async Task<FunkCloudUtils.Response> LoginGameEmail( string email, string password )
	{
		try
		{
			ISession session = await GameClient.AuthenticateEmailAsync( email, password, null, true );

			gameSession = session;
			gameAccount = await GameClient.GetAccountAsync( session );

			MainThreadDispatcher.RunOnMainThread( () =>
			{
				OnUserAuthenticated.Invoke( session );
				OnUserLoggedIn.Invoke( session );
			} );

			_reconnectAttempts = 0;

			return new FunkCloudUtils.Response { success = true };
		}
		catch ( Exception ex )
		{
			Debug.LogError( $"FunkCloudUser::LoginGameEmail: {ex.Message}" );
			return new FunkCloudUtils.Response { success = false, result = "Login failed" };
		}
	}

	public async Task<FunkCloudUtils.Response> RegisterUser( string username, string email, string password, bool subscribeNewsletter = false )
	{
		try
		{
			ISession session = await MasterClient.AuthenticateEmailAsync( email, password, username, true );

			authType = FunkAuthenticationType.Email;
			masterSession = session;
			masterAccount = await MasterClient.GetAccountAsync( session );

			if ( subscribeNewsletter )
			{
				FunkCloudUtils.Response valid = await FunkCloudUtils.IsValidEmailStrict( email );
				if ( valid.success )
					_ = EmailOctopusAPI.AddContactAsync( email, GameInstance.gameDef.emailOctopusGameTag );
			}

			return await RegisterGameAccount( username, email, password );
		}
		catch ( Exception ex )
		{
			Debug.LogError( $"FunkCloudUser::RegisterUser: {ex.Message}" );
			return new FunkCloudUtils.Response { success = false, result = "Register failed" };
		}
	}

	private async Task<FunkCloudUtils.Response> RegisterGameAccount( string username, string email, string password )
	{
		try
		{
			ISession session = await GameClient.AuthenticateEmailAsync( email, password, username, true );

			gameSession = session;
			gameAccount = await GameClient.GetAccountAsync( session );

			MainThreadDispatcher.RunOnMainThread( () =>
			{
				OnUserAuthenticated.Invoke( session );
				OnUserLoggedIn.Invoke( session );
			} );

			await SaveUserIDToStorage( session.UserId );
			return new FunkCloudUtils.Response { success = true };
		}
		catch ( Exception ex )
		{
			Debug.LogError( $"FunkCloudUser::RegisterGameAccount: {ex.Message}" );
			return new FunkCloudUtils.Response { success = false, result = "Register failed" };
		}
	}

	private async Task<Dictionary<string, string>> GetGameUserIdMap()
	{
		if ( MasterClient == null || masterSession == null )
			return null;

		IApiStorageObjects result = await MasterClient.ReadStorageObjectsAsync( masterSession, new[]
		{
			new StorageObjectId
			{
				Collection = "game_userids",
				Key = "userids",
				UserId = masterSession.UserId
			}
		} );

		if ( result.Objects == null || !result.Objects.Any() )
			return null;

		return JsonConvert.DeserializeObject<Dictionary<string, string>>( result.Objects.First().Value );
	}

	private async Task SaveUserIDToStorage( string userId )
	{
		if ( MasterClient == null || masterSession == null || string.IsNullOrEmpty( userId ) )
			return;

		IApiStorageObjects result = await MasterClient.ReadStorageObjectsAsync( masterSession, new[]
		{
			new StorageObjectId
			{
				Collection = "game_userids",
				Key = "userids",
				UserId = masterSession.UserId
			}
		} );

		Dictionary<string, string> map;
		string version = null;

		if ( result.Objects == null || !result.Objects.Any() )
		{
			map = new Dictionary<string, string>();
		}
		else
		{
			IApiStorageObject obj = result.Objects.First();
			version = obj.Version;
			map = JsonConvert.DeserializeObject<Dictionary<string, string>>( obj.Value );
		}

		string backend = GameInstance.gameDef.gameBackendName;

		if ( !map.TryGetValue( backend, out string existingUserId ) || existingUserId != userId )
		{
			map[ backend ] = userId;
			string json = JsonConvert.SerializeObject( map );

			await MasterClient.WriteStorageObjectsAsync( masterSession, new[]
			{
			new WriteStorageObject
			{
				Collection = "game_userids",
				Key = "userids",
				Value = json,
				Version = version // safe even if null
            }
		} );
		}
	}

	public ISession GetMasterSession()
	{
		return masterSession != null ? masterSession : prevMasterSession;
	}

	public ISession GetGameSession()
	{
		return gameSession != null ? gameSession : prevGameSession;
	}

	public static ISession GetOfflineSession()
	{
		if ( _offlineSession == null )
		{
			_offlineSession = new OfflineSession();
		}

		return _offlineSession;
	}

	public string GetMasterUserID()
	{
		if ( authType == FunkAuthenticationType.Anonymous )
		{
			return gameSession != null ? gameSession.UserId : prevGameSession != null ? prevGameSession.UserId : GetOfflineSession().UserId;
		}
		else if ( authType == FunkAuthenticationType.Email || authType == FunkAuthenticationType.Steam || authType == FunkAuthenticationType.Stove )
		{
			return masterSession != null ? masterSession.UserId : prevMasterSession != null ? prevMasterSession.UserId : GetOfflineSession().UserId;
		}

		return "";
	}

	public static string GetGameUserIDStatic()
	{
		// Must match the session used when building MatchInfo players (e.g. BBMSingleplayer.createMatchAIAsync):
		// use game session only when backend is enabled *and* socket is connected; otherwise offline session id.
		if ( GameInstance.gameDef != null && GameInstance.gameDef.FunkBackendEnabled
			&& FunkCloud.FunkSocket != null && FunkCloud.FunkSocket.IsConnected )
		{
			return FunkCloud.FunkUser.GetGameUserID();
		}

		return GetOfflineSession().UserId;
	}

	public string GetGameUserID()
	{
		return gameSession != null ? gameSession.UserId : prevGameSession != null ? prevGameSession.UserId : GetOfflineSession().UserId;
	}

	public static string GetDisplayNameStatic()
	{
		string displayName;

		if ( GameInstance.gameDef.FunkBackendEnabled )
		{
			displayName = FunkCloud.FunkUser.GetDisplayName();
		}
		else
		{
			displayName = "Offline";
		}

		return displayName;
	}

	public string GetDisplayName()
	{
		string displayName = "";

		if ( authType == FunkAuthenticationType.Anonymous )
		{
			displayName = gameAccount != null ? getCleanDisplayName( gameAccount.User.DisplayName ) : "Offline";
		}
		else if ( authType == FunkAuthenticationType.Email || authType == FunkAuthenticationType.Steam || authType == FunkAuthenticationType.Stove )
		{
			displayName = masterAccount != null ? getCleanDisplayName( masterAccount.User.DisplayName ) : "Offline";
		}

		return displayName;
	}

	public static string ValidateDisplayName( string playerName )
	{
		return getCleanDisplayName( playerName );
	}

	private static string getCleanDisplayName( string displayName )
	{
		if ( displayName.IsNullOrEmpty() )
			return "";

		string[] splitName = displayName.Split( "+" );
		string cleanDisplayName = "Anonymous";

		if ( splitName.Length == 2 )
		{
			cleanDisplayName = splitName[ 1 ];
		}
		else if ( splitName.Length == 1 )
		{
			cleanDisplayName = displayName;
		}

		return cleanDisplayName;
	}

	public async Task<string> GetInviteCode()
	{
		IApiRpc inviteCodeResult = await FunkCloud.FunkSocket.SendClientRPC( RpcGetInviteID );

		return inviteCodeResult.Payload;
	}

	public async Task<List<MessageDataUser>> FindUser( string usernameSearch )
	{
		RpcDataFindByUsername findByUsernameData = new RpcDataFindByUsername( usernameSearch );

		string findByUsernameJson = findByUsernameData.Serialize();

		IApiRpc inviteCodeResult = await FunkCloud.FunkSocket.SendClientRPC( RpcFindByUsernameID, findByUsernameJson );

		Debug.Log( "FunkCloudUser::FindUser result: " + inviteCodeResult.Payload );

		if ( inviteCodeResult.Payload == "InvalidID" )
		{
			return null;
		}

		List<MessageDataUser> user = inviteCodeResult.Payload.Deserialize<List<MessageDataUser>>();

		return user;
	}

	public async void SetRandomDisplayName()
	{
		await UpdateDisplayName( GenerateRandomName( GetGameUserID() ) );
	}

	public async Task UpdateDisplayName( string displayName )
	{
		if ( GameClient == null || gameSession == null || gameAccount?.User == null )
			return;

		if ( authType == FunkAuthenticationType.Anonymous )
		{
			await GameClient.UpdateAccountAsync( gameSession, gameAccount.User.Username, displayName );
			gameAccount = await GameClient.GetAccountAsync( gameSession );
		}
		else if ( ( authType == FunkAuthenticationType.Email || authType == FunkAuthenticationType.Steam || authType == FunkAuthenticationType.Stove ) && MasterClient != null && masterSession != null && masterAccount?.User != null )
		{
			await GameClient.UpdateAccountAsync( gameSession, gameAccount.User.Username, displayName );
			gameAccount = await GameClient.GetAccountAsync( gameSession );
			await MasterClient.UpdateAccountAsync( masterSession, masterAccount.User.Username, displayName );
			masterAccount = await MasterClient.GetAccountAsync( masterSession );
		}
	}

	private async Task EnsureSessionsRefreshedIfNeeded()
	{
		try
		{
			DateTime now = DateTime.UtcNow;

			if ( gameSession != null )
			{
				if ( gameSession.HasRefreshExpired( now ) )
				{
					Debug.Log( "FunkCloudUser::EnsureSessionsRefreshedIfNeeded - gameSession refresh token expired. Reauthenticating..." );

					authType = FunkAuthenticationType.None;
					_ = Authenticate();
					return;
				}

				long secondsLeft = gameSession.ExpireTime - ( (DateTimeOffset)now ).ToUnixTimeSeconds();
				if ( secondsLeft <= RefreshBeforeExpirySeconds )
				{
					try
					{
						Debug.Log( "FunkCloudUser::EnsureSessionsRefreshedIfNeeded - refreshing game session..." );
						gameSession = await GameClient.SessionRefreshAsync( gameSession );
						gameAccount = await GameClient.GetAccountAsync( gameSession );
						SaveSession();
					}
					catch ( Exception ex )
					{
						Debug.LogError( "FunkCloudUser::EnsureSessionsRefreshedIfNeeded - game refresh failed: " + ex.Message );
					}
				}
			}

			if ( GameInstance.gameDef.authMode == FunkAuthenticationType.Steam || GameInstance.gameDef.authMode == FunkAuthenticationType.Email || GameInstance.gameDef.authMode == FunkAuthenticationType.Stove )
			{
				if ( masterSession != null )
				{
					if ( masterSession.HasRefreshExpired( now ) )
					{
						Debug.Log( "FunkCloudUser::EnsureSessionsRefreshedIfNeeded - masterSession refresh token expired. Reauthenticating..." );
						authType = FunkAuthenticationType.None;
						_ = Authenticate();
						return;
					}

					long secondsLeftMaster = masterSession.ExpireTime - ( (DateTimeOffset)now ).ToUnixTimeSeconds();
					if ( secondsLeftMaster <= RefreshBeforeExpirySeconds )
					{
						try
						{
							Debug.Log( "FunkCloudUser::EnsureSessionsRefreshedIfNeeded - refreshing master session..." );
							masterSession = await MasterClient.SessionRefreshAsync( masterSession );
							masterAccount = await MasterClient.GetAccountAsync( masterSession );
							SaveSession();
						}
						catch ( Exception ex )
						{
							Debug.LogError( "FunkCloudUser::EnsureSessionsRefreshedIfNeeded - master refresh failed: " + ex.Message );
						}
					}
				}
			}
		}
		catch ( Exception ex )
		{
			Debug.LogWarning( "FunkCloudUser::EnsureSessionsRefreshedIfNeeded overall error: " + ex.Message );
		}
	}

	public static string[] firstName = { "Quacky", "Voracious", "Fascinated", "Red", "Fearful", "Charming", "Snobbish", "Envious", "Peaceful", "Encouraging", "Pink", "Holistic", "Useful", "Bright", "Thin", "Wasteful", "Fine", "Scattered", "Perpetual", "Scary", "Grumpy", "Indigo", "Faulty", "Bad", "Leaning", "Yellow", "Accidental", "Military", "Wise", "Adorable", "Worried", "Unkempt", "Gruesome", "Simple", "Violet", "Erratic", "Lucky", "Changeable", "Easy", "Hissing", "Funky", "Action", "Angry", "Cheerful", "Happy", "Gentle", "Curious", "Brave", "Quiet", "Swift", "Calm", "Kindly", "Loyal", "Clever", "Bold", "Nimble", "Sunny", "Mellow", "Patient", "Playful", "Steady", "Witty", "Earnest", "Serene", "Daring" };

	public static string[] lastName = { "Darron", "Chadwick", "Christa", "Ruth", "Jenna", "Lemuel", "Leroy", "Pedro", "Charlene", "Misty", "Wilber", "Dante", "Weston", "Mia", "Patrice", "Claudette", "Delores", "Sonny", "Flossie", "Vernon", "Damien", "Eli", "Tristan", "Val", "Goldie", "Logan", "Frederick", "Nolan", "Duncan", "Carmine", "Franklyn", "Deidre", "Ted", "Shari", "Matilda", "Brandi", "Kristina", "Miles", "Raymon", "Jaime", "Irvin", "Leonel", "Francesca", "Valeria", "Jolene", "Allyson", "Mitchel", "Elizabeth", "Wilton", "Anibal", "Harrison", "Monroe", "Caleb", "Naomi", "Julian", "Rowan", "Evelyn", "Sebastian", "Clara", "Theo", "Margot", "Adrian", "Lydia", "Elena", "Simon", "Beatrice", "Oliver", "Penelope", "Isaac" };

	public static string GenerateRandomName( string guid )
	{
		int seed = new Guid( guid ).GetHashCode();
		System.Random rng = new System.Random( seed );

		string f = firstName[ rng.Next( firstName.Length ) ];
		string l = lastName[ rng.Next( lastName.Length ) ];

		return f + " " + l;
	}

	public async Task SetPrivacyPolicy( bool dataPrivacyAccept )
	{
		FunkCloud.FunkUser.PrivacyPolicyAccepted = dataPrivacyAccept;

		if ( !dataPrivacyAccept )
		{
#if STEAM_BUILD
			if ( GameClient != null && gameSession != null && GameInstance.gameDef != null && GameInstance.gameDef.SteamEnabled && SteamManager.Instance != null )
			{
				string ticket = SteamManager.Instance.GetSteamAuthTicket();
				if ( !string.IsNullOrEmpty( ticket ) )
				{
					await TaskEx.DelayAsync( 100 );
					try
					{
						await GameClient.UnlinkSteamAsync( gameSession, ticket );
					}
					catch ( Exception ex )
					{
						Debug.LogWarning( $"FunkCloudUser::SetPrivacyPolicy - UnlinkSteam failed: {ex.Message}" );
					}
				}
			}
#endif
#if STOVE_BUILD
			if ( GameClient != null && gameSession != null && GameInstance.gameDef != null && GameInstance.gameDef.StoveEnabled && StoveManager.Instance != null && StoveManager.Instance.Initialized && StoveManager.Instance.GameUserId != 0 )
			{
				await TaskEx.DelayAsync( 100 );
				try
				{
					await GameClient.UnlinkCustomAsync( gameSession, StoveManager.Instance.GameUserId.ToString() );
				}
				catch ( Exception ex )
				{
					Debug.LogWarning( $"FunkCloudUser::SetPrivacyPolicy - UnlinkCustom (Stove) failed: {ex.Message}" );
				}
			}
#endif

			await UpdateDisplayName( GenerateRandomName( Guid.NewGuid().ToString() ) );
		}

		ClearUID();
	}

	private void OnDestroy()
	{
		OnUserAuthenticated?.RemoveListener( handleUserAuthenticated );
		if ( FunkCloud.FunkSocket != null )
		{
			FunkCloud.FunkSocket.OnSocketConnected.RemoveListener( handleSocketConnected );
			FunkCloud.FunkSocket.OnSocketDisconnected.RemoveListener( handleSocketDisconnected );
			FunkCloud.FunkSocket.OnSocketGracefulReconnect.RemoveListener( handleGracefulReconnection );
		}
	}
}