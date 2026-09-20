using UnityEngine;

/// <summary>
/// Idle float for a small lantern. Travel is heading-based with a capped turn rate,
/// so the path is always a curve — never a straight cut or a reverse.
/// Add to the lantern object that should move (not a static post). Motion runs in
/// LateUpdate so Feedbacks on the same object cannot pin the transform.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[DefaultExecutionOrder( 200 )]
[AddComponentMenu( "DragonLoot/Lantern/Magical Lantern Motion" )]
public class MagicalLanternMotion : MonoBehaviour
{
	[Header( "Feel" )]
	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Scales every offset. 1 = authored defaults, 0 = rest pose." )]
	float motionScale = 1f;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Scales cruise speed and orbit rate. 1 = authored defaults." )]
	float speedScale = 1f;

	[SerializeField]
	[Tooltip( "When a LanternActivator is present, motion eases in as the lantern lights." )]
	bool scaleMotionWithLit = true;

	[SerializeField]
	[Range( 0f, 1f )]
	[Tooltip( "Motion amount while fully unlit. Tiny residual life, then bloom when ignited." )]
	float unlitMotionScale = 0.12f;

	[SerializeField]
	[Min( 0.05f )]
	float wakeDuration = 1.35f;

	[Header( "Orbit" )]
	[SerializeField]
	[Tooltip( "Rounded ellipse the lantern lazily traces (X and Z). No crossing, so no hard corners." )]
	Vector2 orbitRadius = new Vector2( 0.08f, 0.07f );

	[SerializeField]
	[Min( 0.01f )]
	float orbitSpeed = 0.11f;

	[Header( "Wander" )]
	[SerializeField]
	[Tooltip( "Slow Perlin drift of the orbit center, in local meters." )]
	Vector3 wanderRadius = new Vector3( 0.08f, 0.035f, 0.08f );

	[SerializeField]
	[Min( 0.01f )]
	float wanderSpeed = 0.18f;

	[Header( "Steering" )]
	[SerializeField]
	[Min( 0.01f )]
	[Tooltip( "How fast the lantern glides along its heading, in meters per second." )]
	float cruiseSpeed = 0.09f;

	[SerializeField]
	[Min( 1f )]
	[Tooltip( "Max heading change in degrees per second. Lower = wider, softer curves." )]
	float maxTurnRate = 48f;

	[SerializeField]
	[Min( 1f )]
	[Tooltip( "How quickly turn rate can ramp up. Stops curvature from snapping on." )]
	float turnAccel = 70f;

	[SerializeField]
	[Min( 0.05f )]
	[Tooltip( "Seconds of attractor motion to chase ahead, so the lantern arcs into the path." )]
	float lookAhead = 0.45f;

	[SerializeField]
	[Min( 0.01f )]
	float speedAccel = 0.14f;

	[Header( "Bob" )]
	[SerializeField]
	[Min( 0f )]
	float bobAmplitude = 0.055f;

	[SerializeField]
	[Min( 0.01f )]
	[Tooltip( "Cycles per second. Slow reads more magical than a bounce." )]
	float bobSpeed = 0.52f;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Rare extra lift when the slow noise crests — like the spell tugs upward." )]
	float liftAmplitude = 0.04f;

	[SerializeField]
	[Min( 0.01f )]
	float liftSpeed = 0.16f;

	[Header( "Flutter" )]
	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Tiny high-frequency wobble so the path never looks mechanical." )]
	float flutterAmplitude = 0.01f;

	[SerializeField]
	[Min( 0.01f )]
	float flutterSpeed = 2.35f;

	[Header( "Tilt" )]
	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Degrees of lean per meter of horizontal offset. Hanging-from-magic feel." )]
	float tiltDegreesPerMeter = 38f;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Extra lean from horizontal speed." )]
	float tiltFromVelocity = 7f;

	[SerializeField]
	[Min( 0f )]
	float wobbleDegrees = 1.8f;

	[SerializeField]
	[Min( 0f )]
	float yawAmplitude = 14f;

	[SerializeField]
	[Min( 0.01f )]
	float yawSpeed = 0.16f;

	[SerializeField]
	[Min( 0.1f )]
	float tiltLerp = 5.5f;

	[Header( "Preview" )]
	[SerializeField]
	[Tooltip( "Animate in the editor outside play mode. Disable before moving the lantern." )]
	bool playInEditMode;

	[SerializeField]
	[HideInInspector]
	Vector3 restLocalPosition;

	[SerializeField]
	[HideInInspector]
	Quaternion restLocalRotation = Quaternion.identity;

	[SerializeField]
	[HideInInspector]
	bool hasRestPose;

