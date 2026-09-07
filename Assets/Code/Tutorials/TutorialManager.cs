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

	enum CeremonyPhase
	{
		None = 0,
		TaskLock = 1,
		TutorialComplete = 2,
		Hiding = 3,
		Cooldown = 4
	}

	static TutorialManager _instance;

	readonly HashSet<string> _insideVolumes = new HashSet<string>();
	readonly HashSet<string> _sessionCompletedTaskKeys = new HashSet<string>();
	readonly HashSet<string> _completedThisSession = new HashSet<string>();
	readonly List<TutorialDefinition> _matchingScratch = new List<TutorialDefinition>( 8 );

	TutorialCatalogDefinition _catalog;
	TutorialPopupUI _popup;
	TutorialDefinition _active;
	TutorialDefinition _pendingShow;
	bool _activeIsReplay;
	bool _subscribed;
	bool _aimingTreasurePile;
	bool _aimingCoinStack;
	bool _playerAboveHeight;
	bool _wasPlayerGliding;
	int _sorterCoinsSortedWhileActive;
	bool _sorterStackLoadedWhileActive;
	TreasureCategory _heldCategory;
	bool _isHolding;
	string _lastShownId;
	string _lastActivatedId;
	CeremonyPhase _phase;
	float _phaseUntil;
	bool _finishMarkedComplete;

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
		_playerAboveHeight = false;
		_wasPlayerGliding = false;
		_sorterCoinsSortedWhileActive = 0;
		_sorterStackLoadedWhileActive = false;
		_isHolding = false;
		HydrateCompletedFromSave();
		RefreshFromPlayerState();
		EvaluateContext( force: true );
	}

	void HydrateCompletedFromSave()
	{
		ProfileSaveData save = GetSave();
		if ( save == null )
			return;
		save.EnsureTutorialProgress();
		if ( save.completedTutorialIds == null )
			return;
		for ( int i = 0; i < save.completedTutorialIds.Count; i++ )
		{
			string id = save.completedTutorialIds[ i ];
			if ( !string.IsNullOrEmpty( id ) )
				_completedThisSession.Add( id );
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
		_subscribed = false;
	}

	void Update()
	{
		if ( _catalog == null || _popup == null )
			return;

		TickCeremony();

		if ( _activeIsReplay )
			return;
		if ( _phase != CeremonyPhase.None )
			return;

		RefreshFromPlayerState();
		PollMoveLookTasks();
		PollGlideTask();
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
		|| _phase == CeremonyPhase.Cooldown;

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
		}
		else
		{
			_aimingTreasurePile = false;
			_aimingCoinStack = false;
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
		if ( _active == null || _active.tasks == null )
			return;
		if ( IsCeremonyBlocking )
			return;

		GameInput input = InputController.Instance != null ? InputController.Instance.GameInput : null;
		if ( input == null )
			return;

		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;
		if ( player != null && !player.GameplayInputEnabled )
			return;

		bool needMove = false;
		bool needLook = false;
		for ( int i = 0; i < _active.tasks.Length; i++ )
		{
			TutorialTask task = _active.tasks[ i ];
			if ( task == null || string.IsNullOrEmpty( task.id ) )
				continue;
			if ( IsTaskCompleted( _active.id, task.id ) )
				continue;
			if ( task.completeTrigger == TutorialTaskCompleteType.Move )
				needMove = true;
			else if ( task.completeTrigger == TutorialTaskCompleteType.Look )
				needLook = true;
		}

		if ( needMove && input.Move != null )
		{
			Vector2 move = input.Move.ReadValue<Vector2>();
			if ( move.sqrMagnitude > 0.04f )
				TryCompleteTask( TutorialTaskCompleteType.Move );
		}

		if ( needLook && input.CameraDelta != null )
		{
			Vector2 look = input.CameraDelta.ReadValue<Vector2>();
			if ( look.sqrMagnitude > 0.25f )
				TryCompleteTask( TutorialTaskCompleteType.Look );
		}
	}

	void PollGlideTask()
	{
		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;
		bool gliding = player != null && player.IsGliding;
		if ( gliding && !_wasPlayerGliding )
			TryCompleteTask( TutorialTaskCompleteType.Glide );
		_wasPlayerGliding = gliding;
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

	void OnCoinSorterUsed( CoinSorterUsedEvent evt )
	{
		if ( _active == null )
			return;
		_sorterCoinsSortedWhileActive++;
		int need = _active.sorterCoinsToComplete > 0 ? _active.sorterCoinsToComplete : 30;
		if ( _sorterStackLoadedWhileActive || _sorterCoinsSortedWhileActive >= need )
			TryCompleteTask( TutorialTaskCompleteType.UseCoinSorter );
	}

	void OnCoinSorterStackLoaded( CoinSorterStackLoadedEvent evt )
	{
		if ( evt.CoinCount < 2 )
			return;
		_sorterStackLoadedWhileActive = true;
		// Completes on the next sorted coin (or immediately if already sorting).
		if ( _sorterCoinsSortedWhileActive > 0 )
			TryCompleteTask( TutorialTaskCompleteType.UseCoinSorter );
	}

	void TryCompleteEnterVolumeTasks( string volumeId )
	{
		if ( _active == null || _active.tasks == null || string.IsNullOrEmpty( volumeId ) )
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

		CompleteMatchingEnterVolumeTasks( volumeId );
	}

	void CompleteMatchingEnterVolumeTasks( string volumeId )
	{
		if ( _active == null || _active.tasks == null || string.IsNullOrEmpty( volumeId ) )
			return;

		bool any = false;
		for ( int i = 0; i < _active.tasks.Length; i++ )
		{
			TutorialTask task = _active.tasks[ i ];
			if ( task == null || task.completeTrigger != TutorialTaskCompleteType.EnterVolume )
				continue;
			if ( string.IsNullOrEmpty( task.id ) || task.completeVolumeId != volumeId )
				continue;
			if ( IsTaskCompleted( _active.id, task.id ) )
				continue;

			MarkTaskCompleted( _active.id, task.id );
			any = true;
		}

		if ( !any )
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
			if ( _active != null )
				RequestHide( complete: false );
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
			_matchingScratch.Add( def );
		}

		TutorialDefinition best = PickBestMatch( _matchingScratch );

		// Sticky incomplete tutorials: do not hide on context loss, but allow a freshly
		// activated context (e.g. picking up a gem) to take priority.
		if ( _active != null && IsEligible( _active ) )
		{
			if ( best != null
			     && best.id != _active.id
			     && best.id == _lastActivatedId
			     && IsContextActive( best ) )
			{
				RequestShow( best );
				return;
			}

			if ( force )
				RefreshActivePopup();
			return;
		}

		if ( best == null )
			return;

		if ( _active != null && _active.id == best.id )
		{
			if ( force )
				RefreshActivePopup();
			return;
		}

		RequestShow( best );
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

	static bool IsAboveHeightContext( TutorialDefinition def )
	{
		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;
		if ( player == null )
			return false;
		float minY = def != null && def.minHeightY > 0f ? def.minHeightY : 30f;
		return player.transform.position.y >= minY;
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

		if ( _active != null && _active.id == def.id && _phase == CeremonyPhase.None )
			return;

		if ( _active == null && _phase == CeremonyPhase.None )
		{
			ShowTutorialNow( def, isReplay: false );
			return;
		}

		_pendingShow = def;
		if ( _phase == CeremonyPhase.None || _phase == CeremonyPhase.TaskLock )
			RequestHide( complete: false );
	}

	void ShowTutorialNow( TutorialDefinition def, bool isReplay )
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
		_sorterCoinsSortedWhileActive = 0;
		_sorterStackLoadedWhileActive = false;

		if ( isReplay )
			ClearSessionTasksFor( def );
		else
			MarkDiscovered( def.id );

		ApplyMapHighlight( def );

		_popup.Show(
			def.title,
			def.body,
			TutorialKeybindFormatter.Format( def.keybindHint ),
			TutorialPopupUI.FormatTasks( def.tasks, IsTaskCompleteInActive ) );

		TryCompleteVolumeTasksIfAlreadyInside();
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
		MapOverlayRegistrar.ClearHighlightedLabels();
		MapOverlayRegistrar.ClearTempMarkers();
		if ( _popup != null && _popup.IsVisible )
			_popup.HideImmediate();

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
		return IsTaskCompleted( _active.id, taskId );
	}

	void TryCompleteTask( TutorialTaskCompleteType completeType )
	{
		if ( completeType == TutorialTaskCompleteType.None )
			return;
		if ( _active == null || _active.tasks == null )
			return;
		if ( IsCeremonyBlocking )
			return;

		bool any = false;
		for ( int i = 0; i < _active.tasks.Length; i++ )
		{
			TutorialTask task = _active.tasks[ i ];
			if ( task == null || task.completeTrigger != completeType )
				continue;
			if ( string.IsNullOrEmpty( task.id ) )
				continue;
			if ( IsTaskCompleted( _active.id, task.id ) )
				continue;

			MarkTaskCompleted( _active.id, task.id );
			any = true;
		}

		if ( !any )
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

		_pendingShow = null;
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
		_sessionCompletedTaskKeys.Clear();
		_completedThisSession.Clear();
		_insideVolumes.Clear();
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
		result = ReplaceToken( result, "SecondaryInteract", gameInput.SecondaryInteract );
		result = ReplaceToken( result, "WholeStackPickup", gameInput.WholeStackPickup );
		result = ReplaceToken( result, "WholeStackPlace", gameInput.WholeStackPlace );
		result = ReplaceToken( result, "RotateLeft", gameInput.RotateLeft );
		result = ReplaceToken( result, "RotateRight", gameInput.RotateRight );
		result = ReplaceToken( result, "Clean", gameInput.Clean );
		result = ReplaceToken( result, "Jump", gameInput.Jump );
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
