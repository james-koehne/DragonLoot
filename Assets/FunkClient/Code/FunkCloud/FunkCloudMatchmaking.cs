using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Nakama;

using System.Text;

using UnityEngine;
using UnityEngine.Localization.Settings;

// Later: Change matches to be a list of lobbies that haven't started yet, for example when a player has challenged someone but they haven't seen the challenge yet
public class FunkCloudMatchmaking : MonoBehaviour
{
	public bool isMatchmaking = false;

	public bool IsInMatch { get => CurrentMatch != null; }
	public FunkMatchInfo CurrentMatch = null;

	private Dictionary<long, Action<FunkCloudMessage>> onReceiveData = new Dictionary<long, Action<FunkCloudMessage>>();

#nullable enable
	private IMatchmakerTicket? matchmakerTicket;
#nullable disable

	/// <summary>
	/// Incremented only in <see cref="StopMatchmaking"/>. After a ticket is obtained we store the value
	/// in <see cref="matchmakerTicketCancelBaseline"/>; the matched callback joins only while it still equals
	/// the current generation (user did not cancel).
	/// </summary>
	private int matchmakerCancelGeneration;
	private int matchmakerTicketCancelBaseline;

	private const int MatchmakerDeclineNotifySettleMilliseconds = 2500;

	private int reliableSequenceId = 0;
	private Dictionary<int, FunkCloudReliableMessage> unackedMessages = new Dictionary<int, FunkCloudReliableMessage>();
	private List<int> resendKeys = new List<int>();

	private Queue<IMatchState> pendingMessages = new Queue<IMatchState>();
	private bool matchReady = false;

	public virtual void Init()
	{
		FunkCloud.FunkSocket.OnSocketConnected.AddListener( handleSocketConnected );
		FunkCloud.FunkSocket.OnSocketDisconnected.AddListener( handleSocketDisconnected );
		FunkCloud.FunkSocket.OnSocketGracefulDisconnect.AddListener( handleSocketDisconnected );
		FunkCloud.FunkSocket.OnSocketGracefulReconnect.AddListener( handleSocketConnected );
	}

	private void OnDestroy()
	{
		FunkCloud.FunkSocket.OnSocketConnected.RemoveListener( handleSocketConnected );
		FunkCloud.FunkSocket.OnSocketDisconnected.RemoveListener( handleSocketDisconnected );
		FunkCloud.FunkSocket.OnSocketGracefulDisconnect.RemoveListener( handleSocketDisconnected );
		FunkCloud.FunkSocket.OnSocketGracefulReconnect.RemoveListener( handleSocketConnected );
	}

	protected virtual void handleSocketConnected()
	{
		if ( CurrentMatch != null )
		{
			// TODO: Ideally we would want to rejoin the match and continue playing, but that requires a bit more work IE: Match state rebuilding at any point and server handling this
			//rejoinMatchAsync( CurrentMatch );

			clearMatch();
		}

		FunkCloud.FunkSocket.Socket.ReceivedMatchmakerMatched += handleReceivedMatchmakerMatchedAsync;
		FunkCloud.FunkSocket.Socket.ReceivedMatchPresence += handleReceivedMatchPresence;

		FunkCloud.FunkSocket.Socket.ReceivedMatchState += handleReceiveMatchState;
	}

	protected virtual void handleSocketDisconnected()
	{
		MainThreadDispatcher.RunOnMainThread( () =>
		{
			if ( CurrentMatch != null )
			{
				CurrentMatch.IsJoined = false;
			}
		} );
	}

	private void Update()
	{
		if ( FunkCloud.FunkSocket.IsConnected && FunkCloud.FunkMatchmaking != null && FunkCloud.FunkMatchmaking.CurrentMatch != null && FunkCloud.FunkMatchmaking.CurrentMatch.IsJoined )
		{
			foreach ( KeyValuePair<int, FunkCloudReliableMessage> unackedMsg in unackedMessages )
			{
				if ( GameInstance.CachedTime - unackedMsg.Value.TimeLastSent > 2.0f )
				{
					resendKeys.Add( unackedMsg.Key );
				}
			}

			foreach ( int key in resendKeys )
			{
				FunkCloudReliableMessage resendMessage = unackedMessages[ key ];

				_ = ResendReliable( resendMessage );
			}

			resendKeys.Clear();
		}
	}

