using UnityEngine;

/// <summary>World-space ghost pose for the item about to be placed.</summary>
public struct PlacementPreview
{
	public Vector3 Position;
	public Quaternion Rotation;
	public Vector3 Scale;
	public bool IsValid;
}
