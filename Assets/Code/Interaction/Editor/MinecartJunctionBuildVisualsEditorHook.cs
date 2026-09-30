#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ensures junction build visuals refresh when tracks are toggled in the Hierarchy at edit time.
/// <see cref="MinecartTrack"/> OnEnable/OnDisable are not always reliable for Hierarchy checkbox
/// changes, so this listens to hierarchy/object-change events as a fallback.
/// </summary>
[InitializeOnLoad]
static class MinecartJunctionBuildVisualsEditorHook
{
	const double DebounceSeconds = 0.05;

	static double _nextRefreshTime;
	static bool _queued;

	static MinecartJunctionBuildVisualsEditorHook()
	{
		EditorApplication.hierarchyChanged += OnHierarchyChanged;
		ObjectChangeEvents.changesPublished += OnObjectChanges;
		EditorApplication.update += OnEditorUpdate;
		Undo.undoRedoPerformed += QueueRefresh;
	}

	static void OnHierarchyChanged()
	{
		QueueRefresh();
	}

	static void OnObjectChanges( ref ObjectChangeEventStream stream )
	{
		for ( int i = 0; i < stream.length; i++ )
		{
			ObjectChangeKind kind = stream.GetEventType( i );
			if ( kind == ObjectChangeKind.ChangeGameObjectOrComponentProperties
				|| kind == ObjectChangeKind.ChangeGameObjectStructure
				|| kind == ObjectChangeKind.ChangeGameObjectParent
				|| kind == ObjectChangeKind.CreateGameObjectHierarchy
				|| kind == ObjectChangeKind.DestroyGameObjectHierarchy
				|| kind == ObjectChangeKind.ChangeScene )
			{
				QueueRefresh();
				return;
			}
		}
	}

	static void QueueRefresh()
	{
		if ( Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode )
			return;

		_queued = true;
		_nextRefreshTime = EditorApplication.timeSinceStartup + DebounceSeconds;
	}

	static void OnEditorUpdate()
	{
		if ( !_queued )
			return;

		if ( Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode )
		{
			_queued = false;
			return;
		}

		if ( EditorApplication.timeSinceStartup < _nextRefreshTime )
			return;

		_queued = false;
		RefreshAllGraphs();
	}

	static void RefreshAllGraphs()
	{
		MinecartJunctionGraph[] graphs = Object.FindObjectsByType<MinecartJunctionGraph>(
			FindObjectsInactive.Exclude,
			FindObjectsSortMode.None );
		for ( int i = 0; i < graphs.Length; i++ )
		{
			MinecartJunctionGraph graph = graphs[ i ];
			if ( graph != null )
				graph.RefreshBuildVisuals();
		}
	}
}
#endif
