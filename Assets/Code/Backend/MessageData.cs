#nullable enable

using System;
using System.Collections.Generic;

using Nakama;

using Newtonsoft.Json;

public class RpcDataFindByInviteCode
{
	private const string InviteCodeKey = "InviteCode";

	[JsonProperty( InviteCodeKey )] public string InviteCode { get; private set; }

	public RpcDataFindByInviteCode( string code )
	{
		InviteCode = code;
	}
}

public class RpcDataFindByUsername
{
	private const string UsernameKey = "Username";

	[JsonProperty( UsernameKey )] public string Username { get; private set; }

	public RpcDataFindByUsername( string username )
	{
		Username = username;
	}
}

public class RpcDataNotifyPlayerMatchResponse
{
	private const string MatchIdKey = "matchId";

	[JsonProperty( MatchIdKey )] public string MatchId { get; private set; }

	public RpcDataNotifyPlayerMatchResponse()
	{
		MatchId = "";
	}
}

public class RpcDataNotifyPlayerInviteCodeResponse
{
	private const string InviteCodeKey = "inviteCode";

	[JsonProperty( InviteCodeKey )] public string InviteCode { get; private set; }

	public RpcDataNotifyPlayerInviteCodeResponse()
	{
		InviteCode = "";
	}
}