using UnityEngine;

/// <summary>
/// Global stylized lighting settings for DragonLoot environment shaders.
/// Asset name must be <c>StylizedLightingDefinition</c> for <see cref="GameInstance.GetDefinition{T}"/>.
/// Pushed to shader globals via <see cref="ApplyToGlobals"/>.
/// </summary>
[CreateAssetMenu( fileName = "StylizedLightingDefinition", menuName = "Definitions/StylizedLightingDefinition" )]
public class StylizedLightingDefinition : ScriptableObject
{
	public static readonly int FalloffExponentId = Shader.PropertyToID( "_DragonLoot_FalloffExponent" );
	public static readonly int FalloffEdgeSoftnessId = Shader.PropertyToID( "_DragonLoot_FalloffEdgeSoftness" );
	public static readonly int DiffuseWrapId = Shader.PropertyToID( "_DragonLoot_DiffuseWrap" );
	public static readonly int DirectLightScaleId = Shader.PropertyToID( "_DragonLoot_DirectLightScale" );
	public static readonly int SpecularIntensityId = Shader.PropertyToID( "_DragonLoot_SpecularIntensity" );
	public static readonly int SpecularSizeId = Shader.PropertyToID( "_DragonLoot_SpecularSize" );
	public static readonly int SpecularColorId = Shader.PropertyToID( "_DragonLoot_SpecularColor" );
	public static readonly int EnableSpecularId = Shader.PropertyToID( "_DragonLoot_EnableSpecular" );
	public static readonly int RimColorId = Shader.PropertyToID( "_DragonLoot_RimColor" );
	public static readonly int RimIntensityId = Shader.PropertyToID( "_DragonLoot_RimIntensity" );
	public static readonly int RimPowerId = Shader.PropertyToID( "_DragonLoot_RimPower" );
	public static readonly int ShadowColorId = Shader.PropertyToID( "_DragonLoot_ShadowColor" );
	public static readonly int ShadowStrengthId = Shader.PropertyToID( "_DragonLoot_ShadowStrength" );
	public static readonly int ReceiveShadowsId = Shader.PropertyToID( "_DragonLoot_ReceiveShadows" );
	public static readonly int AmbientIntensityId = Shader.PropertyToID( "_DragonLoot_AmbientIntensity" );
	public static readonly int AmbientTintId = Shader.PropertyToID( "_DragonLoot_AmbientTint" );
	public static readonly int AmbientFloorId = Shader.PropertyToID( "_DragonLoot_AmbientFloor" );
	public static readonly int SaturationId = Shader.PropertyToID( "_DragonLoot_Saturation" );
	public static readonly int ContrastId = Shader.PropertyToID( "_DragonLoot_Contrast" );
	public static readonly int EnvironmentReflectionsId = Shader.PropertyToID( "_DragonLoot_EnvironmentReflections" );
	public static readonly int ReflectionIntensityId = Shader.PropertyToID( "_DragonLoot_ReflectionIntensity" );

	[Header( "Falloff" )]
	[Tooltip( "Lower = light fills more of its range. 1 = soft linear-ish; 2 = closer to quadratic." )]
	[Range( 0.25f, 4f )]
	public float falloffExponent = 1f;

	[Tooltip( "Smooth fade near the outer edge of the light range." )]
	[Range( 0.01f, 1f )]
	public float falloffEdgeSoftness = 0.35f;

	[Header( "Diffuse / Direct" )]
	[Tooltip( "Wraps light around surfaces. 0 = hard Lambert; higher = softer fill." )]
	[Range( 0f, 1f )]
	public float diffuseWrap = 0.35f;

	[Min( 0f )]
	public float directLightScale = 1f;

	[Header( "Specular" )]
	public bool enableSpecular = true;

	[Min( 0f )]
	public float specularIntensity = 0.45f;

	[Tooltip( "Blinn specular power. Higher = tighter highlight." )]
	[Range( 2f, 256f )]
	public float specularSize = 48f;

	public Color specularColor = Color.white;

	[Header( "Rim" )]
	[ColorUsage( true, true )]
	public Color rimColor = new Color( 1f, 0.95f, 0.85f, 1f );

	[Min( 0f )]
	public float rimIntensity = 0.2f;

	[Range( 0.5f, 8f )]
	public float rimPower = 3f;

	[Header( "Shadows" )]
	public Color shadowColor = new Color( 0.15f, 0.18f, 0.28f, 1f );

	[Range( 0f, 1f )]
	public float shadowStrength = 1f;

	public bool receiveShadows = true;

	[Header( "Ambient / Grade" )]
	[Min( 0f )]
	public float ambientIntensity = 1f;

	public Color ambientTint = Color.white;

	[Range( 0f, 0.5f )]
	public float ambientFloor = 0.04f;

	[Range( 0f, 2f )]
	public float saturation = 1f;

	[Range( 0.25f, 2f )]
	public float contrast = 1f;

	[Header( "Reflections" )]
	public bool environmentReflections = true;

