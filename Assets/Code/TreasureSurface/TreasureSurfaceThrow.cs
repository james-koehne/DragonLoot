using System.Collections;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Predicts ballistic landings on the treasure surface and plays throw flight tweens.
/// Flight follows the throw velocity (aim + inherited player motion) under gravity,
/// and always runs long enough for the arc to reach the floor at any aim pitch.
/// </summary>
public static class TreasureSurfaceThrow
{
	const float BallisticDt = 1f / 60f;
	const int MaxBallisticSteps = 240;
	/// <summary>Shortest tween when prediction fails; real hits may be shorter.</summary>
	const float MinFlightTime = 0.08f;
	/// <summary>Matches MaxBallisticSteps so steep lobs are not cut mid-air.</summary>
	const float MaxFlightTime = MaxBallisticSteps * BallisticDt;
	const float HitEpsilonY = 0.04f;
	const float SettleStartU = 0.88f;

	public static bool TryPredictLanding(
		TreasureSurfaceWorld world,
		Vector3 start,
		Vector3 velocity,
		out Vector3 landPos,
		out Vector3 landVelocity )
	{
		return TryPredictLanding( world, start, velocity, out landPos, out landVelocity, out _ );
	}

	public static bool TryPredictLanding(
		TreasureSurfaceWorld world,
		Vector3 start,
		Vector3 velocity,
		out Vector3 landPos,
		out Vector3 landVelocity,
		out float flightTime )
	{
		landPos = start;
		landVelocity = FlattenHorizontal( velocity );
		flightTime = MinFlightTime;

		if ( world == null || !world.IsInitialized )
			return false;

		TreasureSurfaceDefinition def = world.Definition;
		float g = def.throwBallisticGravity;
		Vector3 p = start;
		Vector3 v = velocity;

		// Ensure the start chunk is loaded so early samples succeed.
		if ( world.TryGetChunkCoord( start, out TreasureChunkCoord startCoord ) )
			world.EnsureChunkLoaded( startCoord );

		for ( int step = 0; step < MaxBallisticSteps; step++ )
		{
			v.y -= g * BallisticDt;
			p += v * BallisticDt;
			float elapsed = ( step + 1 ) * BallisticDt;

			if ( !world.ContainsWorldPoint( p ) )
			{
				// Seat along the throw direction near where we left the surface — never snap
				// sideways to whatever cell is nearest the player.
				if ( TrySeatAlongThrow( world, start, velocity, p, def, out landPos, out landVelocity, out flightTime ) )
					return true;

				break;
			}

			if ( world.TryGetChunkCoord( p, out TreasureChunkCoord coord ) )
				world.EnsureChunkLoaded( coord );

			if ( !world.Sampler.TrySample( p, out TreasureSurfaceSample sample ) )
				continue;

			if ( !sample.Traversable )
				continue;

			if ( p.y <= sample.Height + HitEpsilonY )
			{
				landPos = new Vector3( p.x, sample.Height, p.z );
				landVelocity = FlattenHorizontal( v ) * def.throwLandingSpeedScale;
				// Use true impact time so steep lobs finish and steep drops don't overshoot.
				flightTime = Mathf.Clamp( elapsed, MinFlightTime * 0.5f, MaxFlightTime );
				return true;
			}
		}

		return TrySeatAlongThrow( world, start, velocity, start + FlattenHorizontal( velocity ).normalized * 4f, def, out landPos, out landVelocity, out flightTime );
	}

