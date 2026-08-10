using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Deterministic volume sampling and outside-fraction tests for pile gems/artifacts,
/// plus solid-surface sampling for coin visual reseats.
/// </summary>
public static class GoldPileTreasurePlacement
{
	const int SurfaceAttempts = 48;
	const int VolumeAttempts = 64;
	const int AabbSampleGrid = 4;
	const int CoinFootprintSamples = 8;
	const float ArtifactMaxGroundEmbedFraction = 0.25f;
	const float CoinVisualSinkFraction = 0.08f;

	struct VolumeCandidate
	{
		public VolumePose Pose;
		public Bounds Bounds;
		public float OverlapScore;
		public bool SpacingOk;
	}

	public struct VolumePose
	{
		public Vector3 LocalPos;
		public Quaternion LocalRot;
		public float Scale;
		public float ProbeRadius;
	}

	public static uint HashUInt( int seed, int index )
	{
		unchecked
		{
			uint h = ( uint )seed;
			h ^= ( uint )index * 747796405u;
			h = ( h ^ ( h >> 16 ) ) * 2246822519u;
			h = ( h ^ ( h >> 13 ) ) * 3266489917u;
			h ^= h >> 16;
			return h == 0u ? 1u : h;
		}
	}

	public static float Hash01( int seed, int index )
	{
		return ( HashUInt( seed, index ) & 0x00FFFFFFu ) / 16777215f;
	}

	public static float HashRange( int seed, int index, float min, float max )
	{
		return Mathf.Lerp( min, max, Hash01( seed, index ) );
	}

	public static Quaternion HashRotation( int seed, int index )
	{
		float yaw = HashRange( seed, index * 3 + 1, 0f, 360f );
		float pitch = HashRange( seed, index * 3 + 2, -25f, 25f );
		float roll = HashRange( seed, index * 3 + 3, -25f, 25f );
		return Quaternion.Euler( pitch, yaw, roll );
	}

	/// <summary>Gems and coins must stay above ground; other large props may embed up to 25% of bounds height.</summary>
	public static float GetMaxGroundEmbedFraction( TreasureCategory category )
	{
		if ( category == TreasureCategory.Gem || category == TreasureCategory.Coin )
			return 0f;

		return ArtifactMaxGroundEmbedFraction;
	}

	public static float MinAllowedBottomY( float groundLevel, Bounds localBounds, float maxGroundEmbedFraction )
	{
		if ( maxGroundEmbedFraction <= 1e-5f )
			return groundLevel;

		return groundLevel - localBounds.size.y * Mathf.Clamp01( maxGroundEmbedFraction );
	}

	public static bool ViolatesGroundEmbed(
		Bounds localBounds,
		float groundLevel,
		float maxGroundEmbedFraction )
	{
		return localBounds.min.y < MinAllowedBottomY( groundLevel, localBounds, maxGroundEmbedFraction ) - 0.01f;
	}

	/// <summary>
	/// Map u∈[0,1] through radial power so 0.5≈base-heavy, 1≈even height, &gt;1≈tip-heavy.
	/// </summary>
	public static float ApplyRadialPower( float u01, float radialPower )
	{
		float power = Mathf.Clamp( radialPower, 0.25f, 3f );
		float exponent = 1f / power;
		return Mathf.Pow( Mathf.Clamp01( u01 ), exponent );
	}

	/// <summary>
	/// Sample a deterministic pose inside the solid mound volume for gems/artifacts.
	/// Only places where the pile exists above ground; clamps above the floor; optionally
	/// rejects poses that collide with <paramref name="occupiedLocal"/>.
	/// Attempt salts are fixed per (unitIndex, attempt) so the same inputs always yield the same pose.
	/// </summary>
	public static bool TrySampleVolumePose(
		GoldPileHeightfield heightfield,
		int pileSeed,
		int unitIndex,
		float placementRadiusFraction,
		float scale,
		float probeRadius,
		float treasureRadialPower,
		float treasureHeightBias,
		out VolumePose pose )
	{
		return TrySampleVolumePose(
			heightfield,
			pileSeed,
			unitIndex,
			placementRadiusFraction,
			scale,
			probeRadius,
			treasureRadialPower,
			treasureHeightBias,
			occupiedLocal: null,
			minSpacing: 0f,
			out pose );
	}

	public static bool TrySampleVolumePose(
		GoldPileHeightfield heightfield,
		int pileSeed,
		int unitIndex,
		float placementRadiusFraction,
		float scale,
		float probeRadius,
		float treasureRadialPower,
		float treasureHeightBias,
		List<Vector3> occupiedLocal,
		float minSpacing,
		out VolumePose pose )
	{
		pose = default;
		if ( heightfield == null || scale < 0.01f )
			return false;

		float half = heightfield.WorldSize * 0.5f * Mathf.Clamp( placementRadiusFraction, 0.2f, 1f );
		float ground = heightfield.GroundLevel;
		float maxH = heightfield.MaxHeight;
		float radialPower = Mathf.Clamp( treasureRadialPower, 0.25f, 3f );
		float heightBias = Mathf.Clamp( treasureHeightBias, 0f, 3f );
		float probe = Mathf.Max( 0.02f, probeRadius );
		float spacing = Mathf.Max( 0f, minSpacing );
		float spacingSq = spacing * spacing;
		bool checkSpacing = occupiedLocal != null && spacingSq > 1e-8f;

		for ( int attempt = 0; attempt < VolumeAttempts; attempt++ )
		{
			int salt = unitIndex * 64 + attempt;
			float heightU = Hash01( pileSeed, salt * 4 );
			float angleU = Hash01( pileSeed, salt * 4 + 1 );
			float radialU = Hash01( pileSeed, salt * 4 + 2 );
			float columnU = Hash01( pileSeed, salt * 4 + 3 );

			float heightT = ApplyRadialPower( heightU, radialPower );
			if ( heightBias > 0f )
			{
				float tipT = 1f - Mathf.Pow( 1f - heightT, 1f + heightBias );
				heightT = Mathf.Lerp( heightT, tipT, Mathf.Clamp01( heightBias * 0.35f ) );
			}

			// Area-uniform XZ with mild tip pull via radial power.
			float rNorm = Mathf.Sqrt( radialU );
			if ( heightBias > 0f )
			{
				float tipPull = 1f - ApplyRadialPower( radialU, radialPower );
				rNorm = Mathf.Lerp( rNorm, tipPull, Mathf.Clamp01( heightBias * 0.2f ) );
			}
			rNorm = Mathf.Clamp01( rNorm );

			float angle = angleU * Mathf.PI * 2f;
			float cos = Mathf.Cos( angle );
			float sin = Mathf.Sin( angle );
			float radius = half * rNorm;
			float lx = cos * radius;
			float lz = sin * radius;

			float surface = heightfield.SampleNormalized( lx, lz ) * maxH;
			// Empty / below-ground cells are not pile — never seat treasure there, never collapse to origin.
			if ( surface <= ground + 0.02f || !heightfield.ExistsAtLocal( lx, lz ) )
				continue;

			float floorY = FloorClearanceY( ground, probe );
			float yMin = floorY;
			float yMax = Mathf.Max( yMin + 0.01f, surface - probe * 0.35f );
			if ( yMax <= yMin )
				continue;

			float yT = columnU;
			if ( heightBias > 0f )
				yT = 1f - Mathf.Pow( 1f - yT, 1f + heightBias );
			// Soft mix with height band so tip-biased units also sit higher in their column.
			yT = Mathf.Lerp( yT, Mathf.Max( yT, heightT ), 0.35f );
			float ly = Mathf.Lerp( yMin, yMax, Mathf.Clamp01( yT ) );
			ly = Mathf.Max( ly, floorY );

			Vector3 localPos = new Vector3( lx, ly, lz );
			if ( checkSpacing && IsTooClose( localPos, occupiedLocal, spacingSq ) )
				continue;

			pose.LocalPos = localPos;
			pose.LocalRot = HashRotation( pileSeed, salt );
			pose.Scale = scale;
			pose.ProbeRadius = probe;
			return true;
		}

		return false;
	}

