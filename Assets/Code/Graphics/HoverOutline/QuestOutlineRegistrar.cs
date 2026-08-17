using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Persistent quest-objective outline masks. Independent of hover/placement.
/// </summary>
public static class QuestOutlineRegistrar
{
	static readonly List<Renderer> ActiveRenderers = new List<Renderer>( 16 );

	static HoverOutlineVisualSettings _settings;

	public static bool HasTarget => ActiveRenderers.Count > 0 && _settings != null;

	public static IReadOnlyList<Renderer> Renderers => ActiveRenderers;

	public static HoverOutlineVisualSettings Settings => _settings;

	public static void Clear()
	{
		ActiveRenderers.Clear();
		_settings = null;
	}

	public static void SetTargets( IReadOnlyList<Renderer> renderers, HoverOutlineVisualSettings settings )
	{
		ActiveRenderers.Clear();
		if ( renderers != null )
		{
			for ( int i = 0; i < renderers.Count; i++ )
			{
				Renderer renderer = renderers[ i ];
				if ( renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy )
					continue;

				ActiveRenderers.Add( renderer );
			}
		}

		if ( settings != null )
		{
			_settings = settings.Clone();
			_settings.Validate();
		}
		else
			_settings = null;

		if ( ActiveRenderers.Count == 0 || _settings == null )
		{
			ActiveRenderers.Clear();
			_settings = null;
		}
	}
}
