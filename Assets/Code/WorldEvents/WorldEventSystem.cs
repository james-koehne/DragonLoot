using System.Collections.Generic;

using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.InputSystem;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Runs one-shot world events from a flat catalog: dialogue, Addressables spawn, tutorial HUD hooks.
/// </summary>
public class WorldEventSystem : MonoBehaviour
{
	const float GameStartedDelaySeconds = 2f;
	const float PlayerMoveInputSqrThreshold = 0.01f;
	const float PlayerLookInputSqrThreshold = 0.0001f;

	static WorldEventSystem _instance;

	enum ActionSequencePhase
	{
		Ready = 0,
		WaitingDelay = 1,
		WaitingCallback = 2,
		WaitingDuration = 3
	}

	sealed class RunningActionSequence
	{
		public string EventId;
		public WorldEventAction[] Actions;
		public int Index;
		public ActionSequencePhase Phase;
		public float BlockUntilUnscaled;
	}

	readonly DragonDialoguePlayer _dialogue = new DragonDialoguePlayer();
	readonly HashSet<string> _enteredVolumes = new HashSet<string>();
	readonly HashSet<string> _firedThisSession = new HashSet<string>();
	readonly List<WorldEventDefinition> _pendingFire = new List<WorldEventDefinition>();
	readonly List<RunningActionSequence> _runningSequences = new List<RunningActionSequence>();

	WorldEventCatalogDefinition _catalog;
	bool _subscribed;
	bool _playerHasMadeGameplayInput;
	float _catalogStartUnscaledTime;

	public static WorldEventSystem Instance => _instance;

	public WorldEventCatalogDefinition Catalog => _catalog;

	public bool PlayerHasMadeGameplayInput => _playerHasMadeGameplayInput;

	public bool GameStarted => _playerHasMadeGameplayInput &&
	                           Time.unscaledTime >= _catalogStartUnscaledTime + GameStartedDelaySeconds;

	public static WorldEventSystem EnsureExists()
	{
		if ( _instance != null )
			return _instance;

		GameObject go = new GameObject( "WorldEventSystem" );
		_instance = go.AddComponent<WorldEventSystem>();
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
		StopActionSequences();
		Unsubscribe();
	}

	void Update()
	{
		_dialogue.Tick();
		TrySkipDialogueInput();
		TickActionSequences();
		TickConditionTimers();
	}

	public void StartCatalog()
	{
		StopActionSequences();
		LoadCatalog();
		EventSceneAutoWire.EnsureWired();
		_enteredVolumes.Clear();
		_firedThisSession.Clear();
		_playerHasMadeGameplayInput = false;
		_catalogStartUnscaledTime = Time.unscaledTime;
		EvaluateAll();
	}

	void LoadCatalog()
	{
		_catalog = GameInstance.GetDefinition<WorldEventCatalogDefinition>();
		if ( _catalog == null || _catalog.Count <= 0 )
			_catalog = WorldEventCatalogFallback.GetOrCreate();

		if ( _catalog == null || _catalog.Count <= 0 )
			Debug.LogWarning( "WorldEventSystem: no WorldEventCatalogDefinition found." );
	}

	void TickConditionTimers()
	{
		if ( !_playerHasMadeGameplayInput && HasPlayerMadeGameplayInput() )
			_playerHasMadeGameplayInput = true;

		EvaluateAll();
	}

	void Subscribe()
	{
		if ( _subscribed )
			return;
		EventBus.Subscribe<VolumeEnteredEvent>( OnVolumeEntered );
		EventBus.Subscribe<TreasureCollectedEvent>( OnTreasureCollected );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;
		EventBus.Unsubscribe<VolumeEnteredEvent>( OnVolumeEntered );
		EventBus.Unsubscribe<TreasureCollectedEvent>( OnTreasureCollected );
		_subscribed = false;
	}

	void OnVolumeEntered( VolumeEnteredEvent evt )
	{
		if ( !string.IsNullOrEmpty( evt.VolumeId ) )
			_enteredVolumes.Add( evt.VolumeId );
		EvaluateAll();
	}