	/// <summary>
	/// Local Y that keeps a probe center above the pile floor plane.
	/// </summary>
	public static float FloorClearanceY( float groundLevel, float probeRadius )
	{
		return groundLevel + Mathf.Max( 0.02f, probeRadius ) * 0.5f;
	}

	/// <summary>
	/// Embedded probe band at local XZ: floor clearance through just below the carved surface.
	/// </summary>
	public static void GetPileColumnBand(
		GoldPileHeightfield heightfield,
		float localX,
		float localZ,
		float probeRadius,
		out float floorY,
		out float ceilingY )
	{
		float ground = heightfield != null ? heightfield.GroundLevel : 0f;
		float probe = Mathf.Max( 0.02f, probeRadius );
		floorY = FloorClearanceY( ground, probe );
		ceilingY = floorY;

		if ( heightfield == null )
			return;

		float surface = heightfield.SampleNormalized( localX, localZ ) * heightfield.MaxHeight;
		if ( surface < ground )
			surface = ground;

		ceilingY = Mathf.Max( floorY, surface - probe * 0.35f );
	}

	/// <summary>
	/// Keep a probe center above the pile floor plane.
	/// Does not pull poses back under a carved surface — dug-up treasure must stay at its authored height.
	/// </summary>
	public static Vector3 ClampAboveFloor( GoldPileHeightfield heightfield, Vector3 localPos, float probeRadius )
	{
		if ( heightfield == null )
			return localPos;

		float probe = Mathf.Max( 0.02f, probeRadius );
		float floorY = FloorClearanceY( heightfield.GroundLevel, probe );
		if ( localPos.y < floorY )
			localPos.y = floorY;

		return localPos;
	}

	/// <summary>
	/// True when this column cannot hold the AABB without hovering above the mound
	/// (empty skirt, degenerate band, or floor-clear would float the mesh).
	/// </summary>
	public static bool IsInvalidFloorSeat(
		GoldPileHeightfield heightfield,
		Vector3 localPos,
		Bounds localBounds,
		float probeRadius )
	{
		return IsInvalidFloorSeat(
			heightfield,
			localPos,
			localBounds,
			probeRadius,
			ArtifactMaxGroundEmbedFraction );
	}

	public static bool IsInvalidFloorSeat(
		GoldPileHeightfield heightfield,
		Vector3 localPos,
		Bounds localBounds,
		float probeRadius,
		float maxGroundEmbedFraction )
	{
		if ( heightfield == null )
			return true;

		float probe = Mathf.Max( 0.02f, probeRadius );
		if ( !heightfield.ExistsAtLocal( localPos.x, localPos.z ) )
			return true;

		GetPileColumnBand(
			heightfield,
			localPos.x,
			localPos.z,
			probe,
			out float floorY,
			out float ceilingY );

		if ( ceilingY <= floorY + 0.01f )
			return true;

		float ground = heightfield.GroundLevel;
		float minBottom = MinAllowedBottomY( ground, localBounds, maxGroundEmbedFraction );
		if ( localBounds.min.y >= minBottom - 0.01f )
			return false;

		float bottomOffset = localPos.y - localBounds.min.y;
		if ( bottomOffset < 0f )
			bottomOffset = localBounds.extents.y;

		float targetBottom = minBottom;
		float floorClearPivotY = targetBottom + bottomOffset;
		if ( floorClearPivotY > ceilingY + 0.02f )
			return true;

		float clampedBottom = ceilingY - bottomOffset;
		if ( clampedBottom < minBottom - 0.01f )
			return true;

		return false;
	}

	/// <summary>
	/// Deterministic exhaustive volume placement with staged overlap/spacing relaxation.
	/// </summary>
	public static bool TrySampleValidVolumePose(
		GoldPileHeightfield heightfield,
		int pileSeed,
		int unitIndex,
		float placementRadiusFraction,
		float scale,
		float probeRadius,
		float treasureRadialPower,
		float treasureHeightBias,
		List<Vector3> occupiedLocal,
		float minSpacing,
		out VolumePose pose,
		out Bounds localBounds )
	{
		List<Bounds> occupiedBounds = null;
		if ( occupiedLocal != null && occupiedLocal.Count > 0 )
		{
			occupiedBounds = new List<Bounds>( occupiedLocal.Count );
			for ( int i = 0; i < occupiedLocal.Count; i++ )
			{
				Vector3 p = occupiedLocal[ i ];
				occupiedBounds.Add( new Bounds( p, Vector3.one * minSpacing ) );
			}
		}

		return TrySampleValidVolumePose(
			heightfield,
			pileSeed,
			unitIndex,
			placementRadiusFraction,
			scale,
			probeRadius,
			treasureRadialPower,
			treasureHeightBias,
			TreasureCategory.Artifact,
			occupiedBounds,
			minSpacing,
			null,
			out pose,
			out localBounds );
	}

