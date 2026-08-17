using UnityEngine;

[CreateAssetMenu( fileName = "PlayerPlacementDefinition", menuName = "Definitions/PlayerPlacementDefinition" )]
public class PlayerPlacementDefinition : ScriptableObject
{
	[Header( "Floor / Surface" )]
	[Tooltip( "Extra world offset along hit normal when previewing/placing on floor." )]
	[Min( 0f )]
	public float dropUpBias = 0.05f;

	[Header( "Ground Stack (right-click onto loose treasure)" )]
	[Tooltip( "Lift above the top of the aimed coin / support pile when stacking." )]
	[Min( 0f )]
	public float groundStackUpBias = 0.02f;

	[Tooltip( "Horizontal settle: release velocity scale vs normal throw." )]
	[Range( 0f, 1f )]
	public float groundStackReleaseSpeedScale = 0.05f;

	[Tooltip( "Upward component scale for ground-stack release." )]
	[Range( 0f, 1f )]
	public float groundStackReleaseUpScale = 0.1f;

	[Tooltip( "Show placement ghost while aiming at stackable ground treasure." )]
	public bool showGroundStackPreview = true;

	[Tooltip( "World radius to snap loose vertical coin stacks when aim is near but not on a coin collider." )]
	[Min( 0.05f )]
	public float groundStackSnapRadius = 0.42f;

	[Header( "Constellation" )]
	[Tooltip( "How far a held gem can snap-place onto a constellation. Independent of pickup interact range." )]
	[Min( 0.1f )]
	public float constellationPlaceRange = 12f;

	[Header( "Placement Motion" )]
	[Tooltip( "Peak arc height when snapping treasure into a placement slot." )]
	[Min( 0f )]
	public float placementArcHeight = 0.12f;

	[Tooltip( "Coin flip/spin speed multiplier for placement and pile deposit." )]
	[Min( 0.1f )]
	public float coinFlipSpeed = 1f;

	[Tooltip( "How quickly the placement ghost lerps between candidate positions." )]
	[Min( 0.1f )]
	public float previewSmoothSpeed = 18f;

	[Header( "Placement Ghost Visual" )]
	public Color validGhostColor = new Color( 0.25f, 0.9f, 0.35f, 0.35f );
	public Color invalidGhostColor = new Color( 0.95f, 0.2f, 0.2f, 0.35f );

	[Range( 0.5f, 8f )]
	public float ghostFresnelPower = 2.4f;

	[Range( 0f, 2f )]
	public float ghostFresnelBoost = 0.7f;

	[Range( 0f, 1f )]
	public float ghostPulseAmount = 0.12f;

	[Min( 0f )]
	public float ghostPulseSpeed = 0.85f;

	[Range( 0f, 2f )]
	public float ghostRimIntensity = 1.15f;

	[Range( 0f, 1f )]
	public float ghostCoreIntensity = 0.28f;

	[Header( "Stack Volume Outline" )]
	[Tooltip( "Diameter scale for coin-stack placement preview volume." )]
	[Range( 1f, 1.5f )]
	public float stackVolumeOversize = 1.1f;

	public HoverOutlineVisualSettings stackOutline = HoverOutlineVisualSettings.DefaultStack();

	void OnValidate()
	{
		dropUpBias = Mathf.Max( 0f, dropUpBias );
		groundStackUpBias = Mathf.Max( 0f, groundStackUpBias );
		groundStackReleaseSpeedScale = Mathf.Clamp01( groundStackReleaseSpeedScale );
		groundStackReleaseUpScale = Mathf.Clamp01( groundStackReleaseUpScale );
		groundStackSnapRadius = Mathf.Max( 0.05f, groundStackSnapRadius );
		constellationPlaceRange = Mathf.Max( 0.1f, constellationPlaceRange );
		placementArcHeight = Mathf.Max( 0f, placementArcHeight );
		coinFlipSpeed = Mathf.Max( 0.1f, coinFlipSpeed );
		previewSmoothSpeed = Mathf.Max( 0.1f, previewSmoothSpeed );
		ghostFresnelPower = Mathf.Clamp( ghostFresnelPower, 0.5f, 8f );
		ghostFresnelBoost = Mathf.Clamp( ghostFresnelBoost, 0f, 2f );
		ghostPulseAmount = Mathf.Clamp01( ghostPulseAmount );
		ghostPulseSpeed = Mathf.Max( 0f, ghostPulseSpeed );
		ghostRimIntensity = Mathf.Clamp( ghostRimIntensity, 0f, 2f );
		ghostCoreIntensity = Mathf.Clamp01( ghostCoreIntensity );
		stackVolumeOversize = Mathf.Clamp( stackVolumeOversize, 1f, 1.5f );
		if ( stackOutline == null )
			stackOutline = HoverOutlineVisualSettings.DefaultStack();
		else
			stackOutline.Validate();
	}
}
