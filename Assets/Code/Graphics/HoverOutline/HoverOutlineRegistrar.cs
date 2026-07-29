using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Per-frame registration of hover-outline targets for the URP renderer feature.
/// </summary>
public static class HoverOutlineRegistrar
{
	static readonly List<Renderer> ActiveRenderers = new List<Renderer>( 8 );

	static HoverOutlineVisualSettings _settings;
	static bool _hasTarget;

	public static bool HasTarget => _hasTarget;

	public static IReadOnlyList<Renderer> Renderers => ActiveRenderers;

	public static HoverOutlineVisualSettings Settings => _settings;

	public static void Clear()
	{
		ActiveRenderers.Clear();
		_settings = null;
		_hasTarget = false;
	}

	public static void SetTarget( IReadOnlyList<Renderer> renderers, HoverOutlineVisualSettings settings )
	{
		ActiveRenderers.Clear();
		if ( renderers != null )
		{
			for ( int i = 0; i < renderers.Count; i++ )
			{
				Renderer renderer = renderers[ i ];
				if ( renderer == null || !renderer.gameObject.activeInHierarchy )
					continue;

				ActiveRenderers.Add( renderer );
			}
		}

		_settings = settings != null ? settings.Clone() : null;
		_settings.Validate();
		_hasTarget = ActiveRenderers.Count > 0 && _settings != null;
	}
}
