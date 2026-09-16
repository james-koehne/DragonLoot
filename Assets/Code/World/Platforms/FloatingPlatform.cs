using FeedbackSystem;

using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// One walkable floating slab. Starts sunk below rest, rises with overshoot, then idle-hovers.
/// Motion runs in LateUpdate so Feedbacks on the same object cannot pin the transform.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder( 200 )]
public class FloatingPlatform : MonoBehaviour
{
	public enum State
	{
		Sunk = 0,
		Rising = 1,
		Hovering = 2
	}

	[SerializeField] Feedbacks onRiseStart;

	[Tooltip( "Played when the platform finishes rising and reaches its authored top / rest pose." )]
	[FormerlySerializedAs( "onLand" )]
	[SerializeField] Feedbacks onReachedTop;

	[Tooltip( "Authored rest pose in local space (relative to parent)." )]
	[SerializeField] Vector3 restLocalPosition;

	[SerializeField] Quaternion restLocalRotation = Quaternion.identity;
	[SerializeField] bool hasRestPose;
	[SerializeField] bool restPoseIsLocal;

	// Legacy world-space rest (migrated once into local).
	[SerializeField] Vector3 restPosition;
	[SerializeField] Quaternion restRotation = Quaternion.identity;

	[Tooltip( "Optional. If empty, non-trigger colliders under this transform are used." )]
	[SerializeField] Collider[] colliders;

	State _state = State.Sunk;
	float _riseElapsed;
	float _riseDuration = 1.5f;
	float _riseDelay;
	float _sunkYOffset = -150f;
	AnimationCurve _riseCurve;
	float _hoverAmplitude = 0.06f;
	float _hoverLandingAmplitude = 0.35f;
	float _hoverFrequency = 0.35f;
	float _hoverPhase;
	float _hoverBlendDuration = 2f;
	float _hoverBlendElapsed;
	float _hoverClock;
	float _landingBounceDepth = 0.12f;
	float _landingBounceDuration = 0.35f;
	float _landingBounceElapsed;
	bool _landingBounceActive;
	bool _riseStarted;
	bool _delayDone;
	Vector3 _riseStartLocal;
	Vector3 _lastWorldPosition;
	Vector3 _worldVelocity;
	Collider[] _resolvedColliders;

	public State CurrentState => _state;
	public bool IsRideable => ( _state == State.Rising && _riseStarted ) || _state == State.Hovering;
	public bool IsRising => _state == State.Rising && _riseStarted;
	public Vector3 WorldVelocity => _worldVelocity;
	public Vector3 RestPosition => transform.parent != null
		? transform.parent.TransformPoint( restLocalPosition )
		: restLocalPosition;
	public Quaternion RestRotation => transform.parent != null
		? transform.parent.rotation * restLocalRotation
		: restLocalRotation;
	public bool HasRestPose => hasRestPose;

	void Awake()
	{
		ResolveColliders();
		MigrateRestPoseIfNeeded();
		_lastWorldPosition = transform.position;
	}

	void MigrateRestPoseIfNeeded()
	{
		if ( !hasRestPose || restPoseIsLocal )
			return;

		if ( transform.parent != null )
		{
			restLocalPosition = transform.parent.InverseTransformPoint( restPosition );
			restLocalRotation = Quaternion.Inverse( transform.parent.rotation ) * restRotation;
		}
		else
		{
			restLocalPosition = restPosition;
			restLocalRotation = restRotation;
		}

		restPoseIsLocal = true;
	}

	void LateUpdate()
	{
		float dt = Time.deltaTime;
		TickLandingBounce( dt );
		if ( _state == State.Rising )
			TickRise( dt );
		else if ( _state == State.Hovering )
			TickHover( dt );

		UpdateVelocity( dt );
	}

	/// <summary>Player landed on this platform (fall/jump onto surface).</summary>
	public void NotifyPlayerLanded()
	{
		if ( !IsRideable || _landingBounceDepth <= 0.0001f )
			return;

		_landingBounceActive = true;
		_landingBounceElapsed = 0f;
	}

	public void CaptureRestPose()
	{
		restLocalPosition = transform.localPosition;
		restLocalRotation = transform.localRotation;
		restPosition = transform.position;
		restRotation = transform.rotation;
		hasRestPose = true;
		restPoseIsLocal = true;
	}

