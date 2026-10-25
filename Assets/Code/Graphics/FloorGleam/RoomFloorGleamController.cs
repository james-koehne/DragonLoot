using System.Collections;

using UnityEngine;

/// <summary>
/// Room-complete floor FX director for <c>DragonLoot/FloorGleam</c>:
/// whole-mesh area glow that fades, mid-fade world-space shine swipe, temporary dust motes.
/// Renderer stays disabled when idle so save-restored rooms do not show a stuck gleam.
/// </summary>
[DisallowMultipleComponent]
public class RoomFloorGleamController : MonoBehaviour
{
	static readonly int ColorId = Shader.PropertyToID( "_Color" );
	static readonly int AreaGlowId = Shader.PropertyToID( "_AreaGlow" );
	static readonly int GleamOriginId = Shader.PropertyToID( "_GleamOrigin" );
	static readonly int GleamAxisId = Shader.PropertyToID( "_GleamAxis" );
	static readonly int GleamSpanId = Shader.PropertyToID( "_GleamSpan" );
	static readonly int GleamTId = Shader.PropertyToID( "_GleamT" );
	static readonly int BandWidthId = Shader.PropertyToID( "_BandWidth" );
	static readonly int BandSoftnessId = Shader.PropertyToID( "_BandSoftness" );
	static readonly int LineCountId = Shader.PropertyToID( "_LineCount" );
	static readonly int LineSpacingId = Shader.PropertyToID( "_LineSpacing" );

	[Tooltip( "Overlay mesh using DragonLoot/FloorGleam. Empty = renderer on this object." )]
	[SerializeField]
	MeshRenderer _renderer;

	[Tooltip( "Sweep forward (XZ). Empty = this transform." )]
	[SerializeField]
	Transform _sweepReference;

	[SerializeField]
	[Min( 0.1f )]
	float _duration = 2.25f;

	[SerializeField]
	AnimationCurve _areaGlowCurve = AnimationCurve.EaseInOut( 0f, 1f, 1f, 0f );

	[Tooltip( "Normalized time (0-1 of duration) when the world swipe begins." )]
	[SerializeField]
	[Range( 0f, 1f )]
	float _swipeStart = 0.4f;

	[SerializeField]
	[Min( 0.05f )]
	float _swipeDuration = 0.9f;

	[SerializeField]
	[ColorUsage( true, true )]
	Color _color = new Color( 1.35f, 0.95f, 0.45f, 1f );

	[SerializeField]
	[Range( 0.005f, 0.4f )]
	float _bandWidth = 0.08f;

	[SerializeField]
	[Range( 0.001f, 0.25f )]
	float _bandSoftness = 0.04f;

	[SerializeField]
	[Range( 1, 8 )]
	int _lineCount = 3;

	[SerializeField]
	[Range( 0f, 0.35f )]
	float _lineSpacing = 0.07f;

	[Header( "Motes" )]
	[SerializeField]
	ParticleSystem _motes;

	[SerializeField]
	[Min( 0f )]
	float _moteBaseEmission = 12f;

	[SerializeField]
	[Min( 0f )]
	float _moteSwipeBoostEmission = 35f;

	[SerializeField]
	[Min( 0.01f )]
	float _moteBaseSize = 0.08f;

	[SerializeField]
	[Min( 0.01f )]
	float _moteSwipeBoostSize = 0.14f;

	MaterialPropertyBlock _block;
	Coroutine _routine;
	bool _playing;
	float _moteBaseRateOverTime;
	bool _capturedMoteDefaults;

	public bool IsPlaying => _playing;

	public MeshRenderer Renderer => ResolveRenderer();

	public ParticleSystem Motes => _motes;

	void Awake()
	{
		CaptureMoteDefaults();
		EnsureHidden();
		StopMotes( clear: true );
	}

	void OnDisable()
	{
		Stop();
	}

	/// <summary>Starts the glow + swipe + mote sequence. Restarts if already playing.</summary>
	public void Play()
	{
		if ( !isActiveAndEnabled )
			return;

		StopRoutineOnly();
		_routine = StartCoroutine( PlayRoutine() );
	}

	public void Stop()
	{
		StopRoutineOnly();
		EnsureHidden();
		StopMotes( clear: true );
	}

	void StopRoutineOnly()
	{
		if ( _routine != null )
		{
			StopCoroutine( _routine );
			_routine = null;
		}

		_playing = false;
	}

