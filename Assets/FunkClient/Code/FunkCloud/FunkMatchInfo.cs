using System.Collections;
using System.Collections.Generic;

using UnityEngine;

public class FunkMatchInfo
{
	public string MatchId;
	public bool IsJoined = false;
	public bool Offline = false;
	public bool IsRematch = false;

	protected List<MessageDataUser> players = new List<MessageDataUser>();

	public virtual void Init( string matchId, IEnumerable<MessageDataUser> users )
	{
		this.MatchId = matchId;
		this.players = new List<MessageDataUser>( users );
	}

	public virtual void SetupAI()
	{

	}
}
