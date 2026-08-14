using System.Collections;
using System.Collections.Generic;

using UnityEngine;

/// <summary>Floor / world surface placement: settle at ray hit with no throw impulse.</summary>
public sealed class FloorPlacementTarget : ITreasurePlacementTarget
{
	static readonly Collider[] GemPlaceOverlap = new Collider[ 32 ];
	static readonly Collider[] CoinPlaceOverlap = new Collider[ 32 ];
	static readonly Collider[] ArtifactPlaceOverlap = new Collider[ 48 ];

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

		if ( !PlacementFloorSurface.IsWalkableFloorHit( in query.Hit ) )
			return false;

		if ( item.Definition != null && item.Definition.category == TreasureCategory.Artifact )
		{
			Vector3 dropPos = GetDropPosition( in query, item );
			Quaternion dropRot = GetPlaceRotation( item, in query );
			if ( ArtifactOverlapsObstruction( item, dropPos, dropRot ) )
				return false;
		}

		return true;
	}

	public bool TryGetPlacementPreview( TreasureItem item, in PlacementQuery query, out PlacementPreview preview )
	{
		preview = default;
		if ( item == null || !query.HasHit )
			return false;

		Vector3 dropPos = ResolveGemPlaceSeparation( item, GetDropPosition( in query, item ) );
		dropPos = ResolveCoinPlaceSeparation( item, dropPos );
		dropPos = ResolveGemPyramidJoinPose( item, dropPos, in query, out Quaternion joinRot );
		Quaternion dropRot = item != null && item.Definition != null && item.Definition.category == TreasureCategory.Gem
			? joinRot
			: GetPlaceRotation( item, in query );
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
		Quaternion dropRot = GetPlaceRotation( item, in query );
		dropPos = ResolveGemPlaceSeparation( item, dropPos );
		dropPos = ResolveCoinPlaceSeparation( item, dropPos );
		dropPos = ResolveGemPyramidJoinPose( item, dropPos, in query, out Quaternion joinRot );
		if ( item != null && item.Definition != null && item.Definition.category == TreasureCategory.Gem )
			dropRot = joinRot;

		if ( !carry.TryRemoveBottomCluster( out List<TreasureItem> cluster ) || cluster == null || cluster.Count == 0 )
			return false;

		BuildFloorEndPoses( cluster, dropPos, dropRot, ResolvePlaceUp( in query ), out Vector3[] ends, out Quaternion[] rots );

		for ( int i = 0; i < cluster.Count; i++ )
		{
			if ( cluster[ i ] != null )
				cluster[ i ].BeginFlight();
		}

		bool flipCoin = CoinFlipMotion.IsCoin( item );
		bool plantArtifact = ArtifactPlantFeedback.IsArtifact( item );
		float arcHeight = flipCoin ? CoinFlipMotion.DefaultArcHeight : CoinFlipMotion.DefaultItemArcHeight;
		float spins = flipCoin ? CoinFlipMotion.DefaultSpins : 0f;
		float duration = flipCoin ? CoinFlipMotion.DefaultDuration : CoinFlipMotion.DefaultItemArcDuration;
		if ( plantArtifact )
		{
			arcHeight = 0f;
			spins = 0f;
			duration = 0.05f;
		}

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

			if ( member.State == TreasureItemState.Held && member.Owner is PlayerCarry carry && carry.ContainsItem( member ) )
			{
				member.EndFlight();
				continue;
			}

			if ( ArtifactPlantFeedback.IsArtifact( member ) )
			{
				Vector3 plantPos = ends[ i ];
				Quaternion plantRot = TreasureOrientation.AlignUprightToNormal( rots[ i ], Vector3.up );
				TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
				if ( world != null && world.Sampler != null
					&& world.Sampler.TrySample( plantPos, out TreasureSurfaceSample sample )
					&& sample.Traversable )
				{
					Vector3 up = sample.Normal.sqrMagnitude > 0.0001f ? sample.Normal.normalized : Vector3.up;
					if ( Vector3.Dot( up, Vector3.up ) < PlacementFloorSurface.MinFloorUpDot )
						up = Vector3.up;
					plantRot = TreasureOrientation.AlignUprightToNormal( rots[ i ], up );
					plantPos.y = sample.Height + TreasureSurfaceSeat.GetStableContactLift( member );
				}

				member.EndFlight();
				member.EnterSettledPhysics( plantPos, plantRot );
				ArtifactPlantFeedback.PlayOn( member );
				TreasureInteractSfx.PlayPlace( member.Definition, plantPos );
				continue;
			}

			member.EndFlight();
			member.EnterSettledPhysics( ends[ i ], rots[ i ] );
			CoinGemInteractFeedback.PlayPlace( member );
			TreasureInteractSfx.PlayPlace( member.Definition, ends[ i ] );
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
		Vector3 placeUp,
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
			rots[ i ] = i == 0 ? anchorRot : GetPlaceRotation( member, placeUp );
			stackedY += TreasureStackSpacing.GetStep( member );
		}
	}

	public void Remove( TreasureItem item )
	{
		// Floor does not own items after physics release.
	}

	Vector3 GetDropPosition( in PlacementQuery query, TreasureItem item )
	{
		Vector3 up = ResolvePlaceUp( in query );
		Quaternion placeRot = GetPlaceRotation( item, in query );

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

	static Vector3 ResolvePlaceUp( in PlacementQuery query )
	{
		if ( !query.HasHit )
			return Vector3.up;

		Vector3 normal = query.Hit.normal.sqrMagnitude > 0.0001f
			? query.Hit.normal.normalized
			: Vector3.up;

		// Prefer world-up on walkable ground so items seat flat on the floor plane.
		if ( Vector3.Dot( normal, Vector3.up ) >= PlacementFloorSurface.MinFloorUpDot )
			return Vector3.up;

		return normal;
	}

	static Quaternion GetPlaceRotation( TreasureItem item, in PlacementQuery query )
	{
		return GetPlaceRotation( item, ResolvePlaceUp( in query ) );
	}

	static Quaternion GetPlaceRotation( TreasureItem item, Vector3 up )
	{
		if ( item == null )
			return Quaternion.identity;

		Quaternion rot = item.transform.rotation;
		if ( item.Definition == null )
			return rot;

		if ( item.Definition.category == TreasureCategory.Coin )
			return TreasureOrientation.FlattenUpright( rot );

		if ( item.Definition.category == TreasureCategory.Artifact )
			return TreasureOrientation.AlignUprightToNormal( rot, up );

		return rot;
	}

	/// <summary>
	/// True when an artifact ghost at <paramref name="dropPos"/> overlaps other treasure,
	/// coin stacks, gem pyramids, or non-floor interactables.
	/// Uses the item mesh bounds plus <see cref="TreasureSurfaceDefinition.artifactPlaceMeshLeeway"/>.
	/// </summary>
	public static bool ArtifactOverlapsObstruction( TreasureItem placing, Vector3 dropPos, Quaternion dropRot )
	{
		if ( placing == null || placing.Definition == null )
			return false;
		if ( placing.Definition.category != TreasureCategory.Artifact )
			return false;

		TreasureSurfaceDefinition surfaceDef = null;
		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		if ( world != null )
			surfaceDef = world.Definition;

		float leeway = surfaceDef != null ? surfaceDef.artifactPlaceMeshLeeway : 0.04f;
		float minRadius = surfaceDef != null ? surfaceDef.artifactPushRadius : 0.22f;
		float coinPushScale = surfaceDef != null ? surfaceDef.artifactVsCoinPushRadiusScale : 1.15f;

		GetMeshOrientedBoxAtPose(
			placing,
			dropPos,
			dropRot,
			leeway,
			minRadius,
			out Vector3 boxCenter,
			out Vector3 boxHalf,
			out float footprintRadius );

		int hits = Physics.OverlapBoxNonAlloc(
			boxCenter,
			boxHalf,
			ArtifactPlaceOverlap,
			dropRot,
			Physics.DefaultRaycastLayers,
			QueryTriggerInteraction.Ignore );

		for ( int i = 0; i < hits; i++ )
		{
			Collider col = ArtifactPlaceOverlap[ i ];
			if ( col == null )
				continue;

			if ( IsOwnOrPlayerCollider( placing, col ) )
				continue;

			if ( PlacementFloorSurface.IsFloorCollider( col ) )
				continue;

			TreasureItem other = col.GetComponentInParent<TreasureItem>();
			if ( other != null && other != placing )
			{
				if ( other.IsInFlight )
					continue;
				return true;
			}

			if ( col.GetComponentInParent<GroundCoinStack>() != null )
				return true;

			if ( col.GetComponentInParent<InteractableBase>() != null )
				return true;
		}

		IReadOnlyList<GemPyramidCluster> clusters = GemPyramidRegistry.ActiveClusters;
		float minDist = footprintRadius + Mathf.Max( 0.08f, minRadius * 0.5f );
		for ( int c = 0; c < clusters.Count; c++ )
		{
			GemPyramidCluster cluster = clusters[ c ];
			if ( cluster == null || cluster.Members == null )
				continue;

			for ( int m = 0; m < cluster.Members.Count; m++ )
			{
				TreasureItem member = cluster.Members[ m ];
				if ( member == null || member == placing )
					continue;

				Vector3 delta = dropPos - member.transform.position;
				delta.y = 0f;
				if ( delta.sqrMagnitude < minDist * minDist )
					return true;
			}
		}

		IReadOnlyList<GroundCoinStack> stacks = GroundCoinStack.ActiveStacks;
		for ( int s = 0; s < stacks.Count; s++ )
		{
			GroundCoinStack stack = stacks[ s ];
			if ( stack == null || stack.Count <= 0 )
				continue;

			Vector3 delta = dropPos - stack.ContactPosition;
			delta.y = 0f;
			float min = footprintRadius + stack.FootprintRadius * coinPushScale;
			if ( delta.sqrMagnitude < min * min )
				return true;
		}

		return false;
	}

	/// <summary>
	/// Push a thrown/placed artifact away from other artifacts, gems, coins, and coin stacks.
	/// Separation distance uses mesh footprint + leeway.
	/// </summary>
	public static Vector3 ResolveArtifactPlaceSeparation( TreasureItem placing, Vector3 dropPos )
	{
		if ( placing == null || placing.Definition == null )
			return dropPos;
		if ( placing.Definition.category != TreasureCategory.Artifact )
			return dropPos;

		Vector3 pos = dropPos;
		for ( int iter = 0; iter < 4; iter++ )
		{
			Vector3 next = ResolveArtifactPlaceSeparationOnce( placing, pos );
			Vector3 delta = next - pos;
			delta.y = 0f;
			pos = next;
			if ( delta.sqrMagnitude < 0.0001f )
				break;
		}

		return pos;
	}

	static Vector3 ResolveArtifactPlaceSeparationOnce( TreasureItem placing, Vector3 dropPos )
	{
		TreasureSurfaceDefinition surfaceDef = null;
		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		if ( world != null )
			surfaceDef = world.Definition;

		float leeway = surfaceDef != null ? surfaceDef.artifactPlaceMeshLeeway : 0.04f;
		float minRadius = surfaceDef != null ? surfaceDef.artifactPushRadius : 0.22f;
		float coinPushScale = surfaceDef != null ? surfaceDef.artifactVsCoinPushRadiusScale : 1.15f;
		float gemPushRadius = surfaceDef != null ? surfaceDef.gemPushRadius : 0.18f;

		Quaternion dropRot = TreasureOrientation.AlignUprightToNormal(
			placing.transform.rotation,
			Vector3.up );
		GetMeshOrientedBoxAtPose(
			placing,
			dropPos,
			dropRot,
			leeway,
			minRadius,
			out _,
			out _,
			out float selfRadius );

		float queryRadius = Mathf.Max( 0.55f, selfRadius * 4f );
		int hits = Physics.OverlapSphereNonAlloc(
			dropPos,
			queryRadius,
			ArtifactPlaceOverlap,
			Physics.DefaultRaycastLayers,
			QueryTriggerInteraction.Ignore );

		Vector3 push = Vector3.zero;
		for ( int i = 0; i < hits; i++ )
		{
			Collider col = ArtifactPlaceOverlap[ i ];
			if ( col == null )
				continue;

			TreasureItem other = col.GetComponentInParent<TreasureItem>();
			if ( other == null || other == placing || !other.IsWorldLoose || other.IsInFlight )
				continue;
			if ( other.Definition == null )
				continue;

			float otherRadius = EstimateItemFootprintRadius( other, gemPushRadius, minRadius, coinPushScale );
			if ( otherRadius <= 0f )
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

		IReadOnlyList<GemPyramidCluster> clusters = GemPyramidRegistry.ActiveClusters;
		for ( int c = 0; c < clusters.Count; c++ )
		{
			GemPyramidCluster cluster = clusters[ c ];
			if ( cluster == null || cluster.Members == null )
				continue;

			for ( int m = 0; m < cluster.Members.Count; m++ )
			{
				TreasureItem other = cluster.Members[ m ];
				if ( other == null || other == placing )
					continue;

				float otherRadius = Mathf.Max(
					gemPushRadius,
					TreasureSurfaceSeat.EstimatePushRadius( other, gemPushRadius ) );
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

	static float EstimateItemFootprintRadius(
		TreasureItem item,
		float gemPushRadius,
		float artifactPushRadius,
		float coinPushScale )
	{
		if ( item == null || item.Definition == null )
			return 0f;

		if ( item.Definition.category == TreasureCategory.Artifact )
		{
			if ( TryGetMeshLocalBounds( item, out Bounds local ) )
			{
				Vector3 scale = AbsVec( item.GetWorldScale() );
				float xz = 0.5f * Mathf.Max( local.size.x * scale.x, local.size.z * scale.z );
				return Mathf.Max( artifactPushRadius, xz );
			}

			return Mathf.Max(
				artifactPushRadius,
				TreasureSurfaceSeat.EstimatePushRadius( item, artifactPushRadius ) );
		}

		if ( item.Definition.category == TreasureCategory.Gem )
		{
			if ( GemPyramidRegistry.FindClusterContaining( item ) != null )
				return 0f;

			return Mathf.Max(
				gemPushRadius,
				TreasureSurfaceSeat.EstimatePushRadius( item, gemPushRadius ) );
		}

		if ( item.Definition.category == TreasureCategory.Coin )
			return TreasureSurfaceSeat.EstimatePushRadius( item, 0.08f ) * coinPushScale;

		return 0f;
	}

	/// <summary>
	/// Oriented box from the item's mesh filters at a placement pose, padded by leeway.
	/// </summary>
	public static void GetMeshOrientedBoxAtPose(
		TreasureItem item,
		Vector3 worldPos,
		Quaternion worldRot,
		float leeway,
		float minFootprintRadius,
		out Vector3 boxCenter,
		out Vector3 boxHalfExtents,
		out float footprintRadius )
	{
		leeway = Mathf.Max( 0f, leeway );
		minFootprintRadius = Mathf.Max( 0.01f, minFootprintRadius );

		Vector3 scale = item != null ? AbsVec( item.GetWorldScale() ) : Vector3.one;
		if ( !TryGetMeshLocalBounds( item, out Bounds local ) )
		{
			float half = minFootprintRadius + leeway;
			float height = item != null
				? Mathf.Max( 0.06f, TreasureStackSpacing.GetHalfStep( item ) ) + leeway
				: half;
			boxHalfExtents = new Vector3( half, height, half );
			boxCenter = worldPos + worldRot * ( Vector3.up * height );
			footprintRadius = half;
			return;
		}

		Vector3 localCenter = Vector3.Scale( local.center, scale );
		Vector3 extents = Vector3.Scale( local.extents, scale );
		extents.x = Mathf.Max( extents.x, minFootprintRadius );
		extents.z = Mathf.Max( extents.z, minFootprintRadius );
		extents += Vector3.one * leeway;

		boxHalfExtents = extents;
		boxCenter = worldPos + worldRot * localCenter;
		footprintRadius = Mathf.Max( extents.x, extents.z );
	}

	/// <summary>
	/// Encapsulates MeshFilter bounds in item-local space (skips binder cylinder visuals).
	/// </summary>
	public static bool TryGetMeshLocalBounds( TreasureItem item, out Bounds localBounds )
	{
		localBounds = default;
		if ( item == null )
			return false;

		Transform root = item.transform;
		MeshFilter[] filters = item.GetComponentsInChildren<MeshFilter>( true );
		bool has = false;
		Bounds bounds = new Bounds( Vector3.zero, Vector3.zero );

		for ( int i = 0; i < filters.Length; i++ )
		{
			MeshFilter filter = filters[ i ];
			if ( filter == null || filter.sharedMesh == null )
				continue;
			if ( CoinColumnCylinderBinder.IsBinderVisualRenderer( filter.transform, root ) )
				continue;

			Bounds meshBounds = filter.sharedMesh.bounds;
			Transform ft = filter.transform;
			Vector3[] corners =
			{
				meshBounds.min,
				new Vector3( meshBounds.min.x, meshBounds.min.y, meshBounds.max.z ),
				new Vector3( meshBounds.min.x, meshBounds.max.y, meshBounds.min.z ),
				new Vector3( meshBounds.min.x, meshBounds.max.y, meshBounds.max.z ),
				new Vector3( meshBounds.max.x, meshBounds.min.y, meshBounds.min.z ),
				new Vector3( meshBounds.max.x, meshBounds.min.y, meshBounds.max.z ),
				new Vector3( meshBounds.max.x, meshBounds.max.y, meshBounds.min.z ),
				meshBounds.max
			};

			for ( int c = 0; c < corners.Length; c++ )
			{
				Vector3 world = ft.TransformPoint( corners[ c ] );
				Vector3 local = root.InverseTransformPoint( world );
				if ( !has )
				{
					bounds = new Bounds( local, Vector3.zero );
					has = true;
				}
				else
					bounds.Encapsulate( local );
			}
		}

		if ( !has )
			return false;

		localBounds = bounds;
		return true;
	}

	static Vector3 AbsVec( Vector3 v )
	{
		return new Vector3( Mathf.Abs( v.x ), Mathf.Abs( v.y ), Mathf.Abs( v.z ) );
	}

	static bool IsOwnOrPlayerCollider( TreasureItem placing, Collider col )
	{
		if ( placing != null && col.GetComponentInParent<TreasureItem>() == placing )
			return true;

		if ( col.GetComponentInParent<PlayerController>() != null )
			return true;

		return false;
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

	/// <summary>
	/// Push a coin being placed away from loose gems and gem pyramids (mirror of gem vs coin stacks).
	/// Nearby coin stacks still win via join snap in <see cref="PlayerPlacement"/>.
	/// </summary>
	public static Vector3 ResolveCoinPlaceSeparation( TreasureItem placing, Vector3 dropPos )
	{
		if ( placing == null || placing.Definition == null )
			return dropPos;
		if ( placing.Definition.category != TreasureCategory.Coin )
			return dropPos;

		Vector3 pos = dropPos;
		for ( int iter = 0; iter < 4; iter++ )
		{
			Vector3 next = ResolveCoinPlaceSeparationOnce( placing, pos );
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

	static Vector3 ResolveCoinPlaceSeparationOnce( TreasureItem placing, Vector3 dropPos )
	{
		TreasureSurfaceDefinition surfaceDef = null;
		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		if ( world != null )
			surfaceDef = world.Definition;

		float gemPushRadius = surfaceDef != null ? surfaceDef.gemPushRadius : 0.18f;
		float coinPushScale = surfaceDef != null ? surfaceDef.gemVsCoinPushRadiusScale : 1f;
		float selfRadius = TreasureSurfaceSeat.EstimatePushRadius( placing, 0.08f ) * coinPushScale;
		float queryRadius = Mathf.Max( 0.45f, selfRadius + gemPushRadius * 4f );

		int hits = Physics.OverlapSphereNonAlloc(
			dropPos,
			queryRadius,
			CoinPlaceOverlap,
			Physics.DefaultRaycastLayers,
			QueryTriggerInteraction.Ignore );

		Vector3 push = Vector3.zero;
		for ( int i = 0; i < hits; i++ )
		{
			Collider col = CoinPlaceOverlap[ i ];
			if ( col == null )
				continue;

			TreasureItem other = col.GetComponentInParent<TreasureItem>();
			if ( other == null || other == placing || !other.IsWorldLoose || other.IsInFlight )
				continue;
			if ( other.Definition == null || other.Definition.category != TreasureCategory.Gem )
				continue;
			// Pyramid members are handled via ActiveClusters below (avoids double-push).
			if ( GemPyramidRegistry.FindClusterContaining( other ) != null )
				continue;

			float otherRadius = Mathf.Max(
				gemPushRadius,
				TreasureSurfaceSeat.EstimatePushRadius( other, gemPushRadius ) );
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

		IReadOnlyList<GemPyramidCluster> clusters = GemPyramidRegistry.ActiveClusters;
		for ( int c = 0; c < clusters.Count; c++ )
		{
			GemPyramidCluster cluster = clusters[ c ];
			if ( cluster == null || cluster.Members == null )
				continue;

			for ( int m = 0; m < cluster.Members.Count; m++ )
			{
				TreasureItem other = cluster.Members[ m ];
				if ( other == null || other == placing )
					continue;

				float otherRadius = Mathf.Max(
					gemPushRadius,
					TreasureSurfaceSeat.EstimatePushRadius( other, gemPushRadius ) );
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

		if ( push.sqrMagnitude < 0.0000001f )
			return dropPos;

		Vector3 separated = dropPos;
		separated.x += push.x;
		separated.z += push.z;

		Quaternion placeRot = GetPlaceRotation( placing, Vector3.up );
		if ( world != null && world.Sampler != null
			&& world.Sampler.TrySample( separated, out TreasureSurfaceSample sample )
			&& sample.Traversable )
		{
			separated.y = TreasureSurfaceSeat.GetContactY( placing, sample, placeRot );
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
		dropRot = GetPlaceRotation( item, in query );
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