	public static bool TrySampleValidVolumePose(
		GoldPileHeightfield heightfield,
		int pileSeed,
		int unitIndex,
		float placementRadiusFraction,
		float scale,
		float probeRadius,
		float treasureRadialPower,
		float treasureHeightBias,
		TreasureCategory category,
		List<Bounds> occupiedBounds,
		float minSpacing,
		Mesh mesh,
		out VolumePose pose,
		out Bounds localBounds )
	{
		pose = default;
		localBounds = default;
		if ( heightfield == null || scale < 0.01f )
			return false;

		float probe = Mathf.Max( 0.02f, probeRadius );
		float maxEmbed = GetMaxGroundEmbedFraction( category );
		float spacing = Mathf.Max( 0f, minSpacing );
		float spacingSq = spacing * spacing;

		VolumeCandidate bestStrict = default;
		VolumeCandidate bestNoOverlap = default;
		VolumeCandidate bestAny = default;
		bool hasStrict = false;
		bool hasNoOverlap = false;
		bool hasAny = false;

		for ( int round = 0; round < VolumeAttempts; round++ )
		{
			int saltUnit = unitIndex + round * 7919;
			if ( !TrySampleVolumePose(
				heightfield,
				pileSeed,
				saltUnit,
				placementRadiusFraction,
				scale,
				probe,
				treasureRadialPower,
				treasureHeightBias,
				occupiedLocal: null,
				minSpacing: 0f,
				out VolumePose candidatePose ) )
			{
				continue;
			}

			Bounds candidateBounds = LocalAabbFromPose(
				candidatePose.LocalPos,
				candidatePose.LocalRot,
				candidatePose.Scale,
				mesh );
			candidateBounds.Expand( probe * 0.35f );

			if ( IsInvalidFloorSeat(
				heightfield,
				candidatePose.LocalPos,
				candidateBounds,
				probe,
				maxEmbed ) )
			{
				continue;
			}

			LiftBoundsIntoPileColumn(
				heightfield,
				ref candidatePose.LocalPos,
				ref candidateBounds,
				probe,
				maxEmbed );
			if ( IsInvalidFloorSeat(
				heightfield,
				candidatePose.LocalPos,
				candidateBounds,
				probe,
				maxEmbed ) )
			{
				continue;
			}

			ClampMaxProtrusion( heightfield, ref candidatePose.LocalPos, ref candidateBounds );

			float overlap = ComputeBoundsOverlapScore( candidateBounds, occupiedBounds );
			bool spacingOk = !IsTooClose( candidatePose.LocalPos, spacingSq, occupiedBounds );

			VolumeCandidate candidate = new VolumeCandidate
			{
				Pose = candidatePose,
				Bounds = candidateBounds,
				OverlapScore = overlap,
				SpacingOk = spacingOk
			};

			if ( spacingOk && overlap <= 1e-6f )
			{
				pose = candidatePose;
				localBounds = candidateBounds;
				return true;
			}

			if ( spacingOk && ( !hasStrict || overlap < bestStrict.OverlapScore ) )
			{
				bestStrict = candidate;
				hasStrict = true;
			}

			if ( overlap <= 1e-6f && ( !hasNoOverlap || ( spacingOk && !bestNoOverlap.SpacingOk ) ) )
			{
				bestNoOverlap = candidate;
				hasNoOverlap = true;
			}

			if ( !hasAny || overlap < bestAny.OverlapScore )
			{
				bestAny = candidate;
				hasAny = true;
			}
		}

		VolumeCandidate chosen = default;
		if ( hasStrict )
			chosen = bestStrict;
		else if ( hasNoOverlap )
			chosen = bestNoOverlap;
		else if ( hasAny )
			chosen = bestAny;
		else if ( TryFallbackVolumePose(
			heightfield,
			pileSeed,
			unitIndex,
			placementRadiusFraction,
			scale,
			probe,
			category,
			mesh,
			maxEmbed,
			out pose,
			out localBounds ) )
		{
			return true;
		}
		else
		{
			return false;
		}

		pose = chosen.Pose;
		localBounds = chosen.Bounds;
		return true;
	}

	static bool TryFallbackVolumePose(
		GoldPileHeightfield heightfield,
		int pileSeed,
		int unitIndex,
		float placementRadiusFraction,
		float scale,
		float probeRadius,
		TreasureCategory category,
		Mesh mesh,
		float maxGroundEmbedFraction,
		out VolumePose pose,
		out Bounds localBounds )
	{
		pose = default;
		localBounds = default;
		if ( heightfield == null )
			return false;

		float half = heightfield.WorldSize * 0.5f * Mathf.Clamp( placementRadiusFraction, 0.2f, 1f );
		float ground = heightfield.GroundLevel;
		float maxH = heightfield.MaxHeight;
		int res = Mathf.Max( 4, heightfield.Resolution );
		float step = heightfield.WorldSize / ( res - 1 );

		float bestScore = float.MaxValue;
		bool found = false;

		for ( int z = 0; z < res; z++ )
		{
			for ( int x = 0; x < res; x++ )
			{
				float lx = -half + x * step;
				float lz = -half + z * step;
				if ( lx * lx + lz * lz > half * half )
					continue;
				if ( !heightfield.ExistsAtLocal( lx, lz ) )
					continue;

				float surface = heightfield.SampleNormalized( lx, lz ) * maxH;
				if ( surface <= ground + 0.02f )
					continue;

				int salt = unitIndex * 131 + x * 17 + z * 43;
				float columnU = Hash01( pileSeed, salt );
				float floorY = FloorClearanceY( ground, probeRadius );
				float yMax = Mathf.Max( floorY + 0.01f, surface - probeRadius * 0.35f );
				float ly = Mathf.Lerp( floorY, yMax, columnU );

				VolumePose candidate = new VolumePose
				{
					LocalPos = new Vector3( lx, ly, lz ),
					LocalRot = HashRotation( pileSeed, salt ),
					Scale = scale,
					ProbeRadius = probeRadius
				};
				Bounds bounds = LocalAabbFromPose( candidate.LocalPos, candidate.LocalRot, scale, mesh );
				bounds.Expand( probeRadius * 0.35f );
				if ( IsInvalidFloorSeat(
					heightfield,
					candidate.LocalPos,
					bounds,
					probeRadius,
					maxGroundEmbedFraction ) )
				{
					continue;
				}

				LiftBoundsIntoPileColumn(
					heightfield,
					ref candidate.LocalPos,
					ref bounds,
					probeRadius,
					maxGroundEmbedFraction );
				ClampMaxProtrusion( heightfield, ref candidate.LocalPos, ref bounds );

				float score = Hash01( pileSeed, salt + 7 ) + surface / Mathf.Max( 0.01f, maxH );
				if ( score >= bestScore )
					continue;

				bestScore = score;
				pose = candidate;
				localBounds = bounds;
				found = true;
			}
		}

		return found;
	}

