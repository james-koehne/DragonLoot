using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Publishes tutorial / contextual HUD state for objective UI, compass, world marker, and outlines.
/// Intro world events do not call this; future tutorial systems will.
/// </summary>
public static class TutorialHud
{
	static readonly List<Transform> OutlineRoots = new List<Transform>();

	public static void Publish( TutorialHudChangedEvent evt )
	{
		EventBus.Publish( evt );
	}

	public static void Clear()
	{
		OutlineRoots.Clear();
		EventBus.Publish( new TutorialHudChangedEvent
		{
			Title = string.Empty,
			ObjectiveText = string.Empty,
			Rows = null,
			HasMarker = false,
			MarkerWorldPosition = Vector3.zero,
			Cleared = true
		} );
	}

	public static void SetOutlineRoots( IList<Transform> roots )
	{
		OutlineRoots.Clear();
		if ( roots == null )
			return;

		for ( int i = 0; i < roots.Count; i++ )
		{
			Transform root = roots[ i ];
			if ( root == null )
				continue;
			if ( OutlineRoots.Contains( root ) )
				continue;
			OutlineRoots.Add( root );
		}
	}

	public static void ClearOutlineRoots()
	{
		OutlineRoots.Clear();
	}

	public static void CollectOutlineRoots( System.Action<Transform> onRoot )
	{
		if ( onRoot == null )
			return;

		for ( int i = 0; i < OutlineRoots.Count; i++ )
		{
			Transform root = OutlineRoots[ i ];
			if ( root != null )
				onRoot( root );
		}
	}

	public static bool HasOutlineRoots => OutlineRoots.Count > 0;

	/// <summary>
	/// Convenience: show a simple title + objective text with optional marker target id.
	/// </summary>
	public static void ShowSimple( string title, string objectiveText, string markerTargetId = null )
	{
		bool hasMarker = false;
		Vector3 markerPos = Vector3.zero;
		if ( !string.IsNullOrEmpty( markerTargetId ) &&
		     EventTargetRegistry.TryGetMarkerTransform( markerTargetId, out Transform marker ) &&
		     marker != null )
		{
			hasMarker = true;
			markerPos = marker.position;
		}

		if ( !string.IsNullOrEmpty( markerTargetId ) &&
		     EventTargetRegistry.TryGetTarget( markerTargetId, out QuestTarget target ) &&
		     target != null )
		{
			SetOutlineRoots( new[] { target.ResolveOutlineRoot() } );
		}
		else
		{
			ClearOutlineRoots();
		}

		Publish( new TutorialHudChangedEvent
		{
			Title = title ?? string.Empty,
			ObjectiveText = objectiveText ?? string.Empty,
			Rows = null,
			HasMarker = hasMarker,
			MarkerWorldPosition = markerPos,
			Cleared = false
		} );
	}
}
