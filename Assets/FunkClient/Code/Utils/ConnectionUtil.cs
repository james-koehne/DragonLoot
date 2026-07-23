using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.Networking;

public static class ConnectionUtil
{
	private static bool? _lastCheckResult;
	private static float _lastCheckTime;
	private static readonly float checkCooldown = 10f; // seconds

	public static Task<bool> Connected => GetConnectivityAsync();

	public static async Task<bool> GetConnectivityAsync( string testUrl = "https://funk.games/", int timeoutSeconds = 5 )
	{
		float timeSinceLastCheck = Time.realtimeSinceStartup - _lastCheckTime;

		if ( _lastCheckResult.HasValue && timeSinceLastCheck < checkCooldown )
		{
			return _lastCheckResult.Value;
		}

		_lastCheckTime = Time.realtimeSinceStartup;
		_lastCheckResult = await PerformWebRequestAsync( testUrl, timeoutSeconds );
		return _lastCheckResult.Value;
	}

	private static async Task<bool> PerformWebRequestAsync( string url, int timeoutSeconds )
	{
		using ( UnityWebRequest request = new UnityWebRequest( url, UnityWebRequest.kHttpVerbHEAD ) )
		{
			request.timeout = timeoutSeconds;
			request.downloadHandler = new DownloadHandlerBuffer();

			UnityWebRequestAsyncOperation operation = request.SendWebRequest();
			while ( !operation.isDone )
				await Task.Yield(); // Wait asynchronously

			return request.result == UnityWebRequest.Result.Success;
		}
	}
}
