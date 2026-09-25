using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Contextual tutorials: once shown, stay visible until all tasks complete.
/// Multi-task checkboxes completed by gameplay; permanently complete when all tasks done.
/// Pushes tutorial outline roots to <see cref="TutorialHud"/> (overrides nearby objective outlines).
/// </summary>
public class TutorialManager : MonoBehaviour
{
	const float TaskCompleteHoldPadding = 0.15f;
	const float TutorialCompleteDwellSeconds = 1.1f;
	const float PostHideGapSeconds = 0.55f;
	const float PrecompletedRevealSeconds = 0.55f;
	const string JumpTutorialId = "tut_jumping";

	enum CeremonyPhase
	{
		None = 0,
		TaskLock = 1,
		TutorialComplete = 2,
		Hiding = 3,
		Cooldown = 4,
		PrecompletedReveal = 5
	}

	static TutorialManager _instance;

	readonly HashSet<string> _insideVolumes = new HashSet<string>();
	readonly HashSet<string> _sessionCompletedTaskKeys = new HashSet<string>();
	readonly HashSet<string> _completedThisSession = new HashSet<string>();
	readonly HashSet<string> _hadContextIds = new HashSet<string>();
	readonly HashSet<string> _deferredUncheckedTaskIds = new HashSet<string>();
	readonly Dictionary<string, int> _taskProgressCounts = new Dictionary<string, int>();
	readonly Dictionary<string, float> _completedUnscaledTimes = new Dictionary<string, float>();
	readonly List<TutorialDefinition> _matchingScratch = new List<TutorialDefinition>( 8 );
	readonly List<TutorialDefinition> _cycleScratch = new List<TutorialDefinition>( 8 );
	readonly List<TutorialDefinition> _openTutorials = new List<TutorialDefinition>( 8 );
	readonly List<string> _minimizedTitleScratch = new List<string>( 8 );
	readonly List<Transform> _outlineRootScratch = new List<Transform>( 8 );

	TutorialCatalogDefinition _catalog;
	TutorialPopupUI _popup;
	TutorialDefinition _active;
	TutorialDefinition _pendingShow;
	bool _activeIsReplay;
	bool _subscribed;
	bool _aimingTreasurePile;
	bool _aimingCoinStack;
	bool _aimingMinecart;
	bool _aimingDriveMinecart;
	bool _drivingMinecart;
	bool _playerAboveHeight;
	bool _wasPlayerGliding;
	bool _wasPlayerSliding;
	bool _playerOnSlideSlope;
	TreasureCategory _heldCategory;
	bool _isHolding;
	TreasureCategory _pendingSelectHoldingCategory;
	bool _hasPendingSelectHoldingCategory;
	string _lastShownId;
	string _lastActivatedId;
	CeremonyPhase _phase;
	float _phaseUntil;
	bool _finishMarkedComplete;
	string _endedCinematicPresentationId;
	float _cinematicEndedUnscaledTime = -1f;
	bool _afterCinematicContext;
	bool _hasTriedSprint;
	bool _walkWithoutSprintReady;
	float _walkWithoutSprintSeconds;
	bool _walkWithoutSprintContext;
	bool _driveMinecartContext;
	bool _worldHammerInteracted;

	public static TutorialManager Instance => _instance;

	public TutorialCatalogDefinition Catalog => _catalog;

	public string LastShownTutorialId => _lastShownId;

	public bool IsSequencePlaying => _active != null || _phase != CeremonyPhase.None;

	public static TutorialManager EnsureExists()
	{
		if ( _instance != null )
			return _instance;

		GameObject go = new GameObject( "TutorialManager" );
		_instance = go.AddComponent<TutorialManager>();
		DontDestroyOnLoad( go );
		return _instance;
	}

	void Awake()
	{
		if ( _instance != null && _instance != this )
		{
			Destroy( gameObject );
			return;
		}

		_instance = this;
	}

	void OnEnable()
	{
		Subscribe();
	}

	void OnDisable()
	{
		Unsubscribe();
	}

	void OnDestroy()
	{
		if ( _instance == this )
			_instance = null;
		Unsubscribe();
	}

	public void StartCatalog( TutorialPopupUI popup )
	{
		_popup = popup;
		LoadCatalog();
		_active = null;
		_pendingShow = null;
		_activeIsReplay = false;
		_phase = CeremonyPhase.None;
		_phaseUntil = 0f;
		_finishMarkedComplete = false;
		_insideVolumes.Clear();
		_aimingTreasurePile = false;
		_aimingCoinStack = false;
		_aimingMinecart = false;
		_aimingDriveMinecart = false;
		_drivingMinecart = false;
		_playerAboveHeight = false;
		_wasPlayerGliding = false;
		_wasPlayerSliding = false;
		_playerOnSlideSlope = false;
		_isHolding = false;
		_openTutorials.Clear();
		_endedCinematicPresentationId = null;
		_cinematicEndedUnscaledTime = -1f;
		_afterCinematicContext = false;
		_hasTriedSprint = false;
		_walkWithoutSprintReady = false;
		_walkWithoutSprintSeconds = 0f;
		_walkWithoutSprintContext = false;
		_driveMinecartContext = false;
		_worldHammerInteracted = false;
		_deferredUncheckedTaskIds.Clear();
		_taskProgressCounts.Clear();
		_completedUnscaledTimes.Clear();
		_hadContextIds.Clear();
		TutorialHud.ClearTutorialOverrideOutlineRoots();
		HydrateCompletedFromSave();
		RefreshFromPlayerState();
		EvaluateContext( force: true );
		RefreshCycleHint();
	}

	void HydrateCompletedFromSave()
	{
		ProfileSaveData save = GetSave();
		if ( save == null )
			return;
		save.EnsureTutorialProgress();
		if ( save.completedTutorialIds != null )
		{
			for ( int i = 0; i < save.completedTutorialIds.Count; i++ )
			{
				string id = save.completedTutorialIds[ i ];
				if ( !string.IsNullOrEmpty( id ) )
					_completedThisSession.Add( id );
			}
		}

		if ( save.discoveredTutorialIds == null )
			return;
		for ( int i = 0; i < save.discoveredTutorialIds.Count; i++ )
		{
			string id = save.discoveredTutorialIds[ i ];
			if ( !string.IsNullOrEmpty( id ) )
				_hadContextIds.Add( id );
		}

		if ( save.tutorialTaskProgressCounts == null )
			return;
		foreach ( KeyValuePair<string, int> pair in save.tutorialTaskProgressCounts )
		{
			if ( string.IsNullOrEmpty( pair.Key ) || pair.Value <= 0 )
				continue;
			_taskProgressCounts[ pair.Key ] = pair.Value;
		}
	}

	public void BindPopup( TutorialPopupUI popup )
	{
		_popup = popup;
	}

	void LoadCatalog()
	{
		_catalog = GameInstance.GetDefinition<TutorialCatalogDefinition>();
		if ( _catalog == null || _catalog.Count <= 0 )
			Debug.LogWarning( "TutorialManager: no TutorialCatalogDefinition found." );
	}

	void Subscribe()
	{
		if ( _subscribed )
			return;
		EventBus.Subscribe<VolumeEnteredEvent>( OnVolumeEntered );
		EventBus.Subscribe<VolumeExitedEvent>( OnVolumeExited );
		EventBus.Subscribe<PlayerHeldCategoryChangedEvent>( OnHeldChanged );
		EventBus.Subscribe<PouchChangedEvent>( OnPouchChanged );
		EventBus.Subscribe<TreasurePileAimChangedEvent>( OnPileAimChanged );
		EventBus.Subscribe<TreasurePileDigEvent>( OnDig );
		EventBus.Subscribe<PlacementCompletedEvent>( OnPlacementCompleted );
		EventBus.Subscribe<TreasureThrownEvent>( OnThrown );
		EventBus.Subscribe<CoinStackChangedEvent>( OnCoinStackChanged );
		EventBus.Subscribe<CoinTakenFromStackEvent>( OnCoinTakenFromStack );
		EventBus.Subscribe<CoinDisplayTableChangedEvent>( OnCoinDisplayChanged );
		EventBus.Subscribe<GemConstellationChangedEvent>( OnConstellationChanged );
		EventBus.Subscribe<ArtifactPresentationTableChangedEvent>( OnArtifactChanged );
		EventBus.Subscribe<WholeStackPickupCompletedEvent>( OnWholeStackPickup );
		EventBus.Subscribe<WholeStackPlaceCompletedEvent>( OnWholeStackPlace );
		EventBus.Subscribe<TreasureCollectedEvent>( OnTreasureCollected );
		EventBus.Subscribe<MapOpenedEvent>( OnMapOpened );
		EventBus.Subscribe<CoinSorterUsedEvent>( OnCoinSorterUsed );
		EventBus.Subscribe<CoinSorterStackLoadedEvent>( OnCoinSorterStackLoaded );
		EventBus.Subscribe<ChestOpenedEvent>( OnChestOpened );
		EventBus.Subscribe<MinecartShovedEvent>( OnMinecartShoved );
		EventBus.Subscribe<MinecartHoldPushStartedEvent>( OnMinecartHoldPushStarted );
		EventBus.Subscribe<MinecartCargoLoadedEvent>( OnMinecartCargoLoaded );
		EventBus.Subscribe<MinecartDriveEnteredEvent>( OnMinecartDriveEntered );
		EventBus.Subscribe<MinecartDriveExitedEvent>( OnMinecartDriveExited );
		EventBus.Subscribe<CinematicPresentationEndedEvent>( OnCinematicPresentationEnded );
		EventBus.Subscribe<AbilityUnlockedEvent>( OnAbilityUnlocked );
		EventBus.Subscribe<WorldHammerInteractedEvent>( OnWorldHammerInteracted );
		EventBus.Subscribe<BuildModeEnteredEvent>( OnBuildModeEntered );
		EventBus.Subscribe<BuildableCompletedEvent>( OnBuildableCompleted );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;
		EventBus.Unsubscribe<VolumeEnteredEvent>( OnVolumeEntered );
		EventBus.Unsubscribe<VolumeExitedEvent>( OnVolumeExited );
		EventBus.Unsubscribe<PlayerHeldCategoryChangedEvent>( OnHeldChanged );
		EventBus.Unsubscribe<PouchChangedEvent>( OnPouchChanged );
		EventBus.Unsubscribe<TreasurePileAimChangedEvent>( OnPileAimChanged );
		EventBus.Unsubscribe<TreasurePileDigEvent>( OnDig );
		EventBus.Unsubscribe<PlacementCompletedEvent>( OnPlacementCompleted );
		EventBus.Unsubscribe<TreasureThrownEvent>( OnThrown );
		EventBus.Unsubscribe<CoinStackChangedEvent>( OnCoinStackChanged );
		EventBus.Unsubscribe<CoinTakenFromStackEvent>( OnCoinTakenFromStack );
		EventBus.Unsubscribe<CoinDisplayTableChangedEvent>( OnCoinDisplayChanged );
		EventBus.Unsubscribe<GemConstellationChangedEvent>( OnConstellationChanged );
		EventBus.Unsubscribe<ArtifactPresentationTableChangedEvent>( OnArtifactChanged );
		EventBus.Unsubscribe<WholeStackPickupCompletedEvent>( OnWholeStackPickup );
		EventBus.Unsubscribe<WholeStackPlaceCompletedEvent>( OnWholeStackPlace );
		EventBus.Unsubscribe<TreasureCollectedEvent>( OnTreasureCollected );
		EventBus.Unsubscribe<MapOpenedEvent>( OnMapOpened );
		EventBus.Unsubscribe<CoinSorterUsedEvent>( OnCoinSorterUsed );
		EventBus.Unsubscribe<CoinSorterStackLoadedEvent>( OnCoinSorterStackLoaded );
		EventBus.Unsubscribe<ChestOpenedEvent>( OnChestOpened );
		EventBus.Unsubscribe<MinecartShovedEvent>( OnMinecartShoved );
		EventBus.Unsubscribe<MinecartHoldPushStartedEvent>( OnMinecartHoldPushStarted );
		EventBus.Unsubscribe<MinecartCargoLoadedEvent>( OnMinecartCargoLoaded );
		EventBus.Unsubscribe<MinecartDriveEnteredEvent>( OnMinecartDriveEntered );
		EventBus.Unsubscribe<MinecartDriveExitedEvent>( OnMinecartDriveExited );
		EventBus.Unsubscribe<CinematicPresentationEndedEvent>( OnCinematicPresentationEnded );
		EventBus.Unsubscribe<AbilityUnlockedEvent>( OnAbilityUnlocked );
		EventBus.Unsubscribe<WorldHammerInteractedEvent>( OnWorldHammerInteracted );
		EventBus.Unsubscribe<BuildModeEnteredEvent>( OnBuildModeEntered );
		EventBus.Unsubscribe<BuildableCompletedEvent>( OnBuildableCompleted );
		_subscribed = false;
	}

