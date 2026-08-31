using System.Collections;

using FeedbackSystem;

using UnityEngine;
using UnityEngine.Rendering;

public enum LanternActivationMode
{
	Distance = 0,
	RevealOnly = 1,
	Manual = 2
}

/// <summary>
/// Drives lantern light, flicker, and emissive glow with fade-based ignite/extinguish.
/// </summary>
public class LanternActivator : MonoBehaviour
{
	public const string IntroLedgeRevealId = "intro_ledge_lanterns";

	static readonly int BaseColorId = Shader.PropertyToID( "_BaseColor" );
	static readonly int EmissionColorId = Shader.PropertyToID( "_EmissionColor" );
	static readonly int EmissionIntensityId = Shader.PropertyToID( "_EmissionIntensity" );

	[SerializeField]
	LanternActivationMode _activationMode = LanternActivationMode.Distance;

	[SerializeField]
	string _groupId;

	[SerializeField]
	string _revealId;

	[SerializeField]
	[Min( 0f )]
	float _activateDistance = 8f;

	[SerializeField]
	[Min( 0f )]
	float _deactivateDistance = 10f;

	[SerializeField]
	[Min( 0f )]
	float _fadeDuration = 0.75f;

	[SerializeField]
	[Tooltip( "When false, distance activation only ignites lanterns; they stay lit once activated." )]
	bool _allowDeactivate;

	[SerializeField]
	bool _startLit;

	[SerializeField]
	int _emissiveMaterialIndex = 1;

	[SerializeField]
	Light _light;

	[SerializeField]
	LightFlicker _lightFlicker;

	[SerializeField]
	Feedbacks _onIgnite;

	[SerializeField]
	Feedbacks _onExtinguish;

	MaterialPropertyBlock _propertyBlock;
	System.Collections.Generic.List<MeshRenderer> _emissiveRenderers;

	float _targetLightIntensity;
	Color _targetLightColor = Color.white;
	Color _targetBaseColor = Color.white;
	Color _targetEmissionColor = Color.black;
	float _targetEmissionIntensity = 1f;
	float _currentLitT;
	bool _distanceLitState;
	Coroutine _fadeRoutine;

	public LanternActivationMode ActivationMode => _activationMode;

	public string GroupId => _groupId;

	public string RevealId => _revealId;

	public float ActivateDistance => _activateDistance;

	public float DeactivateDistance => _deactivateDistance;

	public float FadeDuration => _fadeDuration;

	public bool AllowDeactivate => _allowDeactivate;

	public float CurrentLitT => _currentLitT;

	public bool IsFullyLit => _currentLitT >= 0.999f;

	public bool IsFullyUnlit => _currentLitT <= 0.001f;

	void Awake()
	{
		EnsureCaches();
		CacheComponents();
		CacheTargetValues();
		_distanceLitState = _startLit;
		if ( !_startLit )
			PrepareIgniteVisuals();
		ApplyLitState( _startLit ? 1f : 0f, immediate: true );
	}

	void Start()
	{
		RefreshCachedIntensity();
	}

	void OnEnable()
	{
		LanternActivatorRegistry.Register( this );
	}

	void OnDisable()
	{
		LanternActivatorRegistry.Unregister( this );
		if ( _fadeRoutine != null )
		{
			StopCoroutine( _fadeRoutine );
			_fadeRoutine = null;
		}
	}

	void EnsureCaches()
	{
		if ( _propertyBlock == null )
			_propertyBlock = new MaterialPropertyBlock();
		if ( _emissiveRenderers == null )
			_emissiveRenderers = new System.Collections.Generic.List<MeshRenderer>( 4 );
	}

