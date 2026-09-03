using UnityEngine;

/// <summary>
/// Authorable policy for <see cref="TreasurePileLatentBake"/> generation (not pose data).
/// Assign on <see cref="TreasurePileVisual"/> next to the bake asset, then Bake Latents.
/// </summary>
[CreateAssetMenu( fileName = "TreasurePileLatentBakeSettings", menuName = "Definitions/TreasurePileLatentBakeSettings" )]
public class TreasurePileLatentBakeSettings : ScriptableObject
{
	[Header( "Placement" )]
	[Tooltip( "Seat gem/artifact remainder latents just under the mound surface so they spawn without digging. XZ spacing still applies, then relaxes if the surface is too crowded. Rebake after changing." )]
	public bool spawnTreasureNearSurface = false;
}
