using UnityEngine;

/// <summary>
/// Simple fill mesh for recycling machine charge / remaining runtime.
/// </summary>
public class RecyclingChargeGauge : MonoBehaviour
{
	[SerializeField]
	Transform fill;

	[SerializeField]
	Renderer fillRenderer;

	MaterialPropertyBlock _block;
	static readonly int ColorId = Shader.PropertyToID( "_BaseColor" );
	static readonly int ColorIdFallback = Shader.PropertyToID( "_Color" );

	float _normalized;
	Color _color = Color.white;

	public Transform Fill => fill;

	public void SetNormalized( float normalized, Color color )
	{
		_normalized = Mathf.Clamp01( normalized );
		_color = color;
		Apply();
	}

	void Awake()
	{
		if ( fill == null )
		{
			Transform well = transform.Find( "Well" );
			if ( well != null )
				fill = well.Find( "Fill" );
		}

		if ( fillRenderer == null && fill != null )
			fillRenderer = fill.GetComponent<Renderer>();
	}

	void Apply()
	{
		if ( fill != null )
		{
			Vector3 scale = fill.localScale;
			scale.y = Mathf.Max( 0.02f, _normalized );
			fill.localScale = scale;
			Vector3 pos = fill.localPosition;
			pos.y = scale.y * 0.5f;
			fill.localPosition = pos;
		}

		if ( fillRenderer == null )
			return;

		if ( _block == null )
			_block = new MaterialPropertyBlock();

		fillRenderer.GetPropertyBlock( _block );
		_block.SetColor( ColorId, _color );
		_block.SetColor( ColorIdFallback, _color );
		fillRenderer.SetPropertyBlock( _block );
	}

#if UNITY_EDITOR
	public void EditorSetFill( Transform value, Renderer renderer )
	{
		fill = value;
		fillRenderer = renderer;
	}
#endif
}
