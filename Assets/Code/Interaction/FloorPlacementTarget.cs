using System.Collections;
using System.Collections.Generic;

using UnityEngine;

/// <summary>Floor / world surface placement: settle at ray hit with no throw impulse.</summary>
public sealed class FloorPlacementTarget : ITreasurePlacementTarget
{
	static readonly Collider[] GemPlaceOverlap = new Collider[ 32 ];

	float _dropUpBias;

	public FloorPlacementTarget( float dropUpBias = 0.05f )
	{
		SetDropUpBias( dropUpBias );
	}

	public void SetDropUpBias( float dropUpBias )
	{
		_dropUpBias = Mathf.Max( 0f, dropUpBias );
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

		Vector3 dropPos = ResolveGemPlaceSeparation( item, GetDropPosition( in query, item ) );
		dropPos = ResolveGemPyramidJoinPose( item, dropPos, in query, out Quaternion joinRot );
		Quaternion dropRot = item != null && item.Definition != null && item.Definition.category == TreasureCategory.Gem
			? joinRot
			: GetPlaceRotation( item );
		Vector3 scale = item.GetWorldScale();

		PlayerCarry carry = query.Player != null ? query.Player.Carry : null;
		bool surfaceOk = carry != null && carry.Count > 0 && PlacementFloorSurface.IsWalkableFloorHit( in query.Hit );

		// Coins use a full item-mesh ghost on open floor (join nearby stacks via PlayerPlacement).
		if ( GroundCoinStack.IsGroundStackableCoin( item ) )
		{
			preview.SetItemMesh( dropPos, dropRot, scale, surfaceOk );
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
		dropPos = ResolveGemPlaceSeparation( item, dropPos );
		dropPos = ResolveGemPyramidJoinPose( item, dropPos, in query, out Quaternion joinRot );
		if ( item != null && item.Definition != null && item.Definition.category == TreasureCategory.Gem )
			dropRot = joinRot;

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
			rots[ i ] = i == 0 ? anchorRot : GetPlaceRotation( member );
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

		// Artifacts: seat mesh bottom on surface height when available, else ray hit + bottom offset.
		TreasureSurfaceWorld surfaceWorld = TreasureSurfaceWorld.Instance;
		if ( surfaceWorld != null && surfaceWorld.Sampler != null
			&& surfaceWorld.Sampler.TrySample( query.Hit.point, out TreasureSurfaceSample artSample )
			&& artSample.Traversable )
		{
			float y = artSample.Height + TreasureSurfaceSeat.GetStableContactLift( item );
			return new Vector3( query.Hit.point.x, y, query.Hit.point.z );
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

	/// <summary>
	/// Push the gem being placed away from different-type gems, loose coins, and coin stacks.
	/// Iterates until stable so multiple neighbors all repel the placed gem.
	/// </summary>
	public static Vector3 ResolveGemPlaceSeparation( TreasureItem placing, Vector3 dropPos )
	{
		if ( placing == null || placing.Definition == null )
			return dropPos;
		if ( placing.Definition.category != TreasureCategory.Gem )
			return dropPos;

		Vector3 pos = dropPos;
		for ( int iter = 0; iter < 4; iter++ )
		{
			Vector3 next = ResolveGemPlaceSeparationOnce( placing, pos );
			Vector3 delta = next - pos;
			delta.y = 0f;
			pos = next;
			if ( delta.sqrMagnitude < 0.0001f )
				break;
		}

		return pos;
	}

	static Vector3 ResolveGemPlaceSeparationOnce( TreasureItem placing, Vector3 dropPos )
	{
		TreasureSurfaceDefinition surfaceDef = null;
		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		if ( world != null )
			surfaceDef = world.Definition;

		float pushRadius = surfaceDef != null ? surfaceDef.gemPushRadius : 0.18f;
		float coinPushScale = surfaceDef != null ? surfaceDef.gemVsCoinPushRadiusScale : 1f;
		float selfRadius = Mathf.Max(
			pushRadius,
			TreasureSurfaceSeat.EstimatePushRadius( placing, pushRadius ) );
		float queryRadius = Mathf.Max( 0.45f, selfRadius * 4f );

		int hits = Physics.OverlapSphereNonAlloc(
			dropPos,
			queryRadius,
			GemPlaceOverlap,
			Physics.DefaultRaycastLayers,
			QueryTriggerInteraction.Ignore );

		Vector3 push = Vector3.zero;
		for ( int i = 0; i < hits; i++ )
		{
			Collider col = GemPlaceOverlap[ i ];
			if ( col == null )
				continue;

			TreasureItem other = col.GetComponentInParent<TreasureItem>();
			if ( other == null || other == placing || !other.IsWorldLoose || other.IsInFlight )
				continue;
			if ( other.Definition == null )
				continue;

			float otherRadius;
			if ( other.Definition.category == TreasureCategory.Gem )
			{
				if ( AreSameGemType( placing.Definition, other.Definition ) )
					continue;

				otherRadius = Mathf.Max(
					pushRadius,
					TreasureSurfaceSeat.EstimatePushRadius( other, pushRadius ) );
			}
			else if ( other.Definition.category == TreasureCategory.Coin )
			{
				otherRadius = TreasureSurfaceSeat.EstimatePushRadius( other, 0.08f ) * coinPushScale;
			}
			else
				continue;

			float minDist = selfRadius + otherRadius;

			Vector3 delta = dropPos - other.transform.position;
			delta.y = 0f;
			float distSq = delta.sqrMagnitude;
			if ( distSq < 0.0000001f )
			{
				push.x += ( ( placing.GetInstanceID() & 1 ) == 0 ? 1f : -1f ) * minDist;
				continue;
			}

			float dist = Mathf.Sqrt( distSq );
			if ( dist >= minDist )
				continue;

			push += ( delta / dist ) * ( minDist - dist );
		}

		// Also repel from other gem pyramid contacts of different types.
		IReadOnlyList<GemPyramidCluster> clusters = GemPyramidRegistry.ActiveClusters;
		for ( int c = 0; c < clusters.Count; c++ )
		{
			GemPyramidCluster cluster = clusters[ c ];
			if ( cluster == null || cluster.Definition == null )
				continue;
			if ( AreSameGemType( placing.Definition, cluster.Definition ) )
				continue;

			for ( int m = 0; m < cluster.Members.Count; m++ )
			{
				TreasureItem other = cluster.Members[ m ];
				if ( other == null || other == placing )
					continue;

				float otherRadius = Mathf.Max(
					pushRadius,
					TreasureSurfaceSeat.EstimatePushRadius( other, pushRadius ) );
				float minDist = selfRadius + otherRadius;
				Vector3 delta = dropPos - other.transform.position;
				delta.y = 0f;
				float distSq = delta.sqrMagnitude;
				if ( distSq < 0.0000001f )
				{
					push.x += 0.02f;
					continue;
				}

				float dist = Mathf.Sqrt( distSq );
				if ( dist >= minDist )
					continue;

				push += ( delta / dist ) * ( minDist - dist );
			}
		}

		IReadOnlyList<GroundCoinStack> stacks = GroundCoinStack.ActiveStacks;
		for ( int s = 0; s < stacks.Count; s++ )
		{
			GroundCoinStack stack = stacks[ s ];
			if ( stack == null || stack.Count <= 0 )
				continue;

			Vector3 stackPos = stack.ContactPosition;
			Vector3 delta = dropPos - stackPos;
			delta.y = 0f;
			float distSq = delta.sqrMagnitude;
			float minDist = selfRadius + stack.FootprintRadius * coinPushScale;
			if ( distSq >= minDist * minDist )
				continue;

			if ( distSq < 0.0000001f )
			{
				push.x += 0.02f;
				continue;
			}

			float dist = Mathf.Sqrt( distSq );
			push += ( delta / dist ) * ( minDist - dist );
		}

		if ( push.sqrMagnitude < 0.0000001f )
			return dropPos;

		Vector3 separated = dropPos;
		separated.x += push.x;
		separated.z += push.z;

		if ( world != null && world.Sampler != null
			&& world.Sampler.TrySample( separated, out TreasureSurfaceSample sample )
			&& sample.Traversable )
		{
			separated.y = sample.Height + TreasureSurfaceSeat.GetStableContactLift( placing );
			return separated;
		}

		separated.y = dropPos.y;
		return separated;
	}

	static Vector3 ResolveGemPyramidJoinPose(
		TreasureItem item,
		Vector3 dropPos,
		in PlacementQuery query,
		out Quaternion dropRot )
	{
		dropRot = GetPlaceRotation( item );
		if ( item == null || item.Definition == null || item.Definition.category != TreasureCategory.Gem )
			return dropPos;

		// Always snap into a nearby same-type pyramid when in join radius.
		if ( GemPyramidRegistry.TryGetJoinPose( item, dropPos, out Vector3 joinPos, out Quaternion joinRot ) )
		{
			dropRot = joinRot;
			return joinPos;
		}

		return dropPos;
	}

	static bool AreSameGemType( TreasureDefinition a, TreasureDefinition b )
	{
		return GemPyramidRegistry.AreSameGemType( a, b );
	}
}
