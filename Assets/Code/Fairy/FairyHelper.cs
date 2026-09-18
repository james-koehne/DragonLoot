using System.Collections.Generic;
using System.Text;

using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Treasure-surface fairy companion: follows the player, perches on nearby incomplete displays,
/// and talks through a world-space speech bubble.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent( typeof( FairyInteractable ) )]
public class FairyHelper : MonoBehaviour
{
	static readonly int IsMovingHash = Animator.StringToHash( "IsMoving" );
	static readonly int TalkHash = Animator.StringToHash( "Talk" );
	static readonly Collider[] OverlapScratch = new Collider[ 48 ];

	public static FairyHelper Instance { get; private set; }

	[Header( "Follow" )]
	[SerializeField]
	Vector3 followOffsetLocal = new Vector3( 1.1f, 0f, 1.35f );

	[SerializeField]
	[Min( 0.05f )]
	[Tooltip( "Height above the player when not over a treasure surface." )]
	float hoverHeight = 1.15f;

	[SerializeField]
	[Min( 0.05f )]
	[Tooltip( "Height above the treasure surface sample when following over traversable surface." )]
	float surfaceHoverHeight = 0.7f;

	[SerializeField]
	[Min( 0.1f )]
	float moveSpeed = 4.5f;

	[SerializeField]
	[Min( 0.1f )]
	float catchUpSpeed = 8.5f;

	[SerializeField]
	[Min( 0.05f )]
	[Tooltip( "SmoothDamp time — higher = softer approach into the follow slot." )]
	float followSmoothTime = 0.28f;

	[SerializeField]
	[Min( 0.01f )]
	float arriveDistance = 0.12f;

	[SerializeField]
	[Min( 1f )]
	float turnSpeed = 10f;

	[SerializeField]
	[Min( 0.1f )]
	[Tooltip( "If the nearest traversable surface point is farther than this from the follow target, use the movement-yaw player fallback instead." )]
	float surfaceSnapMaxDistance = 2.5f;

	[SerializeField]
	Vector3 viewFallbackOffsetLocal = new Vector3( 0.85f, 0f, 1.1f );

	[SerializeField]
	[Min( 0.05f )]
	[Tooltip( "How long the camera must stay still before the fairy relocates to front-right of look." )]
	float cameraSettleSeconds = 0.5f;

	[SerializeField]
	[Min( 1f )]
	[Tooltip( "Camera yaw speed (deg/sec) above which the settle timer resets." )]
	float cameraRotateSpeedThreshold = 18f;

	[SerializeField]
	[Min( 0.1f )]
	float followYawLerp = 6f;

	[Header( "Perch" )]
	[SerializeField]
	[Min( 0.5f )]
	float displaySeekRadius = 6f;

	[SerializeField]
	[Min( 0.05f )]
	float perchHeight = 1.35f;

	[SerializeField]
	[Min( 0.5f )]
	float pileAskRadius = 6f;

	[Header( "Glow" )]
	[SerializeField]
	[Min( 0.1f )]
	float farGlowDistance = 4f;

	[SerializeField]
	[Min( 0f )]
	float farGlowHysteresis = 0.35f;

	[Header( "Motion polish" )]
	[SerializeField]
	[Min( 0f )]
	float idleBobAmplitude = 0.06f;

	[SerializeField]
	[Min( 0.01f )]
	float idleBobSpeed = 2.2f;

	[Header( "Curved flight" )]
	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Lateral swing when relocating nearby (magical arc instead of a straight cut)." )]
	float closeArcAmplitude = 0.55f;

	[SerializeField]
	[Min( 0.1f )]
	[Tooltip( "Distances at or below this use the close arc feel." )]
	float closeArcDistance = 2.5f;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Side-to-side wave amplitude for longer flights." )]
	float waveAmplitude = 0.85f;

	[SerializeField]
	[Min( 0.05f )]
	[Tooltip( "How quickly the flight wave oscillates (radians per meter traveled)." )]
	float waveFrequency = 1.4f;

	[SerializeField]
	[Min( 0.1f )]
	[Tooltip( "How far ahead along the path the fairy steers toward (creates the curve)." )]
	float curveLookAhead = 1.1f;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Extra vertical flutter while traveling in a curve." )]
	float travelLiftAmplitude = 0.18f;

	[Header( "Refs" )]
	[SerializeField]
	Animator animator;

	[SerializeField]
	FairySpeechBubble speechBubble;

	[SerializeField]
	Feedbacks onInteractFeedback;

