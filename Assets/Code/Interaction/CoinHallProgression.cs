using System;
using System.Collections;
using System.Collections.Generic;

using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Scene-authored Coin Hall expansion: walls/roof tween to per-level targets and content enables
/// when required <see cref="CoinDisplayTableInteractable"/> tables are all complete.
/// </summary>
/// <remarks>
/// Scene hookup:
/// 1. Parent moving walls/roof under named roots and assign them to <see cref="movers"/>.
/// 2. Place empty target Transforms per level (e.g. Wall_North_L0, Wall_North_L1) under a targets folder.
/// 3. Per level: assign parallel <see cref="CoinHallExpansionLevel.moverTargets"/>, tables that gate that level,
///    and inactive content parents in <see cref="CoinHallExpansionLevel.objectsToEnable"/>.
/// 4. Level 0 = starting hall (no required tables). Later tables should be enabled by the previous level's enable list.
/// 5. Optional: wire Feedbacks, cinematic presentation id, lantern reveal id for the expand beat.
/// </remarks>
[DisallowMultipleComponent]
public class CoinHallProgression : MonoBehaviour
{
	public const string DefaultHallId = "coin_hall";

	static readonly List<CoinHallProgression> All = new List<CoinHallProgression>( 4 );

	[SerializeField]
	[Tooltip( "Save key for this hall. Default coin_hall." )]
	string hallId = DefaultHallId;

	[SerializeField]
	[Tooltip( "Wall/roof roots that move. Index matches each level's moverTargets array." )]
	CoinHallMover[] movers = Array.Empty<CoinHallMover>();

	[SerializeField]
	[Tooltip( "Ordered expansion levels. Index 0 = starting hall (empty requiredTables). Each later level lists gate tables + mover targets + content to turn on. For epic reveals: enable dense coin piles, lights, map volumes; use levelFeedbacks for rumble/dust/sting; optional cinematic + lantern ids below." )]
	CoinHallExpansionLevel[] levels = Array.Empty<CoinHallExpansionLevel>();

	[Header( "Motion" )]
	[SerializeField]
	[Min( 0.05f )]
	[Tooltip( "Seconds to tween movers when expanding during play." )]
	float expandDuration = 3f;

	[SerializeField]
	AnimationCurve expandCurve = AnimationCurve.EaseInOut( 0f, 0f, 1f, 1f );

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Delay after the last required table completes before the hall expands (table ceremony)." )]
	float expandDelay = 0.5f;

	[Header( "Juice" )]
	[SerializeField]
	[Tooltip( "Played on every animated expansion. Per-level Feedbacks override when set." )]
	Feedbacks expandFeedbacks;

	[SerializeField]
	[Tooltip( "Optional CinematicPresentationController id to play on animated expand." )]
	string optionalCinematicPresentationId;

	[SerializeField]
	[Tooltip( "Optional LanternRevealSweepController id to play on animated expand." )]
	string optionalLanternRevealId;

	int _currentLevel;
	bool _subscribed;
	bool _initialized;
	Coroutine _expandRoutine;
	Coroutine _advanceRoutine;

	public static IReadOnlyList<CoinHallProgression> ActiveHalls => All;

	public string HallId => string.IsNullOrEmpty( hallId ) ? DefaultHallId : hallId;

	public int CurrentLevel => _currentLevel;

	public int LevelCount => levels != null ? levels.Length : 0;

	public string CurrentLevelId
	{
		get
		{
			if ( levels == null || _currentLevel < 0 || _currentLevel >= levels.Length )
				return string.Empty;
			CoinHallExpansionLevel level = levels[ _currentLevel ];
			return level != null ? level.id : string.Empty;
		}
	}

	void OnEnable()
	{
		if ( !All.Contains( this ) )
			All.Add( this );

		Subscribe();
		InitializeFromSave();
	}

	void OnDisable()
	{
		Unsubscribe();
		StopExpandRoutines();
		All.Remove( this );
	}

