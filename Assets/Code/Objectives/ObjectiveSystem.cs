using System.Collections;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Tracks authored objectives: sub completion, rewards, nearby HUD, and world hooks.
/// </summary>
public class ObjectiveSystem : MonoBehaviour
{
	const float NearbyRefreshInterval = 0.25f;
	const int NearbyCap = 3;
	const float DefaultShowRadius = 40f;
	const float LastTaskToCompleteDelay = 1.75f;

	static ObjectiveSystem _instance;

	struct NearbyCandidate
	{
		public ObjectiveDefinition Definition;
		public float Distance;
		public string MarkerTargetId;
		public Vector3 MarkerWorldPosition;
	}

	readonly HashSet<string> _completedObjectives = new HashSet<string>();
	readonly HashSet<string> _completedSubs = new HashSet<string>();
	readonly HashSet<string> _enteredVolumes = new HashSet<string>();
	readonly HashSet<string> _pendingCompleteIds = new HashSet<string>();
	readonly List<NearbyCandidate> _nearbyScratch = new List<NearbyCandidate>( 16 );
	readonly List<Transform> _outlineScratch = new List<Transform>( 8 );
	readonly List<TutorialHudRow> _rowScratch = new List<TutorialHudRow>( 32 );

	ObjectiveCatalogDefinition _catalog;
	bool _subscribed;
	bool _started;
	float _nextNearbyRefreshUnscaled;
	string _lastHudFingerprint;

	public static ObjectiveSystem Instance => _instance;

	public ObjectiveCatalogDefinition Catalog => _catalog;

	public static ObjectiveSystem EnsureExists()
	{
		if ( _instance != null )
			return _instance;

		GameObject go = new GameObject( "ObjectiveSystem" );
		_instance = go.AddComponent<ObjectiveSystem>();
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
		Unsubscribe();
		if ( _instance == this )
			_instance = null;
	}

	void Update()
	{
		if ( !_started )
			return;

		if ( Time.unscaledTime < _nextNearbyRefreshUnscaled )
			return;

		_nextNearbyRefreshUnscaled = Time.unscaledTime + NearbyRefreshInterval;
		TryCompleteCountSubs();
		RefreshNearbyHud( force: false );
	}

	public void StartCatalog()
	{
		LoadCatalog();
		LoadFromProfile();
		_enteredVolumes.Clear();
		SeedEnteredVolumesFromPlayer();
		_started = true;
		_nextNearbyRefreshUnscaled = 0f;
		_lastHudFingerprint = null;
		TryCompleteCountSubs();
		CompleteObjectivesWithAllSubsDone();
		FireCompletedObjectiveWorldEvents();
		RefreshNearbyHud( force: true );
	}

	void SeedEnteredVolumesFromPlayer()
	{
		if ( GameMode.Instance == null || GameMode.Instance.Player == null )
			return;

		Vector3 playerPos = GameMode.Instance.Player.transform.position;
		if ( _catalog == null || _catalog.objectives == null )
			return;

		for ( int i = 0; i < _catalog.objectives.Count; i++ )
		{
			ObjectiveDefinition definition = _catalog.objectives[ i ];
			if ( definition == null || string.IsNullOrEmpty( definition.showVolumeId ) )
				continue;
			if ( _enteredVolumes.Contains( definition.showVolumeId ) )
				continue;
			if ( !EventTargetRegistry.TryGetVolume( definition.showVolumeId, out QuestVolume volume ) || volume == null )
				continue;

			Collider col = volume.GetComponent<Collider>();
			if ( col == null )
				continue;
			if ( col.bounds.Contains( playerPos ) )
				_enteredVolumes.Add( definition.showVolumeId );
		}
	}

	void LoadCatalog()
	{
		_catalog = GameInstance.GetDefinition<ObjectiveCatalogDefinition>();
		if ( _catalog == null )
			Debug.LogWarning( "ObjectiveSystem: no ObjectiveCatalogDefinition found." );
	}

	void LoadFromProfile()
	{
		_completedObjectives.Clear();
		_completedSubs.Clear();

		ProfileSaveData save = GetSave();
		if ( save == null )
			return;

		save.EnsureObjectiveProgress();
		if ( save.completedObjectiveIds != null )
		{
			for ( int i = 0; i < save.completedObjectiveIds.Count; i++ )
			{
				string id = save.completedObjectiveIds[ i ];
				if ( !string.IsNullOrEmpty( id ) )
					_completedObjectives.Add( id );
			}
		}

		if ( save.completedObjectiveSubIds != null )
		{
			for ( int i = 0; i < save.completedObjectiveSubIds.Count; i++ )
			{
				string key = save.completedObjectiveSubIds[ i ];
				if ( !string.IsNullOrEmpty( key ) )
					_completedSubs.Add( key );
			}
		}
	}

