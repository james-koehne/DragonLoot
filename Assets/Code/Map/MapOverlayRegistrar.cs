using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Registers scene-authored map colour boxes and label markers for <see cref="MapSystem"/>.
/// </summary>
public static class MapOverlayRegistrar
{
	static readonly List<MapRegionVolume> ActiveVolumes = new List<MapRegionVolume>( 32 );
	static readonly List<MapLabelMarker> ActiveLabels = new List<MapLabelMarker>( 32 );
	static readonly HashSet<string> HighlightedLabels = new HashSet<string>();
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
}
