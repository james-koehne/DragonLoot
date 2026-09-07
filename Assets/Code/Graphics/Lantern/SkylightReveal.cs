using System;
using System.Collections;

using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Fades roof skylight lights, sky portal intensity, and light-ray density from zero.
/// Per-instance reveal timing/punch is read by <see cref="LanternRevealSweepController"/>.
/// </summary>
public class SkylightReveal : MonoBehaviour
{
	static readonly int SkyIntensityId = Shader.PropertyToID( "_SkyIntensity" );
	static readonly int DensityId = Shader.PropertyToID( "_Density" );

	[SerializeField]
	Light[] _lights;

	[SerializeField]
	Renderer _skyPortalRenderer;

	[SerializeField]
	Renderer _lightRaysRenderer;

	[Header( "Reveal Timing" )]
	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Seconds after reveal start before this skylight begins fading in." )]
	float _startDelay;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Seconds for this skylight to fade from 0 to 1. 0 = use LanternRevealSweepController default / world-event override." )]
	float _fadeDuration;

	[SerializeField]
	[Tooltip( "Overshoot punch for this skylight. Timeline is relative to this skylight's start delay. Peak <= 1 disables punch." )]
	RevealPunchChannel _punch = new RevealPunchChannel
	{
		rise = 0.1f,
		hold = 0.15f,
		fall = 0.8f,
		peak = 2.5f
	};

	MaterialPropertyBlock _propertyBlock;
	float[] _targetLightIntensities = Array.Empty<float>();
	float _targetSkyIntensity;
	float _targetDensity;
	float _currentT;
	float _overshootScale = 1f;
	Coroutine _fadeRoutine;

	public float CurrentRevealT => _currentT;

	public float CurrentOvershootScale => _overshootScale;

	public float StartDelay => _startDelay;

	public float FadeDuration => _fadeDuration;

	public RevealPunchChannel Punch => _punch;

	void Awake()
	{
		_propertyBlock = new MaterialPropertyBlock();
		CacheTargets();
		ApplyReveal( 0f );
	}

	void CacheTargets()
	{
		if ( _lights == null )
			_lights = Array.Empty<Light>();

		_targetLightIntensities = new float[ _lights.Length ];
		for ( int i = 0; i < _lights.Length; i++ )
		{
			Light light = _lights[ i ];
			_targetLightIntensities[ i ] = light != null ? light.intensity : 0f;
		}

		_targetSkyIntensity = ReadRendererFloat( _skyPortalRenderer, SkyIntensityId, 1f );
		_targetDensity = ReadRendererFloat( _lightRaysRenderer, DensityId, 0.1f );
	}

	static float ReadRendererFloat( Renderer renderer, int propertyId, float fallback )
	{
		if ( renderer == null )
			return fallback;

		Material material = renderer.sharedMaterial;
		if ( material != null && material.HasProperty( propertyId ) )
			return material.GetFloat( propertyId );

		return fallback;
	}

	public void SetReveal( float t )
	{
		if ( _fadeRoutine != null )
		{
			StopCoroutine( _fadeRoutine );
			_fadeRoutine = null;
		}

		ApplyReveal( Mathf.Clamp01( t ) );
	}

	/// <summary>Multiplies authored skylight values on top of <see cref="SetReveal"/>. Rest = 1.</summary>
	public void SetOvershootScale( float scale )
	{
		_overshootScale = Mathf.Max( 0f, scale );
		ApplyReveal( _currentT );
	}

	public void FadeIn( float duration, Action onComplete )
	{
		if ( _fadeRoutine != null )
			StopCoroutine( _fadeRoutine );

		_fadeRoutine = StartCoroutine( FadeRoutine( 1f, duration, onComplete ) );
	}

	public void FadeOut( float duration, Action onComplete )
	{
		if ( _fadeRoutine != null )
			StopCoroutine( _fadeRoutine );

		_fadeRoutine = StartCoroutine( FadeRoutine( 0f, duration, onComplete ) );
	}

	IEnumerator FadeRoutine( float targetT, float duration, Action onComplete )
	{
		float startT = _currentT;
		float elapsed = 0f;

		while ( elapsed < duration )
		{
			elapsed += Time.deltaTime;
			float normalized = duration > 0f ? Mathf.Clamp01( elapsed / duration ) : 1f;
			ApplyReveal( Mathf.Lerp( startT, targetT, normalized ) );
			yield return null;
		}

		ApplyReveal( targetT );
		_fadeRoutine = null;
		onComplete?.Invoke();
	}

	void ApplyReveal( float t )
	{
		_currentT = Mathf.Clamp01( t );
		float scale = _currentT * _overshootScale;

		for ( int i = 0; i < _lights.Length; i++ )
		{
			Light light = _lights[ i ];
			if ( light == null )
				continue;

			float intensity = _targetLightIntensities[ i ] * scale;
			light.intensity = intensity;
			light.enabled = intensity > 0.001f;
		}

		SetRendererReveal( _skyPortalRenderer, SkyIntensityId, _targetSkyIntensity );
		SetRendererReveal( _lightRaysRenderer, DensityId, _targetDensity );
	}

	void SetRendererReveal( Renderer renderer, int propertyId, float targetValue )
	{
		if ( renderer == null )
			return;

		float value = targetValue * _currentT * _overshootScale;
		bool visible = _currentT > 0.001f;

		if ( _propertyBlock == null )
			_propertyBlock = new MaterialPropertyBlock();

		renderer.GetPropertyBlock( _propertyBlock );
		_propertyBlock.SetFloat( propertyId, value );
		renderer.SetPropertyBlock( _propertyBlock );
		renderer.enabled = visible;
	}
}
