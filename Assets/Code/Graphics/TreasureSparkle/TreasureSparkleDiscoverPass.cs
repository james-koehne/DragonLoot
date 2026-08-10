using System.Collections.Generic;
using System.Runtime.InteropServices;

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Walks world cells inside frustum-culled treasure volumes and appends glint instances.
/// </summary>
public sealed class TreasureSparkleDiscoverPass : ScriptableRenderPass
{
	const float CameraMoveRebuildMeters = 0.05f;
	const float CameraAngleRebuildDegrees = 1.5f;
	const int MaxVolumeCacheFrames = 8;

	static readonly int VolumesId = Shader.PropertyToID( "_SparkleVolumes" );
	static readonly int GlintsId = Shader.PropertyToID( "_SparkleGlints" );
	static readonly int GlintCountId = Shader.PropertyToID( "_SparkleGlintCount" );
	static readonly int IndirectArgsId = Shader.PropertyToID( "_SparkleIndirectArgs" );
	static readonly int MaskTexId = Shader.PropertyToID( "_TreasureSparkleMask" );
	static readonly int DepthTexId = Shader.PropertyToID( "_CameraDepthTexture" );
	static readonly int NormalsTexId = Shader.PropertyToID( "_CameraNormalsTexture" );
	static readonly int CameraPositionId = Shader.PropertyToID( "_SparkleCameraPosition" );
	static readonly int LightDirId = Shader.PropertyToID( "_SparkleLightDirWS" );
	static readonly int ViewProjId = Shader.PropertyToID( "_SparkleViewProj" );
	static readonly int InvViewProjId = Shader.PropertyToID( "_SparkleInvViewProj" );
	static readonly int ResolutionId = Shader.PropertyToID( "_SparkleResolution" );
	static readonly int TimeId = Shader.PropertyToID( "_SparkleTime" );
	static readonly int VolumeIndexId = Shader.PropertyToID( "_SparkleVolumeIndex" );
	static readonly int VolumeCellCountId = Shader.PropertyToID( "_SparkleVolumeCellCount" );
	static readonly int MaxGlintsId = Shader.PropertyToID( "_SparkleMaxGlints" );

	readonly ProfilingSampler _profilingSampler = new ProfilingSampler( "TreasureSparkleDiscover" );
	readonly ComputeShader _compute;
	readonly int _discoverKernel;
	readonly int _copyArgsKernel;
	readonly List<TreasureSparkleMaskRegistrar.SparkleVolume> _volumeScratch =
		new List<TreasureSparkleMaskRegistrar.SparkleVolume>( 64 );
	readonly List<TreasureSparkleDefinition.GpuVolume> _gpuVolumeScratch =
		new List<TreasureSparkleDefinition.GpuVolume>( 64 );

	GraphicsBuffer _volumesBuffer;
	GraphicsBuffer _glintsBuffer;
	GraphicsBuffer _glintCountBuffer;
	GraphicsBuffer _argsBuffer;
	int _volumesCapacity;
	int _glintsCapacity;
	readonly uint[] _zeroCount = { 0u };
	readonly uint[] _defaultArgs = { 6u, 0u, 0u, 0u };

	int _cachedRegistrarRevision = int.MinValue;
	int _cachedSettingsVersion = int.MinValue;
	float _cachedCellSize = -1f;
	int _cachedMaxVolumes = -1;
	int _cachedMaxDiscoverCells = -1;
	Vector3 _cachedCameraPos;
	Quaternion _cachedCameraRot = Quaternion.identity;
	int _cachedPixelWidth = -1;
	int _cachedPixelHeight = -1;
	bool _volumesValid;
	bool _volumesUploaded;
	int _cachedTotalCells;
	int _volumeCacheFrames;
	int _lastValidatedSettingsVersion = int.MinValue;

	public GraphicsBuffer GlintsBuffer => _glintsBuffer;
	public GraphicsBuffer ArgsBuffer => _argsBuffer;
	public bool HasGlintBuffers => _glintsBuffer != null && _argsBuffer != null;
	public bool IsReady => _compute != null && _discoverKernel >= 0 && _copyArgsKernel >= 0;

