using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

using Nakama;

using UnityEngine;
using UnityEngine.Events;

public class FunkCloudNotifications : MonoBehaviour
{
	public UnityEvent<IApiNotification> OnNotificationReceived = new UnityEvent<IApiNotification>();

	public void Init()
	{
		FunkCloud.FunkSocket.OnSocketConnected.AddListener( handleSocketConnected );
	}

	private void OnDestroy()
	{
		FunkCloud.FunkSocket.OnSocketConnected.RemoveListener( handleSocketConnected );
	}

	public void handleSocketConnected()
	{
		FunkCloud.FunkSocket.Socket.ReceivedNotification += ( notif ) => onReceivedNotification( notif );

		_ = FunkCloud.FunkNotifications.CheckNotifications( true );

		if ( GameInstance.gameDef.FunkBotEnabled )
		{
			FunkClient.Instance.funkBotClient.AddEventListener( FunkBotMessageType.CheckNotifications, ( m ) => { _ = CheckNotifications( false ); } );
		}
	}

	protected virtual async void onReceivedNotification( IApiNotification notification )
	{
		Debug.Log( $"FunkCloudNotifications::onReceivedNotification received {(NotificationCode)notification.Code} notification Data:{notification.Content}" );

		await CheckNotifications( false );
	}

	protected virtual bool isNotificationHandled( IApiNotification notification, bool initialCheck )
	{
		// If null, empty or can't parse, remove it so it doesn't stick around
		if ( !TimeUtil.TryGetAge( notification.CreateTime, out TimeSpan age ) )
			return true;

		// If age of notification is more than 5 minutes then remove it
		if ( age.TotalMinutes > 5 )
			return true;

		return false;
	}

	public async Task CheckNotifications( bool initialCheck )
	{
		await GameMode.Instance.Loaded;

		IApiNotificationList result = await FunkCloud.FunkUser.GameClient.ListNotificationsAsync( FunkCloud.FunkUser.GetGameSession(), 100 );

		List<string> handledNotifications = new List<string>();

		foreach ( IApiNotification notification in result.Notifications )
		{
			if ( isNotificationHandled( notification, initialCheck ) )
			{
				handledNotifications.Add( notification.Id );
			}
		}

		if ( handledNotifications.Count > 0 )
		{
			Debug.Log( $"Matchmaking::CheckNotifications() deleting {handledNotifications.Count} handled/old notifications" );
			await FunkCloud.FunkUser.GameClient.DeleteNotificationsAsync( FunkCloud.FunkUser.GetGameSession(), handledNotifications );
		}

		MainThreadDispatcher.RunOnMainThread( () =>
		{
			GameMode.Instance.InterfaceController.OnNotificationsUpdated();
		} );
	}

	public async void ClearNotification( IApiNotification notification, bool notifyUpdate )
	{
		List<string> handledNotifications = new List<string>
		{
			notification.Id
		};

		Debug.Log( $"Matchmaking::ClearNotification() deleting notifications" );

		await FunkCloud.FunkUser.GameClient.DeleteNotificationsAsync( FunkCloud.FunkUser.GetGameSession(), handledNotifications );

		if ( notifyUpdate )
		{
			MainThreadDispatcher.RunOnMainThread( () =>
			{
				GameMode.Instance.InterfaceController.OnNotificationsUpdated();
			} );
		}
	}
}
