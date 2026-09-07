using System.Collections;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Pools one <see cref="LineRenderer"/> per resolved constellation edge and toggles visibility
/// when both endpoint slots hold a gem. Uses <c>DragonLoot/ConstellationLine</c> for glowy energy flow.
/// Supports a complete-state surge + idle breathe driven by <see cref="GemConstellationInteractable"/>.
/// </summary>
public class GemConstellationLineVisual : MonoBehaviour
{
	const string EnergyShaderName = "DragonLoot/ConstellationLine";

	static readonly int GlowIntensityId = Shader.PropertyToID( "_GlowIntensity" );
	static readonly int CoreIntensityId = Shader.PropertyToID( "_CoreIntensity" );
	static readonly int ScrollSpeedId = Shader.PropertyToID( "_ScrollSpeed" );

	[SerializeField]
	GemConstellationInteractable constellation;

	[SerializeField]
	Material lineMaterial;

	[SerializeField]
	[Min( 0.001f )]
	float lineWidth = 0.045f;

	[SerializeField]
	[ColorUsage( true, true )]
	Color litColor = new Color( 0.45f, 1.1f, 1.8f, 1f );

	[SerializeField]
	[ColorUsage( true, true )]
	Color coreColor = new Color( 1.6f, 2.2f, 3f, 1f );

	[SerializeField]
	[ColorUsage( true, true )]
	Color pulseColor = new Color( 2f, 1.2f, 3.2f, 1f );

	[SerializeField]
	Color dimColor = new Color( 0.45f, 0.85f, 1f, 0f );

	[SerializeField]
	[Min( 0.1f )]
	float scrollSpeed = 1.35f;

	[SerializeField]
	[Range( 1f, 12f )]
	float pulseCount = 3.5f;

	[Header( "Complete Surge" )]
	[SerializeField]
	[Min( 1f )]
	float surgeWidthMul = 1.22f;

	[SerializeField]
	[Min( 1f )]
	float surgeGlowMul = 1.45f;

	[SerializeField]
	[Min( 1f )]
	float surgeScrollMul = 1.55f;

	[SerializeField]
	[Min( 0.05f )]
	float defaultSurgeDuration = 1.8f;

	[Header( "Complete Breathe" )]
	[SerializeField]
	[Min( 0f )]
	float breatheGlowAmplitude = 0.12f;

	[SerializeField]
	[Min( 0.05f )]
	float breatheSpeed = 0.85f;

	readonly List<LineRenderer> _lines = new List<LineRenderer>();
	readonly List<GemConstellationResolvedConnection> _connections = new List<GemConstellationResolvedConnection>();
	readonly List<bool> _previousLit = new List<bool>();
	Material _runtimeMaterial;
	bool _suppressConnectionEvents;
	Coroutine _surgeRoutine;
	float _widthMul = 1f;
	float _glowMul = 1f;
	float _scrollMul = 1f;
	float _breatheMul = 1f;
	bool _breatheActive;
	float _baseGlowIntensity = 1.35f;
	float _baseCoreIntensity = 1.8f;

	void Awake()
	{
		EnsureMaterial();
		CacheBaseIntensities();
		ApplyMaterialProperties( ResolveMaterial() );
		ApplyRuntimeMultipliers();
	}

	void OnEnable()
	{
		if ( constellation != null )
			Rebuild( constellation.ResolvedConnections );
	}

	void OnDestroy()
	{
		StopSurgeRoutine();
		if ( _runtimeMaterial != null )
		{
			Destroy( _runtimeMaterial );
			_runtimeMaterial = null;
		}
	}

	void LateUpdate()
	{
		if ( constellation == null || _connections.Count == 0 )
			return;

		TickBreathe();
		ApplyGeometry();
		RefreshLitState();
		ApplyRuntimeMultipliers();
	}

	public void Bind( GemConstellationInteractable owner )
	{
		constellation = owner;
	}

	public void Rebuild( IReadOnlyList<GemConstellationResolvedConnection> connections )
	{
		_connections.Clear();
		if ( connections != null )
		{
			for ( int i = 0; i < connections.Count; i++ )
				_connections.Add( connections[ i ] );
		}

		EnsureMaterial();
		CacheBaseIntensities();
		ApplyMaterialProperties( ResolveMaterial() );
		EnsureLineCount( _connections.Count );
		ApplyGeometry();
		_suppressConnectionEvents = true;
		RefreshLitState();
		_suppressConnectionEvents = false;
		ApplyRuntimeMultipliers();
	}

	public void PlayCompleteSurge( float duration )
	{
		EnsureMaterial();
		CacheBaseIntensities();
		StopSurgeRoutine();
		float surgeDuration = duration > 0.0001f ? duration : defaultSurgeDuration;
		_surgeRoutine = StartCoroutine( SurgeRoutine( surgeDuration ) );
	}

	public void SetCompleteBreathe( bool active )
	{
		_breatheActive = active;
		if ( !active )
			_breatheMul = 1f;
	}

