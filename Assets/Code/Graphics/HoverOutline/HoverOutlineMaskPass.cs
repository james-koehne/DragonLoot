using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public sealed class HoverOutlineMaskPass : ScriptableRenderPass
{
	static readonly int MaskTextureId = Shader.PropertyToID( "_HoverOutlineMask" );

	readonly ProfilingSampler _profilingSampler = new ProfilingSampler( "HoverOutlineMask" );
	readonly Material _maskMaterial;
	readonly List<Renderer> _rendererScratch = new List<Renderer>( 8 );

	RTHandle _maskHandle;
	TextureHandle _maskTextureHandle;

	public HoverOutlineMaskPass( Material maskMaterial )
	{
		_maskMaterial = maskMaterial;
		renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
	}

	public void Dispose()
	{
		_maskHandle?.Release();
		_maskHandle = null;
	}

#if URP_COMPATIBILITY_MODE
#pragma warning disable 618, 672

	public override void OnCameraSetup( CommandBuffer cmd, ref RenderingData renderingData )
	{
		RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
		desc.depthBufferBits = 0;
		desc.msaaSamples = 1;
		desc.graphicsFormat = GraphicsFormat.R8_UNorm;
		RenderingUtils.ReAllocateIfNeeded( ref _maskHandle, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_HoverOutlineMask" );
		ConfigureTarget( _maskHandle );
		ConfigureClear( ClearFlag.Color, Color.black );
	}

	public override void Execute( ScriptableRenderContext context, ref RenderingData renderingData )
	{
		if ( !HoverOutlineRegistrar.HasTarget || _maskMaterial == null )
			return;

		CommandBuffer cmd = CommandBufferPool.Get();
		using ( new ProfilingScope( cmd, _profilingSampler ) )
		{
			DrawTargets( cmd, HoverOutlineRegistrar.Renderers, _maskMaterial );
			cmd.SetGlobalTexture( MaskTextureId, _maskHandle.nameID );
		}

		context.ExecuteCommandBuffer( cmd );
		cmd.Clear();
		CommandBufferPool.Release( cmd );
	}

#pragma warning restore 618, 672
#endif

	public override void RecordRenderGraph( RenderGraph renderGraph, ContextContainer frameData )
	{
		if ( !HoverOutlineRegistrar.HasTarget || _maskMaterial == null )
		{
			_maskTextureHandle = TextureHandle.nullHandle;
			return;
		}

		UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
		UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
		HoverOutlineRendererFeature.FrameData outlineData = frameData.GetOrCreate<HoverOutlineRendererFeature.FrameData>();

		RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
		desc.depthBufferBits = 0;
		desc.msaaSamples = 1;
		desc.graphicsFormat = GraphicsFormat.R8_UNorm;

		TextureDesc textureDesc = new TextureDesc( desc.width, desc.height );
		textureDesc.colorFormat = GraphicsFormat.R8_UNorm;
		textureDesc.depthBufferBits = DepthBits.None;
		textureDesc.msaaSamples = MSAASamples.None;
		textureDesc.name = "_HoverOutlineMask";
		textureDesc.filterMode = FilterMode.Point;
		textureDesc.wrapMode = TextureWrapMode.Clamp;
		textureDesc.clearBuffer = true;
		textureDesc.clearColor = Color.black;

		_maskTextureHandle = renderGraph.CreateTexture( textureDesc );
		outlineData.maskTexture = _maskTextureHandle;

		_rendererScratch.Clear();
		IReadOnlyList<Renderer> registered = HoverOutlineRegistrar.Renderers;
		for ( int i = 0; i < registered.Count; i++ )
		{
			Renderer renderer = registered[ i ];
			if ( renderer != null )
				_rendererScratch.Add( renderer );
		}

		using ( var builder = renderGraph.AddRasterRenderPass<PassData>( "HoverOutlineMask", out PassData passData, _profilingSampler ) )
		{
			passData.maskMaterial = _maskMaterial;
			passData.renderers = _rendererScratch;
			builder.SetRenderAttachment( _maskTextureHandle, 0, AccessFlags.Write );
			builder.SetRenderAttachmentDepth( resourceData.activeDepthTexture, AccessFlags.Read );
			builder.SetGlobalTextureAfterPass( _maskTextureHandle, MaskTextureId );
			builder.AllowPassCulling( false );
			builder.SetRenderFunc( static ( PassData data, RasterGraphContext context ) =>
			{
				DrawTargets( context.cmd, data.renderers, data.maskMaterial );
			} );
		}
	}

	static void DrawTargets( RasterCommandBuffer cmd, List<Renderer> renderers, Material maskMaterial )
	{
		if ( cmd == null || renderers == null || maskMaterial == null )
			return;

		for ( int i = 0; i < renderers.Count; i++ )
		{
			Renderer renderer = renderers[ i ];
			if ( renderer == null )
				continue;

			MeshFilter filter = renderer.GetComponent<MeshFilter>();
			if ( filter != null && filter.sharedMesh != null )
			{
				Mesh mesh = filter.sharedMesh;
				int submeshCount = Mathf.Max( 1, mesh.subMeshCount );
				Matrix4x4 matrix = renderer.localToWorldMatrix;
				for ( int submesh = 0; submesh < submeshCount; submesh++ )
					cmd.DrawMesh( mesh, matrix, maskMaterial, submesh, 0 );
				continue;
			}

			int materialSlots = renderer.sharedMaterials != null
				? Mathf.Max( 1, renderer.sharedMaterials.Length )
				: 1;
			for ( int submesh = 0; submesh < materialSlots; submesh++ )
				cmd.DrawRenderer( renderer, maskMaterial, submesh, 0 );
		}
	}

#if URP_COMPATIBILITY_MODE
	static void DrawTargets( CommandBuffer cmd, IReadOnlyList<Renderer> renderers, Material maskMaterial )
	{
		if ( cmd == null || renderers == null || maskMaterial == null )
			return;

		for ( int i = 0; i < renderers.Count; i++ )
		{
			Renderer renderer = renderers[ i ];
			if ( renderer == null )
				continue;

			MeshFilter filter = renderer.GetComponent<MeshFilter>();
			if ( filter != null && filter.sharedMesh != null )
			{
				Mesh mesh = filter.sharedMesh;
				int submeshCount = Mathf.Max( 1, mesh.subMeshCount );
				Matrix4x4 matrix = renderer.localToWorldMatrix;
				for ( int submesh = 0; submesh < submeshCount; submesh++ )
					cmd.DrawMesh( mesh, matrix, maskMaterial, submesh, 0 );
				continue;
			}

			int materialSlots = renderer.sharedMaterials != null
				? Mathf.Max( 1, renderer.sharedMaterials.Length )
				: 1;
			for ( int submesh = 0; submesh < materialSlots; submesh++ )
				cmd.DrawRenderer( renderer, maskMaterial, submesh, 0 );
		}
	}
#endif

	sealed class PassData
	{
		public Material maskMaterial;
		public List<Renderer> renderers;
	}
}