	public TreasureSparkleDiscoverPass( ComputeShader compute )
	{
		_compute = compute;
		_discoverKernel = compute != null ? compute.FindKernel( "Discover" ) : -1;
		_copyArgsKernel = compute != null ? compute.FindKernel( "CopyArgs" ) : -1;
		renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
		ConfigureInput( ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal );
	}

	public void Dispose()
	{
		ReleaseBuffers();
	}

	void ReleaseBuffers()
	{
		if ( _volumesBuffer != null )
		{
			_volumesBuffer.Dispose();
			_volumesBuffer = null;
		}

		if ( _glintsBuffer != null )
		{
			_glintsBuffer.Dispose();
			_glintsBuffer = null;
		}

		if ( _glintCountBuffer != null )
		{
			_glintCountBuffer.Dispose();
			_glintCountBuffer = null;
		}

		if ( _argsBuffer != null )
		{
			_argsBuffer.Dispose();
			_argsBuffer = null;
		}

		_volumesCapacity = 0;
		_glintsCapacity = 0;
		_volumesValid = false;
		_volumesUploaded = false;
	}

	void EnsureBuffers( int maxVolumes, int maxGlints )
	{
		maxVolumes = Mathf.Max( 1, maxVolumes );
		maxGlints = Mathf.Max( 1, maxGlints );

		if ( _volumesBuffer == null || _volumesCapacity < maxVolumes )
		{
			if ( _volumesBuffer != null )
				_volumesBuffer.Dispose();
			_volumesBuffer = new GraphicsBuffer(
				GraphicsBuffer.Target.Structured,
				maxVolumes,
				Marshal.SizeOf<TreasureSparkleDefinition.GpuVolume>() );
			_volumesCapacity = maxVolumes;
			_volumesUploaded = false;
		}

		if ( _glintsBuffer == null || _glintsCapacity < maxGlints )
		{
			if ( _glintsBuffer != null )
				_glintsBuffer.Dispose();
			_glintsBuffer = new GraphicsBuffer(
				GraphicsBuffer.Target.Structured,
				maxGlints,
				Marshal.SizeOf<TreasureSparkleDefinition.GlintInstance>() );
			_glintsCapacity = maxGlints;
		}

		if ( _glintCountBuffer == null )
		{
			_glintCountBuffer = new GraphicsBuffer( GraphicsBuffer.Target.Structured, 1, sizeof( uint ) );
		}

		if ( _argsBuffer == null )
		{
			_argsBuffer = new GraphicsBuffer( GraphicsBuffer.Target.IndirectArguments, 4, sizeof( uint ) );
		}
	}

	void EnsureValidated( TreasureSparkleDefinition definition )
	{
		if ( definition == null )
			return;

		if ( _lastValidatedSettingsVersion == definition.SettingsVersion )
			return;

		definition.Validate();
		_lastValidatedSettingsVersion = definition.SettingsVersion;
	}

	bool NeedsVolumeRebuild( TreasureSparkleDefinition definition, Camera camera )
	{
		if ( !_volumesValid )
			return true;

		_volumeCacheFrames++;
		if ( _volumeCacheFrames >= MaxVolumeCacheFrames )
			return true;

		if ( _cachedRegistrarRevision != TreasureSparkleMaskRegistrar.Revision )
			return true;

		if ( _cachedSettingsVersion != definition.SettingsVersion )
			return true;

		if ( !Mathf.Approximately( _cachedCellSize, definition.cellSize ) )
			return true;

		if ( _cachedMaxVolumes != definition.maxVolumes || _cachedMaxDiscoverCells != definition.maxDiscoverCells )
			return true;

		if ( _cachedPixelWidth != camera.pixelWidth || _cachedPixelHeight != camera.pixelHeight )
			return true;

		Vector3 pos = camera.transform.position;
		if ( ( pos - _cachedCameraPos ).sqrMagnitude > CameraMoveRebuildMeters * CameraMoveRebuildMeters )
			return true;

		float angle = Quaternion.Angle( camera.transform.rotation, _cachedCameraRot );
		if ( angle > CameraAngleRebuildDegrees )
			return true;

		return false;
	}

