using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Registers scene-authored map colour boxes, label markers, and tutorial temp pins for <see cref="MapSystem"/> / <see cref="MapUI"/>.
/// </summary>
public static class MapOverlayRegistrar
{
	static readonly List<MapRegionVolume> ActiveVolumes = new List<MapRegionVolume>( 32 );
	static readonly List<MapLabelMarker> ActiveLabels = new List<MapLabelMarker>( 32 );
	static readonly List<TutorialMapMarker> ActiveTutorialMarkers = new List<TutorialMapMarker>( 16 );
	static readonly HashSet<string> HighlightedLabels = new HashSet<string>();
	static readonly HashSet<string> ActiveTempMarkerIds = new HashSet<string>();
	static int _revision;

	public static int Revision => _revision;

	public static void NotifyChanged()
	{
		_revision++;
	}

	public static void SetLabelHighlighted( string label, bool highlighted )
	{
		if ( string.IsNullOrEmpty( label ) )
			return;

		bool changed = highlighted ? HighlightedLabels.Add( label ) : HighlightedLabels.Remove( label );
		if ( changed )
			_revision++;
	}

	public static void ClearHighlightedLabels()
	{
		if ( HighlightedLabels.Count == 0 )
			return;
		HighlightedLabels.Clear();
		_revision++;
	}

	public static bool IsLabelHighlighted( string label )
	{
		return !string.IsNullOrEmpty( label ) && HighlightedLabels.Contains( label );
	}

	public static void SetTempMarkerActive( string markerId, bool active )
	{
		if ( string.IsNullOrEmpty( markerId ) )
			return;

		bool changed = active ? ActiveTempMarkerIds.Add( markerId ) : ActiveTempMarkerIds.Remove( markerId );
		if ( changed )
			_revision++;
	}

	public static void ClearTempMarkers()
	{
		if ( ActiveTempMarkerIds.Count == 0 )
			return;
		ActiveTempMarkerIds.Clear();
		_revision++;
	}

	public static bool IsTempMarkerActive( string markerId )
	{
		return !string.IsNullOrEmpty( markerId ) && ActiveTempMarkerIds.Contains( markerId );
	}

	public static void RegisterVolume( MapRegionVolume volume )
	{
		if ( volume == null )
			return;

		for ( int i = 0; i < ActiveVolumes.Count; i++ )
		{
			if ( ActiveVolumes[ i ] == volume )
				return;
		}

		ActiveVolumes.Add( volume );
		_revision++;
	}

	public static void UnregisterVolume( MapRegionVolume volume )
	{
		if ( volume == null )
			return;

		if ( ActiveVolumes.Remove( volume ) )
			_revision++;
	}

	public static void RegisterLabel( MapLabelMarker label )
	{
		if ( label == null )
			return;

		for ( int i = 0; i < ActiveLabels.Count; i++ )
		{
			if ( ActiveLabels[ i ] == label )
				return;
		}

		ActiveLabels.Add( label );
		_revision++;
	}

	public static void UnregisterLabel( MapLabelMarker label )
	{
		if ( label == null )
			return;

		if ( ActiveLabels.Remove( label ) )
			_revision++;
	}

	public static void RegisterTutorialMarker( TutorialMapMarker marker )
	{
		if ( marker == null )
			return;

		for ( int i = 0; i < ActiveTutorialMarkers.Count; i++ )
		{
			if ( ActiveTutorialMarkers[ i ] == marker )
				return;
		}

		ActiveTutorialMarkers.Add( marker );
		_revision++;
	}

	public static void UnregisterTutorialMarker( TutorialMapMarker marker )
	{
		if ( marker == null )
			return;

		if ( ActiveTutorialMarkers.Remove( marker ) )
			_revision++;
	}

	public static void CollectVolumes( List<MapRegionVolume> results )
	{
		results.Clear();
		for ( int i = 0; i < ActiveVolumes.Count; i++ )
		{
			MapRegionVolume volume = ActiveVolumes[ i ];
			if ( volume == null || !volume.isActiveAndEnabled )
				continue;
			results.Add( volume );
		}
	}

	public static void CollectLabels( List<MapLabelMarker> results )
	{
		results.Clear();
		for ( int i = 0; i < ActiveLabels.Count; i++ )
		{
			MapLabelMarker label = ActiveLabels[ i ];
			if ( label == null || !label.isActiveAndEnabled )
				continue;
			results.Add( label );
		}
	}

	public static void CollectActiveTutorialMarkers( List<TutorialMapMarker> results )
	{
		results.Clear();
		if ( ActiveTempMarkerIds.Count == 0 )
			return;

		for ( int i = 0; i < ActiveTutorialMarkers.Count; i++ )
		{
			TutorialMapMarker marker = ActiveTutorialMarkers[ i ];
			if ( marker == null || !marker.isActiveAndEnabled )
				continue;
			if ( !IsTempMarkerActive( marker.Id ) )
				continue;
			results.Add( marker );
		}
	}
}
