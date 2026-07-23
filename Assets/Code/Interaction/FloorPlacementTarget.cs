using System.Collections;
using System.Collections.Generic;

using UnityEngine;

/// <summary>Floor / world surface placement: settle at ray hit with no throw impulse.</summary>
public sealed class FloorPlacementTarget : ITreasurePlacementTarget
{
	readonly float _dropUpBias;

	public FloorPlacementTarget( float dropUpBias = 0.05f )
	{
		_dropUpBias = dropUpBias;
	}

	/// <summary>Minimum upright alignment for a surface to count as placeable floor (vs wall/ceiling).</summary>
	const float MinFloorUpDot = 0.35f;

	public bool CanPlace( TreasureItem item, in PlacementQuery query )
	{
		if ( item == null || query.Player == null )
			return false;

		// Ground-stackable coins must go through GroundCoinStack, never free floor seeds.
		if ( GroundCoinStack.IsGroundStackableCoin( item ) )
			return false;

		PlayerCarry carry = query.Player.Carry;
		if ( carry == null || carry.Count <= 0 )
			return false;

		return IsFloorSurfacePlaceable( in query );
	}

	public bool TryGetPlacementPreview( TreasureItem item, in PlacementQuery query, out PlacementPreview preview )
	{
		preview = default;
		if ( item == null || !query.HasHit )
			return false;

		preview.Position = GetDropPosition( in query, item );
		preview.Rotation = GetPlaceRotation( item );
		preview.Scale = item.GetWorldScale();

		// Coins still need a valid floor pose for GroundCoinStack create/join previews.
		if ( GroundCoinStack.IsGroundStackableCoin( item ) )
		{
			PlayerCarry carry = query.Player != null ? query.Player.Carry : null;
			preview.IsValid = carry != null && carry.Count > 0 && IsFloorSurfacePlaceable( in query );
		}
		else
			preview.IsValid = CanPlace( item, in query );

		return true;
	}

	static bool IsFloorSurfacePlaceable( in PlacementQuery query )
	{
		if ( !query.HasHit || query.Hit.collider == null )
			return false;

		if ( !PlacementFloorSurface.IsFloorCollider( query.Hit.collider ) )
			return false;

		Vector3 normal = query.Hit.normal.sqrMagnitude > 0.0001f
			? query.Hit.normal.normalized
			: Vector3.up;
		return Vector3.Dot( normal, Vector3.up ) >= MinFloorUpDot;
	}

	public bool TryPlace( TreasureItem item, in PlacementQuery query )
	{
		if ( !CanPlace( item, in query ) )
			return false;

		PlayerCarry carry = query.Player.Carry;
		if ( carry == null )
			return false;

		Vector3 dropPos = GetDropPosition( in query, item );
		Quaternion dropRot = GetPlaceRotation( item );

		if ( !carry.TryRemoveBottomCluster( out List<TreasureItem> cluster ) || cluster == null || cluster.Count == 0 )
			return false;

		BuildFloorEndPoses( cluster, dropPos, dropRot, out Vector3[] ends, out Quaternion[] rots );

		for ( int i = 0; i < cluster.Count; i++ )
		{
			if ( cluster[ i ] != null )
				cluster[ i ].BeginFlight();
		}

		bool flipCoin = CoinFlipMotion.IsCoin( item );
		float arcHeight = flipCoin ? CoinFlipMotion.DefaultArcHeight : CoinFlipMotion.DefaultItemArcHeight;
		float spins = flipCoin ? CoinFlipMotion.DefaultSpins : 0f;
		float duration = flipCoin ? CoinFlipMotion.DefaultDuration : CoinFlipMotion.DefaultItemArcDuration;
		TreasureMotionHost.Run( CoinFlipFloorRoutine( cluster, ends, rots, duration, arcHeight, spins ) );
		return true;
	}

	static IEnumerator CoinFlipFloorRoutine(
		List<TreasureItem> cluster,
		Vector3[] ends,
		Quaternion[] rots,
		float duration,
		float arcHeight,
		float spins )
	{
		yield return CoinFlipMotion.AnimateWorldFlips( cluster, ends, rots, duration, arcHeight, spins );

		for ( int i = 0; i < cluster.Count; i++ )
		{
			TreasureItem member = cluster[ i ];
			if ( member == null )
				continue;

			member.EndFlight();

			if ( member.State == TreasureItemState.Held && member.Owner is PlayerCarry carry && carry.ContainsItem( member ) )
				continue;

			member.EnterSettledPhysics( ends[ i ], rots[ i ] );
		}

		TreasureItem seed = null;
		for ( int i = 0; i < cluster.Count; i++ )
		{
			if ( cluster[ i ] != null && cluster[ i ].IsWorldLoose )
			{
				seed = cluster[ i ];
				break;
			}
		}

		if ( seed != null )
			GroundTreasureStackTarget.RestackColumnByThickness( seed );
	}

