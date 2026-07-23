#if STOVE_BUILD

using System;

using Stove.PCSDK;

using UnityEngine;

/// <summary>
/// Minimal STOVE PC SDK v3 integration: restart check, async initialize, pump callbacks, shutdown.
/// See <see href="https://developers-beta.onstove.com/en/docs/Store/PCSDK3/UNITY/PCSDK_v3_intro">STOVE Unity PC SDK intro</see>.
/// </summary>
public class StoveManager : MonoBehaviour
{
	public static StoveManager Instance { get; private set; }

	public bool Initialized { get; private set; }

	public ulong GameUserId { get; private set; }
	public string PlayerName { get; private set; }

	private bool runCallbacks;
	private bool shutDown;
	private bool gameSupportInitialized;

	private void Awake()
	{
		if ( Instance != null )
		{
			Destroy( gameObject );
			return;
		}

		Instance = this;
		DontDestroyOnLoad( gameObject );

		TryInitialize();
	}

	private void TryInitialize()
	{
		if ( GameInstance.gameDef == null )
		{
			Debug.LogError( "StoveManager: GameInstance.gameDef is null." );
			return;
		}

		GameDefinition def = GameInstance.gameDef;
		if ( string.IsNullOrWhiteSpace( def.StoveGameId ) || string.IsNullOrWhiteSpace( def.StoveApplicationKey ) )
		{
			Debug.LogError( "StoveManager: Set StoveGameId and StoveApplicationKey on the active GameDefinition." );
			return;
		}

		Base.StovePCInitializeParam initParam = new Base.StovePCInitializeParam
		{
			environment = def.StoveEnvironment,
			gameId = def.StoveGameId,
			applicationKey = def.StoveApplicationKey
		};

		try
		{
			if ( Base.Base_RestartAppIfNecessary( initParam ) )
			{
				Debug.Log( "StoveManager: STOVE requested a launcher restart; exiting the process." );
#if UNITY_EDITOR
				UnityEditor.EditorApplication.isPlaying = false;
#else
				Application.Quit();
#endif
				return;
			}

			runCallbacks = true;
			Base.Base_Initialize( initParam, OnInitializeFinished );
		}
		catch ( Exception e )
		{
			Debug.LogError( "StoveManager: init exception: " + e );
			runCallbacks = false;
		}
	}

	private void OnInitializeFinished( Base.CallbackResult cb )
	{
		if ( cb.result.resultCode != (uint)Base.BaseSDKResultCode.SUCCESS )
		{
			Debug.LogError( $"StoveManager: Base_Initialize failed resultCode={cb.result.resultCode} methodCode={cb.result.methodCode} errorMessage={cb.errorMessage} exceptionMessage={cb.result.exceptionMessage}" );
			runCallbacks = false;
			return;
		}

		Initialized = true;
		RefreshUserSnapshot();

		Base.Result gsInit = GameSupport.GameSupport_Initialize();
		if ( gsInit.resultCode != (uint)Base.BaseSDKResultCode.SUCCESS )
		{
			Debug.LogWarning( $"StoveManager: GameSupport_Initialize failed resultCode={gsInit.resultCode}" );
			gameSupportInitialized = false;
		}
		else
		{
			gameSupportInitialized = true;
			StoveAchievement.OnGameSupportInitialized();
		}

		Debug.Log( $"StoveManager: initialized as {PlayerName} (gameUserId={GameUserId})" );
	}

	private void RefreshUserSnapshot()
	{
		Base.StovePCUser user = default;
		Base.Result r = Base.Base_GetUser( ref user );
		if ( r.resultCode != (uint)Base.BaseSDKResultCode.SUCCESS )
		{
			Debug.LogWarning( $"StoveManager: Base_GetUser failed resultCode={r.resultCode}" );
			return;
		}

		GameUserId = user.gameUserId;
		PlayerName = user.nickname ?? string.Empty;
	}

	/// <summary>Launcher access token for server-side validation (e.g. Nakama <c>AuthenticateCustomAsync</c> vars).</summary>
	public bool TryGetAccessToken( out string accessToken )
	{
		accessToken = null;

		if ( !Initialized )
			return false;

		string tokenBuf = string.Empty;
		const uint maxChars = 16384;
		Base.Result r = Base.Base_GetAccessToken( ref tokenBuf, maxChars );
		if ( r.resultCode != (uint)Base.BaseSDKResultCode.SUCCESS || string.IsNullOrEmpty( tokenBuf ) )
			return false;

		accessToken = tokenBuf;
		return true;
	}

	private void Update()
	{
		if ( runCallbacks )
		{
			Base.Base_RunCallback();
		}
	}

	private void OnDisable()
	{
		ShutdownStove();
	}

	private void OnApplicationQuit()
	{
		ShutdownStove();
	}

	private void ShutdownStove()
	{
		if ( shutDown )
			return;

		shutDown = true;
		runCallbacks = false;

		if ( !Initialized )
			return;

		if ( gameSupportInitialized )
		{
			Base.Result gsUn = GameSupport.GameSupport_UnInitialize();
			if ( gsUn.resultCode != (uint)Base.BaseSDKResultCode.SUCCESS )
			{
				Debug.LogWarning( $"StoveManager: GameSupport_UnInitialize returned resultCode={gsUn.resultCode}" );
			}

			gameSupportInitialized = false;
			StoveAchievement.OnGameSupportShutdown();
		}

		Base.Result un = Base.Base_UnInitialize();
		if ( un.resultCode != (uint)Base.BaseSDKResultCode.SUCCESS )
		{
			Debug.LogWarning( $"StoveManager: Base_UnInitialize returned resultCode={un.resultCode}" );
		}

		Initialized = false;
	}
}

#else

using UnityEngine;

/// <summary>Stub when <c>STOVE_BUILD</c> is not defined (Steam-only builds).</summary>
public class StoveManager : MonoBehaviour
{
	public static StoveManager Instance { get; private set; }

	public bool Initialized { get; private set; }

	public ulong GameUserId { get; private set; }
	public string PlayerName { get; private set; }

	public bool TryGetAccessToken( out string accessToken )
	{
		accessToken = null;
		return false;
	}
}

#endif
