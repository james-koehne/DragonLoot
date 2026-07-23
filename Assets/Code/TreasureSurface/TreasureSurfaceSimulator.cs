using System.Collections.Generic;
using System.Diagnostics;

using UnityEngine;

/// <summary>
/// Rigidbody-free rolling / settle / recovery for loose treasure on the surface.
/// </summary>
public sealed class TreasureSurfaceSimulator
{
	struct SimBody
	{
		public TreasureItem Item;
		public Vector3 Velocity;
		public float RestTimer;
		public bool Sleeping;
		public TreasureCategory Category;
	}

	readonly TreasureSurfaceWorld _world;
	readonly List<SimBody> _bodies = new List<SimBody>( 256 );

	public int ActiveCount { get; private set; }
	public int SleepingCount { get; private set; }
	public int RecoveryCount { get; private set; }
	public int OutsideBoundsCount { get; private set; }
	public float AverageRollingMs { get; private set; }

	public TreasureSurfaceSimulator( TreasureSurfaceWorld world )
	{
		_world = world;
	}

	public void Register( TreasureItem item, Vector3 velocity )
	{
		if ( item == null )
			return;

		Unregister( item );

		TreasureCategory category = item.Definition != null
			? item.Definition.category
			: TreasureCategory.Coin;

		_bodies.Add( new SimBody
		{
			Item = item,
			Velocity = new Vector3( velocity.x, 0f, velocity.z ),
			RestTimer = 0f,
			Sleeping = false,
			Category = category
		} );
	}

	public void Unregister( TreasureItem item )
	{
		if ( item == null )
			return;

		for ( int i = _bodies.Count - 1; i >= 0; i-- )
		{
			if ( _bodies[ i ].Item == item )
				_bodies.RemoveAt( i );
		}
	}

	public bool IsRegistered( TreasureItem item )
	{
		if ( item == null )
			return false;

		for ( int i = 0; i < _bodies.Count; i++ )
		{
			if ( _bodies[ i ].Item == item )
				return true;
		}

		return false;
	}

	public void Wake( TreasureItem item, Vector3 velocity )
	{
		for ( int i = 0; i < _bodies.Count; i++ )
		{
			SimBody body = _bodies[ i ];
			if ( body.Item != item )
				continue;

			body.Sleeping = false;
			body.RestTimer = 0f;
			body.Velocity = new Vector3( velocity.x, 0f, velocity.z );
			_bodies[ i ] = body;
			return;
		}

		Register( item, velocity );
	}

	public void ForceSleep( TreasureItem item )
	{
		for ( int i = 0; i < _bodies.Count; i++ )
		{
			SimBody body = _bodies[ i ];
			if ( body.Item != item )
				continue;

			// Floor ForceSleep used to skip auto-stack — join nearby owned stacks first.
			if ( TryAutoStack( item ) )
				return;

			body.Sleeping = true;
			body.RestTimer = 0f;
			body.Velocity = Vector3.zero;
			_bodies[ i ] = body;
			return;
		}
	}

	public void Tick( float dt )
	{
		if ( _world == null || !_world.IsInitialized || dt <= 0f )
			return;

		TreasureSurfaceDefinition def = _world.Definition;
		TreasureSurfaceSampler sampler = _world.Sampler;
		Stopwatch sw = Stopwatch.StartNew();

		int active = 0;
		int sleeping = 0;
		float rollMsAccum = 0f;
		int rollSamples = 0;

		for ( int i = _bodies.Count - 1; i >= 0; i-- )
		{
			SimBody body = _bodies[ i ];
			TreasureItem item = body.Item;
			if ( item == null )
			{
				_bodies.RemoveAt( i );
				continue;
			}

			if ( item.State != TreasureItemState.SurfaceRolling )
			{
				_bodies.RemoveAt( i );
				continue;
			}

			if ( body.Sleeping )
			{
				sleeping++;
				continue;
			}

			active++;
			long start = sw.ElapsedTicks;
			SimulateBody( ref body, item, def, sampler, dt );
			_bodies[ i ] = body;
			rollMsAccum += ( float )( ( sw.ElapsedTicks - start ) * ( 1000.0 / Stopwatch.Frequency ) );
			rollSamples++;
		}

		ActiveCount = active;
		SleepingCount = sleeping;
		AverageRollingMs = rollSamples > 0 ? rollMsAccum / rollSamples : 0f;
	}