	static bool TrySeatAlongThrow(
		TreasureSurfaceWorld world,
		Vector3 start,
		Vector3 velocity,
		Vector3 preferNear,
		TreasureSurfaceDefinition def,
		out Vector3 landPos,
		out Vector3 landVelocity,
		out float flightTime )
	{
		landPos = start;
		landVelocity = FlattenHorizontal( velocity ) * ( def != null ? def.throwLandingSpeedScale : 0.85f );
		flightTime = MinFlightTime;

		Vector3 flat = FlattenHorizontal( velocity );
		float planarSpeed = flat.magnitude;
		Vector3 dir;
		if ( planarSpeed > 0.0001f )
			dir = flat / planarSpeed;
		else
			dir = Vector3.zero;

		float landScale = def != null ? def.throwLandingSpeedScale : 0.85f;
		float[] distances = { 1.5f, 3f, 5f, 8f, 12f };

		for ( int i = 0; i < distances.Length; i++ )
		{
			float d = distances[ i ];
			Vector3 probe = dir.sqrMagnitude > 0.0001f
				? start + dir * d
				: preferNear;

			if ( world.TryGetChunkCoord( probe, out TreasureChunkCoord coord ) )
				world.EnsureChunkLoaded( coord );

			if ( world.Sampler != null
				&& world.Sampler.TrySample( probe, out TreasureSurfaceSample sample )
				&& sample.Traversable )
			{
				landPos = new Vector3( probe.x, sample.Height, probe.z );
				landVelocity = flat * landScale;
				flightTime = EstimateFlightTime( start, landPos, velocity, def );
				return true;
			}

			if ( world.TryFindNearestTraversable( probe, out Vector3 recovered, out TreasureSurfaceSample recoveredSample, preferStable: false ) )
			{
				// Reject recoveries that swing too far off the throw axis.
				Vector3 toRecovered = FlattenHorizontal( recovered - start );
				if ( dir.sqrMagnitude > 0.0001f && toRecovered.sqrMagnitude > 0.0001f )
				{
					float along = Vector3.Dot( toRecovered.normalized, dir );
					if ( along < 0.35f )
						continue;
				}

				landPos = recovered;
				landPos.y = recoveredSample.Height;
				landVelocity = flat * landScale;
				flightTime = EstimateFlightTime( start, landPos, velocity, def );
				return true;
			}
		}

		if ( world.TryFindNearestTraversable( preferNear, out landPos, out TreasureSurfaceSample nearest, preferStable: false ) )
		{
			landPos.y = nearest.Height;
			landVelocity = flat * landScale;
			flightTime = EstimateFlightTime( start, landPos, velocity, def );
			return true;
		}

		return false;
	}

	static float EstimateFlightTime( Vector3 start, Vector3 end, Vector3 velocity, TreasureSurfaceDefinition def )
	{
		float planarSpeed = FlattenHorizontal( velocity ).magnitude;
		float distance = FlattenHorizontal( end - start ).magnitude;
		float g = def != null ? def.throwBallisticGravity : 18f;

		float fromDistance = planarSpeed > 0.1f ? distance / planarSpeed : MinFlightTime;

		// Vertical ballistic time so steep lobs / drops match the path to the floor.
		float dy = end.y - start.y;
		float vy = velocity.y;
		float disc = vy * vy - 2f * g * dy;
		float fromVertical = MinFlightTime;
		if ( disc >= 0f && g > 0.0001f )
		{
			float sqrt = Mathf.Sqrt( disc );
			float tA = ( vy + sqrt ) / g;
			float tB = ( vy - sqrt ) / g;
			// Prefer the later positive root (landing after apex for lobs).
			float t = Mathf.Max( tA, tB );
			if ( t < 0.05f )
				t = tA > 0.05f ? tA : tB;
			if ( t > fromVertical )
				fromVertical = t;
		}

		return Mathf.Clamp( Mathf.Max( fromDistance, fromVertical ), MinFlightTime, MaxFlightTime );
	}

