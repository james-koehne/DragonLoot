using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Quest runner: sequential objectives with parallel sub-objectives, nested subquests, and non-blocking events.
/// </summary>
public class QuestSystem : MonoBehaviour
{
	class LiveObjective
	{
		public QuestDefinition Owner;
		public QuestObjective Objective;
		public bool Complete;
		public bool NotifiedComplete;
		public bool[] ConditionMet;
		public int[] ConditionProgress;
	}

	class LiveEvent
	{
		public QuestDefinition Owner;
		public QuestEvent Event;
		public bool Fired;
		public bool[] ConditionMet;
		public int[] ConditionProgress;
	}

	static QuestSystem _instance;

	const float StartingQuestDelaySeconds = 2f;
	const float PlayerMoveInputSqrThreshold = 0.01f;
	const float PlayerLookInputSqrThreshold = 0.04f;

	readonly QuestDialoguePlayer _dialogue = new QuestDialoguePlayer();
	readonly HashSet<string> _enteredVolumes = new HashSet<string>();
	readonly List<LiveObjective> _liveObjectives = new List<LiveObjective>();
	readonly List<LiveEvent> _liveEvents = new List<LiveEvent>();
	readonly List<QuestDefinition> _revealedSubquests = new List<QuestDefinition>();
	readonly List<QuestHudRow> _hudRows = new List<QuestHudRow>();
	readonly List<CoinDisplayTableInteractable> _scratchCoinTables = new List<CoinDisplayTableInteractable>();
	readonly List<GoldBarDisplayTableInteractable> _scratchGoldTables = new List<GoldBarDisplayTableInteractable>();
	readonly List<ArtifactPresentationTableInteractable> _scratchArtifactTables = new List<ArtifactPresentationTableInteractable>();
	readonly List<GemConstellationInteractable> _scratchConstellations = new List<GemConstellationInteractable>();
	readonly List<TreasurePileInteractable> _scratchPiles = new List<TreasurePileInteractable>();

	QuestCatalogDefinition _catalog;
	QuestDefinition _activeQuest;
	int _questIndex = -1;
	int _stepIndex = -1;
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

	public void CollectActiveOutlineRoots( System.Action<Transform> onRoot )
	{
		if ( onRoot == null || _catalogComplete || _activeQuest == null )
			return;

		for ( int i = 0; i < _liveObjectives.Count; i++ )
		{
			LiveObjective live = _liveObjectives[ i ];
			if ( live == null || live.Complete || live.Objective == null || live.Objective.optional )
				continue;

			if ( !string.IsNullOrEmpty( live.Objective.markerTargetId ) )
				TryAddOutlineRoot( live.Objective.markerTargetId, onRoot );

			if ( live.Objective.conditions == null )
				continue;

			for ( int c = 0; c < live.Objective.conditions.Length; c++ )
			{
				QuestCondition condition = live.Objective.conditions[ c ];
				if ( condition == null || string.IsNullOrEmpty( condition.targetId ) )
					continue;
				if ( condition.type == QuestConditionType.EnterVolume )
					continue;
				if ( live.ConditionMet != null && c < live.ConditionMet.Length && live.ConditionMet[ c ] )
					continue;
				TryAddOutlineRoot( condition.targetId, onRoot );
			}
		}
	}

	static void TryAddOutlineRoot( string id, System.Action<Transform> onRoot )
	{
		if ( string.IsNullOrEmpty( id ) || onRoot == null )
			return;
		if ( QuestTargetRegistry.TryGetTarget( id, out QuestTarget target ) && target != null )
			onRoot( target.ResolveOutlineRoot() );
	}

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