	void SimulateBody(
		ref SimBody body,
		TreasureItem item,
		TreasureSurfaceDefinition def,
		TreasureSurfaceSampler sampler,
		float dt )
	{
		Transform t = item.transform;
		Vector3 pos = t.position;

		if ( !IsValidPosition( pos, def ) || !sampler.TrySample( pos, out TreasureSurfaceSample sample ) )
		{
			Recover( ref body, item, preferStable: true );
			return;
		}

		if ( !sample.Traversable )
		{
			Recover( ref body, item, preferStable: true );
			return;
		}

		ResolveMotionParams( body.Category, sample.Material, def, out float friction, out float bounce, out float speedScale, out float wobble, out bool artifact );

		Vector3 velocity = body.Velocity;

		if ( artifact )
		{
			velocity = Vector3.MoveTowards( velocity, Vector3.zero, def.artifactSettleSpeed * 8f * dt );
			pos.x += velocity.x * dt;
			pos.z += velocity.z * dt;
			if ( sampler.TrySample( pos, out sample ) && sample.Traversable )
				pos.y = sample.Height;
			else
				pos = t.position;

			t.position = pos;
			item.SyncRigidbodyToTransform();

			if ( velocity.sqrMagnitude <= def.sleepSpeedThreshold * def.sleepSpeedThreshold && sample.Stable )
			{
				body.RestTimer += dt;
				if ( body.RestTimer >= def.sleepRestTime )
					CompleteSettle( ref body, item );
			}
			else
			{
				body.RestTimer = 0f;
			}

			body.Velocity = velocity;
			return;
		}

		// Continuous downhill gravity: NormalX/NormalZ store -dh/dx (horizontal downhill); Flow matches that direction.
		Vector2 downhill;
		float downhillWeight;
		if ( sample.Flow.sqrMagnitude > 0.0001f )
		{
			downhill = sample.Flow;
			downhillWeight = downhill.magnitude;
			downhill /= downhillWeight;
		}
		else
		{
			downhill = new Vector2( sample.Normal.x, sample.Normal.z );
			downhillWeight = downhill.magnitude;
			if ( downhillWeight < 0.0001f )
				downhillWeight = 0f;
			else
				downhill /= downhillWeight;
		}

		if ( downhillWeight > 0.0001f && sample.Slope > def.stableSlopeThreshold * 0.5f )
		{
			float downhillAccel = def.gravity * sample.Slope * speedScale;
			downhillAccel += def.flowAcceleration * sample.Slope * speedScale * 0.35f;
			velocity.x += downhill.x * downhillAccel * dt;
			velocity.z += downhill.y * downhillAccel * dt;
		}

		if ( wobble > 0f && sample.Slope <= def.stableSlopeThreshold )
		{
			float phase = Time.time * 7.3f + item.GetInstanceID() * 0.01f;
			velocity.x += Mathf.Sin( phase ) * wobble * dt;
			velocity.z += Mathf.Cos( phase * 0.87f ) * wobble * dt;
		}

		// Slope-aware friction: steep slopes keep nearly full gravity; flats damp quickly.
		float slopeFactor = Mathf.Clamp01( sample.Slope / Mathf.Max( 0.0001f, def.steepSlopeThreshold ) );
		float effectiveFriction = friction * ( 1f - slopeFactor * 0.92f );
		float speed = new Vector2( velocity.x, velocity.z ).magnitude;
		if ( speed > 0.0001f )
		{
			float decel = effectiveFriction * def.gravity * dt;
			float newSpeed = Mathf.Max( 0f, speed - decel );
			float scale = newSpeed / speed;
			velocity.x *= scale;
			velocity.z *= scale;
			speed = newSpeed;
		}

		float maxSpeed = def.maxSlideSpeed * speedScale;
		if ( speed > maxSpeed && speed > 0.0001f )
		{
			float clampScale = maxSpeed / speed;
			velocity.x *= clampScale;
			velocity.z *= clampScale;
		}

		velocity.y = 0f;

		Vector3 next = pos;
		next.x += velocity.x * dt;
		next.z += velocity.z * dt;

		if ( !sampler.TrySample( next, out TreasureSurfaceSample nextSample ) || !nextSample.Traversable )
		{
			// Slide along the blocked axis instead of reversing (avoids mid-slope wobble).
			Vector3 slideX = new Vector3( next.x, pos.y, pos.z );
			Vector3 slideZ = new Vector3( pos.x, pos.y, next.z );
			bool okX = sampler.TrySample( slideX, out TreasureSurfaceSample sx ) && sx.Traversable;
			bool okZ = sampler.TrySample( slideZ, out TreasureSurfaceSample sz ) && sz.Traversable;

			if ( okX && !okZ )
			{
				next = slideX;
				nextSample = sx;
				velocity.z *= -bounce * 0.25f;
			}
			else if ( okZ && !okX )
			{
				next = slideZ;
				nextSample = sz;
				velocity.x *= -bounce * 0.25f;
			}
			else if ( okX )
			{
				next = slideX;
				nextSample = sx;
			}
			else
			{
				next = pos;
				velocity *= 0.5f;
				if ( !sampler.TrySample( next, out nextSample ) )
				{
					Recover( ref body, item, preferStable: true );
					return;
				}
			}
		}

		sample = nextSample;
		pos = next;
		pos.y = sample.Height;

		// Small slide impulse when snapping down onto a lower cell (always along downhill, never up-pile).
		float drop = t.position.y - pos.y;
		if ( drop > 0.04f && bounce > 0f )
		{
			float boost = Mathf.Min( drop * bounce * 2f, 1.5f );
			Vector2 slideDir = sample.Flow;
			if ( slideDir.sqrMagnitude < 0.0001f )
				slideDir = new Vector2( sample.Normal.x, sample.Normal.z );
			if ( slideDir.sqrMagnitude > 0.0001f )
			{
				slideDir.Normalize();
				velocity.x += slideDir.x * boost;
				velocity.z += slideDir.y * boost;
			}
		}

		t.SetPositionAndRotation( pos, AlignToNormal( t.rotation, sample.Normal, body.Category, dt ) );
		item.SyncRigidbodyToTransform();

		if ( !IsValidPosition( pos, def ) )
		{
			Recover( ref body, item, preferStable: true );
			return;
		}

		float speedSq = velocity.sqrMagnitude;
		float sleepSq = def.sleepSpeedThreshold * def.sleepSpeedThreshold;
		bool canSleep = speedSq <= sleepSq && sample.Stable;
		if ( canSleep )
		{
			body.RestTimer += dt;
			if ( body.RestTimer >= def.sleepRestTime )
			{
				velocity = Vector3.zero;
				CompleteSettle( ref body, item );
			}
		}
		else
		{
			body.RestTimer = 0f;
		}

		body.Velocity = velocity;
	}

