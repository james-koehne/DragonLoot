using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Linear quest runner: catalog progress, AND step conditions, dialogue, HUD + marker events, profile save.
/// </summary>
public class QuestSystem : MonoBehaviour
{
	static QuestSystem _instance;

	const float StartingQuestDelaySeconds = 2f;
	const float PlayerMoveInputSqrThreshold = 0.01f;
	const float PlayerLookInputSqrThreshold = 0.04f;

	readonly QuestDialoguePlayer _dialogue = new QuestDialoguePlayer();
	readonly HashSet<string> _enteredVolumes = new HashSet<string>();

	QuestCatalogDefinition _catalog;
	QuestDefinition _activeQuest;
	int _questIndex = -1;
	int _stepIndex = -1;
	bool[] _conditionMet;
	int[] _conditionProgress;
	bool _subscribed;
	bool _started;
	bool _catalogComplete;
	bool _advancing;
	string _activeMarkerId;
	Vector3 _markerWorldPosition;
	bool _hasMarker;
	bool _pendingStartingQuest;
	bool _playerHasMadeInput;
	float _startingQuestReadyAt;

	public static QuestSystem Instance => _instance;

	public bool CatalogComplete => _catalogComplete;

	public QuestDefinition ActiveQuest => _activeQuest;

	public int ActiveStepIndex => _stepIndex;

