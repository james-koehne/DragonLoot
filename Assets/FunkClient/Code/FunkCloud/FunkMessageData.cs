using System;
using System.Collections.Generic;

using Nakama;

using Newtonsoft.Json;

public class MessageDataUser
{
	private const string UserIdKey = "userId";
	private const string UsernameKey = "username";
	private const string DisplayNameKey = "displayName";
	private const string OnlineKey = "online";
	private const string IsAIKey = "isAi";
	private const string MetadataKey = "metadata";

	[JsonProperty( UserIdKey )] public string UserId { get; private set; }
	[JsonProperty( UsernameKey )] public string Username { get; private set; }
	[JsonProperty( DisplayNameKey )] public string DisplayName { get; private set; }
	[JsonProperty( OnlineKey )] public bool Online { get; private set; }
	[JsonProperty( IsAIKey )] public bool IsAI { get; private set; }
	[JsonProperty( MetadataKey )] public Dictionary<string, object> Metadata { get; private set; }

	public void SetFromPresence( IUserPresence presence )
	{
		UserId = presence.UserId;
		Username = presence.Username;
	}

	public void SetFromSession( ISession session )
	{
		UserId = session.UserId;
		Username = session.Username;
		Online = true;
		IsAI = false;
	}

	public void SetAsAI()
	{
		UserId = "46756e6b-4761-6d65-7346-756e6b424f54";
		Username = "FunkBOT";
		DisplayName = "FunkBOT";
		Online = true;
		IsAI = true;
	}

	public void SetFromApiData( IApiUser user )
	{
		Metadata = user.Metadata.Deserialize<Dictionary<string, object>>();
		DisplayName = user.DisplayName;
	}
}

public class RpcDataCreateMatchResponse
{
	private const string SuccessKey = "success";
	private const string MatchIdKey = "matchId";
	private const string UsersKey = "users";

	[JsonProperty( SuccessKey )] public bool Success { get; set; }
	[JsonProperty( MatchIdKey )] public string MatchId { get; set; }
	[JsonProperty( UsersKey )] public MessageDataUser[] Users { get; set; }
}

public class RpcDataAbandonMatch
{
	private const string MatchIdKey = "MatchId";

	[JsonProperty( MatchIdKey )] public string MatchId { get; set; }
}

public class RpcDataCreateSteamMatchResponse
{
	private const string SuccessKey = "success";
	private const string MatchIdKey = "matchId";

	[JsonProperty( SuccessKey )] public bool Success { get; set; }
	[JsonProperty( MatchIdKey )] public string MatchId { get; set; }
}

public class RpcDataCreateMatch
{
	private const string UserIdsKey = "UserIds";
	private const string RankedKey = "ranked";

	[JsonProperty( UserIdsKey )] public string[] UserIds { get; private set; }
	[JsonProperty( RankedKey )] public bool Ranked { get; private set; }

	public RpcDataCreateMatch( string userId, string opponentId, bool ranked )
	{
		UserIds = new string[ 2 ]
		{
			userId,
			opponentId
		};

		Ranked = ranked;
	}
}

public class RpcDataRestartMatch
{
	private const string PlayersKey = "players";
	private const string RankedKey = "ranked";

	[JsonProperty( PlayersKey )] public string[] Players { get; private set; }
	[JsonProperty( RankedKey )] public bool Ranked { get; private set; }

	public RpcDataRestartMatch( List<MessageDataUser> users, bool ranked )
	{
		Players = new string[ 2 ];

		int index = 0;
		foreach ( MessageDataUser user in users )
		{
			Players[ index ] = user.UserId;
			index++;
		}

		Ranked = ranked;
	}
}

public class RpcDataCreateMatchAI
{
	private const string UserIdKey = "UserId";
	private const string ExtraDataKey = "ExtraData";

	[JsonProperty( UserIdKey )] public string UserId { get; private set; }
	[JsonProperty( ExtraDataKey )] public string ExtraData { get; private set; }

	public RpcDataCreateMatchAI( string userId, string extraData = "" )
	{
		UserId = userId;
		ExtraData = extraData;
	}
}

public class RpcDataRabbitEvent
{
	private const string RabbitEventKey = "RabbitEvent";

	[JsonProperty( RabbitEventKey )] public bool RabbitEvent { get; private set; }
}