	public void ConfigureMotion(
		float sunkYOffset,
		float riseDuration,
		AnimationCurve riseCurve,
		float hoverAmplitude,
		float hoverLandingAmplitude,
		float hoverFrequency,
		float hoverPhase,
		float hoverBlendDuration,
		float landingBounceDepth,
		float landingBounceDuration )
	{
		_sunkYOffset = sunkYOffset;
		_riseDuration = Mathf.Max( 0.05f, riseDuration );
		_riseCurve = riseCurve;
		_hoverAmplitude = Mathf.Max( 0f, hoverAmplitude );
		_hoverLandingAmplitude = Mathf.Max( _hoverAmplitude, hoverLandingAmplitude );
		_hoverFrequency = Mathf.Max( 0f, hoverFrequency );
		_hoverPhase = hoverPhase;
		_hoverBlendDuration = Mathf.Max( 0.05f, hoverBlendDuration );
		_landingBounceDepth = Mathf.Max( 0f, landingBounceDepth );
		_landingBounceDuration = Mathf.Max( 0.05f, landingBounceDuration );
	}

	public void SinkImmediate()
	{
		MigrateRestPoseIfNeeded();
		if ( !hasRestPose )
			CaptureRestPose();

		_state = State.Sunk;
		_riseStarted = false;
		_delayDone = false;
		_riseElapsed = 0f;
		_riseDelay = 0f;
		_hoverBlendElapsed = 0f;
		_hoverClock = 0f;
		_landingBounceActive = false;
		_landingBounceElapsed = 0f;
		_worldVelocity = Vector3.zero;
		ApplyLocalPose( SunkLocalPosition(), restLocalRotation );
		SetCollidersEnabled( false );
		_lastWorldPosition = transform.position;
	}

	public void SnapToRestAndHover()
	{
		MigrateRestPoseIfNeeded();
		if ( !hasRestPose )
			CaptureRestPose();

		_state = State.Hovering;
		_riseStarted = false;
		_delayDone = true;
		_riseElapsed = _riseDuration;
		_hoverBlendElapsed = _hoverBlendDuration;
		_hoverClock = _hoverPhase;
		ApplyHoverPose();
		SetCollidersEnabled( true );
		_lastWorldPosition = transform.position;
		_worldVelocity = Vector3.zero;
	}

	public void BeginRise( float delaySeconds )
	{
		MigrateRestPoseIfNeeded();
		if ( !hasRestPose )
			CaptureRestPose();

		_riseDelay = Mathf.Max( 0f, delaySeconds );
		_riseElapsed = 0f;
		_riseStarted = false;
		_delayDone = _riseDelay <= 0f;
		_hoverBlendElapsed = 0f;
		_hoverClock = 0f;
		_state = State.Rising;
		_riseStartLocal = SunkLocalPosition();
		ApplyLocalPose( _riseStartLocal, restLocalRotation );
		_lastWorldPosition = transform.position;
		_worldVelocity = Vector3.zero;

		if ( Mathf.Abs( _sunkYOffset ) < 0.01f )
			Debug.LogWarning( "FloatingPlatform '" + name + "': sunkYOffset is ~0, rise will be invisible.", this );

		if ( _delayDone )
			StartRiseMotion();
	}

	void StartRiseMotion()
	{
		if ( _riseStarted )
			return;

		_riseStarted = true;
		SetCollidersEnabled( true );
		if ( onRiseStart != null )
			onRiseStart.Play();
	}

	void TickRise( float dt )
	{
		if ( !_delayDone )
		{
			_riseDelay -= dt;
			if ( _riseDelay > 0f )
				return;

			_delayDone = true;
			_riseElapsed = 0f;
			StartRiseMotion();
		}

		_riseElapsed += dt;
		float t = Mathf.Clamp01( _riseElapsed / _riseDuration );
		float curved = EvaluateRiseCurve( t );
		Vector3 local = Vector3.LerpUnclamped( _riseStartLocal, restLocalPosition, curved );
		ApplyLocalPose( local, restLocalRotation );

		if ( t < 1f )
			return;

		EnterHoverFromRise();
	}

	void EnterHoverFromRise()
	{
		_state = State.Hovering;
		_hoverClock = 0f;
		_hoverBlendElapsed = 0f;
		ApplyLocalPose( restLocalPosition, restLocalRotation );
		if ( onReachedTop != null )
			onReachedTop.Play();
	}

	void TickHover( float dt )
	{
		_hoverClock += dt;
		_hoverBlendElapsed += dt;
		ApplyHoverPose();
	}