	public void StopCompleteEffects()
	{
		StopSurgeRoutine();
		_breatheActive = false;
		_widthMul = 1f;
		_glowMul = 1f;
		_scrollMul = 1f;
		_breatheMul = 1f;
		ApplyRuntimeMultipliers();
	}

	public void RefreshLitState()
	{
		if ( constellation == null )
			return;

		EnsurePreviousLitCount( _connections.Count );
		float width = lineWidth * _widthMul;

		for ( int i = 0; i < _connections.Count; i++ )
		{
			GemConstellationResolvedConnection edge = _connections[ i ];
			bool lit = constellation.IsSlotOccupied( edge.SlotA ) && constellation.IsSlotOccupied( edge.SlotB );
			bool wasLit = i < _previousLit.Count && _previousLit[ i ];

			if ( i < _lines.Count && _lines[ i ] != null )
			{
				LineRenderer lr = _lines[ i ];
				lr.enabled = lit;
				Color c = lit ? Color.white : dimColor;
				lr.startColor = c;
				lr.endColor = c;
				lr.startWidth = width;
				lr.endWidth = width;
			}

			if ( !_suppressConnectionEvents && lit != wasLit )
			{
				EventBus.Publish( new GemConstellationConnectionChangedEvent
				{
					Constellation = constellation,
					SlotA = edge.SlotA,
					SlotB = edge.SlotB,
					IsLit = lit
				} );
			}

			if ( i < _previousLit.Count )
				_previousLit[ i ] = lit;
		}
	}

	IEnumerator SurgeRoutine( float duration )
	{
		float elapsed = 0f;
		duration = Mathf.Max( 0.05f, duration );

		while ( elapsed < duration )
		{
			elapsed += Time.deltaTime;
			float u = Mathf.Clamp01( elapsed / duration );
			// Soft flare: gentle rise (~30%), hold a touch, long ease back to base.
			float envelope;
			if ( u < 0.3f )
			{
				float rise = u / 0.3f;
				envelope = rise * rise * ( 3f - 2f * rise );
			}
			else
			{
				float fall = ( u - 0.3f ) / 0.7f;
				envelope = 1f - ( fall * fall * ( 3f - 2f * fall ) );
			}

			_widthMul = Mathf.Lerp( 1f, surgeWidthMul, envelope );
			_glowMul = Mathf.Lerp( 1f, surgeGlowMul, envelope );
			_scrollMul = Mathf.Lerp( 1f, surgeScrollMul, envelope );
			ApplyRuntimeMultipliers();
			yield return null;
		}

		// Explicitly restore base line width / intensity / scroll; breathe is intensity-only.
		_widthMul = 1f;
		_glowMul = 1f;
		_scrollMul = 1f;
		ApplyRuntimeMultipliers();
		_surgeRoutine = null;
	}

	void TickBreathe()
	{
		if ( !_breatheActive )
		{
			_breatheMul = 1f;
			return;
		}

		float wave = 0.5f + 0.5f * Mathf.Sin( Time.time * breatheSpeed );
		_breatheMul = 1f + breatheGlowAmplitude * wave;
	}

	void StopSurgeRoutine()
	{
		if ( _surgeRoutine == null )
			return;

		StopCoroutine( _surgeRoutine );
		_surgeRoutine = null;
		_widthMul = 1f;
		_glowMul = 1f;
		_scrollMul = 1f;
	}

	void EnsurePreviousLitCount( int count )
	{
		while ( _previousLit.Count < count )
			_previousLit.Add( false );

		while ( _previousLit.Count > count )
			_previousLit.RemoveAt( _previousLit.Count - 1 );
	}

	void EnsureLineCount( int count )
	{
		EnsureMaterial();

		while ( _lines.Count < count )
		{
			GameObject go = new GameObject( "Connection_" + _lines.Count );
			go.transform.SetParent( transform, false );

			LineRenderer lr = go.AddComponent<LineRenderer>();
			ConfigureLineRenderer( lr );
			_lines.Add( lr );
		}

		for ( int i = 0; i < _lines.Count; i++ )
		{
			if ( _lines[ i ] == null )
				continue;

			ConfigureLineRenderer( _lines[ i ] );
			_lines[ i ].gameObject.SetActive( i < count );
		}
	}

	void ConfigureLineRenderer( LineRenderer lr )
	{
		if ( lr == null )
			return;

		lr.useWorldSpace = true;
		lr.positionCount = 2;
		lr.numCapVertices = 8;
		lr.numCornerVertices = 4;
		lr.startWidth = lineWidth * _widthMul;
		lr.endWidth = lineWidth * _widthMul;
		lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		lr.receiveShadows = false;
		lr.textureMode = LineTextureMode.Stretch;
		lr.alignment = LineAlignment.View;
		lr.sharedMaterial = ResolveMaterial();
		lr.startColor = dimColor;
		lr.endColor = dimColor;
		lr.enabled = false;
	}