	public virtual void LeaveCurrentMatch( bool sendCloseMatch = true, string matchId = "" )
	{
		bool willLeaveSocket = sendCloseMatch && FunkCloud.FunkSocket.IsConnected && ( ( CurrentMatch != null && !CurrentMatch.Offline && CurrentMatch.IsJoined ) || matchId != string.Empty );

		if ( GameMode.Instance.CoreDefinition.DebugDefinition.debugBackend )
		{
			string effectiveId = matchId != string.Empty ? matchId : ( CurrentMatch != null ? CurrentMatch.MatchId : "" );
			Debug.Log( $"FunkCloudMatchmaking: LeaveCurrentMatch MatchId=\"{effectiveId}\" willCallLeaveMatchAsync={willLeaveSocket} socketConnected={FunkCloud.FunkSocket.IsConnected} currentMatch={( CurrentMatch == null ? "null" : $"joined={CurrentMatch.IsJoined}" )}" );
		}

		if ( willLeaveSocket )
		{
			//_ = FunkCloud.FunkMatchmaking.Send( (long)OpCode.CancelMatch );

			LeaveMatchAsync( matchId == string.Empty ? CurrentMatch.MatchId : matchId );
		}

		cleanupMatch();
	}

	protected async void clearMatch()
	{
		GameMode.Instance.LeaveCurrentMatch();

		UIController nextUi = UIController.TopMostUIController.PeekSecond();

		if ( await GameMode.Instance.InterfaceController.Quit( nextUi.State ) )
		{
			UIController.PopTopUIController();
		}
	}

	private void cleanupMatch()
	{
		CurrentMatch = null;
		matchReady = false;

		unackedMessages.Clear();
		resendKeys.Clear();
		pendingMessages.Clear();
	}

	public void StartMatchmaking()
	{
		if ( FunkCloud.FunkSocket.IsConnected )
		{
			_ = startMatchmaking();
		}
	}

	public void StartAIMatch( string extraJsonData = "" )
	{
		if ( FunkCloud.FunkSocket.IsConnected )
		{
			CreateMatchAI( extraJsonData );
		}
	}

	private async Task startMatchmaking()
	{
		isMatchmaking = true;

		try
		{
			string query = "*";

			// Send off matchmaking ticket
			matchmakerTicket = await FunkCloud.FunkSocket.Socket.AddMatchmakerAsync( query, 2, 2 );
			Volatile.Write( ref matchmakerTicketCancelBaseline, Volatile.Read( ref matchmakerCancelGeneration ) );
		}
		catch ( Exception e )
		{
			Debug.LogError( "FunkCloudMatchmaking: Error StartMatchmaking: " + e.Message );
		}
	}

	public async void StopMatchmaking()
	{
		Interlocked.Increment( ref matchmakerCancelGeneration );

		if ( matchmakerTicket != null && !matchmakerTicket.Ticket.IsNullOrEmpty() )
		{
			string ticketId = matchmakerTicket.Ticket;
			matchmakerTicket = null;

			await FunkCloud.FunkSocket.Socket.RemoveMatchmakerAsync( ticketId );
		}

		isMatchmaking = false;
	}

	protected virtual FunkMatchInfo BuildMatchInfo( string matchId, IEnumerable<MessageDataUser> users )
	{
		FunkMatchInfo matchInfo = new FunkMatchInfo();
		matchInfo.Init( matchId, users );

		return matchInfo;
	}

	protected virtual FunkInstance BuildFunkInstance( FunkMatchInfo funkMatchInfo )
	{
		FunkInstance funkInstance = new FunkInstance();
		funkInstance.Setup( funkMatchInfo );

		return funkInstance;
	}

	protected virtual void StartMatch( FunkMatchInfo matchInfo, FunkInstance funkInstance, bool isRematch = false )
	{

	}

	protected virtual void RejoinMatch( FunkMatchInfo matchInfo )
	{

	}

	// TODO: Include metadata that has deck and champion to send to server
	public void JoinMatch( string matchId, MessageDataUser[] users )
	{
		joinMatchAsync( matchId, users );
	}

