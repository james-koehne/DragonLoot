using System.Collections.Generic;
using System.Runtime.InteropServices;

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Fills a world-fixed 3D irradiance volume from registered <see cref="AreaLightVolume"/> boxes.
/// Re-dispatches compute only when volumes, bounds, or settings change.
/// </summary>
public sealed class AreaLightingFillPass : ScriptableRenderPass
{
	const string ClipmapName = "_AreaClipmap";

	static readonly int VolumesId = Shader.PropertyToID( "_AreaVolumes" );
	static readonly int ClipmapId = Shader.PropertyToID( ClipmapName );
	static readonly int ClipmapOriginId = Shader.PropertyToID( "_AreaClipmapOrigin" );
	static readonly int ClipmapVoxelSizeId = Shader.PropertyToID( "_AreaClipmapVoxelSize" );
	static readonly int BlendFillId = Shader.PropertyToID( "_AreaBlendFill" );
	static readonly int VolumeCountId = Shader.PropertyToID( "_AreaVolumeCount" );

	readonly ProfilingSampler _profilingSampler = new ProfilingSampler( "AreaLightingFill" );
	readonly ComputeShader _compute;
	readonly int _fillKernel;
	readonly List<AreaLightVolume> _volumeScratch = new List<AreaLightVolume>( 32 );
	readonly List<AreaLightingDefinition.GpuAreaVolume> _gpuScratch = new List<AreaLightingDefinition.GpuAreaVolume>( 32 );

	GraphicsBuffer _volumesBuffer;
	RenderTexture _worldClipmap;
	RTHandle _worldClipmapHandle;
	Texture3D _fallbackClipmap;
	Vector3Int _clipmapResolution = Vector3Int.zero;
	int _volumesCapacity;
	int _cachedVolumeRevision = int.MinValue;
	int _cachedBoundsRevision = int.MinValue;
	int _cachedSettingsVersion = int.MinValue;
	bool _hasFilledClipmap;

	public AreaLightingFillPass( ComputeShader compute )
	{
		_compute = compute;
		_fillKernel = compute != null ? compute.FindKernel( "Fill" ) : -1;
		renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
	}

	public bool IsReady => _compute != null && _fillKernel >= 0 && SystemInfo.supportsComputeShaders;

	public void Dispose()
	{
		ReleaseBuffers();
		ReleaseWorldClipmap();
		if ( _fallbackClipmap != null )
		{
			Object.DestroyImmediate( _fallbackClipmap );
			_fallbackClipmap = null;
		}
	}

	void ReleaseBuffers()
	{
		if ( _volumesBuffer != null )
		{
			_volumesBuffer.Dispose();
			_volumesBuffer = null;
		}

		_volumesCapacity = 0;
	}

	void ReleaseWorldClipmap()
	{
		if ( _worldClipmapHandle != null )
		{
			RTHandles.Release( _worldClipmapHandle );
			_worldClipmapHandle = null;
		}

		if ( _worldClipmap != null )
		{
			_worldClipmap.Release();
			Object.DestroyImmediate( _worldClipmap );
			_worldClipmap = null;
		}

		_clipmapResolution = Vector3Int.zero;
		_hasFilledClipmap = false;
	}

	void EnsureFallbackClipmap()
	{
		if ( _fallbackClipmap != null )
			return;

		_fallbackClipmap = new Texture3D( 1, 1, 1, TextureFormat.RGBAHalf, false );
		_fallbackClipmap.SetPixel( 0, 0, 0, new Color( 0f, 0f, 0f, 1f ) );
		_fallbackClipmap.Apply( false, true );
		_fallbackClipmap.name = "AreaAmbientFallback";
	}

	void EnsureVolumesBuffer( int count )
	{
		count = Mathf.Max( 1, count );
		if ( _volumesBuffer != null && _volumesCapacity >= count )
			return;

		if ( _volumesBuffer != null )
			_volumesBuffer.Dispose();

		_volumesBuffer = new GraphicsBuffer(
			GraphicsBuffer.Target.Structured,
			count,
			Marshal.SizeOf<AreaLightingDefinition.GpuAreaVolume>() );
		_volumesCapacity = count;
	}

	void EnsureWorldClipmap( Vector3Int resolution )
	{
		resolution.x = Mathf.Max( 4, resolution.x );
		resolution.y = Mathf.Max( 4, resolution.y );
		resolution.z = Mathf.Max( 4, resolution.z );

		if ( _worldClipmap != null
			&& _clipmapResolution.x == resolution.x
			&& _clipmapResolution.y == resolution.y
			&& _clipmapResolution.z == resolution.z )
			return;

		ReleaseWorldClipmap();

		_worldClipmap = new RenderTexture( resolution.x, resolution.y, 0, RenderTextureFormat.ARGBHalf )
		{
			dimension = TextureDimension.Tex3D,
			volumeDepth = resolution.z,
			enableRandomWrite = true,
			name = "AreaAmbientWorldVolume"
		};
		_worldClipmap.Create();
		_worldClipmapHandle = RTHandles.Alloc( _worldClipmap );
		_clipmapResolution = resolution;
	}

	bool IsDirty( AreaLightingDefinition definition )
	{
		if ( !_hasFilledClipmap || _worldClipmap == null )
			return true;

		if ( _cachedVolumeRevision != AreaLightVolumeRegistrar.Revision )
			return true;

		if ( _cachedBoundsRevision != AreaLightingWorldBounds.Revision )
			return true;

		if ( _cachedSettingsVersion != definition.SettingsVersion )
			return true;

		Vector3Int resolution = definition.worldVolumeResolution;
		if ( _clipmapResolution.x != resolution.x
			|| _clipmapResolution.y != resolution.y
			|| _clipmapResolution.z != resolution.z )
			return true;

		return false;
	}

