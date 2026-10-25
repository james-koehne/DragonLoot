using UnityEngine;

public class DebugCameraSection : DebugOverlaySection
{
	public string Title => "Camera";

	public void Draw()
	{
		FirstPersonCameraController cameraLook = DebugOverlay.GetCameraLook();
		if ( cameraLook == null )
		{
			GUILayout.Label( "No camera" );
			return;
		}

		bool flying = DebugFlyCamera.IsActive;
		if ( GUILayout.Button( flying ? "Fly Cam: ON" : "Fly Cam: OFF" ) )
			DebugFlyCamera.Toggle();

		GUILayout.Label( "WASD move  Q/E down/up  Sprint boost  Esc exit" );

		float speed = DebugFlyCamera.Speed;
		GUILayout.Label( $"Fly speed: {speed:0.0}" );
		float nextSpeed = GUILayout.HorizontalSlider( speed, DebugFlyCamera.MinFlySpeed, DebugFlyCamera.MaxFlySpeed );
		if ( !Mathf.Approximately( nextSpeed, speed ) )
			DebugFlyCamera.Speed = nextSpeed;

		float boost = DebugFlyCamera.BoostMultiplier;
		GUILayout.Label( $"Boost x: {boost:0.0}" );
		float nextBoost = GUILayout.HorizontalSlider( boost, 1f, 10f );
		if ( !Mathf.Approximately( nextBoost, boost ) )
			DebugFlyCamera.BoostMultiplier = nextBoost;

		GUILayout.Space( 6f );

		float min = cameraLook.MinLookSensitivity;
		float max = cameraLook.MaxLookSensitivity;
		if ( max < min )
			max = min;

		float sens = cameraLook.CurrentLookSensitivity;
		GUILayout.Label( $"Sensitivity: {sens:0.00}" );
		float nextSens = GUILayout.HorizontalSlider( sens, min, max );
		if ( !Mathf.Approximately( nextSens, sens ) )
			cameraLook.SetLookSensitivity( nextSens );

		bool invert = cameraLook.InvertY;
		bool nextInvert = GUILayout.Toggle( invert, "Invert Y" );
		if ( nextInvert != invert )
			cameraLook.InvertY = nextInvert;
	}
}