	void Update()
	{
		if ( _catalog == null || _popup == null )
			return;

		if ( CinematicPresentationController.IsAnyPlaying )
			return;

		TickCeremony();

		if ( _activeIsReplay )
			return;

		RefreshFromPlayerState();
		PollMoveLookTasks();
		PollGlideSlideTasks();
		PollJumpSprintTasks();
		PollMinecartDriveTasks();
		TickWalkWithoutSprint();
		TickAfterCinematicActivation();
		TickWalkWithoutSprintActivation();
		TickSteepSlopeActivation();
		TickDriveMinecartActivation();

		if ( _phase != CeremonyPhase.None )
			return;

		PollTutorialCycle();
		EvaluateContext( force: false );
	}

	void TickCeremony()
	{
		if ( _phase == CeremonyPhase.None )
			return;
		if ( Time.unscaledTime < _phaseUntil )
			return;

		switch ( _phase )
		{
			case CeremonyPhase.TaskLock:
				_phase = CeremonyPhase.None;
				EvaluateContext( force: true );
				break;

			case CeremonyPhase.PrecompletedReveal:
				RevealPrecompletedTasks();
				break;

			case CeremonyPhase.TutorialComplete:
				BeginHideAfterComplete();
				break;

			case CeremonyPhase.Hiding:
				FinishHideTransition();
				break;

			case CeremonyPhase.Cooldown:
				_phase = CeremonyPhase.None;
				if ( _pendingShow != null && IsCompleted( _pendingShow.id ) )
					_pendingShow = null;
				if ( _pendingShow != null )
				{
					TutorialDefinition next = _pendingShow;
					_pendingShow = null;
					if ( !IsCompleted( next.id ) )
						ShowTutorialNow( next, isReplay: false );
					else
						EvaluateContext( force: true );
				}
				else
				{
					EvaluateContext( force: true );
				}
				break;
		}
	}

	bool IsCeremonyBlocking =>
		_phase == CeremonyPhase.TutorialComplete
		|| _phase == CeremonyPhase.Hiding
		|| _phase == CeremonyPhase.Cooldown
		|| _phase == CeremonyPhase.PrecompletedReveal;

	void RefreshFromPlayerState()
	{
		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;
		PlayerCarry carry = player != null ? player.Carry : null;
		if ( carry != null )
		{
			_heldCategory = ResolveHeldCategory( carry );
			_isHolding = carry.Count > 0;
		}
		else
		{
			_isHolding = false;
		}

		PlayerInteraction interaction = player != null ? player.Interaction : null;
		if ( interaction != null )
		{
			_aimingTreasurePile = interaction.Current is TreasurePileInteractable;
			bool aimingStack = IsAimingCoinStack( interaction.Current );
			if ( aimingStack && !_aimingCoinStack )
				NoteActivationForAimCoinStack();
			_aimingCoinStack = aimingStack;
			MinecartInteractable aimedCart = interaction.Current as MinecartInteractable;
			bool aimingCart = aimedCart != null;
			if ( aimingCart && !_aimingMinecart )
				NoteActivationForAimMinecart();
			_aimingMinecart = aimingCart;
			_aimingDriveMinecart = aimedCart != null && aimedCart.IsDriveCart;
			if ( IsAimingCoinSorter( interaction.Current ) )
				TryCompleteTask( TutorialTaskCompleteType.AimCoinSorter );
		}
		else
		{
			_aimingTreasurePile = false;
			_aimingCoinStack = false;
			_aimingMinecart = false;
			_aimingDriveMinecart = false;
		}

		PlayerMinecartDrive drive = player != null ? player.MinecartDrive : null;
		_drivingMinecart = drive != null && drive.IsDriving;

		bool above = player != null && IsPlayerAboveAnyHeightTrigger( player.transform.position.y );
		if ( above && !_playerAboveHeight )
			NoteActivationForAboveHeight();
		_playerAboveHeight = above;
	}

	bool IsPlayerAboveAnyHeightTrigger( float playerY )
	{
		if ( _catalog == null || _catalog.tutorials == null )
			return playerY >= 30f;

		bool any = false;
		for ( int i = 0; i < _catalog.tutorials.Count; i++ )
		{
			TutorialDefinition def = _catalog.tutorials[ i ];
			if ( def == null || def.trigger != TutorialTriggerType.AboveHeight )
				continue;
			any = true;
			float minY = def.minHeightY > 0f ? def.minHeightY : 30f;
			if ( playerY >= minY )
				return true;
		}

		return !any && playerY >= 30f;
	}

	void NoteActivationForAboveHeight()
	{
		if ( _catalog == null || _catalog.tutorials == null )
			return;
		for ( int i = 0; i < _catalog.tutorials.Count; i++ )
		{
			TutorialDefinition def = _catalog.tutorials[ i ];
			if ( def == null || !IsEligible( def ) )
				continue;
			if ( def.trigger != TutorialTriggerType.AboveHeight )
				continue;
			if ( !IsContextActive( def ) )
				continue;
			MarkRecentlyActivated( def.id );
		}
	}

	static TreasureCategory ResolveHeldCategory( PlayerCarry carry )
	{
		if ( carry == null )
			return TreasureCategory.Coin;

		if ( carry.TryPeekActive( out TreasureDefinition def, out _ ) && def != null )
			return def.category;

		return PlayerCarry.MapBucketToCategory( carry.SelectedBucket );
	}

	static bool IsAimingCoinStack( IInteractable current )
	{
		if ( current is GroundCoinStack groundStack )
			return groundStack.Count >= 2;
		if ( current is CoinStackInteractable )
			return true;
		return false;
	}

	static bool IsAimingCoinSorter( IInteractable current )
	{
		Component component = current as Component;
		if ( component == null )
			return false;
		return component.GetComponentInParent<CoinSortingStation>() != null;
	}

	void PollMoveLookTasks()
	{
		GameInput input = InputController.Instance != null ? InputController.Instance.GameInput : null;
		if ( input == null )
			return;

		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;
		if ( player != null && !player.GameplayInputEnabled )
			return;

		if ( input.Move != null )
		{
			Vector2 move = input.Move.ReadValue<Vector2>();
			if ( move.sqrMagnitude > 0.04f )
				TryCompleteTask( TutorialTaskCompleteType.Move );
		}

		if ( input.CameraDelta != null )
		{
			Vector2 look = input.CameraDelta.ReadValue<Vector2>();
			if ( look.sqrMagnitude > 0.25f )
				TryCompleteTask( TutorialTaskCompleteType.Look );
		}
	}

	void PollTutorialCycle()
	{
		if ( _activeIsReplay )
			return;
		if ( _phase != CeremonyPhase.None )
			return;
		if ( PauseMenuUI.IsOpen || MapUI.IsOpen || DebugOverlay.IsOpen )
			return;

		Keyboard keyboard = Keyboard.current;
		if ( keyboard == null || !keyboard.tabKey.wasPressedThisFrame )
			return;

		TryCycleNextTutorial();
	}

	void TryCycleNextTutorial()
	{
		CollectCycleCandidates( _cycleScratch );
		if ( _cycleScratch.Count <= 1 )
			return;

		int currentIndex = -1;
		string currentId = _active != null ? _active.id : _lastShownId;
		if ( !string.IsNullOrEmpty( currentId ) )
		{
			for ( int i = 0; i < _cycleScratch.Count; i++ )
			{
				if ( _cycleScratch[ i ] != null && _cycleScratch[ i ].id == currentId )
				{
					currentIndex = i;
					break;
				}
			}
		}

		int nextIndex = currentIndex < 0 ? 0 : ( currentIndex + 1 ) % _cycleScratch.Count;
		TutorialDefinition next = _cycleScratch[ nextIndex ];
		if ( next == null || ( _active != null && _active.id == next.id ) )
			return;

		_lastActivatedId = next.id;
		ShowTutorialNow( next, isReplay: false, animate: false );
	}

	void CollectCycleCandidates( List<TutorialDefinition> into )
	{
		into.Clear();
		PruneOpenTutorials();
		for ( int i = 0; i < _openTutorials.Count; i++ )
		{
			TutorialDefinition def = _openTutorials[ i ];
			if ( def == null || !IsEligible( def ) )
				continue;
			into.Add( def );
		}
	}

	void EnsureOpen( TutorialDefinition def )
	{
		if ( def == null || !IsEligible( def ) || !IsShowDelayElapsed( def ) )
			return;
		for ( int i = 0; i < _openTutorials.Count; i++ )
		{
			TutorialDefinition existing = _openTutorials[ i ];
			if ( existing != null && existing.id == def.id )
				return;
		}

		_openTutorials.Add( def );
	}

	void RemoveOpen( TutorialDefinition def )
	{
		if ( def == null )
			return;
		for ( int i = _openTutorials.Count - 1; i >= 0; i-- )
		{
			TutorialDefinition existing = _openTutorials[ i ];
			if ( existing != null && existing.id == def.id )
				_openTutorials.RemoveAt( i );
		}
	}

	void PruneOpenTutorials()
	{
		for ( int i = _openTutorials.Count - 1; i >= 0; i-- )
		{
			TutorialDefinition def = _openTutorials[ i ];
			if ( def == null || !IsEligible( def ) )
				_openTutorials.RemoveAt( i );
		}
	}

