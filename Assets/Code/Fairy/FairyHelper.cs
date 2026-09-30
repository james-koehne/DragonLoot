using System;

using FeedbackSystem;

using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.Splines;

public enum FairyMode
{
	Idle = 0,
	Guide = 1,
	World = 2
}

enum FairyGuidePhase
{
	None = 0,
	Guiding = 1,
	WaitingAhead = 2,
	WaitingAtDestination = 3,
	Farewell = 4
}

/// <summary>
/// Mode-driven fairy: Idle bob, Guide (lead player to a destination), or World (loop a spline).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent( typeof( FairyInteractable ) )]
public class FairyHelper : MonoBehaviour
{
	public const string AddressableKey = "Companions/FairyHelper";

	static readonly int IsMovingHash = Animator.StringToHash( "IsMoving" );
	static readonly int TalkHash = Animator.StringToHash( "Talk" );

	public static FairyHelper ActiveGuide { get; private set; }

	static int _guideSpawnGeneration;

	[Header( "Mode" )]
	[SerializeField]
	FairyMode mode = FairyMode.Idle;

	[Header( "Shared motion" )]
	[SerializeField]
	[Min( 0.1f )]
	float moveSpeed = 4.5f;

	[SerializeField]
	[Min( 0.01f )]
	float arriveDistance = 0.12f;

	[SerializeField]
	[Min( 1f )]
	float turnSpeed = 10f;

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
	float closeArcAmplitude = 0.55f;

	[SerializeField]
	[Min( 0.1f )]
	float closeArcDistance = 2.5f;

	[SerializeField]
	[Min( 0f )]
	float waveAmplitude = 0.85f;

	[SerializeField]
	[Min( 0.05f )]
	float waveFrequency = 1.4f;

	[SerializeField]
	[Min( 0.1f )]
	float curveLookAhead = 1.1f;

	[SerializeField]
	[Min( 0f )]
	float travelLiftAmplitude = 0.18f;

	[Header( "Guide" )]
	[SerializeField]
	Vector3 guideSpawnOffsetLocal = new Vector3( 1.1f, 1.0f, 0.85f );

	[SerializeField]
	[Min( 0.1f )]
	[Tooltip( "Guide flight speed. Should exceed typical player walk speed (~6)." )]
	float guideMoveSpeed = 8.5f;

	[SerializeField]
	[Min( 0.5f )]
	float guideWaitAheadDistance = 8f;

	[SerializeField]
	[Min( 0.5f )]
	float guideResumeDistance = 4.5f;

	[SerializeField]
	[Min( 0.05f )]
	float guideArriveDistance = 1.25f;

	[SerializeField]
	[Min( 0.5f )]
	float guidePlayerArriveDistance = 3.5f;

	[SerializeField]
	[Min( 0.1f )]
	float farGlowDistance = 4f;

	[SerializeField]
	[Min( 0f )]
	float farGlowHysteresis = 0.35f;

	[SerializeField]
	[TextArea( 1, 2 )]
	string guideFollowLine = "Follow me!";

	[SerializeField]
	[TextArea( 1, 2 )]
	string guideArriveLine = "Here it is, good luck.";

	[SerializeField]
	[Min( 0.5f )]
	float guideFarewellHoldSeconds = 2.5f;

	[Header( "World" )]
	[SerializeField]
	SplineContainer worldSpline;

	[SerializeField]
	[Min( 0.05f )]
	[Tooltip( "World units per second along the spline." )]
	float worldSplineSpeed = 1.25f;

	[SerializeField]
	[Min( 0.25f )]
	float worldTalkPauseSeconds = 2.5f;

	[SerializeField]
	[TextArea( 1, 2 )]
	string worldTalkLine = "Hello there!";

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

	const float MoveEnterPlanarSpeed = 0.12f;
	const float MoveExitPlanarSpeed = 0.035f;

