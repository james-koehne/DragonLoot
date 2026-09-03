using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Reactive contextual tutorials: EventBus triggers → ordered corner popups.
/// Does not drive TutorialHud.
/// </summary>
public class TutorialManager : MonoBehaviour
{
	const float CooldownSeconds = 0.35f;

	static TutorialManager _instance;

	readonly Queue<PendingTutorial> _pending = new Queue<PendingTutorial>();
	readonly HashSet<string> _startedThisSession = new HashSet<string>();

	TutorialCatalogDefinition _catalog;
	TutorialPopupUI _popup;
	TutorialDefinition _active;
	int _activeStepIndex;
	bool _activeIsReplay;
	bool _subscribed;
	float _cooldownUntil;
	string _lastShownId;

	struct PendingTutorial
	{
		public TutorialDefinition Definition;
		public bool IsReplay;
	}

	public static TutorialManager Instance => _instance;

	public TutorialCatalogDefinition Catalog => _catalog;

	public string LastShownTutorialId => _lastShownId;

	public bool IsSequencePlaying => _active != null;

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
		_pending.Clear();
		_active = null;
		_activeStepIndex = 0;
		_activeIsReplay = false;
		_startedThisSession.Clear();
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
		EventBus.Subscribe<TreasurePileDigEvent>( OnDig );
		EventBus.Subscribe<PlacementCompletedEvent>( OnPlacementCompleted );
		EventBus.Subscribe<CoinStackChangedEvent>( OnCoinStackChanged );
		EventBus.Subscribe<CoinDisplayTableChangedEvent>( OnCoinDisplayChanged );
		EventBus.Subscribe<GemConstellationChangedEvent>( OnConstellationChanged );
		EventBus.Subscribe<ArtifactPresentationTableChangedEvent>( OnArtifactChanged );
		EventBus.Subscribe<TreasureCollectedEvent>( OnTreasureCollected );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;
		EventBus.Unsubscribe<VolumeEnteredEvent>( OnVolumeEntered );
		EventBus.Unsubscribe<TreasurePileDigEvent>( OnDig );
		EventBus.Unsubscribe<PlacementCompletedEvent>( OnPlacementCompleted );
		EventBus.Unsubscribe<CoinStackChangedEvent>( OnCoinStackChanged );
		EventBus.Unsubscribe<CoinDisplayTableChangedEvent>( OnCoinDisplayChanged );
		EventBus.Unsubscribe<GemConstellationChangedEvent>( OnConstellationChanged );
		EventBus.Unsubscribe<ArtifactPresentationTableChangedEvent>( OnArtifactChanged );
		EventBus.Unsubscribe<TreasureCollectedEvent>( OnTreasureCollected );
		_subscribed = false;
	}

	void OnVolumeEntered( VolumeEnteredEvent evt )
	{
		if ( string.IsNullOrEmpty( evt.VolumeId ) )
			return;

		TryQueueByTrigger( TutorialTriggerType.EnterVolume, evt.VolumeId );
		TryQueueFallbackVolume( evt.VolumeId );
	}

	void OnDig( TreasurePileDigEvent evt )
	{
		TryQueueByTrigger( TutorialTriggerType.TreasurePileDig, null );
	}

	void OnPlacementCompleted( PlacementCompletedEvent evt )
	{
		if ( evt.Target is FloorPlacementTarget )
			TryQueueByTrigger( TutorialTriggerType.PlacementCompletedFloor, null );
	}

	void OnCoinStackChanged( CoinStackChangedEvent evt )
	{
		if ( evt.Stack == null || evt.Count < 2 )
			return;
		TryQueueByTrigger( TutorialTriggerType.CoinStackChanged, null );
	}

	void OnCoinDisplayChanged( CoinDisplayTableChangedEvent evt )
	{
		TryQueueByTrigger( TutorialTriggerType.CoinDisplayTableChanged, null );
	}

	void OnConstellationChanged( GemConstellationChangedEvent evt )
	{
		TryQueueByTrigger( TutorialTriggerType.GemConstellationChanged, null );
	}

	void OnArtifactChanged( ArtifactPresentationTableChangedEvent evt )
	{
		TryQueueByTrigger( TutorialTriggerType.ArtifactPresentationTableChanged, null );
	}

	void OnTreasureCollected( TreasureCollectedEvent evt )
	{
		TryQueueByTrigger( TutorialTriggerType.TreasureCollected, null );
	}

	void TryQueueFallbackVolume( string volumeId )
	{
		if ( _catalog == null || _catalog.tutorials == null )
			return;

		for ( int i = 0; i < _catalog.tutorials.Count; i++ )
		{
			TutorialDefinition def = _catalog.tutorials[ i ];
			if ( def == null || string.IsNullOrEmpty( def.fallbackVolumeId ) )
				continue;
			if ( def.fallbackVolumeId != volumeId )
				continue;
			TryQueue( def, isReplay: false );
		}
	}

	void TryQueueByTrigger( TutorialTriggerType trigger, string volumeId )
	{
		if ( _catalog == null || _catalog.tutorials == null )
			return;

		for ( int i = 0; i < _catalog.tutorials.Count; i++ )
		{
			TutorialDefinition def = _catalog.tutorials[ i ];
			if ( def == null || def.trigger != trigger )
				continue;
			if ( trigger == TutorialTriggerType.EnterVolume
			     && !string.IsNullOrEmpty( def.volumeId )
			     && def.volumeId != volumeId )
				continue;
			TryQueue( def, isReplay: false );
		}
	}

	bool TryQueue( TutorialDefinition def, bool isReplay )
	{
		if ( def == null || string.IsNullOrEmpty( def.id ) )
			return false;
		if ( def.steps == null || def.steps.Length == 0 )
			return false;

		if ( !isReplay )
		{
			if ( DebugDefinition.TutorialsDisabled )
				return false;
			if ( IsCompleted( def.id ) )
				return false;
			if ( _startedThisSession.Contains( def.id ) )
				return false;
			if ( !ArePrerequisitesMet( def ) )
				return false;
			if ( Time.unscaledTime < _cooldownUntil )
				return false;
		}

		if ( _active != null && _active.id == def.id )
			return false;

		foreach ( PendingTutorial queued in _pending )
		{
			if ( queued.Definition != null && queued.Definition.id == def.id )
				return false;
		}

		if ( !isReplay )
			_startedThisSession.Add( def.id );

		_pending.Enqueue( new PendingTutorial { Definition = def, IsReplay = isReplay } );
		TryStartNext();
		return true;
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

	void TryStartNext()
	{
		if ( _active != null )
			return;
		if ( _pending.Count == 0 )
			return;
		if ( _popup == null )
			return;

		PendingTutorial next = _pending.Dequeue();
		_active = next.Definition;
		_activeIsReplay = next.IsReplay;
		_activeStepIndex = 0;

		if ( _active == null )
		{
			TryStartNext();
			return;
		}

		MarkDiscovered( _active.id );
		_lastShownId = _active.id;
		ShowActiveStep();
	}

	void ShowActiveStep()
	{
		if ( _active == null || _popup == null )
			return;

		TutorialPopupStep[] steps = _active.steps;
		if ( steps == null || _activeStepIndex < 0 || _activeStepIndex >= steps.Length )
		{
			FinishActive();
			return;
		}

		TutorialPopupStep step = steps[ _activeStepIndex ];
		string body = step != null ? step.body : string.Empty;
		string hint = step != null ? TutorialKeybindFormatter.Format( step.keybindHint ) : string.Empty;
		_popup.Show( _active.title, body, hint, _activeStepIndex + 1, steps.Length, OnPopupAdvanced );
	}

	void OnPopupAdvanced()
	{
		if ( _active == null )
			return;

		_activeStepIndex++;
		if ( _active.steps == null || _activeStepIndex >= _active.steps.Length )
		{
			FinishActive();
			return;
		}

		ShowActiveStep();
	}

	void FinishActive()
	{
		TutorialDefinition finished = _active;
		bool wasReplay = _activeIsReplay;
		_active = null;
		_activeStepIndex = 0;
		_activeIsReplay = false;
		_cooldownUntil = Time.unscaledTime + CooldownSeconds;

		if ( finished != null && !wasReplay )
			MarkCompleted( finished.id );

		if ( _popup != null )
			_popup.Hide();

		TryStartNext();
	}

	public bool Replay( string tutorialId )
	{
		if ( _catalog == null || !_catalog.TryGetById( tutorialId, out TutorialDefinition def ) || def == null )
			return false;

		if ( _active != null )
		{
			_pending.Clear();
			_active = null;
			_activeStepIndex = 0;
			if ( _popup != null )
				_popup.Hide();
		}

		return TryQueue( def, isReplay: true );
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
		ProfileSaveData save = GetSave();
		if ( save == null || string.IsNullOrEmpty( tutorialId ) )
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
		_pending.Clear();
		_active = null;
		_activeStepIndex = 0;
		_activeIsReplay = false;
		_startedThisSession.Clear();
		_lastShownId = null;
		_cooldownUntil = 0f;
		if ( _popup != null )
			_popup.Hide();

		ProfileSaveData save = GetSave();
		if ( save != null )
		{
			save.EnsureTutorialProgress();
			save.discoveredTutorialIds.Clear();
			save.completedTutorialIds.Clear();
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