	public bool IsCompleted( string objectiveId )
	{
		if ( string.IsNullOrEmpty( objectiveId ) )
			return false;
		return _completedObjectives.Contains( objectiveId );
	}

	public bool IsSubCompleted( string objectiveId, string subId )
	{
		if ( string.IsNullOrEmpty( objectiveId ) || string.IsNullOrEmpty( subId ) )
			return false;
		return _completedSubs.Contains( SubKey( objectiveId, subId ) );
	}

	public bool ArePrerequisitesMet( ObjectiveDefinition definition )
	{
		if ( definition == null )
			return false;

		string[] prereqs = definition.prerequisiteObjectiveIds;
		if ( prereqs == null || prereqs.Length == 0 )
			return true;

		for ( int i = 0; i < prereqs.Length; i++ )
		{
			string prereq = prereqs[ i ];
			if ( string.IsNullOrEmpty( prereq ) )
				continue;
			if ( !IsCompleted( prereq ) )
				return false;
		}

		return true;
	}

	public void DebugCompleteSub( string objectiveId, string subId )
	{
		if ( _catalog == null || !_catalog.TryGetById( objectiveId, out ObjectiveDefinition definition ) || definition == null )
			return;
		TryCompleteSub( definition, subId );
	}

	public void DebugCompleteObjective( string objectiveId )
	{
		if ( _catalog == null || !_catalog.TryGetById( objectiveId, out ObjectiveDefinition definition ) || definition == null )
			return;

		if ( definition.subs != null )
		{
			for ( int i = 0; i < definition.subs.Length; i++ )
			{
				ObjectiveSubDefinition sub = definition.subs[ i ];
				if ( sub == null || string.IsNullOrEmpty( sub.id ) )
					continue;
				TryCompleteSub( definition, sub.id );
			}
		}

		_pendingCompleteIds.Remove( objectiveId );
		if ( !IsCompleted( objectiveId ) )
			CompleteObjective( definition, ignoreWorldState: true );
	}

	/// <summary>
	/// Marks every catalog objective and sub complete, grants their rewards, and fires completion world events.
	/// Skips the per-island completion toast so a debug skip can surface ability/upgrade rewards instead.
	/// </summary>
	public void DebugCompleteAll()
	{
		if ( _catalog == null || _catalog.objectives == null )
			return;

		for ( int i = 0; i < _catalog.objectives.Count; i++ )
		{
			ObjectiveDefinition definition = _catalog.objectives[ i ];
			if ( definition == null || string.IsNullOrEmpty( definition.id ) )
				continue;

			if ( definition.subs != null )
			{
				for ( int s = 0; s < definition.subs.Length; s++ )
				{
					ObjectiveSubDefinition sub = definition.subs[ s ];
					if ( sub == null || string.IsNullOrEmpty( sub.id ) )
						continue;
					if ( IsSubCompleted( definition.id, sub.id ) )
						continue;

					string key = SubKey( definition.id, sub.id );
					_completedSubs.Add( key );
					PersistSub( key );
				}
			}

			_pendingCompleteIds.Remove( definition.id );
			if ( !IsCompleted( definition.id ) )
				CompleteObjective( definition, ignoreWorldState: true, announce: false );
		}

		RefreshNearbyHud( force: true );
	}

	public void DebugResetProgress()
	{
		StopAllCoroutines();
		_pendingCompleteIds.Clear();
		_completedObjectives.Clear();
		_completedSubs.Clear();

		ProfileSaveData save = GetSave();
		if ( save != null )
		{
			save.EnsureObjectiveProgress();
			save.completedObjectiveIds.Clear();
			save.completedObjectiveSubIds.Clear();
			if ( ProfileManager.Instance != null )
				ProfileManager.Instance.SaveCurrentStatsToProfile();
		}

		_lastHudFingerprint = null;
		RefreshNearbyHud( force: true );
	}