	void Subscribe()
	{
		if ( _subscribed )
			return;

		EventBus.Subscribe<CoinDisplayTableCompletedEvent>( OnCoinDisplayCompleted );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;

		EventBus.Unsubscribe<CoinDisplayTableCompletedEvent>( OnCoinDisplayCompleted );
		_subscribed = false;
	}

	void InitializeFromSave()
	{
		int saved = CoinHallProgress.GetLevel( HallId );
		int clamped = ClampLevel( saved );
		ApplyLevel( clamped, animate: false, fromLoad: true, persist: false );
		_initialized = true;
		TryAdvanceAll( animate: false, fromLoad: true );
	}

	void OnCoinDisplayCompleted( CoinDisplayTableCompletedEvent evt )
	{
		if ( !_initialized )
			return;

		if ( evt.Table == null )
			return;

		if ( !IsTableRelevant( evt.Table ) )
			return;

		TryAdvanceAll( animate: true, fromLoad: false );
	}

	bool IsTableRelevant( CoinDisplayTableInteractable table )
	{
		if ( levels == null )
			return false;

		for ( int i = 0; i < levels.Length; i++ )
		{
			CoinHallExpansionLevel level = levels[ i ];
			if ( level == null || level.requiredTables == null )
				continue;

			for ( int t = 0; t < level.requiredTables.Length; t++ )
			{
				if ( level.requiredTables[ t ] == table )
					return true;
			}
		}

		return false;
	}

	void TryAdvanceAll( bool animate, bool fromLoad )
	{
		if ( levels == null || levels.Length == 0 )
			return;

		if ( _advanceRoutine != null )
		{
			StopCoroutine( _advanceRoutine );
			_advanceRoutine = null;
		}

		if ( animate && !fromLoad && expandDelay > 0f )
			_advanceRoutine = StartCoroutine( AdvanceAfterDelayRoutine() );
		else
			AdvanceWhileReady( animate, fromLoad );
	}

	IEnumerator AdvanceAfterDelayRoutine()
	{
		yield return new WaitForSeconds( expandDelay );
		_advanceRoutine = null;
		AdvanceWhileReady( animate: true, fromLoad: false );
	}

	void AdvanceWhileReady( bool animate, bool fromLoad )
	{
		while ( _currentLevel + 1 < LevelCount && AreRequiredTablesComplete( _currentLevel + 1 ) )
		{
			int next = _currentLevel + 1;
			if ( animate && !fromLoad )
			{
				BeginExpandTo( next, fromLoad: false );
				return;
			}

			ApplyLevel( next, animate: false, fromLoad: fromLoad, persist: true );
		}
	}

	bool AreRequiredTablesComplete( int levelIndex )
	{
		if ( levels == null || levelIndex < 0 || levelIndex >= levels.Length )
			return false;

		CoinHallExpansionLevel level = levels[ levelIndex ];
		if ( level == null )
			return false;

		CoinDisplayTableInteractable[] tables = level.requiredTables;
		if ( tables == null || tables.Length == 0 )
			return true;

		for ( int i = 0; i < tables.Length; i++ )
		{
			CoinDisplayTableInteractable table = tables[ i ];
			if ( table == null || !table.IsComplete )
				return false;
		}

		return true;
	}

	void BeginExpandTo( int levelIndex, bool fromLoad )
	{
		StopExpandRoutines();
		_expandRoutine = StartCoroutine( ExpandRoutine( levelIndex, fromLoad ) );
	}

	IEnumerator ExpandRoutine( int levelIndex, bool fromLoad )
	{
		PlayExpandJuice( levelIndex );
		yield return TweenMoversToLevel( levelIndex );

		ApplyObjectStateThrough( levelIndex );
		_currentLevel = levelIndex;
		CoinHallProgress.MarkReached( HallId, _currentLevel );
		PublishExpanded( fromLoad );

		_expandRoutine = null;
		AdvanceWhileReady( animate: true, fromLoad: false );
	}