	static float ComputeBoundsOverlapScore( Bounds candidate, List<Bounds> occupied )
	{
		if ( occupied == null || occupied.Count == 0 )
			return 0f;

		float total = 0f;
		for ( int i = 0; i < occupied.Count; i++ )
		{
			Bounds other = occupied[ i ];
			if ( !candidate.Intersects( other ) )
				continue;

			Vector3 min = Vector3.Max( candidate.min, other.min );
			Vector3 max = Vector3.Min( candidate.max, other.max );
			Vector3 size = max - min;
			if ( size.x <= 0f || size.y <= 0f || size.z <= 0f )
				continue;

			total += size.x * size.y * size.z;
		}

		return total;
	}

	static bool IsTooClose( Vector3 localPos, float spacingSq, List<Bounds> occupiedBounds )
	{
		if ( occupiedBounds == null || spacingSq <= 1e-8f )
			return false;

		for ( int i = 0; i < occupiedBounds.Count; i++ )
		{
			Vector3 d = occupiedBounds[ i ].ClosestPoint( localPos ) - localPos;
			if ( d.sqrMagnitude < spacingSq )
				return true;
		}

		return false;
	}

	public static void LiftBoundsIntoPileColumn(
		GoldPileHeightfield heightfield,
		ref Vector3 localPos,
		ref Bounds localBounds,
		float probeRadius )
	{
		LiftBoundsIntoPileColumn(
			heightfield,
			ref localPos,
			ref localBounds,
			probeRadius,
			ArtifactMaxGroundEmbedFraction );
	}

	/// <summary>
	/// Lift a latent pose so its local AABB clears the allowed floor embed while staying embedded in the pile column.
	/// </summary>
	public static void LiftBoundsIntoPileColumn(
		GoldPileHeightfield heightfield,
		ref Vector3 localPos,
		ref Bounds localBounds,
		float probeRadius,
		float maxGroundEmbedFraction )
	{
		if ( heightfield == null )
			return;

		float ground = heightfield.GroundLevel;
		float minBottom = MinAllowedBottomY( ground, localBounds, maxGroundEmbedFraction );
		if ( localBounds.min.y >= minBottom - 0.01f )
			return;

		GetPileColumnBand(
			heightfield,
			localPos.x,
			localPos.z,
			probeRadius,
			out float floorY,
			out float ceilingY );

		float bottomOffset = localPos.y - localBounds.min.y;
		float targetPivotY = minBottom + bottomOffset;

		if ( targetPivotY > ceilingY )
		{
			targetPivotY = ceilingY;
			float targetBottom = targetPivotY - bottomOffset;
			if ( targetBottom < minBottom )
			{
				targetBottom = minBottom;
				targetPivotY = targetBottom + bottomOffset;
			}
		}
		else if ( targetPivotY < floorY )
		{
			targetPivotY = Mathf.Min( floorY, ceilingY );
		}

		float dy = targetPivotY - localPos.y;
		if ( dy <= 1e-5f )
			return;

		localPos.y += dy;
		localBounds.center += new Vector3( 0f, dy, 0f );
	}

	/// <summary>
	/// If a prop is protruding past the min-embed limit, move it in XYZ toward an embedded surface seat.
	/// Already-embedded props are left alone. Rotation unchanged; pivot never goes below the floor.
	/// Returns true when the pose was changed.
	/// </summary>
	public static bool StickTowardSurface(
		GoldPileHeightfield heightfield,
		ref Vector3 localPos,
		ref Bounds localBounds,
		float bottomOffsetFromPivot,
		float minEmbedFraction = 0.2f )
	{
		if ( heightfield == null )
			return false;

		float bottomOffset = Mathf.Max( 0.02f, bottomOffsetFromPivot );
		float height = Mathf.Max( 0.04f, localBounds.size.y );
		float minEmbed = Mathf.Max( 0.03f, height * Mathf.Clamp( minEmbedFraction, 0.05f, 0.6f ) );
		float floorY = FloorClearanceY( heightfield.GroundLevel, Mathf.Max( 0.02f, bottomOffset ) );

		float lx = localPos.x;
		float lz = localPos.z;
		float surface = heightfield.SampleNormalized( lx, lz ) * heightfield.MaxHeight;
		bool columnMissing = !heightfield.ExistsAtLocal( lx, lz ) || surface < heightfield.GroundLevel;
		if ( columnMissing )
		{
			if ( !TryFindNearestSolidColumn( heightfield, lx, lz, out lx, out lz, out surface ) )
			{
				if ( localPos.y < floorY )
				{
					float fix = floorY - localPos.y;
					localPos.y = floorY;
					localBounds.center += new Vector3( 0f, fix, 0f );
					return true;
				}

				return false;
			}
		}

		float maxBottom = surface - minEmbed;
		// Already inside the mound enough — do not chase the surface.
		if ( !columnMissing && localBounds.min.y <= maxBottom )
			return false;

		Vector3 normal = SampleLocalNormal( heightfield, lx, lz );
		Vector3 surfacePoint = new Vector3( lx, surface, lz );
		Vector3 target = surfacePoint - normal * minEmbed + Vector3.up * bottomOffset;
		target.y = Mathf.Max( target.y, floorY );

		float half = heightfield.WorldSize * 0.5f;
		target.x = Mathf.Clamp( target.x, -half, half );
		target.z = Mathf.Clamp( target.z, -half, half );

		Vector3 delta = target - localPos;
		if ( delta.sqrMagnitude < 1e-10f )
			return false;

		localPos = target;
		localBounds.center += delta;
		if ( localPos.y < floorY )
		{
			float fix = floorY - localPos.y;
			localPos.y = floorY;
			localBounds.center += new Vector3( 0f, fix, 0f );
		}

		return true;
	}

	/// <summary>
	/// Heightfield surface normal in pile-local space (points roughly outward / up).
	/// </summary>
	public static Vector3 SampleLocalNormal( GoldPileHeightfield heightfield, float localX, float localZ )
	{
		if ( heightfield == null )
			return Vector3.up;

		float step = heightfield.WorldSize / Mathf.Max( 1, heightfield.Resolution - 1 );
		float maxH = heightfield.MaxHeight;
		float hL = heightfield.SampleNormalized( localX - step, localZ ) * maxH;
		float hR = heightfield.SampleNormalized( localX + step, localZ ) * maxH;
		float hD = heightfield.SampleNormalized( localX, localZ - step ) * maxH;
		float hU = heightfield.SampleNormalized( localX, localZ + step ) * maxH;
		Vector3 normal = new Vector3( hL - hR, step * 2f, hD - hU );
		if ( normal.sqrMagnitude < 1e-8f )
			return Vector3.up;
		return normal.normalized;
	}