	void Subscribe()
	{
		if ( _subscribed )
			return;

		EventBus.Subscribe<CoinDisplayTableCompletedEvent>( OnCoinDisplayCompleted );
		EventBus.Subscribe<CoinDisplayTableChangedEvent>( OnCoinDisplayChanged );
		EventBus.Subscribe<GemDisplayTableCompletedEvent>( OnGemDisplayCompleted );
		EventBus.Subscribe<GemDisplayTableChangedEvent>( OnGemDisplayChanged );
		EventBus.Subscribe<ArtifactPresentationTableCompletedEvent>( OnArtifactDisplayCompleted );
		EventBus.Subscribe<ArtifactPresentationTableChangedEvent>( OnArtifactDisplayChanged );
		EventBus.Subscribe<GoldBarDisplayTableCompletedEvent>( OnGoldBarDisplayCompleted );
		EventBus.Subscribe<GoldBarDisplayTableChangedEvent>( OnGoldBarDisplayChanged );
		EventBus.Subscribe<TreasurePileEmptiedEvent>( OnPileEmptied );
		EventBus.Subscribe<TreasurePileDigEvent>( OnPileDig );
		EventBus.Subscribe<GemConstellationCompletedEvent>( OnConstellationCompleted );
		EventBus.Subscribe<GemConstellationChangedEvent>( OnConstellationChanged );
		EventBus.Subscribe<VolumeEnteredEvent>( OnVolumeEntered );
		EventBus.Subscribe<VolumeExitedEvent>( OnVolumeExited );
		EventBus.Subscribe<WorldEventFiredEvent>( OnWorldEventFired );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;

		EventBus.Unsubscribe<CoinDisplayTableCompletedEvent>( OnCoinDisplayCompleted );
		EventBus.Unsubscribe<CoinDisplayTableChangedEvent>( OnCoinDisplayChanged );
		EventBus.Unsubscribe<GemDisplayTableCompletedEvent>( OnGemDisplayCompleted );
		EventBus.Unsubscribe<GemDisplayTableChangedEvent>( OnGemDisplayChanged );
		EventBus.Unsubscribe<ArtifactPresentationTableCompletedEvent>( OnArtifactDisplayCompleted );
		EventBus.Unsubscribe<ArtifactPresentationTableChangedEvent>( OnArtifactDisplayChanged );
		EventBus.Unsubscribe<GoldBarDisplayTableCompletedEvent>( OnGoldBarDisplayCompleted );
		EventBus.Unsubscribe<GoldBarDisplayTableChangedEvent>( OnGoldBarDisplayChanged );
		EventBus.Unsubscribe<TreasurePileEmptiedEvent>( OnPileEmptied );
		EventBus.Unsubscribe<TreasurePileDigEvent>( OnPileDig );
		EventBus.Unsubscribe<GemConstellationCompletedEvent>( OnConstellationCompleted );
		EventBus.Unsubscribe<GemConstellationChangedEvent>( OnConstellationChanged );
		EventBus.Unsubscribe<VolumeEnteredEvent>( OnVolumeEntered );
		EventBus.Unsubscribe<VolumeExitedEvent>( OnVolumeExited );
		EventBus.Unsubscribe<WorldEventFiredEvent>( OnWorldEventFired );
		_subscribed = false;
	}

	void OnCoinDisplayCompleted( CoinDisplayTableCompletedEvent evt )
	{
		TryMatchComponent( ObjectiveSubCompleteType.DisplayComplete, evt.Table as Component );
		OnCountableProgressChanged();
	}

	void OnCoinDisplayChanged( CoinDisplayTableChangedEvent evt )
	{
		OnCountableProgressChanged();
	}

	void OnGemDisplayCompleted( GemDisplayTableCompletedEvent evt )
	{
		TryMatchComponent( ObjectiveSubCompleteType.DisplayComplete, evt.Table as Component );
		OnCountableProgressChanged();
	}

	void OnGemDisplayChanged( GemDisplayTableChangedEvent evt )
	{
		OnCountableProgressChanged();
	}

	void OnArtifactDisplayCompleted( ArtifactPresentationTableCompletedEvent evt )
	{
		TryMatchComponent( ObjectiveSubCompleteType.DisplayComplete, evt.Table as Component );
		OnCountableProgressChanged();
	}

	void OnArtifactDisplayChanged( ArtifactPresentationTableChangedEvent evt )
	{
		OnCountableProgressChanged();
	}

	void OnGoldBarDisplayCompleted( GoldBarDisplayTableCompletedEvent evt )
	{
		TryMatchComponent( ObjectiveSubCompleteType.DisplayComplete, evt.Table as Component );
		OnCountableProgressChanged();
	}

	void OnGoldBarDisplayChanged( GoldBarDisplayTableChangedEvent evt )
	{
		OnCountableProgressChanged();
	}

	void OnPileEmptied( TreasurePileEmptiedEvent evt )
	{
		TryMatchComponent( ObjectiveSubCompleteType.PileEmptied, evt.Pile as Component );
		OnCountableProgressChanged();
	}