	void OnTreasureCollected( TreasureCollectedEvent evt )
	{
		EvaluateAll( evt.Treasure );
	}

	void EvaluateAll( TreasureDefinition pickupContext = null )
	{
		if ( _catalog == null || _catalog.events == null )
			return;

		_pendingFire.Clear();
		for ( int i = 0; i < _catalog.events.Count; i++ )
		{
			WorldEventDefinition definition = _catalog.events[ i ];
			if ( definition == null || string.IsNullOrEmpty( definition.id ) )
				continue;
			if ( HasFired( definition.id ) )
				continue;
			if ( !AreConditionsMet( definition, pickupContext ) )
				continue;
			_pendingFire.Add( definition );
		}

		for ( int i = 0; i < _pendingFire.Count; i++ )
			FireEvent( _pendingFire[ i ] );
	}

	bool AreConditionsMet( WorldEventDefinition definition, TreasureDefinition pickupContext )
	{
		WorldEventCondition[] conditions = definition.conditions;
		if ( conditions == null || conditions.Length == 0 )
			return true;

		for ( int i = 0; i < conditions.Length; i++ )
		{
			if ( !IsConditionMet( conditions[ i ], pickupContext ) )
				return false;
		}

		return true;
	}

	bool IsConditionMet( WorldEventCondition condition, TreasureDefinition pickupContext )
	{
		if ( condition == null )
			return true;

		switch ( condition.type )
		{
			case WorldEventConditionType.GameStarted:
				return GameStarted;
			case WorldEventConditionType.EnterVolume:
				return !string.IsNullOrEmpty( condition.targetId ) && _enteredVolumes.Contains( condition.targetId );
			case WorldEventConditionType.PickupTreasure:
				return MatchesPickup( condition, pickupContext );
			case WorldEventConditionType.PlayerGameplayInput:
				return _playerHasMadeGameplayInput;
			case WorldEventConditionType.ElapsedUnscaledSeconds:
				return Time.unscaledTime >= _catalogStartUnscaledTime + Mathf.Max( 0f, condition.delaySeconds );
			default:
				return false;
		}
	}

	static bool MatchesPickup( WorldEventCondition condition, TreasureDefinition treasure )
	{
		if ( treasure == null )
			return false;

		if ( condition.requireGoldBars && !GoldBarStack.IsStackable( treasure ) )
			return false;
		if ( condition.excludeGoldBars && GoldBarStack.IsStackable( treasure ) )
			return false;
		if ( condition.requiredTreasure != null && condition.requiredTreasure != treasure )
			return false;
		if ( condition.filterByCategory && treasure.category != condition.requiredCategory )
			return false;

		return true;
	}

	void FireEvent( WorldEventDefinition definition )
	{
		if ( definition == null || string.IsNullOrEmpty( definition.id ) )
			return;
		if ( HasFired( definition.id ) )
			return;

		MarkFired( definition.id );
		RunActions( definition );

		EventBus.Publish( new WorldEventFiredEvent
		{
			Id = definition.id,
			Tags = definition.tags
		} );
	}

	void RunActions( WorldEventDefinition definition )
	{
		WorldEventAction[] actions = definition.actions;
		if ( actions == null || actions.Length == 0 )
			return;

		StopSequencesFor( definition.id );

		RunningActionSequence sequence = new RunningActionSequence
		{
			EventId = definition.id,
			Actions = actions,
			Index = 0,
			Phase = ActionSequencePhase.Ready
		};
		_runningSequences.Add( sequence );
		TickSequence( sequence );
	}

	void StopActionSequences()
	{
		_runningSequences.Clear();
	}

	void StopSequencesFor( string eventId )
	{
		if ( string.IsNullOrEmpty( eventId ) )
			return;

		for ( int i = _runningSequences.Count - 1; i >= 0; i-- )
		{
			RunningActionSequence sequence = _runningSequences[ i ];
			if ( sequence != null && sequence.EventId == eventId )
				_runningSequences.RemoveAt( i );
		}
	}

	void TickActionSequences()
	{
		for ( int i = _runningSequences.Count - 1; i >= 0; i-- )
		{
			RunningActionSequence sequence = _runningSequences[ i ];
			if ( sequence == null )
			{
				_runningSequences.RemoveAt( i );
				continue;
			}

			TickSequence( sequence );
		}
	}

