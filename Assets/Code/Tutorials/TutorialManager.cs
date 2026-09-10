using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Contextual tutorials: once shown, stay visible until all tasks complete.
/// Multi-task checkboxes completed by gameplay; permanently complete when all tasks done.
/// Does not drive TutorialHud.
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
	readonly List<TutorialDefinition> _matchingScratch = new List<TutorialDefinition>( 8 );
	readonly List<TutorialDefinition> _cycleScratch = new List<TutorialDefinition>( 8 );
	readonly List<TutorialDefinition> _openTutorials = new List<TutorialDefinition>( 8 );
	readonly List<string> _minimizedTitleScratch = new List<string>( 8 );

	TutorialCatalogDefinition _catalog;
	TutorialPopupUI _popup;
	TutorialDefinition _active;
	TutorialDefinition _pendingShow;
	bool _activeIsReplay;
	bool _subscribed;
	bool _aimingTreasurePile;
	bool _aimingCoinStack;
	bool _aimingMinecart;
	bool _playerAboveHeight;
	bool _wasPlayerGliding;
	bool _wasPlayerSliding;
	bool _playerOnSlideSlope;
	int _sorterCoinsSorted;
	bool _sorterStackLoaded;
	TreasureCategory _heldCategory;
	bool _isHolding;
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
		_playerAboveHeight = false;
		_wasPlayerGliding = false;
		_wasPlayerSliding = false;
		_playerOnSlideSlope = false;
		_sorterCoinsSorted = 0;
		_sorterStackLoaded = false;
		_isHolding = false;
		_openTutorials.Clear();
		_endedCinematicPresentationId = null;
		_cinematicEndedUnscaledTime = -1f;
		_afterCinematicContext = false;
		_hasTriedSprint = false;
		_walkWithoutSprintReady = false;
		_walkWithoutSprintSeconds = 0f;
		_walkWithoutSprintContext = false;
		_deferredUncheckedTaskIds.Clear();
		_hadContextIds.Clear();
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
		EventBus.Subscribe<TreasurePileAimChangedEvent>( OnPileAimChanged );
		EventBus.Subscribe<TreasurePileDigEvent>( OnDig );
		EventBus.Subscribe<PlacementCompletedEvent>( OnPlacementCompleted );
		EventBus.Subscribe<TreasureThrownEvent>( OnThrown );
		EventBus.Subscribe<CoinStackChangedEvent>( OnCoinStackChanged );
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
		EventBus.Subscribe<CinematicPresentationEndedEvent>( OnCinematicPresentationEnded );
		EventBus.Subscribe<AbilityUnlockedEvent>( OnAbilityUnlocked );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;
		EventBus.Unsubscribe<VolumeEnteredEvent>( OnVolumeEntered );
		EventBus.Unsubscribe<VolumeExitedEvent>( OnVolumeExited );
		EventBus.Unsubscribe<PlayerHeldCategoryChangedEvent>( OnHeldChanged );
		EventBus.Unsubscribe<TreasurePileAimChangedEvent>( OnPileAimChanged );
		EventBus.Unsubscribe<TreasurePileDigEvent>( OnDig );
		EventBus.Unsubscribe<PlacementCompletedEvent>( OnPlacementCompleted );
		EventBus.Unsubscribe<TreasureThrownEvent>( OnThrown );
		EventBus.Unsubscribe<CoinStackChangedEvent>( OnCoinStackChanged );
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
		EventBus.Unsubscribe<CinematicPresentationEndedEvent>( OnCinematicPresentationEnded );
		EventBus.Unsubscribe<AbilityUnlockedEvent>( OnAbilityUnlocked );
		_subscribed = false;
	}

	void Update()
	{
		if ( _catalog == null || _popup == null )
			return;

		TickCeremony();

		if ( _activeIsReplay )
			return;

		RefreshFromPlayerState();
		PollMoveLookTasks();
		PollGlideSlideTasks();
		PollJumpSprintTasks();
		TickWalkWithoutSprint();
		TickAfterCinematicActivation();
		TickWalkWithoutSprintActivation();
		TickSteepSlopeActivation();

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
			bool aimingCart = interaction.Current is MinecartInteractable;
			if ( aimingCart && !_aimingMinecart )
				NoteActivationForAimMinecart();
			_aimingMinecart = aimingCart;
		}
		else
		{
			_aimingTreasurePile = false;
			_aimingCoinStack = false;
			_aimingMinecart = false;
		}

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
		if ( def == null || !IsEligible( def ) )
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

		_popup.SetMinimizedTutorials( _minimizedTitleScratch );

		CollectCycleCandidates( _cycleScratch );
		bool canCycle = _popup.IsVisible && !_activeIsReplay && _cycleScratch.Count > 1;
		_popup.SetCycleHint( canCycle, "[Tab]", "Switch" );
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

		if ( jump == null )
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
			MarkRecentlyActivated( def.id );
		}
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
	}

	void OnTreasureCollected( TreasureCollectedEvent evt )
	{
		if ( evt.Treasure == null )
			return;
		if ( evt.Treasure.category == TreasureCategory.Artifact )
			NoteActivationForHolding( TreasureCategory.Artifact );
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
	}

	void OnCoinSorterUsed( CoinSorterUsedEvent evt )
	{
		_sorterCoinsSorted++;
		TryCompleteSorterIfReady();
	}

	void OnCoinSorterStackLoaded( CoinSorterStackLoadedEvent evt )
	{
		if ( evt.CoinCount < 2 )
			return;
		_sorterStackLoaded = true;
		if ( _sorterCoinsSorted > 0 )
			TryCompleteSorterIfReady();
	}

	void TryCompleteSorterIfReady()
	{
		if ( _catalog == null || _catalog.tutorials == null )
		{
			if ( _sorterStackLoaded || _sorterCoinsSorted >= 30 )
				TryCompleteTask( TutorialTaskCompleteType.UseCoinSorter );
			return;
		}

		for ( int i = 0; i < _catalog.tutorials.Count; i++ )
		{
			TutorialDefinition def = _catalog.tutorials[ i ];
			if ( def == null || IsCompleted( def.id ) )
				continue;
			int need = def.sorterCoinsToComplete > 0 ? def.sorterCoinsToComplete : 30;
			if ( _sorterStackLoaded || _sorterCoinsSorted >= need )
			{
				TryCompleteTask( TutorialTaskCompleteType.UseCoinSorter );
				return;
			}
		}
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

	void MarkRecentlyActivated( string id )
	{
		if ( string.IsNullOrEmpty( id ) )
			return;
		_lastActivatedId = id;
	}

	void EvaluateContext( bool force )
	{
		if ( _activeIsReplay )
			return;
		if ( _phase != CeremonyPhase.None )
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
			NoteHadContext( def );
			EnsureOpen( def );
			_matchingScratch.Add( def );
		}

		PruneOpenTutorials();
		TutorialDefinition best = PickBestMatch( _matchingScratch );

		if ( _active != null && IsEligible( _active ) )
		{
			if ( force )
				RefreshActivePopup();
			RefreshStackUi();
			return;
		}

		TutorialDefinition next = PickNextOpen( _active );
		if ( next == null )
			next = best;
		if ( next != null )
			RequestShow( next );
		else if ( _active != null )
			RequestHide( complete: false );
		else
			RefreshStackUi();
	}

	TutorialDefinition PickBestMatch( List<TutorialDefinition> matches )
	{
		if ( matches == null || matches.Count == 0 )
			return null;
		if ( matches.Count == 1 )
			return matches[ 0 ];

		TutorialDefinition preferred = null;
		for ( int i = 0; i < matches.Count; i++ )
		{
			TutorialDefinition def = matches[ i ];
			if ( def != null && def.id == _lastActivatedId )
				return def;
			if ( _active != null && def != null && def.id == _active.id )
				preferred = def;
		}

		if ( preferred != null )
			return preferred;
		return matches[ matches.Count - 1 ];
	}

	bool IsEligible( TutorialDefinition def )
	{
		if ( def == null || string.IsNullOrEmpty( def.id ) )
			return false;
		if ( def.tasks == null || def.tasks.Length == 0 )
			return false;
		if ( IsCompleted( def.id ) )
			return false;
		if ( !ArePrerequisitesMet( def ) )
			return false;
		return true;
	}

	bool IsContextActive( TutorialDefinition def )
	{
		if ( def == null )
			return false;

		bool primary = false;
		switch ( def.trigger )
		{
			case TutorialTriggerType.EnterVolume:
				primary = !string.IsNullOrEmpty( def.volumeId ) && _insideVolumes.Contains( def.volumeId );
				break;
			case TutorialTriggerType.HoldingCategory:
				primary = _isHolding && CategoriesMatchForHold( def.holdingCategory, _heldCategory );
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

	bool IsVolumeContext( TutorialDefinition def, string volumeId )
	{
		if ( def == null || string.IsNullOrEmpty( volumeId ) )
			return false;
		if ( def.trigger == TutorialTriggerType.EnterVolume && def.volumeId == volumeId )
			return true;
		if ( def.fallbackVolumeId == volumeId )
			return true;
		return false;
	}

	bool ArePrerequisitesMet( TutorialDefinition def )
	{
		if ( def.prerequisiteTutorialIds == null || def.prerequisiteTutorialIds.Length == 0 )
			return true;

		for ( int i = 0; i < def.prerequisiteTutorialIds.Length; i++ )
		{
			string prereq = def.prerequisiteTutorialIds[ i ];
			if ( string.IsNullOrEmpty( prereq ) )
				continue;
			if ( !IsCompleted( prereq ) )
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

		EnsureOpen( def );

		if ( _active != null && !IsEligible( _active ) && _phase == CeremonyPhase.None )
		{
			ShowTutorialNow( def, isReplay: false );
			return;
		}

		if ( _active != null && _active.id == def.id && _phase == CeremonyPhase.None )
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

		_popup.Show(
			def.title,
			def.body,
			TutorialKeybindFormatter.Format( def.keybindHint ),
			TutorialPopupUI.FormatTasks( def.tasks, IsTaskCompleteInActive ),
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
			_sessionCompletedTaskKeys.Remove( TaskKey( def.id, task.id ) );
		}
	}

	void RefreshActivePopup()
	{
		if ( _active == null || _popup == null )
			return;

		string tasks = TutorialPopupUI.FormatTasks( _active.tasks, IsTaskCompleteInActive );
		if ( _popup.IsVisible )
		{
			_popup.SetTasks( tasks );
			return;
		}

		_popup.Show(
			_active.title,
			_active.body,
			TutorialKeybindFormatter.Format( _active.keybindHint ),
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

		bool activeHit = RecordMatchingTasks( completeType, null );
		CompleteFinishedOpenTutorials();
		if ( !activeHit )
			return;
		if ( IsCeremonyBlocking )
			return;

		PlayActiveTaskCompleteCeremony();
	}

	void TryCompleteActiveTaskOnly( TutorialTaskCompleteType completeType )
	{
		if ( _active == null || _active.tasks == null )
			return;
		if ( IsCeremonyBlocking )
			return;

		bool any = RecordTasksOnDefinition( _active, completeType, null );
		if ( !any )
			return;

		PlayActiveTaskCompleteCeremony();
	}

	bool RecordMatchingTasks( TutorialTaskCompleteType completeType, string volumeId )
	{
		if ( _catalog == null || _catalog.tutorials == null )
			return RecordTasksOnDefinition( _active, completeType, volumeId );

		bool activeHit = false;
		for ( int i = 0; i < _catalog.tutorials.Count; i++ )
		{
			TutorialDefinition def = _catalog.tutorials[ i ];
			if ( def == null || string.IsNullOrEmpty( def.id ) )
				continue;
			if ( IsCompleted( def.id ) )
				continue;

			bool any = RecordTasksOnDefinition( def, completeType, volumeId );
			if ( !any )
				continue;

			if ( completeType == TutorialTaskCompleteType.EnterVolume && def.completeAllTasksOnVolumeEnter )
				MarkAllTasksCompleted( def );

			if ( _active != null && def.id == _active.id )
				activeHit = true;
		}

		return activeHit;
	}

	bool RecordTasksOnDefinition( TutorialDefinition def, TutorialTaskCompleteType completeType, string volumeId )
	{
		if ( def == null || def.tasks == null || completeType == TutorialTaskCompleteType.None )
			return false;

		bool any = false;
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

			if ( IsTaskCompleted( def.id, task.id ) )
				continue;

			MarkTaskCompleted( def.id, task.id );
			any = true;
		}

		return any;
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
			_popup.SetTasks( TutorialPopupUI.FormatTasks( _active.tasks, IsTaskCompleteInActive ) );
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
			_popup.SetTasks( TutorialPopupUI.FormatTasks( _active.tasks, IsTaskCompleteInActive ) );
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

		_completedThisSession.Add( tutorialId );

		ProfileSaveData save = GetSave();
		if ( save == null )
			return;

		save.EnsureTutorialProgress();
		if ( !save.discoveredTutorialIds.Contains( tutorialId ) )
			save.discoveredTutorialIds.Add( tutorialId );
		if ( !save.completedTutorialIds.Contains( tutorialId ) )
			save.completedTutorialIds.Add( tutorialId );

		if ( ProfileManager.Instance != null )
			ProfileManager.Instance.SaveCurrentStatsToProfile();
	}

	static ProfileSaveData GetSave()
	{
		if ( ProfileManager.Instance == null )
			return null;
		return ProfileManager.Instance.ProfileSaveData;
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
		_sorterCoinsSorted = 0;
		_sorterStackLoaded = false;
		MapOverlayRegistrar.ClearHighlightedLabels();
		MapOverlayRegistrar.ClearTempMarkers();
		if ( _popup != null )
			_popup.HideImmediate();

		ProfileSaveData save = GetSave();
		if ( save != null )
		{
			save.EnsureTutorialProgress();
			save.discoveredTutorialIds.Clear();
			save.completedTutorialIds.Clear();
			save.completedTutorialTaskIds.Clear();
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