	TutorialDefinition PickNextOpen( TutorialDefinition excluding )
	{
		PruneOpenTutorials();
		string excludeId = excluding != null ? excluding.id : null;
		if ( !string.IsNullOrEmpty( _lastActivatedId ) && _lastActivatedId != excludeId )
		{
			for ( int i = 0; i < _openTutorials.Count; i++ )
			{
				TutorialDefinition def = _openTutorials[ i ];
				if ( def != null && def.id == _lastActivatedId )
					return def;
			}
		}

		return FirstOpen( excluding );
	}

	TutorialDefinition FirstOpen( TutorialDefinition excluding )
	{
		PruneOpenTutorials();
		string excludeId = excluding != null ? excluding.id : null;
		for ( int i = 0; i < _openTutorials.Count; i++ )
		{
			TutorialDefinition def = _openTutorials[ i ];
			if ( def == null )
				continue;
			if ( !string.IsNullOrEmpty( excludeId ) && def.id == excludeId )
				continue;
			return def;
		}

		return null;
	}

	void RefreshCycleHint()
	{
		RefreshStackUi();
	}

	void RefreshStackUi()
	{
		if ( _popup == null )
			return;

		PruneOpenTutorials();
		_minimizedTitleScratch.Clear();
		if ( !_activeIsReplay )
		{
			string activeId = _active != null ? _active.id : null;
			for ( int i = 0; i < _openTutorials.Count; i++ )
			{
				TutorialDefinition def = _openTutorials[ i ];
				if ( def == null )
					continue;
				if ( !string.IsNullOrEmpty( activeId ) && def.id == activeId )
					continue;
				string title = string.IsNullOrEmpty( def.title ) ? def.id : def.title;
				_minimizedTitleScratch.Add( title );
			}
		}

		bool addedMinimized = _popup.SetMinimizedTutorials( _minimizedTitleScratch );

		CollectCycleCandidates( _cycleScratch );
		bool canCycle = _popup.IsVisible && !_activeIsReplay && _cycleScratch.Count > 1;
		_popup.SetCycleHint( canCycle, "[Tab]", "Switch" );
		if ( addedMinimized )
			_popup.PlayStackAdd();
	}

	void NoteHadContext( TutorialDefinition def )
	{
		if ( def == null || string.IsNullOrEmpty( def.id ) )
			return;
		if ( !IsEligible( def ) )
			return;
		if ( !_hadContextIds.Add( def.id ) )
			return;
		MarkDiscovered( def.id );
		RefreshCycleHint();
	}

	void PollGlideSlideTasks()
	{
		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;
		bool gliding = player != null && player.IsGliding;
		if ( gliding && !_wasPlayerGliding )
			TryCompleteTask( TutorialTaskCompleteType.Glide );
		_wasPlayerGliding = gliding;

		bool sliding = player != null && player.IsSliding;
		if ( sliding && !_wasPlayerSliding )
			TryCompleteTask( TutorialTaskCompleteType.Slide );
		_wasPlayerSliding = sliding;
	}

	void PollJumpSprintTasks()
	{
		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;
		GameInput input = InputController.Instance != null ? InputController.Instance.GameInput : null;
		if ( player != null && player.GameplayInputEnabled && input != null && input.Sprint != null && input.Sprint.WasPressedThisFrame() )
			_hasTriedSprint = true;

		if ( player != null && player.WasJumpThisFrame )
			TryCompleteTask( TutorialTaskCompleteType.Jump );

		if ( player != null && player.MovementState == PlayerMovementState.Sprinting )
			TryCompleteTask( TutorialTaskCompleteType.Sprint );
	}

	void TickWalkWithoutSprint()
	{
		if ( _hasTriedSprint || _walkWithoutSprintReady )
		{
			_walkWithoutSprintSeconds = 0f;
			return;
		}

		if ( !CanCountSprintWalkTimer() )
		{
			_walkWithoutSprintSeconds = 0f;
			return;
		}

		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;
		GameInput input = InputController.Instance != null ? InputController.Instance.GameInput : null;
		if ( player == null || input == null || input.Move == null )
		{
			_walkWithoutSprintSeconds = 0f;
			return;
		}

		if ( !player.GameplayInputEnabled || player.IsPlanarMovementLocked || !player.IsGrounded )
		{
			_walkWithoutSprintSeconds = 0f;
			return;
		}

		if ( player.WasJumpThisFrame || player.MovementState != PlayerMovementState.Walking )
		{
			_walkWithoutSprintSeconds = 0f;
			return;
		}

		Vector2 move = input.Move.ReadValue<Vector2>();
		if ( move.sqrMagnitude <= 0.04f )
		{
			_walkWithoutSprintSeconds = 0f;
			return;
		}

		_walkWithoutSprintSeconds += Time.unscaledDeltaTime;
		float need = ResolveWalkSecondsToShow();
		if ( _walkWithoutSprintSeconds >= need )
			_walkWithoutSprintReady = true;
	}

	bool CanCountSprintWalkTimer()
	{
		if ( _catalog == null || _catalog.tutorials == null )
			return IsCompleted( JumpTutorialId );

		TutorialDefinition jump = null;
		for ( int i = 0; i < _catalog.tutorials.Count; i++ )
		{
			TutorialDefinition def = _catalog.tutorials[ i ];
			if ( def != null && def.id == JumpTutorialId )
			{
				jump = def;
				break;
			}
		}

		if ( jump == null || jump.disabled )
			return true;

		if ( jump.trigger == TutorialTriggerType.AbilityUnlocked && !IsAbilityUnlockedContext( jump ) )
			return true;

		return IsCompleted( JumpTutorialId );
	}

	float ResolveWalkSecondsToShow()
	{
		if ( _catalog == null || _catalog.tutorials == null )
			return 5f;

		for ( int i = 0; i < _catalog.tutorials.Count; i++ )
		{
			TutorialDefinition def = _catalog.tutorials[ i ];
			if ( def == null || def.trigger != TutorialTriggerType.WalkWithoutSprint )
				continue;
			return def.walkSecondsToShow > 0f ? def.walkSecondsToShow : 5f;
		}

		return 5f;
	}

	void TickAfterCinematicActivation()
	{
		bool after = IsAnyAfterCinematicContext();
		if ( after && !_afterCinematicContext )
			NoteActivationForTrigger( TutorialTriggerType.AfterCinematic );
		_afterCinematicContext = after;
	}

	void TickWalkWithoutSprintActivation()
	{
		bool ready = _walkWithoutSprintReady && !_hasTriedSprint;
		if ( ready && !_walkWithoutSprintContext )
			NoteActivationForTrigger( TutorialTriggerType.WalkWithoutSprint );
		_walkWithoutSprintContext = ready;
	}

	void TickSteepSlopeActivation()
	{
		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;
		bool onSlope = player != null && player.IsSlideTutorialContext;
		if ( onSlope && !_playerOnSlideSlope )
			NoteActivationForTrigger( TutorialTriggerType.SteepSlope );
		_playerOnSlideSlope = onSlope;
	}

	void TickDriveMinecartActivation()
	{
		bool ready = IsDriveMinecartContext();
		if ( ready && !_driveMinecartContext )
			NoteActivationForTrigger( TutorialTriggerType.DriveMinecart );
		_driveMinecartContext = ready;
	}

	void PollMinecartDriveTasks()
	{
		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;
		PlayerMinecartDrive drive = player != null ? player.MinecartDrive : null;
		if ( drive == null || !drive.IsDriving )
			return;

		GameInput input = InputController.Instance != null ? InputController.Instance.GameInput : null;
		if ( input == null || input.Move == null )
			return;

		float axis = input.Move.ReadValue<Vector2>().y;
		const float deadzone = 0.12f;
		if ( axis > deadzone )
			TryCompleteTask( TutorialTaskCompleteType.DriveMinecartAccelerate );
		else if ( axis < -deadzone )
			TryCompleteTask( TutorialTaskCompleteType.DriveMinecartBrake );
	}

	bool IsAnyAfterCinematicContext()
	{
		if ( _catalog == null || _catalog.tutorials == null )
			return false;
		for ( int i = 0; i < _catalog.tutorials.Count; i++ )
		{
			TutorialDefinition def = _catalog.tutorials[ i ];
			if ( def == null || !IsEligible( def ) )
				continue;
			if ( def.trigger != TutorialTriggerType.AfterCinematic )
				continue;
			if ( IsAfterCinematicContext( def ) )
				return true;
		}

		return false;
	}

	void OnCinematicPresentationEnded( CinematicPresentationEndedEvent evt )
	{
		if ( string.IsNullOrEmpty( evt.PresentationId ) )
			return;
		_endedCinematicPresentationId = evt.PresentationId;
		_cinematicEndedUnscaledTime = Time.unscaledTime;
	}

	void OnAbilityUnlocked( AbilityUnlockedEvent evt )
	{
		if ( string.IsNullOrEmpty( evt.AbilityId ) )
			return;
		NoteActivationForAbilityUnlocked( evt.AbilityId );
		TryShowAbilityUnlockedTutorial( evt.AbilityId );
		EvaluateContext( force: true );
	}

	void NoteActivationForAbilityUnlocked( string abilityId )
	{
		if ( _catalog == null || _catalog.tutorials == null || string.IsNullOrEmpty( abilityId ) )
			return;
		for ( int i = 0; i < _catalog.tutorials.Count; i++ )
		{
			TutorialDefinition def = _catalog.tutorials[ i ];
			if ( def == null || !IsEligible( def ) )
				continue;
			if ( def.trigger != TutorialTriggerType.AbilityUnlocked )
				continue;
			if ( def.requiredAbilityId != abilityId )
				continue;
			MarkRecentlyActivated( def.id, force: true );
		}
	}

	void TryShowAbilityUnlockedTutorial( string abilityId )
	{
		if ( DebugDefinition.TutorialsDisabled )
			return;
		if ( _activeIsReplay )
			return;
		if ( CinematicPresentationController.IsAnyPlaying )
			return;
		if ( _catalog == null || _popup == null || string.IsNullOrEmpty( abilityId ) )
			return;

		TutorialDefinition match = null;
		for ( int i = 0; i < _catalog.tutorials.Count; i++ )
		{
			TutorialDefinition def = _catalog.tutorials[ i ];
			if ( def == null || !IsEligible( def ) )
				continue;
			if ( def.trigger != TutorialTriggerType.AbilityUnlocked )
				continue;
			if ( def.requiredAbilityId != abilityId )
				continue;
			match = def;
			break;
		}

		if ( match == null )
			return;
		if ( _active != null && _active.id == match.id )
			return;

		EnsureOpen( match );
		if ( _phase != CeremonyPhase.None )
		{
			_pendingShow = match;
			return;
		}

		ShowTutorialNow( match, isReplay: false );
	}

	void OnVolumeEntered( VolumeEnteredEvent evt )
	{
		if ( string.IsNullOrEmpty( evt.VolumeId ) )
			return;
		_insideVolumes.Add( evt.VolumeId );
		NoteActivationForVolume( evt.VolumeId );
		TryCompleteEnterVolumeTasks( evt.VolumeId );
		EvaluateContext( force: true );
	}