	void CompleteSettle( ref SimBody body, TreasureItem item )
	{
		body.Velocity = Vector3.zero;
		body.RestTimer = 0f;

		if ( TryAutoStack( item ) )
		{
			body.Sleeping = true;
			return;
		}

		if ( item != null
			&& TreasurePileLooseDeposit.TryAbsorbLooseItem( item, item.transform.position ) )
		{
			body.Item = null;
			body.Sleeping = true;
			return;
		}

		body.Sleeping = true;
	}

	bool TryAutoStack( TreasureItem item )
	{
		if ( item == null || item.Definition == null )
			return false;

		if ( !GroundCoinStack.IsGroundStackableCoin( item ) )
			return false;

		TreasureSurfaceDefinition def = _world.Definition;
		float radius = def.autoStackRadius;
		float radiusSq = radius * radius;
		float heightTol = def.autoStackHeightTolerance;
		Vector3 pos = item.transform.position;

		// Prefer joining an owned ground stack.
		GroundCoinStack nearestOwned = GroundCoinStack.FindNearest( pos, radius );
		if ( nearestOwned != null && !nearestOwned.IsFull )
		{
			float topY = nearestOwned.ContactPosition.y + nearestOwned.TotalHeight;
			if ( pos.y <= topY + heightTol )
			{
				Unregister( item );
				if ( nearestOwned.TryAbsorbLooseImmediate( item ) )
					return true;

				// Absorb rejected — keep simulating/sleeping as a loose body.
				Register( item, Vector3.zero );
			}
		}

		TreasureItem best = null;
		float bestSq = radiusSq;

		for ( int i = 0; i < _bodies.Count; i++ )
		{
			SimBody other = _bodies[ i ];
			TreasureItem candidate = other.Item;
			if ( candidate == null || candidate == item )
				continue;

			if ( candidate.State != TreasureItemState.SurfaceRolling )
				continue;

			if ( !GroundCoinStack.IsGroundStackableCoin( candidate ) )
				continue;

			Vector3 anchor = candidate.transform.position;
			Vector3 delta = anchor - pos;
			float sq = delta.x * delta.x + delta.z * delta.z;
			if ( sq > bestSq )
				continue;

			if ( pos.y > anchor.y + heightTol )
				continue;

			bestSq = sq;
			best = candidate;
		}

		if ( best == null )
			return false;

		Unregister( item );
		Unregister( best );
		Vector3 endPos = best.transform.position + Vector3.up * TreasureStackSpacing.GetStep( best );
		GroundCoinStack.CreateFromPair(
			best,
			item,
			endPos,
			FlattenUpright( best.transform.rotation ),
			animateIncoming: false );
		return true;
	}

