using NativeWebSocket;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using TMPro;

using UnityEngine;
using UnityEngine.Events;

public class FunkBotClient : MonoBehaviour
{
	private List<UnityAction<FunkBotMessage>>[] onReceivedMessage;
	public UnityEvent OnClientAuthorised = new UnityEvent();

	private WebSocket clientSocket;

	private int currentMessageId = 1;

	private bool isConnected = false;
	private bool isAuthorised = false;

	private Dictionary<int, FunkBotMessage> awaitingReturnMessage = new Dictionary<int, FunkBotMessage>();

	public bool IsConnectedAndAuthorised => isConnected && isAuthorised;

	private float lastConnectionAttempt;

	void Awake()
	{
		// Initialize the array with lists for each message type
		onReceivedMessage = new List<UnityAction<FunkBotMessage>>[ (int)FunkBotMessageType.Count ];
		for ( int i = 0; i < onReceivedMessage.Length; i++ )
		{
			onReceivedMessage[ i ] = new List<UnityAction<FunkBotMessage>>();
		}
	}

	public async void connectToBot()
	{
		if ( clientSocket != null && clientSocket.State == WebSocketState.Open )
		{
			clientSocket.OnError -= onError;

			Debug.Log( "FunkBotClient: Closing existing connection" );
			close();
		}

		string serverUrl = GameInstance.gameDef.botUrl;

		//Debug.Log( "FunkBotClient: Connecting to " + serverUrl + "..." );

		clientSocket = new WebSocket( serverUrl );

		clientSocket.OnOpen += onConnect;
		clientSocket.OnClose += onDisconnect;
		clientSocket.OnError += onError;
		clientSocket.OnMessage += onMessage;

		lastConnectionAttempt = Time.time;

		await clientSocket.Connect();
	}

	private async Task TryConnecting()
	{
		if ( clientSocket != null && lastConnectionAttempt + 10.0f < Time.time )
		{
			lastConnectionAttempt = Time.time + UnityEngine.Random.Range( 0.0f, 20.0f );

			Debug.Log( "FunkBotClient: Connecting to FunkBOT..." );

			await clientSocket.Connect();

			Debug.Log( "FunkBotClient: Connected to FunkBOT" );
		}
	}

	private void Update()
	{
		if ( !isConnected )
		{
			_ = TryConnecting();
		}

#if !UNITY_WEBGL || UNITY_EDITOR
		if ( clientSocket != null )
		{
			clientSocket.DispatchMessageQueue();
		}
#endif
	}

	private void OnApplicationQuit()
	{
		//FunkCloud.MatchMakingService.DisableMatchMaking();

		close();
	}

	public void close()
	{
		isConnected = false;
		isAuthorised = false;

		if ( clientSocket != null )
		{
			_ = clientSocket.Close();
		}
	}

	private void onConnect()
	{
		Debug.Log( "FunkBotClient: Connected" );

		isConnected = true;

		FunkBotMessage authMessage = new FunkBotMessage();
		authMessage.type = FunkBotMessageType.Authenticate;
		authMessage.data = new Dictionary<string, object>
		{
			{ "apiKey", GameInstance.gameDef.botApiKey },
			{ "masterUserId", FunkCloud.FunkUser.GetMasterUserID() },
			{ "gameUserId", FunkCloud.FunkUser.GetGameUserID() },
			{ "gameName", GameInstance.gameDef.gameName },
			{ "displayName", FunkCloud.FunkUser.GetDisplayName() }
		};

		SendEvent( authMessage );
	}

	private void onDisconnect( WebSocketCloseCode closeCode )
	{
		isConnected = false;
		isAuthorised = false;

		if ( GameMode.Instance != null )
		{
			GameMode.Instance.HandleDisconnection();
		}

		DebugUtil.Log( closeCode != WebSocketCloseCode.Normal ? Color.red : new Color( 0.94f, 0.95f, 0.96f ), "FunkBotClient: Disconnected (" + closeCode.ToString() + ")" );
	}

	private void onError( string errorMsg )
	{
		//if ( GameMode.Instance != null )
		//{
		//	GameMode.Instance.interfaceController.ShowError( errorMsg );
		//}

		Debug.LogError( errorMsg );
	}