	const float ArriveDistance = 0.025f;
	const float MinHeadingSqr = 0.0001f;
	const float ReverseDot = -0.15f;

	LanternActivator _activator;
	Vector3 _offset;
	Vector3 _heading;
	Vector3 _prevAttractor;
	Vector3 _steerSide;
	Quaternion _tilt = Quaternion.identity;
	float _speed;
	float _turnRate;
	float _noiseSeed;
	float _wakeElapsed;
	bool _appliedPose;
	bool _hasAttractor;

	public bool PlayInEditMode
	{
		get => playInEditMode;
		set => playInEditMode = value;
	}

	public Vector3 RestLocalPosition => restLocalPosition;

	public Quaternion RestLocalRotation => restLocalRotation;

	public bool HasRestPose => hasRestPose;

	void OnEnable()
	{
		_activator = GetComponent<LanternActivator>();
		_noiseSeed = HashSeed( GetInstanceID() );
		ResetMotionState();
		if ( !hasRestPose )
			CaptureRestPose();

#if UNITY_EDITOR
		if ( !Application.isPlaying )
		{
			if ( playInEditMode )
				ApplyMotion( immediate: true );
			return;
		}
#endif

		transform.localPosition = restLocalPosition;
		transform.localRotation = restLocalRotation;
		_appliedPose = true;
	}

	void OnDisable()
	{
		RestoreAuthoredPose();
	}

	void Reset()
	{
		CaptureRestPose();
	}

	void OnValidate()
	{
		motionScale = Mathf.Max( 0f, motionScale );
		speedScale = Mathf.Max( 0f, speedScale );
		unlitMotionScale = Mathf.Clamp01( unlitMotionScale );
		wakeDuration = Mathf.Max( 0.05f, wakeDuration );
		orbitSpeed = Mathf.Max( 0.01f, orbitSpeed );
		wanderSpeed = Mathf.Max( 0.01f, wanderSpeed );
		cruiseSpeed = Mathf.Max( 0.01f, cruiseSpeed );
		maxTurnRate = Mathf.Max( 1f, maxTurnRate );
		turnAccel = Mathf.Max( 1f, turnAccel );
		lookAhead = Mathf.Max( 0.05f, lookAhead );
		speedAccel = Mathf.Max( 0.01f, speedAccel );
		bobSpeed = Mathf.Max( 0.01f, bobSpeed );
		liftSpeed = Mathf.Max( 0.01f, liftSpeed );
		flutterSpeed = Mathf.Max( 0.01f, flutterSpeed );
		tiltDegreesPerMeter = Mathf.Max( 0f, tiltDegreesPerMeter );
		tiltFromVelocity = Mathf.Max( 0f, tiltFromVelocity );
		wobbleDegrees = Mathf.Max( 0f, wobbleDegrees );
		yawAmplitude = Mathf.Max( 0f, yawAmplitude );
		yawSpeed = Mathf.Max( 0.01f, yawSpeed );
		tiltLerp = Mathf.Max( 0.1f, tiltLerp );
	}

	void LateUpdate()
	{
#if UNITY_EDITOR
		if ( !Application.isPlaying && !playInEditMode )
		{
			SyncRestFromTransformIfIdle();
			return;
		}
#endif
		ApplyMotion( immediate: false );
	}

	[ContextMenu( "Recapture Rest Pose" )]
	public void RecaptureRestPose()
	{
		RestoreAuthoredPose();
		CaptureRestPose();
		ResetMotionState();
		if ( Application.isPlaying || playInEditMode )
			ApplyMotion( immediate: true );
	}

	public void RestoreAuthoredPose()
	{
		if ( !hasRestPose || !_appliedPose )
			return;

		transform.localPosition = restLocalPosition;
		transform.localRotation = restLocalRotation;
		_appliedPose = false;
		ResetMotionState();

#if UNITY_EDITOR
		if ( !Application.isPlaying )
		{
			UnityEditor.EditorUtility.ClearDirty( transform );
			UnityEditor.EditorUtility.ClearDirty( gameObject );
		}
#endif
	}

	void ResetMotionState()
	{
		_offset = Vector3.zero;
		_heading = Vector3.zero;
		_prevAttractor = Vector3.zero;
		_steerSide = Vector3.zero;
		_tilt = Quaternion.identity;
		_speed = 0f;
		_turnRate = 0f;
		_wakeElapsed = 0f;
		_hasAttractor = false;
	}

	void CaptureRestPose()
	{
		restLocalPosition = transform.localPosition;
		restLocalRotation = transform.localRotation;
		hasRestPose = true;
	}

