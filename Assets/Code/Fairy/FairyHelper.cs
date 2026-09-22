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
	[Min( 1f )]
	[Tooltip( "Planar distance beyond which catchUpSpeed is used. Below this the fairy keeps moveSpeed and never mirrors the player." )]
	float farCatchUpDistance = 20f;

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
	[Min( 0.1f )]
	[Tooltip( "If the player is this far above the sampled treasure surface, use view fallback instead of surface hover (ledge / mid-air)." )]
	float surfaceMaxPlayerAbove = 1f;

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

	[Header( "Hints" )]
	[SerializeField]
	[Min( 1f )]
	[Tooltip( "How far from the fairy to look for almost-complete piles/displays when giving free-roam hints." )]
	float hintSeekRadius = 12f;

	[SerializeField]
	[Range( 0f, 1f )]
	[Tooltip( "Minimum progress (0-1) for an almost-complete hint. Falls back to any incomplete nearby if none qualify." )]
	float almostCompleteThreshold = 0.7f;

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
	const string TutorialIslandObjectiveId = "obj_island_4_artifacts";

	FairyInteractable _interactable;
	Component _perchedDisplay;
	TreasurePileVisual _nearbyPile;
	Vector3 _logicalPosition;
	Vector3 _planarVelocity;
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
	Vector3 _introAnchorPosition;
	readonly DisplayRequirementUI _displayProgress = new DisplayRequirementUI();
	readonly StringBuilder _progressBuilder = new StringBuilder( 96 );

	public Component PerchedDisplay => _perchedDisplay;
	public TreasurePileVisual NearbyPile => _nearbyPile;
	public bool IsTalking => _talking;
	public bool IsHovered => IsFocusedByPlayer();
	public bool IsFlying => _isMoving;
	public bool IsIntroActive => _introActive;

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
		if ( farCatchUpDistance < 1f )
			farCatchUpDistance = 20f;
		if ( closeArcDistance < 0.1f )
			closeArcDistance = 2.5f;
		if ( waveFrequency < 0.05f )
			waveFrequency = 1.4f;
		if ( curveLookAhead < 0.1f )
			curveLookAhead = 1.1f;
		if ( hintSeekRadius < 1f )
			hintSeekRadius = 12f;
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

	/// <summary>
	/// Free-roam talk hint when not over a pile/display: tutorial island early, then nearby almost-complete work.
	/// </summary>
	public bool TryBuildHintTalkLine( out string line )
	{
		line = null;
		ObjectiveSystem objectives = ObjectiveSystem.Instance;
		if ( objectives != null && !objectives.IsCompleted( TutorialIslandObjectiveId ) )
		{
			line = BuildTutorialIslandHint( objectives );
			return !string.IsNullOrEmpty( line );
		}

		if ( TryFindNearbyProgressHint( _logicalPosition, hintSeekRadius, almostCompleteThreshold, out line ) )
			return true;
		if ( TryFindNearbyProgressHint( _logicalPosition, hintSeekRadius, 0f, out line ) )
			return true;
		if ( TryBuildNearbyObjectiveHint( objectives, out line ) )
			return true;
		return false;
	}

	static string BuildTutorialIslandHint( ObjectiveSystem objectives )
	{
		if ( objectives == null || objectives.Catalog == null )
			return "Focus on your tutorial objectives.";

		if ( !objectives.Catalog.TryGetById( TutorialIslandObjectiveId, out ObjectiveDefinition definition ) || definition == null )
			return "Focus on your tutorial objectives.";

		if ( definition.subs != null )
		{
			for ( int i = 0; i < definition.subs.Length; i++ )
			{
				ObjectiveSubDefinition sub = definition.subs[ i ];
				if ( sub == null || string.IsNullOrEmpty( sub.id ) )
					continue;
				if ( objectives.IsSubCompleted( definition.id, sub.id ) )
					continue;
				string label = ObjectiveProgress.FormatLabel( definition, sub, complete: false );
				if ( string.IsNullOrEmpty( label ) )
					continue;
				return "Focus on your tutorial objectives: " + label;
			}
		}

		return "Focus on your tutorial objectives: " + definition.ResolveTitle();
	}

	bool TryFindNearbyProgressHint( Vector3 origin, float radius, float minProgress01, out string line )
	{
		line = null;
		float bestDistSq = radius * radius;
		string bestLine = null;

		TryConsiderNearbyDisplays( origin, radius, minProgress01, ref bestDistSq, ref bestLine );
		TryConsiderNearbyPiles( origin, radius, minProgress01, ref bestDistSq, ref bestLine );

		if ( string.IsNullOrEmpty( bestLine ) )
			return false;
		line = bestLine;
		return true;
	}

	void TryConsiderNearbyDisplays( Vector3 origin, float radius, float minProgress01, ref float bestDistSq, ref string bestLine )
	{
		int hitCount = Physics.OverlapSphereNonAlloc( origin, radius, OverlapScratch, ~0, QueryTriggerInteraction.Collide );
		for ( int i = 0; i < hitCount; i++ )
		{
			Collider hit = OverlapScratch[ i ];
			if ( hit == null )
				continue;

			Component display = ResolveCompletableDisplay( hit );
			if ( display == null )
				continue;
			if ( !TryGetDisplayProgressCounts( display, out int current, out int capacity, out bool complete ) )
				continue;
			if ( complete || capacity <= 0 )
				continue;

			float progress = ( float )current / capacity;
			if ( progress < minProgress01 )
				continue;

			float sq = HorizontalDistanceSq( origin, display.transform.position );
			if ( sq > bestDistSq )
				continue;

			bestDistSq = sq;
			int pct = Mathf.Clamp( Mathf.RoundToInt( progress * 100f ), 0, 100 );
			bestLine = "That display is " + pct + "% full — finish it off!";
		}
	}

	void TryConsiderNearbyPiles( Vector3 origin, float radius, float minProgress01, ref float bestDistSq, ref string bestLine )
	{
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

			int remaining = visual.TotalRemainingLoot;
			int initial = visual.TotalInitialLoot;
			if ( initial <= 0 )
				initial = remaining;
			if ( initial <= 0 || remaining <= 0 )
				continue;

			int cleared = Mathf.Max( 0, initial - remaining );
			float progress = ( float )cleared / initial;
			if ( progress < minProgress01 )
				continue;

			float centerSq = HorizontalDistanceSq( origin, visual.transform.position );
			float halfExtent = EstimatePileHalfExtent( visual );
			float edgeDist = Mathf.Max( 0f, Mathf.Sqrt( centerSq ) - halfExtent );
			if ( edgeDist > radius )
				continue;

			float scoreSq = edgeDist * edgeDist;
			if ( scoreSq > bestDistSq )
				continue;

			bestDistSq = scoreSq;
			int pct = Mathf.Clamp( Mathf.RoundToInt( progress * 100f ), 0, 100 );
			bestLine = "That pile is " + pct + "% cleared — keep digging!";
		}
	}

	bool TryBuildNearbyObjectiveHint( ObjectiveSystem objectives, out string line )
	{
		line = null;
		if ( objectives == null || objectives.Catalog == null || objectives.Catalog.objectives == null )
			return false;

		PlayerController player = ResolvePlayer();
		Vector3 playerPos = player != null ? player.transform.position : _logicalPosition;
		float bestDist = float.MaxValue;
		ObjectiveDefinition best = null;
		ObjectiveSubDefinition bestSub = null;

		for ( int i = 0; i < objectives.Catalog.objectives.Count; i++ )
		{
			ObjectiveDefinition definition = objectives.Catalog.objectives[ i ];
			if ( definition == null || string.IsNullOrEmpty( definition.id ) )
				continue;
			if ( objectives.IsCompleted( definition.id ) )
				continue;
			if ( !objectives.ArePrerequisitesMet( definition ) )
				continue;
			if ( !IsObjectiveInShowContext( definition, playerPos ) )
				continue;

			float distance = EstimateObjectiveDistance( definition, _logicalPosition );
			if ( distance >= bestDist )
				continue;

			ObjectiveSubDefinition incompleteSub = null;
			if ( definition.subs != null )
			{
				for ( int s = 0; s < definition.subs.Length; s++ )
				{
					ObjectiveSubDefinition sub = definition.subs[ s ];
					if ( sub == null || string.IsNullOrEmpty( sub.id ) )
						continue;
					if ( objectives.IsSubCompleted( definition.id, sub.id ) )
						continue;
					incompleteSub = sub;
					break;
				}
			}

			bestDist = distance;
			best = definition;
			bestSub = incompleteSub;
		}

		if ( best == null )
			return false;

		if ( bestSub != null )
		{
			string label = ObjectiveProgress.FormatLabel( best, bestSub, complete: false );
			line = "Nearby: " + label;
		}
		else
		{
			line = "Nearby: " + best.ResolveTitle();
		}

		return !string.IsNullOrEmpty( line );
	}

	static bool IsObjectiveInShowContext( ObjectiveDefinition definition, Vector3 playerPos )
	{
		if ( definition == null )
			return false;

		if ( !string.IsNullOrEmpty( definition.showVolumeId ) )
		{
			if ( !EventTargetRegistry.TryGetVolume( definition.showVolumeId, out QuestVolume volume ) || volume == null )
				return false;
			return volume.ContainsWorldPoint( playerPos );
		}

		float distance = EstimateObjectiveDistance( definition, playerPos );
		float radius = definition.showRadius > 0f ? definition.showRadius : 40f;
		return distance <= radius;
	}

	static float EstimateObjectiveDistance( ObjectiveDefinition definition, Vector3 from )
	{
		if ( definition == null )
			return float.MaxValue;

		if ( !string.IsNullOrEmpty( definition.showVolumeId )
			&& EventTargetRegistry.TryGetMarkerTransform( definition.showVolumeId, out Transform volumeMarker )
			&& volumeMarker != null )
		{
			return HorizontalDistance( from, volumeMarker.position );
		}

		if ( definition.subs != null )
		{
			float best = float.MaxValue;
			for ( int i = 0; i < definition.subs.Length; i++ )
			{
				ObjectiveSubDefinition sub = definition.subs[ i ];
				if ( sub == null || string.IsNullOrEmpty( sub.targetId ) )
					continue;
				if ( !EventTargetRegistry.TryGetMarkerTransform( sub.targetId, out Transform marker ) || marker == null )
					continue;
				float d = HorizontalDistance( from, marker.position );
				if ( d < best )
					best = d;
			}
			if ( best < float.MaxValue )
				return best;
		}

		return float.MaxValue;
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
		_introAnchorPosition = desired;
		_logicalPosition = desired;
		transform.position = desired;
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
		_planarVelocity = Vector3.zero;
		_frameMoveDelta = Vector3.zero;
		_curvePhase = 0f;
		_isMoving = false;
		FaceToward( player.transform.position - transform.position, instant: true );
	}

	void RefreshNearbyTargets( PlayerController player )
	{
		_nearbyPile = FindNearestPile( _logicalPosition, pileAskRadius );
		_perchedDisplay = FindNearestIncompleteDisplay( player.transform.position, displaySeekRadius );
	}

	Vector3 ResolveDesiredPosition( PlayerController player )
	{
		if ( _introActive )
			return _introAnchorPosition;

		EnsureFollowYaw( player );

		// Fairy stuck/lagging on surface (or elsewhere) while the player has moved away:
		// abandon that slot and chase the in-view fallback instead.
		if ( HorizontalDistance( _logicalPosition, player.transform.position ) > farGlowDistance )
			return BuildViewFallback( player );

		if ( _perchedDisplay != null )
		{
			Transform perch = _perchedDisplay.transform;
			return perch.position + Vector3.up * perchHeight;
		}

		return ResolveSurfacePoint( player, BuildFollowTarget( player ) );
	}

	void UpdateFollowYaw( PlayerController player )
	{
		EnsureFollowYaw( player );
		float lookYaw = GetCameraLookYaw( player );
		UpdateCameraSettleTimer( lookYaw );

		// Hold still during intro so the player can aim/hover the fairy.
		if ( _introActive )
			return;

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

		Vector3 playerPos = player.transform.position;

		// Strict: player must actually be standing on traversable surface.
		// Do not use nearest-entry search — that can latch onto a distant pile.
		if ( !world.Sampler.TrySample( playerPos, out TreasureSurfaceSample playerSample ) || !playerSample.Traversable )
			return BuildViewFallback( player );

		// Player on a ledge above painted surface — don't snap the fairy down to floor height.
		if ( playerPos.y > playerSample.Height + surfaceMaxPlayerAbove )
			return BuildViewFallback( player );

		if ( world.TryGetChunkCoord( desired, out TreasureChunkCoord coord ) )
			world.EnsureChunkLoaded( coord );

		// Follow slot must also be on surface under the player. If the slot is off-surface
		// (or only near a distant pile), fall back to the in-view hover offset.
		if ( !world.Sampler.TrySample( desired, out TreasureSurfaceSample sample ) || !sample.Traversable )
			return BuildViewFallback( player );

		if ( playerPos.y > sample.Height + surfaceMaxPlayerAbove )
			return BuildViewFallback( player );

		float dx = desired.x - playerPos.x;
		float dz = desired.z - playerPos.z;
		float maxDist = surfaceSnapMaxDistance + Mathf.Max( Mathf.Abs( followOffsetLocal.x ), Mathf.Abs( followOffsetLocal.z ) );
		if ( dx * dx + dz * dz > maxDist * maxDist )
			return BuildViewFallback( player );

		desired.y = sample.Height + surfaceHoverHeight;
		return desired;
	}

	void StepMove( Vector3 desired )
	{
		Vector3 current = _logicalPosition;
		float dt = Mathf.Max( Time.deltaTime, 0.0001f );

		if ( _introActive )
		{
			_logicalPosition = _introAnchorPosition;
			_frameMoveDelta = Vector3.zero;
			_planarVelocity = Vector3.zero;
			_isMoving = false;
			_bobPhase = 0f;
			transform.position = _introAnchorPosition;
			return;
		}

		Vector3 to = desired - current;
		Vector3 planar = new Vector3( to.x, 0f, to.z );
		float planarDist = planar.magnitude;

		// Hold when close enough. Never latch onto the moving follow slot — that would
		// make the fairy inherit the player's speed regardless of moveSpeed.
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
		float speed = planarDist > farCatchUpDistance ? catchUpSpeed : moveSpeed;
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