	void OnVolumeExited( VolumeExitedEvent evt )
	{
		if ( string.IsNullOrEmpty( evt.VolumeId ) )
			return;
		_insideVolumes.Remove( evt.VolumeId );
		EvaluateContext( force: true );
	}

	void OnHeldChanged( PlayerHeldCategoryChangedEvent evt )
	{
		_heldCategory = evt.Category;
		_isHolding = evt.IsHolding;
		if ( evt.IsHolding && evt.Carry != null )
			_heldCategory = ResolveHeldCategory( evt.Carry );
		if ( evt.IsHolding )
			NoteActivationForHolding( _heldCategory );
		NoteActivationForUnselectedHolding();
		EvaluateContext( force: true );
	}

	void OnPouchChanged( PouchChangedEvent evt )
	{
		TreasureCategory selected = PlayerCarry.MapBucketToCategory( evt.Bucket );
		TryCompleteSelectHoldingCategory( selected );
		NoteActivationForUnselectedHolding();
		EvaluateContext( force: true );
	}

	void OnPileAimChanged( TreasurePileAimChangedEvent evt )
	{
		_aimingTreasurePile = evt.IsAiming;
		if ( evt.IsAiming )
			NoteActivationForAimPile();
		EvaluateContext( force: true );
	}

	void OnDig( TreasurePileDigEvent evt )
	{
		TryCompleteTask( TutorialTaskCompleteType.Dig );
	}

	void OnPlacementCompleted( PlacementCompletedEvent evt )
	{
		if ( evt.Definition == null )
			return;

		if ( evt.Definition.category == TreasureCategory.Chest )
		{
			if ( evt.Target is FloorPlacementTarget )
				TryCompleteTask( TutorialTaskCompleteType.PlaceChestFloor );
			return;
		}

		if ( evt.Definition.category != TreasureCategory.Coin )
			return;

		// Coins on the ground always go through GroundCoinStack / ground stack targets, not FloorPlacementTarget.
		if ( evt.Target is FloorPlacementTarget
		     || evt.Target is GroundCoinStack
		     || evt.Target is GroundTreasureStackTarget )
		{
			TryCompleteTask( TutorialTaskCompleteType.PlaceCoinFloor );
		}
	}

	void OnThrown( TreasureThrownEvent evt )
	{
		TreasureCategory cat = evt.Definition != null ? evt.Definition.category : TreasureCategory.Coin;
		if ( cat == TreasureCategory.Coin )
			TryCompleteTask( TutorialTaskCompleteType.ThrowCoin );
	}

	void OnCoinStackChanged( CoinStackChangedEvent evt )
	{
		if ( evt.Stack == null || evt.Count < 2 )
			return;
		TryCompleteTask( TutorialTaskCompleteType.StackCoins );
	}

	void OnCoinTakenFromStack( CoinTakenFromStackEvent evt )
	{
		if ( evt.Stack == null )
			return;
		TryCompleteTask( TutorialTaskCompleteType.TakeCoinFromStack );
	}

	void OnCoinDisplayChanged( CoinDisplayTableChangedEvent evt )
	{
		TryCompleteTask( TutorialTaskCompleteType.PlaceCoinDisplay );
		NoteActivationForManyCoinsIfReady();
		EvaluateContext( force: true );
	}

	void OnConstellationChanged( GemConstellationChangedEvent evt )
	{
		TryCompleteTask( TutorialTaskCompleteType.PlaceGemConstellation );
	}

	void OnArtifactChanged( ArtifactPresentationTableChangedEvent evt )
	{
		TryCompleteTask( TutorialTaskCompleteType.PlaceArtifactStand );
	}

	void OnWholeStackPickup( WholeStackPickupCompletedEvent evt )
	{
		TryCompleteTask( TutorialTaskCompleteType.WholeStackPickup );
	}

	void OnWholeStackPlace( WholeStackPlaceCompletedEvent evt )
	{
		TryCompleteTask( TutorialTaskCompleteType.WholeStackPlace );
		if ( evt.IndirectJoin )
			TryCompleteTask( TutorialTaskCompleteType.WholeStackPlaceNearby );
	}

	void OnTreasureCollected( TreasureCollectedEvent evt )
	{
		if ( evt.Treasure == null )
			return;
		if ( evt.Treasure.category == TreasureCategory.Artifact )
			NoteActivationForHolding( TreasureCategory.Artifact );
		NoteActivationForUnselectedHolding();
		EvaluateContext( force: true );
	}

	void OnMapOpened( MapOpenedEvent evt )
	{
		TryCompleteTask( TutorialTaskCompleteType.OpenMap );
	}

	void OnChestOpened( ChestOpenedEvent evt )
	{
		TryCompleteTask( TutorialTaskCompleteType.OpenChest );
	}

	void OnMinecartShoved( MinecartShovedEvent evt )
	{
		TryCompleteTask( TutorialTaskCompleteType.PushMinecartTap );
	}

	void OnMinecartHoldPushStarted( MinecartHoldPushStartedEvent evt )
	{
		TryCompleteTask( TutorialTaskCompleteType.HoldMinecart );
	}

	void OnMinecartCargoLoaded( MinecartCargoLoadedEvent evt )
	{
		TryCompleteTask( TutorialTaskCompleteType.LoadMinecart );
	}

	void OnMinecartDriveEntered( MinecartDriveEnteredEvent evt )
	{
		TryCompleteTask( TutorialTaskCompleteType.EnterDriveMinecart );
		NoteActivationForTrigger( TutorialTriggerType.DriveMinecart );
		TryShowDrivingTutorial();
		EvaluateContext( force: true );
	}

	void OnMinecartDriveExited( MinecartDriveExitedEvent evt )
	{
		TryCompleteTask( TutorialTaskCompleteType.ExitDriveMinecart );
	}

	void TryShowDrivingTutorial()
	{
		if ( DebugDefinition.TutorialsDisabled )
			return;
		if ( _activeIsReplay )
			return;
		if ( CinematicPresentationController.IsAnyPlaying )
			return;
		if ( _catalog == null || _popup == null )
			return;

		TutorialDefinition match = null;
		for ( int i = 0; i < _catalog.tutorials.Count; i++ )
		{
			TutorialDefinition def = _catalog.tutorials[ i ];
			if ( def == null || !IsEligible( def ) )
				continue;
			if ( def.trigger != TutorialTriggerType.DriveMinecart )
				continue;
			match = def;
			break;
		}

		if ( match == null )
			return;
		if ( _active != null && _active.id == match.id )
			return;

		EnsureOpen( match );
		MarkRecentlyActivated( match.id, force: true );
		if ( _phase != CeremonyPhase.None )
		{
			_pendingShow = match;
			return;
		}

		ShowTutorialNow( match, isReplay: false );
	}

	void OnCoinSorterUsed( CoinSorterUsedEvent evt )
	{
		TryCompleteTask( TutorialTaskCompleteType.UseCoinSorter );
	}

	void OnCoinSorterStackLoaded( CoinSorterStackLoadedEvent evt )
	{
		if ( evt.CoinCount < 1 )
			return;
		TryCompleteTask( TutorialTaskCompleteType.LoadCoinSorterHopper );
	}

	void TryCompleteEnterVolumeTasks( string volumeId )
	{
		if ( string.IsNullOrEmpty( volumeId ) )
			return;

		RecordMatchingTasks( TutorialTaskCompleteType.EnterVolume, volumeId );
		CompleteFinishedOpenTutorials();

		if ( _active == null || _active.tasks == null )
			return;
		if ( IsCeremonyBlocking )
			return;

		bool matched = false;
		for ( int i = 0; i < _active.tasks.Length; i++ )
		{
			TutorialTask task = _active.tasks[ i ];
			if ( task == null || task.completeTrigger != TutorialTaskCompleteType.EnterVolume )
				continue;
			if ( string.IsNullOrEmpty( task.completeVolumeId ) || task.completeVolumeId != volumeId )
				continue;
			matched = true;
			break;
		}

		if ( !matched )
			return;

		if ( _active.completeAllTasksOnVolumeEnter )
		{
			ForceCompleteAllRemainingTasks();
			return;
		}

		PlayActiveTaskCompleteCeremony();
	}

	void CompleteMatchingEnterVolumeTasks( string volumeId )
	{
		RecordMatchingTasks( TutorialTaskCompleteType.EnterVolume, volumeId );
		PlayActiveTaskCompleteCeremony();
	}

	void OnWorldHammerInteracted( WorldHammerInteractedEvent evt )
	{
		_worldHammerInteracted = true;
		NoteActivationForTrigger( TutorialTriggerType.InteractWorldHammer );
		EvaluateContext( force: true );
	}

	void OnBuildModeEntered( BuildModeEnteredEvent evt )
	{
		TryCompleteTask( TutorialTaskCompleteType.EnterBuildMode );
	}

	void OnBuildableCompleted( BuildableCompletedEvent evt )
	{
		TryCompleteTask( TutorialTaskCompleteType.CompleteBuildable );
	}

	void NoteActivationForTrigger( TutorialTriggerType trigger )
	{
		if ( _catalog == null || _catalog.tutorials == null )
			return;
		for ( int i = 0; i < _catalog.tutorials.Count; i++ )
		{
			TutorialDefinition def = _catalog.tutorials[ i ];
			if ( def == null || !IsEligible( def ) )
				continue;
			if ( def.trigger != trigger )
				continue;
			if ( !IsContextActive( def ) )
				continue;
			MarkRecentlyActivated( def.id );
		}
	}

	void NoteActivationForVolume( string volumeId )
	{
		if ( _catalog == null || _catalog.tutorials == null )
			return;
		for ( int i = 0; i < _catalog.tutorials.Count; i++ )
		{
			TutorialDefinition def = _catalog.tutorials[ i ];
			if ( def == null || !IsEligible( def ) )
				continue;
			if ( IsVolumeContext( def, volumeId ) )
				MarkRecentlyActivated( def.id );
		}
	}

	void NoteActivationForHolding( TreasureCategory category )
	{
		if ( _catalog == null || _catalog.tutorials == null )
			return;
		for ( int i = 0; i < _catalog.tutorials.Count; i++ )
		{
			TutorialDefinition def = _catalog.tutorials[ i ];
			if ( def == null || !IsEligible( def ) )
				continue;
			if ( def.trigger == TutorialTriggerType.HoldingCategory && def.holdingCategory == category )
				MarkRecentlyActivated( def.id );
		}
	}

	void NoteActivationForUnselectedHolding()
	{
		if ( _catalog == null || _catalog.tutorials == null )
			return;

		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;
		PlayerCarry carry = player != null ? player.Carry : null;
		if ( carry == null )
			return;

		for ( int i = 0; i < _catalog.tutorials.Count; i++ )
		{
			TutorialDefinition def = _catalog.tutorials[ i ];
			if ( def == null || !IsEligible( def ) )
				continue;
			if ( def.trigger != TutorialTriggerType.UnselectedHoldingCategory )
				continue;
			if ( !IsUnselectedHoldingCategoryContext( def, carry ) )
				continue;
			MarkRecentlyActivated( def.id );
		}
	}