	void SyncRestFromTransformIfIdle()
	{
		if ( !hasRestPose )
		{
			CaptureRestPose();
			return;
		}

		if ( ( transform.localPosition - restLocalPosition ).sqrMagnitude > 0.000001f
			|| Quaternion.Angle( transform.localRotation, restLocalRotation ) > 0.05f )
			CaptureRestPose();
	}

	void ApplyMotion( bool immediate )
	{
		if ( !hasRestPose )
			CaptureRestPose();

		float dt = GetDeltaTime();
		float time = GetMotionTime();
		float amount = ResolveMotionAmount( dt, immediate );
		Vector3 attractor = EvaluateAttractor( time ) * amount;
		Vector3 tangent = EvaluateOrbitTangent( time );
		EnsureHeading( tangent );

		if ( immediate )
		{
			_offset = attractor;
			_heading = tangent.sqrMagnitude > MinHeadingSqr ? tangent.normalized : _heading;
			_speed = cruiseSpeed * Mathf.Max( speedScale, 0.01f ) * amount;
			_turnRate = 0f;
			_prevAttractor = attractor;
			_hasAttractor = true;
		}
		else
			SteerAlongCurve( attractor, tangent, amount, dt );

		Vector3 velocity = _heading * _speed;
		Vector3 offset = _offset;
		offset.y += EvaluateBob( time ) * amount;
		offset.y += EvaluateLift( time ) * amount;
		offset += EvaluateFlutter( time ) * amount;

		Quaternion desiredTilt = EvaluateTilt( time, offset, velocity, amount );
		if ( immediate || tiltLerp <= 0.1f )
			_tilt = desiredTilt;
		else
		{
			float t = 1f - Mathf.Exp( -tiltLerp * dt );
			_tilt = Quaternion.Slerp( _tilt, desiredTilt, t );
		}

		transform.localPosition = restLocalPosition + offset;
		transform.localRotation = restLocalRotation * _tilt;
		_appliedPose = true;

#if UNITY_EDITOR
		if ( !Application.isPlaying )
		{
			UnityEditor.EditorUtility.ClearDirty( transform );
			UnityEditor.EditorUtility.ClearDirty( gameObject );
		}
#endif
	}

	void SteerAlongCurve( Vector3 attractor, Vector3 tangent, float amount, float dt )
	{
		Vector3 seekPoint = attractor;
		if ( _hasAttractor && dt > 0.0001f )
		{
			Vector3 attractorVel = ( attractor - _prevAttractor ) / dt;
			seekPoint += attractorVel * lookAhead;
		}

		_prevAttractor = attractor;
		_hasAttractor = true;

		Vector3 toSeek = seekPoint - _offset;
		Vector3 desiredHeading;
		if ( toSeek.sqrMagnitude < ArriveDistance * ArriveDistance )
			desiredHeading = tangent.sqrMagnitude > MinHeadingSqr ? tangent.normalized : _heading;
		else
			desiredHeading = toSeek.normalized;

		desiredHeading = KeepInsideRadius( desiredHeading );
		desiredHeading = AvoidReversal( desiredHeading, tangent );
		if ( desiredHeading.sqrMagnitude < MinHeadingSqr )
			desiredHeading = _heading;

		float angle = Vector3.Angle( _heading, desiredHeading );
		float desiredTurnRate = angle > 0.35f ? maxTurnRate : 0f;
		_turnRate = Mathf.MoveTowards( _turnRate, desiredTurnRate, turnAccel * dt );
		float maxRadians = _turnRate * Mathf.Deg2Rad * dt;
		_heading = Vector3.RotateTowards( _heading, desiredHeading, maxRadians, 0f );
		if ( _heading.sqrMagnitude < MinHeadingSqr )
			_heading = desiredHeading;
		else
			_heading.Normalize();

		float align = Vector3.Dot( _heading, desiredHeading );
		float align01 = Mathf.Clamp01( align * 0.5f + 0.5f );
		float targetSpeed = cruiseSpeed * Mathf.Max( speedScale, 0.01f ) * amount * Mathf.Lerp( 0.62f, 1f, align01 );
		if ( amount > 0.02f )
			targetSpeed = Mathf.Max( targetSpeed, cruiseSpeed * 0.35f * amount );
		_speed = Mathf.MoveTowards( _speed, targetSpeed, speedAccel * dt );
		_offset += _heading * _speed * dt;
	}