	private async void joinMatchAsync( string matchId, MessageDataUser[] users )
	{
		if ( matchId == "00000000-0000-0000-0000-000000000000" )
			return;

		// Create matchInfo here first, then load in data related to it when joining it
		CurrentMatch = BuildMatchInfo( matchId, users );

		FunkInstance funkInstance = BuildFunkInstance( CurrentMatch );

		await TaskEx.DelayAsync( 100 );

		try
		{
			await FunkCloud.FunkSocket.Socket.JoinMatchAsync( matchId, BuildPlayerMetaData() );
		}
		catch ( Exception e )
		{
			Debug.LogError( "FunkCloudMatchmaking: Error joinMatchAsync: " + e.Message );

			clearMatch();

			GameMode.Instance.InterfaceController.ShowError( LocalizationSettings.StringDatabase.GetLocalizedString( "menu_error" ) );

			return;
		}

		CurrentMatch.IsJoined = true;

		StartMatch( CurrentMatch, funkInstance );
	}

	private async void rejoinMatchAsync( FunkMatchInfo currentMatch )
	{
		await TaskEx.DelayAsync( 100 );

		await TaskEx.WaitUntil( () => FunkCloud.FunkSocket.IsConnected );

		try
		{
			await FunkCloud.FunkSocket.Socket.JoinMatchAsync( currentMatch.MatchId, BuildPlayerMetaData() );
		}
		catch ( Exception e )
		{
			Debug.LogError( "FunkCloudMatchmaking: Error rejoinMatchAsync: " + e.Message );

			clearMatch();

			GameMode.Instance.InterfaceController.ShowError( LocalizationSettings.StringDatabase.GetLocalizedString( "menu_error" ) );

			return;
		}

		currentMatch.IsJoined = true;

		RejoinMatch( currentMatch );
	}

	public void CreateMatch( string opponentId, bool ranked )
	{
		createMatchAsync( opponentId, ranked );
	}

	private async void createMatchAsync( string opponentId, bool ranked )
	{
		RpcDataCreateMatch createMatchData = new RpcDataCreateMatch( FunkCloud.FunkUser.GetGameUserID(), opponentId, ranked );

		string createMatchJson = createMatchData.Serialize();

		IApiRpc rpcResult = await FunkCloud.FunkSocket.SendClientRPC( FunkCloudRpc.RpcCreateMatchID, createMatchJson );

		//Debug.Log( $"Matchmaking::createMatchAsync result: [{rpcResult.Payload}]" );

		// Handle result, join the created match using the resulting matchId
		RpcDataCreateMatchResponse response = rpcResult.Payload.Deserialize<RpcDataCreateMatchResponse>();

		// Create matchInfo here first, then load in data related to it when joining it
		CurrentMatch = BuildMatchInfo( response.MatchId, response.Users );

		FunkInstance funkInstance = BuildFunkInstance( CurrentMatch );

		await TaskEx.DelayAsync( 100 );

		try
		{
			await FunkCloud.FunkSocket.Socket.JoinMatchAsync( response.MatchId, BuildPlayerMetaData() );
		}
		catch ( Exception e )
		{
			Debug.LogError( "FunkCloudMatchmaking: Error createMatchAsync: " + e.Message );

			clearMatch();

			GameMode.Instance.InterfaceController.ShowError( LocalizationSettings.StringDatabase.GetLocalizedString( "menu_error" ) );

			return;
		}

		CurrentMatch.IsJoined = true;

		StartMatch( CurrentMatch, funkInstance );
	}