	[SerializeField]
	Feedbacks onFarGlowFeedback;

	[SerializeField]
	Feedbacks onLoopFeedback;

	[Header( "Intro" )]
	[SerializeField]
	[Tooltip( "Spawn / intro talk slot relative to camera look (front-center)." )]
	Vector3 introOffsetLocal = new Vector3( 0f, 0f, 1.25f );

	[SerializeField]
	[TextArea( 1, 3 )]
	string[] introLines =
	{
		"Hi! I am here to help you on your journey",
		"Talk to me to find out about your progress"
	};

	const float MoveEnterPlanarSpeed = 0.12f;
	const float MoveExitPlanarSpeed = 0.035f;
	const float SurfaceRaiseEpsilon = 0.001f;

	FairyInteractable _interactable;
	Component _perchedDisplay;
	TreasurePileVisual _nearbyPile;
	Vector3 _logicalPosition;
	Vector3 _planarVelocity;
	Vector3 _smoothVelocity;
	Vector3 _frameMoveDelta;
	float _bobPhase;
	float _curvePhase;
	float _curveSide = 1f;
	float _followYaw;
	float _lastCameraYaw;
	float _cameraSettledFor;
	bool _cameraYawSampled;
	bool _followYawInitialized;
	bool _isMoving;
	bool _farGlowPlaying;
	bool _introPlayed;
	bool _introActive;
	bool _introBubbleSeen;
	bool _talking;
	readonly DisplayRequirementUI _displayProgress = new DisplayRequirementUI();
	readonly StringBuilder _progressBuilder = new StringBuilder( 96 );

	public Component PerchedDisplay => _perchedDisplay;
	public TreasurePileVisual NearbyPile => _nearbyPile;
	public bool IsTalking => _talking;
	public bool IsHovered => IsFocusedByPlayer();
	public bool IsFlying => _isMoving;

	void Awake()
	{
		if ( Instance != null && Instance != this )
		{
			Destroy( gameObject );
			return;
		}

		Instance = this;
		EnsureRuntimeSetup();
		ClampFollowTunables();
		_interactable = GetComponent<FairyInteractable>();
		if ( speechBubble == null )
			speechBubble = GetComponentInChildren<FairySpeechBubble>( true );
		if ( animator == null )
			animator = GetComponentInChildren<Animator>( true );
		if ( onInteractFeedback == null )
		{
			Transform interact = transform.Find( "InteractFeedbacks" );
			if ( interact != null )
				onInteractFeedback = interact.GetComponent<Feedbacks>();
		}
		if ( onFarGlowFeedback == null )
		{
			Transform glow = transform.Find( "Glow" );
			if ( glow != null )
				onFarGlowFeedback = glow.GetComponent<Feedbacks>();
		}
		if ( onLoopFeedback == null )
		{
			Transform loop = transform.Find( "LoopFeedbacks" );
			if ( loop != null )
				onLoopFeedback = loop.GetComponent<Feedbacks>();
		}
		if ( speechBubble != null )
			speechBubble.BindFollow( transform );
	}

	void ClampFollowTunables()
	{
		if ( cameraSettleSeconds < 0.05f )
			cameraSettleSeconds = 0.5f;
		if ( cameraRotateSpeedThreshold < 1f )
			cameraRotateSpeedThreshold = 18f;
		if ( followYawLerp < 0.1f )
			followYawLerp = 6f;
		if ( surfaceHoverHeight < 0.05f )
			surfaceHoverHeight = 0.7f;
		if ( followSmoothTime < 0.05f )
			followSmoothTime = 0.28f;
		if ( closeArcDistance < 0.1f )
			closeArcDistance = 2.5f;
		if ( waveFrequency < 0.05f )
			waveFrequency = 1.4f;
		if ( curveLookAhead < 0.1f )
			curveLookAhead = 1.1f;
	}

	void EnsureRuntimeSetup()
	{
		if ( GetComponent<CapsuleCollider>() == null )
		{
			CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
			capsule.isTrigger = false;
			capsule.center = new Vector3( 0f, 0.35f, 0f );
			capsule.radius = 0.28f;
			capsule.height = 0.85f;
			capsule.direction = 1;
		}

		Rigidbody body = GetComponent<Rigidbody>();
		if ( body == null )
			body = gameObject.AddComponent<Rigidbody>();
		body.isKinematic = true;
		body.useGravity = false;
	}

