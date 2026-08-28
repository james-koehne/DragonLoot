using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Registers <see cref="AreaLightVolume"/> instances for the area-ambient clipmap fill pass.
/// </summary>
public static class AreaLightVolumeRegistrar
{
	static readonly List<AreaLightVolume> ActiveVolumes = new List<AreaLightVolume>( 32 );
	static int _revision;

	public static bool HasVolumes => ActiveVolumes.Count > 0;

	public static int Revision => _revision;

	public static void NotifyChanged()
	{
		_revision++;
	}

	public static void Register( AreaLightVolume volume )
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

	public static void Unregister( AreaLightVolume volume )
	{
		if ( volume == null )
			return;

		if ( ActiveVolumes.Remove( volume ) )
			_revision++;
	}

	public static int CollectVolumes( Bounds clipmapBounds, List<AreaLightVolume> results, int maxCount )
	{
		results.Clear();
		if ( maxCount <= 0 )
			return 0;

		for ( int i = 0; i < ActiveVolumes.Count; i++ )
		{
			AreaLightVolume volume = ActiveVolumes[ i ];
			if ( volume == null || !volume.isActiveAndEnabled )
				continue;

			if ( !volume.TryGetWorldBounds( out Bounds bounds ) )
				continue;

			if ( !clipmapBounds.Intersects( bounds ) )
				continue;

			results.Add( volume );
			if ( results.Count >= maxCount )
				break;
		}

		return results.Count;
	}
}