	void MarkClean( AreaLightingDefinition definition )
	{
		_cachedVolumeRevision = AreaLightVolumeRegistrar.Revision;
		_cachedBoundsRevision = AreaLightingWorldBounds.Revision;
		_cachedSettingsVersion = definition.SettingsVersion;
		_hasFilledClipmap = true;
	}

	bool RebuildGpuVolumes( Bounds worldBounds, int maxVolumes )
	{
		int count = AreaLightVolumeRegistrar.CollectVolumes( worldBounds, _volumeScratch, maxVolumes );
		_gpuScratch.Clear();
		for ( int i = 0; i < count; i++ )
			_gpuScratch.Add( _volumeScratch[ i ].ToGpuVolume() );

		return count > 0;
	}

	public override void RecordRenderGraph( RenderGraph renderGraph, ContextContainer frameData )
	{
		AreaLightingDefinition definition = AreaLightingRendererFeature.ActiveDefinition;
		EnsureFallbackClipmap();

		if ( definition == null || !definition.enableAreaLighting || !IsReady )
		{
			definition?.ApplyDisabledGlobals( _fallbackClipmap );
			return;
		}

		AreaLightingWorldBounds worldBounds = AreaLightingWorldBounds.Active;
		if ( worldBounds == null || !worldBounds.isActiveAndEnabled )
		{
			definition.ApplyDisabledGlobals( _fallbackClipmap );
			return;
		}

		if ( !AreaLightVolumeRegistrar.HasVolumes )
		{
			definition.ApplyDisabledGlobals( _fallbackClipmap );
			return;
		}

		definition.Validate();

		Vector3 worldOrigin = worldBounds.WorldOrigin;
		Vector3 worldSize = worldBounds.WorldSize;
		Vector3Int resolution = definition.worldVolumeResolution;
		Bounds bounds = worldBounds.WorldBounds;

		if ( !RebuildGpuVolumes( bounds, definition.maxVolumes ) || _gpuScratch.Count == 0 )
		{
			definition.ApplyDisabledGlobals( _fallbackClipmap );
			return;
		}

		EnsureWorldClipmap( resolution );

		Vector3 voxelSize = new Vector3(
			worldSize.x / resolution.x,
			worldSize.y / resolution.y,
			worldSize.z / resolution.z );

		bool dirty = IsDirty( definition );
		definition.ApplyGlobals( _worldClipmap, worldOrigin, worldSize, 1f );

		if ( !dirty || _worldClipmapHandle == null )
			return;

		EnsureVolumesBuffer( _gpuScratch.Count );
		_volumesBuffer.SetData( _gpuScratch );

		TextureHandle clipmapHandle = renderGraph.ImportTexture( _worldClipmapHandle );

		using ( var builder = renderGraph.AddComputePass<PassData>( "AreaLightingFill", out PassData passData, _profilingSampler ) )
		{
			passData.compute = _compute;
			passData.kernel = _fillKernel;
			passData.volumesBuffer = _volumesBuffer;
			passData.clipmap = clipmapHandle;
			passData.definition = definition;
			passData.clipmapOrigin = worldOrigin;
			passData.voxelSize = voxelSize;
			passData.volumeCount = _gpuScratch.Count;
			passData.resolution = resolution;
			passData.fillPass = this;
			builder.UseTexture( clipmapHandle, AccessFlags.Write );
			builder.AllowPassCulling( false );
			builder.SetRenderFunc( static ( PassData data, ComputeGraphContext context ) =>
			{
				if ( !ExecutePass( context.cmd, data ) )
					return;

				if ( data.fillPass != null && data.definition != null )
					data.fillPass.MarkClean( data.definition );
			} );
		}
	}

	static bool ExecutePass( ComputeCommandBuffer cmd, PassData data )
	{
		if ( cmd == null || data.compute == null || !data.clipmap.IsValid() || data.volumesBuffer == null )
			return false;

		data.definition.ApplyToCompute( data.compute, data.kernel );
		cmd.SetComputeTextureParam( data.compute, data.kernel, ClipmapId, data.clipmap );
		cmd.SetComputeBufferParam( data.compute, data.kernel, VolumesId, data.volumesBuffer );
		cmd.SetComputeVectorParam( data.compute, ClipmapOriginId, data.clipmapOrigin );
		cmd.SetComputeVectorParam( data.compute, ClipmapVoxelSizeId, data.voxelSize );
		cmd.SetComputeFloatParam( data.compute, BlendFillId, data.definition.blendFill );
		cmd.SetComputeIntParam( data.compute, VolumeCountId, data.volumeCount );

		int groupsX = Mathf.Max( 1, Mathf.CeilToInt( data.resolution.x / 4f ) );
		int groupsY = Mathf.Max( 1, Mathf.CeilToInt( data.resolution.y / 4f ) );
		int groupsZ = Mathf.Max( 1, Mathf.CeilToInt( data.resolution.z / 4f ) );
		cmd.DispatchCompute( data.compute, data.kernel, groupsX, groupsY, groupsZ );
		return true;
	}

	sealed class PassData
	{
		public ComputeShader compute;
		public int kernel;
		public GraphicsBuffer volumesBuffer;
		public TextureHandle clipmap;
		public AreaLightingDefinition definition;
		public Vector3 clipmapOrigin;
		public Vector3 voxelSize;
		public int volumeCount;
		public Vector3Int resolution;
		public AreaLightingFillPass fillPass;
	}
}
