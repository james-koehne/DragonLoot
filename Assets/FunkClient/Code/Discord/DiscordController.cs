#if DISCORD_ENABLED
using Dissonity;

using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

using UnityEngine;
using UnityEngine.Events;

[Serializable]
public class DiscordUser
{
	public string username;
	public string id;
	public bool bot;
#nullable enable
	public AvatarDecoration? avatar_decoration_data;
	public string? global_name;
	public string? avatar;
	public int? flags;
	public int? premium_type;
#nullable disable

	public string display_name
	{
		get
		{
			if ( global_name != null )
			{
				return global_name;
			}

			return username;
		}
	}

	public void CopyFromDissonity( User data )
	{
		username = data.username;
		id = data.id;
		bot = data.bot;
		avatar_decoration_data = data.avatar_decoration_data;
		global_name = data.global_name;
		avatar = data.avatar;
		flags = data.flags;
		premium_type = data.premium_type;
	}
}

[Serializable]
public class DiscordParticipant : DiscordUser
{
#nullable enable
	public string? nickname;
#nullable disable

	public void CopyFromDissonity( Participant data )
	{
		username = data.username;
		id = data.id;
		bot = data.bot;
		avatar_decoration_data = data.avatar_decoration_data;
		global_name = data.global_name;
		avatar = data.avatar;
		flags = data.flags;
		premium_type = data.premium_type;
		nickname = data.nickname;
	}
}

[Serializable]
public class DiscordParticipantsData
{
	public DiscordParticipant[] participants;

	public void CopyFromDissonity( InstanceParticipantsData data )
	{
		participants = new DiscordParticipant[ data.participants.Length ];

		for ( int i = 0; i < data.participants.Length; i++ )
		{
			participants[ i ].CopyFromDissonity( data.participants[ i ] );
		}
	}
}

public class DiscordController : MonoBehaviour
{
	public UnityEvent<DiscordUser> OnUserUpdated = new UnityEvent<DiscordUser>();
	public UnityEvent<DiscordParticipantsData> OnParticipantsUpdated = new UnityEvent<DiscordParticipantsData>();

	private bool isLinked = false;
	private bool isLinkedCacheValid = false;

	private string oauthState;

	public bool IsLinked { get => isLinked; private set => isLinked = value; }

	void Awake()
	{
		_checkDiscordLinkTask = new( () => CheckDiscordLinkInternal(), LazyThreadSafetyMode.ExecutionAndPublication );
	}

	public virtual Task<long> GetDiscordUserId()
	{
		return Task.FromResult<long>( 0 );
	}

	public virtual Task<string> GetDiscordUserName()
	{
		return Task.FromResult( "DiscordUserName" );
	}

	public virtual Task<string> GetDiscordChannelId()
	{
		return Task.FromResult( "TourismTogether_PC" );
	}

	public virtual void OpenLink( string link )
	{
		UnityEngine.Application.OpenURL( link );
	}

	public async void RequestDiscordLink()
	{
		ClearIsLinkedCache();

		bool linked = await CheckDiscordLink();

		if ( linked )
		{
			return;
		}

		bool success = await startOAuthProcess();

		if ( success )
		{
			string oauthUrl = GenerateOAuthURL();

			OpenLink( oauthUrl );
		}
	}

	private async Task<bool> startOAuthProcess()
	{
		oauthState = GenerateStateValue();

		TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

		try
		{
			if ( !GameInstance.gameDef.FunkBotEnabled )
			{
				tcs.TrySetResult( false );

				throw new Exception( "DiscordController: StartOAuthProcess warning: FunkBOT is disabled" );
			}

			FunkClient.Instance.funkBotClient.SendDiscordAuthMessage( oauthState, new UnityAction<FunkBotMessage>( ( message ) =>
			{
				if ( message.getData<string>( "result", out string result ) && result == "Success" )
				{
					tcs.TrySetResult( true );
				}
				else
				{
					Debug.Log( "DiscordController: StartOAuthProcess error: " + result );

					tcs.TrySetResult( false );
				}
			} ) );
		}
		catch ( Exception e )
		{
			Debug.LogError( "DiscordController: StartOAuthProcess Exception: " + e.Message );

			tcs.TrySetResult( false );
		}

		return await tcs.Task;
	}

	private Lazy<Task<bool>> _checkDiscordLinkTask;

	public async Task<bool> CheckDiscordLink()
	{
		if ( _checkDiscordLinkTask == null || !_checkDiscordLinkTask.IsValueCreated )
		{
			_checkDiscordLinkTask = new Lazy<Task<bool>>( () => CheckDiscordLinkInternal(), LazyThreadSafetyMode.ExecutionAndPublication );
		}

		return await _checkDiscordLinkTask.Value;
	}