	Vector3 KeepInsideRadius( Vector3 desiredHeading )
	{
		float maxRadius = MaxTravelRadius();
		float dist = _offset.magnitude;
		float inner = maxRadius * 0.82f;
		if ( dist <= inner || maxRadius <= 0.0001f )
			return desiredHeading;

		Vector3 home = -_offset;
		if ( home.sqrMagnitude < MinHeadingSqr )
			return desiredHeading;

		home.Normalize();
		float over = Mathf.Clamp01( ( dist - inner ) / Mathf.Max( maxRadius * 0.35f, 0.01f ) );
		return Vector3.Slerp( desiredHeading, home, over ).normalized;
	}

	Vector3 AvoidReversal( Vector3 desiredHeading, Vector3 tangent )
	{
		if ( _heading.sqrMagnitude < MinHeadingSqr )
			return desiredHeading;
		if ( Vector3.Dot( _heading, desiredHeading ) >= ReverseDot )
			return desiredHeading;

		Vector3 side = Vector3.Cross( _heading, Vector3.up );
		if ( side.sqrMagnitude < MinHeadingSqr )
			side = Vector3.Cross( _heading, Vector3.right );
		side.Normalize();

		if ( _steerSide.sqrMagnitude < MinHeadingSqr )
		{
			if ( tangent.sqrMagnitude > MinHeadingSqr && Vector3.Dot( side, tangent ) < 0f )
				side = -side;
			_steerSide = side;
		}
		else if ( Vector3.Dot( side, _steerSide ) < 0f )
			side = -side;

		return ( desiredHeading + side * 1.15f ).normalized;
	}

	void EnsureHeading( Vector3 tangent )
	{
		if ( _heading.sqrMagnitude >= MinHeadingSqr )
			return;

		if ( tangent.sqrMagnitude >= MinHeadingSqr )
			_heading = tangent.normalized;
		else
			_heading = Vector3.forward;
	}

	float ResolveMotionAmount( float dt, bool immediate )
	{
		float amount = motionScale;
		if ( scaleMotionWithLit && _activator != null )
			amount *= Mathf.Lerp( unlitMotionScale, 1f, Mathf.Clamp01( _activator.CurrentLitT ) );

		if ( immediate )
		{
			_wakeElapsed = wakeDuration;
			return amount;
		}

		if ( _wakeElapsed < wakeDuration )
			_wakeElapsed += dt;

		float wakeU = Mathf.Clamp01( _wakeElapsed / wakeDuration );
		float wake = 1f - ( 1f - wakeU ) * ( 1f - wakeU ) * ( 1f - wakeU );
		return amount * wake;
	}

	Vector3 EvaluateAttractor( float time )
	{
		return EvaluateWander( time ) + EvaluateOrbit( time );
	}

	Vector3 EvaluateWander( float time )
	{
		float speed = wanderSpeed * speedScale;
		float nx = SignedNoise( time * speed, _noiseSeed );
		float ny = SignedNoise( time * speed * 0.83f, _noiseSeed + 17.3f );
		float nz = SignedNoise( time * speed * 1.17f, _noiseSeed + 41.7f );
		return new Vector3( nx * wanderRadius.x, ny * wanderRadius.y, nz * wanderRadius.z );
	}

	Vector3 EvaluateOrbit( float time )
	{
		float phase = OrbitPhase( time );
		return new Vector3(
			Mathf.Cos( phase ) * orbitRadius.x,
			Mathf.Sin( phase * 0.5f + 0.7f ) * orbitRadius.y * 0.28f,
			Mathf.Sin( phase ) * orbitRadius.y );
	}

	Vector3 EvaluateOrbitTangent( float time )
	{
		float phase = OrbitPhase( time );
		Vector3 tangent = new Vector3(
			-Mathf.Sin( phase ) * orbitRadius.x,
			Mathf.Cos( phase * 0.5f + 0.7f ) * orbitRadius.y * 0.14f,
			Mathf.Cos( phase ) * orbitRadius.y );
		if ( tangent.sqrMagnitude < MinHeadingSqr )
			return Vector3.forward;
		return tangent.normalized;
	}

	float OrbitPhase( float time )
	{
		return time * orbitSpeed * speedScale * Mathf.PI * 2f + _noiseSeed;
	}

	float MaxTravelRadius()
	{
		float orbit = Mathf.Max( orbitRadius.x, orbitRadius.y );
		return wanderRadius.magnitude + orbit;
	}

	float EvaluateBob( float time )
	{
		if ( bobAmplitude <= 0.0001f )
			return 0f;

		float phase = time * bobSpeed * speedScale * Mathf.PI * 2f + _noiseSeed;
		float u = Mathf.Sin( phase ) * 0.5f + 0.5f;
		float highBias = Mathf.Pow( u, 0.65f );
		float primary = ( highBias * 2f - 1f ) * bobAmplitude;
		float secondary = Mathf.Sin( phase * 0.41f + 1.7f ) * bobAmplitude * 0.32f;
		return primary + secondary;
	}

