using System.Collections;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Dense sample path from throw prediction so flight tweens follow pile bounces.
/// </summary>
public sealed class ThrowFlightPath
{
	public readonly List<Vector3> Positions = new List<Vector3>( 128 );
	public readonly List<float> Times = new List<float>( 128 );
	public Vector3 LandVelocity;
	public bool SeatOnPileFlow;

	public void Clear()
	{
		Positions.Clear();
		Times.Clear();
		LandVelocity = Vector3.zero;
		SeatOnPileFlow = false;
	}

	public void Add( Vector3 position, float time )
	{
		Positions.Add( position );
		Times.Add( time );
	}

	public float Duration
	{
		get
		{
			if ( Times.Count <= 0 )
				return 0f;
			return Times[ Times.Count - 1 ];
		}
	}

	public Vector3 Evaluate( float time )
	{
		int count = Positions.Count;
		if ( count <= 0 )
			return Vector3.zero;
		if ( count == 1 || time <= Times[ 0 ] )
			return Positions[ 0 ];

		float endT = Times[ count - 1 ];
		if ( time >= endT )
			return Positions[ count - 1 ];

		for ( int i = 1; i < count; i++ )
		{
			float t1 = Times[ i ];
			if ( time > t1 )
				continue;

			float t0 = Times[ i - 1 ];
			float span = t1 - t0;
			float u = span > 0.0001f ? ( time - t0 ) / span : 1f;
			return Vector3.Lerp( Positions[ i - 1 ], Positions[ i ], u );
		}

		return Positions[ count - 1 ];
	}
}