	void TickSequence( RunningActionSequence sequence )
	{
		if ( sequence == null || sequence.Actions == null )
		{
			_runningSequences.Remove( sequence );
			return;
		}

		const int maxSteps = 32;
		int steps = 0;
		while ( steps < maxSteps )
		{
			steps++;
			if ( !_runningSequences.Contains( sequence ) )
				return;

			if ( sequence.Index >= sequence.Actions.Length )
			{
				_runningSequences.Remove( sequence );
				return;
			}

			WorldEventAction action = sequence.Actions[ sequence.Index ];
			if ( action == null )
			{
				sequence.Index++;
				sequence.Phase = ActionSequencePhase.Ready;
				continue;
			}

			if ( sequence.Phase == ActionSequencePhase.Ready )
			{
				float delay = Mathf.Max( 0f, action.delayBefore );
				sequence.Phase = ActionSequencePhase.WaitingDelay;
				sequence.BlockUntilUnscaled = Time.unscaledTime + delay;
			}

			if ( sequence.Phase == ActionSequencePhase.WaitingDelay )
			{
				if ( Time.unscaledTime < sequence.BlockUntilUnscaled )
					return;

				ExecuteAction( action, sequence );
				if ( sequence.Phase == ActionSequencePhase.WaitingCallback ||
				     sequence.Phase == ActionSequencePhase.WaitingDuration )
					return;

				sequence.Index++;
				sequence.Phase = ActionSequencePhase.Ready;
				continue;
			}

			if ( sequence.Phase == ActionSequencePhase.WaitingCallback )
				return;

			if ( sequence.Phase == ActionSequencePhase.WaitingDuration )
			{
				if ( Time.unscaledTime < sequence.BlockUntilUnscaled )
					return;

				sequence.Index++;
				sequence.Phase = ActionSequencePhase.Ready;
				continue;
			}

			return;
		}
	}

	void CompleteCallbackWait( RunningActionSequence sequence )
	{
		if ( sequence == null || sequence.Phase != ActionSequencePhase.WaitingCallback )
			return;
		if ( !_runningSequences.Contains( sequence ) )
			return;

		sequence.Index++;
		sequence.Phase = ActionSequencePhase.Ready;
		TickSequence( sequence );
	}

	void ExecuteAction( WorldEventAction action, RunningActionSequence sequence )
	{
		switch ( action.type )
		{
			case WorldEventActionType.Dialogue:
				StartDialogueAction( action, sequence );
				break;
			case WorldEventActionType.SpawnAddressable:
				SpawnAddressable( action );
				break;
			case WorldEventActionType.SetTutorialHud:
				ApplyTutorialHud( action );
				break;
			case WorldEventActionType.PlayAudio:
				PlayAudio( action );
				BeginDurationWait( sequence, action, ResolveAudioWaitDuration( action ) );
				break;
			case WorldEventActionType.LanternRevealSweep:
				StartLanternRevealSweep( action );
				BeginDurationWait( sequence, action, ResolveLanternWaitDuration( action ) );
				break;
			case WorldEventActionType.CinematicPresentation:
				StartCinematicPresentation( action );
				BeginDurationWait( sequence, action, ResolveCinematicWaitDuration( action ) );
				break;
			case WorldEventActionType.BrakePlayerMovement:
				BrakePlayerMovement( action );
				BeginDurationWait( sequence, action, Mathf.Max( 0f, action.playerBrakeDuration ) );
				break;
		}
	}

	void StartDialogueAction( WorldEventAction action, RunningActionSequence sequence )
	{
		if ( !action.waitUntilFinished )
		{
			_dialogue.Enqueue( action.dialogue );
			return;
		}

		bool completedSync = false;
		_dialogue.Enqueue( action.dialogue, () =>
		{
			completedSync = true;
			CompleteCallbackWait( sequence );
		} );

		if ( !completedSync )
			sequence.Phase = ActionSequencePhase.WaitingCallback;
	}

