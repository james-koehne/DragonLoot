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

	[Header( "Movement FOV" )]
	[Tooltip( "Extra field of view (degrees) while gliding or sliding." )]
	[Min( 0f )]
	public float speedFovBoost = 8f;

	[Tooltip( "Seconds to ease the glide/slide FOV boost in and out." )]
	[Min( 0.01f )]
	public float speedFovBlendTime = 0.2f;

	[Header( "Player Soft Fill Light" )]
	[Tooltip( "Soft point light on the camera for dark-room readability. Lanterns remain the primary light sources." )]
	public bool softFillLightEnabled = true;

	[Tooltip( "Warm fill color (match world lantern hue)." )]
	public Color softFillLightColor = new Color( 1f, 0.5f, 0.15f, 1f );

	[Tooltip( "Fill intensity (keep low so lanterns still own the look)." )]
	[Min( 0f )]
	public float softFillLightIntensity = 0.45f;

	[Tooltip( "Short range so fill dies before competing with room lanterns." )]
	[Min( 0.1f )]
	public float softFillLightRange = 4.5f;

	[Tooltip( "Local offset from the camera (slightly forward/down reduces floor headlight specular)." )]
	public Vector3 softFillLightLocalOffset = new Vector3( 0f, -0.15f, 0.25f );

	[Header( "Minecart Orbit" )]
	[Tooltip( "Third-person distance behind the cart pivot while driving." )]
	[Min( 0.5f )]
	public float minecartOrbitDistance = 4.5f;

	[Tooltip( "Pivot height above the cart origin." )]
	public float minecartOrbitHeight = 1.6f;

	[Tooltip( "Along-cart look-at bias so the cart does not fill the frame." )]
	[Min( 0f )]
	public float minecartOrbitLookahead = 2.5f;

	[Tooltip( "Seconds to blend between first person and orbit on enter/exit." )]
	[Min( 0.01f )]
	public float minecartOrbitBlendTime = 0.45f;

	[Tooltip( "Default pitch (degrees, look-down positive) after the enter blend." )]
	public float minecartOrbitDefaultPitch = 12f;

	[Tooltip( "Minimum orbit pitch (degrees). Negative looks up." )]
	public float minecartOrbitMinPitch = -15f;

	[Tooltip( "Maximum orbit pitch (degrees). Positive looks down." )]
	public float minecartOrbitMaxPitch = 70f;

	[Tooltip( "SphereCast radius for collision pull-in." )]
	[Min( 0.05f )]
	public float minecartOrbitCollisionRadius = 0.25f;

	[Tooltip( "Keep this gap from hit surfaces after pull-in." )]
	[Min( 0f )]
	public float minecartOrbitCollisionSkin = 0.1f;

	[Tooltip( "After no look input, ease yaw/pitch back behind the cart." )]
	public bool minecartOrbitAutoRecenter = true;

	[Tooltip( "Seconds without look input before auto-recenter starts." )]
	[Min( 0f )]
	public float minecartOrbitRecenterDelay = 1.5f;

	[Tooltip( "Degrees per second while auto-recentering." )]
	[Min( 1f )]
	public float minecartOrbitRecenterSpeed = 90f;

	void OnValidate()
	{
		minLookSensitivity = Mathf.Max( 0.01f, minLookSensitivity );
		maxLookSensitivity = Mathf.Max( minLookSensitivity, maxLookSensitivity );
		lookSensitivity = Mathf.Clamp( lookSensitivity, minLookSensitivity, maxLookSensitivity );
		minPitch = Mathf.Clamp( minPitch, -85f, 85f );
		maxPitch = Mathf.Clamp( maxPitch, minPitch, 85f );
		speedFovBoost = Mathf.Max( 0f, speedFovBoost );
		speedFovBlendTime = Mathf.Max( 0.01f, speedFovBlendTime );
		softFillLightIntensity = Mathf.Max( 0f, softFillLightIntensity );
		softFillLightRange = Mathf.Max( 0.1f, softFillLightRange );
		minecartOrbitDistance = Mathf.Max( 0.5f, minecartOrbitDistance );
		minecartOrbitLookahead = Mathf.Max( 0f, minecartOrbitLookahead );
		minecartOrbitBlendTime = Mathf.Max( 0.01f, minecartOrbitBlendTime );
		minecartOrbitMinPitch = Mathf.Clamp( minecartOrbitMinPitch, -85f, 85f );
		minecartOrbitMaxPitch = Mathf.Clamp( minecartOrbitMaxPitch, minecartOrbitMinPitch, 85f );
		minecartOrbitDefaultPitch = Mathf.Clamp( minecartOrbitDefaultPitch, minecartOrbitMinPitch, minecartOrbitMaxPitch );
		minecartOrbitCollisionRadius = Mathf.Max( 0.05f, minecartOrbitCollisionRadius );
		minecartOrbitCollisionSkin = Mathf.Max( 0f, minecartOrbitCollisionSkin );
		minecartOrbitRecenterDelay = Mathf.Max( 0f, minecartOrbitRecenterDelay );
		minecartOrbitRecenterSpeed = Mathf.Max( 1f, minecartOrbitRecenterSpeed );
	}
}