	void RebuildVolumes( TreasureSparkleDefinition definition, Camera camera )
	{
		_volumeScratch.Clear();
		TreasureSparkleMaskRegistrar.CollectVolumes( definition, camera, _volumeScratch, definition.maxVolumes );

		float cellSize = Mathf.Max( 0.02f, definition.cellSize );
		_gpuVolumeScratch.Clear();
		int totalCells = 0;
		int maxCells = definition.maxDiscoverCells;

		for ( int i = 0; i < _volumeScratch.Count; i++ )
		{
			TreasureSparkleMaskRegistrar.SparkleVolume volume = _volumeScratch[ i ];
			Bounds b = volume.Bounds;
			Vector3 min = b.min;
			Vector3 max = b.max;
			int cellMinX = Mathf.FloorToInt( min.x / cellSize );
			int cellMinY = Mathf.FloorToInt( min.y / cellSize );
			int cellMinZ = Mathf.FloorToInt( min.z / cellSize );
			int cellMaxX = Mathf.FloorToInt( max.x / cellSize );
			int cellMaxY = Mathf.FloorToInt( max.y / cellSize );
			int cellMaxZ = Mathf.FloorToInt( max.z / cellSize );
			int sizeX = Mathf.Max( 1, cellMaxX - cellMinX + 1 );
			int sizeY = Mathf.Max( 1, cellMaxY - cellMinY + 1 );
			int sizeZ = Mathf.Max( 1, cellMaxZ - cellMinZ + 1 );
			int cells = sizeX * sizeY * sizeZ;
			if ( totalCells + cells > maxCells )
			{
				int remaining = maxCells - totalCells;
				if ( remaining <= 0 )
					break;

				while ( cells > remaining && sizeY > 1 )
				{
					sizeY--;
					cells = sizeX * sizeY * sizeZ;
				}
				while ( cells > remaining && sizeZ > 1 )
				{
					sizeZ--;
					cells = sizeX * sizeY * sizeZ;
				}
				while ( cells > remaining && sizeX > 1 )
				{
					sizeX--;
					cells = sizeX * sizeY * sizeZ;
				}
				if ( cells > remaining )
					break;
			}

			_gpuVolumeScratch.Add( new TreasureSparkleDefinition.GpuVolume
			{
				CellMinX = cellMinX,
				CellMinY = cellMinY,
				CellMinZ = cellMinZ,
				SizeX = sizeX,
				SizeY = sizeY,
				SizeZ = sizeZ,
				FlatOffset = totalCells,
				KindMask = TreasureSparkleDefinition.MaskWriteValueForKind( volume.Kind )
			} );
			totalCells += sizeX * sizeY * sizeZ;
		}

		_cachedTotalCells = totalCells;
		_cachedRegistrarRevision = TreasureSparkleMaskRegistrar.Revision;
		_cachedSettingsVersion = definition.SettingsVersion;
		_cachedCellSize = definition.cellSize;
		_cachedMaxVolumes = definition.maxVolumes;
		_cachedMaxDiscoverCells = definition.maxDiscoverCells;
		_cachedCameraPos = camera.transform.position;
		_cachedCameraRot = camera.transform.rotation;
		_cachedPixelWidth = camera.pixelWidth;
		_cachedPixelHeight = camera.pixelHeight;
		_volumeCacheFrames = 0;
		_volumesValid = true;
		_volumesUploaded = false;
	}

