using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Simple on/off fill indicator for hopper-has-coins or output-full on the coin sorter.
/// </summary>
public class CoinSortingBooleanIndicator : MonoBehaviour
{
	public enum Mode
	{
		HopperHasCoins = 0,
		OutputFull = 1
	}

	const string GaugeShaderName = "DragonLoot/Placement Ghost";

	static readonly int BaseColorId = Shader.PropertyToID( "_BaseColor" );
	static readonly int RimColorId = Shader.PropertyToID( "_RimColor" );
	static readonly int CoreColorId = Shader.PropertyToID( "_CoreColor" );

	[SerializeField]
	CoinSortingStation station;

	[SerializeField]
	Mode mode = Mode.HopperHasCoins;

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
	Vector2 _fillBaseXZ;
	Vector3 _wellBaseScale;

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

		if ( well != null )
			_wellBaseScale = well.localScale;
		if ( fill != null )
			_fillBaseXZ = new Vector2( fill.localScale.x, fill.localScale.z );

		EnsureFillMaterial();
	}

	void LateUpdate()
	{
		if ( station == null )
			return;

		bool show = station.IsUnlocked;
		if ( well != null && well.gameObject.activeSelf != show )
			well.gameObject.SetActive( show );
		if ( fill != null && fill.gameObject.activeSelf != show )
			fill.gameObject.SetActive( show );
		if ( !show )
			return;

		bool active = ResolveActive();
		ApplyFillHeight( active ? 1f : 0f );
		ApplyColor( active );
	}

	bool ResolveActive()
	{
		switch ( mode )
		{
			case Mode.HopperHasCoins:
				return station.BufferedCount > 0;
			case Mode.OutputFull:
				return station.IsOutputBlocked;
			default:
				return false;
		}
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

	void ApplyColor( bool active )
	{
		if ( fillRenderer == null )
			return;

		CoinSortingStationDefinition def = station != null ? station.Definition : null;
		Color empty = def != null ? def.gaugeEmptyColor : new Color( 0.92f, 0.12f, 0.1f, 1f );
		Color full = def != null ? def.gaugeFullColor : new Color( 0.18f, 0.92f, 0.28f, 1f );
		// Hopper: green when has coins. Output full: red when blocked, green when clear.
		Color color;
		if ( mode == Mode.OutputFull )
			color = active ? empty : full;
		else
			color = active ? full : empty;

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

		if ( _fillMaterial != null )
		{
			_fillMaterial.SetColor( BaseColorId, baseColor );
			_fillMaterial.SetColor( RimColorId, rimColor );
			_fillMaterial.SetColor( CoreColorId, coreColor );
		}
	}

	void EnsureFillMaterial()
	{
		if ( fillRenderer == null )
			return;

		PlayerPlacementDefinition placementDef = null;
		placementDef = RuntimeDefinition.Resolve( ref placementDef );
		Shader shader = placementDef != null ? placementDef.ghostShader : null;
		if ( shader == null )
			shader = Shader.Find( GaugeShaderName );
		if ( shader == null )
			shader = Shader.Find( "Universal Render Pipeline/Unlit" );
		if ( shader == null )
			shader = Shader.Find( "Sprites/Default" );
		if ( shader == null )
			return;

		_fillMaterial = new Material( shader );
		_fillMaterial.name = "CoinSorterBoolIndicatorFill";
		_fillMaterial.enableInstancing = false;
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
}