		CompleteSequentialObjective();
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
			save.completedObjectiveIds = new List<string>();
			save.firedEventIds = new List<string>();
			Persist( save );
		}

		_dialogue.Stop();
		_advancing = false;
		_catalogComplete = false;
		_questIndex = 0;
		_stepIndex = 0;
		_activeQuest = null;
		ClearLive();
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
			if ( save.completedObjectiveIds == null )
				save.completedObjectiveIds = new List<string>();
			save.completedObjectiveIds.Clear();
			if ( save.firedEventIds == null )
				save.firedEventIds = new List<string>();
			save.firedEventIds.Clear();
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
				ClearLive();
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
		BeginQuest( _questIndex, playStartDialogue: false, resumeObjectiveIndex: _stepIndex );
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

	void BeginQuest( int questIndex, bool playStartDialogue, int resumeObjectiveIndex = 0 )
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
		int objectiveCount = quest.objectives != null ? quest.objectives.Length : 0;
		_stepIndex = Mathf.Clamp( resumeObjectiveIndex, 0, Mathf.Max( 0, objectiveCount - 1 ) );
		PersistProgress();

		if ( playStartDialogue && quest.onStartDialogue != null && quest.onStartDialogue.Length > 0 )
		{
			_advancing = true;
			_dialogue.Enqueue( quest.onStartDialogue, () =>
			{
				_advancing = false;
				EnterSequentialObjective( _stepIndex, playCompleteIfAlreadyDone: true );
			} );
			PublishHud();
			return;
		}

		EnterSequentialObjective( _stepIndex, playCompleteIfAlreadyDone: true );
	}

	void EnterSequentialObjective( int objectiveIndex, bool playCompleteIfAlreadyDone )
	{
		if ( _activeQuest == null || _activeQuest.objectives == null || _activeQuest.objectives.Length == 0 )
		{
			CompleteActiveQuest();
			return;
		}

		if ( objectiveIndex < 0 || objectiveIndex >= _activeQuest.objectives.Length )
		{
			CompleteActiveQuest();
			return;
		}

		_stepIndex = objectiveIndex;
		BuildLiveForCurrentObjective();
		PersistProgress();
		PublishHud();
		PublishProgress();
		PollAllLive();
		EvaluateLiveCompletions( playCompleteDialogue: playCompleteIfAlreadyDone );
	}

	void BuildLiveForCurrentObjective()
	{
		ClearLive();
		if ( _activeQuest == null )
			return;

		QuestObjective sequential = GetActiveSequentialObjective();
		if ( sequential == null )
			return;

		AddLiveObjectiveTree( _activeQuest, sequential );
		RevealSubquests( sequential.id );
		AddLiveEvents( _activeQuest );

		for ( int i = 0; i < _revealedSubquests.Count; i++ )
		{
			QuestDefinition sub = _revealedSubquests[ i ];
			if ( sub == null )
				continue;

			AddLiveEvents( sub );
			if ( sub.objectives == null )
				continue;

			for ( int o = 0; o < sub.objectives.Length; o++ )
				AddLiveObjectiveTree( sub, sub.objectives[ o ] );
		}

		SetMarker( ResolveFocusMarkerId() );
	}

	void RevealSubquests( string parentObjectiveId )
	{
		_revealedSubquests.Clear();
		if ( _activeQuest == null || _activeQuest.subquests == null )
			return;

		for ( int i = 0; i < _activeQuest.subquests.Length; i++ )
		{
			QuestDefinition sub = _activeQuest.subquests[ i ];
			if ( sub == null )
				continue;

			if ( !string.IsNullOrEmpty( sub.revealWhenParentObjectiveId ) &&
			     sub.revealWhenParentObjectiveId != parentObjectiveId )
				continue;

			_revealedSubquests.Add( sub );
		}
	}

	void AddLiveObjectiveTree( QuestDefinition owner, QuestObjective objective )
	{
		if ( owner == null || objective == null )
			return;

		LiveObjective live = new LiveObjective
		{
			Owner = owner,
			Objective = objective,
			Complete = IsObjectiveSavedComplete( owner.id, objective.id )
		};
		InitConditionArrays( objective.conditions, live.Complete, out live.ConditionMet, out live.ConditionProgress );
		if ( live.Complete )
			live.NotifiedComplete = true;
		_liveObjectives.Add( live );

		if ( objective.subObjectives == null )
			return;

		for ( int i = 0; i < objective.subObjectives.Length; i++ )
			AddLiveObjectiveTree( owner, objective.subObjectives[ i ] );
	}

	void AddLiveEvents( QuestDefinition owner )
	{
		if ( owner == null || owner.events == null )
			return;

		for ( int i = 0; i < owner.events.Length; i++ )
		{
			QuestEvent questEvent = owner.events[ i ];
			if ( questEvent == null )
				continue;

			LiveEvent live = new LiveEvent
			{
				Owner = owner,
				Event = questEvent,
				Fired = IsEventSavedFired( owner.id, questEvent.id )
			};
			InitConditionArrays( questEvent.conditions, live.Fired, out live.ConditionMet, out live.ConditionProgress );
			_liveEvents.Add( live );
		}
	}

	static void InitConditionArrays( QuestCondition[] conditions, bool alreadyDone, out bool[] met, out int[] progress )
	{
		if ( conditions == null || conditions.Length == 0 )
		{
			met = System.Array.Empty<bool>();
			progress = System.Array.Empty<int>();
			return;
		}

		met = new bool[ conditions.Length ];
		progress = new int[ conditions.Length ];
		if ( !alreadyDone )
			return;

		for ( int i = 0; i < met.Length; i++ )
			met[ i ] = true;
	}

	void ClearLive()
	{
		_liveObjectives.Clear();
		_liveEvents.Clear();
		_revealedSubquests.Clear();
	}

	QuestObjective GetActiveSequentialObjective()
	{
		if ( _activeQuest == null || _activeQuest.objectives == null )
			return null;
		if ( _stepIndex < 0 || _stepIndex >= _activeQuest.objectives.Length )
			return null;
		return _activeQuest.objectives[ _stepIndex ];
	}

	void CompleteSequentialObjective()
	{
		if ( _activeQuest == null || _advancing )
			return;

		QuestObjective sequential = GetActiveSequentialObjective();
		MarkObjectiveSavedComplete( _activeQuest.id, sequential != null ? sequential.id : null );

		_advancing = true;
		QuestDialogueLine[] lines = sequential != null ? sequential.onCompleteDialogue : null;
		_dialogue.Enqueue( lines, () =>
		{
			int next = _stepIndex + 1;
			if ( _activeQuest.objectives == null || next >= _activeQuest.objectives.Length )
			{
				_advancing = false;
				CompleteActiveQuest();
				return;
			}

			_advancing = false;
			EnterSequentialObjective( next, playCompleteIfAlreadyDone: true );
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
		AddCompletedQuestId( completedId );

		for ( int i = 0; i < _revealedSubquests.Count; i++ )
		{
			QuestDefinition sub = _revealedSubquests[ i ];
			if ( sub != null )
				AddCompletedQuestId( sub.id );
		}

		QuestDialogueLine[] completeLines = _activeQuest.onCompleteDialogue;
		int nextQuest = _questIndex + 1;
		_advancing = true;
		_dialogue.Enqueue( completeLines, () =>
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
		ClearLive();
		SetMarker( null );
		PersistProgress();
		PublishHud();
		PublishProgress();
	}

	void PollAllLive()
	{
		for ( int i = 0; i < _liveObjectives.Count; i++ )
			PollLiveObjective( _liveObjectives[ i ] );

		for ( int i = 0; i < _liveEvents.Count; i++ )
			PollLiveEvent( _liveEvents[ i ] );
	}

	void PollLiveObjective( LiveObjective live )
	{
		if ( live == null || live.Complete || live.Objective == null || live.Objective.conditions == null )
			return;

		for ( int i = 0; i < live.Objective.conditions.Length; i++ )
		{
			if ( live.ConditionMet != null && i < live.ConditionMet.Length && live.ConditionMet[ i ] )
				continue;
			if ( IsConditionCurrentlyMet( live.Objective.conditions[ i ], live.ConditionProgress, i ) )
				MarkLiveCondition( live.ConditionMet, i );
		}
	}

	void PollLiveEvent( LiveEvent live )
	{
		if ( live == null || live.Fired || live.Event == null || live.Event.conditions == null )
			return;

		for ( int i = 0; i < live.Event.conditions.Length; i++ )
		{
			if ( live.ConditionMet != null && i < live.ConditionMet.Length && live.ConditionMet[ i ] )
				continue;
			if ( IsConditionCurrentlyMet( live.Event.conditions[ i ], live.ConditionProgress, i ) )
				MarkLiveCondition( live.ConditionMet, i );
		}
	}

	bool IsConditionCurrentlyMet( QuestCondition condition, int[] progress, int index )
	{
		if ( condition == null )
			return false;

		switch ( condition.type )
		{
			case QuestConditionType.EnterVolume:
				return !string.IsNullOrEmpty( condition.targetId ) && _enteredVolumes.Contains( condition.targetId );
			case QuestConditionType.ClearPile:
				return AreAllPilesCleared( condition );
			case QuestConditionType.PlaceOnOwner:
				return IsPlaceCountMet( condition, progress, index );
			case QuestConditionType.CompleteConstellation:
				return AreAllConstellationsComplete( condition );
			case QuestConditionType.CompleteArtifactTable:
				return AreAllArtifactTablesComplete( condition );
			case QuestConditionType.CompleteCoinDisplay:
				return AreAllCoinTablesComplete( condition );
			case QuestConditionType.CompleteGoldBarDisplay:
				return AreAllGoldBarTablesComplete( condition );
			case QuestConditionType.SectionSorted:
			{
				TreasureCounterManager manager = TreasureCounterManager.Instance;
				if ( manager == null || !manager.IsSectionCompleted )
					return false;
				return string.IsNullOrEmpty( condition.sectionId ) || manager.SectionId == condition.sectionId;
			}
			case QuestConditionType.UseCoinSorter:
			case QuestConditionType.PickupTreasure:
				return false;
			default:
				return false;
		}
	}

	bool AreAllCoinTablesComplete( QuestCondition condition )
	{
		_scratchCoinTables.Clear();
		CollectCoinTables( condition, _scratchCoinTables );
		if ( _scratchCoinTables.Count == 0 )
			return false;
		for ( int i = 0; i < _scratchCoinTables.Count; i++ )
		{
			if ( _scratchCoinTables[ i ] == null || !_scratchCoinTables[ i ].IsComplete )
				return false;
		}
		return true;
	}

	bool AreAllGoldBarTablesComplete( QuestCondition condition )
	{
		_scratchGoldTables.Clear();
		CollectGoldTables( condition, _scratchGoldTables );
		if ( _scratchGoldTables.Count == 0 )
			return false;
		for ( int i = 0; i < _scratchGoldTables.Count; i++ )
		{
			if ( _scratchGoldTables[ i ] == null || !_scratchGoldTables[ i ].IsComplete )
				return false;
		}
		return true;
	}

	bool AreAllArtifactTablesComplete( QuestCondition condition )
	{
		_scratchArtifactTables.Clear();
		CollectArtifactTables( condition, _scratchArtifactTables );
		if ( _scratchArtifactTables.Count == 0 )
			return false;
		for ( int i = 0; i < _scratchArtifactTables.Count; i++ )
		{
			if ( _scratchArtifactTables[ i ] == null || !_scratchArtifactTables[ i ].IsComplete )
				return false;
		}
		return true;
	}

	bool AreAllConstellationsComplete( QuestCondition condition )
	{
		_scratchConstellations.Clear();
		CollectConstellations( condition, _scratchConstellations );
		if ( _scratchConstellations.Count == 0 )
			return false;
		for ( int i = 0; i < _scratchConstellations.Count; i++ )
		{
			if ( _scratchConstellations[ i ] == null || !_scratchConstellations[ i ].IsComplete )
				return false;
		}
		return true;
	}

	bool AreAllPilesCleared( QuestCondition condition )
	{
		_scratchPiles.Clear();
		CollectPiles( condition, _scratchPiles );
		if ( _scratchPiles.Count == 0 )
			return false;
		for ( int i = 0; i < _scratchPiles.Count; i++ )
		{
			if ( _scratchPiles[ i ] == null || _scratchPiles[ i ].RemainingCount > 0 )
				return false;
		}
		return true;
	}

	static bool IsPlaceCountMet( QuestCondition condition, int[] progress, int index )
	{
		if ( condition == null || progress == null || index < 0 || index >= progress.Length )
			return false;
		return progress[ index ] >= Mathf.Max( 1, condition.requiredCount );
	}

	void CollectCoinTables( QuestCondition condition, List<CoinDisplayTableInteractable> into )
	{
		CollectTyped( condition, into );
		if ( condition == null || into == null )
			return;
		if ( condition.areaId != QuestSceneAutoWire.AreaStarting )
			return;

		GameObject startingArea = GameObject.Find( "StartingArea" );
		if ( startingArea == null )
			return;

		CoinDisplayTableInteractable[] tables = startingArea.GetComponentsInChildren<CoinDisplayTableInteractable>( true );
		for ( int i = 0; i < tables.Length; i++ )
		{
			if ( tables[ i ] != null && !into.Contains( tables[ i ] ) )
				into.Add( tables[ i ] );
		}
	}

	void CollectGoldTables( QuestCondition condition, List<GoldBarDisplayTableInteractable> into )
	{
		CollectTyped( condition, into );
		if ( condition == null || into == null )
			return;
		if ( condition.areaId != QuestSceneAutoWire.AreaStarting )
			return;

		GameObject startingArea = GameObject.Find( "StartingArea" );
		if ( startingArea != null )
		{
			GoldBarDisplayTableInteractable[] nested = startingArea.GetComponentsInChildren<GoldBarDisplayTableInteractable>( true );
			for ( int i = 0; i < nested.Length; i++ )
			{
				if ( nested[ i ] != null && !into.Contains( nested[ i ] ) )
					into.Add( nested[ i ] );
			}
		}

		GameObject named = GameObject.Find( "GoldBarDisplayTable" );
		if ( named == null )
			return;
		GoldBarDisplayTableInteractable table = named.GetComponent<GoldBarDisplayTableInteractable>();
		if ( table != null && !into.Contains( table ) )
			into.Add( table );
	}

	void CollectArtifactTables( QuestCondition condition, List<ArtifactPresentationTableInteractable> into )
	{
		CollectTyped( condition, into );
	}

	void CollectConstellations( QuestCondition condition, List<GemConstellationInteractable> into )
	{
		CollectTyped( condition, into );
	}

	void CollectPiles( QuestCondition condition, List<TreasurePileInteractable> into )
	{
		CollectTyped( condition, into );
	}

	void CollectTyped<T>( QuestCondition condition, List<T> into ) where T : Component
	{
		if ( condition == null || into == null )
			return;

		if ( !string.IsNullOrEmpty( condition.targetId ) )
		{
			if ( QuestTargetRegistry.TryGetTarget( condition.targetId, out QuestTarget target ) && target != null )
			{
				T match = target.GetComponentInParent<T>();
				if ( match == null )
					match = target.GetComponentInChildren<T>( true );
				if ( match != null && !into.Contains( match ) )
					into.Add( match );
			}
		}
		else if ( !string.IsNullOrEmpty( condition.areaId ) )
		{
			QuestTargetRegistry.CollectInArea( condition.areaId, into );
		}

		if ( into.Count > 0 )
			return;

		if ( typeof( T ) != typeof( GoldBarDisplayTableInteractable ) &&
		     typeof( T ) != typeof( GemConstellationInteractable ) &&
		     typeof( T ) != typeof( ArtifactPresentationTableInteractable ) )
			return;

		T[] all = Object.FindObjectsByType<T>( FindObjectsInactive.Include, FindObjectsSortMode.None );
		for ( int i = 0; i < all.Length; i++ )
		{
			T candidate = all[ i ];
			if ( candidate == null || into.Contains( candidate ) )
				continue;
			if ( !MatchesAreaFallback( condition, candidate ) )
				continue;
			into.Add( candidate );
		}
	}

	static bool MatchesAreaFallback( QuestCondition condition, Component candidate )
	{
		if ( condition == null || candidate == null )
			return false;
		if ( string.IsNullOrEmpty( condition.areaId ) )
			return true;

		string area = QuestTargetRegistry.ResolveAreaId( candidate );
		if ( area == condition.areaId )
			return true;
		if ( condition.areaId != QuestSceneAutoWire.AreaStarting )
			return false;

		GameObject startingArea = GameObject.Find( "StartingArea" );
		if ( startingArea == null )
			return false;
		return candidate.transform.IsChildOf( startingArea.transform );
	}

	void EvaluateLiveCompletions( bool playCompleteDialogue )
	{
		bool anyHud = false;
		bool progressed = true;
		while ( progressed )
		{
			progressed = false;
			for ( int i = 0; i < _liveObjectives.Count; i++ )
			{
				LiveObjective live = _liveObjectives[ i ];
				if ( live == null || live.Complete )
					continue;

				if ( !AreObjectiveConditionsMet( live ) )
					continue;
				if ( !AreRequiredChildrenComplete( live ) )
					continue;

				live.Complete = true;
				progressed = true;
				MarkObjectiveSavedComplete( live.Owner != null ? live.Owner.id : null, live.Objective != null ? live.Objective.id : null );
				anyHud = true;

				if ( playCompleteDialogue && !live.NotifiedComplete )
				{
					live.NotifiedComplete = true;
					if ( live.Objective != null && live.Objective.onCompleteDialogue != null && live.Objective.onCompleteDialogue.Length > 0 )
						_dialogue.Enqueue( live.Objective.onCompleteDialogue );
				}
				else
					live.NotifiedComplete = true;

				TryCompleteLinkedSubquest( live );
			}
		}

		for ( int i = 0; i < _liveEvents.Count; i++ )
		{
			LiveEvent live = _liveEvents[ i ];
			if ( live == null || live.Fired )
				continue;
			if ( !AreEventConditionsMet( live ) )
				continue;

			FireEvent( live );
			anyHud = true;
		}

		if ( anyHud )
		{
			SetMarker( ResolveFocusMarkerId() );
			PublishHud();
			PersistProgress();
		}

		QuestObjective sequential = GetActiveSequentialObjective();
		LiveObjective sequentialLive = FindLive( _activeQuest, sequential );
		if ( sequentialLive != null &&
		     sequentialLive.Complete &&
		     sequentialLive.Objective == sequential &&
		     !_advancing &&
		     !_catalogComplete )
			CompleteSequentialObjective();
	}

	void TryCompleteLinkedSubquest( LiveObjective live )
	{
		if ( live == null || live.Objective == null || live.Owner != _activeQuest )
			return;

		for ( int i = 0; i < _revealedSubquests.Count; i++ )
		{
			QuestDefinition sub = _revealedSubquests[ i ];
			if ( sub == null || sub.linkedParentObjectiveId != live.Objective.id )
				continue;
			if ( IsQuestSavedComplete( sub.id ) )
				continue;

			AddCompletedQuestId( sub.id );
			if ( sub.onCompleteDialogue != null && sub.onCompleteDialogue.Length > 0 )
				_dialogue.Enqueue( sub.onCompleteDialogue );
		}
	}

	void FireEvent( LiveEvent live )
	{
		if ( live == null || live.Event == null )
			return;

		live.Fired = true;
		MarkEventSavedFired( live.Owner != null ? live.Owner.id : null, live.Event.id );

		if ( live.Event.stinger != null )
			QuestDialogueSfx.PlayClip( live.Event.stinger, live.Event.stingerVol );

		if ( live.Event.dialogue != null && live.Event.dialogue.Length > 0 )
			_dialogue.Enqueue( live.Event.dialogue );
	}

	bool AreObjectiveConditionsMet( LiveObjective live )
	{
		if ( live == null || live.Objective == null )
			return true;
		if ( live.Objective.conditions == null || live.Objective.conditions.Length == 0 )
			return true;
		if ( live.ConditionMet == null || live.ConditionMet.Length != live.Objective.conditions.Length )
			return false;

		for ( int i = 0; i < live.ConditionMet.Length; i++ )
		{
			if ( !live.ConditionMet[ i ] )
				return false;
		}

		return true;
	}

	bool AreEventConditionsMet( LiveEvent live )
	{
		if ( live == null || live.Event == null )
			return false;
		if ( live.Event.conditions == null || live.Event.conditions.Length == 0 )
			return false;
		if ( live.ConditionMet == null || live.ConditionMet.Length != live.Event.conditions.Length )
			return false;

		for ( int i = 0; i < live.ConditionMet.Length; i++ )
		{
			if ( !live.ConditionMet[ i ] )
				return false;
		}

		return true;
	}

	bool AreRequiredChildrenComplete( LiveObjective live )
	{
		if ( live == null || live.Objective == null || live.Objective.subObjectives == null )
			return true;

		for ( int i = 0; i < live.Objective.subObjectives.Length; i++ )
		{
			QuestObjective child = live.Objective.subObjectives[ i ];
			if ( child == null || child.optional )
				continue;

			LiveObjective childLive = FindLive( live.Owner, child );
			if ( childLive == null || !childLive.Complete )
				return false;
		}

		return true;
	}

	LiveObjective FindLive( QuestDefinition owner, QuestObjective objective )
	{
		if ( owner == null || objective == null )
			return null;

		for ( int i = 0; i < _liveObjectives.Count; i++ )
		{
			LiveObjective live = _liveObjectives[ i ];
			if ( live != null && live.Owner == owner && live.Objective == objective )
				return live;
		}

		return null;
	}

	void Subscribe()
	{
		if ( _subscribed )
			return;

		EventBus.Subscribe<QuestVolumeEnteredEvent>( OnVolumeEntered );
		EventBus.Subscribe<TreasurePileEmptiedEvent>( OnPileEmptied );
		EventBus.Subscribe<PlacementCompletedEvent>( OnPlacementCompleted );
		EventBus.Subscribe<CoinSorterUsedEvent>( OnCoinSorterUsed );
		EventBus.Subscribe<GemConstellationCompletedEvent>( OnConstellationCompleted );
		EventBus.Subscribe<ArtifactPresentationTableCompletedEvent>( OnArtifactTableCompleted );
		EventBus.Subscribe<CoinDisplayTableCompletedEvent>( OnCoinDisplayCompleted );
		EventBus.Subscribe<GoldBarDisplayTableCompletedEvent>( OnGoldBarDisplayCompleted );
		EventBus.Subscribe<SectionCompletedEvent>( OnSectionCompleted );
		EventBus.Subscribe<TreasureCollectedEvent>( OnTreasureCollected );
		EventBus.Subscribe<PouchChangedEvent>( OnPouchChanged );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;

		EventBus.Unsubscribe<QuestVolumeEnteredEvent>( OnVolumeEntered );
		EventBus.Unsubscribe<TreasurePileEmptiedEvent>( OnPileEmptied );
		EventBus.Unsubscribe<PlacementCompletedEvent>( OnPlacementCompleted );
		EventBus.Unsubscribe<CoinSorterUsedEvent>( OnCoinSorterUsed );
		EventBus.Unsubscribe<GemConstellationCompletedEvent>( OnConstellationCompleted );
		EventBus.Unsubscribe<ArtifactPresentationTableCompletedEvent>( OnArtifactTableCompleted );
		EventBus.Unsubscribe<CoinDisplayTableCompletedEvent>( OnCoinDisplayCompleted );
		EventBus.Unsubscribe<GoldBarDisplayTableCompletedEvent>( OnGoldBarDisplayCompleted );
		EventBus.Unsubscribe<SectionCompletedEvent>( OnSectionCompleted );
		EventBus.Unsubscribe<TreasureCollectedEvent>( OnTreasureCollected );
		EventBus.Unsubscribe<PouchChangedEvent>( OnPouchChanged );
		_subscribed = false;
	}

	void OnVolumeEntered( QuestVolumeEnteredEvent evt )
	{
		if ( !string.IsNullOrEmpty( evt.VolumeId ) )
			_enteredVolumes.Add( evt.VolumeId );
		ApplySimpleMatch( QuestConditionType.EnterVolume, evt.VolumeId, null, evt.Volume );
	}

	void OnPileEmptied( TreasurePileEmptiedEvent evt )
	{
		ApplySimpleMatch( QuestConditionType.ClearPile, QuestTargetRegistry.ResolveTargetId( evt.Pile ), null, evt.Pile );
		PollAllLive();
		EvaluateLiveCompletions( playCompleteDialogue: true );
	}

	void OnPlacementCompleted( PlacementCompletedEvent evt )
	{
		Component targetComponent = evt.Target as Component;
		ApplyPlaceProgress( QuestTargetRegistry.ResolveTargetId( targetComponent ), evt.Definition, targetComponent );
		PollAllLive();
		EvaluateLiveCompletions( playCompleteDialogue: true );
	}

	void OnCoinSorterUsed( CoinSorterUsedEvent evt )
	{
		ApplySimpleMatch( QuestConditionType.UseCoinSorter, QuestTargetRegistry.ResolveTargetId( evt.Station ), evt.Coin, evt.Station );
	}

	void OnConstellationCompleted( GemConstellationCompletedEvent evt )
	{
		ApplySimpleMatch( QuestConditionType.CompleteConstellation, QuestTargetRegistry.ResolveTargetId( evt.Constellation ), null, evt.Constellation );
		PollAllLive();
		EvaluateLiveCompletions( playCompleteDialogue: true );
	}

	void OnArtifactTableCompleted( ArtifactPresentationTableCompletedEvent evt )
	{
		ApplySimpleMatch( QuestConditionType.CompleteArtifactTable, QuestTargetRegistry.ResolveTargetId( evt.Table ), null, evt.Table );
		PollAllLive();
		EvaluateLiveCompletions( playCompleteDialogue: true );
	}

	void OnCoinDisplayCompleted( CoinDisplayTableCompletedEvent evt )
	{
		ApplySimpleMatch( QuestConditionType.CompleteCoinDisplay, QuestTargetRegistry.ResolveTargetId( evt.Table ), evt.AcceptedCoin, evt.Table );
		PollAllLive();
		EvaluateLiveCompletions( playCompleteDialogue: true );
	}

	void OnGoldBarDisplayCompleted( GoldBarDisplayTableCompletedEvent evt )
	{
		ApplySimpleMatch( QuestConditionType.CompleteGoldBarDisplay, QuestTargetRegistry.ResolveTargetId( evt.Table ), evt.AcceptedBar, evt.Table );
		PollAllLive();
		EvaluateLiveCompletions( playCompleteDialogue: true );
	}

	void OnSectionCompleted( SectionCompletedEvent evt )
	{
		ApplySection( evt.SectionId );
	}

	void OnTreasureCollected( TreasureCollectedEvent evt )
	{
		ApplyPickup( evt.Treasure );
	}

	void OnPouchChanged( PouchChangedEvent evt )
	{
		if ( evt.Carry == null )
			return;

		ApplyPickupFromCarry( evt.Carry );
	}

	void ApplyPickupFromCarry( PlayerCarry carry )
	{
		if ( carry == null )
			return;

		for ( int i = 0; i < _liveObjectives.Count; i++ )
			ApplyPickupToConditions( _liveObjectives[ i ].Objective != null ? _liveObjectives[ i ].Objective.conditions : null, _liveObjectives[ i ].ConditionMet, carry );

		for ( int i = 0; i < _liveEvents.Count; i++ )
			ApplyPickupToConditions( _liveEvents[ i ].Event != null ? _liveEvents[ i ].Event.conditions : null, _liveEvents[ i ].ConditionMet, carry );

		EvaluateLiveCompletions( playCompleteDialogue: true );
	}

	void ApplyPickupToConditions( QuestCondition[] conditions, bool[] met, PlayerCarry carry )
	{
		if ( conditions == null || met == null )
			return;

		for ( int i = 0; i < conditions.Length; i++ )
		{
			QuestCondition condition = conditions[ i ];
			if ( condition == null || condition.type != QuestConditionType.PickupTreasure )
				continue;
			if ( i < met.Length && met[ i ] )
				continue;

			if ( carry.BucketHasMatching( CarryBucketKind.Artifact, def => MatchesTreasure( condition, def ) ) ||
			     carry.BucketHasMatching( CarryBucketKind.Gem, def => MatchesTreasure( condition, def ) ) ||
			     carry.BucketHasMatching( CarryBucketKind.Coin, def => MatchesTreasure( condition, def ) ) )
				MarkLiveCondition( met, i );
		}
	}

	void ApplyPickup( TreasureDefinition treasure )
	{
		ApplySimpleMatch( QuestConditionType.PickupTreasure, null, treasure, null );
	}

	void ApplySection( string sectionId )
	{
		ApplyToAllConditions( ( type, condition, met, progress, index ) =>
		{
			if ( type != QuestConditionType.SectionSorted )
				return;
			if ( !string.IsNullOrEmpty( condition.sectionId ) && condition.sectionId != sectionId )
				return;
			MarkLiveCondition( met, index );
		} );
		EvaluateLiveCompletions( playCompleteDialogue: true );
	}

	void ApplyPlaceProgress( string targetId, TreasureDefinition treasure, Component targetComponent )
	{
		ApplyToAllConditions( ( type, condition, met, progress, index ) =>
		{
			if ( type != QuestConditionType.PlaceOnOwner )
				return;
			if ( !MatchesTarget( condition, targetId, targetComponent ) )
				return;
			if ( !MatchesTreasure( condition, treasure ) )
				return;

			if ( progress != null && index < progress.Length )
				progress[ index ] = progress[ index ] + 1;

			int required = Mathf.Max( 1, condition.requiredCount );
			int current = progress != null && index < progress.Length ? progress[ index ] : required;
			if ( current >= required )
				MarkLiveCondition( met, index );
		} );
	}

	void ApplySimpleMatch( QuestConditionType type, string targetId, TreasureDefinition treasure, Component source )
	{
		ApplyToAllConditions( ( liveType, condition, met, progress, index ) =>
		{
			if ( liveType != type )
				return;
			if ( !MatchesTarget( condition, targetId, source ) )
				return;
			if ( treasure != null && !MatchesTreasure( condition, treasure ) )
				return;

			if ( IsAreaWideCompleteType( type ) && string.IsNullOrEmpty( condition.targetId ) && !string.IsNullOrEmpty( condition.areaId ) )
				return;

			MarkLiveCondition( met, index );
		} );

		if ( IsAreaWideCompleteType( type ) )
			PollAllLive();

		EvaluateLiveCompletions( playCompleteDialogue: true );
	}

	static bool IsAreaWideCompleteType( QuestConditionType type )
	{
		return type == QuestConditionType.CompleteCoinDisplay ||
		       type == QuestConditionType.CompleteGoldBarDisplay ||
		       type == QuestConditionType.CompleteArtifactTable ||
		       type == QuestConditionType.CompleteConstellation ||
		       type == QuestConditionType.ClearPile;
	}

	delegate void ConditionVisitor( QuestConditionType type, QuestCondition condition, bool[] met, int[] progress, int index );

	void ApplyToAllConditions( ConditionVisitor visitor )
	{
		if ( visitor == null )
			return;

		for ( int i = 0; i < _liveObjectives.Count; i++ )
		{
			LiveObjective live = _liveObjectives[ i ];
			if ( live == null || live.Complete || live.Objective == null || live.Objective.conditions == null )
				continue;
			VisitConditions( live.Objective.conditions, live.ConditionMet, live.ConditionProgress, visitor );
		}

		for ( int i = 0; i < _liveEvents.Count; i++ )
		{
			LiveEvent live = _liveEvents[ i ];
			if ( live == null || live.Fired || live.Event == null || live.Event.conditions == null )
				continue;
			VisitConditions( live.Event.conditions, live.ConditionMet, live.ConditionProgress, visitor );
		}
	}

	static void VisitConditions( QuestCondition[] conditions, bool[] met, int[] progress, ConditionVisitor visitor )
	{
		for ( int i = 0; i < conditions.Length; i++ )
		{
			QuestCondition condition = conditions[ i ];
			if ( condition == null )
				continue;
			if ( met != null && i < met.Length && met[ i ] )
				continue;
			visitor( condition.type, condition, met, progress, i );
		}
	}

	static void MarkLiveCondition( bool[] met, int index )
	{
		if ( met == null || index < 0 || index >= met.Length )
			return;
		met[ index ] = true;
	}

	static bool MatchesTarget( QuestCondition condition, string targetId, Component source )
	{
		if ( condition == null )
			return false;

		if ( !string.IsNullOrEmpty( condition.targetId ) )
			return condition.targetId == targetId;

		if ( !string.IsNullOrEmpty( condition.areaId ) )
		{
			string area = source != null ? QuestTargetRegistry.ResolveAreaId( source ) : QuestTargetRegistry.GetAreaIdByTargetId( targetId );
			if ( area == condition.areaId )
				return true;
			if ( condition.areaId == QuestSceneAutoWire.AreaStarting && source != null )
			{
				GameObject startingArea = GameObject.Find( "StartingArea" );
				if ( startingArea != null && source.transform.IsChildOf( startingArea.transform ) )
					return true;
			}
			return false;
		}

		return true;
	}

	static bool MatchesTreasure( QuestCondition condition, TreasureDefinition treasure )
	{
		if ( condition == null )
			return true;
		if ( treasure == null )
			return condition.requiredTreasure == null && !condition.filterByCategory && !condition.excludeGoldBars;

		if ( condition.requiredTreasure != null && condition.requiredTreasure != treasure )
			return false;
		if ( condition.filterByCategory && treasure.category != condition.requiredCategory )
			return false;
		if ( condition.excludeGoldBars && GoldBarStack.IsStackable( treasure ) )
			return false;
		return true;
	}

	string ResolveFocusMarkerId()
	{
		QuestObjective sequential = GetActiveSequentialObjective();
		if ( sequential == null )
			return null;

		if ( sequential.subObjectives != null )
		{
			for ( int i = 0; i < sequential.subObjectives.Length; i++ )
			{
				QuestObjective child = sequential.subObjectives[ i ];
				if ( child == null || child.optional )
					continue;
				LiveObjective live = FindLive( _activeQuest, child );
				if ( live != null && live.Complete )
					continue;
				if ( !string.IsNullOrEmpty( child.markerTargetId ) )
					return child.markerTargetId;
			}
		}

		return sequential.markerTargetId;
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
		BuildHudRows();
		QuestObjective sequential = GetActiveSequentialObjective();
		EventBus.Publish( new QuestHudChangedEvent
		{
			QuestId = _activeQuest != null ? _activeQuest.id : null,
			QuestTitle = _activeQuest != null ? _activeQuest.ResolveTitle() : null,
			StepId = sequential != null ? sequential.id : null,
			ObjectiveText = FormatHudObjectiveText(),
			Rows = _hudRows.ToArray(),
			HasMarker = _hasMarker && !_catalogComplete,
			MarkerWorldPosition = _markerWorldPosition,
			CatalogComplete = _catalogComplete
		} );
	}

	void BuildHudRows()
	{
		_hudRows.Clear();
		if ( _catalogComplete || _activeQuest == null )
			return;

		QuestObjective sequential = GetActiveSequentialObjective();
		if ( sequential == null )
			return;

		AddHudRow( sequential, _activeQuest, 0 );

		if ( sequential.subObjectives == null )
			return;

		for ( int i = 0; i < sequential.subObjectives.Length; i++ )
		{
			QuestObjective child = sequential.subObjectives[ i ];
			if ( child == null )
				continue;

			AddHudRow( child, _activeQuest, 1 );
			AppendLinkedOptionalRows( child.id, 2 );
		}
	}

	void AddHudRow( QuestObjective objective, QuestDefinition owner, int indent )
	{
		if ( objective == null )
			return;

		LiveObjective live = FindLive( owner, objective );
		_hudRows.Add( new QuestHudRow
		{
			Text = objective.objectiveText,
			Indent = indent,
			Optional = objective.optional,
			Complete = live != null && live.Complete
		} );
	}

	void AppendLinkedOptionalRows( string parentSubObjectiveId, int indent )
	{
		for ( int i = 0; i < _revealedSubquests.Count; i++ )
		{
			QuestDefinition sub = _revealedSubquests[ i ];
			if ( sub == null || sub.linkedParentObjectiveId != parentSubObjectiveId || sub.objectives == null )
				continue;

			for ( int o = 0; o < sub.objectives.Length; o++ )
			{
				QuestObjective extra = sub.objectives[ o ];
				if ( extra == null || !extra.optional )
					continue;
				AddHudRow( extra, sub, indent );
			}
		}
	}

	string FormatHudObjectiveText()
	{
		if ( _hudRows.Count == 0 )
			return string.Empty;

		System.Text.StringBuilder sb = new System.Text.StringBuilder();
		for ( int i = 0; i < _hudRows.Count; i++ )
		{
			QuestHudRow row = _hudRows[ i ];
			if ( i > 0 )
				sb.Append( '\n' );
			for ( int n = 0; n < row.Indent; n++ )
				sb.Append( "  " );
			sb.Append( row.Complete ? "✓ " : "• " );
			sb.Append( row.Text ?? string.Empty );
			if ( row.Optional )
				sb.Append( " (optional)" );
		}

		return sb.ToString();
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
			_dialogue.Skip();
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

	static string MakeProgressKey( string questId, string itemId )
	{
		if ( string.IsNullOrEmpty( questId ) || string.IsNullOrEmpty( itemId ) )
			return null;
		return questId + "/" + itemId;
	}

	bool IsQuestSavedComplete( string questId )
	{
		if ( string.IsNullOrEmpty( questId ) )
			return false;
		ProfileSaveData save = GetSave();
		if ( save == null )
			return false;
		save.EnsureQuestProgress();
		return save.completedQuestIds != null && save.completedQuestIds.Contains( questId );
	}

	bool IsObjectiveSavedComplete( string questId, string objectiveId )
	{
		string key = MakeProgressKey( questId, objectiveId );
		if ( string.IsNullOrEmpty( key ) )
			return false;
		ProfileSaveData save = GetSave();
		if ( save == null )
			return false;
		save.EnsureQuestProgress();
		return save.completedObjectiveIds != null && save.completedObjectiveIds.Contains( key );
	}

	bool IsEventSavedFired( string questId, string eventId )
	{
		string key = MakeProgressKey( questId, eventId );
		if ( string.IsNullOrEmpty( key ) )
			return false;
		ProfileSaveData save = GetSave();
		if ( save == null )
			return false;
		save.EnsureQuestProgress();
		return save.firedEventIds != null && save.firedEventIds.Contains( key );
	}

	void AddCompletedQuestId( string questId )
	{
		if ( string.IsNullOrEmpty( questId ) )
			return;
		ProfileSaveData save = GetSave();
		if ( save == null )
			return;
		save.EnsureQuestProgress();
		if ( save.completedQuestIds.Contains( questId ) )
			return;
		save.completedQuestIds.Add( questId );
		Persist( save );
	}

	void MarkObjectiveSavedComplete( string questId, string objectiveId )
	{
		string key = MakeProgressKey( questId, objectiveId );
		if ( string.IsNullOrEmpty( key ) )
			return;
		ProfileSaveData save = GetSave();
		if ( save == null )
			return;
		save.EnsureQuestProgress();
		if ( save.completedObjectiveIds.Contains( key ) )
			return;
		save.completedObjectiveIds.Add( key );
		Persist( save );
	}

	void MarkEventSavedFired( string questId, string eventId )
	{
		string key = MakeProgressKey( questId, eventId );
		if ( string.IsNullOrEmpty( key ) )
			return;
		ProfileSaveData save = GetSave();
		if ( save == null )
			return;
		save.EnsureQuestProgress();
		if ( save.firedEventIds.Contains( key ) )
			return;
		save.firedEventIds.Add( key );
		Persist( save );
	}
}
