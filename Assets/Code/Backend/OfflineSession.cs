using System;
using System.Collections.Generic;

using Nakama;

public class OfflineSession : ISession
{
	public string AuthToken => "";

	public bool Created => true;

	public long CreateTime => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

	public long ExpireTime => -1;

	public bool IsExpired => false;

	public bool IsRefreshExpired => false;

	public long RefreshExpireTime => -1;

	public string RefreshToken => "";

	public IDictionary<string, string> Vars => new Dictionary<string, string>();

	public string Username => "Offline";

	public string UserId => "00000000-0000-0000-0000-000000000000";

	public bool HasExpired( DateTime offset )
	{
		return false;
	}

	public bool HasRefreshExpired( DateTime offset )
	{
		return false;
	}
}