	IEnumerator PlayRoutine()
	{
		MeshRenderer renderer = ResolveRenderer();
		if ( renderer == null )
			yield break;

		CaptureMoteDefaults();
		EnsureBlock();
		renderer.enabled = true;
		_playing = true;

		Transform reference = ResolveSweepReference();
		Vector3 axis = reference.forward;
		axis.y = 0f;
		if ( axis.sqrMagnitude < 0.0001f )
			axis = Vector3.right;
		axis.Normalize();

		Vector3 center = renderer.bounds.center;
		float span = MeasureSpan( renderer, center, axis );
		Vector3 origin = center - axis * ( span * 0.5f );

		StartMotes();

		float duration = Mathf.Max( 0.1f, _duration );
		float swipeStartTime = Mathf.Clamp01( _swipeStart ) * duration;
		float swipeLen = Mathf.Max( 0.05f, _swipeDuration );
		float elapsed = 0f;

		ApplyProps( renderer, origin, axis, span, areaGlow: 1f, gleamT: 0f );
		ApplyMoteIntensity( areaGlow: 1f, swipeBoost: 0f );

		while ( elapsed < duration )
		{
			elapsed += Time.deltaTime;
			float normalized = Mathf.Clamp01( elapsed / duration );
			float areaGlow = _areaGlowCurve != null ? Mathf.Clamp01( _areaGlowCurve.Evaluate( normalized ) ) : 1f - normalized;

			float gleamT = 0f;
			float swipeBoost = 0f;
			if ( elapsed >= swipeStartTime )
			{
				float swipeNorm = Mathf.Clamp01( ( elapsed - swipeStartTime ) / swipeLen );
				gleamT = swipeNorm;
				// Peak boost mid-swipe.
				swipeBoost = 1f - Mathf.Abs( swipeNorm * 2f - 1f );
			}

			ApplyProps( renderer, origin, axis, span, areaGlow, gleamT );
			ApplyMoteIntensity( areaGlow, swipeBoost );
			yield return null;
		}

		ApplyProps( renderer, origin, axis, span, areaGlow: 0f, gleamT: 1f );
		_playing = false;
		_routine = null;
		EnsureHidden();
		StopMotes( clear: true );
	}

	void ApplyProps( MeshRenderer renderer, Vector3 origin, Vector3 axis, float span, float areaGlow, float gleamT )
	{
		EnsureBlock();
		renderer.GetPropertyBlock( _block );
		_block.SetColor( ColorId, _color );
		_block.SetFloat( AreaGlowId, areaGlow );
		_block.SetVector( GleamOriginId, new Vector4( origin.x, origin.y, origin.z, 0f ) );
		_block.SetVector( GleamAxisId, new Vector4( axis.x, axis.y, axis.z, 0f ) );
		_block.SetFloat( GleamSpanId, span );
		_block.SetFloat( GleamTId, gleamT );
		_block.SetFloat( BandWidthId, _bandWidth );
		_block.SetFloat( BandSoftnessId, _bandSoftness );
		_block.SetFloat( LineCountId, _lineCount );
		_block.SetFloat( LineSpacingId, _lineSpacing );
		renderer.SetPropertyBlock( _block );
	}

	void CaptureMoteDefaults()
	{
		if ( _capturedMoteDefaults || _motes == null )
			return;

		ParticleSystem.EmissionModule emission = _motes.emission;
		_moteBaseRateOverTime = emission.rateOverTime.constant;
		if ( _moteBaseRateOverTime < 0.01f )
			_moteBaseRateOverTime = _moteBaseEmission;
		_capturedMoteDefaults = true;
	}

	void StartMotes()
	{
		if ( _motes == null )
			return;

		_motes.Stop( true, ParticleSystemStopBehavior.StopEmittingAndClear );
		ApplyMoteIntensity( areaGlow: 1f, swipeBoost: 0f );
		_motes.Play( true );
	}

	void StopMotes( bool clear )
	{
		if ( _motes == null )
			return;

		if ( clear )
			_motes.Stop( true, ParticleSystemStopBehavior.StopEmittingAndClear );
		else
			_motes.Stop( true, ParticleSystemStopBehavior.StopEmitting );
	}