	static bool TryFindNearestSolidColumn(
		GoldPileHeightfield heightfield,
		float localX,
		float localZ,
		out float solidX,
		out float solidZ,
		out float surface )
	{
		solidX = localX;
		solidZ = localZ;
		surface = heightfield.GroundLevel;

		float half = heightfield.WorldSize * 0.5f;
		float step = heightfield.WorldSize / Mathf.Max( 1, heightfield.Resolution - 1 );
		float bestDistSq = float.MaxValue;
		bool found = false;

		// Spiral search toward solid mound (prefer inward / nearby cells).
		for ( int ring = 1; ring <= 12; ring++ )
		{
			float radius = step * ring;
			int samples = Mathf.Max( 8, ring * 6 );
			for ( int s = 0; s < samples; s++ )
			{
				float angle = ( s / ( float )samples ) * Mathf.PI * 2f;
				float x = localX + Mathf.Cos( angle ) * radius;
				float z = localZ + Mathf.Sin( angle ) * radius;
				x = Mathf.Clamp( x, -half, half );
				z = Mathf.Clamp( z, -half, half );
				if ( !heightfield.ExistsAtLocal( x, z ) )
					continue;

				float h = heightfield.SampleNormalized( x, z ) * heightfield.MaxHeight;
				if ( h < heightfield.GroundLevel )
					continue;

				float dx = x - localX;
				float dz = z - localZ;
				float distSq = dx * dx + dz * dz;
				if ( distSq >= bestDistSq )
					continue;

				bestDistSq = distSq;
				solidX = x;
				solidZ = z;
				surface = h;
				found = true;
			}

			if ( found )
				return true;
		}

		return false;
	}

	/// <summary>
	/// Pull a pose down if its AABB sticks too far above the surface (keeps a minimum embed).
	/// Rotation is unchanged. Y only — XZ already handled by stick.
	/// </summary>
	public static void ClampMaxProtrusion(
		GoldPileHeightfield heightfield,
		ref Vector3 localPos,
		ref Bounds localBounds,
		float minEmbedFraction = 0.2f )
	{
		if ( heightfield == null )
			return;

		float surface = heightfield.SampleNormalized( localPos.x, localPos.z ) * heightfield.MaxHeight;
		if ( surface < heightfield.GroundLevel )
			surface = heightfield.GroundLevel;

		float height = Mathf.Max( 0.04f, localBounds.size.y );
		float minEmbed = Mathf.Max( 0.03f, height * Mathf.Clamp( minEmbedFraction, 0.05f, 0.6f ) );
		float maxBottom = surface - minEmbed;
		float bottom = localBounds.min.y;
		if ( bottom <= maxBottom )
			return;

		float dy = maxBottom - bottom;
		localPos.y += dy;
		localBounds.center += new Vector3( 0f, dy, 0f );

		float floorY = FloorClearanceY( heightfield.GroundLevel, 0.05f );
		if ( localPos.y < floorY )
		{
			float fix = floorY - localPos.y;
			localPos.y = floorY;
			localBounds.center += new Vector3( 0f, fix, 0f );
		}
	}

	public static bool IsTooClose( Vector3 localPos, List<Vector3> occupiedLocal, float spacingSq )
	{
		if ( occupiedLocal == null || spacingSq <= 1e-8f )
			return false;

		for ( int i = 0; i < occupiedLocal.Count; i++ )
		{
			Vector3 d = occupiedLocal[ i ] - localPos;
			if ( d.sqrMagnitude < spacingSq )
				return true;
		}

		return false;
	}

	/// <summary>
	/// Fraction of a sphere above the heightfield surface at the sphere's XZ (0 = fully buried).
	/// Below-ground pile cells have no material — exposure is purely geometric vs the surface height
	/// (carving to ground reveals buried treasure; empty skirt does not invent an outside surface).
	/// </summary>
	public static float OutsideFractionSphere(
		GoldPileHeightfield heightfield,
		Vector3 localCenter,
		float radius )
	{
		if ( heightfield == null || radius <= 1e-5f )
			return 0f;

		// One bilinear sample — ExistsAtLocal would sample the same XZ again.
		float surface = heightfield.SampleNormalized( localCenter.x, localCenter.z ) * heightfield.MaxHeight;
		// Below-ground / empty cells: treat surface as the floor plane so nothing "requires" dig there
		// unless the probe actually sits above that plane.
		if ( surface < heightfield.GroundLevel )
			surface = heightfield.GroundLevel;

		float top = localCenter.y + radius;
		float bottom = localCenter.y - radius;
		if ( top <= surface )
			return 0f;
		if ( bottom >= surface )
			return 1f;

		// Spherical cap / sphere = h²(3r−h)/(4r³); π cancels.
		float h = Mathf.Clamp( top - surface, 0f, 2f * radius );
		float t = h / radius;
		return Mathf.Clamp01( ( t * t * ( 3f - t ) ) * 0.25f );
	}

	/// <summary>
	/// Approximate AABB volume fraction above the heightfield surface via a coarse grid.
	/// Below-ground cells use the floor plane as the surface (no pile material to dig).
	/// </summary>
	public static float OutsideFractionAabb(
		GoldPileHeightfield heightfield,
		Bounds localBounds )
	{
		if ( heightfield == null )
			return 0f;

		Vector3 min = localBounds.min;
		Vector3 max = localBounds.max;
		if ( ( max - min ).sqrMagnitude < 1e-8f )
			return OutsideFractionSphere( heightfield, localBounds.center, 0.05f );

		float ground = heightfield.GroundLevel;
		int n = AabbSampleGrid;
		int outside = 0;
		int total = 0;
		for ( int iz = 0; iz < n; iz++ )
		{
			float tz = ( iz + 0.5f ) / n;
			float z = Mathf.Lerp( min.z, max.z, tz );
			for ( int ix = 0; ix < n; ix++ )
			{
				float tx = ( ix + 0.5f ) / n;
				float x = Mathf.Lerp( min.x, max.x, tx );
				float surface = heightfield.SampleNormalized( x, z ) * heightfield.MaxHeight;
				if ( surface < ground )
					surface = ground;
				for ( int iy = 0; iy < n; iy++ )
				{
					float ty = ( iy + 0.5f ) / n;
					float y = Mathf.Lerp( min.y, max.y, ty );
					total++;
					if ( y > surface )
						outside++;
				}
			}
		}

		return total <= 0 ? 0f : outside / ( float )total;
	}

	public static bool TouchesOutsideSphere(
		GoldPileHeightfield heightfield,
		Vector3 localCenter,
		float radius )
	{
		if ( heightfield == null || radius <= 1e-5f )
			return false;

		float surface = heightfield.SampleNormalized( localCenter.x, localCenter.z ) * heightfield.MaxHeight;
		if ( surface < heightfield.GroundLevel )
			surface = heightfield.GroundLevel;

		return localCenter.y + radius > surface;
	}