	void OnDestroy()
	{
		StopLoopAudio();
		if ( Instance == this )
			Instance = null;
	}

	void Start()
	{
		_logicalPosition = transform.position;
		SnapToIntroFront();
		PlayIntro();
		StartLoopAudio();
	}

	void StartLoopAudio()
	{
		if ( onLoopFeedback == null )
			return;
		if ( onLoopFeedback.IsPlaying )
			return;
		onLoopFeedback.Play();
	}

	void StopLoopAudio()
	{
		if ( onLoopFeedback == null )
			return;
		onLoopFeedback.Stop();
	}

	void LateUpdate()
	{
		PlayerController player = ResolvePlayer();
		if ( player == null )
			return;

		UpdateFollowYaw( player );
		if ( !_introActive )
			RefreshNearbyTargets( player );
		else
		{
			_nearbyPile = null;
			_perchedDisplay = null;
		}

		Vector3 desired = ResolveDesiredPosition( player );
		StepMove( desired );
		UpdateFacing( player );
		UpdateAnimator();
		UpdateFarGlow( player );
		TickIntroEnd();
	}

	public void NotifyTalkStarted()
	{
		_talking = true;
		if ( animator != null )
			animator.SetTrigger( TalkHash );
	}

	public void NotifyTalkEnded()
	{
		_talking = false;
	}

	public void PlayInteractFeedback()
	{
		if ( onInteractFeedback == null )
			return;

		FeedbackContext context = new FeedbackContext();
		context.Source = gameObject;
		context.Target = gameObject;
		context.Position = transform.position;
		onInteractFeedback.Play( context );
	}

	public void Say( string line )
	{
		if ( speechBubble != null )
			speechBubble.Say( line );
	}

	public void Say( params string[] lines )
	{
		if ( speechBubble != null )
			speechBubble.Say( lines );
	}

	public bool TryBuildDisplayProgress( Component display, out string text )
	{
		text = null;
		_progressBuilder.Length = 0;
		if ( !_displayProgress.TryAppendDisplaySummary( display, _progressBuilder ) )
			return false;
		text = _progressBuilder.ToString();
		return !string.IsNullOrEmpty( text );
	}

	public static bool TryGetDisplayProgressCounts( Component display, out int current, out int capacity, out bool complete )
	{
		current = 0;
		capacity = 0;
		complete = false;
		if ( display == null )
			return false;

		TypedDisplayTableInteractable typed = display as TypedDisplayTableInteractable;
		if ( typed != null )
		{
			current = typed.CurrentCount;
			capacity = typed.Capacity;
			complete = typed.IsComplete;
			return true;
		}

		GemConstellationInteractable constellation = display as GemConstellationInteractable;
		if ( constellation != null )
		{
			current = constellation.CurrentCount;
			capacity = constellation.Capacity;
			complete = constellation.IsComplete;
			return true;
		}

		ArtifactPresentationTableInteractable artifact = display as ArtifactPresentationTableInteractable;
		if ( artifact != null )
		{
			current = artifact.CurrentCount;
			capacity = artifact.Capacity;
			complete = artifact.IsComplete;
			return true;
		}

		return false;
	}

	void PlayIntro()
	{
		if ( _introPlayed )
			return;
		_introPlayed = true;
		_introActive = true;
		_introBubbleSeen = false;
		PlayInteractFeedback();
		NotifyTalkStarted();
		Say( introLines );
		CancelInvoke( nameof( EndIntro ) );
		Invoke( nameof( EndIntro ), EstimateIntroHoldSeconds() );
	}

	void EndIntro()
	{
		if ( !_introActive )
			return;
		_introActive = false;
		_introBubbleSeen = false;
		CancelInvoke( nameof( EndIntro ) );
		NotifyTalkEnded();
	}

	void TickIntroEnd()
	{
		if ( !_introActive || speechBubble == null )
			return;
		if ( speechBubble.IsShowing )
		{
			_introBubbleSeen = true;
			return;
		}
		if ( !_introBubbleSeen )
			return;
		EndIntro();
	}

	float EstimateIntroHoldSeconds()
	{
		if ( introLines == null || introLines.Length == 0 )
			return 2.5f;

		float total = 0.4f;
		for ( int i = 0; i < introLines.Length; i++ )
		{
			string line = introLines[ i ];
			if ( string.IsNullOrEmpty( line ) )
				continue;
			total += Mathf.Max( 3.5f, Mathf.Clamp( line.Length / 12f, 2.5f, 8f ) );
			total += 0.25f;
		}
		return Mathf.Max( 2.5f, total );
	}