	static readonly List<TreasureItem> AutoStackBuffer = new List<TreasureItem>( 32 );

	static Quaternion FlattenUpright( Quaternion source )
	{
		Vector3 flatForward = Vector3.ProjectOnPlane( source * Vector3.forward, Vector3.up );
		if ( flatForward.sqrMagnitude < 0.0001f )
			flatForward = Vector3.ProjectOnPlane( source * Vector3.right, Vector3.up );
		if ( flatForward.sqrMagnitude < 0.0001f )
			flatForward = Vector3.forward;
		return Quaternion.LookRotation( flatForward.normalized, Vector3.up );
	}

	void Recover( ref SimBody body, TreasureItem item, bool preferStable )
	{
		OutsideBoundsCount++;
		RecoveryCount++;

		Vector3 from = item.transform.position;
		if ( float.IsNaN( from.x ) || float.IsNaN( from.y ) || float.IsNaN( from.z ) )
			from = _world.Definition.worldOrigin;

		if ( !_world.TryFindNearestTraversable( from, out Vector3 dest, out TreasureSurfaceSample sample, preferStable ) )
		{
			dest = _world.Definition.worldOrigin;
			dest.y = _world.Definition.baseHeight;
		}
		else
		{
			dest.y = sample.Height;
		}

		item.transform.SetPositionAndRotation( dest, Quaternion.identity );
		item.SyncRigidbodyToTransform();
		body.Velocity = Vector3.zero;
		body.RestTimer = 0f;
		body.Sleeping = false;
	}

	static bool IsValidPosition( Vector3 pos, TreasureSurfaceDefinition def )
	{
		if ( float.IsNaN( pos.x ) || float.IsNaN( pos.y ) || float.IsNaN( pos.z ) )
			return false;

		if ( pos.y < def.minWorldY || pos.y > def.maxWorldY )
			return false;

		float halfX = def.worldSizeX * 0.5f;
		float halfZ = def.worldSizeZ * 0.5f;
		float lx = pos.x - def.worldOrigin.x;
		float lz = pos.z - def.worldOrigin.z;
		if ( lx < -halfX || lx > halfX || lz < -halfZ || lz > halfZ )
			return false;

		return true;
	}

	static void ResolveMotionParams(
		TreasureCategory category,
		TreasureSurfaceMaterial material,
		TreasureSurfaceDefinition def,
		out float friction,
		out float bounce,
		out float speedScale,
		out float wobble,
		out bool artifact )
	{
		TreasureSurfaceMaterialParams mat = def.GetMaterialParams( material );
		friction = mat.friction;
		bounce = mat.bounce;
		speedScale = mat.rollSpeedScale;
		wobble = 0f;
		artifact = false;

		switch ( category )
		{
			case TreasureCategory.Coin:
				friction *= def.coinFrictionScale;
				bounce *= def.coinBounceScale;
				speedScale *= def.coinSpeedScale;
				break;
			case TreasureCategory.Gem:
				friction *= def.gemFrictionScale;
				bounce *= def.gemBounceScale;
				speedScale *= def.gemSpeedScale;
				wobble = def.gemWobble;
				break;
			default:
				artifact = true;
				break;
		}
	}

	static Quaternion AlignToNormal( Quaternion current, Vector3 normal, TreasureCategory category, float dt )
	{
		if ( normal.sqrMagnitude < 0.0001f )
			normal = Vector3.up;

		Quaternion target = Quaternion.FromToRotation( Vector3.up, normal ) * Quaternion.Euler( 0f, current.eulerAngles.y, 0f );
		float rate = category == TreasureCategory.Gem ? 8f : 12f;
		return Quaternion.Slerp( current, target, 1f - Mathf.Exp( -rate * dt ) );
	}

	public void ResetStats()
	{
		RecoveryCount = 0;
		OutsideBoundsCount = 0;
	}
}