	static void BeginDurationWait( RunningActionSequence sequence, WorldEventAction action, float duration )
	{
		if ( sequence == null || action == null || !action.waitUntilFinished )
			return;
		if ( duration <= 0f )
			return;

		sequence.Phase = ActionSequencePhase.WaitingDuration;
		sequence.BlockUntilUnscaled = Time.unscaledTime + duration;
	}

	static float ResolveAudioWaitDuration( WorldEventAction action )
	{
		if ( action == null || action.audioClip == null )
			return 0f;

		float pitch = Mathf.Min( Mathf.Abs( action.audioPitchMin ), Mathf.Abs( action.audioPitchMax ) );
		if ( pitch < 0.01f )
			pitch = 0.01f;
		return action.audioClip.length / pitch;
	}

	static float ResolveLanternWaitDuration( WorldEventAction action )
	{
		if ( action == null )
			return 0f;
		return Mathf.Max( 0f, action.lanternStartDelay ) +
		       Mathf.Max( 0f, action.lanternSweepDuration ) +
		       Mathf.Max( 0f, action.lanternFadeDuration );
	}

	static float ResolveCinematicWaitDuration( WorldEventAction action )
	{
		if ( action == null )
			return 0f;
		return Mathf.Max( 0f, action.cinematicRise ) +
		       Mathf.Max( 0f, action.cinematicHold ) +
		       Mathf.Max( 0f, action.cinematicFall );
	}

	static void BrakePlayerMovement( WorldEventAction action )
	{
		if ( action == null )
			return;

		if ( GameMode.Instance == null || GameMode.Instance.Player == null )
			return;

		GameMode.Instance.Player.BrakePlanarVelocityToZero( action.playerBrakeDuration );
	}

	static void PlayAudio( WorldEventAction action )
	{
		if ( action == null || action.audioClip == null )
			return;

		WorldEventAudioPlayer.Play( action, ResolveAudioPosition( action ) );
	}

	static void StartLanternRevealSweep( WorldEventAction action )
	{
		if ( action == null || string.IsNullOrEmpty( action.lanternRevealId ) )
			return;

		LanternRevealSweepOverrides overrides = new LanternRevealSweepOverrides
		{
			SkylightFadeDuration = action.lanternSkylightFadeDuration,
			SweepDuration = action.lanternSweepDuration,
			LanternFadeDuration = action.lanternFadeDuration,
			LanternStartDelay = action.lanternStartDelay
		};
		LanternRevealSweepController.TryStartReveal( action.lanternRevealId, overrides );
	}

	static void StartCinematicPresentation( WorldEventAction action )
	{
		if ( action == null || string.IsNullOrEmpty( action.cinematicPresentationId ) )
			return;

		CinematicPresentationOverrides overrides = new CinematicPresentationOverrides
		{
			FovPeak = action.cinematicFovPeak,
			LetterboxPeak = action.cinematicLetterboxPeak,
			Rise = action.cinematicRise,
			Hold = action.cinematicHold,
			Fall = action.cinematicFall
		};
		CinematicPresentationController.TryPlay( action.cinematicPresentationId, overrides );

		if ( action.cinematicPlayerMovementLockDuration > 0f
		     && GameMode.Instance != null
		     && GameMode.Instance.Player != null )
			GameMode.Instance.Player.LockPlanarMovement( action.cinematicPlayerMovementLockDuration );
	}

	static Vector3 ResolveAudioPosition( WorldEventAction action )
	{
		if ( action.audioAtPlayer )
		{
			if ( GameMode.Instance != null && GameMode.Instance.Player != null )
				return GameMode.Instance.Player.transform.position;
		}

		if ( action.audioUseWorldPosition )
			return action.spawnWorldPosition;

		if ( !string.IsNullOrEmpty( action.spawnPointId ) &&
		     EventTargetRegistry.TryGetSpawnPoint( action.spawnPointId, out EventSpawnPoint spawnPoint ) &&
		     spawnPoint != null &&
		     spawnPoint.SpawnTransform != null )
			return spawnPoint.SpawnTransform.position;

		return Vector3.zero;
	}