	void NoteActivationForAimPile()
	{
		if ( _catalog == null || _catalog.tutorials == null )
			return;
		for ( int i = 0; i < _catalog.tutorials.Count; i++ )
		{
			TutorialDefinition def = _catalog.tutorials[ i ];
			if ( def == null || !IsEligible( def ) )
				continue;
			if ( def.trigger == TutorialTriggerType.AimTreasurePile || def.alsoShowWhenAimingTreasurePile )
				MarkRecentlyActivated( def.id );
		}
	}

	void NoteActivationForAimCoinStack()
	{
		if ( _catalog == null || _catalog.tutorials == null )
			return;
		for ( int i = 0; i < _catalog.tutorials.Count; i++ )
		{
			TutorialDefinition def = _catalog.tutorials[ i ];
			if ( def == null || !IsEligible( def ) )
				continue;
			if ( def.trigger == TutorialTriggerType.AimCoinStack || def.alsoShowWhenAimingCoinStack )
				MarkRecentlyActivated( def.id );
		}
	}

	void NoteActivationForAimMinecart()
	{
		if ( _catalog == null || _catalog.tutorials == null )
			return;
		for ( int i = 0; i < _catalog.tutorials.Count; i++ )
		{
			TutorialDefinition def = _catalog.tutorials[ i ];
			if ( def == null || !IsEligible( def ) )
				continue;
			if ( def.trigger == TutorialTriggerType.AimMinecart )
				MarkRecentlyActivated( def.id );
		}
	}

	void NoteActivationForManyCoinsIfReady()
	{
		if ( _catalog == null || _catalog.tutorials == null )
			return;
		for ( int i = 0; i < _catalog.tutorials.Count; i++ )
		{
			TutorialDefinition def = _catalog.tutorials[ i ];
			if ( def == null || !IsEligible( def ) )
				continue;
			if ( def.trigger != TutorialTriggerType.ManyCoins )
				continue;
			if ( !IsManyCoinsContext( def ) )
				continue;
			MarkRecentlyActivated( def.id );
		}
	}

	void MarkRecentlyActivated( string id, bool force = false )
	{
		if ( string.IsNullOrEmpty( id ) )
			return;
		if ( !force && _active != null && !_activeIsReplay )
			return;
		_lastActivatedId = id;
	}

	void EvaluateContext( bool force )
	{
		if ( CinematicPresentationController.IsAnyPlaying )
			return;
		if ( _activeIsReplay )
			return;
		if ( _catalog == null || _popup == null )
			return;
		if ( DebugDefinition.TutorialsDisabled )
		{
			_openTutorials.Clear();
			if ( _active != null )
				RequestHide( complete: false );
			else
				RefreshStackUi();
			return;
		}

		_matchingScratch.Clear();
		for ( int i = 0; i < _catalog.tutorials.Count; i++ )
		{
			TutorialDefinition def = _catalog.tutorials[ i ];
			if ( def == null || !IsEligible( def ) )
				continue;
			if ( !IsContextActive( def ) )
				continue;
			if ( !IsShowDelayElapsed( def ) )
				continue;
			NoteHadContext( def );
			EnsureOpen( def );
			_matchingScratch.Add( def );
		}

		PruneOpenTutorials();
		RefreshStackUi();

		if ( _phase != CeremonyPhase.None )
			return;

		if ( _active != null )
		{
			if ( _active.disabled )
			{
				RequestHide( complete: false );
				return;
			}

			if ( !IsPrerequisiteVolumeMet( _active ) )
			{
				RequestHide( complete: false );
				return;
			}

			if ( force && IsEligible( _active ) )
				RefreshActivePopup();
			return;
		}

		TutorialDefinition best = PickBestMatch( _matchingScratch );
		TutorialDefinition next = PickNextOpen( null );
		if ( next == null )
			next = best;
		if ( next != null )
			RequestShow( next );
		else
			RefreshStackUi();
	}

	TutorialDefinition PickBestMatch( List<TutorialDefinition> matches )
	{
		if ( matches == null || matches.Count == 0 )
			return null;
		if ( matches.Count == 1 )
			return matches[ 0 ];

		for ( int i = 0; i < matches.Count; i++ )
		{
			TutorialDefinition def = matches[ i ];
			if ( def != null && def.id == _lastActivatedId )
				return def;
		}

		return matches[ matches.Count - 1 ];
	}

	bool IsEligible( TutorialDefinition def )
	{
		if ( def == null || string.IsNullOrEmpty( def.id ) )
			return false;
		if ( def.disabled )
			return false;
		if ( def.tasks == null || def.tasks.Length == 0 )
			return false;
		if ( IsCompleted( def.id ) )
			return false;
		if ( !ArePrerequisitesMet( def ) )
			return false;
		return true;
	}

	/// <summary>
	/// When showDelaySeconds &gt; 0 and this tutorial has prerequisite tutorials, wait that long
	/// after the latest this-session prerequisite completion before showing or joining the stack.
	/// Completions hydrated from save (no session timestamp) count as already elapsed.
	/// </summary>
	bool IsShowDelayElapsed( TutorialDefinition def )
	{
		if ( def == null )
			return false;
		if ( def.showDelaySeconds <= 0f )
			return true;
		if ( def.prerequisiteTutorialIds == null || def.prerequisiteTutorialIds.Length == 0 )
			return true;

		float latestSession = float.NegativeInfinity;
		bool anyPrereq = false;
		for ( int i = 0; i < def.prerequisiteTutorialIds.Length; i++ )
		{
			string prereq = def.prerequisiteTutorialIds[ i ];
			if ( string.IsNullOrEmpty( prereq ) )
				continue;
			if ( IsTutorialDisabled( prereq ) )
				continue;
			anyPrereq = true;
			if ( !_completedUnscaledTimes.TryGetValue( prereq, out float completedAt ) )
				continue;
			if ( completedAt > latestSession )
				latestSession = completedAt;
		}

		if ( !anyPrereq )
			return true;
		if ( latestSession == float.NegativeInfinity )
			return true;
		return Time.unscaledTime >= latestSession + def.showDelaySeconds;
	}

	bool IsContextActive( TutorialDefinition def )
	{
		if ( def == null )
			return false;

		bool primary = false;
		switch ( def.trigger )
		{
			case TutorialTriggerType.None:
				primary = IsPrerequisiteVolumeMet( def ) && !string.IsNullOrEmpty( def.prerequisiteVolumeId );
				break;
			case TutorialTriggerType.EnterVolume:
				primary = !string.IsNullOrEmpty( def.volumeId ) && _insideVolumes.Contains( def.volumeId );
				break;
			case TutorialTriggerType.HoldingCategory:
				primary = IsHoldingCategoryActiveContext( def.holdingCategory );
				break;
			case TutorialTriggerType.UnselectedHoldingCategory:
				primary = IsUnselectedHoldingCategoryContext( def );
				break;
			case TutorialTriggerType.AimTreasurePile:
				primary = _aimingTreasurePile;
				break;
			case TutorialTriggerType.AimCoinStack:
				primary = _aimingCoinStack;
				break;
			case TutorialTriggerType.GameStart:
				primary = true;
				break;
			case TutorialTriggerType.ManyCoins:
				primary = IsManyCoinsContext( def );
				break;
			case TutorialTriggerType.AboveHeight:
				primary = IsAboveHeightContext( def );
				break;
			case TutorialTriggerType.AimMinecart:
				primary = _aimingMinecart;
				break;
			case TutorialTriggerType.DriveMinecart:
				primary = IsDriveMinecartContext();
				break;
			case TutorialTriggerType.AfterCinematic:
				primary = IsAfterCinematicContext( def );
				break;
			case TutorialTriggerType.WalkWithoutSprint:
				primary = _walkWithoutSprintReady && !_hasTriedSprint;
				break;
			case TutorialTriggerType.SteepSlope:
				primary = IsSteepSlopeContext();
				break;
			case TutorialTriggerType.AbilityUnlocked:
				primary = IsAbilityUnlockedContext( def );
				break;
			case TutorialTriggerType.InteractWorldHammer:
				primary = _worldHammerInteracted;
				break;
			default:
				primary = false;
				break;
		}

		if ( !string.IsNullOrEmpty( def.fallbackVolumeId ) && _insideVolumes.Contains( def.fallbackVolumeId ) )
			primary = true;

		if ( def.alsoShowWhenAimingTreasurePile && _aimingTreasurePile )
			primary = true;

		if ( def.alsoShowWhenAimingCoinStack && _aimingCoinStack )
			primary = true;

		return primary;
	}

	bool IsManyCoinsContext( TutorialDefinition def )
	{
		int minWorld = def != null && def.minWorldCoinsToShow > 0 ? def.minWorldCoinsToShow : 100;
		int minCarry = def != null && def.minCarriedCoinsToShow > 0 ? def.minCarriedCoinsToShow : 50;
		int minDisplay = def != null && def.minDisplayCoinsToShow > 0 ? def.minDisplayCoinsToShow : 50;

		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;
		PlayerCarry carry = player != null ? player.Carry : null;
		if ( carry != null && carry.GetBucketCount( CarryBucketKind.Coin ) >= minCarry )
			return true;

		if ( CoinDisplayTableInteractable.CountAllDisplayedCoins() >= minDisplay )
			return true;

		return CountWorldGroundCoins() >= minWorld;
	}

	static bool IsSteepSlopeContext()
	{
		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;
		return player != null && player.IsSlideTutorialContext;
	}

	bool IsDriveMinecartContext()
	{
		if ( _drivingMinecart )
			return true;
		return _aimingDriveMinecart;
	}

	static bool IsAboveHeightContext( TutorialDefinition def )
	{
		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;
		if ( player == null )
			return false;
		float minY = def != null && def.minHeightY > 0f ? def.minHeightY : 30f;
		return player.transform.position.y >= minY;
	}

	static bool IsAbilityUnlockedContext( TutorialDefinition def )
	{
		if ( def == null || string.IsNullOrEmpty( def.requiredAbilityId ) )
			return false;
		AbilitySystem system = AbilitySystem.Instance;
		return system != null && system.IsUnlocked( def.requiredAbilityId );
	}

	bool IsAfterCinematicContext( TutorialDefinition def )
	{
		if ( _cinematicEndedUnscaledTime < 0f || string.IsNullOrEmpty( _endedCinematicPresentationId ) )
			return false;

		string want = def != null && !string.IsNullOrEmpty( def.cinematicPresentationId )
			? def.cinematicPresentationId
			: CinematicPresentationController.IntroLedgePresentationId;
		if ( _endedCinematicPresentationId != want )
			return false;

		float delay = def != null && def.showDelaySeconds > 0f ? def.showDelaySeconds : 0.75f;
		return Time.unscaledTime >= _cinematicEndedUnscaledTime + delay;
	}

	static int CountWorldGroundCoins()
	{
		IReadOnlyList<GroundCoinStack> stacks = GroundCoinStack.ActiveStacks;
		if ( stacks == null || stacks.Count == 0 )
			return 0;

		int total = 0;
		for ( int i = 0; i < stacks.Count; i++ )
		{
			GroundCoinStack stack = stacks[ i ];
			if ( stack == null || stack.IsMachineBuffer )
				continue;
			total += stack.Count;
		}
		return total;
	}