	public override void RecordRenderGraph( RenderGraph renderGraph, ContextContainer frameData )
	{
		TreasureSparkleDefinition definition = TreasureSparkleRendererFeature.ActiveDefinition;
		if ( definition == null || !definition.enableSparkles || _compute == null )
			return;

		if ( !TreasureSparkleMaskRegistrar.HasTargets )
			return;

		if ( _discoverKernel < 0 || _copyArgsKernel < 0 )
			return;

		if ( !frameData.Contains<TreasureSparkleRendererFeature.FrameData>() )
			return;

		TreasureSparkleRendererFeature.FrameData sparkleData = frameData.Get<TreasureSparkleRendererFeature.FrameData>();
		if ( !sparkleData.maskTexture.IsValid() )
			return;

		UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
		UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
		Camera camera = cameraData.camera;
		if ( camera == null )
			return;

		EnsureValidated( definition );
		EnsureBuffers( definition.maxVolumes, definition.maxGlints );

		bool rebuilt = false;
		if ( NeedsVolumeRebuild( definition, camera ) )
		{
			RebuildVolumes( definition, camera );
			rebuilt = true;
		}

		if ( _gpuVolumeScratch.Count == 0 || _cachedTotalCells <= 0 )
		{
			_glintCountBuffer.SetData( _zeroCount );
			_argsBuffer.SetData( _defaultArgs );
			sparkleData.glintsBuffer = _glintsBuffer;
			sparkleData.argsBuffer = _argsBuffer;
			sparkleData.hasDiscoveredGlints = false;
			return;
		}

		if ( rebuilt || !_volumesUploaded )
		{
			_volumesBuffer.SetData( _gpuVolumeScratch );
			_volumesUploaded = true;
		}

		TextureHandle depthTexture = resourceData.cameraDepthTexture;
		TextureHandle normalsTexture = resourceData.cameraNormalsTexture;
		if ( !depthTexture.IsValid() || !normalsTexture.IsValid() )
		{
			sparkleData.glintsBuffer = _glintsBuffer;
			sparkleData.argsBuffer = _argsBuffer;
			sparkleData.hasDiscoveredGlints = false;
			return;
		}

		sparkleData.glintsBuffer = _glintsBuffer;
		sparkleData.argsBuffer = _argsBuffer;
		sparkleData.hasDiscoveredGlints = true;

		using ( var builder = renderGraph.AddComputePass<PassData>( "TreasureSparkleDiscover", out PassData passData, _profilingSampler ) )
		{
			passData.compute = _compute;
			passData.discoverKernel = _discoverKernel;
			passData.copyArgsKernel = _copyArgsKernel;
			passData.volumesBuffer = _volumesBuffer;
			passData.glintsBuffer = _glintsBuffer;
			passData.glintCountBuffer = _glintCountBuffer;
			passData.argsBuffer = _argsBuffer;
			passData.mask = sparkleData.maskTexture;
			passData.depth = depthTexture;
			passData.normals = normalsTexture;
			passData.volumeCount = _gpuVolumeScratch.Count;
			passData.totalCells = _cachedTotalCells;
			passData.maxGlints = definition.maxGlints;
			passData.definition = definition;
			passData.camera = camera;
			passData.gpuVolumes = _gpuVolumeScratch;
			passData.zeroCount = _zeroCount;
			passData.defaultArgs = _defaultArgs;

			builder.UseTexture( sparkleData.maskTexture, AccessFlags.Read );
			builder.UseTexture( depthTexture, AccessFlags.Read );
			builder.UseTexture( normalsTexture, AccessFlags.Read );
			builder.AllowPassCulling( false );
			builder.SetRenderFunc( static ( PassData data, ComputeGraphContext context ) =>
			{
				ExecutePass( context.cmd, data );
			} );
		}
	}

