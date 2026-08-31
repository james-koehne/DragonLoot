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

	readonly DragonDialoguePlayer _dialogue = new DragonDialoguePlayer();
	readonly HashSet<string> _enteredVolumes = new HashSet<string>();
	readonly HashSet<string> _firedThisSession = new HashSet<string>();
	readonly List<WorldEventDefinition> _pendingFire = new List<WorldEventDefinition>();

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
		Unsubscribe();
	}

	void Update()
	{
		_dialogue.Tick();
		TrySkipDialogueInput();
		TickConditionTimers();
	}

	public void StartCatalog()
	{
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
		if ( actions == null )
			return;

		for ( int i = 0; i < actions.Length; i++ )
		{
			WorldEventAction action = actions[ i ];
			if ( action == null )
				continue;

			switch ( action.type )
			{
				case WorldEventActionType.Dialogue:
					_dialogue.Enqueue( action.dialogue );
					break;
				case WorldEventActionType.SpawnAddressable:
					SpawnAddressable( action );
					break;
				case WorldEventActionType.SetTutorialHud:
					ApplyTutorialHud( action );
					break;
				case WorldEventActionType.PlayAudio:
					PlayAudio( action );
					break;
				case WorldEventActionType.LanternRevealSweep:
					StartLanternRevealSweep( action );
					break;
				case WorldEventActionType.CinematicPresentation:
					StartCinematicPresentation( action );
					break;
			}
		}
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
			OverrideMicroPush = action.cinematicOverrideMicroPush,
			EnableMicroPush = action.cinematicEnableMicroPush,
			MicroPushDistance = action.cinematicMicroPushDistance,
			Rise = action.cinematicRise,
			Hold = action.cinematicHold,
			Fall = action.cinematicFall
		};
		CinematicPresentationController.TryPlay( action.cinematicPresentationId, overrides );
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
