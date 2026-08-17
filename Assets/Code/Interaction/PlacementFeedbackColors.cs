using UnityEngine;

/// <summary>
/// Shared valid/invalid tint RGB for placement ghost and artifact slot indicators.
/// Alpha may differ per consumer.
/// </summary>
public static class PlacementFeedbackColors
{
	public static readonly Color ValidRgb = new Color( 0.25f, 0.9f, 0.35f, 1f );
	public static readonly Color InvalidRgb = new Color( 0.95f, 0.2f, 0.2f, 1f );

	public static Color ValidGhost => new Color( ValidRgb.r, ValidRgb.g, ValidRgb.b, 0.35f );
	public static Color InvalidGhost => new Color( InvalidRgb.r, InvalidRgb.g, InvalidRgb.b, 0.35f );

	public static Color ValidIndicator => new Color( ValidRgb.r, ValidRgb.g, ValidRgb.b, 0.6f );
	public static Color InvalidIndicator => new Color( InvalidRgb.r, InvalidRgb.g, InvalidRgb.b, 0.65f );

	/// <summary>Outline tint when aiming at a pickable gem or artifact.</summary>
	public static Color PickableHighlight => new Color( 1f, 0.85f, 0.35f, 1f );

	/// <summary>Outline tint for active quest objective meshes (distinct from gold/green/red).</summary>
	public static Color QuestObjectiveHighlight => new Color( 0.2f, 0.95f, 1f, 1f );
}