	void ApplyMoteIntensity( float areaGlow, float swipeBoost )
	{
		if ( _motes == null )
			return;

		float emission = Mathf.Lerp( 0f, _moteBaseEmission, areaGlow ) + _moteSwipeBoostEmission * swipeBoost;
		float size = Mathf.Lerp( _moteBaseSize * 0.5f, _moteBaseSize, areaGlow ) +
		             ( _moteSwipeBoostSize - _moteBaseSize ) * swipeBoost;

		ParticleSystem.EmissionModule emissionModule = _motes.emission;
		emissionModule.rateOverTime = emission;

		ParticleSystem.MainModule main = _motes.main;
		main.startSize = Mathf.Max( 0.01f, size );

		Color c = _color;
		c.a = Mathf.Clamp01( 0.25f + areaGlow * 0.75f + swipeBoost * 0.5f );
		main.startColor = c;
	}

	static float MeasureSpan( MeshRenderer renderer, Vector3 origin, Vector3 axis )
	{
		Bounds bounds = renderer.bounds;
		Vector3 ext = bounds.extents;
		float min = float.PositiveInfinity;
		float max = float.NegativeInfinity;
		for ( int ix = -1; ix <= 1; ix += 2 )
		{
			for ( int iy = -1; iy <= 1; iy += 2 )
			{
				for ( int iz = -1; iz <= 1; iz += 2 )
				{
					Vector3 corner = bounds.center + new Vector3( ext.x * ix, ext.y * iy, ext.z * iz );
					float along = Vector3.Dot( corner - origin, axis );
					if ( along < min )
						min = along;
					if ( along > max )
						max = along;
				}
			}
		}

		return Mathf.Max( 0.5f, max - min );
	}

	void EnsureHidden()
	{
		MeshRenderer renderer = ResolveRenderer();
		if ( renderer != null )
			renderer.enabled = false;
	}

	MeshRenderer ResolveRenderer()
	{
		if ( _renderer != null )
			return _renderer;
		_renderer = GetComponent<MeshRenderer>();
		return _renderer;
	}

	Transform ResolveSweepReference()
	{
		if ( _sweepReference != null )
			return _sweepReference;
		return transform;
	}

	void EnsureBlock()
	{
		if ( _block == null )
			_block = new MaterialPropertyBlock();
	}

#if UNITY_EDITOR
	public void EditorAssign( MeshRenderer renderer, Transform sweepReference, float duration, float bandWidth, Color color )
	{
		_renderer = renderer;
		_sweepReference = sweepReference;
		_duration = Mathf.Max( 0.1f, duration );
		_bandWidth = Mathf.Clamp( bandWidth, 0.005f, 0.4f );
		_color = color;
	}

	public void EditorAssignMotes( ParticleSystem motes )
	{
		_motes = motes;
		_capturedMoteDefaults = false;
	}

	void OnValidate()
	{
		_duration = Mathf.Max( 0.1f, _duration );
		_swipeDuration = Mathf.Max( 0.05f, _swipeDuration );
		_swipeStart = Mathf.Clamp01( _swipeStart );
		_bandWidth = Mathf.Clamp( _bandWidth, 0.005f, 0.4f );
		_bandSoftness = Mathf.Clamp( _bandSoftness, 0.001f, 0.25f );
		_lineCount = Mathf.Clamp( _lineCount, 1, 8 );
		_lineSpacing = Mathf.Clamp( _lineSpacing, 0f, 0.35f );
		if ( _areaGlowCurve == null || _areaGlowCurve.length == 0 )
			_areaGlowCurve = AnimationCurve.EaseInOut( 0f, 1f, 1f, 0f );
	}

	void OnDrawGizmosSelected()
	{
		Transform reference = ResolveSweepReference();
		MeshRenderer renderer = ResolveRenderer();
		if ( renderer == null )
			return;

		Vector3 axis = reference.forward;
		axis.y = 0f;
		if ( axis.sqrMagnitude < 0.0001f )
			axis = Vector3.right;
		axis.Normalize();

		Vector3 center = renderer.bounds.center;
		float span = MeasureSpan( renderer, center, axis );
		Vector3 origin = center - axis * ( span * 0.5f );

		Gizmos.color = new Color( 1f, 0.85f, 0.3f, 0.9f );
		Gizmos.DrawWireSphere( origin, 0.15f );
		Gizmos.DrawLine( origin, origin + axis * span );
		Gizmos.color = new Color( 0.3f, 1f, 0.5f, 0.85f );
		Gizmos.DrawWireSphere( origin + axis * span, 0.12f );
	}
#endif
}
