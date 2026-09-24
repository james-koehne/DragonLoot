using UnityEngine;

/// <summary>
/// Day length and celestial orbit settings for the procedural Sky Portal.
/// Evaluated by <see cref="TimeOfDayController"/> and pushed as shader globals.
/// </summary>
[CreateAssetMenu( fileName = "TimeOfDayDefinition", menuName = "Definitions/TimeOfDayDefinition" )]
public class TimeOfDayDefinition : ScriptableObject
{
	public static readonly int TimeOfDayId = Shader.PropertyToID( "_DragonLoot_TimeOfDay" );
	public static readonly int SunDirectionId = Shader.PropertyToID( "_DragonLoot_SunDirectionWS" );
	public static readonly int MoonDirectionId = Shader.PropertyToID( "_DragonLoot_MoonDirectionWS" );
	public static readonly int DayFactorId = Shader.PropertyToID( "_DragonLoot_DayFactor" );
	public static readonly int NightFactorId = Shader.PropertyToID( "_DragonLoot_NightFactor" );
	public static readonly int SunsetFactorId = Shader.PropertyToID( "_DragonLoot_SunsetFactor" );

	const float DefaultDayLengthSeconds = 600f;
	const float DefaultStartNormalizedTime = 0.3f;
	const float DefaultSunAzimuthDegrees = 25f;
	const float DefaultMoonPhaseOffset = 0.5f;

	[Header( "Cycle" )]
	[Tooltip( "Real-time seconds for a full day/night loop." )]
	[Min( 1f )]
	public float dayLengthSeconds = DefaultDayLengthSeconds;

	[Tooltip( "Normalized time when play starts. 0 = midnight, 0.25 = sunrise, 0.5 = noon, 0.75 = sunset." )]
	[Range( 0f, 1f )]
	public float startNormalizedTime = DefaultStartNormalizedTime;

	[Header( "Celestial" )]
	[Tooltip( "Y-axis rotation of the sun/moon arc so midday is still near zenith but not always due north." )]
	[Range( 0f, 360f )]
	public float sunAzimuthDegrees = DefaultSunAzimuthDegrees;

	[Tooltip( "Normalized offset of the moon along the same arc (0.5 = opposite the sun)." )]
	[Range( 0f, 1f )]
	public float moonPhaseOffset = DefaultMoonPhaseOffset;

	public void Validate()
	{
		dayLengthSeconds = Mathf.Max( 1f, dayLengthSeconds );
		startNormalizedTime = Mathf.Repeat( startNormalizedTime, 1f );
		sunAzimuthDegrees = Mathf.Repeat( sunAzimuthDegrees, 360f );
		moonPhaseOffset = Mathf.Clamp01( moonPhaseOffset );
	}

	void OnValidate()
	{
		Validate();
	}

	/// <summary>
	/// Sun arcs with noon at zenith so a cave roof opening can see midday sun.
	/// t: 0 midnight, 0.25 sunrise, 0.5 noon, 0.75 sunset.
	/// </summary>
	public void Evaluate( float normalizedTime, out Vector3 sunDirectionWS, out Vector3 moonDirectionWS, out float dayFactor, out float nightFactor, out float sunsetFactor )
	{
		float t = Mathf.Repeat( normalizedTime, 1f );
		sunDirectionWS = EvaluateCelestialDirection( t, sunAzimuthDegrees );
		moonDirectionWS = EvaluateCelestialDirection( Mathf.Repeat( t + moonPhaseOffset, 1f ), sunAzimuthDegrees );

		float sunY = sunDirectionWS.y;
		dayFactor = Mathf.SmoothStep( -0.05f, 0.2f, sunY );
		nightFactor = 1f - dayFactor;

		float nearHorizon = 1f - Mathf.Clamp01( Mathf.Abs( sunY ) / 0.35f );
		sunsetFactor = nearHorizon * Mathf.Clamp01( 1f + sunY * 1.5f );
	}

	public void ApplyToGlobals( float normalizedTime )
	{
		ApplyGlobals( normalizedTime, sunAzimuthDegrees, moonPhaseOffset );
	}

	public static void ApplyDefaultGlobals()
	{
		ApplyGlobals( DefaultStartNormalizedTime, DefaultSunAzimuthDegrees, DefaultMoonPhaseOffset );
	}

	public static void ApplyGlobals( float normalizedTime, float sunAzimuthDegrees, float moonPhaseOffset )
	{
		float t = Mathf.Repeat( normalizedTime, 1f );
		Vector3 sunDirectionWS = EvaluateCelestialDirection( t, sunAzimuthDegrees );
		Vector3 moonDirectionWS = EvaluateCelestialDirection( Mathf.Repeat( t + moonPhaseOffset, 1f ), sunAzimuthDegrees );

		float sunY = sunDirectionWS.y;
		float dayFactor = Mathf.SmoothStep( -0.05f, 0.2f, sunY );
		float nightFactor = 1f - dayFactor;
		float nearHorizon = 1f - Mathf.Clamp01( Mathf.Abs( sunY ) / 0.35f );
		float sunsetFactor = nearHorizon * Mathf.Clamp01( 1f + sunY * 1.5f );

		Shader.SetGlobalFloat( TimeOfDayId, t );
		Shader.SetGlobalVector( SunDirectionId, sunDirectionWS );
		Shader.SetGlobalVector( MoonDirectionId, moonDirectionWS );
		Shader.SetGlobalFloat( DayFactorId, dayFactor );
		Shader.SetGlobalFloat( NightFactorId, nightFactor );
		Shader.SetGlobalFloat( SunsetFactorId, sunsetFactor );
	}

	static Vector3 EvaluateCelestialDirection( float normalizedTime, float azimuthDegrees )
	{
		float angle = normalizedTime * Mathf.PI * 2f;
		// Midnight (0,-1,0), sunrise (0,0,1), noon (0,1,0), sunset (0,0,-1) before azimuth.
		Vector3 local = new Vector3( 0f, -Mathf.Cos( angle ), Mathf.Sin( angle ) );
		Quaternion yaw = Quaternion.AngleAxis( azimuthDegrees, Vector3.up );
		return ( yaw * local ).normalized;
	}
}