	static bool CategoriesMatchForHold( TreasureCategory required, TreasureCategory held )
	{
		if ( required == held )
			return true;
		if ( required == TreasureCategory.Artifact )
		{
			return held == TreasureCategory.Artifact
				|| held == TreasureCategory.Crown
				|| held == TreasureCategory.Goblet
				|| held == TreasureCategory.Helmet
				|| held == TreasureCategory.Key;
		}
		return false;
	}

	bool IsHoldingCategoryActiveContext( TreasureCategory required )
	{
		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;
		PlayerCarry carry = player != null ? player.Carry : null;
		if ( carry == null )
			return false;
		if ( !carry.TryPeekActive( out TreasureDefinition activeDef, out _ ) || activeDef == null )
			return false;
		return CategoriesMatchForHold( required, activeDef.category );
	}

	bool IsUnselectedHoldingCategoryContext( TutorialDefinition def )
	{
		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;
		PlayerCarry carry = player != null ? player.Carry : null;
		return IsUnselectedHoldingCategoryContext( def, carry );
	}

	static bool IsUnselectedHoldingCategoryContext( TutorialDefinition def, PlayerCarry carry )
	{
		if ( def == null || carry == null )
			return false;
		if ( !PlayerCarry.TryMapCategoryToBucket( def.holdingCategory, out CarryBucketKind bucket ) )
			return false;
		if ( carry.SelectedBucket == bucket )
			return false;
		return carry.GetBucketCount( bucket ) > 0;
	}

	bool IsVolumeContext( TutorialDefinition def, string volumeId )
	{
		if ( def == null || string.IsNullOrEmpty( volumeId ) )
			return false;
		if ( def.trigger == TutorialTriggerType.EnterVolume && def.volumeId == volumeId )
			return true;
		if ( def.fallbackVolumeId == volumeId )
			return true;
		if ( def.prerequisiteVolumeId == volumeId )
			return true;
		return false;
	}

	bool IsPrerequisiteVolumeMet( TutorialDefinition def )
	{
		if ( def == null || string.IsNullOrEmpty( def.prerequisiteVolumeId ) )
			return true;
		return _insideVolumes.Contains( def.prerequisiteVolumeId );
	}

	bool ArePrerequisitesMet( TutorialDefinition def )
	{
		if ( def == null )
			return false;
		if ( !IsPrerequisiteVolumeMet( def ) )
			return false;

		if ( def.prerequisiteTutorialIds == null || def.prerequisiteTutorialIds.Length == 0 )
			return true;

		for ( int i = 0; i < def.prerequisiteTutorialIds.Length; i++ )
		{
			string prereq = def.prerequisiteTutorialIds[ i ];
			if ( string.IsNullOrEmpty( prereq ) )
				continue;
			if ( IsCompleted( prereq ) )
				continue;
			if ( IsTutorialDisabled( prereq ) )
				continue;
			return false;
		}

		return true;
	}

	void ShowTutorial( TutorialDefinition def, bool isReplay )
	{
		if ( def == null || _popup == null )
			return;

		if ( isReplay )
		{
			_pendingShow = null;
			_phase = CeremonyPhase.None;
			ShowTutorialNow( def, isReplay: true );
			return;
		}

		RequestShow( def );
	}

	void RequestShow( TutorialDefinition def )
	{
		if ( def == null || IsCompleted( def.id ) )
			return;
		if ( !IsShowDelayElapsed( def ) )
			return;

		EnsureOpen( def );

		if ( _active != null && _active.id == def.id && _phase == CeremonyPhase.None )
		{
			RefreshStackUi();
			return;
		}

		if ( _active != null && !_activeIsReplay )
		{
			RefreshStackUi();
			return;
		}

		if ( _active == null && _phase == CeremonyPhase.None )
		{
			ShowTutorialNow( def, isReplay: false );
			return;
		}

		if ( _phase != CeremonyPhase.None )
		{
			if ( _pendingShow == null )
				_pendingShow = def;
			RefreshStackUi();
			return;
		}

		RefreshStackUi();
	}

	void ShowTutorialNow( TutorialDefinition def, bool isReplay )
	{
		ShowTutorialNow( def, isReplay, animate: true );
	}

	void ShowTutorialNow( TutorialDefinition def, bool isReplay, bool animate )
	{
		if ( def == null || _popup == null )
			return;
		if ( !isReplay && IsCompleted( def.id ) )
			return;

		_active = def;
		_activeIsReplay = isReplay;
		_lastShownId = def.id;
		_finishMarkedComplete = false;
		_phase = CeremonyPhase.None;
		_deferredUncheckedTaskIds.Clear();

		if ( !isReplay )
			EnsureOpen( def );

		if ( isReplay )
			ClearSessionTasksFor( def );
		else
			MarkDiscovered( def.id );

		if ( !isReplay )
			CollectDeferredUncheckedTasks( def );

		ApplyMapHighlight( def );
		ApplyTutorialOutlineOverride( def );

		_popup.Show(
			def.title,
			TutorialKeybindFormatter.Format( def.body ),
			string.Empty,
			FormatActiveTasks(),
			animate );

		_hadContextIds.Add( def.id );
		RefreshCycleHint();

		if ( _deferredUncheckedTaskIds.Count > 0 )
		{
			_phase = CeremonyPhase.PrecompletedReveal;
			_phaseUntil = Time.unscaledTime + PrecompletedRevealSeconds;
			return;
		}

		TryCompleteVolumeTasksIfAlreadyInside();
	}

	void CollectDeferredUncheckedTasks( TutorialDefinition def )
	{
		_deferredUncheckedTaskIds.Clear();
		if ( def == null || def.tasks == null )
			return;

		for ( int i = 0; i < def.tasks.Length; i++ )
		{
			TutorialTask task = def.tasks[ i ];
			if ( task == null || string.IsNullOrEmpty( task.id ) )
				continue;
			if ( !IsTaskCompleted( def.id, task.id ) )
				continue;
			_deferredUncheckedTaskIds.Add( task.id );
		}
	}

	void RevealPrecompletedTasks()
	{
		if ( _active == null )
		{
			_phase = CeremonyPhase.None;
			_deferredUncheckedTaskIds.Clear();
			EvaluateContext( force: true );
			return;
		}

		_deferredUncheckedTaskIds.Clear();
		_phase = CeremonyPhase.None;
		PlayActiveTaskCompleteCeremony();
	}

	void TryCompleteVolumeTasksIfAlreadyInside()
	{
		if ( _active == null || _active.tasks == null )
			return;

		for ( int i = 0; i < _active.tasks.Length; i++ )
		{
			TutorialTask task = _active.tasks[ i ];
			if ( task == null || task.completeTrigger != TutorialTaskCompleteType.EnterVolume )
				continue;
			if ( string.IsNullOrEmpty( task.completeVolumeId ) )
				continue;
			if ( !_insideVolumes.Contains( task.completeVolumeId ) )
				continue;

			if ( _active.completeAllTasksOnVolumeEnter )
				ForceCompleteAllRemainingTasks();
			else
				CompleteMatchingEnterVolumeTasks( task.completeVolumeId );
			return;
		}
	}

	void ApplyMapHighlight( TutorialDefinition def )
	{
		MapOverlayRegistrar.ClearHighlightedLabels();
		MapOverlayRegistrar.ClearTempMarkers();
		if ( def == null )
			return;
		if ( !string.IsNullOrEmpty( def.highlightMapLabel ) )
			MapOverlayRegistrar.SetLabelHighlighted( def.highlightMapLabel, true );
		if ( !string.IsNullOrEmpty( def.mapMarkerId ) )
			MapOverlayRegistrar.SetTempMarkerActive( def.mapMarkerId, true );
	}

	void ApplyTutorialOutlineOverride( TutorialDefinition def )
	{
		_outlineRootScratch.Clear();
		if ( def != null && def.outlineTargetIds != null )
		{
			for ( int i = 0; i < def.outlineTargetIds.Length; i++ )
			{
				string id = def.outlineTargetIds[ i ];
				if ( string.IsNullOrEmpty( id ) )
					continue;
				if ( !EventTargetRegistry.TryGetTarget( id, out QuestTarget target ) || target == null )
					continue;

				Transform root = target.ResolveOutlineRoot();
				if ( root == null )
					continue;
				if ( _outlineRootScratch.Contains( root ) )
					continue;
				_outlineRootScratch.Add( root );
			}
		}

		if ( _outlineRootScratch.Count > 0 )
			TutorialHud.SetTutorialOverrideOutlineRoots( _outlineRootScratch );
		else
			TutorialHud.ClearTutorialOverrideOutlineRoots();
	}

	void ClearTutorialOutlineOverride()
	{
		TutorialHud.ClearTutorialOverrideOutlineRoots();
	}

	void RequestHide( bool complete )
	{
		if ( _phase == CeremonyPhase.TutorialComplete || _phase == CeremonyPhase.Hiding )
			return;

		if ( complete && _active != null && !_activeIsReplay && !_finishMarkedComplete )
		{
			MarkCompleted( _active.id );
			_finishMarkedComplete = true;
		}

		if ( _popup != null && _popup.IsVisible )
		{
			_phase = CeremonyPhase.Hiding;
			_phaseUntil = Time.unscaledTime + _popup.HideFeedbackDuration + PostHideGapSeconds;
			_popup.Hide();
			return;
		}

		FinishHideTransition();
	}

	void BeginHideAfterComplete()
	{
		bool keepStack = _pendingShow != null && !IsCompleted( _pendingShow.id );
		if ( keepStack && _popup != null )
		{
			_phase = CeremonyPhase.Hiding;
			_phaseUntil = Time.unscaledTime + _popup.HideFeedbackDuration + PostHideGapSeconds;
			_popup.HideActiveCard();
			return;
		}

		if ( _popup != null && _popup.IsVisible )
		{
			_phase = CeremonyPhase.Hiding;
			_phaseUntil = Time.unscaledTime + _popup.HideFeedbackDuration + PostHideGapSeconds;
			_popup.Hide();
			return;
		}

		FinishHideTransition();
	}

	void FinishHideTransition()
	{
		_active = null;
		_activeIsReplay = false;
		_finishMarkedComplete = false;
		_deferredUncheckedTaskIds.Clear();
		MapOverlayRegistrar.ClearHighlightedLabels();
		MapOverlayRegistrar.ClearTempMarkers();
		ClearTutorialOutlineOverride();

		bool keepStack = _pendingShow != null && !IsCompleted( _pendingShow.id );
		if ( !keepStack && _openTutorials.Count == 0 && _popup != null )
			_popup.HideImmediate();
		else
			RefreshStackUi();

		_phase = CeremonyPhase.Cooldown;
		_phaseUntil = Time.unscaledTime + 0.05f;
	}

	void ClearSessionTasksFor( TutorialDefinition def )
	{
		if ( def == null || def.tasks == null )
			return;
		for ( int i = 0; i < def.tasks.Length; i++ )
		{
			TutorialTask task = def.tasks[ i ];
			if ( task == null || string.IsNullOrEmpty( task.id ) )
				continue;
			string key = TaskKey( def.id, task.id );
			_sessionCompletedTaskKeys.Remove( key );
			_taskProgressCounts.Remove( key );
		}
	}