	public async Task<string> CreateSteamMatchAsync()
	{
		RpcDataCreateMatch createMatchData = new RpcDataCreateMatch( FunkCloud.FunkUser.GetGameUserID(), "steam", false );

		string createMatchJson = createMatchData.Serialize();

		IApiRpc rpcResult = await FunkCloud.FunkSocket.SendClientRPC( FunkCloudRpc.RpcCreateSteamMatchID, createMatchJson );

		RpcDataCreateSteamMatchResponse response = rpcResult.Payload.Deserialize<RpcDataCreateSteamMatchResponse>();

		CurrentMatch = BuildMatchInfo( response.MatchId, new MessageDataUser[ 0 ] );

		FunkInstance funkInstance = BuildFunkInstance( CurrentMatch );

		await TaskEx.DelayAsync( 100 );

		try
		{
			await FunkCloud.FunkSocket.Socket.JoinMatchAsync( response.MatchId, BuildPlayerMetaData() );
		}
		catch ( Exception e )
		{
			Debug.LogError( "FunkCloudMatchmaking: Error createMatchAsync: " + e.Message );

			clearMatch();

			GameMode.Instance.InterfaceController.ShowError( LocalizationSettings.StringDatabase.GetLocalizedString( "menu_error" ) );

			return "Error";
		}

		CurrentMatch.IsJoined = true;

		StartMatch( CurrentMatch, funkInstance );

		return response.MatchId;
	}

	public void CreateMatchAI( string extraJsonData = "" )
	{
		createMatchAIAsync( extraJsonData );
	}

	private async void createMatchAIAsync( string extraJsonData = "" )
	{
		object createMatchData = new RpcDataCreateMatchAI( FunkCloud.FunkUser.GetGameUserID(), extraJsonData );

		string createMatchJson = createMatchData.Serialize();

		IApiRpc rpcResult = await FunkCloud.FunkSocket.SendClientRPC( FunkCloudRpc.RpcCreateMatchAIID, createMatchJson );

		//Debug.Log( $"Matchmaking::createMatchAIAsync result: [{rpcResult.Payload}]" );

		// Handle result, join the created match using the resulting matchId
		RpcDataCreateMatchResponse response = rpcResult.Payload.Deserialize<RpcDataCreateMatchResponse>();

		// Create matchInfo here first, then load in data related to it when joining it
		CurrentMatch = BuildMatchInfo( response.MatchId, response.Users );
		CurrentMatch.SetupAI();

		FunkInstance funkInstance = BuildFunkInstance( CurrentMatch );

		await TaskEx.DelayAsync( 100 );

		try
		{
			await FunkCloud.FunkSocket.Socket.JoinMatchAsync( response.MatchId, BuildPlayerMetaData() );
		}
		catch ( Exception e )
		{
			Debug.LogError( "FunkCloudMatchmaking: Error createMatchAIAsync: " + e.Message );

			clearMatch();

			GameMode.Instance.InterfaceController.ShowError( LocalizationSettings.StringDatabase.GetLocalizedString( "menu_error" ) );

			return;
		}

		CurrentMatch.IsJoined = true;

		StartMatch( CurrentMatch, funkInstance );
	}

	public async Task CreateMatchByInviteCode( string inviteCode )
	{
		RpcDataFindByInviteCode rpcDataFindByInviteCode = new RpcDataFindByInviteCode( inviteCode );

		string findByInviteCodeJson = rpcDataFindByInviteCode.Serialize();

		IApiRpc rpcResult = await FunkCloud.FunkSocket.SendClientRPC( FunkCloudRpc.RpcFindByInviteCodeID, findByInviteCodeJson );

		//Debug.Log( $"Matchmaking::createMatchByInviteCode result: [{rpcResult.Payload}]" );

		string playerId = rpcResult.Payload;

		if ( playerId == "InvalidID" )
		{
			GameMode.Instance.InterfaceController.ShowError( LocalizationSettings.StringDatabase.GetLocalizedString( "error_invite_code" ) );
			return;
		}

		// Handle finding self player ID
		if ( playerId == FunkCloud.FunkUser.GetGameUserID() )
		{
			GameMode.Instance.InterfaceController.ShowError( LocalizationSettings.StringDatabase.GetLocalizedString( "error_cant_battle_self" ) );
		}
		else
		{
			// Send match start using the player's ID
			FunkCloud.FunkMatchmaking.CreateMatch( playerId, false );
		}
	}

	public void RestartMatch( List<MessageDataUser> users, bool ranked )
	{
		restartMatchAsync( users, ranked );
	}