	/// <summary>
	/// Cheap reveal test: true when any top-face sample sits above the surface.
	/// Prefer this over <see cref="OutsideFractionAabb"/> when only outside&gt;0 matters.
	/// </summary>
	public static bool TouchesOutsideAabb(
		GoldPileHeightfield heightfield,
		Bounds localBounds )
	{
		if ( heightfield == null )
			return false;

		Vector3 min = localBounds.min;
		Vector3 max = localBounds.max;
		if ( ( max - min ).sqrMagnitude < 1e-8f )
			return TouchesOutsideSphere( heightfield, localBounds.center, 0.05f );

		float ground = heightfield.GroundLevel;
		float topY = max.y;
		// Top center + four top corners — enough to know if the AABB pokes out.
		if ( SamplePointAboveSurface( heightfield, localBounds.center.x, topY, localBounds.center.z, ground ) )
			return true;
		if ( SamplePointAboveSurface( heightfield, min.x, topY, min.z, ground ) )
			return true;
		if ( SamplePointAboveSurface( heightfield, max.x, topY, min.z, ground ) )
			return true;
		if ( SamplePointAboveSurface( heightfield, min.x, topY, max.z, ground ) )
			return true;
		if ( SamplePointAboveSurface( heightfield, max.x, topY, max.z, ground ) )
			return true;

		return false;
	}

	static bool SamplePointAboveSurface(
		GoldPileHeightfield heightfield,
		float localX,
		float localY,
		float localZ,
		float ground )
	{
		float surface = heightfield.SampleNormalized( localX, localZ ) * heightfield.MaxHeight;
		if ( surface < ground )
			surface = ground;
		return localY > surface;
	}

	/// <summary>
	/// Deterministic area-uniform footprint sample on the solid pile skirt.
	/// Out-of-footprint samples are rejected (never clamped onto the rim).
	/// </summary>
	public static bool TrySampleSolidCoinSurface(
		GoldPileHeightfield heightfield,
		int pileSeed,
		int slotIndex,
		Vector3 nearLocal,
		float searchRadius,
		float placementRadiusFraction,
		float coinSurfaceHeightFraction,
		float coinRadius,
		out Vector3 localPos,
		out float surfaceHeight )
	{
		localPos = nearLocal;
		surfaceHeight = 0f;
		if ( heightfield == null )
			return false;

		float half = heightfield.WorldSize * 0.5f * Mathf.Clamp( placementRadiusFraction, 0.2f, 1f );
		float usableRadius = Mathf.Max( 0.05f, half - Mathf.Max( 0f, coinRadius ) );
		float minSurface = Mathf.Max(
			heightfield.GroundLevel,
			heightfield.MaxHeight * Mathf.Clamp01( coinSurfaceHeightFraction ) );
		float search = Mathf.Max( 0.15f, searchRadius );
		float searchSq = search * search;
		bool preferNear = nearLocal.sqrMagnitude > 1e-6f || search < usableRadius * 0.95f;

		for ( int pass = 0; pass < 2; pass++ )
		{
			bool nearOnly = pass == 0 && preferNear;
			int attempts = nearOnly ? SurfaceAttempts : SurfaceAttempts * 2;
			for ( int attempt = 0; attempt < attempts; attempt++ )
			{
				int salt = slotIndex * 97 + attempt + ( nearOnly ? 0 : 10000 );
				if ( !TrySampleUniformCoinFootprint(
					heightfield,
					pileSeed,
					salt,
					usableRadius,
					minSurface,
					coinRadius,
					out Vector3 candidate,
					out float surface ) )
				{
					continue;
				}

				if ( nearOnly )
				{
					float dx = candidate.x - nearLocal.x;
					float dz = candidate.z - nearLocal.z;
					if ( dx * dx + dz * dz > searchSq )
						continue;
				}

				localPos = candidate;
				surfaceHeight = surface;
				return true;
			}

			if ( !preferNear )
				break;
		}

		return false;
	}

	static bool TrySampleUniformCoinFootprint(
		GoldPileHeightfield heightfield,
		int pileSeed,
		int salt,
		float usableRadius,
		float minSurface,
		float coinRadius,
		out Vector3 localPos,
		out float surfaceHeight )
	{
		localPos = Vector3.zero;
		surfaceHeight = 0f;

		float angle = Hash01( pileSeed, salt * 2 ) * Mathf.PI * 2f;
		float radial = usableRadius * Mathf.Sqrt( Hash01( pileSeed, salt * 2 + 1 ) );
		float lx = Mathf.Cos( angle ) * radial;
		float lz = Mathf.Sin( angle ) * radial;
		float surface = heightfield.SampleNormalized( lx, lz ) * heightfield.MaxHeight;
		if ( surface < minSurface || !heightfield.ExistsAtLocal( lx, lz ) )
			return false;

		localPos = new Vector3( lx, 0f, lz );
		surfaceHeight = surface;
		return IsCoinFootprintSupported(
			heightfield,
			localPos,
			coinRadius,
			minSurface,
			out surfaceHeight );
	}

	public static bool IsCoinFootprintSupported(
		GoldPileHeightfield heightfield,
		Vector3 localPos,
		float coinRadius,
		float minSurface,
		out float surfaceHeight )
	{
		surfaceHeight = 0f;
		if ( heightfield == null )
			return false;

		float radius = Mathf.Max( 0.01f, coinRadius );
		float centerSurface = heightfield.SampleNormalized( localPos.x, localPos.z ) * heightfield.MaxHeight;
		if ( centerSurface < minSurface || !heightfield.ExistsAtLocal( localPos.x, localPos.z ) )
			return false;

		surfaceHeight = centerSurface;
		for ( int i = 0; i < CoinFootprintSamples; i++ )
		{
			float angle = ( i / ( float )CoinFootprintSamples ) * Mathf.PI * 2f;
			float px = localPos.x + Mathf.Cos( angle ) * radius;
			float pz = localPos.z + Mathf.Sin( angle ) * radius;
			float sample = heightfield.SampleNormalized( px, pz ) * heightfield.MaxHeight;
			if ( sample < minSurface || !heightfield.ExistsAtLocal( px, pz ) )
				return false;

			surfaceHeight = Mathf.Max( surfaceHeight, sample );
		}

		return true;
	}

