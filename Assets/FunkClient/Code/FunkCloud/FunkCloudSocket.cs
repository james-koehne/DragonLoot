using System;
using System.Threading;
using System.Threading.Tasks;

using Nakama;

using UnityEngine;
using UnityEngine.Events;

public class FunkCloudSocket : MonoBehaviour
{
	private const bool AppearOnline = true;
	private const int ConnectionTimeoutSec = 60;

	public const float GracePeriod = 3f;

	private const float KeepAliveInterval = 25f;
	private const float PingInterval = 5f;
	private const float SendTimeout = 3f;

	private const float BackoffBase = 0.5f;
	private const float BackoffMax = 16f;
	private const float BackoffJitter = 0.25f;

	private const int MaxPingFails = 3;

	public UnityEvent OnSocketConnected = new UnityEvent();
	public UnityEvent OnSocketGracefulDisconnect = new UnityEvent();
	public UnityEvent OnSocketGracefulReconnect = new UnityEvent();
	public UnityEvent OnSocketDisconnected = new UnityEvent();

	public ISocket Socket { get; private set; }
	public bool IsConnected => Socket != null && Socket.IsConnected;
	public bool IsConnecting => _state == ConnectionState.Connecting || ( _isConnectingFlag || ( Socket != null && Socket.IsConnecting ) );

	private enum ConnectionState { Idle, Connecting, Connected, Grace, Backoff }
	private volatile ConnectionState _state = ConnectionState.Idle;

	private readonly object _socketLock = new object();
	private volatile bool _isConnectingFlag = false;
	private CancellationTokenSource _connectCts;

	private int _connectionGeneration = 0;

	private float _lastConnectionAttemptTime = 0f;
	private int _reconnectAttempts = 0;

	private bool _connectedFlag = false;
	private bool _graceActive = false;
	private float _graceStartTime = 0f;

	private float _lastKeepAliveTime = 0f;
	private float _lastPingScheduleTime = 0f;

	private int _pingFailCount = 0;

	private bool _requireReconnect = false;

	private ISession _currentSession = null;

	private UnityLogger unityLogger = new UnityLogger();

	public void Init()
	{
		FunkCloudUser.OnUserAuthenticated.AddListener( HandleUserAuthenticated );
	}

	private void OnDestroy()
	{
		FunkCloudUser.OnUserAuthenticated.RemoveListener( HandleUserAuthenticated );
		_ = CleanupAndCloseSocketImmediate();
	}

	private void Update()
	{
		if ( ThreadSafeTime.Now > 1.0f && FunkCloud.FunkUser.IsAuthenticated )
		{
			if ( ( !IsConnected || _requireReconnect ) && !_graceActive && !IsConnecting )
			{
				if ( ThreadSafeTime.Now >= _lastConnectionAttemptTime )
				{
					_ = TryConnectAsync();
				}
			}
		}

		if ( ThreadSafeTime.Now - _lastPingScheduleTime > PingInterval )
		{
			_lastPingScheduleTime = ThreadSafeTime.Now;

			if ( _graceActive )
			{
				_ = TryLivenessProbeDuringGrace();
			}

			if ( IsConnected && !_graceActive )
			{
				if ( FunkCloud.FunkMatchmaking.CurrentMatch != null && ThreadSafeTime.Now - _lastKeepAliveTime > KeepAliveInterval )
				{
					var matchId = FunkCloud.FunkMatchmaking.CurrentMatch.MatchId;
					_ = SendKeepAliveAsync( matchId );
					_lastKeepAliveTime = ThreadSafeTime.Now;
				}
			}
		}

		if ( _graceActive && ThreadSafeTime.Now - _graceStartTime > GracePeriod )
		{
			Debug.Log( "FunkCloudSocket:: Grace period expired, cleaning up socket." );
			_ = CleanupAndCloseSocketImmediate();
		}
	}

	private async Task TryConnectAsync()
	{
		if ( _isConnectingFlag )
			return;

		ISession gameSession = FunkCloud.FunkUser.GetGameSession();
		if ( gameSession == null )
			return;

		_lastConnectionAttemptTime = ThreadSafeTime.Now + UnityEngine.Random.Range( 0.0f, 0.3f );

		await ConnectAsync( gameSession );
	}

