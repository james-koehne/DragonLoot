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
		bool TryGetSparkleWorldBounds( out Bounds bounds );
		void DrawSparkleMask( UnityEngine.Rendering.RasterCommandBuffer cmd, Material maskMaterial );
	}

	public struct RendererEntry
	{
		public Renderer Renderer;
		public Mesh Mesh;
		public TreasureSparkleDefinition.SparkleSourceKind Kind;
	}

	public struct SparkleVolume
	{
		public Bounds Bounds;
		public TreasureSparkleDefinition.SparkleSourceKind Kind;
	}

	static readonly List<RendererEntry> ActiveEntries = new List<RendererEntry>( 64 );
	static readonly HashSet<int> ActiveIds = new HashSet<int>();
	static readonly List<IInstanceMaskSource> InstanceSources = new List<IInstanceMaskSource>( 8 );
	static int _revision;

	public static bool HasTargets => ActiveEntries.Count > 0 || InstanceSources.Count > 0;

	public static int Revision => _revision;

	public static IReadOnlyList<IInstanceMaskSource> Instances => InstanceSources;

	public static void Clear()
	{
		ActiveEntries.Clear();
		ActiveIds.Clear();
		InstanceSources.Clear();
		_revision++;
	}

	static Mesh ResolveMesh( Renderer renderer )
	{
		if ( renderer == null )
			return null;

		MeshFilter filter = renderer.GetComponent<MeshFilter>();
		if ( filter != null && filter.sharedMesh != null )
			return filter.sharedMesh;

		SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
		if ( skinned != null )
			return skinned.sharedMesh;

		return null;
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
					if ( updated.Mesh == null )
						updated.Mesh = ResolveMesh( renderer );
					ActiveEntries[ i ] = updated;
					_revision++;
					return;
				}
			}

			return;
		}

		ActiveEntries.Add( new RendererEntry
		{
			Renderer = renderer,
			Mesh = ResolveMesh( renderer ),
			Kind = kind
		} );
		_revision++;
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

		_revision++;
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
		_revision++;
	}

	public static void UnregisterInstanceSource( IInstanceMaskSource source )
	{
		if ( source == null )
			return;

		if ( !InstanceSources.Remove( source ) )
			return;

		_revision++;
	}

	public static void CollectEntries(
		TreasureSparkleDefinition definition,
		List<RendererEntry> dst )
	{
		if ( dst == null )
			return;

		dst.Clear();
		bool removed = false;
		for ( int i = ActiveEntries.Count - 1; i >= 0; i-- )
		{
			if ( ActiveEntries[ i ].Renderer != null )
				continue;

			ActiveEntries.RemoveAt( i );
			removed = true;
		}

		if ( removed )
		{
			ActiveIds.Clear();
			for ( int i = 0; i < ActiveEntries.Count; i++ )
			{
				Renderer r = ActiveEntries[ i ].Renderer;
				if ( r != null )
					ActiveIds.Add( r.GetInstanceID() );
			}

			_revision++;
		}

		for ( int i = 0; i < ActiveEntries.Count; i++ )
		{
			RendererEntry entry = ActiveEntries[ i ];
			Renderer renderer = entry.Renderer;
			if ( renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy )
				continue;

			if ( definition != null && !definition.Allows( entry.Kind ) )
				continue;

			if ( entry.Mesh == null )
			{
				entry.Mesh = ResolveMesh( renderer );
				ActiveEntries[ i ] = entry;
			}

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
		bool removed = false;
		for ( int i = InstanceSources.Count - 1; i >= 0; i-- )
		{
			IInstanceMaskSource source = InstanceSources[ i ];
			if ( source == null )
			{
				InstanceSources.RemoveAt( i );
				removed = true;
				continue;
			}

			if ( definition != null && !definition.Allows( source.MaskKind ) )
				continue;

			dst.Add( source );
		}

		if ( removed )
			_revision++;
	}

	public static void CollectVolumes(
		TreasureSparkleDefinition definition,
		Camera camera,
		List<SparkleVolume> dst,
		int maxVolumes )
	{
		if ( dst == null )
			return;

		dst.Clear();
		if ( maxVolumes <= 0 )
			return;

		Plane[] frustum = camera != null ? GeometryUtility.CalculateFrustumPlanes( camera ) : null;

		for ( int i = 0; i < ActiveEntries.Count; i++ )
		{
			if ( dst.Count >= maxVolumes )
				return;

			RendererEntry entry = ActiveEntries[ i ];
			Renderer renderer = entry.Renderer;
			if ( renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy )
				continue;

			if ( definition != null && !definition.Allows( entry.Kind ) )
				continue;

			Bounds bounds = renderer.bounds;
			if ( frustum != null && !GeometryUtility.TestPlanesAABB( frustum, bounds ) )
				continue;

			dst.Add( new SparkleVolume { Bounds = bounds, Kind = entry.Kind } );
		}

		for ( int i = 0; i < InstanceSources.Count; i++ )
		{
			if ( dst.Count >= maxVolumes )
				return;

			IInstanceMaskSource source = InstanceSources[ i ];
			if ( source == null )
				continue;

			if ( definition != null && !definition.Allows( source.MaskKind ) )
				continue;

			Bounds bounds;
			if ( !source.TryGetSparkleWorldBounds( out bounds ) )
				continue;

			if ( frustum != null && !GeometryUtility.TestPlanesAABB( frustum, bounds ) )
				continue;

			dst.Add( new SparkleVolume { Bounds = bounds, Kind = source.MaskKind } );
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

		return shaderName == "DragonLoot/Gold Pile Procedural"
			|| shaderName == "DragonLoot/Coin"
			|| shaderName == "DragonLoot/Coin Pile"
			|| shaderName == "DragonLoot/Coin Stack"
			|| shaderName == "DragonLoot/Coin Stack Multi"
			|| shaderName == "DragonLoot/Gem";
	}

	public static void CompactNulls()
	{
		bool removed = false;
		for ( int i = ActiveEntries.Count - 1; i >= 0; i-- )
		{
			if ( ActiveEntries[ i ].Renderer != null )
				continue;

			ActiveEntries.RemoveAt( i );
			removed = true;
		}

		if ( removed )
		{
			ActiveIds.Clear();
			for ( int i = 0; i < ActiveEntries.Count; i++ )
				ActiveIds.Add( ActiveEntries[ i ].Renderer.GetInstanceID() );
		}

		for ( int i = InstanceSources.Count - 1; i >= 0; i-- )
		{
			if ( InstanceSources[ i ] == null )
			{
				InstanceSources.RemoveAt( i );
				removed = true;
			}
		}

		if ( removed )
			_revision++;
	}
}
