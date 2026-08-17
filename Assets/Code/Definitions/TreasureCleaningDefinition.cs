using UnityEngine;

/// <summary>
/// Global artifact cleaning feature gate, timings, and station settings.
/// Asset name must be <c>TreasureCleaningDefinition</c> for <see cref="GameInstance.GetDefinition{T}"/>.
/// </summary>
[CreateAssetMenu( fileName = "TreasureCleaningDefinition", menuName = "Definitions/TreasureCleaningDefinition" )]
public class TreasureCleaningDefinition : ScriptableObject
{
	static TreasureCleaningDefinition s_runtimeCache;

	[Header( "Feature" )]
	[Tooltip( "When false, artifacts spawn fully clean and skip manual/station cleaning." )]
	public bool cleaningEnabled = false;

	[Header( "Manual" )]
	[Tooltip( "Seconds to hold Clean while carrying a dirty artifact to finish cleaning." )]
	[Min( 0.1f )]
	public float manualCleanDurationSeconds = 2.5f;

	[Header( "Cleaning Station" )]
	[Tooltip( "When false, the station rejects place and physics intake (upgrade gate)." )]
	public bool stationUnlocked = true;

	[Tooltip( "Seconds for the single belt item to travel intake → exit while cleaning." )]
	[Min( 0.25f )]
	public float stationBeltTravelSeconds = 4f;

	[Tooltip( "Seconds for the hand-place snap flight onto the intake socket." )]
	[Min( 0.05f )]
	public float stationIntakeSnapSeconds = 0.22f;

	[Header( "Dirt Visual" )]
	[Tooltip( "Material _DirtStrength when CleanProgress is 0." )]
	[Min( 0f )]
	public float maxDirtStrength = 1.35f;

	/// <summary>
	/// Runtime gate for the cleaning loop. Missing definition falls back to disabled.
	/// </summary>
	public static bool IsCleaningEnabled()
	{
		TreasureCleaningDefinition def = RuntimeDefinition.Resolve( ref s_runtimeCache );
		return RuntimeDefinition.Get( def, d => d.cleaningEnabled, false );
	}

	void OnValidate()
	{
		manualCleanDurationSeconds = Mathf.Max( 0.1f, manualCleanDurationSeconds );
		stationBeltTravelSeconds = Mathf.Max( 0.25f, stationBeltTravelSeconds );
		stationIntakeSnapSeconds = Mathf.Max( 0.05f, stationIntakeSnapSeconds );
		maxDirtStrength = Mathf.Max( 0f, maxDirtStrength );
	}
}
