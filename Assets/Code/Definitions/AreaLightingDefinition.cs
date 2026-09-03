using System.Runtime.InteropServices;

using UnityEngine;

/// <summary>
/// Global area-ambient settings for <see cref="AreaLightingRendererFeature"/>.
/// World extent comes from <see cref="AreaLightingWorldBounds"/> in the level.
/// Asset name must be <c>AreaLightingDefinition</c> for <see cref="GameInstance.GetDefinition{T}"/>.
/// </summary>
[CreateAssetMenu( fileName = "AreaLightingDefinition", menuName = "Definitions/Level/AreaLightingDefinition" )]
public class AreaLightingDefinition : ScriptableObject
{
	public const int DefaultMaxVolumes = 16;

	public static readonly int ClipmapTexId = Shader.PropertyToID( "_DragonLoot_AreaAmbientTex" );
	public static readonly int ClipmapOriginId = Shader.PropertyToID( "_DragonLoot_AreaAmbientOrigin" );
	public static readonly int ClipmapSizeId = Shader.PropertyToID( "_DragonLoot_AreaAmbientSize" );
	public static readonly int GlobalIntensityId = Shader.PropertyToID( "_DragonLoot_AreaAmbientIntensity" );
	public static readonly int AmbientMixId = Shader.PropertyToID( "_DragonLoot_AreaAmbientMix" );
	public static readonly int AlbedoInfluenceId = Shader.PropertyToID( "_DragonLoot_AreaAmbientAlbedoInfluence" );
	public static readonly int FogInfluenceId = Shader.PropertyToID( "_DragonLoot_AreaAmbientFogInfluence" );
	public static readonly int EnabledId = Shader.PropertyToID( "_DragonLoot_AreaAmbientEnabled" );

	static float _runtimeFogInfluenceScale = 1f;

	public static float RuntimeFogInfluenceScale => _runtimeFogInfluenceScale;

	[Header( "Feature" )]
	public bool enableAreaLighting = true;

	[Min( 0f )]
	[Tooltip( "Scales baked area tint before it reaches materials." )]
	public float globalIntensity = 0.35f;

	[Header( "Material Response" )]
	[Range( 0f, 1f )]
	[Tooltip( "How strongly area tint blends into ambient. Lower = subtle colour hint." )]
	public float ambientMix = 0.4f;

	[Range( 0f, 1f )]
	[Tooltip( "0 = flat colour cast; 1 = multiply tint through albedo (stronger on bright surfaces)." )]
	public float albedoInfluence = 0.35f;

	[Range( 0f, 1f )]
	[Tooltip( "How much area lights tint fog colour. 0 = no effect, 1 = full area colour in fog." )]
	public float fogInfluence = 0.5f;

	[Header( "World Volume" )]
	[Tooltip( "Voxel resolution of the level-fixed 3D irradiance volume (X, Y, Z)." )]
	public Vector3Int worldVolumeResolution = new Vector3Int( 64, 24, 64 );

	[Range( 1, 64 )]
	public int maxVolumes = DefaultMaxVolumes;

	[Header( "Blend" )]
	[Range( 0f, 1f )]
	[Tooltip( "0 = weighted average where volumes overlap; 1 = additive stack." )]
	public float blendFill = 0f;

	int _settingsVersion;

	public int SettingsVersion => _settingsVersion;

	[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.SubsystemRegistration )]
	static void ResetStatics()
	{
		_runtimeFogInfluenceScale = 1f;
	}

	public static void SetRuntimeFogInfluenceScale( float scale )
	{
		_runtimeFogInfluenceScale = Mathf.Clamp01( scale );
	}

	public static void ClearRuntimeFogInfluenceScale()
	{
		_runtimeFogInfluenceScale = 1f;
	}

	[StructLayout( LayoutKind.Sequential )]
	public struct GpuAreaVolume
	{
		public Matrix4x4 worldToLocal;
		public Vector4 color;
		public float softness;
		public float falloffExtend;
		public float fogInfluence;
		public float pad0;
	}

	public void Validate()
	{
		worldVolumeResolution.x = Mathf.Clamp( worldVolumeResolution.x, 4, 128 );
		worldVolumeResolution.y = Mathf.Clamp( worldVolumeResolution.y, 4, 64 );
		worldVolumeResolution.z = Mathf.Clamp( worldVolumeResolution.z, 4, 128 );
		maxVolumes = Mathf.Clamp( maxVolumes, 1, 64 );
		globalIntensity = Mathf.Max( 0f, globalIntensity );
		ambientMix = Mathf.Clamp01( ambientMix );
		albedoInfluence = Mathf.Clamp01( albedoInfluence );
		fogInfluence = Mathf.Clamp01( fogInfluence );
	}

	public void NotifyChanged()
	{
		Validate();
		_settingsVersion++;
	}

	public void ApplyDisabledGlobals( Texture clipmap )
	{
		if ( clipmap != null )
			Shader.SetGlobalTexture( ClipmapTexId, clipmap );
		Shader.SetGlobalVector( ClipmapOriginId, Vector3.zero );
		Shader.SetGlobalVector( ClipmapSizeId, Vector3.one );
		Shader.SetGlobalFloat( GlobalIntensityId, 0f );
		Shader.SetGlobalFloat( AmbientMixId, 0f );
		Shader.SetGlobalFloat( AlbedoInfluenceId, 0f );
		Shader.SetGlobalFloat( FogInfluenceId, 0f );
		Shader.SetGlobalFloat( EnabledId, 0f );
	}

	public void ApplyGlobals( Texture clipmap, Vector3 origin, Vector3 worldSize, float intensityScale )
	{
		Validate();
		if ( clipmap != null )
			Shader.SetGlobalTexture( ClipmapTexId, clipmap );
		Shader.SetGlobalVector( ClipmapOriginId, origin );
		Shader.SetGlobalVector( ClipmapSizeId, worldSize );
		float intensity = enableAreaLighting ? globalIntensity * intensityScale : 0f;
		Shader.SetGlobalFloat( GlobalIntensityId, intensity );
		Shader.SetGlobalFloat( AmbientMixId, enableAreaLighting ? ambientMix : 0f );
		Shader.SetGlobalFloat( AlbedoInfluenceId, enableAreaLighting ? albedoInfluence : 0f );
		float fogInfluenceValue = enableAreaLighting ? fogInfluence * _runtimeFogInfluenceScale : 0f;
		Shader.SetGlobalFloat( FogInfluenceId, fogInfluenceValue );
		Shader.SetGlobalFloat( EnabledId, intensity > 1e-4f ? 1f : 0f );
	}

	public void ApplyToCompute( ComputeShader compute, int kernel )
	{
		if ( compute == null || kernel < 0 )
			return;

		Validate();
		compute.SetInts( "_AreaClipmapResolution", worldVolumeResolution.x, worldVolumeResolution.y, worldVolumeResolution.z );
		compute.SetFloat( "_AreaBlendFill", blendFill );
	}

#if UNITY_EDITOR
	void OnValidate()
	{
		NotifyChanged();
	}
#endif
}
