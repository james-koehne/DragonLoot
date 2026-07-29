using UnityEngine;

/// <summary>
/// Shared contact-height seating for loose treasure on the surface simulator.
/// Coins sit pivot-on-plane; gems/artifacts sit on their mesh/collider bottom.
/// </summary>
public static class TreasureSurfaceSeat
{
	public static float GetContactY(
		TreasureItem item,
		float surfaceHeight,
		Quaternion worldRotation,
		Vector3 up )
	{
		if ( up.sqrMagnitude < 0.0001f )
			up = Vector3.up;
		else
			up.Normalize();

		float lift = GetContactLift( item, worldRotation, up );
		return surfaceHeight + lift;
	}

	public static float GetContactY(
		TreasureItem item,
		TreasureSurfaceSample sample,
		Quaternion worldRotation )
	{
		Vector3 up = sample.Normal.sqrMagnitude > 0.0001f ? sample.Normal.normalized : Vector3.up;
		if ( Vector3.Dot( up, Vector3.up ) < PlacementFloorSurface.MinFloorUpDot )
			up = Vector3.up;
		return GetContactY( item, sample.Height, worldRotation, up );
	}

	public static float GetContactLift( TreasureItem item, Quaternion worldRotation, Vector3 up )
	{
		if ( item == null || item.Definition == null )
			return 0f;

		if ( item.Definition.category == TreasureCategory.Coin )
			return 0f;

		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		TreasureSurfaceDefinition def = world != null ? world.Definition : null;
		if ( def != null && !def.gemSeatUsesBottomOffset )
			return 0f;

		return GetBottomOffsetAlongUp( item, worldRotation, up );
	}

	/// <summary>
	/// Rotation-independent seat lift for rolling gems. Using the live mesh bottom while
	/// tumbling makes contact Y bounce every frame.
	/// </summary>
	public static float GetStableContactLift( TreasureItem item )
	{
		if ( item == null || item.Definition == null )
			return 0f;

		if ( item.Definition.category == TreasureCategory.Coin )
			return 0f;

		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		TreasureSurfaceDefinition def = world != null ? world.Definition : null;
		if ( def != null && !def.gemSeatUsesBottomOffset )
			return 0f;

		float radius = EstimatePushRadius( item, TreasureStackSpacing.GetHalfStep( item ) );
		// Prefer the larger of push-radius and upright bottom offset so settled pose stays clear.
		float upright = GetBottomOffsetAlongUp( item, Quaternion.identity, Vector3.up );
		return Mathf.Max( radius, upright );
	}

	/// <summary>
	/// Distance from pivot to the lowest mesh/collider point along <paramref name="up"/>,
	/// using placement (world) scale so the body sits on the surface instead of through it.
	/// </summary>
	public static float GetBottomOffsetAlongUp( TreasureItem item, Quaternion placeRot, Vector3 up )
	{
		float fallback = TreasureStackSpacing.GetHalfStep( item );
		if ( item == null )
			return fallback;

		if ( up.sqrMagnitude < 0.0001f )
			up = Vector3.up;
		else
			up.Normalize();

		Vector3 placeScale = item.GetWorldScale();
		Vector3 heldScale = item.GetHeldScale();
		Transform root = item.transform;

		float maxBelow = 0f;
		bool any = false;

		MeshFilter[] filters = item.GetComponentsInChildren<MeshFilter>();
		for ( int f = 0; f < filters.Length; f++ )
		{
			MeshFilter filter = filters[ f ];
			if ( filter == null || filter.sharedMesh == null )
				continue;

			Bounds lb = filter.sharedMesh.bounds;
			Vector3 min = lb.min;
			Vector3 max = lb.max;
			Transform meshT = filter.transform;

			for ( int i = 0; i < 8; i++ )
			{
				Vector3 meshLocal = new Vector3(
					( i & 1 ) == 0 ? min.x : max.x,
					( i & 2 ) == 0 ? min.y : max.y,
					( i & 4 ) == 0 ? min.z : max.z );

				Vector3 worldCorner = meshT.TransformPoint( meshLocal );
				Vector3 rootLocal = root.InverseTransformPoint( worldCorner );
				rootLocal = RescaleHeldLocalToPlace( rootLocal, heldScale, placeScale );
				Vector3 placedOffset = placeRot * rootLocal;
				float below = -Vector3.Dot( placedOffset, up );
				if ( below > maxBelow )
					maxBelow = below;
				any = true;
			}
		}

		if ( !any )
		{
			Collider[] colliders = item.GetComponentsInChildren<Collider>();
			for ( int c = 0; c < colliders.Length; c++ )
			{
				Collider col = colliders[ c ];
				if ( col == null || !col.enabled )
					continue;

				Bounds wb = col.bounds;
				Vector3 center = root.InverseTransformPoint( wb.center );
				Vector3 extents = wb.extents;
				center = RescaleHeldLocalToPlace( center, heldScale, placeScale );
				extents = RescaleHeldLocalToPlace( extents, heldScale, placeScale );
				extents = new Vector3( Mathf.Abs( extents.x ), Mathf.Abs( extents.y ), Mathf.Abs( extents.z ) );

				for ( int i = 0; i < 8; i++ )
				{
					Vector3 corner = center + new Vector3(
						( i & 1 ) == 0 ? -extents.x : extents.x,
						( i & 2 ) == 0 ? -extents.y : extents.y,
						( i & 4 ) == 0 ? -extents.z : extents.z );
					Vector3 placedOffset = placeRot * corner;
					float below = -Vector3.Dot( placedOffset, up );
					if ( below > maxBelow )
						maxBelow = below;
					any = true;
				}
			}
		}

		if ( !any || maxBelow < 0.0001f )
			return fallback;

		return maxBelow;
	}

	public static float EstimatePushRadius( TreasureItem item, float fallback )
	{
		if ( item == null )
			return fallback;

		float half = TreasureStackSpacing.GetHalfStep( item );
		if ( item.Definition != null )
		{
			Vector3 scale = item.Definition.worldScale;
			float xz = Mathf.Max( Mathf.Abs( scale.x ), Mathf.Abs( scale.z ) ) * 0.5f;
			if ( xz > 0.0001f )
				return Mathf.Max( half, xz );
		}

		return Mathf.Max( fallback, half );
	}

	static Vector3 RescaleHeldLocalToPlace( Vector3 rootLocal, Vector3 heldScale, Vector3 placeScale )
	{
		return new Vector3(
			SafeRescaleAxis( rootLocal.x, heldScale.x, placeScale.x ),
			SafeRescaleAxis( rootLocal.y, heldScale.y, placeScale.y ),
			SafeRescaleAxis( rootLocal.z, heldScale.z, placeScale.z ) );
	}

	static float SafeRescaleAxis( float value, float fromScale, float toScale )
	{
		if ( Mathf.Abs( fromScale ) < 0.0001f )
			return value;
		return value * ( toScale / fromScale );
	}
}
