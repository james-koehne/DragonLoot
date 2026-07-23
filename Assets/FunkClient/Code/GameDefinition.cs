using System;

using UnityEngine;
using UnityEngine.AddressableAssets;

[CreateAssetMenu( fileName = "GameDefinition", menuName = "Definitions/GameDefinition", order = 0 )]
public partial class GameDefinition : ScriptableObject
{
	[Serializable]
	public class PlatformGameDefinition
	{
		public PlatformType type;
		public string backendScheme;
		public string backendHost;
		public string backendServerKey;
		public string gameBaseUrl;
	}

	[Serializable]
	public enum PlatformType
	{
		Standalone,
		WebGL
	}

	public string gameName;
	public string gameBackendName;

	public bool singleplayer = false;
	public bool StatsRequireLogin = true;
	public bool FunkBackendEnabled = false;
	public bool FunkBotEnabled = false;
	public bool SteamEnabled = false;
	public AssetReference SteamAssetRef;
	public bool StoveEnabled = false;
	public AssetReference StoveAssetRef;
	public string StoveEnvironment = "live";
	public string StoveGameId = "";
	public string StoveApplicationKey = "";
	public uint SteamAppID = 0;

	public bool FunkBackendOverrideClasses = false;
	public AssetReference FunkBackendAssetRef;

	public PlatformType platform;

	public PlatformGameDefinition[] platformGameDefinitions;
	public string masterBackendScheme;
	public string masterBackendHost;
	public string masterBackendServerKey;
	public string botUrl;
	public string botApiKey;

	public long discordClientId;
	public string discordOAuthRedirectURI;

	public string openGameBaseLink;

	public string emailOctopusGameTag;
	public string emailOctopusApiKey;
	public string emailOctopusListId;

	public FunkAuthenticationType authMode = FunkAuthenticationType.Anonymous;

	public bool autoLogin = false;
	public bool testEmailLogin = false;
	public string testEmailEmail;
	public string testEmailPassword;

	public PlatformGameDefinition GetPlatformGameDefinition( PlatformType type )
	{
		if ( platformGameDefinitions == null || platformGameDefinitions.Length == 0 )
		{
			return null;
		}

		for ( int i = 0; i < platformGameDefinitions.Length; i++ )
		{
			if ( platformGameDefinitions[ i ] != null && platformGameDefinitions[ i ].type == type )
			{
				return platformGameDefinitions[ i ];
			}
		}

		return null;
	}
}