	protected async void restartMatchAsync( List<MessageDataUser> userPresences, bool ranked )
	{
		object createMatchData = new RpcDataRestartMatch( userPresences, ranked );

		string createMatchJson = createMatchData != null ? createMatchData.Serialize() : string.Empty;

		IApiRpc rpcResult = await FunkCloud.FunkSocket.SendClientRPC( FunkCloudRpc.RpcRestartMatchID, createMatchJson );

		Debug.Log( $"Matchmaking::restartMatchAsync result: [{rpcResult.Payload}]" );

		// Handle result, join the created match using the resulting matchId
		RpcDataCreateMatchResponse response = rpcResult.Payload.Deserialize<RpcDataCreateMatchResponse>();

		// Create matchInfo here first, then load in data related to it when joining it
		CurrentMatch = BuildMatchInfo( response.MatchId, response.Users );

		FunkInstance funkInstance = BuildFunkInstance( CurrentMatch );

		await TaskEx.DelayAsync( 100 );

		try
		{
			await FunkCloud.FunkSocket.Socket.JoinMatchAsync( response.MatchId, BuildPlayerMetaData() );
		}
		catch ( Exception e )
		{
			Debug.LogError( "FunkCloudMatchmaking: Error restartMatchAsync: " + e.Message );

			clearMatch();

			GameMode.Instance.InterfaceController.ShowError( LocalizationSettings.StringDatabase.GetLocalizedString( "menu_error" ) );

			return;
		}

		CurrentMatch.IsJoined = true;

		StartMatch( CurrentMatch, funkInstance, isRematch: true );
	}

	private bool matchmakerJoinStillWanted( int cancelBaseline )
	{
		return cancelBaseline == Volatile.Read( ref matchmakerCancelGeneration );
	}

	/// <summary>
	/// Nakama only delivers match messages to clients that have joined. If we decline after a match id exists,
	/// we join briefly, broadcast <see cref="OpCode.CancelMatch"/>, then leave so the other player can exit matchmaking.
	/// A server-side authoritative cleanup is still the most reliable if a peer never joins in time.
	/// </summary>
	private async Task tryNotifyPeersMatchmakerDeclinedAsync( string matchId, bool weAreAlreadyInMatch )
	{
		if ( matchId == "00000000-0000-0000-0000-000000000000" )
			return;

		try
		{
			if ( !weAreAlreadyInMatch )
			{
				await FunkCloud.FunkSocket.Socket.JoinMatchAsync( matchId, BuildPlayerMetaData() );
			}

			await TaskEx.DelayAsync( MatchmakerDeclineNotifySettleMilliseconds );

			#if BBM
				await FunkCloud.FunkMatchmaking.Send( (long)OpCode.CancelMatch, matchId: matchId );
			#endif

			await TaskEx.DelayAsync( 150 );

			if ( !weAreAlreadyInMatch )
			{
				LeaveCurrentMatch( matchId: matchId );
			}
			else
			{
				MainThreadDispatcher.RunOnMainThread( () => GameMode.Instance.LeaveCurrentMatch() );
			}
		}
		catch ( Exception e )
		{
			Debug.LogWarning( "FunkCloudMatchmaking: tryNotifyPeersMatchmakerDeclinedAsync: " + e.Message );
		}
	}