	private async Task ConnectAsync( ISession gameSession )
	{
		if ( _isConnectingFlag )
			return;

		_isConnectingFlag = true;
		_connectCts?.Cancel();
		_connectCts = new CancellationTokenSource();
		CancellationToken ct = _connectCts.Token;

		int myGeneration = Interlocked.Increment( ref _connectionGeneration );

		try
		{
			lock ( _socketLock )
			{
				if ( Socket != null && ( Socket.IsConnected || Socket.IsConnecting ) )
				{
					return;
				}
			}

			await CloseExistingSocketAsync();

			if ( ct.IsCancellationRequested )
				return;

			lock ( _socketLock )
			{
				if ( Socket == null )
				{
					Socket = FunkCloud.FunkUser.GameClient.NewSocket( false, GetSocketAdapter( KeepAliveIntervalSec(), (int)SendTimeout ) );
					Socket.Connected += OnSocketConnectedInternal;
					Socket.Closed += OnSocketClosedInternal;
					Socket.ReceivedError += OnSocketErrorInternal;
				}
			}

			_lastConnectionAttemptTime = ThreadSafeTime.Now + UnityEngine.Random.Range( 0.0f, 0.5f );
			_state = ConnectionState.Connecting;

			await Socket.ConnectAsync( gameSession, AppearOnline, ConnectionTimeoutSec );

			if ( myGeneration == _connectionGeneration )
			{
				_reconnectAttempts = 0;
				_state = ConnectionState.Connected;
			}
		}
		catch ( OperationCanceledException )
		{

		}
		catch ( Exception ex )
		{
			Debug.LogWarning( $"FunkCloudSocket:: ConnectAsync failed: {ex}" );
			_reconnectAttempts++;
			float backoff = CalculateBackoff( _reconnectAttempts );
			_lastConnectionAttemptTime = ThreadSafeTime.Now + backoff;
			_state = ConnectionState.Backoff;

			await CloseExistingSocketAsync();
		}
		finally
		{
			_isConnectingFlag = false;
		}
	}

	public async void Reconnect()
	{
		await CloseExistingSocketAsync();

		_requireReconnect = true;
		_lastConnectionAttemptTime = 0;
	}

	private async Task CloseExistingSocketAsync()
	{
		ISocket toClose = null;

		lock ( _socketLock )
		{
			if ( Socket != null )
			{
				toClose = Socket;

				try
				{
					toClose.Connected -= OnSocketConnectedInternal;
					toClose.Closed -= OnSocketClosedInternal;
					toClose.ReceivedError -= OnSocketErrorInternal;
				}
				catch { }

				Socket = null;
			}
		}

		if ( toClose != null )
		{
			try
			{
				await toClose.CloseAsync();
			}
			catch ( Exception ex )
			{
				Debug.LogWarning( $"CloudSocket:: CloseExistingSocketAsync error while closing socket: {ex}" );
			}
		}
	}

	private async Task CleanupAndCloseSocketImmediate()
	{
		_connectCts?.Cancel();

		Interlocked.Increment( ref _connectionGeneration );

		await CloseExistingSocketAsync();

		_connectedFlag = false;
		_graceActive = false;
		_state = ConnectionState.Idle;

		MainThreadDispatcher.RunOnMainThread( () =>
		{
			OnSocketDisconnected?.Invoke();
		} );
	}

	private void OnSocketConnectedInternal()
	{
		_graceActive = false;
		_connectedFlag = true;
		_pingFailCount = 0;
		_lastKeepAliveTime = ThreadSafeTime.Now;
		_state = ConnectionState.Connected;

		Debug.Log( "FunkCloudSocket:: socket connected" );

		MainThreadDispatcher.RunOnMainThread( () =>
		{
			OnSocketConnected?.Invoke();
		} );
	}

	private void OnSocketClosedInternal( string reason )
	{
		Debug.Log( "FunkCloudSocket:: socket closed event = " + reason );

		if ( _connectedFlag && !_graceActive )
		{
			_connectedFlag = false;
			_graceActive = true;
			_graceStartTime = ThreadSafeTime.Now;
			_state = ConnectionState.Grace;

			MainThreadDispatcher.RunOnMainThread( () =>
			{
				OnSocketGracefulDisconnect?.Invoke();
			} );
		}
		else
		{
			if ( !_graceActive )
			{
				MainThreadDispatcher.RunOnMainThread( async () => await CleanupAndCloseSocketImmediate() );
			}
		}
	}

	private void OnSocketErrorInternal( Exception ex )
	{
		Debug.LogError( $"FunkCloudSocket:: socket error: {ex}" );
	}

