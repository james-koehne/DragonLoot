using UnityEngine;

public class DebugSettingsSection : DebugOverlaySection
{
	public string Title => "Settings";

	public void Draw()
	{
		float vol = AudioMaster.UserMasterVolume;
		GUILayout.Label( $"Master Volume: {vol:0.00}" );
		float next = GUILayout.HorizontalSlider( vol, 0f, 1f );
		if ( !Mathf.Approximately( next, vol ) )
			AudioMaster.SetUserMasterVolume( next );

		DrawChannelToggle( AudioChannel.Fx, "FX" );
		DrawChannelToggle( AudioChannel.Music, "Music" );
		DrawChannelToggle( AudioChannel.Ambience, "Ambience" );
	}

	static void DrawChannelToggle( AudioChannel channel, string label )
	{
		bool enabled = AudioMaster.IsChannelEnabled( channel );
		bool next = GUILayout.Toggle( enabled, label );
		if ( next != enabled )
			AudioMaster.SetChannelEnabled( channel, next );
	}
}
