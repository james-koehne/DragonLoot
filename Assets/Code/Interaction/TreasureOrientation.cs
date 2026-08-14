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

	/// <summary>
	/// Keeps source yaw on the tangent plane and aligns local up to <paramref name="normal"/>.
	/// </summary>
	public static Quaternion AlignUprightToNormal( Quaternion source, Vector3 normal )
	{
		if ( normal.sqrMagnitude < 0.0001f )
			normal = Vector3.up;
		else
			normal = normal.normalized;

		Quaternion flat = FlattenUpright( source );
		if ( Vector3.Dot( normal, Vector3.up ) >= 0.999f )
			return flat;

		return Quaternion.FromToRotation( Vector3.up, normal ) * flat;
	}
}
