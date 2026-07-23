#if STEAM_BUILD

using UnityEngine;

using Steamworks;

using System;
using System.Text;

public class SteamManager : MonoBehaviour
{
	public static SteamManager Instance { get; private set; }
	public bool Initialized;

	protected Callback<GameRichPresenceJoinRequested_t> gameRichPresenceJoinRequested;
	protected Callback<LobbyCreated_t> lobbyCreated;
	protected Callback<GameLobbyJoinRequested_t> lobbyJoinRequested;

	protected CSteamID lobbyId;
	private HAuthTicket hAuthTicket;

	void Awake()
	{
		if ( Instance != null )
		{
			Destroy( gameObject );
			return;
		}
		Instance = this;
		DontDestroyOnLoad( gameObject );

		try
		{
			if ( !SteamAPI.Init() )
			{
				Debug.LogError( "SteamAPI_Init failed!" );
				return;
			}
			Initialized = true;
			Debug.Log( "Steam initialized: " + SteamFriends.GetPersonaName() );
		}
		catch ( System.Exception e )
		{
			Debug.LogError( "Steam init exception: " + e );
		}
	}

	void Update()
	{
		if ( Initialized )
			SteamAPI.RunCallbacks();
	}

	private void OnEnable()
	{
		gameRichPresenceJoinRequested = Callback<GameRichPresenceJoinRequested_t>.Create( OnGameRichPresenceJoinRequested );
		lobbyCreated = Callback<LobbyCreated_t>.Create( OnLobbyCreated );
		lobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create( OnGameLobbyJoinRequested );
	}

	void OnDisable()
	{
		if ( Initialized )
		{
			SteamAPI.Shutdown();
			Initialized = false;
		}
	}

	void OnApplicationQuit()
	{
		if ( Initialized )
		{
			SteamAPI.Shutdown();
			Initialized = false;
		}
	}

	public void OpenStorePage()
	{
		bool overlayEnabled = SteamUtils.IsOverlayEnabled();
		if ( overlayEnabled )
		{
			SteamFriends.ActivateGameOverlayToStore( new AppId_t( GameInstance.gameDef.SteamAppID ), EOverlayToStoreFlag.k_EOverlayToStoreFlag_None );
		}
		else
		{
			Application.OpenURL( $"https://store.steampowered.com/app/{GameInstance.gameDef.SteamAppID}/" );
		}
	}

	public string PlayerName => SteamFriends.GetPersonaName();
	public CSteamID PlayerId => SteamUser.GetSteamID();

	public void SetRichPresence( string key, string value ) => SteamFriends.SetRichPresence( key, value );

	public void ClearRichPresence() => SteamFriends.ClearRichPresence();

	public void WriteCloudFile( string filename, byte[] data ) => SteamRemoteStorage.FileWrite( filename, data, data.Length );

	public byte[] ReadCloudFile( string filename )
	{
		if ( !SteamRemoteStorage.FileExists( filename ) )
			return null;
		int size = SteamRemoteStorage.GetFileSize( filename );
		byte[] buffer = new byte[ size ];
		SteamRemoteStorage.FileRead( filename, buffer, size );
		return buffer;
	}

	public void ShowOverlay( string page = "friends" ) => SteamFriends.ActivateGameOverlay( page );

	protected virtual void OnLobbyCreated( LobbyCreated_t lobby )
	{
		if ( lobby.m_eResult == EResult.k_EResultOK )
		{
			lobbyId = new CSteamID( lobby.m_ulSteamIDLobby );
		}
	}

	public void ClearLobby()
	{
		if ( lobbyId.IsLobby() && lobbyId.IsValid() )
		{
			SteamMatchmaking.LeaveLobby( lobbyId );
			lobbyId.Clear();
		}
	}

	protected virtual void OnGameRichPresenceJoinRequested( GameRichPresenceJoinRequested_t pCallback )
	{
		Debug.Log( pCallback.m_rgchConnect );
	}

	protected virtual void OnGameLobbyJoinRequested( GameLobbyJoinRequested_t param )
	{

	}

	public void EndAuthSession()
	{
		SteamUser.EndAuthSession( PlayerId );
	}

	public string GetSteamAuthTicket()
	{
		byte[] ticketBuffer = new byte[ 1024 ];
		uint ticketSize;

		SteamNetworkingIdentity identity = new SteamNetworkingIdentity();
		identity.Clear();

		hAuthTicket = SteamUser.GetAuthSessionTicket( ticketBuffer, ticketBuffer.Length, out ticketSize, ref identity );

		Array.Resize( ref ticketBuffer, (int)ticketSize );

		StringBuilder sb = new StringBuilder( ticketBuffer.Length * 2 );
		foreach ( byte b in ticketBuffer )
			sb.AppendFormat( "{0:x2}", b );

		return sb.ToString();
	}

	public void CancelSteamAuthTicket()
	{
		if ( hAuthTicket != HAuthTicket.Invalid )
		{
			SteamUser.CancelAuthTicket( hAuthTicket );
			hAuthTicket = HAuthTicket.Invalid;
		}
	}
}

#else

using UnityEngine;

/// <summary>Stub when <c>STEAM_BUILD</c> is not defined (Stove-only builds).</summary>
public class SteamManager : MonoBehaviour
{
	public static SteamManager Instance { get; private set; }
	public bool Initialized;
}

#endif
