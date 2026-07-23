using UnityEngine;

/// <summary>Raycast/aim context passed to placement targets (no allocations).</summary>
public struct PlacementQuery
{
	public PlayerController Player;
	public RaycastHit Hit;
	public bool HasHit;
	public float InteractRange;

	/// <summary>
	/// When true (right-click place), pick the nearest valid slot/stack on the surface,
	/// not only the slot nearest aim.
	/// </summary>
	public bool AutoFindValidSlot;
}