	static void ExecutePass( ComputeCommandBuffer cmd, PassData data )
	{
		if ( cmd == null )
			return;

		if ( data.glintCountBuffer != null && data.zeroCount != null )
			cmd.SetBufferData( data.glintCountBuffer, data.zeroCount );

		if ( data.argsBuffer != null && data.defaultArgs != null )
			cmd.SetBufferData( data.argsBuffer, data.defaultArgs );

		if ( data.compute == null || data.definition == null || data.camera == null )
			return;

		if ( data.gpuVolumes == null || data.gpuVolumes.Count == 0 )
			return;

		TreasureSparkleDefinition definition = data.definition;
		Camera camera = data.camera;
		definition.ApplyToComputeCommand( cmd, data.compute );

		Matrix4x4 view = camera.worldToCameraMatrix;
		Matrix4x4 proj = GL.GetGPUProjectionMatrix( camera.projectionMatrix, true );
		Matrix4x4 viewProj = proj * view;

		Vector3 lightDir = new Vector3( 0.35f, 0.85f, 0.35f );
		Light sun = RenderSettings.sun;
		if ( sun != null && sun.isActiveAndEnabled )
			lightDir = -sun.transform.forward;

		float tanHalfFovY = Mathf.Tan( camera.fieldOfView * 0.5f * Mathf.Deg2Rad );
		cmd.SetComputeVectorParam( data.compute, CameraPositionId, camera.transform.position );
		cmd.SetComputeVectorParam( data.compute, LightDirId, lightDir );
		cmd.SetComputeMatrixParam( data.compute, ViewProjId, viewProj );
		cmd.SetComputeMatrixParam( data.compute, InvViewProjId, viewProj.inverse );
		cmd.SetComputeVectorParam(
			data.compute,
			ResolutionId,
			new Vector4( camera.pixelWidth, camera.pixelHeight, tanHalfFovY, 0f ) );
		cmd.SetComputeFloatParam( data.compute, TimeId, Time.time );
		cmd.SetComputeIntParam( data.compute, MaxGlintsId, data.maxGlints );

		cmd.SetComputeBufferParam( data.compute, data.discoverKernel, VolumesId, data.volumesBuffer );
		cmd.SetComputeBufferParam( data.compute, data.discoverKernel, GlintsId, data.glintsBuffer );
		cmd.SetComputeBufferParam( data.compute, data.discoverKernel, GlintCountId, data.glintCountBuffer );
		cmd.SetComputeTextureParam( data.compute, data.discoverKernel, MaskTexId, data.mask );
		cmd.SetComputeTextureParam( data.compute, data.discoverKernel, DepthTexId, data.depth );
		cmd.SetComputeTextureParam( data.compute, data.discoverKernel, NormalsTexId, data.normals );

		for ( int i = 0; i < data.gpuVolumes.Count; i++ )
		{
			TreasureSparkleDefinition.GpuVolume volume = data.gpuVolumes[ i ];
			int cellCount = Mathf.Max( 1, volume.SizeX * volume.SizeY * volume.SizeZ );
			cmd.SetComputeIntParam( data.compute, VolumeIndexId, i );
			cmd.SetComputeIntParam( data.compute, VolumeCellCountId, cellCount );
			int groups = Mathf.Max( 1, Mathf.CeilToInt( cellCount / 64f ) );
			cmd.DispatchCompute( data.compute, data.discoverKernel, groups, 1, 1 );
		}

		cmd.SetComputeBufferParam( data.compute, data.copyArgsKernel, GlintCountId, data.glintCountBuffer );
		cmd.SetComputeBufferParam( data.compute, data.copyArgsKernel, IndirectArgsId, data.argsBuffer );
		cmd.SetComputeIntParam( data.compute, MaxGlintsId, data.maxGlints );
		cmd.DispatchCompute( data.compute, data.copyArgsKernel, 1, 1, 1 );
	}

	sealed class PassData
	{
		public ComputeShader compute;
		public int discoverKernel;
		public int copyArgsKernel;
		public GraphicsBuffer volumesBuffer;
		public GraphicsBuffer glintsBuffer;
		public GraphicsBuffer glintCountBuffer;
		public GraphicsBuffer argsBuffer;
		public TextureHandle mask;
		public TextureHandle depth;
		public TextureHandle normals;
		public int volumeCount;
		public int totalCells;
		public int maxGlints;
		public TreasureSparkleDefinition definition;
		public Camera camera;
		public List<TreasureSparkleDefinition.GpuVolume> gpuVolumes;
		public uint[] zeroCount;
		public uint[] defaultArgs;
	}
}