	void CacheComponents()
	{
		EnsureCaches();
		if ( _light == null )
			_light = GetComponentInChildren<Light>( true );

		if ( _lightFlicker == null )
			_lightFlicker = GetComponent<LightFlicker>();
		if ( _lightFlicker == null && _light != null )
			_lightFlicker = _light.GetComponent<LightFlicker>();

		_emissiveRenderers.Clear();
		LODGroup lodGroup = GetComponent<LODGroup>();
		if ( lodGroup != null )
		{
			LOD[] lods = lodGroup.GetLODs();
			for ( int i = 0; i < lods.Length; i++ )
			{
				Renderer[] renderers = lods[ i ].renderers;
				for ( int j = 0; j < renderers.Length; j++ )
				{
					if ( renderers[ j ] is MeshRenderer meshRenderer && !_emissiveRenderers.Contains( meshRenderer ) )
						_emissiveRenderers.Add( meshRenderer );
				}
			}
		}

		if ( _emissiveRenderers.Count == 0 )
		{
			MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>( true );
			for ( int i = 0; i < renderers.Length; i++ )
				_emissiveRenderers.Add( renderers[ i ] );
		}
	}

	void CacheTargetValues()
	{
		RefreshCachedIntensity();

		if ( _light != null )
			_targetLightColor = _light.color;

		RefreshTargetColorsFromFlicker();
		CacheTargetBaseColor();
	}

	void CacheTargetBaseColor()
	{
		for ( int i = 0; i < _emissiveRenderers.Count; i++ )
		{
			MeshRenderer renderer = _emissiveRenderers[ i ];
			if ( renderer == null )
				continue;

			Material[] materials = renderer.sharedMaterials;
			if ( materials.Length == 0 )
				continue;

			int index = Mathf.Clamp( _emissiveMaterialIndex, 0, materials.Length - 1 );
			Material material = materials[ index ];
			if ( material != null && material.HasProperty( BaseColorId ) )
			{
				_targetBaseColor = material.GetColor( BaseColorId );
				return;
			}
		}
	}

	void RefreshCachedIntensity()
	{
		if ( _light != null && _light.intensity > 0.001f )
			_targetLightIntensity = _light.intensity;
	}

	public void FadeToLit( bool lit, float duration )
	{
		float target = lit ? 1f : 0f;
		if ( duration <= 0f )
		{
			SetLitInstant( lit );
			return;
		}

		BeginFade( target, duration );
	}

	public void SetLit( float t )
	{
		ApplyLitState( Mathf.Clamp01( t ), immediate: true );
	}

	public void SetLitInstant( bool lit )
	{
		if ( lit && !IsFullyLit )
			PrepareIgniteVisuals();

		ApplyLitState( lit ? 1f : 0f, immediate: true );
	}

	public void ApplyDistanceLitState( bool lit )
	{
		if ( !lit && !_allowDeactivate )
			return;

		if ( _distanceLitState == lit )
			return;

		_distanceLitState = lit;
		FadeToLit( lit, _fadeDuration );
	}

	public void DebugReset()
	{
		if ( _fadeRoutine != null )
		{
			StopCoroutine( _fadeRoutine );
			_fadeRoutine = null;
		}

		_distanceLitState = _startLit;
		if ( !_startLit )
			PrepareIgniteVisuals();
		ApplyLitState( _startLit ? 1f : 0f, immediate: true );
	}

	void BeginFade( float targetT, float duration )
	{
		bool towardLit = targetT > _currentLitT + 0.001f;
		bool towardUnlit = targetT < _currentLitT - 0.001f;
		if ( towardLit && !IsFullyLit )
		{
			PrepareIgniteVisuals();
			PlayTransitionFeedback( true );
		}
		else if ( towardUnlit && !IsFullyUnlit )
			PlayTransitionFeedback( false );

		if ( _fadeRoutine != null )
			StopCoroutine( _fadeRoutine );

		_fadeRoutine = StartCoroutine( FadeRoutine( targetT, duration ) );
	}

	void PrepareIgniteVisuals()
	{
		RefreshTargetColorsFromFlicker();
		PrepareLightVisuals( 0f, enabled: false );
		SetFlickerActive( false );
		ApplyEmissiveFade( 0f );
	}