	void OnPileDig( TreasurePileDigEvent evt )
	{
		OnCountableProgressChanged();
	}

	void OnConstellationCompleted( GemConstellationCompletedEvent evt )
	{
		TryMatchComponent( ObjectiveSubCompleteType.ConstellationComplete, evt.Constellation as Component );
		OnCountableProgressChanged();
	}

	void OnConstellationChanged( GemConstellationChangedEvent evt )
	{
		OnCountableProgressChanged();
	}

	void OnCountableProgressChanged()
	{
		TryCompleteCountSubs();
		ScheduleObjectivesWithAllSubsDone();
		RefreshNearbyHud( force: true );
	}

	void TryCompleteCountSubs()
	{
		if ( _catalog == null || _catalog.objectives == null )
			return;

		for ( int i = 0; i < _catalog.objectives.Count; i++ )
		{
			ObjectiveDefinition definition = _catalog.objectives[ i ];
			if ( definition == null || IsCompleted( definition.id ) )
				continue;
			if ( !ArePrerequisitesMet( definition ) )
				continue;
			if ( definition.subs == null )
				continue;

			for ( int s = 0; s < definition.subs.Length; s++ )
			{
				ObjectiveSubDefinition sub = definition.subs[ s ];
				if ( sub == null || string.IsNullOrEmpty( sub.id ) )
					continue;
				if ( IsSubCompleted( definition.id, sub.id ) )
					continue;
				if ( sub.completeType == ObjectiveSubCompleteType.DisplayComplete )
				{
					if ( !ObjectiveProgress.IsDisplayTargetComplete( definition, sub ) )
						continue;
				}
				else if ( !ObjectiveProgress.HasCountProgress( sub.completeType ) )
				{
					continue;
				}
				else if ( !ObjectiveProgress.IsCountMet( definition, sub ) )
				{
					continue;
				}

				TryCompleteSub( definition, sub.id );
			}
		}
	}

	void OnVolumeEntered( VolumeEnteredEvent evt )
	{
		if ( !string.IsNullOrEmpty( evt.VolumeId ) )
			_enteredVolumes.Add( evt.VolumeId );

		TryMatchTargetId( ObjectiveSubCompleteType.EnterVolume, evt.VolumeId );
		RefreshNearbyHud( force: true );
	}

	void OnVolumeExited( VolumeExitedEvent evt )
	{
		if ( !string.IsNullOrEmpty( evt.VolumeId ) )
			_enteredVolumes.Remove( evt.VolumeId );

		RefreshNearbyHud( force: true );
	}

	void OnWorldEventFired( WorldEventFiredEvent evt )
	{
		TryMatchTargetId( ObjectiveSubCompleteType.WorldEventFired, evt.Id );
	}

	void TryMatchComponent( ObjectiveSubCompleteType type, Component component )
	{
		string targetId = EventTargetRegistry.ResolveTargetId( component );
		TryMatchTargetId( type, targetId, allowEmptyTarget: true );
	}

	void TryMatchTargetId( ObjectiveSubCompleteType type, string eventTargetId, bool allowEmptyTarget = false )
	{
		if ( _catalog == null || _catalog.objectives == null )
			return;

		for ( int i = 0; i < _catalog.objectives.Count; i++ )
		{
			ObjectiveDefinition definition = _catalog.objectives[ i ];
			if ( definition == null || IsCompleted( definition.id ) )
				continue;
			if ( !ArePrerequisitesMet( definition ) )
				continue;
			if ( definition.subs == null )
				continue;

			for ( int s = 0; s < definition.subs.Length; s++ )
			{
				ObjectiveSubDefinition sub = definition.subs[ s ];
				if ( sub == null || sub.completeType != type )
					continue;
				if ( string.IsNullOrEmpty( sub.id ) )
					continue;
				if ( IsSubCompleted( definition.id, sub.id ) )
					continue;

				if ( !string.IsNullOrEmpty( sub.targetId ) )
				{
					if ( sub.targetId != eventTargetId )
						continue;
				}
				else
				{
					// Empty DisplayComplete / PileEmptied targets aggregate via counts — never complete from a single event.
					if ( type == ObjectiveSubCompleteType.DisplayComplete || type == ObjectiveSubCompleteType.PileEmptied )
						continue;
					if ( !allowEmptyTarget )
						continue;
				}

				TryCompleteSub( definition, sub.id );
			}
		}
	}