	public static QuestSystem EnsureExists()
	{
		if ( _instance != null )
			return _instance;

		GameObject go = new GameObject( "QuestSystem" );
		_instance = go.AddComponent<QuestSystem>();
		Object.DontDestroyOnLoad( go );
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

	void OnDestroy()
	{
		if ( _instance == this )
			_instance = null;

		Unsubscribe();
		_dialogue.Stop();
	}

	void Update()
	{
		TickStartingQuestDelay();
		_dialogue.Tick();
		TrySkipDialogueInput();
		RefreshMarkerPosition();
	}

	public void StartOrResumeCatalog()
	{
		EnsureExists();
		_catalog = GameInstance.GetDefinition<QuestCatalogDefinition>();
		if ( _catalog == null || _catalog.Count <= 0 )
			_catalog = QuestCatalogFallback.GetOrCreate();

		if ( _catalog == null || _catalog.Count <= 0 )
		{
			Debug.LogWarning( "QuestSystem: no QuestCatalogDefinition found." );
			return;
		}

		Subscribe();

		if ( _started )
		{
			PublishHud();
			PublishProgress();
			return;
		}

		_started = true;

		QuestSceneAutoWire.EnsureWired();

		if ( !TryRestoreFromProfile() )
		{
			_questIndex = 0;
			_stepIndex = 0;
			_catalogComplete = false;
			BeginStartingQuestAfterInputAndDelay();
		}

		PublishHud();
		PublishProgress();
	}

	public void DebugCompleteActiveStep()
	{
		if ( !_started || _catalogComplete || _activeQuest == null || _advancing )
			return;

		CompleteActiveStep();
	}

	public void DebugResetProgress()
	{
		ProfileSaveData save = GetSave();
		if ( save != null )
		{
			save.EnsureQuestProgress();
			save.activeQuestId = null;
			save.activeStepIndex = 0;
			save.completedQuestIds = new List<string>();
			Persist( save );
		}

		_dialogue.Stop();
		_advancing = false;
		_catalogComplete = false;
		_questIndex = 0;
		_stepIndex = 0;
		_activeQuest = null;
		_conditionMet = null;
		_conditionProgress = null;
		CancelStartingQuestDelay();

		if ( _started && _catalog != null && _catalog.Count > 0 )
			BeginQuest( 0, playStartDialogue: true );
	}

	public void DebugSkipToQuest( int questIndex )
	{
		if ( _catalog == null || questIndex < 0 || questIndex >= _catalog.Count )
			return;

		_dialogue.Stop();
		_advancing = false;
		_catalogComplete = false;
		CancelStartingQuestDelay();

		ProfileSaveData save = GetSave();
		if ( save != null )
		{
			save.EnsureQuestProgress();
			if ( save.completedQuestIds == null )
				save.completedQuestIds = new List<string>();
			save.completedQuestIds.Clear();
			for ( int i = 0; i < questIndex; i++ )
			{
				QuestDefinition prior = _catalog.GetAt( i );
				if ( prior != null && !string.IsNullOrEmpty( prior.id ) )
					save.completedQuestIds.Add( prior.id );
			}
			Persist( save );
		}

		BeginQuest( questIndex, playStartDialogue: true );
	}

	bool TryRestoreFromProfile()
	{
		ProfileSaveData save = GetSave();
		if ( save == null )
			return false;

		save.EnsureQuestProgress();

		if ( save.completedQuestIds != null && _catalog.Count > 0 )
		{
			bool allDone = true;
			for ( int i = 0; i < _catalog.Count; i++ )
			{
				QuestDefinition quest = _catalog.GetAt( i );
				if ( quest == null || string.IsNullOrEmpty( quest.id ) )
					continue;
				if ( !save.completedQuestIds.Contains( quest.id ) )
				{
					allDone = false;
					break;
				}
			}

			if ( allDone )
			{
				_catalogComplete = true;
				_questIndex = _catalog.Count - 1;
				_stepIndex = -1;
				_activeQuest = null;
				ClearConditions();
				SetMarker( null );
				return true;
			}
		}

		int index = _catalog.IndexOfId( save.activeQuestId );
		if ( index < 0 )
			return false;

		_questIndex = index;
		_stepIndex = Mathf.Max( 0, save.activeStepIndex );
		_catalogComplete = false;
		BeginQuest( _questIndex, playStartDialogue: false, resumeStepIndex: _stepIndex );
		return true;
	}

	void BeginStartingQuestAfterInputAndDelay()
	{
		_pendingStartingQuest = true;
		_playerHasMadeInput = false;
		_startingQuestReadyAt = Time.unscaledTime + StartingQuestDelaySeconds;
	}

	void CancelStartingQuestDelay()
	{
		_pendingStartingQuest = false;
		_playerHasMadeInput = false;
		_startingQuestReadyAt = 0f;
	}

	void TickStartingQuestDelay()
	{
		if ( !_pendingStartingQuest )
			return;

		if ( !_playerHasMadeInput && HasPlayerMadeGameplayInput() )
			_playerHasMadeInput = true;

		if ( !_playerHasMadeInput || Time.unscaledTime < _startingQuestReadyAt )
			return;

		_pendingStartingQuest = false;
		BeginQuest( 0, playStartDialogue: true );
		PublishHud();
		PublishProgress();
	}

	static bool HasPlayerMadeGameplayInput()
	{
		InputController inputController = InputController.Instance;
		if ( inputController == null || !inputController.InputEnabled )
			return false;

		GameInput input = inputController.GameInput;
		if ( input == null )
			return false;

		if ( input.Move.ReadValue<Vector2>().sqrMagnitude > PlayerMoveInputSqrThreshold )
			return true;

		if ( input.CameraDelta.ReadValue<Vector2>().sqrMagnitude > PlayerLookInputSqrThreshold )
			return true;

		if ( WasPressed( input.Jump ) ||
		     WasPressed( input.Sprint ) ||
		     WasPressed( input.Interact ) ||
		     WasPressed( input.SecondaryInteract ) ||
		     WasPressed( input.Clean ) ||
		     WasPressed( input.WholeStackPickup ) ||
		     WasPressed( input.WholeStackPlace ) ||
		     WasPressed( input.RotateLeft ) ||
		     WasPressed( input.RotateRight ) )
			return true;

		if ( AnyPressed( input.AbilitySlots ) || AnyPressed( input.CategorySlots ) )
			return true;

		return false;
	}

	static bool AnyPressed( InputAction[] actions )
	{
		if ( actions == null )
			return false;

		for ( int i = 0; i < actions.Length; i++ )
		{
			if ( WasPressed( actions[ i ] ) )
				return true;
		}

		return false;
	}

	static bool WasPressed( InputAction action )
	{
		if ( action == null )
			return false;
		return action.WasPressedThisFrame();
	}

	void BeginQuest( int questIndex, bool playStartDialogue, int resumeStepIndex = 0 )
	{
		CancelStartingQuestDelay();

		QuestDefinition quest = _catalog != null ? _catalog.GetAt( questIndex ) : null;
		if ( quest == null )
		{
			MarkCatalogComplete();
			return;
		}

		_questIndex = questIndex;
		_activeQuest = quest;
		_stepIndex = Mathf.Clamp( resumeStepIndex, 0, Mathf.Max( 0, ( quest.steps != null ? quest.steps.Length : 1 ) - 1 ) );
		PersistProgress();

		if ( playStartDialogue && quest.onStartDialogue != null && quest.onStartDialogue.Length > 0 )
		{
			_advancing = true;
			_dialogue.Play( quest.onStartDialogue, () =>
			{
				_advancing = false;
				EnterStep( _stepIndex, playEnterDialogue: true );
			} );
			PublishHud();
			return;
		}

		EnterStep( _stepIndex, playEnterDialogue: playStartDialogue );
	}

	void EnterStep( int stepIndex, bool playEnterDialogue )
	{
		if ( _activeQuest == null || _activeQuest.steps == null || _activeQuest.steps.Length == 0 )
		{
			CompleteActiveQuest();
			return;
		}

		if ( stepIndex < 0 || stepIndex >= _activeQuest.steps.Length )
		{
			CompleteActiveQuest();
			return;
		}

		_stepIndex = stepIndex;
		QuestStep step = _activeQuest.steps[ stepIndex ];
		InitConditions( step );
		SetMarker( step != null ? step.markerTargetId : null );
		PersistProgress();
		PublishHud();
		PublishProgress();
		PollExistingConditionState();

		if ( playEnterDialogue && step != null && step.onEnterDialogue != null && step.onEnterDialogue.Length > 0 )
		{
			_advancing = true;
			_dialogue.Play( step.onEnterDialogue, () =>
			{
				_advancing = false;
				EvaluateStepCompletion();
			} );
			return;
		}

		EvaluateStepCompletion();
	}

	void PollExistingConditionState()
	{
		QuestStep step = GetActiveStep();
		if ( step == null || step.conditions == null || _conditionMet == null )
			return;

		for ( int i = 0; i < step.conditions.Length; i++ )
		{
			QuestCondition condition = step.conditions[ i ];
			if ( condition == null || _conditionMet[ i ] )
				continue;

			switch ( condition.type )
			{
				case QuestConditionType.EnterVolume:
				{
					if ( !string.IsNullOrEmpty( condition.targetId ) && _enteredVolumes.Contains( condition.targetId ) )
						_conditionMet[ i ] = true;
					break;
				}
				case QuestConditionType.ClearPile:
				{
					if ( QuestTargetRegistry.TryGetTarget( condition.targetId, out QuestTarget target ) && target != null )
					{
						TreasurePileInteractable pile = target.GetComponentInParent<TreasurePileInteractable>();
						if ( pile != null && pile.RemainingCount <= 0 )
							_conditionMet[ i ] = true;
					}
					break;
				}
				case QuestConditionType.CompleteConstellation:
				{
					if ( QuestTargetRegistry.TryGetTarget( condition.targetId, out QuestTarget target ) && target != null )
					{
						GemConstellationInteractable constellation = target.GetComponentInParent<GemConstellationInteractable>();
						if ( constellation != null && constellation.IsComplete )
							_conditionMet[ i ] = true;
					}
					break;
				}
				case QuestConditionType.CompleteArtifactTable:
				{
					if ( QuestTargetRegistry.TryGetTarget( condition.targetId, out QuestTarget target ) && target != null )
					{
						ArtifactPresentationTableInteractable table = target.GetComponentInParent<ArtifactPresentationTableInteractable>();
						if ( table != null && table.IsComplete )
							_conditionMet[ i ] = true;
					}
					break;
				}
				case QuestConditionType.CompleteCoinDisplay:
				{
					if ( QuestTargetRegistry.TryGetTarget( condition.targetId, out QuestTarget target ) && target != null )
					{
						CoinDisplayTableInteractable table = target.GetComponentInParent<CoinDisplayTableInteractable>();
						if ( table != null && table.IsComplete )
							_conditionMet[ i ] = true;
					}
					break;
				}
				case QuestConditionType.SectionSorted:
				{
					TreasureCounterManager manager = TreasureCounterManager.Instance;
					if ( manager != null && manager.IsSectionCompleted )
					{
						if ( string.IsNullOrEmpty( condition.sectionId ) || manager.SectionId == condition.sectionId )
							_conditionMet[ i ] = true;
					}
					break;
				}
			}
		}
	}

	void InitConditions( QuestStep step )
	{
		if ( step == null || step.conditions == null || step.conditions.Length == 0 )
		{
			_conditionMet = System.Array.Empty<bool>();
			_conditionProgress = System.Array.Empty<int>();
			return;
		}

		_conditionMet = new bool[ step.conditions.Length ];
		_conditionProgress = new int[ step.conditions.Length ];
	}

	void ClearConditions()
	{
		_conditionMet = null;
		_conditionProgress = null;
	}

	void CompleteActiveStep()
	{
		if ( _activeQuest == null || _advancing )
			return;

		QuestStep step = GetActiveStep();
		_advancing = true;

		QuestDialogueLine[] completeLines = step != null ? step.onCompleteDialogue : null;
		_dialogue.Play( completeLines, () =>
		{
			int next = _stepIndex + 1;
			if ( _activeQuest.steps == null || next >= _activeQuest.steps.Length )
			{
				_advancing = false;
				CompleteActiveQuest();
				return;
			}

			_advancing = false;
			EnterStep( next, playEnterDialogue: true );
		} );
	}

	void CompleteActiveQuest()
	{
		if ( _activeQuest == null )
		{
			MarkCatalogComplete();
			return;
		}

		string completedId = _activeQuest.id;
		ProfileSaveData save = GetSave();
		if ( save != null )
		{
			save.EnsureQuestProgress();
			if ( save.completedQuestIds == null )
				save.completedQuestIds = new List<string>();
			if ( !string.IsNullOrEmpty( completedId ) && !save.completedQuestIds.Contains( completedId ) )
				save.completedQuestIds.Add( completedId );
			Persist( save );
		}

		QuestDialogueLine[] completeLines = _activeQuest.onCompleteDialogue;
		int nextQuest = _questIndex + 1;
		_advancing = true;
		_dialogue.Play( completeLines, () =>
		{
			_advancing = false;
			if ( _catalog == null || nextQuest >= _catalog.Count )
			{
				MarkCatalogComplete();
				return;
			}

			BeginQuest( nextQuest, playStartDialogue: true );
		} );
	}

	void MarkCatalogComplete()
	{
		_catalogComplete = true;
		_activeQuest = null;
		_stepIndex = -1;
		ClearConditions();
		SetMarker( null );
		PersistProgress();
		PublishHud();
		PublishProgress();
	}

	QuestStep GetActiveStep()
	{
		if ( _activeQuest == null || _activeQuest.steps == null )
			return null;
		if ( _stepIndex < 0 || _stepIndex >= _activeQuest.steps.Length )
			return null;
		return _activeQuest.steps[ _stepIndex ];
	}

	void EvaluateStepCompletion()
	{
		if ( _advancing || _catalogComplete || _activeQuest == null )
			return;

		QuestStep step = GetActiveStep();
		if ( step == null )
		{
			CompleteActiveStep();
			return;
		}

		if ( step.conditions == null || step.conditions.Length == 0 )
		{
			CompleteActiveStep();
			return;
		}

		if ( _conditionMet == null || _conditionMet.Length != step.conditions.Length )
			return;

		for ( int i = 0; i < _conditionMet.Length; i++ )
		{
			if ( !_conditionMet[ i ] )
				return;
		}

		CompleteActiveStep();
	}

	void MarkCondition( int index )
	{
		if ( _conditionMet == null || index < 0 || index >= _conditionMet.Length )
			return;
		if ( _conditionMet[ index ] )
			return;

		_conditionMet[ index ] = true;
		EvaluateStepCompletion();
	}

	void Subscribe()
	{
		if ( _subscribed )
			return;

		EventBus.Subscribe<QuestVolumeEnteredEvent>( OnVolumeEntered );
		EventBus.Subscribe<TreasurePileEmptiedEvent>( OnPileEmptied );
		EventBus.Subscribe<PlacementCompletedEvent>( OnPlacementCompleted );
		EventBus.Subscribe<TreasurePlacedOnSortingTableEvent>( OnSortingTablePlaced );
		EventBus.Subscribe<CoinSorterUsedEvent>( OnCoinSorterUsed );
		EventBus.Subscribe<GemConstellationCompletedEvent>( OnConstellationCompleted );
		EventBus.Subscribe<ArtifactPresentationTableCompletedEvent>( OnArtifactTableCompleted );
		EventBus.Subscribe<CoinDisplayTableCompletedEvent>( OnCoinDisplayCompleted );
		EventBus.Subscribe<SectionCompletedEvent>( OnSectionCompleted );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;

		EventBus.Unsubscribe<QuestVolumeEnteredEvent>( OnVolumeEntered );
		EventBus.Unsubscribe<TreasurePileEmptiedEvent>( OnPileEmptied );
		EventBus.Unsubscribe<PlacementCompletedEvent>( OnPlacementCompleted );
		EventBus.Unsubscribe<TreasurePlacedOnSortingTableEvent>( OnSortingTablePlaced );
		EventBus.Unsubscribe<CoinSorterUsedEvent>( OnCoinSorterUsed );
		EventBus.Unsubscribe<GemConstellationCompletedEvent>( OnConstellationCompleted );
		EventBus.Unsubscribe<ArtifactPresentationTableCompletedEvent>( OnArtifactTableCompleted );
		EventBus.Unsubscribe<CoinDisplayTableCompletedEvent>( OnCoinDisplayCompleted );
		EventBus.Unsubscribe<SectionCompletedEvent>( OnSectionCompleted );
		_subscribed = false;
	}

	void OnVolumeEntered( QuestVolumeEnteredEvent evt )
	{
		if ( !string.IsNullOrEmpty( evt.VolumeId ) )
			_enteredVolumes.Add( evt.VolumeId );

		ForEachMatchingCondition( QuestConditionType.EnterVolume, evt.VolumeId, null, MarkCondition );
	}

	void OnPileEmptied( TreasurePileEmptiedEvent evt )
	{
		string id = QuestTargetRegistry.ResolveTargetId( evt.Pile );
		ForEachMatchingCondition( QuestConditionType.ClearPile, id, null, MarkCondition );
	}

	void OnPlacementCompleted( PlacementCompletedEvent evt )
	{
		Component targetComponent = evt.Target as Component;
		string id = QuestTargetRegistry.ResolveTargetId( targetComponent );
		HandlePlaceProgress( id, evt.Definition );
	}

	void OnSortingTablePlaced( TreasurePlacedOnSortingTableEvent evt )
	{
		// Freeform table may also carry a QuestTarget on the same object via PlacementCompleted;
		// this event has no target ref — ignore unless we find a single PlaceOnOwner with empty filter.
	}

	void OnCoinSorterUsed( CoinSorterUsedEvent evt )
	{
		string id = QuestTargetRegistry.ResolveTargetId( evt.Station );
		ForEachMatchingCondition( QuestConditionType.UseCoinSorter, id, null, MarkCondition );
	}

	void OnConstellationCompleted( GemConstellationCompletedEvent evt )
	{
		string id = QuestTargetRegistry.ResolveTargetId( evt.Constellation );
		ForEachMatchingCondition( QuestConditionType.CompleteConstellation, id, null, MarkCondition );
	}

	void OnArtifactTableCompleted( ArtifactPresentationTableCompletedEvent evt )
	{
		string id = QuestTargetRegistry.ResolveTargetId( evt.Table );
		ForEachMatchingCondition( QuestConditionType.CompleteArtifactTable, id, null, MarkCondition );
	}

	void OnCoinDisplayCompleted( CoinDisplayTableCompletedEvent evt )
	{
		string id = QuestTargetRegistry.ResolveTargetId( evt.Table );
		ForEachMatchingCondition( QuestConditionType.CompleteCoinDisplay, id, null, MarkCondition );
	}

	void OnSectionCompleted( SectionCompletedEvent evt )
	{
		QuestStep step = GetActiveStep();
		if ( step == null || step.conditions == null || _conditionMet == null )
			return;

		for ( int i = 0; i < step.conditions.Length; i++ )
		{
			QuestCondition condition = step.conditions[ i ];
			if ( condition == null || condition.type != QuestConditionType.SectionSorted )
				continue;
			if ( _conditionMet[ i ] )
				continue;
			if ( !string.IsNullOrEmpty( condition.sectionId ) && condition.sectionId != evt.SectionId )
				continue;
			MarkCondition( i );
		}
	}

	void HandlePlaceProgress( string targetId, TreasureDefinition treasure )
	{
		QuestStep step = GetActiveStep();
		if ( step == null || step.conditions == null || _conditionMet == null || _conditionProgress == null )
			return;

		for ( int i = 0; i < step.conditions.Length; i++ )
		{
			QuestCondition condition = step.conditions[ i ];
			if ( condition == null || condition.type != QuestConditionType.PlaceOnOwner )
				continue;
			if ( _conditionMet[ i ] )
				continue;
			if ( !string.IsNullOrEmpty( condition.targetId ) && condition.targetId != targetId )
				continue;
			if ( condition.requiredTreasure != null && condition.requiredTreasure != treasure )
				continue;

			_conditionProgress[ i ] = _conditionProgress[ i ] + 1;
			if ( _conditionProgress[ i ] >= Mathf.Max( 1, condition.requiredCount ) )
				MarkCondition( i );
		}
	}

	void ForEachMatchingCondition(
		QuestConditionType type,
		string targetId,
		TreasureDefinition treasure,
		System.Action<int> onMatch )
	{
		QuestStep step = GetActiveStep();
		if ( step == null || step.conditions == null || _conditionMet == null )
			return;

		for ( int i = 0; i < step.conditions.Length; i++ )
		{
			QuestCondition condition = step.conditions[ i ];
			if ( condition == null || condition.type != type )
				continue;
			if ( _conditionMet[ i ] )
				continue;

			if ( !string.IsNullOrEmpty( condition.targetId ) )
			{
				if ( condition.targetId != targetId )
					continue;
			}

			if ( treasure != null && condition.requiredTreasure != null && condition.requiredTreasure != treasure )
				continue;

			onMatch( i );
		}
	}

	void SetMarker( string markerTargetId )
	{
		_activeMarkerId = markerTargetId;
		RefreshMarkerPosition();
	}

	void RefreshMarkerPosition()
	{
		bool had = _hasMarker;
		Vector3 previous = _markerWorldPosition;

		if ( string.IsNullOrEmpty( _activeMarkerId ) ||
		     !QuestTargetRegistry.TryGetMarkerTransform( _activeMarkerId, out Transform marker ) ||
		     marker == null )
		{
			_hasMarker = false;
		}
		else
		{
			_hasMarker = true;
			_markerWorldPosition = marker.position;
		}

		if ( had != _hasMarker || ( _hasMarker && ( previous - _markerWorldPosition ).sqrMagnitude > 0.01f ) )
			PublishHud();
	}

	void PublishHud()
	{
		QuestStep step = GetActiveStep();
		EventBus.Publish( new QuestHudChangedEvent
		{
			QuestId = _activeQuest != null ? _activeQuest.id : null,
			QuestTitle = _activeQuest != null ? _activeQuest.ResolveTitle() : null,
			StepId = step != null ? step.id : null,
			ObjectiveText = _catalogComplete
				? string.Empty
				: ( step != null ? step.objectiveText : string.Empty ),
			HasMarker = _hasMarker && !_catalogComplete,
			MarkerWorldPosition = _markerWorldPosition,
			CatalogComplete = _catalogComplete
		} );
	}

	void PublishProgress()
	{
		EventBus.Publish( new QuestProgressChangedEvent
		{
			ActiveQuestId = _activeQuest != null ? _activeQuest.id : null,
			ActiveStepIndex = _stepIndex,
			CatalogComplete = _catalogComplete
		} );
	}

	void PersistProgress()
	{
		ProfileSaveData save = GetSave();
		if ( save == null )
			return;

		save.EnsureQuestProgress();
		save.activeQuestId = _catalogComplete ? null : ( _activeQuest != null ? _activeQuest.id : null );
		save.activeStepIndex = _catalogComplete ? 0 : Mathf.Max( 0, _stepIndex );
		Persist( save );
	}

	void TrySkipDialogueInput()
	{
		if ( !_dialogue.IsPlaying )
			return;

		InputController inputController = InputController.Instance;
		if ( inputController == null || inputController.GameInput == null )
			return;

		if ( inputController.GameInput.Interact.WasPressedThisFrame() ||
		     inputController.GameInput.SecondaryInteract.WasPressedThisFrame() )
		{
			_dialogue.Skip();
		}
	}

	static ProfileSaveData GetSave()
	{
		ProfileManager manager = ProfileManager.Instance;
		if ( manager == null )
			return null;
		return manager.ProfileSaveData;
	}

	static void Persist( ProfileSaveData save )
	{
		if ( save == null )
			return;

		ProfileManager manager = ProfileManager.Instance;
		if ( manager == null )
			return;

		manager.SaveCurrentStatsToProfile();
	}
}
