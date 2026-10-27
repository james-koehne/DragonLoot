using UnityEngine;

/// <summary>
/// Idle float for a small lantern. Traces a figure-8 whose center and yaw/pitch are
/// slowly Perlin-warped, so the path stays curvy and bounded near the rest pose.
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
	[Tooltip( "Scales path rate and drift rate. 1 = authored defaults." )]
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

	[Header( "Figure Eight" )]
	[SerializeField]
	[Tooltip( "Half-extents of the lemniscate in local X (long axis) and Z (cross axis)." )]
	Vector2 figureEightRadius = new Vector2( 0.08f, 0.07f );

	[SerializeField]
	[Min( 0.01f )]
	[Tooltip( "Figure-8 cycles per second." )]
	float figureEightSpeed = 0.11f;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "How quickly the lantern eases onto the path. Higher = tighter follow." )]
	float pathFollow = 4.5f;

	[Header( "Center Drift" )]
	[SerializeField]
	[Tooltip( "Slow Perlin drift of the figure-8 center, in local meters. Hard-capped." )]
	Vector3 wanderRadius = new Vector3( 0.08f, 0.035f, 0.08f );

	[SerializeField]
	[Min( 0.01f )]
	float wanderSpeed = 0.18f;

	[Header( "Orientation Warp" )]
	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Max yaw swing of the figure-8 plane, in degrees." )]
	float yawWarpDegrees = 40f;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Max pitch tip of the figure-8 plane, in degrees." )]
	float pitchWarpDegrees = 12f;

	[SerializeField]
	[Min( 0.01f )]
	[Tooltip( "How fast orientation noise evolves." )]
	float warpSpeed = 0.12f;

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
	[Tooltip( "Rare extra lift when the slow noise crests - like the spell tugs upward." )]
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

	LanternActivator _activator;
	Vector3 _offset;
	Vector3 _prevPath;
	Quaternion _tilt = Quaternion.identity;
	float _noiseSeed;
	float _wakeElapsed;
	bool _appliedPose;
	bool _hasPrevPath;

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
		_noiseSeed = HashSeed( GetEntityId().GetHashCode() );
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
		figureEightSpeed = Mathf.Max( 0.01f, figureEightSpeed );
		pathFollow = Mathf.Max( 0f, pathFollow );
		wanderSpeed = Mathf.Max( 0.01f, wanderSpeed );
		yawWarpDegrees = Mathf.Max( 0f, yawWarpDegrees );
		pitchWarpDegrees = Mathf.Max( 0f, pitchWarpDegrees );
		warpSpeed = Mathf.Max( 0.01f, warpSpeed );
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

#if UNITY_EDITOR
		UnityEngine.SceneManagement.Scene scene = default;
		bool wasSceneDirty = false;
		if ( !Application.isPlaying )
			EditModePreviewDirtyUtil.BeginMutation( gameObject, out scene, out wasSceneDirty );
#endif

		ApplyRestPoseTransform();
		_appliedPose = false;
		ResetMotionState();

#if UNITY_EDITOR
		if ( !Application.isPlaying )
			EditModePreviewDirtyUtil.EndMutation( scene, wasSceneDirty, transform, gameObject );