	void SnapToIntroFront()
	{
		PlayerController player = ResolvePlayer();
		if ( player == null )
			return;

		_followYaw = GetCameraLookYaw( player );
		_followYawInitialized = true;
		_lastCameraYaw = _followYaw;
		_cameraYawSampled = true;
		_cameraSettledFor = 0f;
		_introActive = true;
		Vector3 desired = BuildViewFallback( player );
		_logicalPosition = desired;
		transform.position = desired;
		_smoothVelocity = Vector3.zero;
		_planarVelocity = Vector3.zero;
		_frameMoveDelta = Vector3.zero;
		_curvePhase = 0f;
		_isMoving = false;
		FaceToward( player.transform.position - transform.position, instant: true );
	}

	void SnapToFollowOffset()
	{
		PlayerController player = ResolvePlayer();
		if ( player == null )
			return;

		EnsureFollowYaw( player );
		Vector3 desired = ResolveSurfacePoint( player, BuildFollowTarget( player ) );
		_logicalPosition = desired;
		transform.position = desired;
		_smoothVelocity = Vector3.zero;
		_planarVelocity = Vector3.zero;
		_frameMoveDelta = Vector3.zero;
		_curvePhase = 0f;
		_isMoving = false;
		FaceToward( player.transform.position - transform.position, instant: true );
	}

	void RefreshNearbyTargets( PlayerController player )
	{
		_nearbyPile = FindNearestPile( player.transform.position, pileAskRadius );
		_perchedDisplay = FindNearestIncompleteDisplay( player.transform.position, displaySeekRadius );
	}

	Vector3 ResolveDesiredPosition( PlayerController player )
	{
		if ( _introActive )
		{
			EnsureFollowYaw( player );
			// Intro always uses the authored override slot — never snap onto treasure surface.
			return BuildViewFallback( player );
		}

		if ( _perchedDisplay != null )
		{
			Transform perch = _perchedDisplay.transform;
			return perch.position + Vector3.up * perchHeight;
		}

		EnsureFollowYaw( player );
		return ResolveSurfacePoint( player, BuildFollowTarget( player ) );
	}

	void UpdateFollowYaw( PlayerController player )
	{
		EnsureFollowYaw( player );
		float lookYaw = GetCameraLookYaw( player );
		UpdateCameraSettleTimer( lookYaw );

		// Intro: ease toward camera look so the override slot doesn't feel hard-locked.
		if ( _introActive )
		{
			_followYaw = Mathf.LerpAngle( _followYaw, lookYaw, 1f - Mathf.Exp( -followYawLerp * Time.deltaTime ) );
			return;
		}

		if ( IsHovered || _talking )
			return;

		if ( _cameraSettledFor < cameraSettleSeconds )
			return;

		_followYaw = Mathf.LerpAngle( _followYaw, lookYaw, 1f - Mathf.Exp( -followYawLerp * Time.deltaTime ) );
	}

	void UpdateCameraSettleTimer( float lookYaw )
	{
		if ( !_cameraYawSampled )
		{
			_lastCameraYaw = lookYaw;
			_cameraYawSampled = true;
			_cameraSettledFor = 0f;
			return;
		}

		float dt = Mathf.Max( Time.deltaTime, 0.0001f );
		float yawRate = Mathf.Abs( Mathf.DeltaAngle( _lastCameraYaw, lookYaw ) ) / dt;
		_lastCameraYaw = lookYaw;

		if ( yawRate > cameraRotateSpeedThreshold )
			_cameraSettledFor = 0f;
		else
			_cameraSettledFor += dt;
	}

	void EnsureFollowYaw( PlayerController player )
	{
		if ( _followYawInitialized || player == null )
			return;

		_followYaw = GetCameraLookYaw( player );
		_lastCameraYaw = _followYaw;
		_cameraYawSampled = true;
		_cameraSettledFor = cameraSettleSeconds;
		_followYawInitialized = true;
	}

	static float GetCameraLookYaw( PlayerController player )
	{
		Transform cam = null;
		if ( player != null && player.CameraMount != null )
			cam = player.CameraMount;
		else if ( GameMode.Instance != null && GameMode.Instance.cameraController != null )
			cam = GameMode.Instance.cameraController.transform;

		if ( cam != null )
		{
			Vector3 forward = cam.forward;
			forward.y = 0f;
			if ( forward.sqrMagnitude > 0.0001f )
				return Mathf.Atan2( forward.x, forward.z ) * Mathf.Rad2Deg;
		}

		if ( player == null )
			return 0f;

		Vector3 bodyForward = player.transform.forward;
		bodyForward.y = 0f;
		if ( bodyForward.sqrMagnitude < 0.0001f )
			return player.transform.eulerAngles.y;
		return Mathf.Atan2( bodyForward.x, bodyForward.z ) * Mathf.Rad2Deg;
	}

