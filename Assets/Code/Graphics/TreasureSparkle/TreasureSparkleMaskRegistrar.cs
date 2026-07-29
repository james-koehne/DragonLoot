using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Registers MeshRenderers and GPU-instance sources for the treasure sparkle R8 mask.
/// Mask draws are depth-tested so occluders in front of treasure block sparkles.
/// </summary>
public static class TreasureSparkleMaskRegistrar
{
	public interface IInstanceMaskSource
	{
		TreasureSparkleDefinition.SparkleSourceKind MaskKind { get; }
		void DrawSparkleMask( UnityEngine.Rendering.RasterCommandBuffer cmd, Material maskMaterial );
	}

	public struct RendererEntry
	{
		public Renderer Renderer;
		public TreasureSparkleDefinition.SparkleSourceKind Kind;
	}

	static readonly List<RendererEntry> ActiveEntries = new List<RendererEntry>( 64 );
	static readonly HashSet<int> ActiveIds = new HashSet<int>();
	static readonly List<IInstanceMaskSource> InstanceSources = new List<IInstanceMaskSource>( 8 );

	public static bool HasTargets => ActiveEntries.Count > 0 || InstanceSources.Count > 0;

	public static IReadOnlyList<IInstanceMaskSource> Instances => InstanceSources;

	public static void Clear()
	{
		ActiveEntries.Clear();
		ActiveIds.Clear();
		InstanceSources.Clear();
	}

	public static void Register( Renderer renderer, TreasureSparkleDefinition.SparkleSourceKind kind )
	{
		if ( renderer == null )
			return;

		int id = renderer.GetInstanceID();
		if ( !ActiveIds.Add( id ) )
		{
			for ( int i = 0; i < ActiveEntries.Count; i++ )
			{
				if ( ActiveEntries[ i ].Renderer != null && ActiveEntries[ i ].Renderer.GetInstanceID() == id )
				{
					RendererEntry updated = ActiveEntries[ i ];
					updated.Kind = kind;
					ActiveEntries[ i ] = updated;
					return;
				}
			}

			return;
		}

		ActiveEntries.Add( new RendererEntry { Renderer = renderer, Kind = kind } );
	}

	public static void Register( IReadOnlyList<Renderer> renderers, TreasureSparkleDefinition.SparkleSourceKind kind )
	{
		if ( renderers == null )
			return;

		for ( int i = 0; i < renderers.Count; i++ )
			Register( renderers[ i ], kind );
	}

	public static void Unregister( Renderer renderer )
	{
		if ( renderer == null )
			return;

		int id = renderer.GetInstanceID();
		if ( !ActiveIds.Remove( id ) )
			return;

		for ( int i = ActiveEntries.Count - 1; i >= 0; i-- )
		{
			Renderer existing = ActiveEntries[ i ].Renderer;
			if ( existing == null || existing.GetInstanceID() == id )
				ActiveEntries.RemoveAt( i );
		}
	}

	public static void Unregister( IReadOnlyList<Renderer> renderers )
	{
		if ( renderers == null )
			return;

		for ( int i = 0; i < renderers.Count; i++ )
			Unregister( renderers[ i ] );
	}

	public static void RegisterInstanceSource( IInstanceMaskSource source )
	{
		if ( source == null )
			return;

		if ( InstanceSources.Contains( source ) )
			return;

		InstanceSources.Add( source );
	}

	public static void UnregisterInstanceSource( IInstanceMaskSource source )
	{
		if ( source == null )
			return;

		InstanceSources.Remove( source );
	}

	public static void CollectEntries(
		TreasureSparkleDefinition definition,
		List<RendererEntry> dst )
	{
		if ( dst == null )
			return;

		dst.Clear();
		for ( int i = 0; i < ActiveEntries.Count; i++ )
		{
			RendererEntry entry = ActiveEntries[ i ];
			Renderer renderer = entry.Renderer;
			if ( renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy )
				continue;

			if ( definition != null && !definition.Allows( entry.Kind ) )
				continue;

			dst.Add( entry );
		}
	}

	public static void CollectRenderers(
		TreasureSparkleDefinition definition,
		List<Renderer> dst )
	{
		if ( dst == null )
			return;

		dst.Clear();
		for ( int i = 0; i < ActiveEntries.Count; i++ )
		{
			RendererEntry entry = ActiveEntries[ i ];
			Renderer renderer = entry.Renderer;
			if ( renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy )
				continue;

			if ( definition != null && !definition.Allows( entry.Kind ) )
				continue;

			dst.Add( renderer );
		}
	}

	public static void CollectInstanceSources(
		TreasureSparkleDefinition definition,
		List<IInstanceMaskSource> dst )
	{
		if ( dst == null )
			return;

		dst.Clear();
		for ( int i = 0; i < InstanceSources.Count; i++ )
		{
			IInstanceMaskSource source = InstanceSources[ i ];
			if ( source == null )
				continue;

			if ( definition != null && !definition.Allows( source.MaskKind ) )
				continue;

			dst.Add( source );
		}
	}

	public static bool UsesBuiltInTreasureStencil( Renderer renderer )
	{
		if ( renderer == null )
			return false;

		Material[] materials = renderer.sharedMaterials;
		if ( materials == null || materials.Length == 0 )
			return false;

		for ( int i = 0; i < materials.Length; i++ )
		{
			Material material = materials[ i ];
			if ( material == null || material.shader == null )
				return false;

			if ( !IsTreasureStencilShader( material.shader.name ) )
				return false;
		}

		return true;
	}

	public static bool IsTreasureStencilShader( string shaderName )
	{
		if ( string.IsNullOrEmpty( shaderName ) )
			return false;

		return shaderName == "DragonLoot/Gold Pile"
			|| shaderName == "DragonLoot/Gold Pile Stylized"
			|| shaderName == "DragonLoot/Gold Pile Procedural"
			|| shaderName == "DragonLoot/Coin"
			|| shaderName == "DragonLoot/Coin Pile"
			|| shaderName == "DragonLoot/Coin Stack"
			|| shaderName == "DragonLoot/Coin Stack Multi"
			|| shaderName == "DragonLoot/Gem";
	}

	public static void CompactNulls()
	{
		for ( int i = ActiveEntries.Count - 1; i >= 0; i-- )
		{
			if ( ActiveEntries[ i ].Renderer != null )
				continue;

			ActiveEntries.RemoveAt( i );
		}

		ActiveIds.Clear();
		for ( int i = 0; i < ActiveEntries.Count; i++ )
			ActiveIds.Add( ActiveEntries[ i ].Renderer.GetInstanceID() );

		for ( int i = InstanceSources.Count - 1; i >= 0; i-- )
		{
			if ( InstanceSources[ i ] == null )
				InstanceSources.RemoveAt( i );
		}
	}
}