	[Min( 0f )]
	public float reflectionIntensity = 0.35f;

	public void Validate()
	{
		falloffExponent = Mathf.Clamp( falloffExponent, 0.25f, 4f );
		falloffEdgeSoftness = Mathf.Clamp( falloffEdgeSoftness, 0.01f, 1f );
		diffuseWrap = Mathf.Clamp01( diffuseWrap );
		directLightScale = Mathf.Max( 0f, directLightScale );
		specularIntensity = Mathf.Max( 0f, specularIntensity );
		specularSize = Mathf.Clamp( specularSize, 2f, 256f );
		rimIntensity = Mathf.Max( 0f, rimIntensity );
		rimPower = Mathf.Clamp( rimPower, 0.5f, 8f );
		shadowStrength = Mathf.Clamp01( shadowStrength );
		ambientIntensity = Mathf.Max( 0f, ambientIntensity );
		ambientFloor = Mathf.Clamp( ambientFloor, 0f, 0.5f );
		saturation = Mathf.Clamp( saturation, 0f, 2f );
		contrast = Mathf.Clamp( contrast, 0.25f, 2f );
		reflectionIntensity = Mathf.Max( 0f, reflectionIntensity );
	}

	void OnValidate()
	{
		Validate();
		ApplyToGlobals();
	}

	/// <summary>Pushes settings to shader globals used by stylized lighting HLSL.</summary>
	public void ApplyToGlobals()
	{
		Validate();
		Shader.SetGlobalFloat( FalloffExponentId, falloffExponent );
		Shader.SetGlobalFloat( FalloffEdgeSoftnessId, falloffEdgeSoftness );
		Shader.SetGlobalFloat( DiffuseWrapId, diffuseWrap );
		Shader.SetGlobalFloat( DirectLightScaleId, directLightScale );
		Shader.SetGlobalFloat( SpecularIntensityId, specularIntensity );
		Shader.SetGlobalFloat( SpecularSizeId, specularSize );
		Shader.SetGlobalColor( SpecularColorId, specularColor );
		Shader.SetGlobalFloat( EnableSpecularId, enableSpecular ? 1f : 0f );
		Shader.SetGlobalColor( RimColorId, rimColor );
		Shader.SetGlobalFloat( RimIntensityId, rimIntensity );
		Shader.SetGlobalFloat( RimPowerId, rimPower );
		Shader.SetGlobalColor( ShadowColorId, shadowColor );
		Shader.SetGlobalFloat( ShadowStrengthId, shadowStrength );
		Shader.SetGlobalFloat( ReceiveShadowsId, receiveShadows ? 1f : 0f );
		Shader.SetGlobalFloat( AmbientIntensityId, ambientIntensity );
		Shader.SetGlobalColor( AmbientTintId, ambientTint );
		Shader.SetGlobalFloat( AmbientFloorId, ambientFloor );
		Shader.SetGlobalFloat( SaturationId, saturation );
		Shader.SetGlobalFloat( ContrastId, contrast );
		Shader.SetGlobalFloat( EnvironmentReflectionsId, environmentReflections ? 1f : 0f );
		Shader.SetGlobalFloat( ReflectionIntensityId, reflectionIntensity );
	}

	/// <summary>Sensible soft-fill defaults when no definition is loaded yet.</summary>
	public static void ApplyDefaultGlobals()
	{
		Shader.SetGlobalFloat( FalloffExponentId, 1f );
		Shader.SetGlobalFloat( FalloffEdgeSoftnessId, 0.35f );
		Shader.SetGlobalFloat( DiffuseWrapId, 0.35f );
		Shader.SetGlobalFloat( DirectLightScaleId, 1f );
		Shader.SetGlobalFloat( SpecularIntensityId, 0.45f );
		Shader.SetGlobalFloat( SpecularSizeId, 48f );
		Shader.SetGlobalColor( SpecularColorId, Color.white );
		Shader.SetGlobalFloat( EnableSpecularId, 1f );
		Shader.SetGlobalColor( RimColorId, new Color( 1f, 0.95f, 0.85f, 1f ) );
		Shader.SetGlobalFloat( RimIntensityId, 0.2f );
		Shader.SetGlobalFloat( RimPowerId, 3f );
		Shader.SetGlobalColor( ShadowColorId, new Color( 0.15f, 0.18f, 0.28f, 1f ) );
		Shader.SetGlobalFloat( ShadowStrengthId, 1f );
		Shader.SetGlobalFloat( ReceiveShadowsId, 1f );
		Shader.SetGlobalFloat( AmbientIntensityId, 1f );
		Shader.SetGlobalColor( AmbientTintId, Color.white );
		Shader.SetGlobalFloat( AmbientFloorId, 0.04f );
		Shader.SetGlobalFloat( SaturationId, 1f );
		Shader.SetGlobalFloat( ContrastId, 1f );
		Shader.SetGlobalFloat( EnvironmentReflectionsId, 1f );
		Shader.SetGlobalFloat( ReflectionIntensityId, 0.35f );
	}
}