	Vector3 BuildFollowTarget( PlayerController player )
	{
		Vector3 offset = _introActive ? introOffsetLocal : followOffsetLocal;
		return BuildOffsetFromYaw( player.transform.position, offset, _followYaw );
	}

	Vector3 BuildViewFallback( PlayerController player )
	{
		EnsureFollowYaw( player );
		Vector3 offset = _introActive ? introOffsetLocal : viewFallbackOffsetLocal;
		Vector3 fallback = BuildOffsetFromYaw( player.transform.position, offset, _followYaw );
		fallback.y = player.transform.position.y + hoverHeight;
		return fallback;
	}

	static Vector3 BuildOffsetFromYaw( Vector3 origin, Vector3 localOffset, float yawDegrees )
	{
		Quaternion yaw = Quaternion.Euler( 0f, yawDegrees, 0f );
		Vector3 planarForward = yaw * Vector3.forward;
		Vector3 planarRight = yaw * Vector3.right;
		Vector3 offset = planarRight * localOffset.x + planarForward * localOffset.z;
		Vector3 desired = origin + offset;
		desired.y = origin.y;
		return desired;
	}

	Vector3 ResolveSurfacePoint( PlayerController player, Vector3 desired )
	{
		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		if ( world == null )
			world = TreasureSurfaceWorld.EnsureExists();

		if ( world == null || world.Sampler == null )
			return BuildViewFallback( player );

		// Only ride the treasure surface when the player is actually near it.
		if ( !IsPlayerNearTreasureSurface( player, world ) )
			return BuildViewFallback( player );

		if ( world.TryGetChunkCoord( desired, out TreasureChunkCoord coord ) )
			world.EnsureChunkLoaded( coord );

		float maxSnapSq = surfaceSnapMaxDistance * surfaceSnapMaxDistance;

		if ( world.TryResolveTraversableEntry( desired, out Vector3 resolved, out TreasureSurfaceSample sample, preferStable: true ) )
		{
			float dx = resolved.x - desired.x;
			float dz = resolved.z - desired.z;
			if ( dx * dx + dz * dz <= maxSnapSq && IsSurfacePointNearPlayer( player, resolved ) )
			{
				resolved.y = sample.Height + surfaceHoverHeight;
				return resolved;
			}
		}

		if ( world.Sampler.TrySample( desired, out sample ) && sample.Traversable && IsSurfacePointNearPlayer( player, desired ) )
		{
			desired.y = sample.Height + surfaceHoverHeight;
			return desired;
		}

		return BuildViewFallback( player );
	}

	bool IsPlayerNearTreasureSurface( PlayerController player, TreasureSurfaceWorld world )
	{
		Vector3 playerPos = player.transform.position;
		float maxSnapSq = surfaceSnapMaxDistance * surfaceSnapMaxDistance;

		if ( world.TryResolveTraversableEntry( playerPos, out Vector3 resolved, out _, preferStable: true ) )
		{
			float dx = resolved.x - playerPos.x;
			float dz = resolved.z - playerPos.z;
			if ( dx * dx + dz * dz <= maxSnapSq )
				return true;
		}

		if ( world.Sampler.TrySample( playerPos, out TreasureSurfaceSample sample ) && sample.Traversable )
			return true;

		return false;
	}

	bool IsSurfacePointNearPlayer( PlayerController player, Vector3 surfacePoint )
	{
		float dx = surfacePoint.x - player.transform.position.x;
		float dz = surfacePoint.z - player.transform.position.z;
		float maxDist = surfaceSnapMaxDistance + Mathf.Max( Mathf.Abs( followOffsetLocal.x ), Mathf.Abs( followOffsetLocal.z ) );
		return dx * dx + dz * dz <= maxDist * maxDist;
	}