	bool TryCompleteSub( ObjectiveDefinition definition, string subId )
	{
		if ( definition == null || string.IsNullOrEmpty( definition.id ) || string.IsNullOrEmpty( subId ) )
			return false;
		if ( IsCompleted( definition.id ) )
			return false;
		if ( !ArePrerequisitesMet( definition ) )
			return false;
		if ( IsSubCompleted( definition.id, subId ) )
			return false;

		if ( !HasSub( definition, subId ) )
			return false;

		string key = SubKey( definition.id, subId );
		_completedSubs.Add( key );
		PersistSub( key );

		EventBus.Publish( new ObjectiveSubCompletedEvent
		{
			ObjectiveId = definition.id,
			SubId = subId
		} );

		if ( AreAllSubsComplete( definition ) )
			ScheduleCompleteObjective( definition );
		RefreshNearbyHud( force: true );

		return true;
	}

	static bool HasSub( ObjectiveDefinition definition, string subId )
	{
		if ( definition.subs == null )
			return false;

		for ( int i = 0; i < definition.subs.Length; i++ )
		{
			ObjectiveSubDefinition sub = definition.subs[ i ];
			if ( sub != null && sub.id == subId )
				return true;
		}

		return false;
	}

	bool AreAllSubsComplete( ObjectiveDefinition definition )
	{
		if ( definition.subs == null || definition.subs.Length == 0 )
			return true;

		for ( int i = 0; i < definition.subs.Length; i++ )
		{
			ObjectiveSubDefinition sub = definition.subs[ i ];
			if ( sub == null || string.IsNullOrEmpty( sub.id ) )
				continue;
			if ( !IsSubCompleted( definition.id, sub.id ) )
				return false;
			if ( !IsSubWorldStateSatisfied( definition, sub ) )
				return false;
		}

		return true;
	}

	static bool IsSubWorldStateSatisfied( ObjectiveDefinition definition, ObjectiveSubDefinition sub )
	{
		if ( sub == null )
			return false;

		if ( sub.completeType == ObjectiveSubCompleteType.DisplayComplete )
			return ObjectiveProgress.IsDisplayTargetComplete( definition, sub );

		if ( ObjectiveProgress.HasCountProgress( sub.completeType ) )
			return ObjectiveProgress.IsCountMet( definition, sub );

		return true;
	}

	void CompleteObjectivesWithAllSubsDone()
	{
		if ( _catalog == null || _catalog.objectives == null )
			return;

		for ( int i = 0; i < _catalog.objectives.Count; i++ )
		{
			ObjectiveDefinition definition = _catalog.objectives[ i ];
			if ( definition == null || string.IsNullOrEmpty( definition.id ) )
				continue;
			if ( IsCompleted( definition.id ) )
				continue;
			if ( !ArePrerequisitesMet( definition ) )
				continue;
			if ( !AreAllSubsComplete( definition ) )
				continue;
			CompleteObjective( definition );
		}
	}

	void ScheduleObjectivesWithAllSubsDone()
	{
		if ( _catalog == null || _catalog.objectives == null )
			return;

		for ( int i = 0; i < _catalog.objectives.Count; i++ )
		{
			ObjectiveDefinition definition = _catalog.objectives[ i ];
			if ( definition == null || string.IsNullOrEmpty( definition.id ) )
				continue;
			if ( IsCompleted( definition.id ) )
				continue;
			if ( !ArePrerequisitesMet( definition ) )
				continue;
			if ( !AreAllSubsComplete( definition ) )
				continue;
			ScheduleCompleteObjective( definition );
		}
	}

	void ScheduleCompleteObjective( ObjectiveDefinition definition )
	{
		if ( definition == null || string.IsNullOrEmpty( definition.id ) )
			return;
		if ( IsCompleted( definition.id ) )
			return;
		if ( !_pendingCompleteIds.Add( definition.id ) )
			return;

		StartCoroutine( DelayedCompleteObjective( definition ) );
	}

	IEnumerator DelayedCompleteObjective( ObjectiveDefinition definition )
	{
		float elapsed = 0f;
		while ( elapsed < LastTaskToCompleteDelay )
		{
			elapsed += Time.unscaledDeltaTime;
			yield return null;
		}

		if ( definition != null && !string.IsNullOrEmpty( definition.id ) )
			_pendingCompleteIds.Remove( definition.id );

		if ( definition == null || !AreAllSubsComplete( definition ) )
			yield break;

		CompleteObjective( definition );
	}