	static void ApplyTutorialHud( WorldEventAction action )
	{
		if ( action == null )
			return;

		TutorialHud.ShowSimple( action.tutorialTitle, action.tutorialObjectiveText, action.markerTargetId );
	}

	static void SpawnAddressable( WorldEventAction action )
	{
		if ( action == null || string.IsNullOrEmpty( action.addressableKey ) )
			return;

		Vector3 position = action.spawnWorldPosition;
		Quaternion rotation = Quaternion.identity;
		Transform parent = null;

		if ( !action.useWorldPosition &&
		     !string.IsNullOrEmpty( action.spawnPointId ) &&
		     EventTargetRegistry.TryGetSpawnPoint( action.spawnPointId, out EventSpawnPoint spawnPoint ) &&
		     spawnPoint != null &&
		     spawnPoint.SpawnTransform != null )
		{
			position = spawnPoint.SpawnTransform.position;
			rotation = spawnPoint.SpawnTransform.rotation;
			parent = spawnPoint.SpawnTransform;
		}

		string key = action.addressableKey;
		AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync( key, position, rotation, parent );
		handle.Completed += op =>
		{
			if ( op.Status != AsyncOperationStatus.Succeeded || op.Result == null )
				Debug.LogWarning( "WorldEventSystem: failed to spawn Addressable '" + key + "'." );
		};
	}

	bool HasFired( string eventId )
	{
		if ( string.IsNullOrEmpty( eventId ) )
			return true;
		if ( _firedThisSession.Contains( eventId ) )
			return true;

		ProfileSaveData save = GetSave();
		if ( save == null )
			return false;

		save.EnsureWorldEventProgress();
		return save.firedWorldEventIds != null && save.firedWorldEventIds.Contains( eventId );
	}

	void MarkFired( string eventId )
	{
		_firedThisSession.Add( eventId );

		ProfileSaveData save = GetSave();
		if ( save == null )
			return;

		save.EnsureWorldEventProgress();
		if ( !save.firedWorldEventIds.Contains( eventId ) )
			save.firedWorldEventIds.Add( eventId );

		if ( ProfileManager.Instance != null )
			ProfileManager.Instance.SaveCurrentStatsToProfile();
	}

	static ProfileSaveData GetSave()
	{
		if ( ProfileManager.Instance == null )
			return null;
		return ProfileManager.Instance.ProfileSaveData;
	}

	public void DebugResetFiredEvents()
	{
		StopActionSequences();
		_firedThisSession.Clear();
		_enteredVolumes.Clear();
		_playerHasMadeGameplayInput = false;
		_catalogStartUnscaledTime = Time.unscaledTime;
		_dialogue.Stop();
		TutorialHud.Clear();
		LanternRevealSweepController.DebugResetAll();

		ProfileSaveData save = GetSave();
		if ( save != null )
		{
			save.EnsureWorldEventProgress();
			save.firedWorldEventIds.Clear();
			if ( ProfileManager.Instance != null )
				ProfileManager.Instance.SaveCurrentStatsToProfile();
		}

		EvaluateAll();
	}

	public void DebugFireEvent( string eventId )
	{
		if ( _catalog == null || !_catalog.TryGetById( eventId, out WorldEventDefinition definition ) || definition == null )
		{
			Debug.LogWarning( "WorldEventSystem: unknown event id '" + eventId + "'." );
			return;
		}

		if ( HasFired( definition.id ) )
			_firedThisSession.Remove( definition.id );

		ProfileSaveData save = GetSave();
		if ( save != null )
		{
			save.EnsureWorldEventProgress();
			save.firedWorldEventIds.Remove( definition.id );
		}

		FireEvent( definition );
	}

	void TrySkipDialogueInput()
	{
		if ( !_dialogue.IsPlaying )
			return;

		InputController inputController = InputController.Instance;
		if ( inputController == null || !inputController.InputEnabled )
			return;

		GameInput input = inputController.GameInput;
		if ( input == null )
			return;

		if ( input.Interact.WasPressedThisFrame() || input.SecondaryInteract.WasPressedThisFrame() )
			_dialogue.Skip();
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
		return action != null && action.WasPressedThisFrame();
	}
}