	/// <summary>Debug / resume: set hall to a level immediately (snap, no delay).</summary>
	public void ForceLevel( int levelIndex, bool persist = true )
	{
		StopExpandRoutines();
		int clamped = ClampLevel( levelIndex );
		ApplyLevel( clamped, animate: false, fromLoad: false, persist: false );
		if ( persist )
			CoinHallProgress.SetLevel( HallId, clamped );
	}

	/// <summary>Debug: reset hall to level 0 and clear save for this hall id.</summary>
	public void ResetHall()
	{
		StopExpandRoutines();
		CoinHallProgress.Clear( HallId );
		ApplyLevel( 0, animate: false, fromLoad: false, persist: false );
		CoinHallProgress.SetLevel( HallId, 0 );
	}

	void ApplyLevel( int levelIndex, bool animate, bool fromLoad, bool persist )
	{
		levelIndex = ClampLevel( levelIndex );
		ApplyObjectStateThrough( levelIndex );

		if ( animate )
		{
			BeginExpandTo( levelIndex, fromLoad );
			return;
		}

		SnapMoversToLevel( levelIndex );
		_currentLevel = levelIndex;

		if ( persist )
			CoinHallProgress.MarkReached( HallId, _currentLevel );

		PublishExpanded( fromLoad );
	}

	void ApplyObjectStateThrough( int levelIndex )
	{
		if ( levels == null )
			return;

		for ( int i = 0; i < levels.Length; i++ )
		{
			CoinHallExpansionLevel level = levels[ i ];
			if ( level == null )
				continue;

			bool reached = i <= levelIndex;
			SetActiveArray( level.objectsToEnable, reached );
			SetActiveArray( level.objectsToDisable, !reached );
		}
	}

	static void SetActiveArray( GameObject[] objects, bool active )
	{
		if ( objects == null )
			return;

		for ( int i = 0; i < objects.Length; i++ )
		{
			GameObject go = objects[ i ];
			if ( go == null )
				continue;
			if ( go.activeSelf == active )
				continue;
			go.SetActive( active );
		}
	}

	void SnapMoversToLevel( int levelIndex )
	{
		if ( movers == null || levels == null )
			return;
		if ( levelIndex < 0 || levelIndex >= levels.Length )
			return;

		CoinHallExpansionLevel level = levels[ levelIndex ];
		if ( level == null || level.moverTargets == null )
			return;

		for ( int i = 0; i < movers.Length; i++ )
		{
			CoinHallMover mover = movers[ i ];
			if ( mover == null || mover.movingTransform == null )
				continue;
			if ( i >= level.moverTargets.Length )
				continue;

			Transform target = level.moverTargets[ i ];
			if ( target == null )
				continue;

			mover.movingTransform.SetPositionAndRotation( target.position, target.rotation );
		}
	}

	IEnumerator TweenMoversToLevel( int levelIndex )
	{
		if ( movers == null || levels == null || levelIndex < 0 || levelIndex >= levels.Length )
			yield break;

		CoinHallExpansionLevel level = levels[ levelIndex ];
		if ( level == null || level.moverTargets == null )
			yield break;

		int count = Mathf.Min( movers.Length, level.moverTargets.Length );
		if ( count <= 0 )
			yield break;

		Vector3[] starts = new Vector3[ count ];
		Quaternion[] startRots = new Quaternion[ count ];
		Vector3[] ends = new Vector3[ count ];
		Quaternion[] endRots = new Quaternion[ count ];
		Transform[] moving = new Transform[ count ];
		int live = 0;

		for ( int i = 0; i < count; i++ )
		{
			CoinHallMover mover = movers[ i ];
			Transform target = level.moverTargets[ i ];
			if ( mover == null || mover.movingTransform == null || target == null )
				continue;

			moving[ live ] = mover.movingTransform;
			starts[ live ] = mover.movingTransform.position;
			startRots[ live ] = mover.movingTransform.rotation;
			ends[ live ] = target.position;
			endRots[ live ] = target.rotation;
			live++;
		}

		if ( live <= 0 )
			yield break;

		float duration = Mathf.Max( 0.05f, expandDuration );
		float elapsed = 0f;
		while ( elapsed < duration )
		{
			elapsed += Time.deltaTime;
			float t = Mathf.Clamp01( elapsed / duration );
			float curved = expandCurve != null ? expandCurve.Evaluate( t ) : t;
			for ( int i = 0; i < live; i++ )
			{
				Transform xf = moving[ i ];
				if ( xf == null )
					continue;
				xf.SetPositionAndRotation(
					Vector3.LerpUnclamped( starts[ i ], ends[ i ], curved ),
					Quaternion.SlerpUnclamped( startRots[ i ], endRots[ i ], curved ) );
			}

			yield return null;
		}

		for ( int i = 0; i < live; i++ )
		{
			Transform xf = moving[ i ];
			if ( xf == null )
				continue;
			xf.SetPositionAndRotation( ends[ i ], endRots[ i ] );
		}
	}

