using Newtonsoft.Json;

using UnityEngine;

public sealed class GameSaveProgressContributor : IGameSaveContributor
{
	public string Key => "progress";

	public void Capture( GameSaveData data )
	{
		if ( data == null )
			return;

		ProfileManager manager = ProfileManager.Instance;
		if ( manager == null || manager.ProfileSaveData == null )
		{
			data.progress = null;
			return;
		}

		string json = JsonConvert.SerializeObject( manager.ProfileSaveData );
		data.progress = JsonConvert.DeserializeObject<ProfileSaveData>( json );
		if ( data.progress != null )
			data.progress.EnsureProgressDictionaries();
	}

	public void Apply( GameSaveData data )
	{
		if ( data == null || data.progress == null )
			return;

		ProfileManager manager = ProfileManager.Instance;
		if ( manager == null )
		{
			Debug.LogWarning( "GameSaveProgressContributor: ProfileManager missing" );
			return;
		}

		manager.ReplaceProfileSaveData( data.progress );
	}
}
