using UnityEngine;

/// <summary>Shared orientation helpers for loose treasure seating and stacking.</summary>
public static class TreasureOrientation
{
	/// <summary>
	/// Flattens yaw onto the ground plane while keeping the item upright (coins / columns).
	/// </summary>
	public static Quaternion FlattenUpright( Quaternion source )
	{
		Vector3 flatForward = Vector3.ProjectOnPlane( source * Vector3.forward, Vector3.up );
		if ( flatForward.sqrMagnitude < 0.0001f )
			flatForward = Vector3.ProjectOnPlane( source * Vector3.right, Vector3.up );
		if ( flatForward.sqrMagnitude < 0.0001f )
			flatForward = Vector3.forward;
		return Quaternion.LookRotation( flatForward.normalized, Vector3.up );
	}
}