	void StepMove( Vector3 desired )
	{
		Vector3 current = _logicalPosition;
		float dt = Mathf.Max( Time.deltaTime, 0.0001f );

		Vector3 to = desired - current;
		Vector3 planar = new Vector3( to.x, 0f, to.z );
		float planarDist = planar.magnitude;

		if ( planarDist <= arriveDistance && Mathf.Abs( to.y ) <= arriveDistance )
		{
			_frameMoveDelta = desired - current;
			_planarVelocity = Vector3.zero;
			_smoothVelocity = Vector3.zero;
			_logicalPosition = desired;
			UpdateMoveState( dt );
			ApplyIdleBob( desired );
			return;
		}

		// Intro eases into the override slot; skip wave/arc so it reads as a soft settle.
		if ( _introActive )
		{
			Vector3 next = Vector3.SmoothDamp( current, desired, ref _smoothVelocity, followSmoothTime, moveSpeed, Time.deltaTime );
			_frameMoveDelta = next - current;
			_planarVelocity = new Vector3( _frameMoveDelta.x, 0f, _frameMoveDelta.z ) / dt;
			_logicalPosition = next;
			UpdateMoveState( dt );
			_bobPhase = 0f;
			transform.position = next;
			return;
		}

		if ( !_isMoving && planarDist > arriveDistance * 2f )
			PickCurveSide( planar );

		Vector3 seek = BuildCurvedSeekPoint( current, desired, planar, planarDist );
		float maxSpeed = planarDist > farGlowDistance ? catchUpSpeed : moveSpeed;
		Vector3 nextFree = Vector3.SmoothDamp( current, seek, ref _smoothVelocity, followSmoothTime, maxSpeed, Time.deltaTime );

		_frameMoveDelta = nextFree - current;
		_planarVelocity = new Vector3( _frameMoveDelta.x, 0f, _frameMoveDelta.z ) / dt;
		float traveled = new Vector3( _frameMoveDelta.x, 0f, _frameMoveDelta.z ).magnitude;
		if ( traveled > 0.0001f )
			_curvePhase += traveled * waveFrequency;
		_logicalPosition = nextFree;
		UpdateMoveState( dt );

		Vector3 display = nextFree;
		if ( !_isMoving && idleBobAmplitude > 0f )
		{
			_bobPhase += Time.deltaTime * idleBobSpeed;
			display.y += Mathf.Sin( _bobPhase ) * idleBobAmplitude;
		}
		else
		{
			_bobPhase = 0f;
		}

		transform.position = display;
	}

	void ApplyIdleBob( Vector3 logical )
	{
		Vector3 display = logical;
		if ( !_isMoving && idleBobAmplitude > 0f )
		{
			_bobPhase += Time.deltaTime * idleBobSpeed;
			display.y += Mathf.Sin( _bobPhase ) * idleBobAmplitude;
		}
		else
		{
			_bobPhase = 0f;
		}

		transform.position = display;
	}

	void PickCurveSide( Vector3 planarToTarget )
	{
		if ( planarToTarget.sqrMagnitude < 0.0001f )
		{
			_curveSide = Random.value < 0.5f ? -1f : 1f;
			return;
		}

		Vector3 dir = planarToTarget.normalized;
		Vector3 lateral = Vector3.Cross( Vector3.up, dir );
		Vector3 fromPlayer = Vector3.zero;
		PlayerController player = ResolvePlayer();
		if ( player != null )
		{
			fromPlayer = _logicalPosition - player.transform.position;
			fromPlayer.y = 0f;
		}

		float bias = Vector3.Dot( fromPlayer, lateral );
		if ( Mathf.Abs( bias ) > 0.05f )
			_curveSide = bias >= 0f ? 1f : -1f;
		else
			_curveSide = Random.value < 0.5f ? -1f : 1f;
	}

	Vector3 BuildCurvedSeekPoint( Vector3 current, Vector3 desired, Vector3 planar, float planarDist )
	{
		Vector3 dir = planar / planarDist;
		Vector3 lateral = Vector3.Cross( Vector3.up, dir ) * _curveSide;

		float closeT = 1f - Mathf.Clamp01( planarDist / closeArcDistance );
		// Peak the close arc mid-approach so short hops swing around the player instead of cutting through.
		float closeArc = Mathf.Sin( closeT * Mathf.PI ) * closeArcAmplitude;

		float farT = Mathf.Clamp01( ( planarDist - closeArcDistance ) / Mathf.Max( closeArcDistance, 0.01f ) );
		float wave = Mathf.Sin( _curvePhase ) * waveAmplitude * farT;

		float lateralOffset = closeArc + wave;
		float lookAhead = Mathf.Min( planarDist, curveLookAhead );
		float pathT = lookAhead / planarDist;
		Vector3 along = Vector3.Lerp( current, desired, pathT );
		along += lateral * lateralOffset;
		along.y += Mathf.Abs( Mathf.Sin( _curvePhase * 0.5f ) ) * travelLiftAmplitude * Mathf.Clamp01( planarDist / closeArcDistance );
		return along;
	}