	void ApplyGeometry()
	{
		if ( constellation == null )
			return;

		for ( int i = 0; i < _connections.Count; i++ )
		{
			if ( i >= _lines.Count || _lines[ i ] == null )
				continue;

			GemConstellationResolvedConnection edge = _connections[ i ];
			constellation.GetSlotWorldPose( edge.SlotA, out Vector3 a, out _ );
			constellation.GetSlotWorldPose( edge.SlotB, out Vector3 b, out _ );

			LineRenderer lr = _lines[ i ];
			lr.SetPosition( 0, a );
			lr.SetPosition( 1, b );
		}
	}

	void EnsureMaterial()
	{
		if ( _runtimeMaterial != null )
			return;

		if ( lineMaterial != null )
		{
			_runtimeMaterial = new Material( lineMaterial );
			_runtimeMaterial.name = lineMaterial.name + " (Instance)";
			ApplyMaterialProperties( _runtimeMaterial );
			return;
		}

		Shader shader = Shader.Find( EnergyShaderName );
		if ( shader == null )
		{
			shader = Shader.Find( "Universal Render Pipeline/Unlit" );
			if ( shader == null )
				shader = Shader.Find( "Sprites/Default" );
			if ( shader == null )
				return;
		}

		_runtimeMaterial = new Material( shader );
		_runtimeMaterial.name = "GemConstellationLine (Runtime)";
		ApplyMaterialProperties( _runtimeMaterial );
	}

	Material ResolveMaterial()
	{
		EnsureMaterial();
		return _runtimeMaterial != null ? _runtimeMaterial : lineMaterial;
	}

	void CacheBaseIntensities()
	{
		Material mat = ResolveMaterial();
		if ( mat == null )
			return;

		if ( mat.HasProperty( GlowIntensityId ) )
			_baseGlowIntensity = mat.GetFloat( GlowIntensityId );
		if ( mat.HasProperty( CoreIntensityId ) )
			_baseCoreIntensity = mat.GetFloat( CoreIntensityId );
	}

	void ApplyMaterialProperties( Material mat )
	{
		if ( mat == null )
			return;

		if ( mat.HasProperty( "_Color" ) )
			mat.SetColor( "_Color", litColor );
		if ( mat.HasProperty( "_CoreColor" ) )
			mat.SetColor( "_CoreColor", coreColor );
		if ( mat.HasProperty( "_PulseColor" ) )
			mat.SetColor( "_PulseColor", pulseColor );
		if ( mat.HasProperty( "_ScrollSpeed" ) )
			mat.SetFloat( "_ScrollSpeed", scrollSpeed );
		if ( mat.HasProperty( "_PulseCount" ) )
			mat.SetFloat( "_PulseCount", pulseCount );
		if ( mat.HasProperty( "_BaseColor" ) )
			mat.SetColor( "_BaseColor", litColor );
	}

	void ApplyRuntimeMultipliers()
	{
		Material mat = ResolveMaterial();
		if ( mat == null )
			return;

		float glow = _baseGlowIntensity * _glowMul * _breatheMul;
		float core = _baseCoreIntensity * _glowMul * _breatheMul;
		float scroll = scrollSpeed * _scrollMul;

		if ( mat.HasProperty( GlowIntensityId ) )
			mat.SetFloat( GlowIntensityId, glow );
		if ( mat.HasProperty( CoreIntensityId ) )
			mat.SetFloat( CoreIntensityId, core );
		if ( mat.HasProperty( ScrollSpeedId ) )
			mat.SetFloat( ScrollSpeedId, scroll );

		float width = lineWidth * _widthMul;
		for ( int i = 0; i < _lines.Count; i++ )
		{
			if ( _lines[ i ] == null )
				continue;

			_lines[ i ].startWidth = width;
			_lines[ i ].endWidth = width;
		}
	}

	void OnValidate()
	{
		lineWidth = Mathf.Max( 0.001f, lineWidth );
		scrollSpeed = Mathf.Max( 0.1f, scrollSpeed );
		pulseCount = Mathf.Clamp( pulseCount, 1f, 12f );
		surgeWidthMul = Mathf.Max( 1f, surgeWidthMul );
		surgeGlowMul = Mathf.Max( 1f, surgeGlowMul );
		surgeScrollMul = Mathf.Max( 1f, surgeScrollMul );
		defaultSurgeDuration = Mathf.Max( 0.05f, defaultSurgeDuration );
		breatheGlowAmplitude = Mathf.Max( 0f, breatheGlowAmplitude );
		breatheSpeed = Mathf.Max( 0.05f, breatheSpeed );

		if ( _runtimeMaterial != null )
		{
			ApplyMaterialProperties( _runtimeMaterial );
			CacheBaseIntensities();
			ApplyRuntimeMultipliers();
		}

		for ( int i = 0; i < _lines.Count; i++ )
		{
			if ( _lines[ i ] == null )
				continue;

			_lines[ i ].startWidth = lineWidth * _widthMul;
			_lines[ i ].endWidth = lineWidth * _widthMul;
			if ( _runtimeMaterial != null )
				_lines[ i ].sharedMaterial = _runtimeMaterial;
		}
	}
}
