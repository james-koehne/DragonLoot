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