	void UpdateMoveState( float dt )
	{
		float planarSpeed = new Vector3( _frameMoveDelta.x, 0f, _frameMoveDelta.z ).magnitude / dt;
		if ( _isMoving )
		{
			if ( planarSpeed < MoveExitPlanarSpeed )
				_isMoving = false;
		}
		else if ( planarSpeed > MoveEnterPlanarSpeed )
		{
			_isMoving = true;
		}
	}

	void UpdateFacing( PlayerController player )
	{
		Vector3 lookDir;
		if ( _isMoving )
		{
			lookDir = _frameMoveDelta;
			lookDir.y = 0f;
			if ( lookDir.sqrMagnitude < 0.0001f )
				lookDir = _planarVelocity;
		}
		else if ( _talking || IsHovered || _perchedDisplay != null )
		{
			lookDir = player.transform.position - transform.position;
		}
		else
		{
			return;
		}

		lookDir.y = 0f;
		if ( lookDir.sqrMagnitude < 0.0001f )
			return;

		FaceToward( lookDir, instant: false );
	}

	void FaceToward( Vector3 planarDir, bool instant )
	{
		planarDir.y = 0f;
		if ( planarDir.sqrMagnitude < 0.0001f )
			return;

		Quaternion target = Quaternion.LookRotation( planarDir.normalized, Vector3.up );
		if ( instant )
		{
			transform.rotation = target;
			return;
		}

		transform.rotation = Quaternion.Slerp( transform.rotation, target, 1f - Mathf.Exp( -turnSpeed * Time.deltaTime ) );
	}

	void UpdateAnimator()
	{
		if ( animator == null )
			return;
		animator.SetBool( IsMovingHash, _isMoving );
	}

	void UpdateFarGlow( PlayerController player )
	{
		if ( onFarGlowFeedback == null )
			return;

		float dist = HorizontalDistance( transform.position, player.transform.position );
		if ( !_farGlowPlaying && dist > farGlowDistance )
		{
			onFarGlowFeedback.Play();
			_farGlowPlaying = true;
		}
		else if ( _farGlowPlaying && dist < farGlowDistance - farGlowHysteresis )
		{
			onFarGlowFeedback.Stop();
			_farGlowPlaying = false;
		}
	}

	bool IsFocusedByPlayer()
	{
		PlayerController player = ResolvePlayer();
		if ( player == null || player.Interaction == null || _interactable == null )
			return false;
		return player.Interaction.Current == (IInteractable)_interactable;
	}

	static PlayerController ResolvePlayer()
	{
		if ( GameMode.Instance == null )
			return null;
		return GameMode.Instance.Player;
	}

	static float HorizontalDistance( Vector3 a, Vector3 b )
	{
		float dx = a.x - b.x;
		float dz = a.z - b.z;
		return Mathf.Sqrt( dx * dx + dz * dz );
	}

	static TreasurePileVisual FindNearestPile( Vector3 origin, float radius )
	{
		float bestSq = radius * radius;
		TreasurePileVisual best = null;
		TreasurePileVisual containing = null;
		float containingBestSq = float.MaxValue;

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
			if ( !IsValidPileNearPlayer( visual, origin ) )
				continue;

			float centerSq = HorizontalDistanceSq( origin, visual.transform.position );
			if ( visual.ContainsWorldPointXZ( origin ) || visual.HasAnyHeightAtWorld( origin ) )
			{
				if ( centerSq < containingBestSq )
				{
					containingBestSq = centerSq;
					containing = visual;
				}
				continue;
			}

			float halfExtent = EstimatePileHalfExtent( visual );
			float edgeDist = Mathf.Max( 0f, Mathf.Sqrt( centerSq ) - halfExtent );
			if ( edgeDist > radius )
				continue;

			float scoreSq = edgeDist * edgeDist;
			if ( scoreSq > bestSq )
				continue;
			bestSq = scoreSq;
			best = visual;
		}

		if ( containing != null )
			return containing;

		if ( best != null )
			return best;

