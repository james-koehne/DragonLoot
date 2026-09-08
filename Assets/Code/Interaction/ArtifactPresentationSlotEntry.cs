using System;

using UnityEngine;

[Serializable]
public struct ArtifactPresentationSlotEntry
{
	[Tooltip( "World anchor for this slot. Move in the scene to shape the display." )]
	public Transform anchor;

	[Tooltip( "Only this artifact definition can be placed in the slot. Ignored when the table uses a shared required artifact." )]
	public TreasureDefinition requiredArtifact;

	[Tooltip( "Extra Euler rotation applied in the slot, after table socket rotation." )]
	public Vector3 rotationOffset;
}
