using System.Collections;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Predicts ballistic landings on the treasure surface and plays throw flight tweens.
/// </summary>
public static class TreasureSurfaceThrow
{
	const float BallisticDt = 1f / 60f;
	const int MaxBallisticSteps = 240;

	public static bool TryPredictLanding(
		TreasureSurfaceWorld world,
		Vector3 start,
		Vector3 velocity,
		out Vector3 landPos,
		out Vector3 landVelocity )
	{
		landPos = start;
		landVelocity = new Vector3( velocity.x, 0f, velocity.z );

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

			if ( !world.ContainsWorldPoint( p ) )
			{
				if ( world.TryFindNearestTraversable( p, out Vector3 recovered, out TreasureSurfaceSample recoveredSample, preferStable: false ) )
				{
					landPos = recovered;
					landPos.y = recoveredSample.Height;
					landVelocity = new Vector3( v.x, 0f, v.z ) * def.throwLandingSpeedScale;
					return true;
				}

				break;
			}

			if ( world.TryGetChunkCoord( p, out TreasureChunkCoord coord ) )
				world.EnsureChunkLoaded( coord );

			if ( !world.Sampler.TrySample( p, out TreasureSurfaceSample sample ) )
				continue;

			if ( !sample.Traversable )
				continue;

			if ( p.y <= sample.Height + 0.04f )
			{
				landPos = new Vector3( p.x, sample.Height, p.z );
				landVelocity = new Vector3( v.x, 0f, v.z ) * def.throwLandingSpeedScale;
				return true;
			}
		}

		// Fallback: project forward on XZ and seat on surface.
		Vector3 flat = start + new Vector3( velocity.x, 0f, velocity.z ).normalized * 4f;
		if ( world.Sampler.TrySample( flat, out TreasureSurfaceSample fallback ) && fallback.Traversable )
		{
			landPos = new Vector3( flat.x, fallback.Height, flat.z );
			landVelocity = new Vector3( velocity.x, 0f, velocity.z ) * def.throwLandingSpeedScale;
			return true;
		}

		if ( world.TryFindNearestTraversable( start, out landPos, out TreasureSurfaceSample nearest, preferStable: false ) )
		{
			landPos.y = nearest.Height;
			landVelocity = new Vector3( velocity.x, 0f, velocity.z ) * def.throwLandingSpeedScale;
			return true;
		}

		return false;
	}

	public static IEnumerator AnimateThrowCluster(
		List<TreasureItem> cluster,
		Vector3[] endPositions,
		Quaternion[] endRotations,
		Vector3[] landVelocities,
		float duration,
		float arcHeight,
		float spins )
	{
		if ( cluster == null || endPositions == null || endRotations == null )
			yield break;

		yield return CoinFlipMotion.AnimateWorldFlips(
			cluster,
			endPositions,
			endRotations,
			duration,
			arcHeight,
			spins );

		for ( int i = 0; i < cluster.Count; i++ )
		{
			TreasureItem member = cluster[ i ];
			if ( member == null )
				continue;

			member.EndFlight();

			if ( member.State == TreasureItemState.Held && member.Owner is PlayerCarry carry && carry.ContainsItem( member ) )
				continue;

			Vector3 vel = Vector3.zero;
			if ( landVelocities != null && i < landVelocities.Length )
				vel = landVelocities[ i ];

			Vector3 pos = i < endPositions.Length ? endPositions[ i ] : member.transform.position;
			Quaternion rot = i < endRotations.Length ? endRotations[ i ] : member.transform.rotation;

			if ( TreasurePileLooseDeposit.TryAbsorbLooseItem( member, pos ) )
				continue;

			member.EnterSurface( pos, rot, vel );
		}
	}

	public static void ResolveFlightTuning(
		TreasureItem item,
		TreasureSurfaceDefinition def,
		float travelDistance,
		out float duration,
		out float arcHeight,
		out float spins )
	{
		bool coin = CoinFlipMotion.IsCoin( item );
		float arcScale = def != null
			? ( coin ? def.throwCoinArcScale : def.throwItemArcScale )
			: 1f;

		if ( coin )
		{
			arcHeight = CoinFlipMotion.DefaultArcHeight * arcScale;
			spins = CoinFlipMotion.DefaultSpins;
			duration = CoinFlipMotion.DefaultDuration;
		}
		else
		{
			arcHeight = CoinFlipMotion.DefaultItemArcHeight * arcScale;
			spins = 0f;
			duration = CoinFlipMotion.DefaultItemArcDuration;
		}

		// Longer throws get a slightly longer flight.
		float distanceBoost = Mathf.Clamp( travelDistance * 0.04f, 0f, 0.35f );
		duration = Mathf.Clamp( duration + distanceBoost, 0.12f, 0.85f );
		arcHeight += travelDistance * ( coin ? 0.04f : 0.01f );
	}
}