	void CompleteObjective( ObjectiveDefinition definition, bool ignoreWorldState = false, bool announce = true )
	{
		if ( definition == null || string.IsNullOrEmpty( definition.id ) )
			return;
		if ( IsCompleted( definition.id ) )
			return;
		if ( !ignoreWorldState && !AreAllSubsComplete( definition ) )
			return;

		_pendingCompleteIds.Remove( definition.id );
		_completedObjectives.Add( definition.id );
		PersistObjective( definition.id );

		if ( announce && !string.IsNullOrEmpty( definition.completionToast ) )
			UnlockRewardToastUI.NotifyMessage( definition.completionToast, null, ToastStackUI.ToastTier.Milestone );

		GrantRewards( definition );
		FireWorldEvents( definition );

		EventBus.Publish( new ObjectiveCompletedEvent
		{
			ObjectiveId = definition.id,
			Definition = definition
		} );

		RefreshNearbyHud( force: true );
	}

	void GrantRewards( ObjectiveDefinition definition )
	{
		if ( definition.rewards == null )
			return;

		for ( int i = 0; i < definition.rewards.Length; i++ )
		{
			ObjectiveReward reward = definition.rewards[ i ];
			if ( reward == null )
				continue;

			if ( reward.type == ObjectiveRewardType.Ability )
			{
				if ( string.IsNullOrEmpty( reward.abilityId ) )
					continue;
				AbilitySystem abilities = AbilitySystem.Instance;
				if ( abilities != null )
					abilities.UnlockAbility( reward.abilityId );
				continue;
			}

			if ( string.IsNullOrEmpty( reward.upgradeId ) )
				continue;

			UpgradeSystem upgrades = UpgradeSystem.Instance;
			if ( upgrades == null )
				continue;

			UpgradeDefinition upgradeDef = null;
			upgrades.TryGetDefinition( reward.upgradeId, out upgradeDef );

			if ( reward.upgradeLevel > 0 )
				upgrades.SetUpgradeLevel( reward.upgradeId, reward.upgradeLevel );
			else
				upgrades.UnlockUpgrade( reward.upgradeId );

			if ( upgradeDef != null )
				UnlockRewardToastUI.NotifyUnlock( upgradeDef );
			else
				UnlockRewardToastUI.NotifyMessage( "Unlocked: " + reward.upgradeId );
		}
	}

	void FireCompletedObjectiveWorldEvents()
	{
		if ( _catalog == null || _catalog.objectives == null )
			return;

		for ( int i = 0; i < _catalog.objectives.Count; i++ )
		{
			ObjectiveDefinition definition = _catalog.objectives[ i ];
			if ( definition == null || !IsCompleted( definition.id ) )
				continue;
			FireWorldEvents( definition );
		}
	}

	void FireWorldEvents( ObjectiveDefinition definition )
	{
		if ( definition == null || definition.onCompleteWorldEventIds == null )
			return;

		WorldEventSystem worldEvents = WorldEventSystem.Instance;
		if ( worldEvents == null )
			return;

		for ( int i = 0; i < definition.onCompleteWorldEventIds.Length; i++ )
		{
			string eventId = definition.onCompleteWorldEventIds[ i ];
			if ( string.IsNullOrEmpty( eventId ) )
				continue;
			worldEvents.TryFireIfUnfired( eventId );
		}
	}

	void PersistSub( string key )
	{
		ProfileSaveData save = GetSave();
		if ( save == null )
			return;

		save.EnsureObjectiveProgress();
		if ( !save.completedObjectiveSubIds.Contains( key ) )
			save.completedObjectiveSubIds.Add( key );

		if ( ProfileManager.Instance != null )
			ProfileManager.Instance.SaveCurrentStatsToProfile();
	}

	void PersistObjective( string objectiveId )
	{
		ProfileSaveData save = GetSave();
		if ( save == null )
			return;

		save.EnsureObjectiveProgress();
		if ( !save.completedObjectiveIds.Contains( objectiveId ) )
			save.completedObjectiveIds.Add( objectiveId );

		if ( ProfileManager.Instance != null )
			ProfileManager.Instance.SaveCurrentStatsToProfile();
	}

	static ProfileSaveData GetSave()
	{
		if ( ProfileManager.Instance == null )
			return null;
		return ProfileManager.Instance.ProfileSaveData;
	}

	static string SubKey( string objectiveId, string subId )
	{
		return objectiveId + "/" + subId;
	}