	void ApplyHoverPose()
	{
		float amp = CurrentHoverAmplitude();
		Vector3 local = restLocalPosition;
		if ( amp > 0.0001f && _hoverFrequency > 0.0001f )
		{
			// clock=0 => sin=0, derivative > 0 => always starts going up.
			local.y += Mathf.Sin( _hoverClock * _hoverFrequency * Mathf.PI * 2f ) * amp;
		}

		ApplyLocalPose( local, restLocalRotation );
	}

	float CurrentHoverAmplitude()
	{
		if ( _hoverBlendElapsed >= _hoverBlendDuration )
			return _hoverAmplitude;

		float u = Mathf.Clamp01( _hoverBlendElapsed / _hoverBlendDuration );
		float s = 1f - ( 1f - u ) * ( 1f - u ) * ( 1f - u );
		return Mathf.Lerp( _hoverLandingAmplitude, _hoverAmplitude, s );
	}

	float EvaluateRiseCurve( float t )
	{
		if ( _riseCurve != null && _riseCurve.length > 0 )
			return _riseCurve.Evaluate( t );
		return EaseOutCubic( t );
	}

	Vector3 SunkLocalPosition()
	{
		return restLocalPosition + new Vector3( 0f, _sunkYOffset, 0f );
	}

	void ApplyLocalPose( Vector3 localPosition, Quaternion localRotation )
	{
		localPosition.y += EvaluateLandingBounceOffset();
		transform.localPosition = localPosition;
		transform.localRotation = localRotation;
	}

	void TickLandingBounce( float dt )
	{
		if ( !_landingBounceActive )
			return;

		_landingBounceElapsed += dt;
		if ( _landingBounceElapsed >= _landingBounceDuration )
			_landingBounceActive = false;
	}

	float EvaluateLandingBounceOffset()
	{
		if ( !_landingBounceActive || _landingBounceDepth <= 0.0001f )
			return 0f;

		float t = Mathf.Clamp01( _landingBounceElapsed / _landingBounceDuration );
		// 0 -> down -> 0 (smooth squash when the player lands).
		return -_landingBounceDepth * Mathf.Sin( t * Mathf.PI );
	}

	void UpdateVelocity( float dt )
	{
		Vector3 pos = transform.position;
		if ( dt > 0.0001f )
			_worldVelocity = ( pos - _lastWorldPosition ) / dt;
		else
			_worldVelocity = Vector3.zero;
		_lastWorldPosition = pos;
	}

	void ResolveColliders()
	{
		if ( colliders != null && colliders.Length > 0 )
		{
			_resolvedColliders = colliders;
			return;
		}

		Collider[] found = GetComponentsInChildren<Collider>( true );
		int count = 0;
		for ( int i = 0; i < found.Length; i++ )
		{
			if ( found[ i ] != null && !found[ i ].isTrigger )
				count++;
		}

		_resolvedColliders = new Collider[ count ];
		int write = 0;
		for ( int i = 0; i < found.Length; i++ )
		{
			Collider c = found[ i ];
			if ( c == null || c.isTrigger )
				continue;
			_resolvedColliders[ write++ ] = c;
		}
	}

	void SetCollidersEnabled( bool enabled )
	{
		if ( _resolvedColliders == null )
			ResolveColliders();

		if ( _resolvedColliders == null )
			return;

		for ( int i = 0; i < _resolvedColliders.Length; i++ )
		{
			Collider c = _resolvedColliders[ i ];
			if ( c == null )
				continue;
			c.enabled = enabled;
		}
	}

	static float EaseOutCubic( float t )
	{
		float u = 1f - t;
		return 1f - u * u * u;
	}

#if UNITY_EDITOR
	public void EditorCaptureRestPose()
	{
		CaptureRestPose();
		UnityEditor.EditorUtility.SetDirty( this );
	}

	/// <summary>
	/// Clears a corrupted rest pose (e.g. captured while sunk) and re-captures from the current transform.
	/// Call while platforms are at their authored height in the editor.
	/// </summary>
	public void EditorRecaptureRestPoseFromCurrent()
	{
		hasRestPose = false;
		restPoseIsLocal = false;
		CaptureRestPose();
		UnityEditor.EditorUtility.SetDirty( this );
	}

	public void EditorSetFeedbacks( Feedbacks rise, Feedbacks reachedTop )
	{
		onRiseStart = rise;
		onReachedTop = reachedTop;
		UnityEditor.EditorUtility.SetDirty( this );
	}
#endif
}
