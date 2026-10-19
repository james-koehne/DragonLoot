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
	World = 2,
	Delivery = 3
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
/// Mode-driven fairy: Idle bob, Guide (lead player to a destination), World (loop a spline),
/// or Delivery (hover with an Addressable takeable item underneath).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent( typeof( FairyInteractable ) )]
public class FairyHelper : MonoBehaviour
{
	public const string AddressableKey = "Companions/FairyHelper";
	public const string WorldHammerAddressableKey = "Assets/Prefabs/Build/WorldHammer.prefab";

	static readonly int IsMovingHash = Animator.StringToHash( "IsMoving" );
	static readonly int TalkHash = Animator.StringToHash( "Talk" );

	public static FairyHelper ActiveGuide { get; private set; }
	public static FairyHelper ActiveDelivery { get; private set; }

	static int _guideSpawnGeneration;
	static int _deliverySpawnGeneration;

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

	[SerializeField]
	[Min( 0.1f )]
	[Tooltip( "Logical hover height above walkable ground under the fairy." )]
	float hoverHeight = 1f;

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
	[Tooltip( "Player-local planar spawn offset. Y is ignored; hoverHeight is used instead." )]
	Vector3 guideSpawnOffsetLocal = new Vector3( 1.1f, 0f, 0.85f );

	[SerializeField]
	[Min( 0.1f )]
	[Tooltip( "Guide flight speed. Must exceed player sprint (~9) so the lead never feels sluggish." )]
	float guideMoveSpeed = 11.5f;

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

	[Header( "Delivery" )]
	[SerializeField]
	[Tooltip( "Player-local planar follow offset while offering cargo. Y is ignored; hoverHeight is used instead." )]
	Vector3 deliveryFollowOffsetLocal = new Vector3( 0f, 0f, 1.35f );

	[SerializeField]
	[Min( 0.1f )]
	float deliveryFollowSpeed = 7f;

	[SerializeField]
	Vector3 deliveryCargoOffsetLocal = new Vector3( 0f, -0.45f, 0f );

	[SerializeField]
	[TextArea( 1, 2 )]
	string deliveryOfferLine = "A gift for you!";

	[SerializeField]
	[TextArea( 1, 2 )]
	string deliveryTakenLine = "You're welcome!";

	[SerializeField]
	[Min( 0.5f )]
	float deliveryFarewellHoldSeconds = 2.5f;

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

	[SerializeField]
	Feedbacks onGuideTrailFeedback;

	const float MoveEnterPlanarSpeed = 0.12f;
	const float MoveExitPlanarSpeed = 0.035f;
	const float GroundProbeUp = 6f;
	const float GroundProbeDown = 24f;

	FairyInteractable _interactable;
	readonly RaycastHit[] _groundHits = new RaycastHit[ 16 ];
	Vector3 _logicalPosition;
	Vector3 _planarVelocity;
	Vector3 _frameMoveDelta;
	float _bobPhase;
	float _curvePhase;
	float _curveSide = 1f;
	bool _isMoving;
	bool _farGlowPlaying;
	bool _guideTrailPlaying;
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
	GameObject _deliveryCargo;
	bool _deliveryCargoAddressableOwned;
	bool _deliveryCargoAttached;
	bool _deliveryCargoTaken;
	bool _deliveryFarewell;
	Action _deliveryOnFinished;

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

	public static void StartGuideToMarker( string markerId, Action onFinished = null )
	{
		if ( string.IsNullOrEmpty( markerId ) )
		{
			Debug.LogWarning( "FairyHelper.StartGuideToMarker: marker id is empty." );
			return;
		}

		if ( !EventTargetRegistry.TryGetMarkerTransform( markerId, out Transform marker ) || marker == null )
		{
			Debug.LogWarning( "FairyHelper.StartGuideToMarker: no marker/volume for id '" + markerId + "'." );
			return;
		}

		StartGuide( marker, onFinished );
	}

