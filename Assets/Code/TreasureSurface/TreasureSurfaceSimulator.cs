using System.Collections.Generic;
using System.Diagnostics;

using UnityEngine;

/// <summary>
/// Rigidbody-free hopping / rolling / settle / recovery for loose treasure on the surface.
/// </summary>
public sealed class TreasureSurfaceSimulator
{
	struct SimBody
	{
		public TreasureItem Item;
		public Vector3 Velocity;
		public float VerticalVelocity;
		public float HeightAboveSurface;
		public int BouncesRemaining;
		public Vector3 AngularVelocity;
		public Vector3 SmoothedRollAxis;
		public float SeatLift;
		public bool HasLandedOnce;
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

		TreasureSurfaceDefinition def = _world != null ? _world.Definition : null;
		Vector3 flatVel = new Vector3( velocity.x, 0f, velocity.z );
		float impactSpeed = ComputeImpactSpeed( velocity );
		float threshold = def != null ? def.hopImpactSpeedThreshold : 0.35f;

		int bounces = 0;
		Vector3 angular = Vector3.zero;
		if ( impactSpeed >= threshold && def != null )
		{
			bounces = ResolveBounceCount( category, def );
			if ( category == TreasureCategory.Gem )
			{
				Vector3 travel = flatVel.sqrMagnitude > 0.0001f ? flatVel.normalized : Vector3.forward;
				Vector3 rollAxis = Vector3.Cross( Vector3.up, travel );
				if ( rollAxis.sqrMagnitude > 0.0001f )
					angular = rollAxis.normalized * ( impactSpeed * def.gemRollAngularScale );
			}
		}

		_bodies.Add( new SimBody
		{
			Item = item,
			Velocity = flatVel,
			VerticalVelocity = velocity.y,
			HeightAboveSurface = 0f,
			BouncesRemaining = bounces,
			AngularVelocity = angular,
			SmoothedRollAxis = angular.sqrMagnitude > 0.0001f ? angular.normalized : Vector3.zero,
			// Stable seat radius — must not depend on tumbling rotation or Y jitters every frame.
			SeatLift = TreasureSurfaceSeat.GetStableContactLift( item ),
			HasLandedOnce = false,
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
			body.VerticalVelocity = velocity.y;
			body.HeightAboveSurface = 0f;
			body.HasLandedOnce = false;
			body.SeatLift = TreasureSurfaceSeat.GetStableContactLift( item );
			body.SmoothedRollAxis = Vector3.zero;
			TreasureSurfaceDefinition def = _world != null ? _world.Definition : null;
			float impactSpeed = ComputeImpactSpeed( velocity );
			float threshold = def != null ? def.hopImpactSpeedThreshold : 0.35f;
			body.BouncesRemaining = 0;
			if ( impactSpeed >= threshold && def != null )
				body.BouncesRemaining = ResolveBounceCount( body.Category, def );

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
			body.VerticalVelocity = 0f;
			body.HeightAboveSurface = 0f;
			body.BouncesRemaining = 0;
			body.AngularVelocity = Vector3.zero;
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
			// Multi-remove (auto-stack / absorb nearby) can shrink Count by >1.
			if ( i >= _bodies.Count )
				continue;

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
			// SimulateBody may Unregister/Register (auto-stack), shifting or removing this slot.
			TryWriteBackBody( item, ref body );
			rollMsAccum += ( float )( ( sw.ElapsedTicks - start ) * ( 1000.0 / Stopwatch.Frequency ) );
			rollSamples++;

			if ( i > _bodies.Count )
				i = _bodies.Count;
		}

		ActiveCount = active;
		SleepingCount = sleeping;
		AverageRollingMs = rollSamples > 0 ? rollMsAccum / rollSamples : 0f;
	}

