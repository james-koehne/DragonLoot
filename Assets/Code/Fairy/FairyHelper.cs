using System;
using System.Collections.Generic;

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
	Farewell = 4,
	AwaitingContinue = 5
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

	[SerializeField]
	[Tooltip( "Guide intro line waits for E before the fairy starts leading." )]
	bool awaitContinueOnGuideIntro = true;

	[SerializeField]
	[Tooltip( "Guide arrive / farewell line waits for E before despawn." )]
	bool awaitContinueOnGuideArrive = false;

	[Header( "Delivery" )]
	[SerializeField]
	[Tooltip( "Player-local planar follow offset while offering cargo. Y is ignored; hoverHeight is used instead." )]
	Vector3 deliveryFollowOffsetLocal = new Vector3( 0f, 0f, 1.35f );

	[SerializeField]
	[Min( 0.1f )]
	float deliveryFollowSpeed = 4.5f;

	[SerializeField]
	[Min( 0.25f )]
	[Tooltip( "Stay put while inside this radius of the preferred front point." )]
	float deliveryLooseArriveDistance = 1.35f;

	[SerializeField]
	[Min( 0.5f )]
	[Tooltip( "Only retarget the front point once farther than this (hysteresis)." )]
	float deliveryLooseRetargetDistance = 2.75f;

	[SerializeField]
	[Range( -1f, 1f )]
	[Tooltip( "Dot(playerForward, toFairy). Below this, fairy is treated as behind and may retarget." )]
	float deliveryFrontDotMin = 0.15f;

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

	[SerializeField]
	[Tooltip( "Delivery offer line waits for E; E also gives the cargo (e.g. hammer) without aiming at it." )]
	bool awaitContinueOnDeliveryOffer = true;

	[SerializeField]
	[Tooltip( "Delivery thank-you line waits for E before despawn." )]
	bool awaitContinueOnDeliveryFarewell = false;

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

	[SerializeField]
	[Tooltip( "World / idle talk waits for E before ending the talk." )]
	bool awaitContinueOnWorldTalk = false;

	[Header( "Attention Talk" )]
	[SerializeField]
	[Min( 0f )]
	[Tooltip( "How far the player can look away from the fairy while attention talk is active." )]
	float attentionLookSlackDegrees = 18f;

	[SerializeField]
	[Min( 0.01f )]
	[Tooltip( "How quickly look is resisted when the player tries to look past the attention slack." )]
	float attentionLookClampSpeed = 8f;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Ignore E briefly after attention starts so the same press cannot skip the line." )]
	float attentionContinueIgnoreSeconds = 0.2f;

	[SerializeField]
	[Min( 0.5f )]
	float attentionEyeHeightFallback = 1.6f;

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
	string _guideArriveLineOverride;
	Action _guideOnFinished;
	Vector3[] _guideWaypoints;
	int _guideWaypointIndex;
	Vector3 _guidePathDestination;
	bool _farewellBubbleSeen;
	float _farewellEndsAt = -1f;
	float _flightSpeed;
	GameObject _deliveryCargo;
	bool _deliveryCargoAddressableOwned;
	bool _deliveryCargoAttached;
	bool _deliveryCargoTaken;
	bool _deliveryFarewell;
	Action _deliveryOnFinished;
	Vector3 _deliverySoftTarget;
	bool _hasDeliverySoftTarget;
	bool _attentionActive;
	bool _attentionCargoBlocked;
	float _attentionContinueReadyAt = -1f;
	Action _attentionOnContinue;
	PlayerController _attentionPlayer;

	public FairyMode Mode => mode;
	public bool IsTalking => _talking;
	public bool IsHovered => IsFocusedByPlayer();
	public bool IsFlying => _isMoving;
	public bool IsGuiding =>
		mode == FairyMode.Guide
		&& _guidePhase != FairyGuidePhase.None
		&& _guidePhase != FairyGuidePhase.Farewell
		&& _guidePhase != FairyGuidePhase.AwaitingContinue;
	public bool IsAttentionTalkActive => _attentionActive;
	public bool CanAcceptTalk =>
		( mode == FairyMode.World || mode == FairyMode.Idle ) && !_attentionActive;
	public bool AwaitContinueOnWorldTalk => awaitContinueOnWorldTalk;

	public static void StartGuide( Vector3 destination, Action onFinished = null )
	{
		StartGuideInternal( destination, null, null, null, onFinished );
	}

	public static void StartGuide( Transform destination, Action onFinished = null )
	{
		StartGuide( destination, null, null, onFinished );
	}

	/// <summary>
	/// Spawns a guide fairy. When <paramref name="followLineOverride"/> is non-empty it replaces the prefab follow line.
	/// </summary>
	public static void StartGuide( Transform destination, string followLineOverride, Action onFinished = null )
	{
		StartGuide( destination, followLineOverride, null, onFinished );
	}

	/// <summary>
	/// Spawns a guide fairy. Non-empty overrides replace the prefab follow / arrival lines.
	/// </summary>
	public static void StartGuide( Transform destination, string followLineOverride, string arriveLineOverride, Action onFinished = null )
	{
		if ( destination == null )
		{
			Debug.LogWarning( "FairyHelper.StartGuide: destination transform is null." );
			if ( onFinished != null )
				onFinished();
			return;
		}

		StartGuideInternal( destination.position, destination, followLineOverride, arriveLineOverride, onFinished );
	}

	public static void StartGuideToMarker( string markerId, Action onFinished = null )
	{
		StartGuideToMarker( markerId, null, null, onFinished );
	}

	/// <summary>
	/// Guides to a marker/volume/spawn id, or a <see cref="BuildableObject"/> persistence id.
	/// When <paramref name="followLineOverride"/> is non-empty it replaces the prefab follow line.
	/// </summary>
	public static void StartGuideToMarker( string markerId, string followLineOverride, Action onFinished = null )
	{
		StartGuideToMarker( markerId, followLineOverride, null, onFinished );
	}

	/// <summary>
	/// Guides to a marker/volume/spawn id, or a <see cref="BuildableObject"/> persistence id.
	/// Non-empty overrides replace the prefab follow / arrival lines.
	/// </summary>
	public static void StartGuideToMarker( string markerId, string followLineOverride, string arriveLineOverride, Action onFinished = null )
	{
		if ( string.IsNullOrEmpty( markerId ) )
		{
			Debug.LogWarning( "FairyHelper.StartGuideToMarker: marker id is empty." );
			if ( onFinished != null )
				onFinished();
			return;
		}

		Transform marker = null;
		if ( !EventTargetRegistry.TryGetMarkerTransform( markerId, out marker ) || marker == null )
			marker = ResolveBuildableMarker( markerId );

		if ( marker == null )
		{
			Debug.LogWarning( "FairyHelper.StartGuideToMarker: no marker/volume/buildable for id '" + markerId + "'." );
			if ( onFinished != null )
				onFinished();
			return;
		}

		StartGuide( marker, followLineOverride, arriveLineOverride, onFinished );
	}

	static Transform ResolveBuildableMarker( string persistenceId )
	{
		if ( string.IsNullOrEmpty( persistenceId ) )
			return null;

		IReadOnlyList<BuildableObject> active = BuildableObject.Active;
		for ( int i = 0; i < active.Count; i++ )
		{
			BuildableObject buildable = active[ i ];
			if ( buildable == null )
				continue;
			if ( buildable.ResolvePersistenceId() != persistenceId )
				continue;
			return buildable.transform;
		}

		return null;
	}

	static void StartGuideInternal( Vector3 destination, Transform destinationTransform, string followLineOverride, string arriveLineOverride, Action onFinished )
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
			if ( onFinished != null )
				onFinished();
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
				if ( onFinished != null )
					onFinished();
				return;
			}

			FairyHelper fairy = op.Result.GetComponent<FairyHelper>();
			if ( fairy == null )
			{
				Addressables.ReleaseInstance( op.Result );
				Debug.LogWarning( "FairyHelper.StartGuide: spawned prefab missing FairyHelper." );
				if ( onFinished != null )
					onFinished();
				return;
			}

			fairy._addressableOwned = true;
			fairy.ApplyGuideSpawnOffset( player );
			fairy.BeginGuide( destination, destinationTransform, followLineOverride, arriveLineOverride, onFinished );
		};
	}

	/// <summary>
	/// Spawns a fairy near the player that hovers with an Addressable takeable item underneath.
	/// Farewells and despawns once the cargo is taken (deactivated).
	/// </summary>
	public static void StartDelivery( string itemAddressableKey, Action onFinished = null )
	{
		StartDelivery( itemAddressableKey, null, onFinished );
	}

	/// <summary>
	/// Spawns a delivery fairy. When <paramref name="offerLineOverride"/> is non-empty it replaces the prefab offer line.
	/// </summary>
	public static void StartDelivery( string itemAddressableKey, string offerLineOverride, Action onFinished = null )
	{
		StartDeliveryInternal( itemAddressableKey, offerLineOverride, onFinished, useExplicitPose: false, Vector3.zero, Quaternion.identity );
	}

	/// <summary>
	/// Spawns a delivery fairy at an explicit world pose (skips player-relative delivery offset).
	/// </summary>
	public static void StartDeliveryAt(
		string itemAddressableKey,
		Vector3 spawnPosition,
		Quaternion spawnRotation,
		string offerLineOverride,
		Action onFinished = null )
	{
		StartDeliveryInternal( itemAddressableKey, offerLineOverride, onFinished, useExplicitPose: true, spawnPosition, spawnRotation );
	}

	static void StartDeliveryInternal(
		string itemAddressableKey,
		string offerLineOverride,
		Action onFinished,
		bool useExplicitPose,
		Vector3 explicitSpawnPos,
		Quaternion explicitSpawnRot )
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

		Vector3 spawnPos = useExplicitPose
			? explicitSpawnPos
			: player.transform.position;
		Quaternion spawnRot = useExplicitPose
			? explicitSpawnRot
			: Quaternion.LookRotation( FlatForward( player.transform ), Vector3.up );
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
			if ( useExplicitPose )
				fairy.ApplyExplicitDeliverySpawn( spawnPos, spawnRot, player );
			else
				fairy.ApplyDeliverySpawnOffset( player );
			fairy.BeginDelivery( itemAddressableKey, offerLineOverride, onFinished );
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

		Vector3 spawnPos = BuildSpawnOffset( player, guideSpawnOffsetLocal );
		spawnPos = awaitContinueOnGuideIntro
			? SnapToEyeHeight( spawnPos, player )
			: SnapToHoverHeight( spawnPos );
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

		Vector3 spawnPos = BuildSpawnOffset( player, deliveryFollowOffsetLocal );
		spawnPos = awaitContinueOnDeliveryOffer
			? SnapToEyeHeight( spawnPos, player )
			: SnapToHoverHeight( spawnPos );
		_logicalPosition = spawnPos;
		transform.position = spawnPos;
		_planarVelocity = Vector3.zero;
		_frameMoveDelta = Vector3.zero;
		_isMoving = false;
		_deliverySoftTarget = spawnPos;
		_hasDeliverySoftTarget = true;
		FaceToward( player.transform.position - spawnPos, instant: true );
	}

	void ApplyExplicitDeliverySpawn( Vector3 spawnPos, Quaternion spawnRot, PlayerController player )
	{
		spawnPos = awaitContinueOnDeliveryOffer
			? SnapToEyeHeight( spawnPos, player )
			: SnapToHoverHeight( spawnPos );
		_logicalPosition = spawnPos;
		transform.SetPositionAndRotation( spawnPos, spawnRot );
		_planarVelocity = Vector3.zero;
		_frameMoveDelta = Vector3.zero;
		_isMoving = false;
		_deliverySoftTarget = spawnPos;
		_hasDeliverySoftTarget = true;
		if ( player != null )
			FaceToward( player.transform.position - spawnPos, instant: true );
	}

	public void BeginGuide( Vector3 destination, Action onFinished = null )
	{
		BeginGuide( destination, null, null, null, onFinished );
	}

	public void BeginGuide( Vector3 destination, Transform destinationTransform, Action onFinished )
	{
		BeginGuide( destination, destinationTransform, null, null, onFinished );
	}

	public void BeginGuide( Vector3 destination, Transform destinationTransform, string followLineOverride, Action onFinished )
	{
		BeginGuide( destination, destinationTransform, followLineOverride, null, onFinished );
	}

	public void BeginGuide( Vector3 destination, Transform destinationTransform, string followLineOverride, string arriveLineOverride, Action onFinished )
	{
		mode = FairyMode.Guide;
		_guideDestination = destination;
		_guideDestinationTransform = destinationTransform;
		_guideArriveLineOverride = arriveLineOverride;
		_guideOnFinished = onFinished;
		_farewellBubbleSeen = false;
		_farewellEndsAt = -1f;
		_flightSpeed = guideMoveSpeed;
		_logicalPosition = transform.position;
		ActiveGuide = this;

		string followLine = !string.IsNullOrEmpty( followLineOverride )
			? followLineOverride
			: ( string.IsNullOrEmpty( guideFollowLine ) ? "Follow me!" : guideFollowLine );

		PlayInteractFeedback();
		NotifyTalkStarted();

		if ( awaitContinueOnGuideIntro )
		{
			_guidePhase = FairyGuidePhase.AwaitingContinue;
			ClearGuidePath();
			BeginAttentionTalk( followLine, OnGuideIntroContinue );
			return;
		}

		_guidePhase = FairyGuidePhase.Guiding;
		BuildGuidePath( destination );
		Say( followLine );
		CancelInvoke( nameof( EndGuideIntroTalk ) );
		Invoke( nameof( EndGuideIntroTalk ), 1.6f );
	}

	void OnGuideIntroContinue()
	{
		if ( mode != FairyMode.Guide )
			return;

		_guidePhase = FairyGuidePhase.Guiding;
		BuildGuidePath( _guideDestination );
		NotifyTalkEnded();
	}

	void EndGuideIntroTalk()
	{
		if ( mode == FairyMode.Guide && !_attentionActive )
			NotifyTalkEnded();
	}

	public void CancelGuide( bool invokeFinished )
	{
		CancelInvoke( nameof( EndGuideIntroTalk ) );
		EndAttentionTalk( invokeContinue: false );
		StopGuideTrail();
		Action finished = _guideOnFinished;
		_guideOnFinished = null;
		_guideArriveLineOverride = null;
		_guidePhase = FairyGuidePhase.None;
		_guideDestinationTransform = null;
		ClearGuidePath();
		if ( ActiveGuide == this )
			ActiveGuide = null;
		if ( invokeFinished && finished != null )
			finished();
		DespawnSelf();
	}

	void BeginDelivery( string itemAddressableKey, string offerLineOverride, Action onFinished )
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
		_hasDeliverySoftTarget = false;
		ActiveDelivery = this;

		string offerLine = !string.IsNullOrEmpty( offerLineOverride )
			? offerLineOverride
			: ( string.IsNullOrEmpty( deliveryOfferLine ) ? "A gift for you!" : deliveryOfferLine );

		PlayInteractFeedback();
		NotifyTalkStarted();

		if ( awaitContinueOnDeliveryOffer )
			BeginAttentionTalk( offerLine, OnDeliveryOfferContinue );
		else
		{
			Say( offerLine );
			CancelInvoke( nameof( EndDeliveryIntroTalk ) );
			Invoke( nameof( EndDeliveryIntroTalk ), 1.6f );
		}

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

	void OnDeliveryOfferContinue()
	{
		if ( mode != FairyMode.Delivery || _deliveryFarewell )
			return;

		ForceGiveDeliveryCargo();
		NotifyTalkEnded();
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
		if ( awaitContinueOnDeliveryOffer && _attentionActive )
			SetDeliveryCargoTakeable( false );
	}

	void EndDeliveryIntroTalk()
	{
		if ( mode == FairyMode.Delivery && !_deliveryFarewell && !_attentionActive )
			NotifyTalkEnded();
	}

	public void CancelDelivery( bool invokeFinished )
	{
		CancelInvoke( nameof( EndDeliveryIntroTalk ) );
		EndAttentionTalk( invokeContinue: false );
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

	/// <summary>
	/// World / idle talk entry used by <see cref="FairyInteractable"/>. Returns true when E-gated.
	/// </summary>
	public bool BeginOptionalAttentionTalk( string line, Action onContinue )
	{
		PlayInteractFeedback();
		NotifyTalkStarted();
		if ( !awaitContinueOnWorldTalk )
		{
			Say( line );
			return false;
		}

		BeginAttentionTalk( line, () =>
		{
			NotifyTalkEnded();
			if ( onContinue != null )
				onContinue();
		} );
		return true;
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
		EndAttentionTalk( invokeContinue: false );
		StopLoopAudio();
		if ( ActiveGuide == this )
			ActiveGuide = null;
		if ( ActiveDelivery == this )
			ActiveDelivery = null;
		ReleaseOrDetachDeliveryCargo( releaseIfUntaken: true );
		if ( speechBubble != null )
		{
			Destroy( speechBubble.gameObject );
			speechBubble = null;
		}
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

		if ( _attentionActive )
			TickAttentionTalk( player );

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

	void BeginAttentionTalk( string line, Action onContinue )
	{
		EndAttentionTalk( invokeContinue: false );

		PlayerController player = ResolvePlayer();
		_attentionActive = true;
		_attentionOnContinue = onContinue;
		_attentionPlayer = player;
		_attentionContinueReadyAt = Time.unscaledTime + Mathf.Max( 0f, attentionContinueIgnoreSeconds );

		if ( player != null )
		{
			HoldAtEyeLevel( player );
			TickAttentionLook( player, instant: true );
		}

		if ( mode == FairyMode.Delivery && awaitContinueOnDeliveryOffer )
			SetDeliveryCargoTakeable( false );

		Say( line );
		if ( speechBubble != null )
			speechBubble.SetAwaitingContinue( true );
	}

	void EndAttentionTalk( bool invokeContinue )
	{
		if ( !_attentionActive )
		{
			if ( speechBubble != null )
				speechBubble.SetAwaitingContinue( false );
			return;
		}

		Action continueCallback = _attentionOnContinue;
		_attentionOnContinue = null;
		_attentionActive = false;
		_attentionContinueReadyAt = -1f;
		_attentionPlayer = null;

		if ( speechBubble != null )
			speechBubble.SetAwaitingContinue( false );

		if ( _attentionCargoBlocked )
			SetDeliveryCargoTakeable( true );

		if ( invokeContinue && continueCallback != null )
			continueCallback();
	}

	void TickAttentionTalk( PlayerController player )
	{
		if ( !_attentionActive )
			return;

		if ( player == null )
			player = _attentionPlayer;
		if ( player == null )
			player = ResolvePlayer();

		if ( player != null )
		{
			HoldAtEyeLevel( player );
			FaceToward( player.transform.position - transform.position, instant: false );
		}
		else
		{
			HoldInPlace();
		}

		if ( Time.unscaledTime < _attentionContinueReadyAt )
			return;

		// Delivery offer: wait until cargo exists so E can give it immediately.
		if ( mode == FairyMode.Delivery && !_deliveryFarewell && !_deliveryCargoAttached )
			return;

		if ( !WasAttentionContinuePressed() )
			return;

		EndAttentionTalk( invokeContinue: true );
	}

	bool WasAttentionContinuePressed()
	{
		InputController inputController = InputController.Instance;
		if ( inputController == null || !inputController.InputEnabled )
			return false;

		GameInput input = inputController.GameInput;
		if ( input == null || input.ContextualInteract == null )
			return false;

		return input.ContextualInteract.WasPressedThisFrame();
	}

	void HoldAtEyeLevel( PlayerController player )
	{
		Vector3 pos = _logicalPosition;
		pos.y = ResolveEyeHeight( player );
		_logicalPosition = pos;
		_frameMoveDelta = Vector3.zero;
		_planarVelocity = Vector3.zero;
		_isMoving = false;

		// Do not use ApplyIdleBob here — it snaps to ground hover height.
		Vector3 display = pos;
		if ( idleBobAmplitude > 0f )
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

	void TickAttentionLook( PlayerController player, bool instant )
	{
		if ( player == null )
			return;

		FirstPersonCameraController camera = player.GetComponentInChildren<FirstPersonCameraController>( true );
		if ( camera == null && GameMode.Instance != null && GameMode.Instance.cameraController != null )
			camera = GameMode.Instance.cameraController.FirstPerson;
		if ( camera == null || camera.Camera == null )
			return;

		// Use logical (non-bobbing) fairy origin so idle bob does not jitter the clamp.
		if ( !TryResolveAttentionLookAim( player, camera, out float desiredYaw, out float desiredPitch ) )
			return;

		float yaw = player.transform.eulerAngles.y;
		float pitch = camera.Pitch;

		if ( instant )
		{
			player.transform.rotation = Quaternion.Euler( 0f, desiredYaw, 0f );
			camera.SetPitch( desiredPitch );
			return;
		}

		float slack = Mathf.Max( 0f, attentionLookSlackDegrees );
		float resist = Mathf.Clamp01( attentionLookClampSpeed * Time.unscaledDeltaTime );

		float yawDelta = Mathf.DeltaAngle( desiredYaw, yaw );
		if ( Mathf.Abs( yawDelta ) > slack )
		{
			float boundaryYaw = desiredYaw + Mathf.Sign( yawDelta ) * slack;
			yaw = Mathf.LerpAngle( yaw, boundaryYaw, resist );
		}

		float pitchDelta = pitch - desiredPitch;
		if ( Mathf.Abs( pitchDelta ) > slack )
		{
			float boundaryPitch = desiredPitch + Mathf.Sign( pitchDelta ) * slack;
			pitch = Mathf.Lerp( pitch, boundaryPitch, resist );
		}

		player.transform.rotation = Quaternion.Euler( 0f, yaw, 0f );
		camera.SetPitch( pitch );
	}

	bool TryResolveAttentionLookAim(
		PlayerController player,
		FirstPersonCameraController camera,
		out float desiredYaw,
		out float desiredPitch )
	{
		desiredYaw = 0f;
		desiredPitch = 0f;
		if ( player == null || camera == null || camera.Camera == null )
			return false;

		Vector3 origin = camera.Camera.transform.position;
		Vector3 lookPoint = _logicalPosition + Vector3.up * 0.15f;
		Vector3 toTarget = lookPoint - origin;
		if ( toTarget.sqrMagnitude < 0.0001f )
			return false;

		Quaternion look = Quaternion.LookRotation( toTarget.normalized, Vector3.up );
		Vector3 euler = look.eulerAngles;
		desiredYaw = euler.y;
		desiredPitch = Mathf.Clamp( NormalizePitch( euler.x ), -85f, 85f );
		return true;
	}

	static float NormalizePitch( float eulerX )
	{
		if ( eulerX > 180f )
			eulerX -= 360f;
		return eulerX;
	}

	float ResolveEyeHeight( PlayerController player )
	{
		if ( player != null && player.CameraMount != null )
			return player.CameraMount.position.y;
		if ( player != null )
			return player.transform.position.y + attentionEyeHeightFallback;
		return transform.position.y;
	}

	Vector3 SnapToEyeHeight( Vector3 pos, PlayerController player )
	{
		pos.y = ResolveEyeHeight( player );
		return pos;
	}

	void SetDeliveryCargoTakeable( bool takeable )
	{
		_attentionCargoBlocked = !takeable;
		if ( _deliveryCargo == null )
			return;

		InteractableBase[] interactables = _deliveryCargo.GetComponentsInChildren<InteractableBase>( true );
		for ( int i = 0; i < interactables.Length; i++ )
		{
			if ( interactables[ i ] != null )
				interactables[ i ].enabled = takeable;
		}
	}

	void ForceGiveDeliveryCargo()
	{
		if ( _deliveryCargoTaken || _deliveryCargo == null )
			return;

		SetDeliveryCargoTakeable( true );
		PlayerController player = ResolvePlayer();
		if ( player == null )
			return;

		WorldHammerInteractable hammer = _deliveryCargo.GetComponentInChildren<WorldHammerInteractable>( true );
		if ( hammer != null )
		{
			if ( hammer.CanInteract( player ) )
				hammer.Interact( player );
			else if ( player.BuildMode != null )
				player.BuildMode.TryTakeWorldHammer( hammer );
			return;
		}

		InteractableBase interactable = _deliveryCargo.GetComponentInChildren<InteractableBase>( true );
		if ( interactable != null && interactable.CanInteract( player ) )
			interactable.Interact( player );
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
			if ( _attentionActive )
			{
				if ( player != null )
					FaceToward( player.transform.position - transform.position, instant: false );
				return;
			}

			HoldInPlace();
			if ( player != null )
				FaceToward( player.transform.position - transform.position, instant: false );
			TickDeliveryFarewell();
			return;
		}

		if ( _attentionActive )
		{
			// Position / look handled by TickAttentionTalk.
			return;
		}

		if ( player == null )
		{
			HoldInPlace();
		}
		else
		{
			TickLooseFrontFollow( player );
		}

		// Taken = deactivated or destroyed (build mode consumes the world prop permanently).
		if ( !_deliveryCargoTaken && _deliveryCargoAttached && ( _deliveryCargo == null || !_deliveryCargo.activeSelf ) )
			OnDeliveryCargoTaken();
	}

	/// <summary>
	/// Softly keep a front-of-player hover point. Does not aggressively chase every look turn —
	/// retargets only when too far or clearly behind the player.
	/// </summary>
	void TickLooseFrontFollow( PlayerController player )
	{
		Vector3 preferred = SnapToHoverHeight( BuildSpawnOffset( player, deliveryFollowOffsetLocal ) );
		float distToPreferred = HorizontalDistance( _logicalPosition, preferred );

		Vector3 toFairy = _logicalPosition - player.transform.position;
		toFairy.y = 0f;
		float frontDot = -1f;
		if ( toFairy.sqrMagnitude > 0.0001f )
			frontDot = Vector3.Dot( FlatForward( player.transform ), toFairy.normalized );

		bool behindPlayer = frontDot < deliveryFrontDotMin;
		bool needRetarget = !_hasDeliverySoftTarget
			|| distToPreferred >= deliveryLooseRetargetDistance
			|| behindPlayer;

		if ( needRetarget )
		{
			_deliverySoftTarget = preferred;
			_hasDeliverySoftTarget = true;
		}

		float distToSoft = HorizontalDistance( _logicalPosition, _deliverySoftTarget );
		if ( distToSoft <= deliveryLooseArriveDistance && !behindPlayer )
		{
			HoldInPlace();
			if ( _talking || IsHovered )
				FaceToward( player.transform.position - transform.position, instant: false );
			return;
		}

		_flightSpeed = deliveryFollowSpeed > 0.01f ? deliveryFollowSpeed : moveSpeed;
		StepMove( SnapToHoverHeight( _deliverySoftTarget ) );
		if ( _talking || IsHovered || distToSoft > deliveryLooseArriveDistance * 1.5f )
			FaceToward( player.transform.position - transform.position, instant: false );
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
		string takenLine = string.IsNullOrEmpty( deliveryTakenLine ) ? "You're welcome!" : deliveryTakenLine;
		if ( awaitContinueOnDeliveryFarewell )
		{
			BeginAttentionTalk( takenLine, FinishDeliveryFarewell );
			return;
		}

		Say( takenLine );
	}

	void FinishDeliveryFarewell()
	{
		NotifyTalkEnded();
		Action finished = _deliveryOnFinished;
		_deliveryOnFinished = null;
		_deliveryFarewell = false;
		_farewellBubbleSeen = false;
		if ( ActiveDelivery == this )
			ActiveDelivery = null;
		if ( finished != null )
			finished();
		DespawnSelf();
	}

	void TickDeliveryFarewell()
	{
		if ( _attentionActive )
			return;

		if ( speechBubble != null && speechBubble.IsShowing )
		{
			_farewellBubbleSeen = true;
			return;
		}

		if ( !_farewellBubbleSeen && Time.unscaledTime < _farewellEndsAt )
			return;

		FinishDeliveryFarewell();
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

		if ( _guidePhase == FairyGuidePhase.AwaitingContinue )
		{
			if ( player != null )
				FaceToward( player.transform.position - transform.position, instant: false );
			return;
		}

		if ( _guideDestinationTransform != null )
		{
			Vector3 liveDest = _guideDestinationTransform.position;
			_guideDestination = liveDest;
			float moved = HorizontalDistance( liveDest, _guidePathDestination );
			if ( moved > 1.5f )
				BuildGuidePath( liveDest );
		}

		if ( _guidePhase == FairyGuidePhase.Farewell )
		{
			if ( _attentionActive )
			{
				if ( player != null )
					FaceToward( player.transform.position - transform.position, instant: false );
				return;
			}

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
		StepMove( SnapToHoverHeight( ResolveGuideMoveTarget() ) );
		UpdateFacingWhileMoving( player );
	}

	void BuildGuidePath( Vector3 destination )
	{
		_guidePathDestination = destination;
		_guideWaypointIndex = 0;
		_guideWaypoints = null;

		TreasureSurfacePathSettings settings = TreasureSurfacePathSettings.Default;
		if ( TreasureSurfacePathfinder.TryFindPath( _logicalPosition, destination, settings, out TreasureSurfacePath path )
			&& path != null
			&& path.Success
			&& path.WaypointCount > 0 )
		{
			_guideWaypoints = new Vector3[ path.WaypointCount ];
			for ( int i = 0; i < path.WaypointCount; i++ )
				_guideWaypoints[ i ] = path.Waypoints[ i ];
		}
	}

	void ClearGuidePath()
	{
		_guideWaypoints = null;
		_guideWaypointIndex = 0;
		_guidePathDestination = Vector3.zero;
	}

	Vector3 ResolveGuideMoveTarget()
	{
		if ( _guideWaypoints == null || _guideWaypoints.Length == 0 )
			return _guideDestination;

		while ( _guideWaypointIndex < _guideWaypoints.Length - 1
			&& HorizontalDistance( _logicalPosition, _guideWaypoints[ _guideWaypointIndex ] ) <= guideArriveDistance )
		{
			_guideWaypointIndex++;
		}

		if ( _guideWaypointIndex >= _guideWaypoints.Length )
			return _guideDestination;

		return _guideWaypoints[ _guideWaypointIndex ];
	}

	void BeginFarewell()
	{
		_guidePhase = FairyGuidePhase.Farewell;
		_farewellBubbleSeen = false;
		_farewellEndsAt = Time.unscaledTime + guideFarewellHoldSeconds;
		StopGuideTrail();
		PlayInteractFeedback();
		NotifyTalkStarted();
		string arriveLine = !string.IsNullOrEmpty( _guideArriveLineOverride )
			? _guideArriveLineOverride
			: ( string.IsNullOrEmpty( guideArriveLine ) ? "Here it is, good luck." : guideArriveLine );
		if ( awaitContinueOnGuideArrive )
		{
			BeginAttentionTalk( arriveLine, FinishGuideFarewell );
			return;
		}

		Say( arriveLine );
	}

	void FinishGuideFarewell()
	{
		NotifyTalkEnded();
		Action finished = _guideOnFinished;
		_guideOnFinished = null;
		_guideArriveLineOverride = null;
		_guidePhase = FairyGuidePhase.None;
		_farewellBubbleSeen = false;
		if ( ActiveGuide == this )
			ActiveGuide = null;
		if ( finished != null )
			finished();
		DespawnSelf();
	}

	void TickFarewell()
	{
		if ( _attentionActive )
			return;

		if ( speechBubble != null && speechBubble.IsShowing )
		{
			_farewellBubbleSeen = true;
			return;
		}

		if ( !_farewellBubbleSeen && Time.unscaledTime < _farewellEndsAt )
			return;

		FinishGuideFarewell();
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
		EndAttentionTalk( invokeContinue: false );
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

		// Speech bubble detaches to a root Overlay; destroy it explicitly.
		if ( speechBubble != null )
		{
			Destroy( speechBubble.gameObject );
			speechBubble = null;
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

		// Prefer treasure surface height (includes gold-pile stamps) so guides hover above piles.
		TreasureSurfaceWorld surfaceWorld = TreasureSurfaceWorld.Instance;
		if ( surfaceWorld != null && surfaceWorld.IsInitialized && surfaceWorld.Sampler != null )
		{
			if ( surfaceWorld.TryGetChunkCoord( pos, out TreasureChunkCoord coord ) )
				surfaceWorld.EnsureChunkLoaded( coord );

			if ( surfaceWorld.Sampler.TrySample( pos, out TreasureSurfaceSample sample ) && sample.Valid )
			{
				groundY = sample.Height;
				return true;
			}
		}

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