	/// <summary>
	/// True when the coin instance pivot is inside the mound volume
	/// (column exists and pivot Y is strictly below the heightfield surface).
	/// Inverse of the GPU seat release check.
	/// Playtest: after bind, coin pivots should sit under the visual mound (not past the skirt);
	/// digs should only release seats whose pivot leaves this volume.
	/// </summary>
	public static bool IsCoinPivotInside( GoldPileHeightfield heightfield, Vector3 localPos )
	{
		if ( heightfield == null )
			return false;
		if ( !heightfield.ExistsAtLocal( localPos.x, localPos.z ) )
			return false;

		float surface = heightfield.SampleNormalized( localPos.x, localPos.z ) * heightfield.MaxHeight;
		return localPos.y < surface;
	}

	/// <summary>
	/// Fast near-surface coin seat: XZ on the mound, Y a shallow embed under the current surface.
	/// Optional dig focus via <paramref name="preferNear"/> / <paramref name="searchRadius"/>.
	/// Pivot is required to stay inside the heightfield volume.
	/// </summary>
	public static bool TrySampleShallowCoinPose(
		GoldPileHeightfield heightfield,
		int pileSeed,
		int unitIndex,
		float placementRadiusFraction,
		float scale,
		float probeRadius,
		float buryBand,
		float minSurfaceFraction,
		Vector3 preferNear,
		float searchRadius,
		out VolumePose pose )
	{
		pose = default;
		if ( heightfield == null || scale < 0.01f )
			return false;

		float probe = Mathf.Max( 0.02f, probeRadius );
		float band = Mathf.Max( probe, buryBand );
		float half = heightfield.WorldSize * 0.5f * Mathf.Clamp( placementRadiusFraction, 0.2f, 1f );
		float minSurface = Mathf.Max(
			heightfield.GroundLevel,
			heightfield.MaxHeight * Mathf.Clamp01( minSurfaceFraction ) );
		float ground = heightfield.GroundLevel;
		bool focus = searchRadius > 0.05f;
		float focusRadius = Mathf.Max( 0.1f, searchRadius );

		const int attempts = 24;
		for ( int attempt = 0; attempt < attempts; attempt++ )
		{
			int salt = unitIndex * 48 + attempt;
			float lx;
			float lz;
			if ( focus )
			{
				float angle = Hash01( pileSeed, salt * 3 ) * Mathf.PI * 2f;
				float r = focusRadius * Mathf.Sqrt( Hash01( pileSeed, salt * 3 + 1 ) );
				lx = preferNear.x + Mathf.Cos( angle ) * r;
				lz = preferNear.z + Mathf.Sin( angle ) * r;
				if ( lx * lx + lz * lz > half * half )
					continue;
			}
			else
			{
				float angle = Hash01( pileSeed, salt * 3 ) * Mathf.PI * 2f;
				float rNorm = Mathf.Sqrt( Hash01( pileSeed, salt * 3 + 1 ) );
				float radius = half * rNorm;
				lx = Mathf.Cos( angle ) * radius;
				lz = Mathf.Sin( angle ) * radius;
			}

			if ( !heightfield.ExistsAtLocal( lx, lz ) )
				continue;

			float surface = heightfield.SampleNormalized( lx, lz ) * heightfield.MaxHeight;
			if ( surface < minSurface )
				continue;

			float embed = HashRange( pileSeed, salt * 3 + 2, probe * 0.35f, band );
			float ly = surface - embed;
			float floorY = FloorClearanceY( ground, probe );
			if ( ly < floorY )
				ly = floorY;

			Vector3 candidate = new Vector3( lx, ly, lz );
			if ( !IsCoinPivotInside( heightfield, candidate ) )
				continue;

			pose.LocalPos = candidate;
			pose.LocalRot = Quaternion.identity;
			pose.Scale = scale;
			pose.ProbeRadius = probe;
			return true;
		}

		return false;
	}

	/// <summary>
	/// Coin face aligned to the heightfield normal at XZ, with deterministic yaw and slight tip jitter.
	/// </summary>
	public static Quaternion CoinSurfaceTiltRotation(
		GoldPileHeightfield heightfield,
		int pileSeed,
		int slotIndex,
		float localX,
		float localZ )
	{
		Vector3 normal = heightfield != null
			? SampleLocalNormal( heightfield, localX, localZ )
			: Vector3.up;
		if ( normal.sqrMagnitude < 1e-8f )
			normal = Vector3.up;

		Quaternion tilt = Quaternion.FromToRotation( Vector3.up, normal.normalized );
		float yaw = HashRange( pileSeed, slotIndex * 3 + 11, 0f, 360f );
		float tipX = HashRange( pileSeed, slotIndex * 3 + 12, -6f, 6f );
		float tipZ = HashRange( pileSeed, slotIndex * 3 + 13, -6f, 6f );
		return tilt * Quaternion.Euler( tipX, yaw, tipZ );
	}

	/// <summary>
	/// Tilts and seats a coin so its pivot stays inside the mound volume.
	/// Returns false without mutating pose when the column/footprint cannot keep the pivot inside.
	/// </summary>
	public static bool ConformCoinToPileSurface(
		GoldPileHeightfield heightfield,
		int pileSeed,
		int slotIndex,
		ref Vector3 localPos,
		ref Quaternion localRot,
		float scale,
		Bounds meshBounds,
		float embedDepth,
		float minSurfaceFraction )
	{
		if ( heightfield == null )
			return false;
		if ( !heightfield.ExistsAtLocal( localPos.x, localPos.z ) )
			return false;

		float minSurface = Mathf.Max(
			heightfield.GroundLevel,
			heightfield.MaxHeight * Mathf.Clamp01( minSurfaceFraction ) );
		float surfaceHeight = heightfield.SampleNormalized( localPos.x, localPos.z ) * heightfield.MaxHeight;
		if ( surfaceHeight < minSurface )
			return false;

		float footprintRadius = Mathf.Max(
			meshBounds.extents.x,
			meshBounds.extents.z ) * Mathf.Max( 0.01f, scale );
		// Footprint is a helper against hanging mesh; pivot-inside is the required gate.
		if ( !IsCoinFootprintSupported(
			heightfield,
			localPos,
			footprintRadius,
			minSurface,
			out surfaceHeight ) )
		{
			return false;
		}

		Quaternion tilted = CoinSurfaceTiltRotation( heightfield, pileSeed, slotIndex, localPos.x, localPos.z );

		float visualSink = Mathf.Max( 0.004f, scale * CoinVisualSinkFraction );
		float pivotY = SolveCoinPivotYForContact(
			localPos,
			tilted,
			meshBounds,
			scale,
			heightfield,
			footprintRadius,
			minSurface,
			embedDepth + visualSink,
			surfaceHeight );

		// Contact solve can raise the pivot onto the surface; keep it strictly inside the volume.
		const float insideEps = 0.005f;
		float maxInsideY = surfaceHeight - insideEps;
		if ( pivotY > maxInsideY )
			pivotY = maxInsideY;

		Vector3 seated = new Vector3( localPos.x, pivotY, localPos.z );
		if ( !IsCoinPivotInside( heightfield, seated ) )
			return false;

		localRot = tilted;
		localPos = seated;
		return true;
	}

