using UnityEngine;

/// <summary>
/// Walkable capsule probes for ravine safe-ground caching (distance from ledge).
/// </summary>
public static class RavineWalkableLedge
{
	const int RingSampleCount = 8;

	/// <summary>
	/// True when the standing pose and samples at <paramref name="minDistance"/> in a ring all find Walkable support.
	/// </summary>
	public static bool IsFarEnoughFromLedge(
		Vector3 characterPosition,
		float minDistance,
		Vector3 capsuleCenter,
		float capsuleHeight,
		float capsuleRadius,
		float groundProbeDistance )
	{
		if ( !HasWalkableSupport( characterPosition, capsuleCenter, capsuleHeight, capsuleRadius, groundProbeDistance ) )
			return false;

		float distance = Mathf.Max( minDistance, 0.01f );
		for ( int i = 0; i < RingSampleCount; i++ )
		{
			float angle = ( Mathf.PI * 2f ) * ( i / (float)RingSampleCount );
			Vector3 offset = new Vector3( Mathf.Cos( angle ), 0f, Mathf.Sin( angle ) ) * distance;
			if ( !HasWalkableSupport( characterPosition + offset, capsuleCenter, capsuleHeight, capsuleRadius, groundProbeDistance ) )
				return false;
		}

		return true;
	}

	public static bool HasWalkableSupport(
		Vector3 characterPosition,
		Vector3 capsuleCenter,
		float capsuleHeight,
		float capsuleRadius,
		float groundProbeDistance )
	{
		float radius = Mathf.Max( capsuleRadius, 0.05f );
		GetCapsuleEnds( characterPosition, capsuleCenter, capsuleHeight, radius, out Vector3 p1, out Vector3 p2 );

		const float lift = 0.05f;
		p1 += Vector3.up * lift;
		p2 += Vector3.up * lift;

		float probe = Mathf.Max( groundProbeDistance, 0.1f ) + lift;
		if ( !Physics.CapsuleCast(
			    p1,
			    p2,
			    radius,
			    Vector3.down,
			    out RaycastHit hit,
			    probe,
			    PhysicsLayers.WalkableMask,
			    QueryTriggerInteraction.Ignore ) )
			return false;

		return PlacementFloorSurface.IsWalkableFloorHit( in hit );
	}

	static void GetCapsuleEnds( Vector3 position, Vector3 center, float height, float radius, out Vector3 p1, out Vector3 p2 )
	{
		float half = Mathf.Max( height * 0.5f - radius, 0f );
		Vector3 worldCenter = position + center;
		p1 = worldCenter + Vector3.up * half;
		p2 = worldCenter - Vector3.up * half;
	}
}
