using UnityEngine;

[CreateAssetMenu( fileName = "CameraDefinition", menuName = "Definitions/CameraDefinition" )]
public class CameraDefinition : ScriptableObject
{
	[Header( "Look" )]
	[Tooltip( "Multiplier applied to mouse look delta." )]
	public float lookSensitivity = 1f;

	[Tooltip( "Minimum look sensitivity (runtime and editor clamp)." )]
	public float minLookSensitivity = 0.01f;

	[Tooltip( "Maximum look sensitivity (runtime and editor clamp)." )]
	public float maxLookSensitivity = 5f;

	[Tooltip( "Invert vertical look axis." )]
	public bool invertY = false;

	[Header( "Pitch" )]
	public float minPitch = -85f;
	public float maxPitch = 85f;

	void OnValidate()
	{
		minLookSensitivity = Mathf.Max( 0.01f, minLookSensitivity );
		maxLookSensitivity = Mathf.Max( minLookSensitivity, maxLookSensitivity );
		lookSensitivity = Mathf.Clamp( lookSensitivity, minLookSensitivity, maxLookSensitivity );
		minPitch = Mathf.Clamp( minPitch, -85f, 85f );
		maxPitch = Mathf.Clamp( maxPitch, minPitch, 85f );
	}
}
