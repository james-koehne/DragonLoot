using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// World-space energy bar for L1 crank reserve. Fill height + red/orange/green color.
/// </summary>
public class CoinSortingEnergyGauge : MonoBehaviour
{
	const string GaugeShaderName = "DragonLoot/Placement Ghost";

	static readonly int BaseColorId = Shader.PropertyToID( "_BaseColor" );
	static readonly int RimColorId = Shader.PropertyToID( "_RimColor" );
	static readonly int CoreColorId = Shader.PropertyToID( "_CoreColor" );

	[SerializeField]
	CoinSortingStation station;

	[SerializeField]
	Transform well;

	[SerializeField]
	Transform fill;

	[SerializeField]
	Renderer fillRenderer;

	[SerializeField]
	float minFillHeight = 0.04f;

	[SerializeField]
	float maxFillHeight = 0.42f;

	MaterialPropertyBlock _block;
	Material _fillMaterial;
	float _flashRemaining;
	Vector3 _wellBaseScale;
	Vector2 _fillBaseXZ;
	Vector3 _punchAmount;
	float _punchDuration;
	float _punchElapsed;
	bool _punchActive;

	public Transform Well => well;

	public void BindStation( CoinSortingStation owner )
	{
		station = owner;
	}

	void Awake()
	{
		if ( station == null )
			station = GetComponentInParent<CoinSortingStation>();
		if ( well == null )
		{
			Transform found = transform.Find( "Well" );
			if ( found != null )
				well = found;
		}

		if ( fill == null )
		{
			Transform found = transform.Find( "Fill" );
			if ( found != null )
				fill = found;
		}

		if ( fillRenderer == null && fill != null )
			fillRenderer = fill.GetComponent<Renderer>();

		CacheBaseTransforms();
		EnsureFillMaterial();
	}

	void CacheBaseTransforms()
	{
		if ( well != null )
			_wellBaseScale = well.localScale;
		if ( fill != null )
			_fillBaseXZ = new Vector2( fill.localScale.x, fill.localScale.z );
	}

	void LateUpdate()
	{
		if ( station == null )
			return;

		bool show = station.Definition != null && station.Definition.RequiresCrank( station.StationLevel );
		if ( well != null && well.gameObject.activeSelf != show )
			well.gameObject.SetActive( show );
		if ( fill != null && fill.gameObject.activeSelf != show )
			fill.gameObject.SetActive( show );
		if ( !show )
			return;

		float fillAmount = station.ReserveNormalized;
		ApplyFillHeight( fillAmount );
		ApplyFillColor( fillAmount );
		ApplyWellPunch();

		if ( _flashRemaining > 0f )
			_flashRemaining -= Time.deltaTime;
	}

	public void PlayChargePulse()
	{
		StartWellPunch( new Vector3( 0.045f, 0f, 0.045f ), 0.12f );
	}

	public void PlayFullPop()
	{
		_flashRemaining = 0.28f;
		StartWellPunch( new Vector3( 0.1f, 0.04f, 0.1f ), 0.22f );
	}

	void StartWellPunch( Vector3 punch, float duration )
	{
		_punchAmount = punch;
		_punchDuration = Mathf.Max( 0.01f, duration );
		_punchElapsed = 0f;
		_punchActive = true;
	}

	void ApplyWellPunch()
	{
		if ( well == null )
			return;

		if ( !_punchActive )
		{
			if ( well.localScale != _wellBaseScale )
				well.localScale = _wellBaseScale;
			return;
		}

		_punchElapsed += Time.deltaTime;
		float t = _punchElapsed / _punchDuration;
		if ( t >= 1f )
		{
			_punchActive = false;
			well.localScale = _wellBaseScale;
			return;
		}

		float curve = EvaluatePunchCurve( t );
		well.localScale = _wellBaseScale + _punchAmount * curve;
	}

	static float EvaluatePunchCurve( float normalizedTime )
	{
		return Mathf.Sin( normalizedTime * Mathf.PI );
	}

	void ApplyFillHeight( float normalized )
	{
		if ( fill == null )
			return;

		float height = Mathf.Lerp( minFillHeight, maxFillHeight, Mathf.Clamp01( normalized ) );
		fill.localScale = new Vector3( _fillBaseXZ.x, height, _fillBaseXZ.y );

		Vector3 pos = fill.localPosition;
		float wellBottom = -_wellBaseScale.y * 0.5f;
		pos.y = wellBottom + height * 0.5f;
		fill.localPosition = pos;
	}

	void ApplyFillColor( float normalized )
	{
		if ( fillRenderer == null )
			return;

		CoinSortingStationDefinition def = station != null ? station.Definition : null;
		Color color = def != null
			? def.EvaluateGaugeColor( normalized )
			: Color.Lerp( Color.red, Color.green, Mathf.Clamp01( normalized ) );

		float warning = def != null ? def.gaugeEmptyWarningNormalized : 0.15f;
		bool emptyWarn = normalized <= warning && station.IsReserveDischarging;
		if ( emptyWarn )
		{
			float flicker = 0.55f + 0.45f * Mathf.Abs( Mathf.Sin( Time.time * 14f ) );
			color *= flicker;
			color.a = 1f;
		}

		if ( _flashRemaining > 0f )
		{
			float flash = Mathf.Clamp01( _flashRemaining / 0.28f );
			Color full = def != null ? def.gaugeFullColor : Color.green;
			color = Color.Lerp( color, full * 1.8f, flash );
		}

		if ( _block == null )
			_block = new MaterialPropertyBlock();

		fillRenderer.GetPropertyBlock( _block );
		Color baseColor = color;
		baseColor.a = Mathf.Clamp01( baseColor.a <= 0.001f ? 0.92f : baseColor.a );
		Color rimColor = color * 1.55f;
		rimColor.a = 1f;
		Color coreColor = color * 0.42f;
		coreColor.a = 1f;
		_block.SetColor( BaseColorId, baseColor );
		_block.SetColor( RimColorId, rimColor );
		_block.SetColor( CoreColorId, coreColor );
		fillRenderer.SetPropertyBlock( _block );
	}

	void EnsureFillMaterial()
	{
		if ( fillRenderer == null )
			return;

		Shader shader = Shader.Find( GaugeShaderName );
		if ( shader == null )
			shader = Shader.Find( "Universal Render Pipeline/Unlit" );
		if ( shader == null )
			shader = Shader.Find( "Sprites/Default" );
		if ( shader == null )
			return;

		_fillMaterial = new Material( shader );
		_fillMaterial.name = "CoinSorterGaugeFill";
		if ( _fillMaterial.HasProperty( "_Surface" ) )
			_fillMaterial.SetFloat( "_Surface", 1f );
		if ( _fillMaterial.HasProperty( "_Blend" ) )
			_fillMaterial.SetFloat( "_Blend", 0f );
		if ( _fillMaterial.HasProperty( "_ZWrite" ) )
			_fillMaterial.SetFloat( "_ZWrite", 0f );
		_fillMaterial.renderQueue = (int)RenderQueue.Transparent;
		_fillMaterial.EnableKeyword( "_SURFACE_TYPE_TRANSPARENT" );
		fillRenderer.sharedMaterial = _fillMaterial;
	}

	void OnDestroy()
	{
		if ( _fillMaterial != null )
			Destroy( _fillMaterial );
	}

#if UNITY_EDITOR
	public void EditorSetParts( Transform wellTransform, Transform fillTransform, Renderer fillRend )
	{
		well = wellTransform;
		fill = fillTransform;
		fillRenderer = fillRend;
		CacheBaseTransforms();
	}
#endif
}