	string FormatActiveTasks()
	{
		if ( _active == null )
			return string.Empty;
		return TutorialPopupUI.FormatTasks( _active.tasks, IsTaskCompleteInActive, GetActiveTaskProgress, GetActiveTaskRequired );
	}

	int GetActiveTaskProgress( string taskId )
	{
		if ( _active == null )
			return 0;
		return GetTaskProgress( _active.id, taskId );
	}

	int GetActiveTaskRequired( string taskId )
	{
		if ( _active == null || _active.tasks == null || string.IsNullOrEmpty( taskId ) )
			return 0;
		for ( int i = 0; i < _active.tasks.Length; i++ )
		{
			TutorialTask task = _active.tasks[ i ];
			if ( task == null || task.id != taskId )
				continue;
			return ResolveTaskRequiredCount( _active, task );
		}

		return 0;
	}

	void RefreshActivePopup()
	{
		if ( _active == null || _popup == null )
			return;

		string tasks = FormatActiveTasks();
		if ( _popup.IsVisible )
		{
			_popup.SetTasks( tasks );
			return;
		}

		_popup.Show(
			_active.title,
			TutorialKeybindFormatter.Format( _active.body ),
			string.Empty,
			tasks );
	}

	bool IsTaskCompleteInActive( string taskId )
	{
		if ( _active == null )
			return false;
		if ( _deferredUncheckedTaskIds.Contains( taskId ) )
			return false;
		return IsTaskCompleted( _active.id, taskId );
	}

	void TryCompleteTask( TutorialTaskCompleteType completeType )
	{
		if ( completeType == TutorialTaskCompleteType.None )
			return;
		if ( _activeIsReplay )
		{
			TryCompleteActiveTaskOnly( completeType );
			return;
		}

		int outcome = RecordMatchingTasks( completeType, null );
		CompleteFinishedOpenTutorials();
		ApplyActiveTaskRecord( outcome );
	}

	void TryCompleteSelectHoldingCategory( TreasureCategory selectedCategory )
	{
		_pendingSelectHoldingCategory = selectedCategory;
		_hasPendingSelectHoldingCategory = true;
		TryCompleteTask( TutorialTaskCompleteType.SelectHoldingCategory );
		_hasPendingSelectHoldingCategory = false;
	}

	void TryCompleteActiveTaskOnly( TutorialTaskCompleteType completeType )
	{
		if ( _active == null || _active.tasks == null )
			return;

		int outcome = RecordTasksOnDefinition( _active, completeType, null );
		ApplyActiveTaskRecord( outcome );
	}

	void ApplyActiveTaskRecord( int outcome )
	{
		if ( outcome <= 0 )
			return;

		if ( outcome >= 2 )
		{
			if ( IsCeremonyBlocking )
				return;
			PlayActiveTaskCompleteCeremony();
			return;
		}

		bool wasVisible = _popup != null && _popup.IsVisible;
		RefreshActivePopup();
		if ( wasVisible && _popup != null )
			_popup.PlayTaskCountPop();
	}

	int RecordMatchingTasks( TutorialTaskCompleteType completeType, string volumeId )
	{
		if ( _catalog == null || _catalog.tutorials == null )
			return RecordTasksOnDefinition( _active, completeType, volumeId );

		int activeOutcome = 0;
		for ( int i = 0; i < _catalog.tutorials.Count; i++ )
		{
			TutorialDefinition def = _catalog.tutorials[ i ];
			if ( def == null || !IsEligible( def ) )
				continue;

			int outcome = RecordTasksOnDefinition( def, completeType, volumeId );
			if ( outcome <= 0 )
				continue;

			if ( completeType == TutorialTaskCompleteType.EnterVolume && def.completeAllTasksOnVolumeEnter )
				MarkAllTasksCompleted( def );

			if ( _active != null && def.id == _active.id )
			{
				if ( outcome > activeOutcome )
					activeOutcome = outcome;
			}
		}

		return activeOutcome;
	}

	int RecordTasksOnDefinition( TutorialDefinition def, TutorialTaskCompleteType completeType, string volumeId )
	{
		if ( def == null || def.disabled || def.tasks == null || completeType == TutorialTaskCompleteType.None )
			return 0;

		int best = 0;
		for ( int i = 0; i < def.tasks.Length; i++ )
		{
			TutorialTask task = def.tasks[ i ];
			if ( task == null || task.completeTrigger != completeType )
				continue;
			if ( string.IsNullOrEmpty( task.id ) )
				continue;
			if ( completeType == TutorialTaskCompleteType.EnterVolume )
			{
				if ( string.IsNullOrEmpty( volumeId ) || task.completeVolumeId != volumeId )
					continue;
			}

			if ( completeType == TutorialTaskCompleteType.SelectHoldingCategory )
			{
				if ( !_hasPendingSelectHoldingCategory
				     || !CategoriesMatchForHold( def.holdingCategory, _pendingSelectHoldingCategory ) )
					continue;
			}

			if ( IsTaskCompleted( def.id, task.id ) )
				continue;

			int need = ResolveTaskRequiredCount( def, task );
			if ( need > 1 )
			{
				int next = IncrementTaskProgress( def.id, task.id );
				if ( next < need )
				{
					if ( best < 1 )
						best = 1;
					continue;
				}

				FillTaskProgress( def.id, task.id, need );
			}

			MarkTaskCompleted( def.id, task.id );
			best = 2;
		}

		return best;
	}

	void ForceCompleteMatchingTasks( TutorialTaskCompleteType completeType )
	{
		if ( completeType == TutorialTaskCompleteType.None )
			return;

		if ( _catalog == null || _catalog.tutorials == null )
		{
			if ( _active != null )
				ForceCompleteTasksOfType( _active, completeType );
			ApplyActiveTaskRecord( _active != null ? 2 : 0 );
			return;
		}

		bool activeHit = false;
		for ( int i = 0; i < _catalog.tutorials.Count; i++ )
		{
			TutorialDefinition def = _catalog.tutorials[ i ];
			if ( def == null || !IsEligible( def ) )
				continue;
			if ( !ForceCompleteTasksOfType( def, completeType ) )
				continue;
			if ( _active != null && def.id == _active.id )
				activeHit = true;
		}

		CompleteFinishedOpenTutorials();
		if ( activeHit )
			ApplyActiveTaskRecord( 2 );
	}

	bool ForceCompleteTasksOfType( TutorialDefinition def, TutorialTaskCompleteType completeType )
	{
		if ( def == null || def.disabled || def.tasks == null )
			return false;

		bool any = false;
		for ( int i = 0; i < def.tasks.Length; i++ )
		{
			TutorialTask task = def.tasks[ i ];
			if ( task == null || task.completeTrigger != completeType )
				continue;
			if ( string.IsNullOrEmpty( task.id ) )
				continue;
			if ( IsTaskCompleted( def.id, task.id ) )
				continue;
			FillTaskProgress( def.id, task.id, ResolveTaskRequiredCount( def, task ) );
			MarkTaskCompleted( def.id, task.id );
			any = true;
		}

		return any;
	}

	static int ResolveTaskRequiredCount( TutorialDefinition def, TutorialTask task )
	{
		if ( task == null )
			return 1;
		if ( task.requiredCount > 1 )
			return task.requiredCount;
		if ( task.requiredCount == 1 )
			return 1;
		if ( task.completeTrigger == TutorialTaskCompleteType.UseCoinSorter )
			return def != null && def.sorterCoinsToComplete > 0 ? def.sorterCoinsToComplete : 30;
		return 1;
	}

	int GetTaskProgress( string tutorialId, string taskId )
	{
		string key = TaskKey( tutorialId, taskId );
		if ( _taskProgressCounts.TryGetValue( key, out int count ) )
			return count;
		return 0;
	}

	int IncrementTaskProgress( string tutorialId, string taskId )
	{
		int next = GetTaskProgress( tutorialId, taskId ) + 1;
		FillTaskProgress( tutorialId, taskId, next );
		return next;
	}

	void FillTaskProgress( string tutorialId, string taskId, int count )
	{
		if ( string.IsNullOrEmpty( tutorialId ) || string.IsNullOrEmpty( taskId ) )
			return;
		if ( count < 0 )
			count = 0;

		string key = TaskKey( tutorialId, taskId );
		_taskProgressCounts[ key ] = count;

		if ( _activeIsReplay )
			return;

		ProfileSaveData save = GetSave();
		if ( save == null )
			return;

		save.EnsureTutorialProgress();
		save.tutorialTaskProgressCounts[ key ] = count;
		if ( ProfileManager.Instance != null )
			ProfileManager.Instance.SaveCurrentStatsToProfile();
	}

	void CompleteFinishedOpenTutorials()
	{
		bool changed = false;
		string activeId = _active != null ? _active.id : null;
		for ( int i = _openTutorials.Count - 1; i >= 0; i-- )
		{
			TutorialDefinition def = _openTutorials[ i ];
			if ( def == null )
			{
				_openTutorials.RemoveAt( i );
				changed = true;
				continue;
			}

			if ( !string.IsNullOrEmpty( activeId ) && def.id == activeId )
				continue;
			if ( !AreAllTasksComplete( def ) )
				continue;

			MarkCompleted( def.id );
			_openTutorials.RemoveAt( i );
			changed = true;
		}

		if ( changed )
			RefreshStackUi();
	}

	void MarkAllTasksCompleted( TutorialDefinition def )
	{
		if ( def == null || def.tasks == null )
			return;

		for ( int i = 0; i < def.tasks.Length; i++ )
		{
			TutorialTask task = def.tasks[ i ];
			if ( task == null || string.IsNullOrEmpty( task.id ) )
				continue;
			if ( IsTaskCompleted( def.id, task.id ) )
				continue;
			FillTaskProgress( def.id, task.id, ResolveTaskRequiredCount( def, task ) );
			MarkTaskCompleted( def.id, task.id );
		}
	}

	void PlayActiveTaskCompleteCeremony()
	{
		if ( _active == null || _active.tasks == null )
			return;
		if ( IsCeremonyBlocking )
			return;

		if ( _popup != null )
		{
			_popup.SetTasks( FormatActiveTasks() );
			_popup.PlayTaskComplete();
		}

		if ( AreAllTasksComplete( _active ) )
		{
			BeginTutorialCompleteCeremony();
			return;
		}

		_phase = CeremonyPhase.TaskLock;
		float lockSeconds = 0.35f;
		if ( _popup != null )
			lockSeconds = _popup.TaskCompleteFeedbackDuration + TaskCompleteHoldPadding;
		_phaseUntil = Time.unscaledTime + lockSeconds;
	}

	void ForceCompleteAllRemainingTasks()
	{
		if ( _active == null || _active.tasks == null )
			return;
		if ( IsCeremonyBlocking )
			return;

		bool any = false;
		for ( int i = 0; i < _active.tasks.Length; i++ )
		{
			TutorialTask task = _active.tasks[ i ];
			if ( task == null || string.IsNullOrEmpty( task.id ) )
				continue;
			if ( IsTaskCompleted( _active.id, task.id ) )
				continue;
			FillTaskProgress( _active.id, task.id, ResolveTaskRequiredCount( _active, task ) );
			MarkTaskCompleted( _active.id, task.id );
			any = true;
		}

		if ( !any && AreAllTasksComplete( _active ) )
		{
			BeginTutorialCompleteCeremony();
			return;
		}

		if ( !any )
			return;

		if ( _popup != null )
		{
			_popup.SetTasks( FormatActiveTasks() );
			_popup.PlayTaskComplete();
		}

		BeginTutorialCompleteCeremony();
	}

