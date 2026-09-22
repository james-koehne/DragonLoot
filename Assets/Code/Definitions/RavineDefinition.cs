using UnityEngine;

/// <summary>
/// Tuning for ravine fog follow and fall recovery arcs.
/// Asset name must be <c>RavineDefinition</c> for <see cref="GameInstance.GetDefinition{T}"/>.
/// </summary>
[CreateAssetMenu( fileName = "RavineDefinition", menuName = "Definitions/RavineDefinition" )]
public class RavineDefinition : ScriptableObject
{
	[Header( "Safe Ground" )]
	[Tooltip( "Minimum horizontal distance to a missing Walkable ledge before caching a safe grounded pose." )]
	[Min( 0f )]
	public float safeGroundDistance = 1f;

	[Tooltip( "Downward capsule-cast distance used by Walkable safe-ground probes." )]
	[Min( 0.1f )]
	public float walkGroundProbeDistance = 1.25f;

	[Header( "Recovery Arc" )]
	[Tooltip( "Seconds to keep falling after entering the fall trigger before the recovery arc starts." )]
	[Min( 0f )]
	public float fallContinueDuration = 0.85f;

	[Min( 0.05f )]
	public float arcDuration = 1.1f;

	[Min( 0f )]
	public float arcHeight = 2.5f;

	[Tooltip( "Recovery arc peak is this many meters above the safe target Y so the hop visually clears rim edges." )]
	[Min( 0.05f )]
	public float arcClearanceAboveTarget = 2f;
}