	private void HandleUserAuthenticated( ISession session )
	{
		_reconnectAttempts = 0;
		_lastConnectionAttemptTime = 0f;

		// Only reconnect when a different user logs in; same user with refreshed tokens should not reconnect
		bool differentUser = _currentSession != null && session.UserId != _currentSession.UserId;
		if ( differentUser )
		{
			Reconnect();
		}

		_currentSession = session;
	}

	private async Task TryLivenessProbeDuringGrace()
	{
		if ( Socket == null )
			return;
		if ( FunkCloud.FunkMatchmaking.CurrentMatch != null )
		{
			try
			{
				using ( var cts = new CancellationTokenSource( TimeSpan.FromSeconds( SendTimeout ) ) )
				{
					await Socket.SendMatchStateAsync( FunkCloud.FunkMatchmaking.CurrentMatch.MatchId, 0, "{}" );

					_pingFailCount = 0;

					if ( _graceActive )
					{
						_graceActive = false;
						_connectedFlag = true;
						MainThreadDispatcher.RunOnMainThread( () => OnSocketGracefulReconnect?.Invoke() );
						_state = ConnectionState.Connected;
					}
				}
			}
			catch ( OperationCanceledException )
			{
				_pingFailCount++;
				Debug.LogWarning( $"FunkCloudSocket:: grace probe timed out ({_pingFailCount}/{MaxPingFails})" );
			}
			catch ( Exception ex )
			{
				_pingFailCount++;
				Debug.LogWarning( $"FunkCloudSocket:: grace probe failed ({_pingFailCount}/{MaxPingFails}): {ex.Message}" );
			}

			if ( _pingFailCount >= MaxPingFails )
			{
				Debug.LogWarning( "FunkCloudSocket:: grace probes failed - assuming socket dead, performing cleanup." );
				await CleanupAndCloseSocketImmediate();
			}
		}
		else
		{
			_lastConnectionAttemptTime = ThreadSafeTime.Now + 0.15f + UnityEngine.Random.Range( 0f, 0.1f );
		}
	}

	private async Task SendKeepAliveAsync( string matchId )
	{
		if ( Socket == null || !Socket.IsConnected )
			return;

		try
		{
			using ( var cts = new CancellationTokenSource( TimeSpan.FromSeconds( SendTimeout ) ) )
			{
				await Socket.SendMatchStateAsync( matchId, 0, "{}" );
			}
		}
		catch ( Exception ex )
		{
			Debug.LogWarning( $"FunkCloudSocket:: keepalive failed: {ex.Message}" );
		}
	}

	private float CalculateBackoff( int attempts )
	{
		float raw = BackoffBase * Mathf.Pow( 2f, Mathf.Max( 0, attempts - 1 ) );
		float capped = Mathf.Min( raw, BackoffMax );
		float jitter = UnityEngine.Random.Range( 0f, BackoffJitter );
		return capped + jitter;
	}

	private int KeepAliveIntervalSec() => Mathf.Max( 5, Mathf.RoundToInt( KeepAliveInterval ) );

	private ISocketAdapter GetSocketAdapter( int keepAliveIntervalSec, int sendTimeoutSec )
	{
#if UNITY_WEBGL && !UNITY_EDITOR
        return new JsWebSocketAdapter();
#elif UNITY_EDITOR
		return new WebSocketAdapter( keepAliveIntervalSec, sendTimeoutSec, logger: unityLogger );
#else
        return new WebSocketAdapter(keepAliveIntervalSec, sendTimeoutSec);
#endif
	}

	public async Task<IApiRpc> SendMasterRPC( string rpc, string payload = "{}" )
	{
		ISession masterSession = FunkCloud.FunkUser.GetMasterSession();
		if ( masterSession.IsExpired || masterSession.IsRefreshExpired )
		{
			await FunkCloud.FunkUser.Authenticate();
		}

		return await FunkCloud.FunkUser.MasterClient.RpcAsync( FunkCloud.FunkUser.GetMasterSession(), rpc, payload );
	}

	public async Task<IApiRpc> SendClientRPC( string rpc, string payload = "{}" )
	{
		ISession gameSession = FunkCloud.FunkUser.GetGameSession();
		if ( gameSession.IsExpired || gameSession.IsRefreshExpired )
		{
			await FunkCloud.FunkUser.Authenticate();
		}

		return await FunkCloud.FunkUser.GameClient.RpcAsync( FunkCloud.FunkUser.GetGameSession(), rpc, payload );
	}
}