	public static IEnumerator AnimateThrowCluster(
		List<TreasureItem> cluster,
		Vector3 throwVelocity,
		float gravity,
		Vector3[] endPositions,
		Quaternion[] endRotations,
		Vector3[] landVelocities,
		float[] flightTimes,
		float spins,
		float speedScale = 1f )
	{
		if ( cluster == null || endPositions == null || endRotations == null )
			yield break;

		int count = Mathf.Min( cluster.Count, Mathf.Min( endPositions.Length, endRotations.Length ) );
		if ( count <= 0 )
			yield break;

		Vector3[] startPos = new Vector3[ count ];
		Quaternion[] startRot = new Quaternion[ count ];
		Vector3[] startScale = new Vector3[ count ];
		Vector3[] endScale = new Vector3[ count ];
		float[] durations = new float[ count ];

		speedScale = Mathf.Max( 0.1f, speedScale );
		float maxDuration = MinFlightTime;

		for ( int i = 0; i < count; i++ )
		{
			TreasureItem item = cluster[ i ];
			if ( item == null )
				continue;

			Transform t = item.transform;
			t.SetParent( null, true );
			item.ApplyWorldScale();

			Rigidbody body = item.Body;
			if ( body != null )
			{
				body.isKinematic = true;
				body.detectCollisions = false;
				body.useGravity = false;
				body.linearVelocity = Vector3.zero;
				body.angularVelocity = Vector3.zero;
			}

			startPos[ i ] = t.position;
			startRot[ i ] = t.rotation;
			startScale[ i ] = t.localScale;
			endScale[ i ] = item.GetWorldScale();

			float raw = flightTimes != null && i < flightTimes.Length ? flightTimes[ i ] : MinFlightTime;
			durations[ i ] = Mathf.Clamp( raw / speedScale, MinFlightTime * 0.5f, MaxFlightTime );
			if ( durations[ i ] > maxDuration )
				maxDuration = durations[ i ];
		}

		Vector3 accel = Vector3.down * Mathf.Max( 1f, gravity );
		Vector3 travelHint = FlattenHorizontal( throwVelocity );
		if ( travelHint.sqrMagnitude < 0.0001f && count > 0 )
			travelHint = FlattenHorizontal( endPositions[ 0 ] - startPos[ 0 ] );

		float elapsed = 0f;
		while ( elapsed < maxDuration )
		{
			elapsed += Time.deltaTime;

			for ( int i = 0; i < count; i++ )
			{
				TreasureItem item = cluster[ i ];
				if ( item == null )
					continue;

				float dur = durations[ i ];
				float t = Mathf.Min( elapsed, dur );
				float u = dur > 0.0001f ? Mathf.Clamp01( t / dur ) : 1f;

				// True ballistic path from throw velocity (aim + inherited motion).
				Vector3 ballistic = startPos[ i ] + throwVelocity * t + 0.5f * accel * ( t * t );

				// Stop at the floor: steep drops must not tunnel under the seat height.
				float landY = endPositions[ i ].y;
				float verticalSpeed = throwVelocity.y + accel.y * t;
				if ( ballistic.y < landY && verticalSpeed <= 0f )
					ballistic.y = landY;

				// Late ease only for stack-height / seating corrections — keep the arc intact.
				float settle = CoinFlipMotion.SmoothStep( Mathf.Clamp01( ( u - SettleStartU ) / ( 1f - SettleStartU ) ) );
				Vector3 pos = Vector3.Lerp( ballistic, endPositions[ i ], settle );

				Transform tr = item.transform;
				tr.position = pos;

				Vector3 flipEnd = travelHint.sqrMagnitude > 0.0001f
					? startPos[ i ] + travelHint
					: endPositions[ i ];
				tr.rotation = CoinFlipMotion.EvaluateFlipRotation(
					startRot[ i ],
					endRotations[ i ],
					startPos[ i ],
					flipEnd,
					u,
					spins );
				tr.localScale = Vector3.Lerp( startScale[ i ], endScale[ i ], CoinFlipMotion.SmoothStep( u ) );
			}

			yield return null;
		}

		for ( int i = 0; i < count; i++ )
		{
			TreasureItem member = cluster[ i ];
			if ( member == null )
				continue;

			member.transform.SetPositionAndRotation( endPositions[ i ], endRotations[ i ] );
			member.ApplyWorldScale();
			member.EndFlight();

			if ( member.State == TreasureItemState.Held && member.Owner is PlayerCarry carry && carry.ContainsItem( member ) )
				continue;

			Vector3 vel = Vector3.zero;
			if ( landVelocities != null && i < landVelocities.Length )
				vel = landVelocities[ i ];

			float dur = durations[ i ];
			float impactVertical = throwVelocity.y + accel.y * dur;
			if ( impactVertical < -0.05f )
				vel.y = impactVertical;

			Vector3 pos = endPositions[ i ];
			Quaternion rot = endRotations[ i ];

			TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
			if ( world != null && world.Sampler != null
				&& world.Sampler.TrySample( pos, out TreasureSurfaceSample landSample )
				&& landSample.Traversable )
			{
				pos.y = member.Definition != null && member.Definition.category == TreasureCategory.Gem
					? landSample.Height + TreasureSurfaceSeat.GetStableContactLift( member )
					: TreasureSurfaceSeat.GetContactY( member, landSample, rot );
			}

			if ( TreasurePileLooseDeposit.TryAbsorbLooseItem( member, pos ) )
				continue;

			member.EnterSurface( pos, rot, vel );
		}
	}

	public static void ResolveFlightSpins( TreasureItem item, out float spins )
	{
		spins = CoinFlipMotion.IsCoin( item ) ? CoinFlipMotion.DefaultSpins : 0.55f;
	}

	static Vector3 FlattenHorizontal( Vector3 v )
	{
		return new Vector3( v.x, 0f, v.z );
	}
}