	void BeginTutorialCompleteCeremony()
	{
		if ( _active == null )
			return;

		if ( !_activeIsReplay && !_finishMarkedComplete )
		{
			MarkCompleted( _active.id );
			_finishMarkedComplete = true;
		}

		if ( !_activeIsReplay )
		{
			RemoveOpen( _active );
			_pendingShow = FirstOpen( null );
		}
		else
		{
			_pendingShow = null;
		}

		RefreshStackUi();
		if ( _popup != null )
			_popup.PlayTutorialComplete();

		float hold = TutorialCompleteDwellSeconds;
		if ( _popup != null )
			hold = _popup.TutorialCompleteFeedbackDuration + TutorialCompleteDwellSeconds;

		_phase = CeremonyPhase.TutorialComplete;
		_phaseUntil = Time.unscaledTime + hold;
	}

	bool AreAllTasksComplete( TutorialDefinition def )
	{
		if ( def == null || def.tasks == null || def.tasks.Length == 0 )
			return false;

		for ( int i = 0; i < def.tasks.Length; i++ )
		{
			TutorialTask task = def.tasks[ i ];
			if ( task == null || string.IsNullOrEmpty( task.id ) )
				continue;
			if ( !IsTaskCompleted( def.id, task.id ) )
				return false;
		}

		return true;
	}

	static string TaskKey( string tutorialId, string taskId )
	{
		return tutorialId + "/" + taskId;
	}

	bool IsTaskCompleted( string tutorialId, string taskId )
	{
		string key = TaskKey( tutorialId, taskId );
		if ( _sessionCompletedTaskKeys.Contains( key ) )
			return true;

		if ( _activeIsReplay )
			return false;

		ProfileSaveData save = GetSave();
		if ( save == null )
			return false;
		save.EnsureTutorialProgress();
		return save.completedTutorialTaskIds != null && save.completedTutorialTaskIds.Contains( key );
	}

	void MarkTaskCompleted( string tutorialId, string taskId )
	{
		string key = TaskKey( tutorialId, taskId );
		_sessionCompletedTaskKeys.Add( key );

		if ( _activeIsReplay )
			return;

		ProfileSaveData save = GetSave();
		if ( save == null || string.IsNullOrEmpty( tutorialId ) || string.IsNullOrEmpty( taskId ) )
			return;

		save.EnsureTutorialProgress();
		if ( !save.completedTutorialTaskIds.Contains( key ) )
			save.completedTutorialTaskIds.Add( key );

		if ( ProfileManager.Instance != null )
			ProfileManager.Instance.SaveCurrentStatsToProfile();
	}

	public bool Replay( string tutorialId )
	{
		if ( _catalog == null || !_catalog.TryGetById( tutorialId, out TutorialDefinition def ) || def == null )
			return false;

		ShowTutorial( def, isReplay: true );
		return true;
	}

	public bool ReplayLast()
	{
		if ( string.IsNullOrEmpty( _lastShownId ) )
			return false;
		return Replay( _lastShownId );
	}

	public IReadOnlyList<TutorialDefinition> GetCatalogEntries()
	{
		if ( _catalog == null || _catalog.tutorials == null )
			return System.Array.Empty<TutorialDefinition>();
		return _catalog.tutorials;
	}

	public bool IsDiscovered( string tutorialId )
	{
		if ( string.IsNullOrEmpty( tutorialId ) )
			return false;
		ProfileSaveData save = GetSave();
		if ( save == null )
			return false;
		save.EnsureTutorialProgress();
		return save.discoveredTutorialIds != null && save.discoveredTutorialIds.Contains( tutorialId );
	}

	public bool IsCompleted( string tutorialId )
	{
		if ( string.IsNullOrEmpty( tutorialId ) )
			return false;
		if ( _completedThisSession.Contains( tutorialId ) )
			return true;
		ProfileSaveData save = GetSave();
		if ( save == null )
			return false;
		save.EnsureTutorialProgress();
		return save.completedTutorialIds != null && save.completedTutorialIds.Contains( tutorialId );
	}

	bool IsTutorialDisabled( string tutorialId )
	{
		if ( string.IsNullOrEmpty( tutorialId ) || _catalog == null )
			return false;
		if ( !_catalog.TryGetById( tutorialId, out TutorialDefinition def ) || def == null )
			return false;
		return def.disabled;
	}

	void MarkDiscovered( string tutorialId )
	{
		ProfileSaveData save = GetSave();
		if ( save == null || string.IsNullOrEmpty( tutorialId ) )
			return;

		save.EnsureTutorialProgress();
		if ( !save.discoveredTutorialIds.Contains( tutorialId ) )
			save.discoveredTutorialIds.Add( tutorialId );

		if ( ProfileManager.Instance != null )
			ProfileManager.Instance.SaveCurrentStatsToProfile();
	}

	void MarkCompleted( string tutorialId )
	{
		if ( string.IsNullOrEmpty( tutorialId ) )
			return;

		bool alreadyCompleted = _completedThisSession.Contains( tutorialId );
		_completedThisSession.Add( tutorialId );
		if ( !_completedUnscaledTimes.ContainsKey( tutorialId ) )
			_completedUnscaledTimes[ tutorialId ] = Time.unscaledTime;

		ProfileSaveData save = GetSave();
		if ( save != null )
		{
			save.EnsureTutorialProgress();
			if ( !save.discoveredTutorialIds.Contains( tutorialId ) )
				save.discoveredTutorialIds.Add( tutorialId );
			if ( save.completedTutorialIds.Contains( tutorialId ) )
				alreadyCompleted = true;
			else
				save.completedTutorialIds.Add( tutorialId );

			if ( ProfileManager.Instance != null )
				ProfileManager.Instance.SaveCurrentStatsToProfile();
		}

		if ( !alreadyCompleted )
		{
			EventBus.Publish( new TutorialCompletedEvent
			{
				TutorialId = tutorialId
			} );
		}
	}

	static ProfileSaveData GetSave()
	{
		if ( ProfileManager.Instance == null )
			return null;
		return ProfileManager.Instance.ProfileSaveData;
	}

	public void DebugMarkCompleted( string tutorialId )
	{
		MarkCompleted( tutorialId );
	}

	public void DebugResetProgress()
	{
		_active = null;
		_pendingShow = null;
		_activeIsReplay = false;
		_phase = CeremonyPhase.None;
		_phaseUntil = 0f;
		_finishMarkedComplete = false;
		_lastShownId = null;
		_lastActivatedId = null;
		_openTutorials.Clear();
		_sessionCompletedTaskKeys.Clear();
		_completedThisSession.Clear();
		_completedUnscaledTimes.Clear();
		_hadContextIds.Clear();
		_insideVolumes.Clear();
		_deferredUncheckedTaskIds.Clear();
		_endedCinematicPresentationId = null;
		_cinematicEndedUnscaledTime = -1f;
		_afterCinematicContext = false;
		_wasPlayerGliding = false;
		_wasPlayerSliding = false;
		_playerOnSlideSlope = false;
		_hasTriedSprint = false;
		_walkWithoutSprintReady = false;
		_walkWithoutSprintSeconds = 0f;
		_walkWithoutSprintContext = false;
		_driveMinecartContext = false;
		_worldHammerInteracted = false;
		_taskProgressCounts.Clear();
		MapOverlayRegistrar.ClearHighlightedLabels();
		MapOverlayRegistrar.ClearTempMarkers();
		ClearTutorialOutlineOverride();
		if ( _popup != null )
			_popup.HideImmediate();

		ProfileSaveData save = GetSave();
		if ( save != null )
		{
			save.EnsureTutorialProgress();
			save.discoveredTutorialIds.Clear();
			save.completedTutorialIds.Clear();
			save.completedTutorialTaskIds.Clear();
			save.tutorialTaskProgressCounts.Clear();
			if ( ProfileManager.Instance != null )
				ProfileManager.Instance.SaveCurrentStatsToProfile();
		}
	}

	public void DebugForceShow( string tutorialId )
	{
		Replay( tutorialId );
	}
}

/// <summary>Resolves {ActionName} tokens in tutorial hint strings.</summary>
public static class TutorialKeybindFormatter
{
	public static string Format( string template )
	{
		if ( string.IsNullOrEmpty( template ) )
			return string.Empty;

		InputController inputController = InputController.Instance;
		GameInput gameInput = inputController != null ? inputController.GameInput : null;
		if ( gameInput == null )
			return template;

		string result = template;
		result = ReplaceToken( result, "Interact", gameInput.Interact );
		result = ReplaceToken( result, "ContextualInteract", gameInput.ContextualInteract );
		result = ReplaceToken( result, "SecondaryInteract", gameInput.SecondaryInteract );
		result = ReplaceToken( result, "WholeStackPickup", gameInput.ContextualInteract );
		result = ReplaceToken( result, "WholeStackPlace", gameInput.ContextualInteract );
		result = ReplaceToken( result, "RotateLeft", gameInput.RotateLeft );
		result = ReplaceToken( result, "RotateRight", gameInput.RotateRight );
		result = ReplaceToken( result, "Clean", gameInput.Clean );
		result = ReplaceToken( result, "Jump", gameInput.Jump );
		result = ReplaceToken( result, "Sprint", gameInput.Sprint );
		result = ReplaceToken( result, "CyclePouch", gameInput.CyclePouch );
		result = ReplaceToken( result, "BuildModeToggle", gameInput.BuildModeToggle );
		if ( gameInput.CategorySlots != null )
		{
			for ( int i = 0; i < gameInput.CategorySlots.Length; i++ )
				result = ReplaceToken( result, "CategorySlot" + ( i + 1 ), gameInput.CategorySlots[ i ] );
		}

		return result;
	}

	static string ReplaceToken( string text, string token, InputAction action )
	{
		string needle = "{" + token + "}";
		if ( text.IndexOf( needle ) < 0 )
			return text;

		string display = FormatBinding( action );
		if ( string.IsNullOrEmpty( display ) )
			display = token;
		return text.Replace( needle, display );
	}

	static string FormatBinding( InputAction action )
	{
		if ( action == null )
			return null;

		var bindings = action.bindings;
		bool has = false;
		for ( int i = 0; i < bindings.Count; i++ )
		{
			if ( !bindings[ i ].isComposite && !string.IsNullOrEmpty( bindings[ i ].effectivePath ) )
			{
				has = true;
				break;
			}
		}

		if ( !has )
			return null;

		string display = action.GetBindingDisplayString();
		if ( string.IsNullOrEmpty( display ) )
			return null;

		int pipe = display.IndexOf( '|' );
		if ( pipe >= 0 )
			display = display.Substring( 0, pipe ).Trim();

		return string.IsNullOrEmpty( display ) ? null : display;
	}
}