	private async void handleReceivedMatchmakerMatchedAsync( IMatchmakerMatched foundMatch )
	{
		int cancelBaselineForThisTicket = Volatile.Read( ref matchmakerTicketCancelBaseline );

		isMatchmaking = false;
		matchmakerTicket = null;

		// Join found match
		Debug.Log( $"FunkCloudMatchmaking::handleReceivedMatchmakerMatchedAsync Received: {foundMatch}" );

		string opponents = "";

		foreach ( IMatchmakerUser user in foundMatch.Users )
		{
			opponents += user.Presence.Username + " ";
		}

		Debug.Log( $"FunkCloudMatchmaking::handleReceivedMatchmakerMatchedAsync Matched opponents: [{opponents}]" );

		List<MessageDataUser> users = new List<MessageDataUser>();
		List<string> userIds = new List<string>();

		foreach ( IMatchmakerUser user in foundMatch.Users )
		{
			MessageDataUser matchmakeUser = new MessageDataUser();
			matchmakeUser.SetFromPresence( user.Presence );

			users.Add( matchmakeUser );
			userIds.Add( matchmakeUser.UserId );
		}

		IApiUsers apiUsers = await FunkCloud.FunkUser.GameClient.GetUsersAsync( FunkCloud.FunkUser.GetGameSession(), userIds );

		if ( !matchmakerJoinStillWanted( cancelBaselineForThisTicket ) )
		{
			Debug.Log( "FunkCloudMatchmaking::handleReceivedMatchmakerMatchedAsync Ignoring match: matchmaking was stopped after this match was found." );
			await tryNotifyPeersMatchmakerDeclinedAsync( foundMatch.MatchId, weAreAlreadyInMatch: false );
			return;
		}

		foreach ( IApiUser user in apiUsers.Users )
		{
			MessageDataUser dataUser = users.Find( ( u ) => u.UserId == user.Id );

			if ( dataUser != null )
			{
				dataUser.SetFromApiData( user );
			}
		}

		if ( !matchmakerJoinStillWanted( cancelBaselineForThisTicket ) )
		{
			Debug.Log( "FunkCloudMatchmaking::handleReceivedMatchmakerMatchedAsync Ignoring match: matchmaking was stopped after user lookup." );
			await tryNotifyPeersMatchmakerDeclinedAsync( foundMatch.MatchId, weAreAlreadyInMatch: false );
			return;
		}

		// Create matchInfo here first, then load in data related to it when joining it
		CurrentMatch = BuildMatchInfo( foundMatch.MatchId, users );

		FunkInstance funkInstance = BuildFunkInstance( CurrentMatch );

		try
		{
			await FunkCloud.FunkSocket.Socket.JoinMatchAsync( foundMatch.MatchId, BuildPlayerMetaData() );
		}
		catch ( Exception e )
		{
			Debug.LogError( "FunkCloudMatchmaking: Error handleReceivedMatchmakerMatchedAsync: " + e.Message );

			clearMatch();

			GameMode.Instance.InterfaceController.ShowError( LocalizationSettings.StringDatabase.GetLocalizedString( "menu_error" ) );

			return;
		}

		if ( !matchmakerJoinStillWanted( cancelBaselineForThisTicket ) )
		{
			Debug.Log( "FunkCloudMatchmaking::handleReceivedMatchmakerMatchedAsync Leaving match: matchmaking was stopped; join completed but session was cancelled." );
			await tryNotifyPeersMatchmakerDeclinedAsync( foundMatch.MatchId, weAreAlreadyInMatch: true );
			CurrentMatch = null;
			matchReady = false;
			return;
		}

		CurrentMatch.IsJoined = true;

		StartMatch( CurrentMatch, funkInstance );
	}

	private void handleReceivedMatchPresence( IMatchPresenceEvent matchPresence )
	{
		//Debug.Log( $"FunkCloudMatchmaking::handleReceivedMatchPresence Received: {matchPresence}" );
	}

	public async void LeaveMatchAsync( string matchId )
	{
		await FunkCloud.FunkSocket.Socket.LeaveMatchAsync( matchId );
	}

	public async Task Send( long code, object data = null, string matchId = "" )
	{
		if ( CurrentMatch == null && matchId == string.Empty )
			return;

		await TaskEx.WaitUntil( () => FunkCloud.FunkSocket.IsConnected && ( CurrentMatch != null && CurrentMatch.IsJoined || matchId != string.Empty ) );

		string json = data != null ? data.Serialize() : string.Empty;

		OpCode opCode = (OpCode)code;

		if ( GameMode.Instance.CoreDefinition.DebugDefinition.debugLogging )
		{
			Debug.Log( $"FunkCloudMatchmaking:Send message code={opCode} data={json}" );
		}

		_ = FunkCloud.FunkSocket.Socket.SendMatchStateAsync( matchId == string.Empty ? CurrentMatch.MatchId : matchId, code, json );
	}

