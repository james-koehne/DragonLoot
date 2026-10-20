using System.Collections.Generic;

using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.SceneManagement;
#endif

/// <summary>
/// Drives a <see cref="Light"/> with organic intensity / color / range flicker from
/// <see cref="LightFlickerDefinition"/> presets, mirrors that onto EnvironmentLit
/// emissive materials (typically the second material on each mesh), and tints an
/// optional SoftVolume volumetric mesh from the flickering light color.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class LightFlicker : MonoBehaviour
{
	static readonly int EmissionColorId = Shader.PropertyToID( "_EmissionColor" );
	static readonly int EmissionIntensityId = Shader.PropertyToID( "_EmissionIntensity" );
	static readonly int SoftVolumeColorId = Shader.PropertyToID( "_Color" );
	const string DefaultVolumetricChildName = "VolumetricLight";

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
	[Tooltip( "Auto finds nearby emissive meshes. Manual uses only the assigned renderers (which can be a different object). None flickers the Light only." )]
	LightFlickerEmissiveMode _emissiveMode = LightFlickerEmissiveMode.Auto;

	[SerializeField]
	[Tooltip( "Glow meshes used when Emissive Mode is Manual. Can be any MeshRenderer, not the light's own mesh." )]
	MeshRenderer[] _emissiveRenderers;

	[SerializeField]
	[Tooltip( "Material slot with the emissive DragonLoot/EnvironmentLit material. Use -1 to pick the first emissive slot on each renderer." )]
	int _emissiveMaterialIndex = 1;

	[SerializeField]
	[Tooltip( "Optional SoftVolume mesh renderer. When empty, looks for a child named VolumetricLight. That child is kept inactive in edit mode (avoids Scene picking) and enabled at runtime." )]
	MeshRenderer _volumetricRenderer;

	[SerializeField, HideInInspector]
	float _authoredIntensity = 1f;

	[SerializeField, HideInInspector]
	Color _authoredColor = Color.white;

	[SerializeField, HideInInspector]
	float _authoredRange = 10f;

	[SerializeField, HideInInspector]
	bool _hasAuthoredBase;

	Light _cachedLight;
	MeshRenderer _cachedVolumetricRenderer;
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
	Color _baseVolumetricColor = Color.white;
	bool _hasVolumetricBase;
	int _lastPresetFingerprint = int.MinValue;
	bool _emissiveResolved;

	public bool PlayInEditMode
	{
		get => _playInEditMode;
		set => _playInEditMode = value;
	}

	public float AuthoredIntensity => _authoredIntensity;

	public float AuthoredRange => _authoredRange;

	public bool HasAuthoredBase => _hasAuthoredBase;

	public LightFlickerPreset ActivePreset => _activePreset;

	public LightFlickerEmissiveMode EmissiveMode => _emissiveMode;

	public int EmissiveMaterialIndex => _emissiveMaterialIndex;

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
		EnsureVolumetricRenderer();
		SyncVolumetricActiveForMode();
		ResolveEmissiveRenderers();
		CaptureBase( forceFromLight: false );
		_noiseSeed = HashSeed( GetEntityId().GetHashCode() );
		_smoothedNoise = 0.5f;
		ResolveActivePreset( force: true );

#if UNITY_EDITOR
		if ( !Application.isPlaying && !_playInEditMode )
		{
			RestoreBase();
			return;
		}
#endif

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
		_emissiveMaterialIndex = Mathf.Max( -1, _emissiveMaterialIndex );
		_emissiveResolved = false;
		if ( string.IsNullOrWhiteSpace( _customPresetName ) )
			_customPresetName = nameof( LightFlickerPresetKind.Lantern );

		EnsureCaches();
		EnsureLight();
		EnsureVolumetricRenderer();
		SyncVolumetricActiveForMode();
		if ( isActiveAndEnabled )
			ResolveEmissiveRenderers();

		if ( !Application.isPlaying && !_playInEditMode )
		{
			RestoreBase();
			return;
		}

		if ( isActiveAndEnabled )
		{
			if ( !_hasAuthoredBase )
				CaptureBase( forceFromLight: false );
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
		if ( !_emissiveResolved )
		{
			ResolveEmissiveRenderers();
			if ( _hasAuthoredBase )
				CaptureEmissionBase();
		}

		if ( !_hasAuthoredBase )
			CaptureBase( forceFromLight: false );

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
		_hasAuthoredBase = false;
		EnsureVolumetricRenderer();
		ResolveEmissiveRenderers();
		CaptureBase( forceFromLight: true );
		ResolveActivePreset( force: true );
		ApplyFlicker( GetFlickerTime(), immediate: true );
	}

	public void RestoreAuthoredState()
	{
		RestoreBase();
	}

	public int GetPresetFingerprint()
	{
		return ComputePresetFingerprint();
	}

	public void RefreshEditorPreview()
	{
#if UNITY_EDITOR
		if ( Application.isPlaying )
			return;

		ResolveEmissiveRenderers();
		if ( !_hasAuthoredBase )
			CaptureBase( forceFromLight: false );
		else
			CaptureEmissionBase();

		int fingerprint = ComputePresetFingerprint();
		if ( fingerprint != _lastPresetFingerprint )
		{
			_lastPresetFingerprint = fingerprint;
			_smoothedNoise = 0.5f;
		}

		ResolveActivePreset( force: true );
		ApplyFlicker( GetFlickerTime(), immediate: true );
		UnityEditor.SceneView.RepaintAll();
#endif
	}

	LightFlickerDefinition ResolveDefinition()
	{
		if ( _definition != null )
			return _definition;

#if UNITY_EDITOR
		if ( !Application.isPlaying )
			return UnityEditor.AssetDatabase.LoadAssetAtPath<LightFlickerDefinition>( LightFlickerDefinition.DefaultAssetPath );
#endif

		return RuntimeDefinition.Resolve( ref _definition );
	}

	int ComputePresetFingerprint()
	{
		ResolveActivePreset( force: true );
		if ( _activePreset == null )
			return 0;

		unchecked
		{
			int hash = 17;
			hash = hash * 31 + ( _activePresetId != null ? _activePresetId.GetHashCode() : 0 );
			hash = hash * 31 + _activePreset.intensityMin.GetHashCode();
			hash = hash * 31 + _activePreset.intensityMax.GetHashCode();
			hash = hash * 31 + _activePreset.speed.GetHashCode();
			hash = hash * 31 + _activePreset.smoothness.GetHashCode();
			hash = hash * 31 + _activePreset.affectColor.GetHashCode();
			hash = hash * 31 + _activePreset.affectEmissionColor.GetHashCode();
			hash = hash * 31 + _activePreset.affectRange.GetHashCode();
			hash = hash * 31 + _intensityScale.GetHashCode();
			hash = hash * 31 + _speedScale.GetHashCode();
			return hash;
		}
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

	void CaptureBase( bool forceFromLight )
	{
		if ( !EnsureLight() )
			return;

		// Keep serialized authored values across domain reload / edit-mode preview so we
		// never treat mid-flicker Light properties as the saved baseline.
		if ( forceFromLight || !_hasAuthoredBase )
		{
			_authoredIntensity = _cachedLight.intensity;
			_authoredColor = _cachedLight.color;
			_authoredRange = _cachedLight.range;
			_hasAuthoredBase = true;
		}

		CaptureEmissionBase();
		CaptureVolumetricBase();
	}

	void RestoreBase()
	{
		if ( !_hasAuthoredBase )
			return;

#if UNITY_EDITOR
		Scene scene = default;
		bool wasSceneDirty = false;
		if ( !Application.isPlaying )
			BeginEditModePreviewMutation( out scene, out wasSceneDirty );
#endif

		if ( EnsureLight() )
			ApplyLightValues( _authoredIntensity, _authoredColor, _authoredRange );

		RestoreEmissionBase();
		RestoreVolumetricBase();

#if UNITY_EDITOR
		if ( !Application.isPlaying )
			EndEditModePreviewMutation( scene, wasSceneDirty, _cachedLight, _cachedLight != null ? _cachedLight.gameObject : null );
#endif
	}

	void ResolveActivePreset( bool force )
	{
#if UNITY_EDITOR
		if ( !Application.isPlaying )
		{
			string editorDesiredId = ResolvePresetId();
			LightFlickerDefinition editorDefinition = ResolveDefinition();
			_hadDefinition = editorDefinition != null;
			_activePresetId = editorDesiredId;
			_activePreset = editorDefinition != null
				? editorDefinition.GetPresetOrDefault( editorDesiredId )
				: FallbackPreset( editorDesiredId );
			return;
		}
#endif

		string desiredId = ResolvePresetId();
		LightFlickerDefinition definition = ResolveDefinition();
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

#if UNITY_EDITOR
		Scene scene = default;
		bool wasSceneDirty = false;
		if ( !Application.isPlaying )
			BeginEditModePreviewMutation( out scene, out wasSceneDirty );
#endif

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

		float intensity = Mathf.Max( 0f, _authoredIntensity * intensityMul );
		Color color = _activePreset.affectColor ? _activePreset.GetLightColor( _smoothedNoise ) : _authoredColor;
		float range = _authoredRange;
		if ( _activePreset.affectRange )
		{
			float rangeMul = Mathf.Lerp( _activePreset.rangeMin, _activePreset.rangeMax, _smoothedNoise );
			range = Mathf.Max( 0.01f, _authoredRange * rangeMul );
		}

		ApplyLightValues( intensity, color, range );
		ApplyEmission( intensityMul );
		ApplyVolumetricColor();

#if UNITY_EDITOR
		if ( !Application.isPlaying )
			EndEditModePreviewMutation( scene, wasSceneDirty, _cachedLight, _cachedLight.gameObject );
#endif
	}

	void ApplyLightValues( float intensity, Color color, float range )
	{
		if ( _cachedLight == null )
			return;

		// Avoid no-op writes — Unity marks the scene dirty even when values are unchanged.
		if ( !Mathf.Approximately( _cachedLight.intensity, intensity ) )
			_cachedLight.intensity = intensity;
		if ( _cachedLight.color != color )
			_cachedLight.color = color;
		if ( !Mathf.Approximately( _cachedLight.range, range ) )
			_cachedLight.range = range;
	}

	void EnsureCaches()
	{
		if ( _propertyBlock == null )
			_propertyBlock = new MaterialPropertyBlock();
		if ( _resolvedEmissiveRenderers == null )
			_resolvedEmissiveRenderers = new List<MeshRenderer>( 4 );
	}

	void EnsureVolumetricRenderer()
	{
		if ( _volumetricRenderer != null )
		{
			_cachedVolumetricRenderer = _volumetricRenderer;
			return;
		}

		if ( _cachedVolumetricRenderer != null )
			return;

		// Cache only — do not write the serialized override field (that dirties the scene).
		Transform child = transform.Find( DefaultVolumetricChildName );
		if ( child != null )
			_cachedVolumetricRenderer = child.GetComponent<MeshRenderer>();
	}

	/// <summary>
	/// SoftVolume meshes steal Scene picks; keep them off outside play mode and on at runtime.
	/// </summary>
	void SyncVolumetricActiveForMode()
	{
		EnsureVolumetricRenderer();
		if ( _cachedVolumetricRenderer == null )
			return;

		GameObject volumetricObject = _cachedVolumetricRenderer.gameObject;
		bool wantActive = Application.isPlaying;
		if ( volumetricObject.activeSelf == wantActive )
			return;

#if UNITY_EDITOR
		Scene scene = default;
		bool wasSceneDirty = false;
		if ( !Application.isPlaying )
			BeginEditModePreviewMutation( out scene, out wasSceneDirty );
#endif

		volumetricObject.SetActive( wantActive );

#if UNITY_EDITOR
		if ( !Application.isPlaying )
			EndEditModePreviewMutation( scene, wasSceneDirty, volumetricObject );
#endif
	}

	public void CopyResolvedEmissiveRenderers( List<MeshRenderer> results )
	{
		if ( results == null )
			return;

		ResolveEmissiveRenderers();
		results.Clear();
		for ( int i = 0; i < _resolvedEmissiveRenderers.Count; i++ )
		{
			if ( _resolvedEmissiveRenderers[ i ] != null )
				results.Add( _resolvedEmissiveRenderers[ i ] );
		}
	}

	public int ResolvedEmissiveRendererCount => _resolvedEmissiveRenderers != null ? _resolvedEmissiveRenderers.Count : 0;

	public MeshRenderer GetResolvedEmissiveRenderer( int index )
	{
		if ( _resolvedEmissiveRenderers == null || index < 0 || index >= _resolvedEmissiveRenderers.Count )
			return null;
		return _resolvedEmissiveRenderers[ index ];
	}

	void ResolveEmissiveRenderers()
	{
		EnsureCaches();
		EnsureVolumetricRenderer();

		List<MeshRenderer> previous = null;
		if ( _resolvedEmissiveRenderers.Count > 0 )
		{
			previous = new List<MeshRenderer>( _resolvedEmissiveRenderers );
		}

		_resolvedEmissiveRenderers.Clear();
		_emissiveResolved = true;

		if ( _emissiveMode == LightFlickerEmissiveMode.None )
		{
			ClearEmissionOverrides( previous );
			return;
		}

		if ( _emissiveMode == LightFlickerEmissiveMode.Manual )
		{
			if ( _emissiveRenderers != null )
			{
				for ( int i = 0; i < _emissiveRenderers.Length; i++ )
					TryAddEmissiveRenderer( _emissiveRenderers[ i ] );
			}

			ClearEmissionOverrides( previous );
			return;
		}

		if ( HasAssignedEmissiveRenderers() )
		{
			for ( int i = 0; i < _emissiveRenderers.Length; i++ )
				TryAddEmissiveRenderer( _emissiveRenderers[ i ] );

			ClearEmissionOverrides( previous );
			return;
		}

		LODGroup lodGroup = GetComponent<LODGroup>();
		if ( lodGroup == null && transform.parent != null )
			lodGroup = transform.parent.GetComponent<LODGroup>();

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
					if ( renderers[ j ] is MeshRenderer meshRenderer )
						TryAddEmissiveRenderer( meshRenderer );
				}
			}

			if ( _resolvedEmissiveRenderers.Count > 0 )
			{
				ClearEmissionOverrides( previous );
				return;
			}
		}

		CollectChildEmissiveRenderers( transform );
		if ( _resolvedEmissiveRenderers.Count == 0 && transform.parent != null )
			CollectChildEmissiveRenderers( transform.parent );

		ClearEmissionOverrides( previous );
	}

	void ClearEmissionOverrides( List<MeshRenderer> previous )
	{
		if ( previous == null )
			return;

		for ( int i = 0; i < previous.Count; i++ )
		{
			MeshRenderer renderer = previous[ i ];
			if ( renderer == null || _resolvedEmissiveRenderers.Contains( renderer ) )
				continue;

			ClearEmissionOverride( renderer );
		}
	}

	void ClearEmissionOverride( MeshRenderer renderer )
	{
		if ( renderer == null )
			return;

		if ( TryResolveEmissiveMaterial( renderer, out int materialIndex, out _ ) )
			renderer.SetPropertyBlock( null, materialIndex );
		else
			renderer.SetPropertyBlock( null );
	}

	bool HasAssignedEmissiveRenderers()
	{
		if ( _emissiveRenderers == null )
			return false;

		for ( int i = 0; i < _emissiveRenderers.Length; i++ )
		{
			if ( _emissiveRenderers[ i ] != null )
				return true;
		}

		return false;
	}

	void CollectChildEmissiveRenderers( Transform root )
	{
		if ( root == null )
			return;

		MeshRenderer[] found = root.GetComponentsInChildren<MeshRenderer>( true );
		for ( int i = 0; i < found.Length; i++ )
			TryAddEmissiveRenderer( found[ i ] );
	}

	void TryAddEmissiveRenderer( MeshRenderer renderer )
	{
		if ( renderer == null || renderer == _cachedVolumetricRenderer )
			return;
		if ( _resolvedEmissiveRenderers.Contains( renderer ) )
			return;
		if ( !TryResolveEmissiveMaterial( renderer, out _, out _ ) )
			return;

		_resolvedEmissiveRenderers.Add( renderer );
	}

	bool TryResolveEmissiveMaterial( MeshRenderer renderer, out int materialIndex, out Material material )
	{
		materialIndex = -1;
		material = null;
		if ( renderer == null )
			return false;

		Material[] materials = renderer.sharedMaterials;
		if ( materials == null || materials.Length == 0 )
			return false;

		if ( _emissiveMaterialIndex >= 0 && _emissiveMaterialIndex < materials.Length )
		{
			Material candidate = materials[ _emissiveMaterialIndex ];
			if ( candidate != null && candidate.HasProperty( EmissionColorId ) )
			{
				materialIndex = _emissiveMaterialIndex;
				material = candidate;
				return true;
			}

			return false;
		}

		for ( int i = 0; i < materials.Length; i++ )
		{
			Material candidate = materials[ i ];
			if ( candidate == null || !candidate.HasProperty( EmissionColorId ) )
				continue;

			materialIndex = i;
			material = candidate;
			return true;
		}

		return false;
	}

	void CaptureVolumetricBase()
	{
		EnsureVolumetricRenderer();
		_hasVolumetricBase = false;
		if ( _cachedVolumetricRenderer == null )
			return;

		Material material = _cachedVolumetricRenderer.sharedMaterial;
		if ( material == null || !material.HasProperty( SoftVolumeColorId ) )
			return;

		_baseVolumetricColor = material.GetColor( SoftVolumeColorId );
		_hasVolumetricBase = true;
	}

	void RestoreVolumetricBase()
	{
		if ( !_hasVolumetricBase || _cachedVolumetricRenderer == null )
			return;

		SetVolumetricColor( _baseVolumetricColor );
	}

	void ApplyVolumetricColor()
	{
		if ( _cachedVolumetricRenderer == null )
		{
			EnsureVolumetricRenderer();
			if ( _cachedVolumetricRenderer == null )
				return;
		}

		Color color = _cachedLight.color;
		float intensity = Mathf.Max( 0f, _cachedLight.intensity );
		color.r *= intensity;
		color.g *= intensity;
		color.b *= intensity;
		color.a = 1f;
		SetVolumetricColor( color );
	}

	void SetVolumetricColor( Color color )
	{
		if ( _cachedVolumetricRenderer == null )
			return;

		EnsureCaches();
		_cachedVolumetricRenderer.GetPropertyBlock( _propertyBlock );
		_propertyBlock.SetColor( SoftVolumeColorId, color );
		_cachedVolumetricRenderer.SetPropertyBlock( _propertyBlock );
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

			if ( !TryResolveEmissiveMaterial( renderer, out _, out Material material ) )
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
		if ( !TryResolveEmissiveMaterial( renderer, out int materialIndex, out Material material ) )
			return;

		EnsureCaches();
		renderer.GetPropertyBlock( _propertyBlock, materialIndex );
		_propertyBlock.SetColor( EmissionColorId, emission );
		if ( material.HasProperty( EmissionIntensityId ) )
			_propertyBlock.SetFloat( EmissionIntensityId, emissionIntensity );
		renderer.SetPropertyBlock( _propertyBlock, materialIndex );
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

#if UNITY_EDITOR
	void BeginEditModePreviewMutation( out Scene scene, out bool wasSceneDirty )
	{
		EditModePreviewDirtyUtil.BeginMutation( gameObject, out scene, out wasSceneDirty );
	}

	static void EndEditModePreviewMutation( Scene scene, bool wasSceneDirty, params Object[] objects )
	{
		EditModePreviewDirtyUtil.EndMutation( scene, wasSceneDirty, objects );
	}
#endif
}

public enum LightFlickerEmissiveMode
{
	Auto = 0,
	Manual = 1,
	None = 2
}