#endif
	}

	void ResetMotionState()
	{
		_offset = Vector3.zero;
		_prevPath = Vector3.zero;
		_tilt = Quaternion.identity;
		_wakeElapsed = 0f;
		_hasPrevPath = false;
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
		Vector3 path = EvaluatePath( time ) * amount;

		if ( immediate || pathFollow <= 0.0001f || dt <= 0.0001f )
			_offset = path;
		else
		{
			float t = 1f - Mathf.Exp( -pathFollow * dt );
			_offset = Vector3.Lerp( _offset, path, t );
		}

		Vector3 velocity = Vector3.zero;
		if ( _hasPrevPath && dt > 0.0001f )
			velocity = ( path - _prevPath ) / dt;
		_prevPath = path;
		_hasPrevPath = true;

		Vector3 offset = _offset;
		offset.y += EvaluateBob( time ) * amount;
		offset.y += EvaluateLift( time ) * amount;
		offset += EvaluateFlutter( time ) * amount;

		Quaternion desiredTilt = EvaluateTilt( time, offset, velocity, amount );
		if ( immediate || tiltLerp <= 0.1f )
			_tilt = desiredTilt;
		else
		{
			float tiltT = 1f - Mathf.Exp( -tiltLerp * dt );
			_tilt = Quaternion.Slerp( _tilt, desiredTilt, tiltT );
		}

		Vector3 localPosition = restLocalPosition + offset;
		Quaternion localRotation = restLocalRotation * _tilt;

#if UNITY_EDITOR
		UnityEngine.SceneManagement.Scene scene = default;
		bool wasSceneDirty = false;
		if ( !Application.isPlaying )
			EditModePreviewDirtyUtil.BeginMutation( gameObject, out scene, out wasSceneDirty );
#endif

		if ( transform.localPosition != localPosition )
			transform.localPosition = localPosition;
		if ( transform.localRotation != localRotation )
			transform.localRotation = localRotation;
		_appliedPose = true;

#if UNITY_EDITOR
		if ( !Application.isPlaying )
			EditModePreviewDirtyUtil.EndMutation( scene, wasSceneDirty, transform, gameObject );
#endif
	}

	void ApplyRestPoseTransform()
	{
		if ( transform.localPosition != restLocalPosition )
			transform.localPosition = restLocalPosition;
		if ( transform.localRotation != restLocalRotation )
			transform.localRotation = restLocalRotation;
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

	Vector3 EvaluatePath( float time )
	{
		return EvaluateWander( time ) + EvaluateFigureEight( time );
	}

	Vector3 EvaluateWander( float time )
	{
		float speed = wanderSpeed * speedScale;
		float nx = SignedNoise( time * speed, _noiseSeed );
		float ny = SignedNoise( time * speed * 0.83f, _noiseSeed + 17.3f );
		float nz = SignedNoise( time * speed * 1.17f, _noiseSeed + 41.7f );
		return new Vector3( nx * wanderRadius.x, ny * wanderRadius.y, nz * wanderRadius.z );
	}

	Vector3 EvaluateFigureEight( float time )
	{
		float phase = FigureEightPhase( time );
		float sinP = Mathf.Sin( phase );
		float cosP = Mathf.Cos( phase );

		// Lemniscate in XZ: x = rx * sin, z = rz * sin*cos*2 so both axes hit their authored amplitude.
		Vector3 local = new Vector3(
			figureEightRadius.x * sinP,
			0f,
			figureEightRadius.y * sinP * cosP * 2f );

		return EvaluateOrientation( time ) * local;
	}

	Quaternion EvaluateOrientation( float time )
	{
		float speed = warpSpeed * speedScale;
		float yaw = SignedNoise( time * speed, _noiseSeed + 71.2f ) * yawWarpDegrees;
		float pitch = SignedNoise( time * speed * 0.91f, _noiseSeed + 103.5f ) * pitchWarpDegrees;
		return Quaternion.Euler( pitch, yaw, 0f );
	}

	float FigureEightPhase( float time )
	{
		return time * figureEightSpeed * speedScale * Mathf.PI * 2f + _noiseSeed;
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

		float figureExtent = Mathf.Max( figureEightRadius.x, figureEightRadius.y );
		Vector3 radius = wanderRadius;
		radius.x += figureExtent + flutterAmplitude;
		radius.y += bobAmplitude + liftAmplitude + flutterAmplitude;
		radius.z += figureExtent + flutterAmplitude;
		radius *= Mathf.Max( motionScale, 0.01f );
		if ( radius.sqrMagnitude < 0.0001f )
			return;

		Color color = new Color( 0.45f, 0.85f, 1f, 0.75f );
		Gizmos.color = color;
		Matrix4x4 prev = Gizmos.matrix;
		Gizmos.matrix = Matrix4x4.TRS( restWorld, restWorldRot, radius );
		Gizmos.DrawWireSphere( Vector3.zero, 1f );
		Gizmos.matrix = prev;

		DrawFigureEightGizmo( restWorld, restWorldRot );
	}

	void DrawFigureEightGizmo( Vector3 restWorld, Quaternion restWorldRot )
	{
		const int Segments = 48;
		float amount = Mathf.Max( motionScale, 0.01f );
		Vector3 prev = Vector3.zero;
		bool hasPrev = false;
		Gizmos.color = new Color( 0.7f, 0.95f, 1f, 0.9f );

		for ( int i = 0; i <= Segments; i++ )
		{
			float u = i / (float)Segments;
			float phase = u * Mathf.PI * 2f + _noiseSeed;
			float sinP = Mathf.Sin( phase );
			float cosP = Mathf.Cos( phase );
			Vector3 local = new Vector3(
				figureEightRadius.x * sinP,
				0f,
				figureEightRadius.y * sinP * cosP * 2f ) * amount;
			Vector3 world = restWorld + restWorldRot * local;
			if ( hasPrev )
				Gizmos.DrawLine( prev, world );
			prev = world;
			hasPrev = true;
		}
	}
}
