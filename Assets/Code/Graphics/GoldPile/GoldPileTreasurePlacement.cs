using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Deterministic volume sampling and outside-fraction tests for pile gems/artifacts,
/// plus solid-surface sampling for coin visual reseats.
/// </summary>
public static class GoldPileTreasurePlacement
{
	const int SurfaceAttempts = 40;
	const int VolumeAttempts = 32;
	const int HeightSearchIterations = 10;
	const int AabbSampleGrid = 4;

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

			// Tip bias pulls toward center (small radius); base bias stays outward.
			float rNorm = 1f - ApplyRadialPower( radialU, radialPower );
			rNorm = Mathf.Clamp( rNorm, 0f, 1f );

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
			float yMax = Mathf.Max( yMin + 0.01f, surface - probe * 0.15f );
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

			if ( occupiedLocal != null )
				occupiedLocal.Add( localPos );
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
		if ( !heightfield.ExistsAtLocal( localX, localZ ) )
			surface = ground;

		ceilingY = Mathf.Max( floorY, surface - probe * 0.15f );
	}

	/// <summary>
	/// Keep a probe center inside the solid pile column — clears the floor without hovering above the skirt.
	/// </summary>
	public static Vector3 ClampAboveFloor( GoldPileHeightfield heightfield, Vector3 localPos, float probeRadius )
	{
		if ( heightfield == null )
			return localPos;

		GetPileColumnBand(
			heightfield,
			localPos.x,
			localPos.z,
			probeRadius,
			out float floorY,
			out float ceilingY );

		if ( localPos.y < floorY )
			localPos.y = Mathf.Min( floorY, ceilingY );
		else if ( localPos.y > ceilingY )
			localPos.y = ceilingY;

		return localPos;
	}

	/// <summary>
	/// Lift a latent pose so its local AABB clears the floor while staying embedded in the pile column.
	/// </summary>
	public static void LiftBoundsIntoPileColumn(
		GoldPileHeightfield heightfield,
		ref Vector3 localPos,
		ref Bounds localBounds,
		float probeRadius )
	{
		if ( heightfield == null )
			return;

		float ground = heightfield.GroundLevel;
		if ( localBounds.min.y >= ground )
			return;

		GetPileColumnBand(
			heightfield,
			localPos.x,
			localPos.z,
			probeRadius,
			out float floorY,
			out float ceilingY );

		float bottomOffset = localPos.y - localBounds.min.y;
		float targetPivotY = ground + bottomOffset;

		// Prefer clearing the floor; cap within the embedded column so props don't hover off the mound.
		if ( targetPivotY > ceilingY )
		{
			targetPivotY = ceilingY;
			float targetBottom = targetPivotY - bottomOffset;
			if ( targetBottom < ground )
			{
				targetBottom = ground;
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

		float surface = heightfield.SampleNormalized( localCenter.x, localCenter.z ) * heightfield.MaxHeight;
		// Below-ground / empty cells: treat surface as the floor plane so nothing "requires" dig there
		// unless the probe actually sits above that plane.
		if ( !heightfield.ExistsAtLocal( localCenter.x, localCenter.z ) )
			surface = heightfield.GroundLevel;

		float top = localCenter.y + radius;
		float bottom = localCenter.y - radius;
		if ( top <= surface )
			return 0f;
		if ( bottom >= surface )
			return 1f;

		// Spherical cap height above the plane.
		float h = top - surface;
		h = Mathf.Clamp( h, 0f, 2f * radius );
		float volCap = ( Mathf.PI * h * h * ( 3f * radius - h ) ) / 3f;
		float volSphere = ( 4f / 3f ) * Mathf.PI * radius * radius * radius;
		return Mathf.Clamp01( volCap / Mathf.Max( 1e-8f, volSphere ) );
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
				if ( !heightfield.ExistsAtLocal( x, z ) )
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
		return OutsideFractionSphere( heightfield, localCenter, radius ) > 0f;
	}

	public static bool TouchesOutsideAabb(
		GoldPileHeightfield heightfield,
		Bounds localBounds )
	{
		return OutsideFractionAabb( heightfield, localBounds ) > 0f;
	}

	/// <summary>
	/// Sample a solid coin surface point, preferring near <paramref name="nearLocal"/> when provided.
	/// Height-stratified (not area-uniform) so coins spread base→tip instead of bunching on the skirt.
	/// Out-of-footprint samples are rejected (never clamped onto the rim).
	/// </summary>
	public static bool TrySampleSolidCoinSurface(
		GoldPileHeightfield heightfield,
		Vector3 nearLocal,
		float searchRadius,
		float placementRadiusFraction,
		float coinSurfaceHeightFraction,
		float coinInsetRadius,
		float coinRadialPower,
		out Vector3 localPos,
		out float surfaceHeight )
	{
		localPos = nearLocal;
		surfaceHeight = 0f;
		if ( heightfield == null )
			return false;

		float half = heightfield.WorldSize * 0.5f * Mathf.Clamp( placementRadiusFraction, 0.2f, 1f );
		float usableRadius = Mathf.Max( 0.05f, half - Mathf.Max( 0f, coinInsetRadius ) );
		float minSurface = Mathf.Max(
			heightfield.GroundLevel,
			heightfield.MaxHeight * Mathf.Clamp01( coinSurfaceHeightFraction ) );
		float peak = heightfield.SampleNormalized( 0f, 0f ) * heightfield.MaxHeight;
		float maxSurface = Mathf.Max( minSurface + 0.01f, peak );
		float search = Mathf.Max( 0.15f, searchRadius );
		float searchSq = search * search;
		bool preferNear = nearLocal.sqrMagnitude > 1e-6f || search < usableRadius * 0.95f;

		// Prefer height-stratified samples near the dig first.
		if ( preferNear )
		{
			for ( int attempt = 0; attempt < SurfaceAttempts; attempt++ )
			{
				if ( !TrySampleHeightStratified(
					heightfield,
					usableRadius,
					minSurface,
					maxSurface,
					coinRadialPower,
					out Vector3 candidate,
					out float surface ) )
				{
					continue;
				}

				float dx = candidate.x - nearLocal.x;
				float dz = candidate.z - nearLocal.z;
				if ( dx * dx + dz * dz > searchSq )
					continue;

				localPos = candidate;
				surfaceHeight = surface;
				return true;
			}
		}

		// Broad height-stratified placement across the solid footprint.
		for ( int attempt = 0; attempt < SurfaceAttempts; attempt++ )
		{
			if ( !TrySampleHeightStratified(
				heightfield,
				usableRadius,
				minSurface,
				maxSurface,
				coinRadialPower,
				out Vector3 candidate,
				out float surface ) )
			{
				continue;
			}

			localPos = candidate;
			surfaceHeight = surface;
			return true;
		}

		return false;
	}

	static bool TrySampleHeightStratified(
		GoldPileHeightfield heightfield,
		float usableRadius,
		float minSurface,
		float maxSurface,
		float coinRadialPower,
		out Vector3 localPos,
		out float surfaceHeight )
	{
		localPos = Vector3.zero;
		surfaceHeight = 0f;

		float heightT = ApplyRadialPower( Random.value, coinRadialPower );
		float targetH = Mathf.Lerp( minSurface, maxSurface, heightT );
		float angle = Random.Range( 0f, Mathf.PI * 2f );
		float cos = Mathf.Cos( angle );
		float sin = Mathf.Sin( angle );

		if ( !TryFindRadiusForHeight(
			heightfield,
			cos,
			sin,
			usableRadius,
			targetH,
			minSurface,
			seed: 0,
			salt: 0,
			out float radius,
			out float surface ) )
			return false;

		// Small tangential jitter so contours don't look like perfect rings.
		float jitter = usableRadius * 0.02f * Random.Range( -1f, 1f );
		float lx = cos * radius - sin * jitter;
		float lz = sin * radius + cos * jitter;
		float distSq = lx * lx + lz * lz;
		if ( distSq > usableRadius * usableRadius )
			return false;

		surface = heightfield.SampleNormalized( lx, lz ) * heightfield.MaxHeight;
		if ( surface < minSurface )
			return false;

		localPos = new Vector3( lx, 0f, lz );
		surfaceHeight = surface;
		return true;
	}

	/// <summary>
	/// Binary-search radius along a ray where mound height ≈ target (mound is taller at center).
	/// When <paramref name="seed"/> is non-zero, tip jitter is deterministic.
	/// </summary>
	static bool TryFindRadiusForHeight(
		GoldPileHeightfield heightfield,
		float cos,
		float sin,
		float maxRadius,
		float targetH,
		float minSurface,
		int seed,
		int salt,
		out float radius,
		out float surface )
	{
		radius = 0f;
		surface = heightfield.SampleNormalized( 0f, 0f ) * heightfield.MaxHeight;
		if ( surface < minSurface )
			return false;

		// Target at/above peak → place near center.
		if ( targetH >= surface - 0.001f )
		{
			float tipJitter = seed != 0
				? Hash01( seed, salt )
				: 0f;
			radius = maxRadius * 0.08f * tipJitter;
			float lx = cos * radius;
			float lz = sin * radius;
			surface = heightfield.SampleNormalized( lx, lz ) * heightfield.MaxHeight;
			return surface >= minSurface;
		}

		float lo = 0f;
		float hi = maxRadius;
		float bestR = 0f;
		float bestH = surface;

		for ( int i = 0; i < HeightSearchIterations; i++ )
		{
			float mid = ( lo + hi ) * 0.5f;
			float h = heightfield.SampleNormalized( cos * mid, sin * mid ) * heightfield.MaxHeight;
			bestR = mid;
			bestH = h;
			// Taller toward center: if still above target, move outward.
			if ( h > targetH )
				lo = mid;
			else
				hi = mid;
		}

		if ( bestH < minSurface )
			return false;

		radius = bestR;
		surface = bestH;
		return true;
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
