using UnityEngine;

/// <summary>
/// Tunables for interleaved gold-bar stacks (ground + gold-bar display table).
/// Asset name must be <c>GoldBarStackSettings</c> for <see cref="GameInstance.GetDefinition{T}"/>.
/// </summary>
[CreateAssetMenu( fileName = "GoldBarStackSettings", menuName = "Definitions/GoldBarStackSettings" )]
public class GoldBarStackSettings : ScriptableObject
{
	public const int DefaultMaxHeight = 1000;
	public const float DefaultJoinRadius = 0.35f;

	[Header( "Layout" )]
	[Tooltip( "Extra gap between the two bars in a pair, along the short axis." )]
	[Min( 0f )]
	public float pairGap = 0.012f;

	[Tooltip( "Extra vertical gap between pair layers." )]
	[Min( 0f )]
	public float layerGap = 0.004f;

	[Header( "Join" )]
	[Tooltip( "XZ radius used to join/create near an existing stack. Scaled up by bar length when larger." )]
	[Min( 0.05f )]
	public float joinRadius = DefaultJoinRadius;

	[Tooltip( "0 = unlimited (clamped to DefaultMaxHeight). Ground stacks only." )]
	[Min( 0 )]
	public int groundMaxStackHeight = 0;

	[Header( "Motion" )]
	[Min( 0.05f )]
	public float hopDuration = 0.28f;

	[Min( 0f )]
	public float hopHeight = 0.12f;
}