/// <summary>
/// Predicts ballistic landings on the treasure surface and plays throw flight tweens.
/// Flight follows the throw velocity (aim + inherited player motion) under gravity,
/// and always runs long enough for the arc to reach the floor at any aim pitch.
/// Gold piles are solid: throws bounce off the mound, then land and flow on the floor.
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
	const int DefaultPileMaxBounces = 3;
	const float DefaultPileRestitution = 0.45f;
	const int DefaultBoundaryMaxBounces = 4;
	const float DefaultBoundaryRestitution = 0.55f;
	const float BoundaryNudge = 0.04f;

	public static bool IsStrictTraversableLanding( TreasureSurfaceWorld world, Vector3 landPos )
	{
		if ( world == null || world.Sampler == null )
			return false;
		if ( IsGoldPileSurfacePoint( landPos ) )
			return false;
		return world.Sampler.TrySample( landPos, out TreasureSurfaceSample sample ) && sample.Traversable;
	}

	public static bool TryPredictLanding(
		TreasureSurfaceWorld world,
		Vector3 start,
		Vector3 velocity,
		out Vector3 landPos,
		out Vector3 landVelocity )
	{
		return TryPredictLanding( world, start, velocity, out landPos, out landVelocity, out _, null );
	}

	public static bool TryPredictLanding(
		TreasureSurfaceWorld world,
		Vector3 start,
		Vector3 velocity,
		out Vector3 landPos,
		out Vector3 landVelocity,
		out float flightTime )
	{
		return TryPredictLanding( world, start, velocity, out landPos, out landVelocity, out flightTime, null );
	}

	public static bool TryPredictLanding(
		TreasureSurfaceWorld world,
		Vector3 start,
		Vector3 velocity,
		out Vector3 landPos,
		out Vector3 landVelocity,
		out float flightTime,
		ThrowFlightPath path )
	{
		landPos = start;
		landVelocity = FlattenHorizontal( velocity );
		flightTime = MinFlightTime;
		if ( path != null )
			path.Clear();

		if ( world == null || !world.IsInitialized )
			return false;

		TreasureSurfaceDefinition def = world.Definition;
		float g = def != null ? def.throwBallisticGravity : 18f;
		float landScale = def != null ? def.throwLandingSpeedScale : 0.85f;
		int pileBouncesLeft = def != null ? Mathf.Max( 0, def.throwPileMaxBounces ) : DefaultPileMaxBounces;
		float pileRestitution = def != null ? Mathf.Max( 0.05f, def.throwPileBounceRestitution ) : DefaultPileRestitution;
		int boundaryBouncesLeft = def != null ? Mathf.Max( 0, def.throwBoundaryMaxBounces ) : DefaultBoundaryMaxBounces;
		float boundaryRestitution = def != null ? Mathf.Max( 0.05f, def.throwBoundaryBounceRestitution ) : DefaultBoundaryRestitution;

		Vector3 p = start;
		Vector3 v = velocity;

		if ( path != null )
			path.Add( p, 0f );

		// Ensure the start chunk is loaded so early samples succeed.
		if ( world.TryGetChunkCoord( start, out TreasureChunkCoord startCoord ) )
			world.EnsureChunkLoaded( startCoord );

		// Thrown from over a blocker / unpainted cell: send it back toward the surface.
		if ( !IsThrowFlightPassable( world, start )
			&& TryRedirectTowardTraversable( world, start, ref v, boundaryRestitution ) )
		{
			boundaryBouncesLeft = Mathf.Max( 0, boundaryBouncesLeft - 1 );
		}

		for ( int step = 0; step < MaxBallisticSteps; step++ )
		{
			Vector3 prev = p;
			v.y -= g * BallisticDt;
			p += v * BallisticDt;
			float elapsed = ( step + 1 ) * BallisticDt;

			if ( !IsThrowFlightPassable( world, p ) )
			{
				bool fromPassable = IsThrowFlightPassable( world, prev );
				if ( boundaryBouncesLeft <= 0 )
				{
					Vector3 seatFrom = fromPassable ? prev : p;
					if ( TryLandAt( world, seatFrom, v, landScale, elapsed, path, out landPos, out landVelocity, out flightTime ) )
						return true;
					if ( TrySeatAlongThrow( world, start, v, seatFrom, def, out landPos, out landVelocity, out flightTime ) )
					{
						if ( path != null )
						{
							path.Add( landPos, flightTime );
							path.LandVelocity = landVelocity;
						}

						return true;
					}

					break;
				}

				if ( fromPassable )
				{
					boundaryBouncesLeft--;
					BounceOffThrowBoundary( world, prev, p, ref v, boundaryRestitution );
					p = new Vector3( prev.x, p.y, prev.z );
					Vector3 inward = FlattenHorizontal( v );
					if ( inward.sqrMagnitude > 0.0001f )
						p += inward.normalized * BoundaryNudge;
					if ( !IsThrowFlightPassable( world, p ) )
						p = new Vector3( prev.x, p.y, prev.z );
					if ( path != null )
						path.Add( p, elapsed );
					continue;
				}

				if ( TryRedirectTowardTraversable( world, p, ref v, boundaryRestitution ) )
				{
					boundaryBouncesLeft--;
					Vector3 toward = FlattenHorizontal( v );
					if ( toward.sqrMagnitude > 0.0001f )
						p += toward.normalized * BoundaryNudge;
					if ( path != null )
						path.Add( p, elapsed );
					continue;
				}
			}

			if ( !world.ContainsWorldPoint( p ) )
			{
				if ( TrySeatAlongThrow( world, start, velocity, p, def, out landPos, out landVelocity, out flightTime ) )
				{
					if ( path != null )
					{
						path.Add( landPos, flightTime );
						path.LandVelocity = landVelocity;
					}

					return true;
				}

				break;
			}

			if ( world.TryGetChunkCoord( p, out TreasureChunkCoord coord ) )
				world.EnsureChunkLoaded( coord );

			if ( TryGetPileSurface( p, out float pileY, out Vector3 pileNormal ) )
			{
				bool hitPile = p.y <= pileY + HitEpsilonY || prev.y > pileY + HitEpsilonY && p.y <= pileY + HitEpsilonY;
				if ( hitPile )
				{
					p = new Vector3( p.x, pileY + HitEpsilonY * 0.5f, p.z );
					if ( path != null )
						path.Add( p, elapsed );

					if ( pileBouncesLeft <= 0 )
					{
						landPos = new Vector3( p.x, pileY, p.z );
						landVelocity = FlattenHorizontal( v ) * landScale;
						if ( landVelocity.sqrMagnitude < 0.04f )
						{
							Vector3 downhill = Vector3.ProjectOnPlane( Vector3.down, pileNormal );
							if ( downhill.sqrMagnitude > 0.0001f )
								landVelocity = downhill.normalized * 0.6f;
						}

						flightTime = Mathf.Clamp( elapsed, MinFlightTime * 0.5f, MaxFlightTime );
						if ( path != null )
						{
							path.LandVelocity = landVelocity;
							path.SeatOnPileFlow = true;
						}

						return true;
					}

					pileBouncesLeft--;
					v = Vector3.Reflect( v, pileNormal.normalized );
					v *= pileRestitution;
					// Nudge outward so the next step does not immediately re-hit.
					if ( Vector3.Dot( v, pileNormal ) < 0.35f )
						v += pileNormal.normalized * 0.35f;
					continue;
				}
			}

			if ( !world.Sampler.TrySample( p, out TreasureSurfaceSample sample ) )
			{
				if ( path != null && ( step % 2 == 0 ) )
					path.Add( p, elapsed );
				continue;
			}

			if ( !sample.Traversable )
			{
				bool hitBlockedFloor = p.y <= sample.Height + HitEpsilonY;
				if ( boundaryBouncesLeft > 0 && TryRedirectTowardTraversable( world, p, ref v, boundaryRestitution ) )
				{
					boundaryBouncesLeft--;
					if ( hitBlockedFloor && v.y < 1.5f )
						v.y = 1.5f;
					Vector3 toward = FlattenHorizontal( v );
					if ( toward.sqrMagnitude > 0.0001f )
						p += toward.normalized * BoundaryNudge;
					if ( path != null )
						path.Add( p, elapsed );
					continue;
				}

				if ( path != null && ( step % 2 == 0 ) )
					path.Add( p, elapsed );
				continue;
			}

			// Gold-pile stamped cells are not valid floor landings — bounce handled above.
			if ( IsGoldPileSurfacePoint( p ) )
			{
				if ( path != null && ( step % 2 == 0 ) )
					path.Add( p, elapsed );
				continue;
			}

			if ( p.y <= sample.Height + HitEpsilonY )
			{
				landPos = new Vector3( p.x, sample.Height, p.z );
				if ( !IsStrictTraversableLanding( world, landPos ) )
					continue;

				landVelocity = FlattenHorizontal( v ) * landScale;
				flightTime = Mathf.Clamp( elapsed, MinFlightTime * 0.5f, MaxFlightTime );
				if ( path != null )
				{
					path.Add( landPos, flightTime );
					path.LandVelocity = landVelocity;
					path.SeatOnPileFlow = false;
				}

				return true;
			}

			if ( path != null && ( step % 2 == 0 ) )
				path.Add( p, elapsed );
		}

		if ( TrySeatAlongThrow( world, start, velocity, start + FlattenHorizontal( velocity ).normalized * 4f, def, out landPos, out landVelocity, out flightTime ) )
		{
			if ( path != null )
			{
				path.Add( landPos, flightTime );
				path.LandVelocity = landVelocity;
			}

			return true;
		}

		return false;
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

			if ( IsGoldPileSurfacePoint( probe ) )
				continue;

			if ( world.Sampler != null
				&& world.Sampler.TrySample( probe, out TreasureSurfaceSample sample )
				&& sample.Traversable )
			{
				landPos = new Vector3( probe.x, sample.Height, probe.z );
				if ( !IsStrictTraversableLanding( world, landPos ) )
					continue;

				landVelocity = flat * landScale;
				flightTime = EstimateFlightTime( start, landPos, velocity, def );
				return true;
			}

			if ( world.TryFindNearestTraversable( probe, out Vector3 recovered, out TreasureSurfaceSample recoveredSample, preferStable: false ) )
			{
				if ( IsGoldPileSurfacePoint( recovered ) )
					continue;

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
				if ( !IsStrictTraversableLanding( world, landPos ) )
					continue;

				landVelocity = flat * landScale;
				flightTime = EstimateFlightTime( start, landPos, velocity, def );
				return true;
			}
		}

		if ( world.TryFindNearestTraversable( preferNear, out landPos, out TreasureSurfaceSample nearest, preferStable: false ) )
		{
			if ( !IsGoldPileSurfacePoint( landPos ) && IsStrictTraversableLanding( world, landPos ) )
			{
				landPos.y = nearest.Height;
				landVelocity = flat * landScale;
				flightTime = EstimateFlightTime( start, landPos, velocity, def );
				return true;
			}
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
		float speedScale = 1f,
		ThrowFlightPath[] paths = null )
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
			if ( paths != null && i < paths.Length && paths[ i ] != null && paths[ i ].Positions.Count > 1 )
				raw = Mathf.Max( raw, paths[ i ].Duration );

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

				Vector3 pos;
				ThrowFlightPath path = paths != null && i < paths.Length ? paths[ i ] : null;
				if ( path != null && path.Positions.Count > 1 )
				{
					float pathT = path.Duration * u;
					pos = path.Evaluate( pathT );
					// Late ease into authored seat height (stack offsets).
					float settle = CoinFlipMotion.SmoothStep( Mathf.Clamp01( ( u - SettleStartU ) / ( 1f - SettleStartU ) ) );
					pos = Vector3.Lerp( pos, endPositions[ i ], settle );
				}
				else
				{
					// True ballistic path from throw velocity (aim + inherited motion).
					Vector3 ballistic = startPos[ i ] + throwVelocity * t + 0.5f * accel * ( t * t );

					// Stop at the floor: steep drops must not tunnel under the seat height.
					float landY = endPositions[ i ].y;
					float verticalSpeed = throwVelocity.y + accel.y * t;
					if ( ballistic.y < landY && verticalSpeed <= 0f )
						ballistic.y = landY;

					// Late ease only for stack-height / seating corrections — keep the arc intact.
					float settle = CoinFlipMotion.SmoothStep( Mathf.Clamp01( ( u - SettleStartU ) / ( 1f - SettleStartU ) ) );
					pos = Vector3.Lerp( ballistic, endPositions[ i ], settle );
				}

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

			bool keepFlightRotation = member.Definition != null
				&& member.Definition.category == TreasureCategory.Gem;
			Quaternion landRot = keepFlightRotation
				? member.transform.rotation
				: endRotations[ i ];
			member.transform.SetPositionAndRotation( endPositions[ i ], landRot );
			member.ApplyWorldScale();

			if ( member.State == TreasureItemState.Held && member.Owner is PlayerCarry carry && carry.ContainsItem( member ) )
			{
				member.EndFlight();
				continue;
			}

			Vector3 vel = Vector3.zero;
			if ( landVelocities != null && i < landVelocities.Length )
				vel = landVelocities[ i ];

			ThrowFlightPath path = paths != null && i < paths.Length ? paths[ i ] : null;
			if ( path != null && path.LandVelocity.sqrMagnitude > 0.0001f )
				vel = path.LandVelocity;

			float dur = durations[ i ];
			float impactVertical = throwVelocity.y + accel.y * dur;
			if ( impactVertical < -0.05f && ( path == null || !path.SeatOnPileFlow ) )
				vel.y = impactVertical;

			Vector3 pos = endPositions[ i ];
			Quaternion rot = landRot;

			TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
			if ( world != null && world.Sampler != null
				&& world.Sampler.TrySample( pos, out TreasureSurfaceSample landSample )
				&& landSample.Traversable )
			{
				bool useStableSeat = member.Definition != null && member.Definition.category != TreasureCategory.Coin;
				pos.y = useStableSeat
					? landSample.Height + TreasureSurfaceSeat.GetStableContactLift( member )
					: TreasureSurfaceSeat.GetContactY( member, landSample, rot );
			}

			if ( TreasurePileLooseDeposit.TryAbsorbLooseItem( member, pos ) )
			{
				member.EndFlight();
				continue;
			}

			bool seatOnPileFlow = path != null && path.SeatOnPileFlow;

			if ( ArtifactPlantFeedback.IsArtifact( member ) && !seatOnPileFlow )
			{
				Quaternion plantRot = TreasureOrientation.AlignUprightToNormal( rot, Vector3.up );
				Vector3 plantUp = Vector3.up;
				if ( world != null && world.Sampler != null
					&& world.Sampler.TrySample( pos, out TreasureSurfaceSample plantSample )
					&& plantSample.Traversable )
				{
					plantUp = plantSample.Normal.sqrMagnitude > 0.0001f
						? plantSample.Normal.normalized
						: Vector3.up;
					if ( Vector3.Dot( plantUp, Vector3.up ) < PlacementFloorSurface.MinFloorUpDot )
						plantUp = Vector3.up;
					plantRot = TreasureOrientation.AlignUprightToNormal( rot, plantUp );
					pos.y = plantSample.Height + TreasureSurfaceSeat.GetStableContactLift( member );
				}

				pos = FloorPlacementTarget.ResolveArtifactPlaceSeparation( member, pos );
				if ( world != null && world.Sampler != null
					&& world.Sampler.TrySample( pos, out TreasureSurfaceSample separatedSample )
					&& separatedSample.Traversable )
				{
					pos.y = separatedSample.Height + TreasureSurfaceSeat.GetStableContactLift( member );
				}

				member.EndFlight();
				member.EnterSettledPhysics( pos, plantRot );
				ArtifactPlantFeedback.PlayOn( member );
				continue;
			}

			member.EndFlight();

			if ( TreasureItem.UsesSurfaceSimulation( member.Definition ) || seatOnPileFlow )
			{
				member.EnterSurface( pos, rot, FlattenHorizontal( vel ) );
			}
			else
			{
				member.EnterPhysics( pos, rot, vel );
				if ( member.Definition != null && member.Definition.category == TreasureCategory.Artifact )
					member.ClampArtifactToTreasureSurface( allowBounce: true );
			}
		}
	}

	public static void ResolveFlightSpins( TreasureItem item, out float spins )
	{
		TreasureSurfaceDefinition def = null;
		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		if ( world != null )
			def = world.Definition;

		if ( CoinFlipMotion.IsCoin( item ) )
		{
			spins = def != null ? def.throwCoinSpins : CoinFlipMotion.DefaultSpins;
			return;
		}

		if ( item != null
			&& item.Definition != null
			&& item.Definition.category == TreasureCategory.Gem )
		{
			spins = def != null ? def.throwGemSpins : 0.55f;
			return;
		}

		// Crowns / goblets / helmets / artifacts.
		spins = def != null ? def.throwArtifactSpins : 1f;
	}

	static bool IsThrowFlightPassable( TreasureSurfaceWorld world, Vector3 worldPos )
	{
		if ( world == null || !world.IsInitialized )
			return false;
		if ( !world.ContainsWorldPoint( worldPos ) )
			return false;
		if ( IsGoldPileSurfacePoint( worldPos ) )
			return true;
		if ( world.Sampler == null )
			return false;
		if ( world.TryGetChunkCoord( worldPos, out TreasureChunkCoord coord ) )
			world.EnsureChunkLoaded( coord );
		return world.Sampler.TrySample( worldPos, out TreasureSurfaceSample sample ) && sample.Traversable;
	}

	static bool TryRedirectTowardTraversable(
		TreasureSurfaceWorld world,
		Vector3 pos,
		ref Vector3 velocity,
		float restitution )
	{
		if ( world == null || !world.IsInitialized )
			return false;

		if ( !world.TryFindNearestTraversable( pos, out Vector3 recovered, out _, preferStable: false ) )
			return false;

		Vector3 toSurface = FlattenHorizontal( recovered - pos );
		if ( toSurface.sqrMagnitude < 0.0001f )
			return false;

		toSurface.Normalize();
		Vector3 planar = FlattenHorizontal( velocity );
		float toward = Vector3.Dot( planar, toSurface );
		if ( toward > 0.35f )
			return false;

		restitution = Mathf.Clamp( restitution, 0.05f, 1.5f );
		float speed = Mathf.Max( planar.magnitude, 2.5f ) * restitution;
		velocity.x = toSurface.x * speed;
		velocity.z = toSurface.z * speed;
		if ( velocity.y < 0.75f )
			velocity.y = 0.75f;
		return true;
	}

	static void BounceOffThrowBoundary(
		TreasureSurfaceWorld world,
		Vector3 prev,
		Vector3 blocked,
		ref Vector3 velocity,
		float restitution )
	{
		restitution = Mathf.Clamp( restitution, 0.05f, 1.5f );
		bool passX = IsThrowFlightPassable( world, new Vector3( blocked.x, prev.y, prev.z ) );
		bool passZ = IsThrowFlightPassable( world, new Vector3( prev.x, prev.y, blocked.z ) );
		float y = velocity.y;
		if ( !passX && passZ )
		{
			float sign = prev.x <= blocked.x ? -1f : 1f;
			velocity.x = sign * Mathf.Abs( velocity.x );
		}
		else if ( passX && !passZ )
		{
			float sign = prev.z <= blocked.z ? -1f : 1f;
			velocity.z = sign * Mathf.Abs( velocity.z );
		}
		else
		{
			velocity.x = -velocity.x;
			velocity.z = -velocity.z;
		}

		velocity.x *= restitution;
		velocity.z *= restitution;
		velocity.y = y;
	}

	static bool TryLandAt(
		TreasureSurfaceWorld world,
		Vector3 pos,
		Vector3 velocity,
		float landScale,
		float elapsed,
		ThrowFlightPath path,
		out Vector3 landPos,
		out Vector3 landVelocity,
		out float flightTime )
	{
		landPos = pos;
		landVelocity = FlattenHorizontal( velocity ) * landScale;
		flightTime = Mathf.Clamp( elapsed, MinFlightTime * 0.5f, MaxFlightTime );
		if ( world == null || world.Sampler == null )
			return false;
		if ( !world.Sampler.TrySample( pos, out TreasureSurfaceSample sample ) || !sample.Traversable )
			return false;
		if ( IsGoldPileSurfacePoint( pos ) )
			return false;

		landPos = new Vector3( pos.x, sample.Height, pos.z );
		if ( !IsStrictTraversableLanding( world, landPos ) )
			return false;

		if ( path != null )
		{
			path.Add( landPos, flightTime );
			path.LandVelocity = landVelocity;
			path.SeatOnPileFlow = false;
		}

		return true;
	}

	static bool IsGoldPileSurfacePoint( Vector3 worldPos )
	{
		IReadOnlyList<TreasurePileSurfaceBridge> bridges = TreasurePileSurfaceBridge.Active;
		for ( int i = 0; i < bridges.Count; i++ )
		{
			TreasurePileSurfaceBridge bridge = bridges[ i ];
			if ( bridge == null )
				continue;

			TreasurePileVisual visual = bridge.Visual;
			if ( visual == null )
				visual = bridge.GetComponent<TreasurePileVisual>();
			if ( visual == null )
				continue;

			if ( visual.HasPileSurfaceAt( worldPos ) || visual.IsPointBuried( worldPos ) )
				return true;
		}

		return false;
	}

	static bool TryGetPileSurface( Vector3 worldPos, out float surfaceY, out Vector3 normal )
	{
		surfaceY = 0f;
		normal = Vector3.up;
		bool found = false;
		float bestY = float.MinValue;

		IReadOnlyList<TreasurePileSurfaceBridge> bridges = TreasurePileSurfaceBridge.Active;
		for ( int i = 0; i < bridges.Count; i++ )
		{
			TreasurePileSurfaceBridge bridge = bridges[ i ];
			if ( bridge == null )
				continue;

			TreasurePileVisual visual = bridge.Visual;
			if ( visual == null )
				visual = bridge.GetComponent<TreasurePileVisual>();
			if ( visual == null )
				continue;

			GoldPileHeightfield hf = visual.Heightfield;
			if ( hf == null || !hf.IsInitialized )
				continue;

			if ( !hf.ExistsAtWorld( worldPos, visual.transform ) )
				continue;

			float y = hf.SampleWorldHeight( worldPos, visual.transform );
			if ( !found || y > bestY )
			{
				found = true;
				bestY = y;
				surfaceY = y;
				normal = hf.SampleWorldNormal( worldPos, visual.transform );
				if ( normal.sqrMagnitude < 0.0001f )
					normal = Vector3.up;
				else
					normal = normal.normalized;
			}
		}

		return found;
	}

	static Vector3 FlattenHorizontal( Vector3 v )
	{
		return new Vector3( v.x, 0f, v.z );
	}
}
