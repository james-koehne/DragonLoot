using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu( fileName = "EnvironmentDefinition", menuName = "Definitions/EnvironmentDefinition" )]
public class EnvironmentDefinition : ScriptableObject
{
	static bool _hasFogDensityOverride;
	static float _fogDensityOverride;

	public static bool HasFogDensityOverride => _hasFogDensityOverride;

	[Header( "Fog" )]
	public bool fogEnabled = true;
	public FogMode fogMode = FogMode.ExponentialSquared;
	public Color fogColor = new Color( 0.103773594f, 0.03332926f, 0f, 1f );
	[Min( 0f )]
	public float fogDensity = 0.002f;
	public float fogStartDistance = 0f;
	public float fogEndDistance = 300f;

	[Header( "Ambient" )]
	public AmbientMode ambientMode = AmbientMode.Flat;
	[ColorUsage( false, true )]
	public Color ambientSkyColor = new Color( 0.047169805f, 0.026274007f, 0.020247417f, 1f );
	[ColorUsage( false, true )]
	public Color ambientEquatorColor = new Color( 0.114f, 0.125f, 0.133f, 1f );
	[ColorUsage( false, true )]
	public Color ambientGroundColor = new Color( 0.047f, 0.043f, 0.035f, 1f );
	[Min( 0f )]
	public float ambientIntensity = 1f;

	[Header( "Skybox" )]
	[Tooltip( "Optional. Leave null for no skybox." )]
	public Material skybox;

	[Header( "Sun" )]
	[Tooltip( "When enabled and a sun Light is provided, applies sun color, intensity, and rotation." )]
	public bool applySun = false;
	public Color sunColor = Color.white;
	[Min( 0f )]
	public float sunIntensity = 1f;
	public Vector3 sunEulerAngles = new Vector3( 50f, -30f, 0f );

	[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.SubsystemRegistration )]
	static void ResetStatics()
	{
		_hasFogDensityOverride = false;
		_fogDensityOverride = 0f;
	}

	public static void SetFogDensityOverride( float density )
	{
		_hasFogDensityOverride = true;
		_fogDensityOverride = Mathf.Max( 0f, density );
		RenderSettings.fogDensity = _fogDensityOverride;
	}

	public static void ClearFogDensityOverride()
	{
		_hasFogDensityOverride = false;
	}

	public void Apply( Light sunLight )
	{
		RenderSettings.fog = fogEnabled;
		RenderSettings.fogMode = fogMode;
		RenderSettings.fogColor = fogColor;
		RenderSettings.fogDensity = _hasFogDensityOverride ? _fogDensityOverride : fogDensity;
		RenderSettings.fogStartDistance = fogStartDistance;
		RenderSettings.fogEndDistance = fogEndDistance;

		RenderSettings.ambientMode = ambientMode;
		RenderSettings.ambientSkyColor = ambientSkyColor;
		RenderSettings.ambientEquatorColor = ambientEquatorColor;
		RenderSettings.ambientGroundColor = ambientGroundColor;
		RenderSettings.ambientIntensity = ambientIntensity;
		if ( ambientMode == AmbientMode.Flat )
			RenderSettings.ambientLight = ambientSkyColor;

		RenderSettings.skybox = skybox;

		if ( !applySun || sunLight == null )
			return;

		sunLight.color = sunColor;
		sunLight.intensity = sunIntensity;
		sunLight.transform.rotation = Quaternion.Euler( sunEulerAngles );
		RenderSettings.sun = sunLight;
	}

	void OnValidate()
	{
		fogDensity = Mathf.Max( 0f, fogDensity );
		fogStartDistance = Mathf.Max( 0f, fogStartDistance );
		fogEndDistance = Mathf.Max( fogStartDistance, fogEndDistance );
		ambientIntensity = Mathf.Max( 0f, ambientIntensity );
		sunIntensity = Mathf.Max( 0f, sunIntensity );
	}
}
