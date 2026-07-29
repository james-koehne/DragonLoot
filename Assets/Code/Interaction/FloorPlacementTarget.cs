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

		if ( !query.HasHit )
			return false;

		return PlacementFloorSurface.IsWalkableFloorHit( in query.Hit );
	}

	public bool TryGetPlacementPreview( TreasureItem item, in PlacementQuery query, out PlacementPreview preview )
	{
		preview = default;
		if ( item == null || !query.HasHit )
			return false;

		Vector3 dropPos = GetDropPosition( in query, item );
		Quaternion dropRot = GetPlaceRotation( item );
		Vector3 scale = item.GetWorldScale();

		PlayerCarry carry = query.Player != null ? query.Player.Carry : null;
		bool surfaceOk = carry != null && carry.Count > 0 && PlacementFloorSurface.IsWalkableFloorHit( in query.Hit );

		// Coins still need a valid floor pose for GroundCoinStack create/join previews.
		if ( GroundCoinStack.IsGroundStackableCoin( item ) )
		{
			float height = TreasureStackSpacing.GetStep( item );
			float diameter = Mathf.Max( scale.x, scale.z );
			preview.SetStackVolume( dropPos, dropRot, scale, height, diameter, surfaceOk );
			return true;
		}

		preview.SetItemMesh( dropPos, dropRot, scale, CanPlace( item, in query ) );
		return true;
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
		Vector3 up = Vector3.Dot( normal, Vector3.up ) >= PlacementFloorSurface.MinFloorUpDot
			? Vector3.up
			: normal;

		Quaternion placeRot = GetPlaceRotation( item );

		// Match surface-sim seating so place and throw/roll share the same contact height.
		if ( TreasureItem.UsesSurfaceSimulation( item != null ? item.Definition : null ) )
		{
			TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
			if ( world != null && world.Sampler != null
				&& world.Sampler.TrySample( query.Hit.point, out TreasureSurfaceSample sample )
				&& sample.Traversable )
			{
				float y = item != null && item.Definition != null && item.Definition.category == TreasureCategory.Gem
					? sample.Height + TreasureSurfaceSeat.GetStableContactLift( item )
					: TreasureSurfaceSeat.GetContactY( item, sample, placeRot );
				return new Vector3( query.Hit.point.x, y, query.Hit.point.z );
			}

			float lift = item != null && item.Definition != null && item.Definition.category == TreasureCategory.Gem
				? TreasureSurfaceSeat.GetStableContactLift( item )
				: TreasureSurfaceSeat.GetContactLift( item, placeRot, up );
			return query.Hit.point + up * Mathf.Max( lift, 0.001f );
		}

		float legacyLift = 0.001f;
		if ( item != null && item.Definition != null && item.Definition.category != TreasureCategory.Coin )
			legacyLift = Mathf.Max( _dropUpBias, 0.001f ) + TreasureSurfaceSeat.GetBottomOffsetAlongUp( item, placeRot, up );

		return query.Hit.point + up * legacyLift;
	}

	static Quaternion GetPlaceRotation( TreasureItem item )
	{
		if ( item == null )
			return Quaternion.identity;

		Quaternion rot = item.transform.rotation;
		if ( item.Definition != null && item.Definition.category == TreasureCategory.Coin )
			return TreasureOrientation.FlattenUpright( rot );

		return rot;
	}
}
