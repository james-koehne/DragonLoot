using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Drives a <see cref="Light"/> with organic intensity / color / range flicker from
/// <see cref="LightFlickerDefinition"/> presets, and mirrors that onto EnvironmentLit
/// emissive materials (typically the second material on each mesh).
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class LightFlicker : MonoBehaviour
{
	static readonly int EmissionColorId = Shader.PropertyToID( "_EmissionColor" );
	static readonly int EmissionIntensityId = Shader.PropertyToID( "_EmissionIntensity" );

	[SerializeField]
	LightFlickerDefinition _definition;

	[SerializeField]
	LightFlickerPresetKind _preset = LightFlickerPresetKind.Lantern;

	[SerializeField]
	[Tooltip( "Used when Preset is Custom. Must match a preset id on the definition." )]
	string _customPresetName = "Lantern";

	[SerializeField]
	[Tooltip( "Multiplies the preset intensity swing around the authored light intensity." )]
	[Range( 0f, 3f )]
	float _intensityScale = 1f;

	[SerializeField]
	[Tooltip( "Multiplies preset flicker speed." )]
	[Min( 0f )]
	float _speedScale = 1f;

	[SerializeField]
	[Tooltip( "Animate in the editor outside play mode." )]
	bool _playInEditMode = true;

	[SerializeField]
	[Tooltip( "Optional override. When null, uses the Light on this GameObject." )]
	Light _light;

	[SerializeField]
	[Tooltip( "Optional emissive mesh renderers. When empty, uses LODGroup / child MeshRenderers." )]
	MeshRenderer[] _emissiveRenderers;

	[SerializeField]
	[Tooltip( "Material index with the emissive DragonLoot/EnvironmentLit material." )]
	int _emissiveMaterialIndex = 1;

	LightFlickerDefinition Definition => RuntimeDefinition.Resolve( ref _definition );

	Light _cachedLight;
	float _baseIntensity = 1f;
	Color _baseColor = Color.white;
	float _baseRange = 10f;
	bool _hasBase;
	float _noiseSeed;
	float _smoothedNoise = 0.5f;
	string _activePresetId;
	LightFlickerPreset _activePreset;
	bool _hadDefinition;
	MaterialPropertyBlock _propertyBlock;
	List<MeshRenderer> _resolvedEmissiveRenderers;
	Color[] _baseEmissionColors;
	float[] _baseEmissionIntensities;
	bool[] _emissiveValid;

	public LightFlickerPresetKind Preset
	{
		get => _preset;
		set
		{
			if ( _preset == value )
				return;
			_preset = value;
			ResolveActivePreset( force: true );
		}
	}

	public string CustomPresetName
	{
		get => _customPresetName;
		set
		{
			_customPresetName = value;
			if ( _preset == LightFlickerPresetKind.Custom )
				ResolveActivePreset( force: true );
		}
	}

	void OnEnable()
	{
		EnsureCaches();
		EnsureLight();
		ResolveEmissiveRenderers();
		CaptureBase();
		_noiseSeed = HashSeed( GetInstanceID() );
		_smoothedNoise = 0.5f;
		ResolveActivePreset( force: true );
		ApplyFlicker( GetFlickerTime(), immediate: true );
	}

	void OnDisable()
	{
		RestoreBase();
	}

	void OnValidate()
	{
		_intensityScale = Mathf.Clamp( _intensityScale, 0f, 3f );
		_speedScale = Mathf.Max( 0f, _speedScale );
		_emissiveMaterialIndex = Mathf.Max( 0, _emissiveMaterialIndex );
		if ( string.IsNullOrWhiteSpace( _customPresetName ) )
			_customPresetName = nameof( LightFlickerPresetKind.Lantern );

		EnsureCaches();
		EnsureLight();
		if ( !Application.isPlaying && !_playInEditMode )
		{
			RestoreBase();
			return;
		}

		if ( isActiveAndEnabled )
		{
			ResolveEmissiveRenderers();
			if ( !_hasBase )
				CaptureBase();
			else
				CaptureEmissionBase();
			ResolveActivePreset( force: true );
		}
	}

	void Update()
	{
#if UNITY_EDITOR
		if ( !Application.isPlaying && !_playInEditMode )
			return;
#endif

		if ( !EnsureLight() )
			return;

		EnsureCaches();
		if ( _resolvedEmissiveRenderers.Count == 0 )
		{
			ResolveEmissiveRenderers();
			if ( _hasBase )
				CaptureEmissionBase();
		}

		if ( !_hasBase )
			CaptureBase();

		ResolveActivePreset( force: false );
		ApplyFlicker( GetFlickerTime(), immediate: false );
	}

	static float GetFlickerTime()
	{
#if UNITY_EDITOR
		if ( !Application.isPlaying )
			return (float)UnityEditor.EditorApplication.timeSinceStartup;
#endif
		return Time.time;
	}

	public void SetPreset( LightFlickerPresetKind kind )
	{
		Preset = kind;
	}

	public void SetCustomPreset( string presetId )
	{
		_preset = LightFlickerPresetKind.Custom;
		CustomPresetName = presetId;
	}

	[ContextMenu( "Recapture Light Base" )]
	public void RecaptureLightBase()
	{
		_hasBase = false;
		ResolveEmissiveRenderers();
		CaptureBase();
		ResolveActivePreset( force: true );
		ApplyFlicker( GetFlickerTime(), immediate: true );
	}

	bool EnsureLight()
	{
		if ( _light != null )
		{
			_cachedLight = _light;
			return true;
		}

		if ( _cachedLight == null )
			_cachedLight = GetComponent<Light>();

		return _cachedLight != null;
	}

	void CaptureBase()
	{
		if ( !EnsureLight() )
			return;

		_baseIntensity = _cachedLight.intensity;
		_baseColor = _cachedLight.color;
		_baseRange = _cachedLight.range;
		CaptureEmissionBase();
		_hasBase = true;
	}

	void RestoreBase()
	{
		if ( !_hasBase )
			return;

		if ( EnsureLight() )
		{
			_cachedLight.intensity = _baseIntensity;
			_cachedLight.color = _baseColor;
			_cachedLight.range = _baseRange;
		}

		RestoreEmissionBase();
	}

	void ResolveActivePreset( bool force )
	{
		string desiredId = ResolvePresetId();
		LightFlickerDefinition definition = Definition;
		bool hasDefinition = definition != null;
		if ( !force
			&& hasDefinition == _hadDefinition
			&& string.Equals( desiredId, _activePresetId, System.StringComparison.OrdinalIgnoreCase )
			&& _activePreset != null )
			return;

		_hadDefinition = hasDefinition;
		_activePresetId = desiredId;
		if ( definition != null )
			_activePreset = definition.GetPresetOrDefault( desiredId );
		else
			_activePreset = FallbackPreset( desiredId );
	}

	string ResolvePresetId()
	{
		if ( _preset == LightFlickerPresetKind.Custom )
			return string.IsNullOrWhiteSpace( _customPresetName )
				? nameof( LightFlickerPresetKind.Lantern )
				: _customPresetName.Trim();

		return _preset.ToString();
	}

	static LightFlickerPreset FallbackPreset( string id )
	{
		if ( string.Equals( id, nameof( LightFlickerPresetKind.Flame ), System.StringComparison.OrdinalIgnoreCase ) )
			return LightFlickerPreset.Flame();
		if ( string.Equals( id, nameof( LightFlickerPresetKind.Torch ), System.StringComparison.OrdinalIgnoreCase ) )
			return LightFlickerPreset.Torch();
		if ( string.Equals( id, nameof( LightFlickerPresetKind.Candle ), System.StringComparison.OrdinalIgnoreCase ) )
			return LightFlickerPreset.Candle();
		if ( string.Equals( id, nameof( LightFlickerPresetKind.Campfire ), System.StringComparison.OrdinalIgnoreCase ) )
			return LightFlickerPreset.Campfire();
		if ( string.Equals( id, nameof( LightFlickerPresetKind.Magical ), System.StringComparison.OrdinalIgnoreCase ) )
			return LightFlickerPreset.Magical();
		if ( string.Equals( id, nameof( LightFlickerPresetKind.Ember ), System.StringComparison.OrdinalIgnoreCase ) )
			return LightFlickerPreset.Ember();
		return LightFlickerPreset.Lantern();
	}

	void ApplyFlicker( float time, bool immediate )
	{
		if ( _activePreset == null || !EnsureLight() )
			return;

		float speed = _activePreset.speed * _speedScale;
		float noise = SampleLayeredNoise( time, speed, _noiseSeed, _activePreset );

		if ( immediate || _activePreset.smoothness <= 0f )
		{
			_smoothedNoise = noise;
		}
		else
		{
			float dt = Application.isPlaying ? Time.deltaTime : ( 1f / 60f );
			float t = 1f - Mathf.Exp( -_activePreset.smoothness * dt );
			_smoothedNoise = Mathf.Lerp( _smoothedNoise, noise, t );
		}

		float intensityMul = Mathf.Lerp( _activePreset.intensityMin, _activePreset.intensityMax, _smoothedNoise );
		if ( _intensityScale != 1f )
		{
			float centered = intensityMul - 1f;
			intensityMul = 1f + centered * _intensityScale;
		}

		_cachedLight.intensity = Mathf.Max( 0f, _baseIntensity * intensityMul );

		if ( _activePreset.affectColor )
			_cachedLight.color = _activePreset.GetLightColor( _smoothedNoise );
		else
			_cachedLight.color = _baseColor;

		if ( _activePreset.affectRange )
		{
			float rangeMul = Mathf.Lerp( _activePreset.rangeMin, _activePreset.rangeMax, _smoothedNoise );
			_cachedLight.range = Mathf.Max( 0.01f, _baseRange * rangeMul );
		}
		else
		{
			_cachedLight.range = _baseRange;
		}

		ApplyEmission( intensityMul );
	}

	void EnsureCaches()
	{
		if ( _propertyBlock == null )
			_propertyBlock = new MaterialPropertyBlock();
		if ( _resolvedEmissiveRenderers == null )
			_resolvedEmissiveRenderers = new List<MeshRenderer>( 4 );
	}

	void ResolveEmissiveRenderers()
	{
		EnsureCaches();
		_resolvedEmissiveRenderers.Clear();

		if ( _emissiveRenderers != null )
		{
			for ( int i = 0; i < _emissiveRenderers.Length; i++ )
			{
				MeshRenderer renderer = _emissiveRenderers[ i ];
				if ( renderer != null )
					_resolvedEmissiveRenderers.Add( renderer );
			}

			if ( _resolvedEmissiveRenderers.Count > 0 )
				return;
		}

		LODGroup lodGroup = GetComponent<LODGroup>();
		if ( lodGroup == null )
			lodGroup = GetComponentInParent<LODGroup>();

		if ( lodGroup != null )
		{
			LOD[] lods = lodGroup.GetLODs();
			for ( int i = 0; i < lods.Length; i++ )
			{
				Renderer[] renderers = lods[ i ].renderers;
				if ( renderers == null )
					continue;

				for ( int j = 0; j < renderers.Length; j++ )
				{
					if ( renderers[ j ] is MeshRenderer meshRenderer && !_resolvedEmissiveRenderers.Contains( meshRenderer ) )
						_resolvedEmissiveRenderers.Add( meshRenderer );
				}
			}
		}

		if ( _resolvedEmissiveRenderers.Count > 0 )
			return;

		Transform root = transform.parent != null ? transform.parent : transform;
		MeshRenderer[] found = root.GetComponentsInChildren<MeshRenderer>( true );
		for ( int i = 0; i < found.Length; i++ )
			_resolvedEmissiveRenderers.Add( found[ i ] );
	}

	void CaptureEmissionBase()
	{
		if ( _resolvedEmissiveRenderers == null )
			return;

		int count = _resolvedEmissiveRenderers.Count;
		if ( _baseEmissionColors == null || _baseEmissionColors.Length != count )
			_baseEmissionColors = new Color[ count ];
		if ( _baseEmissionIntensities == null || _baseEmissionIntensities.Length != count )
			_baseEmissionIntensities = new float[ count ];
		if ( _emissiveValid == null || _emissiveValid.Length != count )
			_emissiveValid = new bool[ count ];

		for ( int i = 0; i < count; i++ )
		{
			_baseEmissionColors[ i ] = Color.black;
			_baseEmissionIntensities[ i ] = 0f;
			_emissiveValid[ i ] = false;
			MeshRenderer renderer = _resolvedEmissiveRenderers[ i ];
			if ( renderer == null )
				continue;

			Material[] materials = renderer.sharedMaterials;
			if ( _emissiveMaterialIndex < 0 || _emissiveMaterialIndex >= materials.Length )
				continue;

			Material material = materials[ _emissiveMaterialIndex ];
			if ( material == null || !material.HasProperty( EmissionColorId ) )
				continue;

			_baseEmissionColors[ i ] = material.GetColor( EmissionColorId );
			_baseEmissionIntensities[ i ] = material.HasProperty( EmissionIntensityId ) ? material.GetFloat( EmissionIntensityId ) : 0f;
			_emissiveValid[ i ] = true;
		}
	}

	void RestoreEmissionBase()
	{
		if ( _resolvedEmissiveRenderers == null || _baseEmissionColors == null || _baseEmissionIntensities == null || _emissiveValid == null )
			return;

		int count = Mathf.Min( _resolvedEmissiveRenderers.Count, _baseEmissionColors.Length );
		count = Mathf.Min( count, _baseEmissionIntensities.Length );
		count = Mathf.Min( count, _emissiveValid.Length );
		for ( int i = 0; i < count; i++ )
		{
			if ( _emissiveValid[ i ] )
				SetEmission( _resolvedEmissiveRenderers[ i ], _baseEmissionColors[ i ], _baseEmissionIntensities[ i ] );
		}
	}

	public bool TryGetEmissionColor( out Color color )
	{
		ResolveActivePreset( force: true );
		if ( _activePreset == null || !_activePreset.affectEmissionColor )
		{
			color = Color.black;
			return false;
		}

		color = _activePreset.GetEmissionColor( 0.5f );
		return true;
	}

	public bool TryGetLightColor( out Color color )
	{
		ResolveActivePreset( force: true );
		if ( _activePreset != null && _activePreset.affectColor )
		{
			color = _activePreset.GetLightColor( 0.5f );
			return true;
		}

		if ( EnsureLight() )
		{
			color = _cachedLight.color;
			return true;
		}

		color = Color.white;
		return false;
	}

	void ApplyEmission( float intensityMul )
	{
		if ( _resolvedEmissiveRenderers == null || _baseEmissionColors == null || _baseEmissionIntensities == null || _emissiveValid == null )
			return;

		bool usePresetColor = _activePreset != null && _activePreset.affectEmissionColor;
		Color presetColor = usePresetColor ? _activePreset.GetEmissionColor( _smoothedNoise ) : Color.black;

		int count = Mathf.Min( _resolvedEmissiveRenderers.Count, _baseEmissionColors.Length );
		count = Mathf.Min( count, _baseEmissionIntensities.Length );
		count = Mathf.Min( count, _emissiveValid.Length );
		for ( int i = 0; i < count; i++ )
		{
			if ( !_emissiveValid[ i ] )
				continue;

			Color emission = usePresetColor ? presetColor : _baseEmissionColors[ i ];
			emission.r *= intensityMul;
			emission.g *= intensityMul;
			emission.b *= intensityMul;
			float emissionIntensity = usePresetColor ? 1f : _baseEmissionIntensities[ i ];
			SetEmission( _resolvedEmissiveRenderers[ i ], emission, emissionIntensity );
		}
	}

	void SetEmission( MeshRenderer renderer, Color emission, float emissionIntensity )
	{
		if ( renderer == null )
			return;

		EnsureCaches();
		renderer.GetPropertyBlock( _propertyBlock, _emissiveMaterialIndex );
		_propertyBlock.SetColor( EmissionColorId, emission );
		if ( renderer.sharedMaterials.Length > _emissiveMaterialIndex
			&& renderer.sharedMaterials[ _emissiveMaterialIndex ] != null
			&& renderer.sharedMaterials[ _emissiveMaterialIndex ].HasProperty( EmissionIntensityId ) )
			_propertyBlock.SetFloat( EmissionIntensityId, emissionIntensity );
		renderer.SetPropertyBlock( _propertyBlock, _emissiveMaterialIndex );
	}

	static float SampleLayeredNoise( float time, float speed, float seed, LightFlickerPreset preset )
	{
		float t = time * Mathf.Max( 0f, speed );
		float n1 = Mathf.PerlinNoise( t + seed, seed * 0.37f );
		float n2 = Mathf.PerlinNoise( t * preset.secondarySpeed + seed + 17.3f, seed * 0.71f + 4.1f );
		float n3 = Mathf.PerlinNoise( t * preset.tertiarySpeed + seed + 41.7f, seed * 1.13f + 9.7f );

		float w1 = preset.primaryWeight;
		float w2 = preset.secondaryWeight;
		float w3 = preset.tertiaryWeight;
		float sum = w1 + w2 + w3;
		if ( sum <= 0.0001f )
			return n1;

		return ( n1 * w1 + n2 * w2 + n3 * w3 ) / sum;
	}

	static float HashSeed( int instanceId )
	{
		unchecked
		{
			uint x = (uint)instanceId;
			x ^= x >> 16;
			x *= 0x7feb352du;
			x ^= x >> 15;
			x *= 0x846ca68bu;
			x ^= x >> 16;
			return ( x & 0xFFFF ) / 65535f * 100f + 1f;
		}
	}
}
