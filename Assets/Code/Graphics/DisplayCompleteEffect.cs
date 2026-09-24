using UnityEngine;

/// <summary>
/// Complete-display FX root: tints an EnvironmentLit mesh (emission) and a SoftShaft mesh (color).
/// Place on the CompleteEffect object under a coin or artifact display.
/// </summary>
[DisallowMultipleComponent]
public class DisplayCompleteEffect : MonoBehaviour
{
	static readonly int EmissionColorId = Shader.PropertyToID( "_EmissionColor" );
	static readonly int ColorId = Shader.PropertyToID( "_Color" );

	[Tooltip( "Mesh using DragonLoot/EnvironmentLit — Setup writes _EmissionColor." )]
	[SerializeField]
	MeshRenderer litRenderer;

	[Tooltip( "Mesh using DragonLoot/SoftShaft — Setup writes _Color." )]
	[SerializeField]
	MeshRenderer softShaftRenderer;

	[SerializeField]
	[ColorUsage( true, true )]
	Color defaultColor = new Color( 1f, 0.72f, 0.28f, 1f );

	MaterialPropertyBlock _block;

	public MeshRenderer LitRenderer => litRenderer;
	public MeshRenderer SoftShaftRenderer => softShaftRenderer;
	public Color DefaultColor => defaultColor;

	/// <summary>Applies <paramref name="color"/> to both mesh materials via MaterialPropertyBlock.</summary>
	public void Setup( Color color )
	{
		EnsureBlock();
		ApplyEmission( litRenderer, color );
		ApplyShaftColor( softShaftRenderer, color );
	}

	void Awake()
	{
		Setup( defaultColor );
	}

	void OnValidate()
	{
		Setup( defaultColor );
	}

	void EnsureBlock()
	{
		if ( _block == null )
			_block = new MaterialPropertyBlock();
	}

	void ApplyEmission( MeshRenderer renderer, Color color )
	{
		if ( renderer == null )
			return;

		renderer.GetPropertyBlock( _block );
		_block.SetColor( EmissionColorId, color );
		renderer.SetPropertyBlock( _block );
	}

	void ApplyShaftColor( MeshRenderer renderer, Color color )
	{
		if ( renderer == null )
			return;

		renderer.GetPropertyBlock( _block );
		_block.SetColor( ColorId, color );
		renderer.SetPropertyBlock( _block );
	}
}
