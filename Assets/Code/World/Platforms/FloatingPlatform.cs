using FeedbackSystem;

using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// One walkable floating slab. Starts sunk below rest, rises with overshoot, then idle-hovers.
/// Ride support is via <see cref="PlayerFloatingPlatformRide"/> (CharacterController delta carry).
/// </summary>
[DisallowMultipleComponent]
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

	[SerializeField] Vector3 restPosition;
	[SerializeField] Quaternion restRotation = Quaternion.identity;
	[SerializeField] bool hasRestPose;

	[Tooltip( "Optional. If empty, all Colliders under this transform are used." )]
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
	bool _riseStarted;
	bool _delayDone;
	Vector3 _riseStartPos;
	Vector3 _lastPosition;
	Vector3 _worldVelocity;
	Collider[] _resolvedColliders;

	public State CurrentState => _state;
	public bool IsRideable => ( _state == State.Rising && _riseStarted ) || _state == State.Hovering;
	public bool IsRising => _state == State.Rising && _riseStarted;
	public Vector3 WorldVelocity => _worldVelocity;
	public Vector3 RestPosition => restPosition;
	public Quaternion RestRotation => restRotation;
	public bool HasRestPose => hasRestPose;

	void Awake()
	{
		ResolveColliders();
		if ( !hasRestPose )
			CaptureRestPose();
		_lastPosition = transform.position;
	}

	void Update()
	{
		if ( _state == State.Rising )
			TickRise( Time.deltaTime );
		else if ( _state == State.Hovering )
			TickHover( Time.deltaTime );

		UpdateVelocity( Time.deltaTime );
	}

	public void CaptureRestPose()
	{
		restPosition = transform.position;
		restRotation = transform.rotation;
		hasRestPose = true;
	}

	public void ConfigureMotion(
		float sunkYOffset,
		float riseDuration,
		AnimationCurve riseCurve,
		float hoverAmplitude,
		float hoverLandingAmplitude,
		float hoverFrequency,
		float hoverPhase,
		float hoverBlendDuration )
	{
		_sunkYOffset = sunkYOffset;
		_riseDuration = Mathf.Max( 0.05f, riseDuration );
		_riseCurve = riseCurve;
		_hoverAmplitude = Mathf.Max( 0f, hoverAmplitude );
		_hoverLandingAmplitude = Mathf.Max( _hoverAmplitude, hoverLandingAmplitude );
		_hoverFrequency = Mathf.Max( 0f, hoverFrequency );
		_hoverPhase = hoverPhase;
		_hoverBlendDuration = Mathf.Max( 0.05f, hoverBlendDuration );
	}

	public void SinkImmediate()
	{
		if ( !hasRestPose )
			CaptureRestPose();

		_state = State.Sunk;
		_riseStarted = false;
		_delayDone = false;
		_riseElapsed = 0f;
		_riseDelay = 0f;
		_hoverBlendElapsed = 0f;
		_hoverClock = 0f;
		_worldVelocity = Vector3.zero;
		transform.SetPositionAndRotation( SunkPosition(), restRotation );
		SetCollidersEnabled( false );
		_lastPosition = transform.position;
	}

	public void SnapToRestAndHover()
	{
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
		_lastPosition = transform.position;
		_worldVelocity = Vector3.zero;
	}

	public void BeginRise( float delaySeconds )
	{
		if ( !hasRestPose )
			CaptureRestPose();

		_riseDelay = Mathf.Max( 0f, delaySeconds );
		_riseElapsed = 0f;
		_riseStarted = false;
		_delayDone = _riseDelay <= 0f;
		_hoverBlendElapsed = 0f;
		_hoverClock = 0f;
		_state = State.Rising;
		_riseStartPos = SunkPosition();
		transform.SetPositionAndRotation( _riseStartPos, restRotation );
		_lastPosition = transform.position;
		_worldVelocity = Vector3.zero;

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
		transform.SetPositionAndRotation( Vector3.LerpUnclamped( _riseStartPos, restPosition, curved ), restRotation );

		if ( t < 1f )
			return;

		EnterHoverFromRise();
	}

	void EnterHoverFromRise()
	{
		_state = State.Hovering;
		_hoverClock = 0f;
		_hoverBlendElapsed = 0f;
		// Start at rest; first hover sample is sin(0)=0 so we leave rest going upward.
		transform.SetPositionAndRotation( restPosition, restRotation );
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
		float y = restPosition.y;
		if ( amp > 0.0001f && _hoverFrequency > 0.0001f )
		{
			// clock=0 => sin=0, derivative > 0 => always starts going up.
			y += Mathf.Sin( _hoverClock * _hoverFrequency * Mathf.PI * 2f ) * amp;
		}

		transform.SetPositionAndRotation( new Vector3( restPosition.x, y, restPosition.z ), restRotation );
	}

	float CurrentHoverAmplitude()
	{
		if ( _hoverBlendElapsed >= _hoverBlendDuration )
			return _hoverAmplitude;

		float u = Mathf.Clamp01( _hoverBlendElapsed / _hoverBlendDuration );
		// Ease-out: hold the larger landing bob, then settle into usual amplitude.
		float s = 1f - ( 1f - u ) * ( 1f - u ) * ( 1f - u );
		return Mathf.Lerp( _hoverLandingAmplitude, _hoverAmplitude, s );
	}

	float EvaluateRiseCurve( float t )
	{
		if ( _riseCurve != null && _riseCurve.length > 0 )
			return _riseCurve.Evaluate( t );
		return EaseOutCubic( t );
	}

	Vector3 SunkPosition()
	{
		return restPosition + new Vector3( 0f, _sunkYOffset, 0f );
	}

	void UpdateVelocity( float dt )
	{
		Vector3 pos = transform.position;
		if ( dt > 0.0001f )
			_worldVelocity = ( pos - _lastPosition ) / dt;
		else
			_worldVelocity = Vector3.zero;
		_lastPosition = pos;
	}

	void ResolveColliders()
	{
		if ( colliders != null && colliders.Length > 0 )
		{
			_resolvedColliders = colliders;
			return;
		}

		_resolvedColliders = GetComponentsInChildren<Collider>( true );
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

	public void EditorSetFeedbacks( Feedbacks rise, Feedbacks reachedTop )
	{
		onRiseStart = rise;
		onReachedTop = reachedTop;
		UnityEditor.EditorUtility.SetDirty( this );
	}
#endif
}