		return FindNearestPileByOverlap( origin, radius );
	}

	static TreasurePileVisual FindNearestPileByOverlap( Vector3 origin, float radius )
	{
		int hitCount = Physics.OverlapSphereNonAlloc( origin, radius, OverlapScratch, ~0, QueryTriggerInteraction.Collide );
		float bestSq = radius * radius;
		TreasurePileVisual best = null;

		for ( int i = 0; i < hitCount; i++ )
		{
			Collider hit = OverlapScratch[ i ];
			if ( hit == null )
				continue;

			TreasurePileVisual visual = hit.GetComponentInParent<TreasurePileVisual>();
			if ( visual == null )
				continue;
			if ( !IsValidPileNearPlayer( visual, origin ) )
				continue;

			if ( visual.ContainsWorldPointXZ( origin ) || visual.HasAnyHeightAtWorld( origin ) )
				return visual;

			float sq = HorizontalDistanceSq( origin, visual.transform.position );
			if ( sq > bestSq )
				continue;
			bestSq = sq;
			best = visual;
		}

		return best;
	}

	static float EstimatePileHalfExtent( TreasurePileVisual visual )
	{
		if ( visual == null )
			return 3f;
		if ( visual.Heightfield != null && visual.Heightfield.IsInitialized )
			return visual.Heightfield.WorldSize * 0.5f;
		if ( visual.AuthoredWorldSize > 0.1f )
			return visual.AuthoredWorldSize * 0.5f;
		return 3f;
	}

	static float HorizontalDistanceSq( Vector3 a, Vector3 b )
	{
		float dx = a.x - b.x;
		float dz = a.z - b.z;
		return dx * dx + dz * dz;
	}

	static bool IsValidPileNearPlayer( TreasurePileVisual visual, Vector3 origin )
	{
		if ( visual == null )
			return false;
		if ( !visual.ContainsWorldPointXZ( origin ) && !visual.HasAnyHeightAtWorld( origin ) )
			return false;
		return HasRaisedTreasureSurfaceAt( origin );
	}

	static bool HasRaisedTreasureSurfaceAt( Vector3 worldPos )
	{
		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		if ( world == null )
			world = TreasureSurfaceWorld.EnsureExists();
		if ( world == null || world.Sampler == null )
			return false;

		if ( world.TryGetChunkCoord( worldPos, out TreasureChunkCoord coord ) )
			world.EnsureChunkLoaded( coord );

		if ( !world.Sampler.TrySample( worldPos, out TreasureSurfaceSample sample ) || !sample.Valid )
			return false;

		if ( !world.TryGetChunkCoord( worldPos, out coord ) )
			return false;

		TreasureChunk chunk = world.GetLoadedChunk( coord );
		if ( chunk == null || !chunk.Loaded || chunk.BaseHeight == null )
			return false;

		int cellX = Mathf.Clamp( sample.CellX, 0, chunk.Resolution - 1 );
		int cellZ = Mathf.Clamp( sample.CellZ, 0, chunk.Resolution - 1 );
		int index = chunk.Index( cellX, cellZ );
		float raise = sample.Height - chunk.BaseHeight[ index ];
		return raise > SurfaceRaiseEpsilon;
	}

	static Component FindNearestIncompleteDisplay( Vector3 origin, float radius )
	{
		int hitCount = Physics.OverlapSphereNonAlloc( origin, radius, OverlapScratch, ~0, QueryTriggerInteraction.Collide );
		float bestSq = radius * radius;
		Component best = null;

		for ( int i = 0; i < hitCount; i++ )
		{
			Collider hit = OverlapScratch[ i ];
			if ( hit == null )
				continue;

			Component display = ResolveCompletableDisplay( hit );
			if ( display == null )
				continue;
			if ( !TryGetDisplayProgressCounts( display, out _, out _, out bool complete ) )
				continue;
			if ( complete )
				continue;

			float sq = ( display.transform.position - origin ).sqrMagnitude;
			if ( sq > bestSq )
				continue;
			bestSq = sq;
			best = display;
		}

		return best;
	}

	static Component ResolveCompletableDisplay( Collider collider )
	{
		TypedDisplayTableInteractable typed = collider.GetComponentInParent<TypedDisplayTableInteractable>();
		if ( typed != null )
			return typed;

		GemConstellationInteractable constellation = collider.GetComponentInParent<GemConstellationInteractable>();
		if ( constellation != null )
			return constellation;

		ArtifactPresentationTableInteractable artifact = collider.GetComponentInParent<ArtifactPresentationTableInteractable>();
		if ( artifact != null )
			return artifact;

		return null;
	}
}
