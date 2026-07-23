using UnityEngine;

public class DebugSessionSection : DebugOverlaySection
{
	readonly DebugOverlay _host;

	public DebugSessionSection( DebugOverlay host )
	{
		_host = host;
	}

	public string Title => "Session";

	public void Draw()
	{
		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Respawn" ) )
			_host.RespawnPlayer();
		if ( GUILayout.Button( "Reload" ) )
			_host.ReloadGame();
		if ( GUILayout.Button( "Quit" ) )
			_host.QuitGame();
		GUILayout.EndHorizontal();

		float scale = Time.timeScale;
		GUILayout.Label( $"Time Scale: {scale:0.00}" );
		float next = GUILayout.HorizontalSlider( scale, 0f, 2f );
		if ( !Mathf.Approximately( next, scale ) )
			Time.timeScale = next;

		if ( GUILayout.Button( "Reset Time Scale (1)" ) )
			Time.timeScale = 1f;
	}
}
