using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Per-frame registration of hover-outline targets for the URP renderer feature.
/// Pickable hover and stack-volume placement share one active target set.
/// Ownership tags prevent placement ClearPreview from wiping pickable outlines.
/// </summary>
public static class HoverOutlineRegistrar
{
	public enum Owner
	{
		None = 0,
		Pickable = 1,
		StackVolume = 2
	}

	static readonly List<Renderer> ActiveRenderers = new List<Renderer>( 8 );

	static HoverOutlineVisualSettings _settings;
	static Owner _owner;
	static int _sourceId;

	public static bool HasTarget => _owner != Owner.None && ActiveRenderers.Count > 0 && _settings != null;

	public static IReadOnlyList<Renderer> Renderers => ActiveRenderers;

	public static HoverOutlineVisualSettings Settings => _settings;

	public static Owner CurrentOwner => _owner;

	public static void Clear()
	{
		ActiveRenderers.Clear();
		_settings = null;
		_owner = Owner.None;
		_sourceId = 0;
	}

	public static void ClearIfOwner( Owner owner )
	{
		ClearIfOwner( owner, 0 );
	}

	public static void ClearIfOwner( Owner owner, int sourceId )
	{
		if ( _owner != owner )
			return;
		if ( sourceId != 0 && _sourceId != 0 && sourceId != _sourceId )
			return;
		if ( sourceId == 0 && _sourceId != 0 )
			return;

		Clear();
	}

	public static void SetTarget( Owner owner, IReadOnlyList<Renderer> renderers, HoverOutlineVisualSettings settings )
	{
		SetTarget( owner, renderers, settings, 0 );
	}

	public static void SetTarget( Owner owner, IReadOnlyList<Renderer> renderers, HoverOutlineVisualSettings settings, int sourceId )
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

		_owner = ActiveRenderers.Count > 0 && _settings != null ? owner : Owner.None;
		_sourceId = _owner != Owner.None ? sourceId : 0;
		if ( _owner == Owner.None )
		{
			ActiveRenderers.Clear();
			_settings = null;
		}
	}
}