	void RefreshNearbyHud( bool force )
	{
		_nearbyScratch.Clear();

		if ( _catalog != null && _catalog.objectives != null )
		{
			for ( int i = 0; i < _catalog.objectives.Count; i++ )
			{
				ObjectiveDefinition definition = _catalog.objectives[ i ];
				if ( definition == null || string.IsNullOrEmpty( definition.id ) )
					continue;
				if ( IsCompleted( definition.id ) )
					continue;
				if ( !ArePrerequisitesMet( definition ) )
					continue;
				if ( !IsInShowContext( definition ) )
					continue;

				string markerId = null;
				Vector3 markerPos = Vector3.zero;
				float distance = 0f;
				if ( !TryResolveNearbyAnchor( definition, out markerId, out markerPos, out distance ) )
				{
					// Volume-gated objectives can still list with no marker if subs have no QuestTarget yet.
					if ( string.IsNullOrEmpty( definition.showVolumeId ) )
						continue;
					if ( EventTargetRegistry.TryGetMarkerTransform( definition.showVolumeId, out Transform volumeMarker ) && volumeMarker != null )
					{
						markerId = definition.showVolumeId;
						markerPos = volumeMarker.position;
						distance = TutorialHudDistance.HorizontalTo( markerPos );
					}
				}

				_nearbyScratch.Add( new NearbyCandidate
				{
					Definition = definition,
					Distance = distance,
					MarkerTargetId = markerId,
					MarkerWorldPosition = markerPos
				} );
			}
		}

		_nearbyScratch.Sort( CompareNearby );

		int count = Mathf.Min( NearbyCap, _nearbyScratch.Count );
		string fingerprint = BuildFingerprint( count );
		if ( !force && fingerprint == _lastHudFingerprint )
			return;
		_lastHudFingerprint = fingerprint;

		if ( count <= 0 )
		{
			TutorialHud.Clear();
			return;
		}

		_rowScratch.Clear();
		_outlineScratch.Clear();

		string markerTargetId = null;
		Vector3 markerWorld = Vector3.zero;
		bool hasMarker = false;

		for ( int i = 0; i < count; i++ )
		{
			NearbyCandidate candidate = _nearbyScratch[ i ];
			ObjectiveDefinition definition = candidate.Definition;
			string title = definition.ResolveTitle();
			string distanceText = TutorialHudDistance.Format( candidate.Distance );

			_rowScratch.Add( new TutorialHudRow
			{
				ObjectiveId = definition.id,
				Text = title + "  (" + distanceText + ")",
				Indent = 0,
				Complete = false,
				ContextualFocus = i == 0
			} );

			if ( definition.subs != null )
			{
				for ( int s = 0; s < definition.subs.Length; s++ )
				{
					ObjectiveSubDefinition sub = definition.subs[ s ];
					if ( sub == null || string.IsNullOrEmpty( sub.id ) )
						continue;

					bool subComplete = IsSubCompleted( definition.id, sub.id );
					string label = ObjectiveProgress.FormatLabel( definition, sub, subComplete );
					_rowScratch.Add( new TutorialHudRow
					{
						ObjectiveId = definition.id + "/" + sub.id,
						Text = label,
						Indent = 1,
						Complete = subComplete,
						ContextualFocus = false
					} );
				}
			}

			if ( definition.ShouldShowReward() )
			{
				string reward = definition.ResolveRewardLabel();
				if ( !string.IsNullOrEmpty( reward ) )
				{
					_rowScratch.Add( new TutorialHudRow
					{
						ObjectiveId = definition.id + "/reward",
						Text = reward,
						Indent = 1,
						Complete = false,
						ContextualFocus = false,
						IsReward = true
					} );
				}
			}

			CollectIncompleteSubOutlines( definition, _outlineScratch );

			if ( !hasMarker && !string.IsNullOrEmpty( candidate.MarkerTargetId ) )
			{
				hasMarker = true;
				markerTargetId = candidate.MarkerTargetId;
				markerWorld = candidate.MarkerWorldPosition;
			}
		}

		TutorialHud.SetOutlineRoots( _outlineScratch );
		TutorialHud.Publish( new TutorialHudChangedEvent
		{
			Title = "Nearby",
			ObjectiveText = string.Empty,
			Rows = _rowScratch.ToArray(),
			HasMarker = hasMarker,
			MarkerWorldPosition = markerWorld,
			Cleared = false
		} );

		// Keep markerTargetId referenced for clarity / future compass wiring via position.
		_ = markerTargetId;
	}