	FairyInteractable _interactable;
	Vector3 _logicalPosition;
	Vector3 _planarVelocity;
	Vector3 _frameMoveDelta;
	float _bobPhase;
	float _curvePhase;
	float _curveSide = 1f;
	bool _isMoving;
	bool _farGlowPlaying;
	bool _talking;
	bool _addressableOwned;
	bool _worldPathPaused;
	float _worldDistance;
	float _worldTalkResumeAt = -1f;
	FairyGuidePhase _guidePhase;
	Vector3 _guideDestination;
	Transform _guideDestinationTransform;
	Action _guideOnFinished;
	bool _farewellBubbleSeen;
	float _farewellEndsAt = -1f;
	float _flightSpeed;

	public FairyMode Mode => mode;
	public bool IsTalking => _talking;
	public bool IsHovered => IsFocusedByPlayer();
	public bool IsFlying => _isMoving;
	public bool IsGuiding => mode == FairyMode.Guide && _guidePhase != FairyGuidePhase.None && _guidePhase != FairyGuidePhase.Farewell;
	public bool CanAcceptTalk => mode == FairyMode.World || mode == FairyMode.Idle;

	public static void StartGuide( Vector3 destination, Action onFinished = null )
	{
		StartGuideInternal( destination, null, onFinished );
	}

	public static void StartGuide( Transform destination, Action onFinished = null )
	{
		if ( destination == null )
		{
			Debug.LogWarning( "FairyHelper.StartGuide: destination transform is null." );
			return;
		}

		StartGuideInternal( destination.position, destination, onFinished );
	}

	static void StartGuideInternal( Vector3 destination, Transform destinationTransform, Action onFinished )
	{
		int generation = ++_guideSpawnGeneration;
		if ( ActiveGuide != null )
			ActiveGuide.CancelGuide( invokeFinished: false );

		PlayerController player = ResolvePlayer();
		if ( player == null )
		{
			Debug.LogWarning( "FairyHelper.StartGuide: no player available." );
			return;
		}

		Vector3 spawnPos = player.transform.position;
		Quaternion spawnRot = Quaternion.LookRotation( FlatForward( player.transform ), Vector3.up );
		AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync( AddressableKey, spawnPos, spawnRot );
		handle.Completed += op =>
		{
			if ( generation != _guideSpawnGeneration )
			{
				if ( op.Result != null )
					Addressables.ReleaseInstance( op.Result );
				return;
			}

			if ( op.Status != AsyncOperationStatus.Succeeded || op.Result == null )
			{
				Debug.LogWarning( "FairyHelper.StartGuide: failed to spawn '" + AddressableKey + "'." );
				return;
			}

			FairyHelper fairy = op.Result.GetComponent<FairyHelper>();
			if ( fairy == null )
			{
				Addressables.ReleaseInstance( op.Result );
				Debug.LogWarning( "FairyHelper.StartGuide: spawned prefab missing FairyHelper." );
				return;
			}

			fairy._addressableOwned = true;
			fairy.ApplyGuideSpawnOffset( player );
			fairy.BeginGuide( destination, destinationTransform, onFinished );
		};
	}

	void ApplyGuideSpawnOffset( PlayerController player )
	{
		if ( player == null )
			return;

		Vector3 spawnPos = BuildSpawnOffset( player, guideSpawnOffsetLocal );
		_logicalPosition = spawnPos;
		transform.position = spawnPos;
		_planarVelocity = Vector3.zero;
		_frameMoveDelta = Vector3.zero;
		_isMoving = false;
		FaceToward( FlatForward( player.transform ), instant: true );
	}

	public void BeginGuide( Vector3 destination, Action onFinished = null )
	{
		BeginGuide( destination, null, onFinished );
	}

	public void BeginGuide( Vector3 destination, Transform destinationTransform, Action onFinished )
	{
		mode = FairyMode.Guide;
		_guideDestination = destination;
		_guideDestinationTransform = destinationTransform;
		_guideOnFinished = onFinished;
		_guidePhase = FairyGuidePhase.Guiding;
		_farewellBubbleSeen = false;
		_farewellEndsAt = -1f;
		_flightSpeed = guideMoveSpeed;
		_logicalPosition = transform.position;
		ActiveGuide = this;

		PlayInteractFeedback();
		NotifyTalkStarted();
		Say( string.IsNullOrEmpty( guideFollowLine ) ? "Follow me!" : guideFollowLine );
		CancelInvoke( nameof( EndGuideIntroTalk ) );
		Invoke( nameof( EndGuideIntroTalk ), 1.6f );
	}