	void PlayExpandJuice( int levelIndex )
	{
		Feedbacks feedbacks = expandFeedbacks;
		if ( levels != null && levelIndex >= 0 && levelIndex < levels.Length )
		{
			CoinHallExpansionLevel level = levels[ levelIndex ];
			if ( level != null && level.levelFeedbacks != null )
				feedbacks = level.levelFeedbacks;
		}

		if ( feedbacks != null )
		{
			FeedbackContext context = new FeedbackContext();
			context.Source = gameObject;
			context.Target = gameObject;
			context.Position = transform.position;
			feedbacks.Play( context );
		}

		if ( !string.IsNullOrEmpty( optionalCinematicPresentationId ) )
			CinematicPresentationController.TryPlay( optionalCinematicPresentationId, default );

		if ( !string.IsNullOrEmpty( optionalLanternRevealId ) )
			LanternRevealSweepController.TryStartReveal( optionalLanternRevealId, default );
	}

	void PublishExpanded( bool fromLoad )
	{
		EventBus.Publish( new CoinHallExpandedEvent
		{
			HallId = HallId,
			LevelIndex = _currentLevel,
			LevelId = CurrentLevelId,
			FromLoad = fromLoad
		} );
	}

	int ClampLevel( int levelIndex )
	{
		if ( LevelCount <= 0 )
			return 0;
		return Mathf.Clamp( levelIndex, 0, LevelCount - 1 );
	}

	void StopExpandRoutines()
	{
		if ( _expandRoutine != null )
		{
			StopCoroutine( _expandRoutine );
			_expandRoutine = null;
		}

		if ( _advanceRoutine != null )
		{
			StopCoroutine( _advanceRoutine );
			_advanceRoutine = null;
		}
	}

#if UNITY_EDITOR
	struct EditorMoverPose
	{
		public Transform Transform;
		public Vector3 Position;
		public Quaternion Rotation;
	}

	struct EditorObjectActive
	{
		public GameObject Object;
		public bool Active;
	}

	EditorMoverPose[] _editorCachedMoverPoses;
	EditorObjectActive[] _editorCachedObjectActives;
	int _editorPreviewLevel = -1;
	bool _editorHasCachedPose;

	/// <summary>Editor-only: which level is currently previewed (-1 = none / restored).</summary>
	public int EditorPreviewLevelIndex => _editorPreviewLevel;

	/// <summary>Editor-only: true after a temporary preview that can be restored.</summary>
	public bool EditorHasCachedPose => _editorHasCachedPose;