	private void onMessage( byte[] data )
	{
		try
		{
			string messageData = System.Text.Encoding.UTF8.GetString( data );

			FunkBotMessage socketMessage = JsonConvert.DeserializeObject<FunkBotMessage>( messageData );

			//Debug.Log( "FunkBotClient: message received: (" + FunkBotMessage.messageNames[ socketMessage.type ] + ") " + messageData );

			handleEventReceived( socketMessage );
		}
		catch ( JsonSerializationException ex )
		{
			Debug.LogError( ex.Message );
		}
	}

	private void handleEventReceived( FunkBotMessage message )
	{
		if ( message.id != -1 && awaitingReturnMessage.TryGetValue( message.id, out FunkBotMessage messageHandler ) )
		{
			messageHandler.handleResponse.Invoke( message );
			awaitingReturnMessage.Remove( message.id );
		}

		if ( onReceivedMessage != null && onReceivedMessage.Length > message.type )
		{
			InvokeEventListeners( message.type, message );
		}

		switch ( message.type )
		{
			case FunkBotMessageType.Empty:
				break;

			case FunkBotMessageType.Authenticate:
				break;

			case FunkBotMessageType.Authorised:
				handleAuthorised();
				break;

			default:
				break;
		}
	}

	private void handleAuthorised()
	{
		Debug.Log( "FunkBotClient: Authorised" );

		isAuthorised = true;

		//FunkCloud.FunkMatchmaking.enableMatchmaking();

		OnClientAuthorised.Invoke();
	}

	private async void sendMessage( string message )
	{
		if ( clientSocket.State == WebSocketState.Open )
		{
			Debug.Log( "FunkBotClient: Sending: " + message );

			byte[] encoded = Encoding.UTF8.GetBytes( message );

			await clientSocket.Send( encoded );
		}
	}

	public void SendEvent( FunkBotMessage funkSocketMessage )
	{
		if ( clientSocket.State != WebSocketState.Open )
			return;

		funkSocketMessage.id = currentMessageId;

		string json = JsonConvert.SerializeObject( funkSocketMessage );

		if ( funkSocketMessage.handleResponse != null )
		{
			awaitingReturnMessage.Add( funkSocketMessage.id, funkSocketMessage );
		}

		sendMessage( json );

		currentMessageId++;
	}

	public void AddEventListener( int messageType, UnityAction<FunkBotMessage> listener )
	{
		onReceivedMessage[ messageType ].Add( listener );
	}

	public void RemoveEventListener( int messageType, UnityAction<FunkBotMessage> listener )
	{
		onReceivedMessage[ messageType ].Remove( listener );
	}

	public void ClearEventListener( int messageType )
	{
		onReceivedMessage[ messageType ].Clear();
	}

	public void InvokeEventListeners( int messageType, FunkBotMessage message )
	{
		foreach ( UnityAction<FunkBotMessage> listener in onReceivedMessage[ messageType ] )
		{
			listener.Invoke( message );
		}
	}

	public void SendDiscordAuthMessage( string state, UnityAction<FunkBotMessage> handleResponse )
	{
		if ( !IsConnectedAndAuthorised )
		{
			throw new Exception( "FunkBotClient: Not connected" );
		}

		FunkBotMessage discordAuthMessage = new FunkBotMessage();
		discordAuthMessage.type = FunkBotMessageType.DiscordAuth;
		discordAuthMessage.data = new Dictionary<string, object>
		{
			{ "state", state }
		};

		discordAuthMessage.handleResponse = handleResponse;

		SendEvent( discordAuthMessage );
	}

	public void SendDiscordCheckLinkMessage( UnityAction<FunkBotMessage> handleResponse )
	{
		if ( !IsConnectedAndAuthorised )
		{
			throw new Exception( "FunkBotClient: Not connected" );
		}

		FunkBotMessage discordAuthMessage = new FunkBotMessage();
		discordAuthMessage.type = FunkBotMessageType.DiscordCheckLink;

		discordAuthMessage.handleResponse = handleResponse;

		SendEvent( discordAuthMessage );
	}

	public void SendDiscordRequest( int type, UnityAction<FunkBotMessage> handleResponse )
	{
		if ( !IsConnectedAndAuthorised )
		{
			throw new Exception( "FunkBotClient: Not connected" );
		}

		FunkBotMessage discordAuthMessage = new FunkBotMessage();
		discordAuthMessage.type = type;

		discordAuthMessage.handleResponse = handleResponse;

		SendEvent( discordAuthMessage );
	}
}