	void EndGuideIntroTalk()
	{
		if ( mode == FairyMode.Guide )
			NotifyTalkEnded();
	}

	public void CancelGuide( bool invokeFinished )
	{
		CancelInvoke( nameof( EndGuideIntroTalk ) );
		Action finished = _guideOnFinished;
		_guideOnFinished = null;
		_guidePhase = FairyGuidePhase.None;
		_guideDestinationTransform = null;
		if ( ActiveGuide == this )
			ActiveGuide = null;
		if ( invokeFinished && finished != null )
			finished();
		DespawnSelf();
	}

	public string ResolveTalkLine()
	{
		if ( mode == FairyMode.World )
			return string.IsNullOrEmpty( worldTalkLine ) ? "Hello there!" : worldTalkLine;
		return "Need a hand?";
	}

	void Awake()
	{
		EnsureRuntimeSetup();
		ClampTunables();
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

	void Start()
	{
		_logicalPosition = transform.position;
		_flightSpeed = moveSpeed;
		StartLoopAudio();

		if ( mode == FairyMode.World )
			SnapToWorldSpline();
	}

	void OnDestroy()
	{
		StopLoopAudio();
		if ( ActiveGuide == this )
			ActiveGuide = null;
	}

	void ClampTunables()
	{
		if ( closeArcDistance < 0.1f )
			closeArcDistance = 2.5f;
		if ( waveFrequency < 0.05f )
			waveFrequency = 1.4f;
		if ( curveLookAhead < 0.1f )
			curveLookAhead = 1.1f;
		if ( guideResumeDistance > guideWaitAheadDistance )
			guideResumeDistance = guideWaitAheadDistance * 0.6f;
		if ( worldSplineSpeed < 0.05f )
			worldSplineSpeed = 1.25f;
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

		switch ( mode )
		{
			case FairyMode.Guide:
				TickGuide( player );
				break;
			case FairyMode.World:
				TickWorld( player );
				break;
			default:
				TickIdle( player );
				break;
		}

		UpdateAnimator();
		if ( mode == FairyMode.Guide && player != null )
			UpdateFarGlow( player );
	}

	void TickIdle( PlayerController player )
	{
		_frameMoveDelta = Vector3.zero;
		_planarVelocity = Vector3.zero;
		_isMoving = false;
		ApplyIdleBob( _logicalPosition );
		if ( player != null && ( _talking || IsHovered ) )
			FaceToward( player.transform.position - transform.position, instant: false );
	}

	void TickGuide( PlayerController player )
	{
		if ( _guidePhase == FairyGuidePhase.None )
			return;

		if ( _guideDestinationTransform != null )
			_guideDestination = _guideDestinationTransform.position;

		if ( _guidePhase == FairyGuidePhase.Farewell )
		{
			TickFarewell();
			_frameMoveDelta = Vector3.zero;
			_planarVelocity = Vector3.zero;
			_isMoving = false;
			ApplyIdleBob( _logicalPosition );
			if ( player != null )
				FaceToward( player.transform.position - transform.position, instant: false );
			return;
		}

		if ( player == null )
		{
			HoldInPlace();
			return;
		}

		float playerDist = HorizontalDistance( _logicalPosition, player.transform.position );
		float destDist = HorizontalDistance( _logicalPosition, _guideDestination );

		if ( _guidePhase == FairyGuidePhase.WaitingAtDestination || destDist <= guideArriveDistance )
		{
			_guidePhase = FairyGuidePhase.WaitingAtDestination;
			HoldInPlace();
			FaceToward( player.transform.position - transform.position, instant: false );

			if ( playerDist <= guidePlayerArriveDistance )
				BeginFarewell();
			return;
		}

		if ( _guidePhase == FairyGuidePhase.WaitingAhead )
		{
			HoldInPlace();
			FaceToward( player.transform.position - transform.position, instant: false );
			if ( playerDist <= guideResumeDistance )
				_guidePhase = FairyGuidePhase.Guiding;
			return;
		}

		if ( playerDist > guideWaitAheadDistance )
		{
			_guidePhase = FairyGuidePhase.WaitingAhead;
			HoldInPlace();
			FaceToward( player.transform.position - transform.position, instant: false );
			return;
		}

		_guidePhase = FairyGuidePhase.Guiding;
		_flightSpeed = guideMoveSpeed;
		StepMove( _guideDestination );
		UpdateFacingWhileMoving( player );
	}

	void BeginFarewell()
	{
		_guidePhase = FairyGuidePhase.Farewell;
		_farewellBubbleSeen = false;
		_farewellEndsAt = Time.unscaledTime + guideFarewellHoldSeconds;
		PlayInteractFeedback();
		NotifyTalkStarted();
		Say( string.IsNullOrEmpty( guideArriveLine ) ? "Here it is, good luck." : guideArriveLine );
	}

	void TickFarewell()
	{
		if ( speechBubble != null && speechBubble.IsShowing )
		{
			_farewellBubbleSeen = true;
			return;
		}

		if ( !_farewellBubbleSeen && Time.unscaledTime < _farewellEndsAt )
			return;

		NotifyTalkEnded();
		Action finished = _guideOnFinished;
		_guideOnFinished = null;
		_guidePhase = FairyGuidePhase.None;
		if ( ActiveGuide == this )
			ActiveGuide = null;
		if ( finished != null )
			finished();
		DespawnSelf();
	}

	void TickWorld( PlayerController player )
	{
		TickWorldTalkPause();

		if ( !_worldPathPaused && worldSpline != null && worldSpline.Spline != null && worldSpline.Spline.Count >= 2 )
		{
			float length = worldSpline.CalculateLength();
			if ( length > 0.05f )
			{
				_worldDistance += worldSplineSpeed * Time.deltaTime;
				if ( _worldDistance >= length )
					_worldDistance %= length;
				if ( _worldDistance < 0f )
					_worldDistance += length;

				float t = _worldDistance / length;
				Vector3 pos;
				Vector3 tangent;
				Vector3 up;
				if ( CinematicSplineLook.TryEvaluate( worldSpline, t, out pos, out tangent, out up ) )
				{
					Vector3 previous = _logicalPosition;
					_logicalPosition = pos;
					_frameMoveDelta = pos - previous;
					float dt = Mathf.Max( Time.deltaTime, 0.0001f );
					_planarVelocity = new Vector3( _frameMoveDelta.x, 0f, _frameMoveDelta.z ) / dt;
					UpdateMoveState( dt );

					Vector3 display = pos;
					if ( !_isMoving && idleBobAmplitude > 0f )
					{
						_bobPhase += Time.deltaTime * idleBobSpeed;
						display.y += Mathf.Sin( _bobPhase ) * idleBobAmplitude * 0.5f;
					}
					transform.position = display;

					if ( _talking || IsHovered )
					{
						if ( player != null )
							FaceToward( player.transform.position - transform.position, instant: false );
					}
					else
					{
						FaceToward( tangent, instant: false );
					}
					return;
				}
			}
		}

		HoldInPlace();
		if ( player != null && ( _talking || IsHovered ) )
			FaceToward( player.transform.position - transform.position, instant: false );
	}

	void TickWorldTalkPause()
	{
		if ( !_worldPathPaused )
			return;
		if ( _talking )
			return;
		if ( Time.unscaledTime < _worldTalkResumeAt )
			return;
		_worldPathPaused = false;
		_worldTalkResumeAt = -1f;
	}

	void SnapToWorldSpline()
	{
		if ( worldSpline == null || worldSpline.Spline == null || worldSpline.Spline.Count < 2 )
			return;

		_worldDistance = 0f;
		Vector3 pos;
		Vector3 tangent;
		Vector3 up;
		if ( !CinematicSplineLook.TryEvaluate( worldSpline, 0f, out pos, out tangent, out up ) )
			return;

		_logicalPosition = pos;
		transform.position = pos;
		_planarVelocity = Vector3.zero;
		_frameMoveDelta = Vector3.zero;
		_isMoving = false;
		FaceToward( tangent, instant: true );
	}

	void HoldInPlace()
	{
		_frameMoveDelta = Vector3.zero;
		_planarVelocity = Vector3.zero;
		_isMoving = false;
		ApplyIdleBob( _logicalPosition );
	}

	public void NotifyTalkStarted()
	{
		_talking = true;
		if ( animator != null )
			animator.SetTrigger( TalkHash );

		if ( mode == FairyMode.World )
		{
			_worldPathPaused = true;
			_worldTalkResumeAt = -1f;
		}
	}

	public void NotifyTalkEnded()
	{
		_talking = false;
		if ( mode == FairyMode.World )
			_worldTalkResumeAt = Time.unscaledTime + worldTalkPauseSeconds;
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

	void StepMove( Vector3 desired )
	{
		Vector3 current = _logicalPosition;
		float dt = Mathf.Max( Time.deltaTime, 0.0001f );

		Vector3 to = desired - current;
		Vector3 planar = new Vector3( to.x, 0f, to.z );
		float planarDist = planar.magnitude;

		if ( planarDist <= arriveDistance && Mathf.Abs( to.y ) <= arriveDistance )
		{
			_frameMoveDelta = Vector3.zero;
			_planarVelocity = Vector3.zero;
			UpdateMoveState( dt );
			ApplyIdleBob( current );
			return;
		}

		if ( !_isMoving && planarDist > arriveDistance * 2f )
			PickCurveSide( planar );

		Vector3 seek = BuildCurvedSeekPoint( current, desired, planar, planarDist );
		float speed = _flightSpeed > 0.01f ? _flightSpeed : moveSpeed;
		Vector3 nextFree = Vector3.MoveTowards( current, seek, speed * dt );

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
			_curveSide = UnityEngine.Random.value < 0.5f ? -1f : 1f;
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
			_curveSide = UnityEngine.Random.value < 0.5f ? -1f : 1f;
	}

	Vector3 BuildCurvedSeekPoint( Vector3 current, Vector3 desired, Vector3 planar, float planarDist )
	{
		Vector3 dir = planar / planarDist;
		Vector3 lateral = Vector3.Cross( Vector3.up, dir ) * _curveSide;

		float closeT = 1f - Mathf.Clamp01( planarDist / closeArcDistance );
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

	void UpdateFacingWhileMoving( PlayerController player )
	{
		Vector3 lookDir;
		if ( _isMoving )
		{
			lookDir = _frameMoveDelta;
			lookDir.y = 0f;
			if ( lookDir.sqrMagnitude < 0.0001f )
				lookDir = _planarVelocity;
		}
		else if ( _talking || IsHovered )
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

	void DespawnSelf()
	{
		StopLoopAudio();
		if ( _farGlowPlaying && onFarGlowFeedback != null )
		{
			onFarGlowFeedback.Stop();
			_farGlowPlaying = false;
		}

		if ( _addressableOwned )
		{
			Addressables.ReleaseInstance( gameObject );
			return;
		}

		Destroy( gameObject );
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

	static Vector3 BuildSpawnOffset( PlayerController player, Vector3 localOffset )
	{
		float yaw = player.transform.eulerAngles.y;
		Quaternion rot = Quaternion.Euler( 0f, yaw, 0f );
		Vector3 offset = rot * localOffset;
		return player.transform.position + offset;
	}

	static Vector3 FlatForward( Transform t )
	{
		Vector3 forward = t.forward;
		forward.y = 0f;
		if ( forward.sqrMagnitude < 0.0001f )
			return Vector3.forward;
		return forward.normalized;
	}

	static float HorizontalDistance( Vector3 a, Vector3 b )
	{
		float dx = a.x - b.x;
		float dz = a.z - b.z;
		return Mathf.Sqrt( dx * dx + dz * dz );
	}
}