	/// <summary>
	/// Editor-only: snap movers (and optionally object enable state) to a level for authoring.
	/// Caches the current poses on first preview so <see cref="EditorRestorePreview"/> can undo.
	/// </summary>
	public void EditorPreviewLevel( int levelIndex, bool applyObjectState )
	{
		if ( Application.isPlaying )
			return;
		if ( levelIndex < 0 || levelIndex >= LevelCount )
			return;

		if ( !_editorHasCachedPose )
			EditorCapturePreviewState( applyObjectState );

		UnityEditor.Undo.IncrementCurrentGroup();
		int undoGroup = UnityEditor.Undo.GetCurrentGroup();
		UnityEditor.Undo.SetCurrentGroupName( "Coin Hall Preview Level " + levelIndex );

		SnapMoversToLevelWithUndo( levelIndex );
		if ( applyObjectState )
			ApplyObjectStateThroughWithUndo( levelIndex );

		_editorPreviewLevel = levelIndex;
		UnityEditor.Undo.CollapseUndoOperations( undoGroup );
		UnityEditor.EditorUtility.SetDirty( this );
	}

	/// <summary>Editor-only level id for inspector buttons.</summary>
	public string EditorGetLevelId( int levelIndex )
	{
		if ( levels == null || levelIndex < 0 || levelIndex >= levels.Length )
			return string.Empty;
		CoinHallExpansionLevel level = levels[ levelIndex ];
		return level != null ? level.id : string.Empty;
	}

	/// <summary>Editor-only: restore mover poses (and object actives) captured before preview.</summary>
	public void EditorRestorePreview()
	{
		if ( Application.isPlaying || !_editorHasCachedPose )
			return;

		UnityEditor.Undo.IncrementCurrentGroup();
		int undoGroup = UnityEditor.Undo.GetCurrentGroup();
		UnityEditor.Undo.SetCurrentGroupName( "Coin Hall Restore Preview" );

		if ( _editorCachedMoverPoses != null )
		{
			for ( int i = 0; i < _editorCachedMoverPoses.Length; i++ )
			{
				EditorMoverPose pose = _editorCachedMoverPoses[ i ];
				if ( pose.Transform == null )
					continue;
				UnityEditor.Undo.RecordObject( pose.Transform, "Restore Coin Hall Mover" );
				pose.Transform.SetPositionAndRotation( pose.Position, pose.Rotation );
				UnityEditor.EditorUtility.SetDirty( pose.Transform );
			}
		}

		if ( _editorCachedObjectActives != null )
		{
			for ( int i = 0; i < _editorCachedObjectActives.Length; i++ )
			{
				EditorObjectActive entry = _editorCachedObjectActives[ i ];
				if ( entry.Object == null )
					continue;
				UnityEditor.Undo.RecordObject( entry.Object, "Restore Coin Hall Object" );
				entry.Object.SetActive( entry.Active );
				UnityEditor.EditorUtility.SetDirty( entry.Object );
			}
		}

		_editorPreviewLevel = -1;
		_editorHasCachedPose = false;
		_editorCachedMoverPoses = null;
		_editorCachedObjectActives = null;
		UnityEditor.Undo.CollapseUndoOperations( undoGroup );
		UnityEditor.EditorUtility.SetDirty( this );
	}

	void EditorCapturePreviewState( bool includeObjects )
	{
		List<EditorMoverPose> poses = new List<EditorMoverPose>( movers != null ? movers.Length : 0 );
		if ( movers != null )
		{
			for ( int i = 0; i < movers.Length; i++ )
			{
				CoinHallMover mover = movers[ i ];
				if ( mover == null || mover.movingTransform == null )
					continue;
				poses.Add( new EditorMoverPose
				{
					Transform = mover.movingTransform,
					Position = mover.movingTransform.position,
					Rotation = mover.movingTransform.rotation
				} );
			}
		}

		_editorCachedMoverPoses = poses.ToArray();

		if ( includeObjects )
		{
			List<EditorObjectActive> actives = new List<EditorObjectActive>( 32 );
			CollectObjectActives( actives );
			_editorCachedObjectActives = actives.ToArray();
		}
		else
		{
			_editorCachedObjectActives = null;
		}

		_editorHasCachedPose = true;
	}

