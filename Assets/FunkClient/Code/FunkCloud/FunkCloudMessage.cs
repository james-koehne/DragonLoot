using Nakama;

using Newtonsoft.Json;

using System;
using System.IO;
using System.IO.Compression;
using System.Text;

public class FunkCloudMessage
{
	private string json = null;

	public long OpCode { get; private set; }
	public string MatchId { get; private set; }
	public string UserId { get; private set; }

	public FunkCloudMessage( IMatchState matchState )
	{
		OpCode = matchState.OpCode;
		MatchId = matchState.MatchId;

		if ( matchState.UserPresence != null )
		{
			UserId = matchState.UserPresence.UserId;
		}

		Encoding encoding = System.Text.Encoding.UTF8;
		json = encoding.GetString( matchState.State );
	}

	public FunkCloudMessage( int opCode, string matchId, string data, string sender = "" )
	{
		OpCode = opCode;
		MatchId = matchId;

		if ( !string.IsNullOrEmpty( sender ) )
		{
			UserId = sender;
		}

		json = data;
	}

	public T GetData<T>()
	{
		return json.Deserialize<T>();
	}
}

public class FunkCloudReliableMessage
{
	public int MessageId;
	[JsonIgnore] public long OpCode;
	[JsonIgnore] public float TimeLastSent;
	[JsonIgnore] public string MatchId;
	public string MessageData;
}