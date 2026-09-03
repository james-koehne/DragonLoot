using UnityEngine;

/// <summary>
/// Tuning for the treasure-surface map bake, fog-of-war, and open animation.
/// Asset name must be <c>MapDefinition</c> for <see cref="GameInstance.GetDefinition{T}"/>.
/// </summary>
[CreateAssetMenu( fileName = "MapDefinition", menuName = "Definitions/MapDefinition" )]
public class MapDefinition : ScriptableObject
{
	[Header( "Bake" )]
	[Tooltip( "Map texture edge length in pixels." )]
	[Range( 64, 1024 )]
	public int resolution = 256;

	[Tooltip( "Meters of padding around the walkable AABB." )]
	[Min( 0f )]
	public float boundsPadding = 2f;

	[Tooltip( "Max CPU milliseconds spent baking base map rows per frame." )]
	[Min( 0.1f )]
	public float bakeBudgetMs = 1.5f;

	[Tooltip( "Skip a bake slice when frame delta exceeds this (seconds)." )]
	[Min( 0.02f )]
	public float busyFrameDeltaSeconds = 0.033f;

	[Header( "Base Colours" )]
	public Color goldColor = new Color( 0.85f, 0.68f, 0.22f, 1f );
	public Color walkableColor = new Color( 0.45f, 0.45f, 0.48f, 1f );
	public Color emptyColor = new Color( 0.04f, 0.04f, 0.045f, 1f );

	[Header( "Discovery" )]
	[Tooltip( "World-space radius around the player that reveals the map." )]
	[Min( 0.5f )]
	public float discoveryRadius = 12f;

	[Tooltip( "Soft edge as a fraction of discovery radius (0 = hard circle)." )]
	[Range( 0f, 1f )]
	public float discoverySoftness = 0.35f;

	[Header( "Open Animation" )]
	[Min( 0.05f )]
	public float openDuration = 0.22f;

	[Range( 0.5f, 1f )]
	public float openStartScale = 0.92f;

	[Header( "Player Marker" )]
	[Min( 4f )]
	public float playerDotSize = 14f;

	[Tooltip( "Aim cone half-angle in degrees." )]
	[Range( 5f, 90f )]
	public float aimConeHalfAngle = 28f;

	[Tooltip( "Aim cone length in map UI pixels." )]
	[Min( 8f )]
	public float aimConeLength = 48f;

	[Min( 8f )]
	public float aimConeBaseWidth = 36f;

	public Color playerDotColor = new Color( 0.95f, 0.95f, 1f, 1f );
	public Color aimConeColor = new Color( 0.95f, 0.95f, 1f, 0.45f );

	[Header( "Labels" )]
	[Min( 12 )]
	public int labelFontSize = 22;

	public Color labelColor = new Color( 0.92f, 0.9f, 0.82f, 0.95f );

	public void EnsureDefaults()
	{
		resolution = Mathf.Clamp( resolution, 64, 1024 );
		boundsPadding = Mathf.Max( 0f, boundsPadding );
		bakeBudgetMs = Mathf.Max( 0.1f, bakeBudgetMs );
		busyFrameDeltaSeconds = Mathf.Max( 0.02f, busyFrameDeltaSeconds );
		discoveryRadius = Mathf.Max( 0.5f, discoveryRadius );
		discoverySoftness = Mathf.Clamp01( discoverySoftness );
		openDuration = Mathf.Max( 0.05f, openDuration );
		openStartScale = Mathf.Clamp( openStartScale, 0.5f, 1f );
		playerDotSize = Mathf.Max( 4f, playerDotSize );
		aimConeHalfAngle = Mathf.Clamp( aimConeHalfAngle, 5f, 90f );
		aimConeLength = Mathf.Max( 8f, aimConeLength );
		aimConeBaseWidth = Mathf.Max( 8f, aimConeBaseWidth );
		labelFontSize = Mathf.Max( 12, labelFontSize );
	}
}
