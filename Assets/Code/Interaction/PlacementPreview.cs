using UnityEngine;

public enum PlacementGhostStyle
{
	ItemMesh = 0,
	StackOutline = 1,
	Suppressed = 2
}

/// <summary>World-space ghost pose for the item about to be placed.</summary>
public struct PlacementPreview
{
	public Vector3 Position;
	public Quaternion Rotation;
	public Vector3 Scale;
	public bool IsValid;
	public PlacementGhostStyle GhostStyle;
	public Vector3 VolumeContact;
	public float VolumeHeight;
	public float VolumeDiameter;

	public void SetItemMesh( Vector3 position, Quaternion rotation, Vector3 scale, bool valid )
	{
		GhostStyle = PlacementGhostStyle.ItemMesh;
		Position = position;
		Rotation = rotation;
		Scale = scale;
		IsValid = valid;
		VolumeContact = position;
		VolumeHeight = 0f;
		VolumeDiameter = 0f;
	}

	/// <summary>
	/// Outline-only stack/station feedback — no cylinder volume ghost and no item-mesh tip ghost.
	/// </summary>
	public void SetStackOutline( Vector3 contact, Quaternion rotation, Vector3 scale, bool valid )
	{
		GhostStyle = PlacementGhostStyle.StackOutline;
		Position = contact;
		VolumeContact = contact;
		Rotation = rotation;
		Scale = scale;
		VolumeHeight = 0f;
		VolumeDiameter = 0f;
		IsValid = valid;
	}

	public void SetSuppressed( Vector3 position, Quaternion rotation, Vector3 scale, bool valid )
	{
		GhostStyle = PlacementGhostStyle.Suppressed;
		Position = position;
		Rotation = rotation;
		Scale = scale;
		IsValid = valid;
		VolumeContact = position;
		VolumeHeight = 0f;
		VolumeDiameter = 0f;
	}
}