	private async Task<bool> CheckDiscordLinkInternal()
	{
		TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

		if ( isLinkedCacheValid )
		{
			tcs.TrySetResult( isLinked );

			return await tcs.Task;
		}

		try
		{
			if ( !GameInstance.gameDef.FunkBotEnabled )
			{
				IsLinked = false;

				tcs.TrySetResult( false );

				throw new Exception( "DiscordController: StartOAuthProcess warning: FunkBOT is disabled" );
			}

			await TaskEx.WaitUntil( () => FunkClient.Instance.funkBotClient.IsConnectedAndAuthorised );

			FunkClient.Instance.funkBotClient.SendDiscordCheckLinkMessage( new UnityAction<FunkBotMessage>( ( message ) =>
			{
				if ( message.getData<bool>( "isLinked", out bool isLinked ) && isLinked )
				{
					IsLinked = true;
					isLinkedCacheValid = true;

					tcs.TrySetResult( true );
				}
				else
				{
					Debug.Log( "DiscordController: CheckDiscordLink not linked" );

					IsLinked = false;
					isLinkedCacheValid = true;

					tcs.TrySetResult( false );
				}
			} ) );
		}
		catch ( Exception e )
		{
			Debug.LogError( "DiscordController: CheckDiscordLink Exception: " + e.Message );

			IsLinked = false;

			tcs.TrySetResult( false );
		}

		return await tcs.Task;
	}

	public void ClearIsLinkedCache()
	{
		isLinkedCacheValid = false;
	}

	public async Task<bool> RequestDiscordInfo()
	{
		TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

		try
		{
			if ( !GameInstance.gameDef.FunkBotEnabled )
			{
				tcs.TrySetResult( false );

				throw new Exception( "DiscordController: StartOAuthProcess warning: FunkBOT is disabled" );
			}

			FunkClient.Instance.funkBotClient.SendDiscordRequest( FunkBotMessageType.RequestUserInfo, new UnityAction<FunkBotMessage>( ( message ) =>
			{
				tcs.TrySetResult( true );
			} ) );
		}
		catch ( Exception e )
		{
			Debug.LogError( "DiscordController: CheckDiscordLink Exception: " + e.Message );

			tcs.TrySetResult( false );
		}

		return await tcs.Task;
	}

	public async Task<bool> RevokeDiscordAuth()
	{
		TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

		try
		{
			if ( !GameInstance.gameDef.FunkBotEnabled )
			{
				tcs.TrySetResult( false );

				throw new Exception( "DiscordController: StartOAuthProcess warning: FunkBOT is disabled" );
			}

			FunkClient.Instance.funkBotClient.SendDiscordRequest( FunkBotMessageType.RevokeDiscordAuth, new UnityAction<FunkBotMessage>( ( message ) =>
			{
				if ( message.getData<string>( "result", out string result ) && result == "Success" )
				{
					tcs.TrySetResult( true );
				}
				else
				{
					Debug.Log( "DiscordController: RevokeDiscordAuth error: " + result );

					tcs.TrySetResult( false );
				}
			} ) );
		}
		catch ( Exception e )
		{
			Debug.LogError( "DiscordController: CheckDiscordLink Exception: " + e.Message );

			tcs.TrySetResult( false );
		}

		ClearIsLinkedCache();

		return await tcs.Task;
	}

	public string GetOAuthState()
	{
		return oauthState;
	}

	private string GenerateStateValue()
	{
		// Create a byte array to hold the random value
		byte[] randomBytes = new byte[ 16 ];

		// Use RandomNumberGenerator to fill the array with a cryptographically secure random number
		using ( var rng = RandomNumberGenerator.Create() )
		{
			rng.GetBytes( randomBytes );
		}

		// Convert the byte array to a hexadecimal string
		StringBuilder hex = new StringBuilder( randomBytes.Length * 2 );
		foreach ( byte b in randomBytes )
		{
			hex.AppendFormat( "{0:x2}", b );
		}

		return hex.ToString();
	}

	public string GenerateOAuthURL()
	{
		string url = "https://discord.com/oauth2/authorize?";

		url += "client_id=1200252512876384296";
		url += "&response_type=code";
		url += "&scope=identify+guilds.members.read";
		url += "&prompt=consent";
		url += "&redirect_uri=" + HttpUtility.UrlEncode( GameInstance.gameDef.discordOAuthRedirectURI );
		url += "&state=" + oauthState;

		Debug.Log( "DiscordController: GenerateOAuthURL() = " + url );

		return url;
	}
}
#endif