	static void BuildFloorEndPoses(
		List<TreasureItem> cluster,
		Vector3 anchorDropPos,
		Quaternion anchorRot,
		out Vector3[] ends,
		out Quaternion[] rots )
	{
		ends = new Vector3[ cluster.Count ];
		rots = new Quaternion[ cluster.Count ];
		float stackedY = 0f;
		for ( int i = 0; i < cluster.Count; i++ )
		{
			TreasureItem member = cluster[ i ];
			if ( member == null )
				continue;

			ends[ i ] = anchorDropPos + Vector3.up * stackedY;
			rots[ i ] = GetPlaceRotation( member );
			stackedY += TreasureStackSpacing.GetStep( member );
		}
	}

	public void Remove( TreasureItem item )
	{
		// Floor does not own items after physics release.
	}

	Vector3 GetDropPosition( in PlacementQuery query, TreasureItem item )
	{
		Vector3 normal = query.Hit.normal.sqrMagnitude > 0.0001f
			? query.Hit.normal.normalized
			: Vector3.up;

		// Prefer world-up on walkable ground so items seat flat on the floor plane.
		Vector3 up = Vector3.Dot( normal, Vector3.up ) >= MinFloorUpDot
			? Vector3.up
			: normal;

		// Surface sim seats the transform pivot on the contact height. Match that so coins
		// touch the floor instead of floating by mesh-half + drop bias.
		float lift = 0.001f;
		if ( item != null && item.Definition != null && item.Definition.category != TreasureCategory.Coin )
		{
			Quaternion placeRot = GetPlaceRotation( item );
			float bottomOffset = GetBottomOffsetAlongUp( item, placeRot, up );
			lift = Mathf.Max( _dropUpBias, 0.001f ) + bottomOffset;
		}

		return query.Hit.point + up * lift;
	}

	/// <summary>
	/// Distance from pivot to the lowest mesh/collider point along <paramref name="up"/>,
	/// using placement (world) scale so the ghost sits on the surface instead of through it.
	/// </summary>
	static float GetBottomOffsetAlongUp( TreasureItem item, Quaternion placeRot, Vector3 up )
	{
		float fallback = TreasureStackSpacing.GetHalfStep( item );
		if ( item == null )
			return fallback;

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

				// Corner in root-local space at current (held) scale, then rescale to world place scale.
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
				// Approximate with axis extents in root space; rescale held -> place.
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

	static Quaternion GetPlaceRotation( TreasureItem item )
	{
		if ( item == null )
			return Quaternion.identity;

		Quaternion rot = item.transform.rotation;
		if ( item.Definition != null && item.Definition.category == TreasureCategory.Coin )
			return FlattenUpright( rot );

		return rot;
	}

	static void ReleaseClusterToWorld( List<TreasureItem> cluster, Vector3 anchorDropPos, Quaternion anchorRot )
	{
		if ( cluster == null || cluster.Count == 0 )
			return;

		float stackedY = 0f;
		TreasureItem seed = null;
		for ( int i = 0; i < cluster.Count; i++ )
		{
			TreasureItem member = cluster[ i ];
			if ( member == null )
				continue;

			Vector3 pos = anchorDropPos + Vector3.up * stackedY;
			Quaternion rot = member.Definition != null && member.Definition.category == TreasureCategory.Coin
				? FlattenUpright( anchorRot )
				: member.transform.rotation;

			member.EnterSettledPhysics( pos, rot );
			if ( seed == null )
				seed = member;
			stackedY += TreasureStackSpacing.GetStep( member );
		}

		if ( seed != null )
			GroundTreasureStackTarget.RestackColumnByThickness( seed );
	}

	static Quaternion FlattenUpright( Quaternion source )
	{
		Vector3 flatForward = Vector3.ProjectOnPlane( source * Vector3.forward, Vector3.up );
		if ( flatForward.sqrMagnitude < 0.0001f )
			flatForward = Vector3.ProjectOnPlane( source * Vector3.right, Vector3.up );
		if ( flatForward.sqrMagnitude < 0.0001f )
			flatForward = Vector3.forward;
		return Quaternion.LookRotation( flatForward.normalized, Vector3.up );
	}
}