	void CollectObjectActives( List<EditorObjectActive> actives )
	{
		if ( levels == null )
			return;

		HashSet<GameObject> seen = new HashSet<GameObject>();
		for ( int i = 0; i < levels.Length; i++ )
		{
			CoinHallExpansionLevel level = levels[ i ];
			if ( level == null )
				continue;
			AppendObjectActives( level.objectsToEnable, seen, actives );
			AppendObjectActives( level.objectsToDisable, seen, actives );
		}
	}

	static void AppendObjectActives( GameObject[] objects, HashSet<GameObject> seen, List<EditorObjectActive> actives )
	{
		if ( objects == null )
			return;

		for ( int i = 0; i < objects.Length; i++ )
		{
			GameObject go = objects[ i ];
			if ( go == null || !seen.Add( go ) )
				continue;
			actives.Add( new EditorObjectActive { Object = go, Active = go.activeSelf } );
		}
	}

	void SnapMoversToLevelWithUndo( int levelIndex )
	{
		if ( movers == null || levels == null )
			return;
		if ( levelIndex < 0 || levelIndex >= levels.Length )
			return;

		CoinHallExpansionLevel level = levels[ levelIndex ];
		if ( level == null || level.moverTargets == null )
			return;

		for ( int i = 0; i < movers.Length; i++ )
		{
			CoinHallMover mover = movers[ i ];
			if ( mover == null || mover.movingTransform == null )
				continue;
			if ( i >= level.moverTargets.Length )
				continue;

			Transform target = level.moverTargets[ i ];
			if ( target == null )
				continue;

			UnityEditor.Undo.RecordObject( mover.movingTransform, "Preview Coin Hall Level" );
			mover.movingTransform.SetPositionAndRotation( target.position, target.rotation );
			UnityEditor.EditorUtility.SetDirty( mover.movingTransform );
		}
	}

	void ApplyObjectStateThroughWithUndo( int levelIndex )
	{
		if ( levels == null )
			return;

		for ( int i = 0; i < levels.Length; i++ )
		{
			CoinHallExpansionLevel level = levels[ i ];
			if ( level == null )
				continue;

			bool reached = i <= levelIndex;
			SetActiveArrayWithUndo( level.objectsToEnable, reached );
			SetActiveArrayWithUndo( level.objectsToDisable, !reached );
		}
	}

	static void SetActiveArrayWithUndo( GameObject[] objects, bool active )
	{
		if ( objects == null )
			return;

		for ( int i = 0; i < objects.Length; i++ )
		{
			GameObject go = objects[ i ];
			if ( go == null || go.activeSelf == active )
				continue;
			UnityEditor.Undo.RecordObject( go, "Preview Coin Hall Objects" );
			go.SetActive( active );
			UnityEditor.EditorUtility.SetDirty( go );
		}
	}

	void OnValidate()
	{
		if ( string.IsNullOrEmpty( hallId ) )
			hallId = DefaultHallId;

		if ( expandDuration < 0.05f )
			expandDuration = 0.05f;

		if ( expandCurve == null || expandCurve.length == 0 )
			expandCurve = AnimationCurve.EaseInOut( 0f, 0f, 1f, 1f );
	}
#endif
}

[Serializable]
public class CoinHallMover
{
	[Tooltip( "Wall or roof root Transform that will move." )]
	public Transform movingTransform;
}

[Serializable]
public class CoinHallExpansionLevel
{
	[Tooltip( "Debug / event label for this level." )]
	public string id;

	[Tooltip( "All of these tables must be complete to unlock this level (leave empty for level 0)." )]
	public CoinDisplayTableInteractable[] requiredTables;

	[Tooltip( "Target poses for movers at this level. Index matches CoinHallProgression.movers." )]
	public Transform[] moverTargets;

	[Tooltip( "Enabled when this level is reached (new tables, piles, lights, map volumes)." )]
	public GameObject[] objectsToEnable;

	[Tooltip( "Disabled when this level is reached (scaffolding, blocker walls)." )]
	public GameObject[] objectsToDisable;

	[Tooltip( "Optional Feedbacks that replace the shared expandFeedbacks for this level." )]
	public Feedbacks levelFeedbacks;
}