	static void StartGuideInternal( Vector3 destination, Transform destinationTransform, Action onFinished )
	{
		int generation = ++_guideSpawnGeneration;
		if ( ActiveGuide != null )
			ActiveGuide.CancelGuide( invokeFinished: false );
		if ( ActiveDelivery != null )
			ActiveDelivery.CancelDelivery( invokeFinished: false );

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

	/// <summary>
	/// Spawns a fairy near the player that hovers with an Addressable takeable item underneath.
	/// Farewells and despawns once the cargo is taken (deactivated).
	/// </summary>
	public static void StartDelivery( string itemAddressableKey, Action onFinished = null )
	{
		if ( string.IsNullOrEmpty( itemAddressableKey ) )
		{
			Debug.LogWarning( "FairyHelper.StartDelivery: item addressable key is empty." );
			return;
		}

		if ( itemAddressableKey == WorldHammerAddressableKey && IsWorldHammerAlreadyUnlocked() )
		{
			if ( onFinished != null )
				onFinished();
			return;
		}

		int generation = ++_deliverySpawnGeneration;
		if ( ActiveGuide != null )
			ActiveGuide.CancelGuide( invokeFinished: false );
		if ( ActiveDelivery != null )
			ActiveDelivery.CancelDelivery( invokeFinished: false );

		PlayerController player = ResolvePlayer();
		if ( player == null )
		{
			Debug.LogWarning( "FairyHelper.StartDelivery: no player available." );
			return;
		}

		Vector3 spawnPos = player.transform.position;
		Quaternion spawnRot = Quaternion.LookRotation( FlatForward( player.transform ), Vector3.up );
		AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync( AddressableKey, spawnPos, spawnRot );
		handle.Completed += op =>
		{
			if ( generation != _deliverySpawnGeneration )
			{
				if ( op.Result != null )
					Addressables.ReleaseInstance( op.Result );
				return;
			}

			if ( op.Status != AsyncOperationStatus.Succeeded || op.Result == null )
			{
				Debug.LogWarning( "FairyHelper.StartDelivery: failed to spawn '" + AddressableKey + "'." );
				return;
			}

			FairyHelper fairy = op.Result.GetComponent<FairyHelper>();
			if ( fairy == null )
			{
				Addressables.ReleaseInstance( op.Result );
				Debug.LogWarning( "FairyHelper.StartDelivery: spawned prefab missing FairyHelper." );
				return;
			}

			fairy._addressableOwned = true;
			fairy.ApplyDeliverySpawnOffset( player );
			fairy.BeginDelivery( itemAddressableKey, onFinished );
		};
	}

	static bool IsWorldHammerAlreadyUnlocked()
	{
		PlayerController player = ResolvePlayer();
		if ( player == null || player.BuildMode == null )
			return false;
		return player.BuildMode.HasUnlockedWorldHammer;
	}

	void ApplyGuideSpawnOffset( PlayerController player )
	{
		if ( player == null )
			return;

		Vector3 spawnPos = SnapToHoverHeight( BuildSpawnOffset( player, guideSpawnOffsetLocal ) );
		_logicalPosition = spawnPos;
		transform.position = spawnPos;
		_planarVelocity = Vector3.zero;
		_frameMoveDelta = Vector3.zero;
		_isMoving = false;
		FaceToward( FlatForward( player.transform ), instant: true );
	}

	void ApplyDeliverySpawnOffset( PlayerController player )
	{
		if ( player == null )
			return;

		Vector3 spawnPos = SnapToHoverHeight( BuildSpawnOffset( player, deliveryFollowOffsetLocal ) );
		_logicalPosition = spawnPos;
		transform.position = spawnPos;
		_planarVelocity = Vector3.zero;
		_frameMoveDelta = Vector3.zero;
		_isMoving = false;
		FaceToward( player.transform.position - spawnPos, instant: true );
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
		StopGuideTrail();
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

	void BeginDelivery( string itemAddressableKey, Action onFinished )
	{
		mode = FairyMode.Delivery;
		_deliveryOnFinished = onFinished;
		_deliveryCargoAttached = false;
		_deliveryCargoTaken = false;
		_deliveryFarewell = false;
		_farewellBubbleSeen = false;
		_farewellEndsAt = -1f;
		_flightSpeed = moveSpeed;
		_logicalPosition = transform.position;
		ActiveDelivery = this;

		PlayInteractFeedback();
		NotifyTalkStarted();
		Say( string.IsNullOrEmpty( deliveryOfferLine ) ? "A gift for you!" : deliveryOfferLine );
		CancelInvoke( nameof( EndDeliveryIntroTalk ) );
		Invoke( nameof( EndDeliveryIntroTalk ), 1.6f );

		AsyncOperationHandle<GameObject> cargoHandle = Addressables.InstantiateAsync( itemAddressableKey );
		cargoHandle.Completed += op =>
		{
			if ( this == null || mode != FairyMode.Delivery || _deliveryFarewell )
			{
				if ( op.Result != null )
					Addressables.ReleaseInstance( op.Result );
				return;
			}

			if ( op.Status != AsyncOperationStatus.Succeeded || op.Result == null )
			{
				Debug.LogWarning( "FairyHelper.BeginDelivery: failed to spawn cargo '" + itemAddressableKey + "'." );
				CancelDelivery( invokeFinished: true );
				return;
			}

			AttachDeliveryCargo( op.Result );
		};
	}

	void AttachDeliveryCargo( GameObject cargo )
	{
		_deliveryCargo = cargo;
		_deliveryCargoAddressableOwned = true;
		_deliveryCargoAttached = true;
		Transform cargoTransform = cargo.transform;
		cargoTransform.SetParent( transform, false );
		cargoTransform.localPosition = deliveryCargoOffsetLocal;
		cargoTransform.localRotation = Quaternion.identity;
	}

	void EndDeliveryIntroTalk()
	{
		if ( mode == FairyMode.Delivery && !_deliveryFarewell )
			NotifyTalkEnded();
	}

	public void CancelDelivery( bool invokeFinished )
	{
		CancelInvoke( nameof( EndDeliveryIntroTalk ) );
		Action finished = _deliveryOnFinished;
		_deliveryOnFinished = null;
		_deliveryFarewell = false;
		if ( ActiveDelivery == this )
			ActiveDelivery = null;
		ReleaseOrDetachDeliveryCargo( releaseIfUntaken: true );
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
		if ( onGuideTrailFeedback == null )
		{
			Transform trail = transform.Find( "GuideTrailFeedbacks" );
			if ( trail != null )
				onGuideTrailFeedback = trail.GetComponent<Feedbacks>();
		}
		if ( speechBubble != null )
			speechBubble.BindFollow( transform );
	}

	void Start()
	{
		_logicalPosition = SnapToHoverHeight( transform.position );
		transform.position = _logicalPosition;
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
		if ( ActiveDelivery == this )
			ActiveDelivery = null;
		ReleaseOrDetachDeliveryCargo( releaseIfUntaken: true );
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
			case FairyMode.Delivery:
				TickDelivery( player );
				break;
			default:
				TickIdle( player );
				break;
		}

		UpdateAnimator();
		if ( mode == FairyMode.Guide && player != null )
			UpdateFarGlow( player );
		UpdateGuideTrail();
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

	void TickDelivery( PlayerController player )
	{
		if ( _deliveryFarewell )
		{
			HoldInPlace();
			if ( player != null )
				FaceToward( player.transform.position - transform.position, instant: false );
			TickDeliveryFarewell();
			return;
		}

		if ( player == null )
		{
			HoldInPlace();
		}
		else
		{
			Vector3 followTarget = SnapToHoverHeight( BuildSpawnOffset( player, deliveryFollowOffsetLocal ) );
			_flightSpeed = deliveryFollowSpeed > 0.01f ? deliveryFollowSpeed : moveSpeed;
			StepMove( followTarget );
			FaceToward( player.transform.position - transform.position, instant: false );
		}

		// Taken = deactivated or destroyed (build mode consumes the world prop permanently).
		if ( !_deliveryCargoTaken && _deliveryCargoAttached && ( _deliveryCargo == null || !_deliveryCargo.activeSelf ) )
			OnDeliveryCargoTaken();
	}

	void OnDeliveryCargoTaken()
	{
		_deliveryCargoTaken = true;
		ReleaseOrDetachDeliveryCargo( releaseIfUntaken: false );
		BeginDeliveryFarewell();
	}

	void BeginDeliveryFarewell()
	{
		if ( _deliveryFarewell )
			return;

		_deliveryFarewell = true;
		_farewellBubbleSeen = false;
		_farewellEndsAt = Time.unscaledTime + deliveryFarewellHoldSeconds;
		CancelInvoke( nameof( EndDeliveryIntroTalk ) );
		PlayInteractFeedback();
		NotifyTalkStarted();
		Say( string.IsNullOrEmpty( deliveryTakenLine ) ? "You're welcome!" : deliveryTakenLine );
	}

	void TickDeliveryFarewell()
	{
		if ( speechBubble != null && speechBubble.IsShowing )
		{
			_farewellBubbleSeen = true;
			return;
		}

		if ( !_farewellBubbleSeen && Time.unscaledTime < _farewellEndsAt )
			return;

		NotifyTalkEnded();
		Action finished = _deliveryOnFinished;
		_deliveryOnFinished = null;
		_deliveryFarewell = false;
		if ( ActiveDelivery == this )
			ActiveDelivery = null;
		if ( finished != null )
			finished();
		DespawnSelf();
	}

	/// <summary>
	/// Unparents cargo before fairy despawn. Releases Addressable cargo only when still untaken.
	/// </summary>
	void ReleaseOrDetachDeliveryCargo( bool releaseIfUntaken )
	{
		GameObject cargo = _deliveryCargo;
		bool owned = _deliveryCargoAddressableOwned;
		bool attached = _deliveryCargoAttached;
		_deliveryCargo = null;
		_deliveryCargoAddressableOwned = false;
		_deliveryCargoAttached = false;

		if ( cargo == null )
			return;

		bool taken = _deliveryCargoTaken || !cargo.activeSelf;
		if ( cargo.transform.parent == transform )
			cargo.transform.SetParent( null, true );

		// Only release untaken cargo we still own. Taken cargo is consumed by PlayerBuildMode.
		if ( releaseIfUntaken && owned && attached && !taken )
			Addressables.ReleaseInstance( cargo );
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
		StepMove( SnapToHoverHeight( _guideDestination ) );
		UpdateFacingWhileMoving( player );
	}

	void BeginFarewell()
	{
		_guidePhase = FairyGuidePhase.Farewell;
		_farewellBubbleSeen = false;
		_farewellEndsAt = Time.unscaledTime + guideFarewellHoldSeconds;
		StopGuideTrail();
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
					_logicalPosition = SnapToHoverHeight( pos );
					_frameMoveDelta = _logicalPosition - previous;
					float dt = Mathf.Max( Time.deltaTime, 0.0001f );
					_planarVelocity = new Vector3( _frameMoveDelta.x, 0f, _frameMoveDelta.z ) / dt;
					UpdateMoveState( dt );

					Vector3 display = _logicalPosition;
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

		_logicalPosition = SnapToHoverHeight( pos );
		transform.position = _logicalPosition;
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
		desired = SnapToHoverHeight( desired );
		Vector3 current = SnapToHoverHeight( _logicalPosition );
		_logicalPosition = current;
		float dt = Mathf.Max( Time.deltaTime, 0.0001f );

		Vector3 to = desired - current;
		Vector3 planar = new Vector3( to.x, 0f, to.z );
		float planarDist = planar.magnitude;

		// Arrive on planar distance only — hover Y is applied separately and must not gate movement.
		if ( planarDist <= arriveDistance )
		{
			_frameMoveDelta = Vector3.zero;
			_planarVelocity = Vector3.zero;
			UpdateMoveState( dt );
			ApplyIdleBob( desired );
			return;
		}

		if ( !_isMoving && planarDist > arriveDistance * 2f )
			PickCurveSide( planar );

		Vector3 seek = BuildCurvedSeekPoint( current, desired, planar, planarDist );
		float speed = _flightSpeed > 0.01f ? _flightSpeed : moveSpeed;
		// Keep MoveTowards planar so ground-hover Y never steals flight speed.
		Vector3 seekPlanar = new Vector3( seek.x, current.y, seek.z );
		Vector3 nextFree = Vector3.MoveTowards( current, seekPlanar, speed * dt );
		float travelLift = BuildTravelLift( planarDist );
		nextFree = SnapToHoverHeight( nextFree, travelLift );

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
		logical = SnapToHoverHeight( logical );
		_logicalPosition = logical;
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
		along.y = desired.y + BuildTravelLift( planarDist );
		return along;
	}

	float BuildTravelLift( float planarDist )
	{
		if ( travelLiftAmplitude <= 0f || planarDist <= 0.0001f )
			return 0f;
		return Mathf.Abs( Mathf.Sin( _curvePhase * 0.5f ) ) * travelLiftAmplitude * Mathf.Clamp01( planarDist / closeArcDistance );
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

	void UpdateGuideTrail()
	{
		bool wantTrail = mode == FairyMode.Guide && _guidePhase == FairyGuidePhase.Guiding && _isMoving;
		if ( wantTrail )
			PlayGuideTrail();
		else
			StopGuideTrail();
	}

	void PlayGuideTrail()
	{
		if ( _guideTrailPlaying || onGuideTrailFeedback == null )
			return;

		onGuideTrailFeedback.Play();
		_guideTrailPlaying = true;
	}

	void StopGuideTrail()
	{
		if ( !_guideTrailPlaying )
			return;

		if ( onGuideTrailFeedback != null )
			onGuideTrailFeedback.Stop();
		_guideTrailPlaying = false;
	}

	void DespawnSelf()
	{
		ReleaseOrDetachDeliveryCargo( releaseIfUntaken: true );
		StopLoopAudio();
		StopGuideTrail();
		if ( _farGlowPlaying && onFarGlowFeedback != null )
		{
			onFarGlowFeedback.Stop();
			_farGlowPlaying = false;
		}

		if ( ActiveDelivery == this )
			ActiveDelivery = null;
		if ( ActiveGuide == this )
			ActiveGuide = null;

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

	Vector3 SnapToHoverHeight( Vector3 pos, float extraLift = 0f )
	{
		float groundY;
		if ( !TrySampleGroundY( pos, out groundY ) )
			return pos;

		pos.y = groundY + hoverHeight + extraLift;
		return pos;
	}

	bool TrySampleGroundY( Vector3 pos, out float groundY )
	{
		groundY = pos.y;
		Vector3 origin = new Vector3( pos.x, pos.y + GroundProbeUp, pos.z );
		int hitCount = Physics.RaycastNonAlloc(
			origin,
			Vector3.down,
			_groundHits,
			GroundProbeUp + GroundProbeDown,
			Physics.DefaultRaycastLayers,
			QueryTriggerInteraction.Ignore );
		if ( hitCount <= 0 )
			return false;

		float bestDistance = float.MaxValue;
		bool found = false;
		for ( int i = 0; i < hitCount; i++ )
		{
			RaycastHit hit = _groundHits[ i ];
			if ( hit.collider == null )
				continue;
			if ( IsOwnCollider( hit.collider ) )
				continue;
			if ( !PlacementFloorSurface.IsWalkableFloorHit( in hit ) )
				continue;
			if ( hit.distance >= bestDistance )
				continue;

			bestDistance = hit.distance;
			groundY = hit.point.y;
			found = true;
		}

		return found;
	}

	bool IsOwnCollider( Collider collider )
	{
		Transform t = collider.transform;
		return t == transform || t.IsChildOf( transform );
	}

	Vector3 BuildSpawnOffset( PlayerController player, Vector3 localOffset )
	{
		float yaw = player.transform.eulerAngles.y;
		Quaternion rot = Quaternion.Euler( 0f, yaw, 0f );
		Vector3 planarOffset = localOffset;
		planarOffset.y = 0f;
		Vector3 offset = rot * planarOffset;
		Vector3 pos = player.transform.position + offset;
		// Seed Y near the player so the ground probe starts from a sensible height.
		pos.y = player.transform.position.y + hoverHeight;
		return pos;
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