	static float SolveCoinPivotYForContact(
		Vector3 localPos,
		Quaternion localRot,
		Bounds meshBounds,
		float scale,
		GoldPileHeightfield heightfield,
		float footprintRadius,
		float minSurface,
		float embedDepth,
		float centerSurface )
	{
		Vector3 scaledCenter = meshBounds.center * scale;
		Vector3 scaledExtents = meshBounds.extents * scale;
		float bottomY = scaledCenter.y - scaledExtents.y;
		float requiredPivotY = float.MinValue;

		TryRaisePivotForBottomSupport(
			localPos,
			localRot,
			scaledCenter.x,
			bottomY,
			scaledCenter.z,
			heightfield,
			minSurface,
			embedDepth,
			ref requiredPivotY );

		for ( int sx = -1; sx <= 1; sx += 2 )
		{
			for ( int sz = -1; sz <= 1; sz += 2 )
			{
				TryRaisePivotForBottomSupport(
					localPos,
					localRot,
					scaledCenter.x + scaledExtents.x * sx,
					bottomY,
					scaledCenter.z + scaledExtents.z * sz,
					heightfield,
					minSurface,
					embedDepth,
					ref requiredPivotY );
			}
		}

		float ringRadius = Mathf.Max(
			Mathf.Max( scaledExtents.x, scaledExtents.z ),
			footprintRadius * 0.85f );
		for ( int i = 0; i < CoinFootprintSamples; i++ )
		{
			float angle = ( i / ( float )CoinFootprintSamples ) * Mathf.PI * 2f;
			TryRaisePivotForBottomSupport(
				localPos,
				localRot,
				scaledCenter.x + Mathf.Cos( angle ) * ringRadius,
				bottomY,
				scaledCenter.z + Mathf.Sin( angle ) * ringRadius,
				heightfield,
				minSurface,
				embedDepth,
				ref requiredPivotY );
		}

		if ( requiredPivotY <= float.MinValue + 1f )
		{
			float bottomOffset = GetLowestSupportYOffset( localRot, meshBounds, scale );
			return centerSurface - embedDepth - bottomOffset;
		}

		return requiredPivotY;
	}

	static void TryRaisePivotForBottomSupport(
		Vector3 localPos,
		Quaternion localRot,
		float supportX,
		float supportY,
		float supportZ,
		GoldPileHeightfield heightfield,
		float minSurface,
		float embedDepth,
		ref float requiredPivotY )
	{
		Vector3 supportLocal = new Vector3( supportX, supportY, supportZ );
		Vector3 supportPile = localPos + localRot * supportLocal;
		float sample = heightfield.SampleNormalized( supportPile.x, supportPile.z ) * heightfield.MaxHeight;
		if ( sample < minSurface || !heightfield.ExistsAtLocal( supportPile.x, supportPile.z ) )
			return;

		float contactY = sample - embedDepth;
		float pivotY = contactY - ( localRot * supportLocal ).y;
		if ( pivotY > requiredPivotY )
			requiredPivotY = pivotY;
	}

	static float GetLowestSupportYOffset( Quaternion localRot, Bounds meshBounds, float scale )
	{
		Vector3 scaledCenter = meshBounds.center * scale;
		Vector3 scaledExtents = meshBounds.extents * scale;
		float bottomY = scaledCenter.y - scaledExtents.y;
		float minY = float.MaxValue;

		TryCollectSupportYOffset( localRot, scaledCenter.x, bottomY, scaledCenter.z, ref minY );
		for ( int sx = -1; sx <= 1; sx += 2 )
		{
			for ( int sz = -1; sz <= 1; sz += 2 )
			{
				TryCollectSupportYOffset(
					localRot,
					scaledCenter.x + scaledExtents.x * sx,
					bottomY,
					scaledCenter.z + scaledExtents.z * sz,
					ref minY );
			}
		}

		return minY;
	}

	static void TryCollectSupportYOffset(
		Quaternion localRot,
		float supportX,
		float supportY,
		float supportZ,
		ref float minY )
	{
		Vector3 supportLocal = new Vector3( supportX, supportY, supportZ );
		minY = Mathf.Min( minY, ( localRot * supportLocal ).y );
	}

	public static float EstimateProbeRadius( TreasureDefinition definition, float scale, Mesh mesh )
	{
		float s = Mathf.Max( 0.01f, scale );
		if ( mesh != null && mesh.bounds.size.sqrMagnitude > 1e-6f )
		{
			Vector3 extents = mesh.bounds.extents;
			float r = Mathf.Max( extents.x, Mathf.Max( extents.y, extents.z ) ) * s;
			return Mathf.Max( 0.02f, r );
		}

		if ( definition != null )
		{
			Vector3 ws = definition.worldScale;
			float r = Mathf.Max( ws.x, Mathf.Max( ws.y, ws.z ) ) * 0.5f;
			return Mathf.Max( 0.02f, r * s / Mathf.Max( 0.01f, ws.x ) );
		}

		return Mathf.Max( 0.02f, s * 0.5f );
	}

	public static Bounds LocalAabbFromPose( Vector3 localPos, Quaternion localRot, float scale, Mesh mesh )
	{
		Bounds meshBounds = mesh != null ? mesh.bounds : new Bounds( Vector3.zero, Vector3.one * 0.2f );
		Vector3 extents = meshBounds.extents * Mathf.Max( 0.01f, scale );
		// Conservative axis-aligned box after rotation.
		Vector3 e = AbsRotateExtents( localRot, extents );
		return new Bounds( localPos + localRot * ( meshBounds.center * scale ), e * 2f );
	}

	static Vector3 AbsRotateExtents( Quaternion rot, Vector3 extents )
	{
		Matrix4x4 m = Matrix4x4.Rotate( rot );
		Vector3 x = m.GetColumn( 0 ) * extents.x;
		Vector3 y = m.GetColumn( 1 ) * extents.y;
		Vector3 z = m.GetColumn( 2 ) * extents.z;
		return new Vector3(
			Mathf.Abs( x.x ) + Mathf.Abs( y.x ) + Mathf.Abs( z.x ),
			Mathf.Abs( x.y ) + Mathf.Abs( y.y ) + Mathf.Abs( z.y ),
			Mathf.Abs( x.z ) + Mathf.Abs( y.z ) + Mathf.Abs( z.z ) );
	}
}