	public async Task<int> SendReliable( long code, object data = null, string matchId = "" )
	{
		if ( CurrentMatch == null && matchId == string.Empty )
			return -1;

		await TaskEx.WaitUntil( () => FunkCloud.FunkSocket.IsConnected && ( CurrentMatch != null && CurrentMatch.IsJoined || matchId != string.Empty ) );

		reliableSequenceId++;

		FunkCloudReliableMessage reliableMessage = new FunkCloudReliableMessage()
		{
			MessageId = reliableSequenceId,
			TimeLastSent = GameInstance.CachedTime,
			OpCode = code,
			MessageData = data != null ? data.Serialize() : string.Empty,
			MatchId = matchId == string.Empty ? CurrentMatch.MatchId : matchId
		};

		string json = reliableMessage.Serialize();

		OpCode opCode = (OpCode)reliableMessage.OpCode;

		if ( GameMode.Instance.CoreDefinition.DebugDefinition.debugLogging )
		{
			Debug.Log( $"FunkCloudMatchmaking:Send reliable message code={opCode} msgId={reliableMessage.MessageId} data={json}" );
		}

		_ = FunkCloud.FunkSocket.Socket.SendMatchStateAsync( reliableMessage.MatchId, code, json );

		reliableMessage.TimeLastSent = GameInstance.CachedTime;
		unackedMessages[ reliableMessage.MessageId ] = reliableMessage;

		return reliableMessage.MessageId;
	}

	public async Task ResendReliable( FunkCloudReliableMessage reliableMessage )
	{
		if ( CurrentMatch == null && reliableMessage.MatchId == string.Empty )
			return;

		await TaskEx.WaitUntil( () => FunkCloud.FunkSocket.IsConnected && ( CurrentMatch != null && CurrentMatch.IsJoined || reliableMessage.MatchId != string.Empty ) );

		string json = reliableMessage.Serialize();

		OpCode opCode = (OpCode)reliableMessage.OpCode;

		//Debug.Log( $"FunkCloudMatchmaking:RESEND reliable message code={opCode} msgId={reliableMessage.MessageId} data={json}" );

		_ = FunkCloud.FunkSocket.Socket.SendMatchStateAsync( reliableMessage.MatchId, reliableMessage.OpCode, json );

		reliableMessage.TimeLastSent = GameInstance.CachedTime;
	}

	public bool IsReliableMessageAcked( int messageId )
	{
		if ( unackedMessages.TryGetValue( messageId, out _ ) )
		{
			return false;
		}

		return true;
	}

	private void handleReceiveMatchState( IMatchState newState )
	{
		MainThreadDispatcher.RunOnMainThread( () => handleReceiveMatchStateMain( newState ) );
	}

	public void SetMatchReady()
	{
		matchReady = true;
		ProcessPendingMessages();
	}

	private void ProcessPendingMessages()
	{
		while ( pendingMessages.Count > 0 )
		{
			IMatchState state = pendingMessages.Dequeue();
			processMatchState( state );
		}
	}

	private void handleReceiveMatchStateMain( IMatchState newState )
	{
		if ( !matchReady )
		{
			pendingMessages.Enqueue( newState );
			return;
		}

		processMatchState( newState );
	}

	private void processMatchState( IMatchState newState )
	{
		string json = Encoding.UTF8.GetString( newState.State );
		OpCode opCode = (OpCode)newState.OpCode;

		if ( GameMode.Instance.CoreDefinition.DebugDefinition.debugLogging )
		{
			Debug.Log( $"FunkCloudMatchmaking:Receive message code={opCode} data={json}" );
		}

		FunkCloudMessage msg = new FunkCloudMessage( newState );

		if ( opCode == OpCode.Ack )
		{
			int ackMessageId = msg.GetData<int>();

			unackedMessages.Remove( ackMessageId );
		}
		else
		{
			if ( onReceiveData.ContainsKey( msg.OpCode ) )
			{
				onReceiveData[ msg.OpCode ]?.Invoke( msg );
			}
			else
			{
				if ( GameMode.Instance.CoreDefinition.DebugDefinition.debugLogging )
				{
					Debug.LogError( $"FunkCloudMatchmaking:handleReceiveMatchStateMain - Received message but no subscriber to receive message" );
				}
			}
		}
	}

	public void Subscribe( long code, Action<FunkCloudMessage> action )
	{
		if ( !onReceiveData.ContainsKey( code ) )
			onReceiveData.Add( code, null );

		onReceiveData[ code ] += action;
	}

	public void Unsubscribe( long code, Action<FunkCloudMessage> action )
	{
		if ( onReceiveData.ContainsKey( code ) )
			onReceiveData[ code ] -= action;
	}

	public virtual Dictionary<string, string> BuildPlayerMetaData()
	{
		return null;
	}
}