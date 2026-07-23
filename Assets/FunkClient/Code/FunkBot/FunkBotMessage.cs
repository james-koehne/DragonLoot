using Newtonsoft.Json;

using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Events;

public class FunkBotMessageType
{
	public const int Count = 11;
	public const int Empty = 0;
	public const int Authenticate = 1;
	public const int Authorised = 2;
	public const int DiscordAuth = 3;
	public const int DiscordLinked = 4;
	public const int DiscordCheckLink = 5;
	public const int RequestUserInfo = 6;
	public const int RequestMemberInfo = 7;
	public const int RequestFriendsInfo = 8;
	public const int RevokeDiscordAuth = 9;
	public const int CheckNotifications = 10;
}

public class FunkBotMessageError
{
	public const int Count = 8;
	public const int Success = 0;
	public const int AuthenticateError = 1;
	public const int FindByInviteCodeNotFound = 2;
	public const int FindLobbyNotFound = 3;
	public const int JoinLobbyError = 4;
	public const int CreateLobbyError = 5;
	public const int LobbyError = 6;
	public const int Disconnected = 7;
}

public struct FunkBotMessage
{
	public int id;
	public int type;
	public Dictionary<string, object> data;

	[JsonIgnore]
	public UnityAction<FunkBotMessage> handleResponse;

	[JsonIgnore]
	public static Dictionary<int, string> messageErrors = new Dictionary<int, string>()
	{
		{ FunkBotMessageError.Success, "Success" },
		{ FunkBotMessageError.AuthenticateError, "Couldn't authenticate" },
		{ FunkBotMessageError.FindByInviteCodeNotFound, "Couldn't find player" },
		{ FunkBotMessageError.FindLobbyNotFound, "Lobby doesn't exist" },
		{ FunkBotMessageError.JoinLobbyError, "Lobby doesn't exist or is full" },
		{ FunkBotMessageError.CreateLobbyError, "Lobby is full" },
		{ FunkBotMessageError.LobbyError, "Lobby error" },
		{ FunkBotMessageError.Disconnected, "Disconnected from server" }
	};

	[JsonIgnore]
	public static Dictionary<int, string> messageNames = new Dictionary<int, string>()
	{
		{ FunkBotMessageType.Empty, "Empty" },
		{ FunkBotMessageType.Authenticate, "Authenticate" },
		{ FunkBotMessageType.Authorised, "Authorised" },
		{ FunkBotMessageType.DiscordAuth, "DiscordAuth" },
		{ FunkBotMessageType.DiscordLinked, "DiscordLinked" },
		{ FunkBotMessageType.DiscordCheckLink, "DiscordCheckLink" },
		{ FunkBotMessageType.RequestUserInfo, "RequestUserInfo" },
		{ FunkBotMessageType.RequestMemberInfo, "RequestMemberInfo" },
		{ FunkBotMessageType.RequestFriendsInfo, "RequestFriendsInfo" },
		{ FunkBotMessageType.RevokeDiscordAuth, "RevokeDiscordAuth" },
		{ FunkBotMessageType.CheckNotifications, "CheckNotifications" },
	};

	public bool getData<T>( string valIdx, out T val )
	{
		if ( data != null && data.TryGetValue( valIdx, out object dataVal ) )
		{
			// Check if the type of dataVal is compatible with T
			if ( dataVal is T )
			{
				val = (T)dataVal;
				return true;
			}
			else
			{
				try
				{
					// Attempt to convert dataVal to type T
					val = (T)Convert.ChangeType( dataVal, typeof( T ) );
					return true;
				}
				catch ( InvalidCastException )
				{
					Debug.LogError( $"Failed to convert {dataVal.GetType()} to {typeof( T )}" );
				}
			}
		}

		val = default( T );
		return false;
	}

	public bool getData<T>( object dataObj, out T val )
	{
		if ( dataObj != null )
		{
			// Check if the type of dataVal is compatible with T
			if ( dataObj is T )
			{
				val = (T)dataObj;
				return true;
			}
			else
			{
				try
				{
					// Attempt to convert dataVal to type T
					val = (T)Convert.ChangeType( dataObj, typeof( T ) );
					return true;
				}
				catch ( InvalidCastException )
				{
					Debug.LogError( $"Failed to convert {dataObj.GetType()} to {typeof( T )}" );
				}
			}
		}

		val = default( T );
		return false;
	}

	public bool getJsonData<T>( string valIdx, out T val )
	{
		if ( data != null && data.TryGetValue( valIdx, out object dataVal ) && dataVal is string valString )
		{
			val = JsonConvert.DeserializeObject<T>( valString );
			return true;
		}

		val = default( T );
		return false;
	}
}