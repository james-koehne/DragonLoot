using System.Collections;

using UnityEngine;

/// <summary>
/// Drives a floor overlay using <c>DragonLoot/FloorGleam</c>.
/// Sweep is full-viewport screen-space (TL→BR by default); the mesh is only the draw mask.
/// Renderer stays disabled when idle so save-restored rooms do not show a stuck gleam.
/// </summary>
[DisallowMultipleComponent]
public class RoomFloorGleamController : MonoBehaviour
{
	static readonly int ColorId = Shader.PropertyToID( "_Color" );
	static readonly int GleamTId = Shader.PropertyToID( "_GleamT" );
	static readonly int ScreenAngleId = Shader.PropertyToID( "_ScreenAngle" );
	static readonly int BandWidthId = Shader.PropertyToID( "_BandWidth" );
	static readonly int BandSoftnessId = Shader.PropertyToID( "_BandSoftness" );
	static readonly int LineCountId = Shader.PropertyToID( "_LineCount" );
	static readonly int LineSpacingId = Shader.PropertyToID( "_LineSpacing" );

	[Tooltip( "Overlay mesh using DragonLoot/FloorGleam. Empty = renderer on this object." )]
	[SerializeField]
	MeshRenderer _renderer;

	[SerializeField]
	[Min( 0.05f )]
	float _duration = 1.5f;

	[SerializeField]
	AnimationCurve _curve = AnimationCurve.EaseInOut( 0f, 0f, 1f, 1f );

	[SerializeField]
	[ColorUsage( true, true )]
	Color _color = new Color( 1.35f, 0.95f, 0.45f, 1f );

	[Tooltip( "Degrees in screen space. 45 = top-left toward bottom-right of the viewport." )]
	[SerializeField]
	[Range( -180f, 180f )]
	float _screenAngle = 45f;

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

	MaterialPropertyBlock _block;
	Coroutine _routine;
	bool _playing;

	public bool IsPlaying => _playing;

	public MeshRenderer Renderer => ResolveRenderer();

	void Awake()
	{
		EnsureHidden();
	}

	void OnDisable()
	{
		Stop();
	}

	/// <summary>Starts the gleam sweep. Restarts if already playing.</summary>
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

		EnsureBlock();
		renderer.enabled = true;
		_playing = true;

		float duration = Mathf.Max( 0.05f, _duration );
		float elapsed = 0f;
		ApplyProps( renderer, 0f );

		while ( elapsed < duration )
		{
			elapsed += Time.deltaTime;
			float normalized = Mathf.Clamp01( elapsed / duration );
			float eased = _curve != null ? _curve.Evaluate( normalized ) : normalized;
			ApplyProps( renderer, eased );
			yield return null;
		}

		ApplyProps( renderer, 1f );
		_playing = false;
		_routine = null;
		EnsureHidden();
	}

	void ApplyProps( MeshRenderer renderer, float gleamT )
	{
		EnsureBlock();
		renderer.GetPropertyBlock( _block );
		_block.SetColor( ColorId, _color );
		_block.SetFloat( GleamTId, gleamT );
		_block.SetFloat( ScreenAngleId, _screenAngle );
		_block.SetFloat( BandWidthId, _bandWidth );
		_block.SetFloat( BandSoftnessId, _bandSoftness );
		_block.SetFloat( LineCountId, _lineCount );
		_block.SetFloat( LineSpacingId, _lineSpacing );
		renderer.SetPropertyBlock( _block );
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

	void EnsureBlock()
	{
		if ( _block == null )
			_block = new MaterialPropertyBlock();
	}

#if UNITY_EDITOR
	public void EditorAssign( MeshRenderer renderer, Transform sweepReference, float duration, float bandWidth, Color color )
	{
		_renderer = renderer;
		_duration = Mathf.Max( 0.05f, duration );
		_bandWidth = Mathf.Clamp( bandWidth, 0.005f, 0.4f );
		_color = color;
	}

	void OnValidate()
	{
		_duration = Mathf.Max( 0.05f, _duration );
		_bandWidth = Mathf.Clamp( _bandWidth, 0.005f, 0.4f );
		_bandSoftness = Mathf.Clamp( _bandSoftness, 0.001f, 0.25f );
		_lineCount = Mathf.Clamp( _lineCount, 1, 8 );
		_lineSpacing = Mathf.Clamp( _lineSpacing, 0f, 0.35f );
		if ( _curve == null || _curve.length == 0 )
			_curve = AnimationCurve.EaseInOut( 0f, 0f, 1f, 1f );
	}
#endif
}