	void TryWriteBackBody( TreasureItem item, ref SimBody body )
	{
		if ( item == null )
			return;

		for ( int j = 0; j < _bodies.Count; j++ )
		{
			if ( _bodies[ j ].Item != item )
				continue;

			_bodies[ j ] = body;
			return;
		}
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

		bool airborne = body.HeightAboveSurface > 0.001f || body.VerticalVelocity > 0.01f;

		// First surface contact: spend a bounce as an upward hop + flip/roll impulse.
		if ( !airborne && !body.HasLandedOnce && body.BouncesRemaining > 0 )
		{
			float impactSpeed = ComputeImpactSpeed( new Vector3( velocity.x, body.VerticalVelocity, velocity.z ) );
			if ( impactSpeed >= def.hopImpactSpeedThreshold )
				ApplySurfaceBounce( ref body, ref velocity, sample.Normal, def, impactSpeed );
		}

		airborne = body.HeightAboveSurface > 0.001f || body.VerticalVelocity > 0.01f;

		// Continuous downhill gravity while grounded (airborne keeps XZ momentum).
		if ( !airborne )
		{
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

			if ( wobble > 0f
				&& body.Category != TreasureCategory.Gem
				&& sample.Slope <= def.stableSlopeThreshold )
			{
				float phase = Time.time * 7.3f + item.GetInstanceID() * 0.01f;
				velocity.x += Mathf.Sin( phase ) * wobble * dt;
				velocity.z += Mathf.Cos( phase * 0.87f ) * wobble * dt;
			}

			// Slope-aware friction: steep slopes keep nearly full gravity; flats damp quickly.
			float slopeFactor = Mathf.Clamp01( sample.Slope / Mathf.Max( 0.0001f, def.steepSlopeThreshold ) );
			float slopeFrictionBlend = artifact ? 0.35f : 0.92f;
			float effectiveFriction = friction * ( 1f - slopeFactor * slopeFrictionBlend );
			float speed = new Vector2( velocity.x, velocity.z ).magnitude;
			if ( speed > 0.0001f )
			{
				float decel = effectiveFriction * def.gravity * dt;

				// Sliding against painted flow (uphill) is heavily resisted.
				if ( def.flowUphillResistance > 0f && sample.Flow.sqrMagnitude > 0.0001f )
				{
					Vector2 velDir = new Vector2( velocity.x, velocity.z ) / speed;
					Vector2 flowDir = sample.Flow;
					float flowMag = flowDir.magnitude;
					flowDir /= flowMag;
					float flowDot = Vector2.Dot( velDir, flowDir );
					if ( flowDot < -0.05f )
					{
						float againstStrength = Mathf.Clamp01( -flowDot );
						decel += def.flowUphillResistance * friction * def.gravity * againstStrength * dt;
					}
				}

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
		}
		else
		{
			// Light air drag on horizontal speed.
			velocity.x *= 1f - Mathf.Clamp01( 0.35f * dt );
			velocity.z *= 1f - Mathf.Clamp01( 0.35f * dt );
		}

		velocity.y = 0f;

		Vector3 next = pos;
		next.x += velocity.x * dt;
		next.z += velocity.z * dt;

		if ( !sampler.TrySample( next, out TreasureSurfaceSample nextSample ) || !nextSample.Traversable )
		{
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

		if ( body.Category == TreasureCategory.Gem )
		{
			ApplyGemPush( ref pos, ref velocity, item, def, sampler );
			if ( sampler.TrySample( pos, out TreasureSurfaceSample pushedSample ) && pushedSample.Traversable )
				sample = pushedSample;
		}

		float contactY = sample.Height + body.SeatLift;

		if ( airborne || body.VerticalVelocity != 0f || body.HeightAboveSurface > 0f )
		{
			body.VerticalVelocity -= def.gravity * dt;
			body.HeightAboveSurface += body.VerticalVelocity * dt;
			if ( body.HeightAboveSurface <= 0f )
			{
				body.HeightAboveSurface = 0f;
				float impactDown = -body.VerticalVelocity;
				if ( body.BouncesRemaining > 0 && impactDown > 0.5f )
				{
					float impactSpeed = Mathf.Max( impactDown, new Vector2( velocity.x, velocity.z ).magnitude );
					ApplySurfaceBounce( ref body, ref velocity, sample.Normal, def, impactSpeed );
				}
				else
				{
					body.VerticalVelocity = 0f;
					body.BouncesRemaining = 0;
					body.HasLandedOnce = true;

					// Small slide impulse when snapping down onto a lower cell.
					float drop = t.position.y - contactY;
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
				}
			}
		}

		pos.y = contactY + Mathf.Max( 0f, body.HeightAboveSurface );

		Quaternion rot = IntegrateRotation( ref body, t.rotation, sample.Normal, velocity, def, dt );
		float minContactY = TreasureSurfaceSeat.GetContactY( item, sample, rot );
		if ( pos.y < minContactY )
			pos.y = minContactY;

		t.SetPositionAndRotation( pos, rot );
		item.SyncRigidbodyToTransform();

		if ( !IsValidPosition( pos, def ) )
		{
			Recover( ref body, item, preferStable: true );
			return;
		}

		bool stillAirborne = body.HeightAboveSurface > 0.001f || Mathf.Abs( body.VerticalVelocity ) > 0.05f;
		float speedSq = velocity.sqrMagnitude + body.VerticalVelocity * body.VerticalVelocity;
		float sleepSq = def.sleepSpeedThreshold * def.sleepSpeedThreshold;
		bool canSleep = !stillAirborne
			&& body.BouncesRemaining <= 0
			&& speedSq <= sleepSq
			&& sample.Stable
			&& body.AngularVelocity.sqrMagnitude <= 0.25f;

		if ( canSleep )
		{
			body.RestTimer += dt;
			if ( body.RestTimer >= def.sleepRestTime )
			{
				velocity = Vector3.zero;
				body.AngularVelocity = Vector3.zero;
				CompleteSettle( ref body, item );
			}
		}
		else
		{
			body.RestTimer = 0f;
		}

		body.Velocity = velocity;
	}

	void ApplySurfaceBounce(
		ref SimBody body,
		ref Vector3 velocity,
		Vector3 normal,
		TreasureSurfaceDefinition def,
		float impactSpeed )
	{
		if ( body.BouncesRemaining <= 0 )
			return;

		float restitution = body.Category == TreasureCategory.Coin
			? def.coinBounceRestitution
			: body.Category == TreasureCategory.Gem
				? def.gemBounceRestitution
				: def.artifactBounceRestitution;
		restitution = Mathf.Max( 0.05f, restitution );

		float hopCap = body.Category == TreasureCategory.Coin || body.Category == TreasureCategory.Gem ? 4.5f : 1.6f;
		float hopMin = body.Category == TreasureCategory.Coin || body.Category == TreasureCategory.Gem ? 0.8f : 0.25f;
		float hop = Mathf.Clamp( impactSpeed * restitution, hopMin, hopCap );
		body.VerticalVelocity = hop;
		body.HeightAboveSurface = Mathf.Max( body.HeightAboveSurface, 0.002f );
		body.BouncesRemaining--;
		body.HasLandedOnce = true;

		Vector3 travel = new Vector3( velocity.x, 0f, velocity.z );
		if ( travel.sqrMagnitude < 0.0001f )
			travel = Vector3.forward;
		else
			travel.Normalize();

		if ( body.Category == TreasureCategory.Coin )
		{
			// Flip around axis perpendicular to travel (coin tumble).
			Vector3 flipAxis = Vector3.Cross( Vector3.up, travel );
			if ( flipAxis.sqrMagnitude < 0.0001f )
				flipAxis = Vector3.right;
			else
				flipAxis.Normalize();
			body.AngularVelocity = flipAxis * def.coinPostBounceFlipSpins;
		}
		else if ( body.Category == TreasureCategory.Gem )
		{
			Vector3 n = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
			Vector3 rollAxis = Vector3.Cross( n, travel );
			if ( rollAxis.sqrMagnitude < 0.0001f )
				rollAxis = Vector3.right;
			else
				rollAxis.Normalize();
			body.AngularVelocity = rollAxis * ( impactSpeed * def.gemRollAngularScale );
		}
		else
		{
			body.AngularVelocity = Vector3.zero;
		}
	}

	Quaternion IntegrateRotation(
		ref SimBody body,
		Quaternion current,
		Vector3 normal,
		Vector3 velocity,
		TreasureSurfaceDefinition def,
		float dt )
	{
		if ( body.Category == TreasureCategory.Gem )
		{
			Vector3 travel = new Vector3( velocity.x, 0f, velocity.z );
			float speed = travel.magnitude;
			const float rollSpeedGate = 0.08f;

			if ( body.HeightAboveSurface <= 0.001f && speed > rollSpeedGate )
			{
				Vector3 n = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
				Vector3 desiredAxis = Vector3.Cross( n, travel / speed );
				if ( desiredAxis.sqrMagnitude > 0.0001f )
				{
					desiredAxis.Normalize();
					if ( body.SmoothedRollAxis.sqrMagnitude < 0.0001f )
						body.SmoothedRollAxis = desiredAxis;
					else
					{
						if ( Vector3.Dot( body.SmoothedRollAxis, desiredAxis ) < 0f )
							desiredAxis = -desiredAxis;
						body.SmoothedRollAxis = Vector3.Slerp(
							body.SmoothedRollAxis,
							desiredAxis,
							1f - Mathf.Exp( -8f * dt ) ).normalized;
					}

					float targetSpin = speed * def.gemRollAngularScale;
					body.AngularVelocity = body.SmoothedRollAxis * targetSpin;
				}
			}
			else
			{
				body.AngularVelocity *= Mathf.Exp( -def.gemAngularDamping * dt );
				if ( speed <= rollSpeedGate )
					body.SmoothedRollAxis = Vector3.Lerp( body.SmoothedRollAxis, Vector3.zero, 1f - Mathf.Exp( -6f * dt ) );
			}

			if ( body.AngularVelocity.sqrMagnitude < 0.04f )
			{
				body.AngularVelocity = Vector3.zero;
				return current;
			}

			float angSpeed = body.AngularVelocity.magnitude;
			return Quaternion.AngleAxis( angSpeed * Mathf.Rad2Deg * dt, body.AngularVelocity / angSpeed ) * current;
		}

		// Coins: tumble while hopping, then flatten to the surface while sliding.
		if ( body.HeightAboveSurface > 0.001f && body.AngularVelocity.sqrMagnitude > 0.0001f )
		{
			float angSpeed = body.AngularVelocity.magnitude;
			Quaternion spun = Quaternion.AngleAxis( angSpeed * Mathf.Rad2Deg * dt, body.AngularVelocity / angSpeed ) * current;
			body.AngularVelocity *= Mathf.Exp( -1.5f * dt );
			return spun;
		}

		body.AngularVelocity = Vector3.MoveTowards( body.AngularVelocity, Vector3.zero, 20f * dt );
		return AlignToNormal( current, normal, body.Category, dt );
	}

	void ApplyGemPush(
		ref Vector3 pos,
		ref Vector3 velocity,
		TreasureItem item,
		TreasureSurfaceDefinition def,
		TreasureSurfaceSampler sampler )
	{
		float selfRadius = Mathf.Max( def.gemPushRadius, TreasureSurfaceSeat.EstimatePushRadius( item, def.gemPushRadius ) );
		float strength = def.gemPushStrength;
		if ( strength <= 0f )
			return;

		Vector3 push = Vector3.zero;

		for ( int i = 0; i < _bodies.Count; i++ )
		{
			SimBody other = _bodies[ i ];
			TreasureItem otherItem = other.Item;
			if ( otherItem == null || otherItem == item )
				continue;

			if ( otherItem.State != TreasureItemState.SurfaceRolling )
				continue;

			Vector3 otherPos = otherItem.transform.position;
			Vector3 delta = pos - otherPos;
			delta.y = 0f;
			float distSq = delta.sqrMagnitude;
			if ( distSq < 0.0000001f )
			{
				push.x += ( ( item.GetInstanceID() & 1 ) == 0 ? 1f : -1f ) * 0.02f;
				continue;
			}

			float otherRadius;
			if ( other.Category == TreasureCategory.Gem )
				otherRadius = Mathf.Max( def.gemPushRadius, TreasureSurfaceSeat.EstimatePushRadius( otherItem, def.gemPushRadius ) );
			else if ( other.Category == TreasureCategory.Coin )
				otherRadius = TreasureSurfaceSeat.EstimatePushRadius( otherItem, 0.08f ) * def.gemVsCoinPushRadiusScale;
			else
				continue;

			float minDist = selfRadius + otherRadius;
			if ( distSq >= minDist * minDist )
				continue;

			float dist = Mathf.Sqrt( distSq );
			float overlap = minDist - dist;
			Vector3 dirPush = delta / dist;
			push += dirPush * overlap;
		}

		IReadOnlyList<GroundCoinStack> stacks = GroundCoinStack.ActiveStacks;
		for ( int i = 0; i < stacks.Count; i++ )
		{
			GroundCoinStack stack = stacks[ i ];
			if ( stack == null || stack.Count <= 0 )
				continue;

			Vector3 stackPos = stack.ContactPosition;
			Vector3 delta = pos - stackPos;
			delta.y = 0f;
			float distSq = delta.sqrMagnitude;
			float minDist = selfRadius + stack.FootprintRadius * def.gemVsCoinPushRadiusScale;
			if ( distSq >= minDist * minDist )
				continue;

			if ( distSq < 0.0000001f )
			{
				push.x += 0.02f;
				continue;
			}

			float dist = Mathf.Sqrt( distSq );
			float overlap = minDist - dist;
			push += ( delta / dist ) * overlap;
		}

		if ( push.sqrMagnitude < 0.0000001f )
			return;

		// Prefer positional separation; keep velocity impulse small so roll axis stays stable.
		float overlapMag = push.magnitude;
		Vector3 dir = push / overlapMag;
		float maxStep = Mathf.Min( overlapMag * 0.55f, 0.08f );
		pos.x += dir.x * maxStep;
		pos.z += dir.z * maxStep;

		float impulse = Mathf.Min( overlapMag * strength * 0.12f, 1.2f );
		velocity.x += dir.x * impulse;
		velocity.z += dir.z * impulse;

		if ( !sampler.TrySample( pos, out TreasureSurfaceSample pushedSample ) || !pushedSample.Traversable )
		{
			pos.x -= dir.x * maxStep;
			pos.z -= dir.z * maxStep;
			velocity.x -= dir.x * impulse;
			velocity.z -= dir.z * impulse;
		}
	}

	void CompleteSettle( ref SimBody body, TreasureItem item )
	{
		body.Velocity = Vector3.zero;
		body.VerticalVelocity = 0f;
		body.HeightAboveSurface = 0f;
		body.AngularVelocity = Vector3.zero;
		body.BouncesRemaining = 0;
		body.RestTimer = 0f;

		if ( TryAutoStack( item ) )
		{
			body.Sleeping = true;
			return;
		}

		if ( item != null
			&& TreasurePileLooseDeposit.TryAbsorbLooseItem( item, item.transform.position ) )
		{
			Unregister( item );
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

		GroundCoinStack nearestOwned = GroundCoinStack.FindNearest( pos, radius );
		if ( nearestOwned != null && !nearestOwned.IsFull )
		{
			float topY = nearestOwned.ContactPosition.y + nearestOwned.TotalHeight;
			if ( pos.y <= topY + heightTol )
			{
				Unregister( item );
				if ( nearestOwned.TryAbsorbLooseImmediate( item ) )
					return true;

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
			TreasureOrientation.FlattenUpright( best.transform.rotation ),
			animateIncoming: true );
		return true;
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
			dest.y = sample.Height + TreasureSurfaceSeat.GetStableContactLift( item );
		}

		item.transform.SetPositionAndRotation( dest, Quaternion.identity );
		item.SyncRigidbodyToTransform();
		body.Velocity = Vector3.zero;
		body.VerticalVelocity = 0f;
		body.HeightAboveSurface = 0f;
		body.BouncesRemaining = 0;
		body.AngularVelocity = Vector3.zero;
		body.SmoothedRollAxis = Vector3.zero;
		body.SeatLift = TreasureSurfaceSeat.GetStableContactLift( item );
		body.RestTimer = 0f;
		body.Sleeping = false;
	}

	static int ResolveBounceCount( TreasureCategory category, TreasureSurfaceDefinition def )
	{
		switch ( category )
		{
			case TreasureCategory.Coin:
				return Mathf.Max( 0, def.coinMaxBounces );
			case TreasureCategory.Gem:
				return Mathf.Max( 0, def.gemMaxBounces );
			default:
				return Mathf.Max( 0, def.artifactMaxBounces );
		}
	}

	static float ComputeImpactSpeed( Vector3 velocity )
	{
		return velocity.magnitude;
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
				friction *= def.artifactFrictionScale;
				speedScale *= def.artifactSpeedScale;
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