	void RefreshTargetColorsFromFlicker()
	{
		if ( _lightFlicker != null && _lightFlicker.TryGetLightColor( out Color flickerLightColor ) )
			_targetLightColor = flickerLightColor;

		if ( _lightFlicker != null && _lightFlicker.TryGetEmissionColor( out Color flickerEmission ) )
		{
			_targetEmissionColor = flickerEmission;
			_targetEmissionIntensity = 1f;
		}
	}

	void PrepareLightVisuals( float intensityScale, bool enabled )
	{
		if ( _light == null )
			return;

		_light.color = _targetLightColor;
		_light.intensity = _targetLightIntensity * intensityScale;
		_light.enabled = enabled && intensityScale > 0.001f;
	}

	void SetFlickerActive( bool active )
	{
		if ( _lightFlicker == null || _lightFlicker.enabled == active )
			return;

		_lightFlicker.enabled = active;
		if ( !active )
			PrepareLightVisuals( _currentLitT, _currentLitT > 0.001f );
	}

	void ApplyEmissiveFade( float t )
	{
		EnsureCaches();
		float litT = Mathf.Clamp01( t );
		bool lit = litT > 0.001f;
		Color baseColor = lit ? Color.Lerp( Color.black, _targetBaseColor, litT ) : Color.black;
		Color emission = lit ? _targetEmissionColor : Color.black;
		float emissionIntensity = lit ? _targetEmissionIntensity * litT : 0f;
		for ( int i = 0; i < _emissiveRenderers.Count; i++ )
		{
			MeshRenderer renderer = _emissiveRenderers[ i ];
			if ( renderer == null )
				continue;

			renderer.GetPropertyBlock( _propertyBlock, _emissiveMaterialIndex );
			_propertyBlock.SetColor( BaseColorId, baseColor );
			_propertyBlock.SetColor( EmissionColorId, emission );
			_propertyBlock.SetFloat( EmissionIntensityId, emissionIntensity );
			renderer.SetPropertyBlock( _propertyBlock, _emissiveMaterialIndex );
		}
	}

	IEnumerator FadeRoutine( float targetT, float duration )
	{
		float startT = _currentLitT;
		float elapsed = 0f;

		while ( elapsed < duration )
		{
			elapsed += Time.deltaTime;
			float normalized = duration > 0f ? Mathf.Clamp01( elapsed / duration ) : 1f;
			ApplyLitState( Mathf.Lerp( startT, targetT, normalized ), immediate: true );
			yield return null;
		}

		ApplyLitState( targetT, immediate: true );
		_fadeRoutine = null;
	}

	void ApplyLitState( float t, bool immediate )
	{
		_currentLitT = Mathf.Clamp01( t );
		bool lit = _currentLitT > 0.001f;
		bool fullyLit = _currentLitT >= 0.999f;

		PrepareLightVisuals( _currentLitT, lit );

		if ( fullyLit )
		{
			ApplyEmissiveFade( 1f );
			if ( _lightFlicker != null && !_lightFlicker.enabled )
			{
				PrepareLightVisuals( 1f, true );
				_lightFlicker.enabled = true;
				_lightFlicker.RecaptureLightBase();
			}
		}
		else
		{
			SetFlickerActive( false );
			ApplyEmissiveFade( _currentLitT );
		}

		if ( immediate && fullyLit && _lightFlicker != null && _lightFlicker.enabled )
			_lightFlicker.RecaptureLightBase();
	}

	void PlayTransitionFeedback( bool ignite )
	{
		Feedbacks feedbacks = ignite ? _onIgnite : _onExtinguish;
		if ( feedbacks == null )
			return;

		FeedbackContext context = new FeedbackContext();
		context.Source = gameObject;
		context.Position = transform.position;
		feedbacks.Play( context );
	}
}
