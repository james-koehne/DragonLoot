using UnityEngine;
using UnityEngine.Networking;
using System.Text;
using System.Threading.Tasks;

public static class EmailOctopusAPI
{
	private const string BASE_URL = "https://api.emailoctopus.com/lists/";

	public static async Task AddContactAsync( string email, string game )
	{
		if ( string.IsNullOrEmpty( GameInstance.gameDef.emailOctopusApiKey ) || string.IsNullOrEmpty( GameInstance.gameDef.emailOctopusListId ) )
		{
			return;
		}


		string url = BASE_URL + GameInstance.gameDef.emailOctopusListId + "/contacts";

		// Create JSON payload
		string jsonData = $"{{ \"email_address\": \"{email}\", \"tags\": [ \"{game}\" ], \"status\": \"subscribed\" }}";
		byte[] jsonToSend = Encoding.UTF8.GetBytes( jsonData );

		using ( UnityWebRequest request = new UnityWebRequest( url, "POST" ) )
		{
			request.uploadHandler = new UploadHandlerRaw( jsonToSend );
			request.downloadHandler = new DownloadHandlerBuffer();
			request.SetRequestHeader( "Content-Type", "application/json" );
			request.SetRequestHeader( "Authorization", "Bearer " + GameInstance.gameDef.emailOctopusApiKey );

			UnityWebRequestAsyncOperation operation = request.SendWebRequest();

			while ( !operation.isDone )
				await Task.Yield();

			if ( request.result == UnityWebRequest.Result.Success )
			{
				Debug.Log( "EmailOctopusAPI: Contact added successfully: " + request.downloadHandler.text );
			}
			else
			{
				Debug.LogError( "EmailOctopusAPI: Error adding contact: " + request.error + " - " + request.downloadHandler.text );
			}
		}
	}
}