	float EvaluateLift( float time )
	{
		if ( liftAmplitude <= 0.0001f )
			return 0f;

		float n = Mathf.PerlinNoise( time * liftSpeed * speedScale + _noiseSeed + 55f, _noiseSeed * 0.29f + 8.2f );
		float crest = n * 2f - 1.18f;
		if ( crest <= 0f )
			return 0f;
		return crest * liftAmplitude;
	}

	Vector3 EvaluateFlutter( float time )
	{
		if ( flutterAmplitude <= 0.0001f )
			return Vector3.zero;

		float speed = flutterSpeed * speedScale;
		float fx = SignedNoise( time * speed, _noiseSeed + 8f );
		float fy = SignedNoise( time * speed * 1.13f, _noiseSeed + 19.4f );
		float fz = SignedNoise( time * speed * 0.91f, _noiseSeed + 33.1f );
		return new Vector3( fx, fy, fz ) * flutterAmplitude;
	}

	Quaternion EvaluateTilt( float time, Vector3 offset, Vector3 velocity, float amount )
	{
		float pitch = -offset.z * tiltDegreesPerMeter;
		float roll = offset.x * tiltDegreesPerMeter;
		pitch += -velocity.z * tiltFromVelocity;
		roll += velocity.x * tiltFromVelocity;

		if ( wobbleDegrees > 0.0001f )
		{
			float speed = flutterSpeed * 0.55f * speedScale;
			pitch += SignedNoise( time * speed, _noiseSeed + 3f ) * wobbleDegrees * amount;
			roll += SignedNoise( time * speed * 1.07f, _noiseSeed + 11.6f ) * wobbleDegrees * amount;
		}

		float yaw = 0f;
		if ( yawAmplitude > 0.0001f )
		{
			float phase = time * yawSpeed * speedScale * Mathf.PI * 2f + _noiseSeed;
			yaw = Mathf.Sin( phase ) * yawAmplitude;
			yaw += SignedNoise( time * yawSpeed * speedScale * 0.55f, _noiseSeed + 90f ) * yawAmplitude * 0.4f;
			yaw *= amount;
		}

		return Quaternion.Euler( pitch, yaw, roll );
	}

	static float SignedNoise( float t, float seed )
	{
		return Mathf.PerlinNoise( t + seed, seed * 0.37f ) * 2f - 1f;
	}

	static float GetMotionTime()
	{
#if UNITY_EDITOR
		if ( !Application.isPlaying )
			return (float)UnityEditor.EditorApplication.timeSinceStartup;
#endif
		return Time.time;
	}

	static float GetDeltaTime()
	{
#if UNITY_EDITOR
		if ( !Application.isPlaying )
			return 1f / 60f;
#endif
		return Time.deltaTime;
	}

	static float HashSeed( int instanceId )
	{
		unchecked
		{
			uint x = (uint)instanceId;
			x ^= x >> 16;
			x *= 0x7feb352du;
			x ^= x >> 15;
			x *= 0x846ca68bu;
			x ^= x >> 16;
			return ( x & 0xFFFF ) / 65535f * 100f + 1f;
		}
	}

	void OnDrawGizmosSelected()
	{
		Vector3 restWorld;
		Quaternion restWorldRot;
		if ( hasRestPose )
		{
			restWorld = transform.parent != null
				? transform.parent.TransformPoint( restLocalPosition )
				: restLocalPosition;
			restWorldRot = transform.parent != null
				? transform.parent.rotation * restLocalRotation
				: restLocalRotation;
		}
		else
		{
			restWorld = transform.position;
			restWorldRot = transform.rotation;
		}

		Vector3 radius = wanderRadius;
		radius.x += orbitRadius.x + flutterAmplitude;
		radius.y += bobAmplitude + liftAmplitude + orbitRadius.y * 0.28f + flutterAmplitude;
		radius.z += orbitRadius.y + flutterAmplitude;
		radius *= Mathf.Max( motionScale, 0.01f );
		if ( radius.sqrMagnitude < 0.0001f )
			return;

		Color color = new Color( 0.45f, 0.85f, 1f, 0.75f );
		Gizmos.color = color;
		Matrix4x4 prev = Gizmos.matrix;
		Gizmos.matrix = Matrix4x4.TRS( restWorld, restWorldRot, radius );
		Gizmos.DrawWireSphere( Vector3.zero, 1f );
		Gizmos.matrix = prev;
	}
}