	string BuildFingerprint( int count )
	{
		System.Text.StringBuilder sb = new System.Text.StringBuilder( 64 );
		sb.Append( count );
		for ( int i = 0; i < count; i++ )
		{
			NearbyCandidate candidate = _nearbyScratch[ i ];
			sb.Append( '|' ).Append( candidate.Definition.id );
			sb.Append( '@' ).Append( Mathf.RoundToInt( candidate.Distance ) );
			if ( candidate.Definition.subs == null )
				continue;
			for ( int s = 0; s < candidate.Definition.subs.Length; s++ )
			{
				ObjectiveSubDefinition sub = candidate.Definition.subs[ s ];
				if ( sub == null || string.IsNullOrEmpty( sub.id ) )
					continue;
				sb.Append( IsSubCompleted( candidate.Definition.id, sub.id ) ? '1' : '0' );
				if ( !ObjectiveProgress.HasCountProgress( sub.completeType ) )
					continue;
				int current;
				int required;
				if ( !ObjectiveProgress.TryGetCounts( candidate.Definition, sub, out current, out required ) )
					continue;
				sb.Append( current ).Append( '/' ).Append( required );
			}
		}

		return sb.ToString();
	}

	bool IsInShowContext( ObjectiveDefinition definition )
	{
		if ( definition == null )
			return false;

		if ( !string.IsNullOrEmpty( definition.showVolumeId ) )
			return _enteredVolumes.Contains( definition.showVolumeId );

		if ( !TryResolveNearbyAnchor( definition, out _, out Vector3 markerPos, out float distance ) )
			return false;

		float radius = definition.showRadius > 0f ? definition.showRadius : DefaultShowRadius;
		return distance <= radius;
	}

	static int CompareNearby( NearbyCandidate a, NearbyCandidate b )
	{
		int cmp = a.Distance.CompareTo( b.Distance );
		if ( cmp != 0 )
			return cmp;
		return string.CompareOrdinal( a.Definition.id, b.Definition.id );
	}

	bool TryResolveNearbyAnchor( ObjectiveDefinition definition, out string markerId, out Vector3 markerPos, out float distance )
	{
		markerId = null;
		markerPos = Vector3.zero;
		distance = float.MaxValue;

		if ( definition.subs == null )
			return false;

		// Primary: centroid of all incomplete subs with resolvable QuestTargets.
		Vector3 sum = Vector3.zero;
		int count = 0;
		string firstId = null;
		for ( int i = 0; i < definition.subs.Length; i++ )
		{
			ObjectiveSubDefinition sub = definition.subs[ i ];
			if ( sub == null || string.IsNullOrEmpty( sub.id ) )
				continue;
			if ( IsSubCompleted( definition.id, sub.id ) )
				continue;

			if ( !TryResolveSubAnchor( sub, out string id, out Vector3 pos, out _ ) )
				continue;

			sum += pos;
			count++;
			if ( firstId == null )
				firstId = id;
		}

		if ( count > 0 )
		{
			markerId = firstId;
			markerPos = sum / count;
			distance = TutorialHudDistance.HorizontalTo( markerPos );
			return true;
		}

		// Fallback: any sub with a resolvable target (even completed) so the objective can still show.
		for ( int i = 0; i < definition.subs.Length; i++ )
		{
			ObjectiveSubDefinition sub = definition.subs[ i ];
			if ( sub == null )
				continue;
			if ( !TryResolveSubAnchor( sub, out string id, out Vector3 pos, out _ ) )
				continue;

			markerId = id;
			markerPos = pos;
			distance = TutorialHudDistance.HorizontalTo( pos );
			return true;
		}

		return false;
	}

	void CollectIncompleteSubOutlines( ObjectiveDefinition definition, List<Transform> destination )
	{
		if ( definition == null || definition.subs == null || destination == null )
			return;

		for ( int i = 0; i < definition.subs.Length; i++ )
		{
			ObjectiveSubDefinition sub = definition.subs[ i ];
			if ( sub == null || string.IsNullOrEmpty( sub.id ) )
				continue;
			if ( IsSubCompleted( definition.id, sub.id ) )
				continue;
			if ( !TryResolveSubAnchor( sub, out _, out _, out Transform outline ) )
				continue;
			if ( outline == null )
				continue;
			if ( destination.Contains( outline ) )
				continue;
			destination.Add( outline );
		}
	}

	static bool TryResolveSubAnchor( ObjectiveSubDefinition sub, out string markerId, out Vector3 markerPos, out Transform outlineRoot )
	{
		markerId = null;
		markerPos = Vector3.zero;
		outlineRoot = null;

		if ( sub == null || string.IsNullOrEmpty( sub.targetId ) )
			return false;

		if ( EventTargetRegistry.TryGetMarkerTransform( sub.targetId, out Transform marker ) && marker != null )
		{
			markerId = sub.targetId;
			markerPos = marker.position;
			if ( EventTargetRegistry.TryGetTarget( sub.targetId, out QuestTarget target ) && target != null )
				outlineRoot = target.ResolveOutlineRoot();
			return true;
		}

		return false;
	}
}
