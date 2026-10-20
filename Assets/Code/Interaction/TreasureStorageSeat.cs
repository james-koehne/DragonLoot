using UnityEngine;

/// <summary>
/// Shared seating for treasure on storage platforms (display tables, minecart cargo, etc.).
/// Plants the mesh/collider base onto the platform collider top; stack height is added along up.
/// </summary>
public static class TreasureStorageSeat
{
	/// <summary>
	/// Prefers a collider on <paramref name="displayArea"/> (cargo floor / tabletop child),
	/// otherwise the host storage collider.
	/// </summary>
	public static Collider ResolvePlatformCollider( Transform displayArea, Collider hostCollider )
	{
		if ( displayArea != null )
		{
			Collider onArea = displayArea.GetComponent<Collider>();
			if ( onArea != null )
				return onArea;
		}

		return hostCollider;
	}

	public static float GetPlatformTopWorldY( Collider platform, float fallbackY )
	{
		if ( platform == null )
			return fallbackY;
		return platform.bounds.max.y;
	}

	/// <summary>
	/// Local Y on <paramref name="displayArea"/> that matches the platform collider top.
	/// </summary>
	public static float GetPlatformTopLocalY( Collider platform, Transform displayArea )
	{
		if ( displayArea == null )
			return 0f;

		float topY = GetPlatformTopWorldY( platform, displayArea.position.y );
		Vector3 world = displayArea.position;
		world.y = topY;
		return displayArea.InverseTransformPoint( world ).y;
	}

	/// <summary>
	/// Distance from pivot to the lowest mesh/collider point along <paramref name="up"/>.
	/// </summary>
	public static float GetSeatLift( TreasureItem item, Quaternion worldRotation, Vector3 up )
	{
		if ( item == null )
			return 0f;
		return TreasureSurfaceSeat.GetBottomOffsetAlongUp( item, worldRotation, up );
	}

	/// <summary>
	/// World position with XZ from <paramref name="planePoint"/> and Y from platform top + stack + seat lift.
	/// </summary>
	public static Vector3 PlantWorldPosition(
		Collider platform,
		Vector3 planePoint,
		float stackHeight,
		TreasureItem item,
		Quaternion worldRotation,
		Vector3 up )
	{
		if ( up.sqrMagnitude < 0.0001f )
			up = Vector3.up;
		else
			up.Normalize();

		float surfaceY = GetPlatformTopWorldY( platform, planePoint.y );
		float seatLift = GetSeatLift( item, worldRotation, up );
		Vector3 pos = planePoint;
		pos.y = surfaceY + stackHeight + seatLift;
		return pos;
	}
}
