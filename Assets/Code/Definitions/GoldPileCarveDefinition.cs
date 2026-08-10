using UnityEngine;

/// <summary>
/// Global soft-brush settings for gold pile dig/deposit.
/// Asset name must be <c>GoldPileCarveDefinition</c> for <see cref="GameInstance.GetDefinition{T}"/>.
/// </summary>
[CreateAssetMenu( fileName = "GoldPileCarveDefinition", menuName = "Definitions/GoldPileCarveDefinition" )]
public class GoldPileCarveDefinition : ScriptableObject
{
	[Header( "Brush" )]
	[Tooltip( "World-space radius of the soft dig/deposit brush on gold piles." )]
	[Min( 0.05f )]
	public float carveRadius = 2f;

	[Tooltip( "Minimum carve radius as a fraction of the pile worldSize (avoids pinholes on large piles)." )]
	[Range( 0f, 0.5f )]
	public float carveMinRadiusFractionOfPile = 0.06f;

	[Header( "Re-blur" )]
	[Tooltip( "Max heightfield cells of re-blur pad beyond the brush AABB. Higher = smoother skirts, more cost." )]
	[Min( 0 )]
	public int carveBlurPadCells = 4;

	[Tooltip( "How many 3x3 re-blur passes after each dig/deposit brush. 0 = leave the raw soft falloff." )]
	[Min( 0 )]
	public int carveBlurPasses = 1;

	[Tooltip( "Re-blur mix per pass. 0 = sharp brush result, 1 = full neighborhood blur." )]
	[Range( 0f, 1f )]
	public float carveBlurStrength = 1f;

	[Tooltip( "Soft-brush Gaussian sharpness. Higher digs a tighter center with a thinner skirt." )]
	[Min( 0.1f )]
	public float carveFalloffSharpness = 3.5f;

	public void Validate()
	{
		carveRadius = Mathf.Max( 0.05f, carveRadius );
		carveMinRadiusFractionOfPile = Mathf.Clamp( carveMinRadiusFractionOfPile, 0f, 0.5f );
		carveBlurPadCells = Mathf.Max( 0, carveBlurPadCells );
		carveBlurPasses = Mathf.Max( 0, carveBlurPasses );
		carveBlurStrength = Mathf.Clamp01( carveBlurStrength );
		carveFalloffSharpness = Mathf.Max( 0.1f, carveFalloffSharpness );
	}

	void OnValidate()
	{
		Validate();
	}
}
