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

	RTHandle _maskHandle;
	TextureHandle _maskTextureHandle;

	public HoverOutlineMaskPass( Material maskMaterial )
	{
		_maskMaterial = maskMaterial;
		renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
	}

	public RTHandle MaskHandle => _maskHandle;

	public TextureHandle MaskTextureHandle => _maskTextureHandle;

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
			IReadOnlyList<Renderer> renderers = HoverOutlineRegistrar.Renderers;
			for ( int i = 0; i < renderers.Count; i++ )
			{
				Renderer renderer = renderers[ i ];
				if ( renderer == null )
					continue;

				int submeshCount = 1;
				if ( renderer.sharedMaterials != null )
					submeshCount = Mathf.Max( 1, renderer.sharedMaterials.Length );

				for ( int submesh = 0; submesh < submeshCount; submesh++ )
					cmd.DrawRenderer( renderer, _maskMaterial, submesh, 0 );
			}

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

		using ( var builder = renderGraph.AddRasterRenderPass<PassData>( "HoverOutlineMask", out PassData passData, _profilingSampler ) )
		{
			passData.maskMaterial = _maskMaterial;
			passData.renderers = HoverOutlineRegistrar.Renderers;
			builder.SetRenderAttachment( _maskTextureHandle, 0, AccessFlags.Write );
			builder.SetRenderAttachmentDepth( resourceData.activeDepthTexture, AccessFlags.Read );
			builder.SetGlobalTextureAfterPass( _maskTextureHandle, MaskTextureId );
			builder.AllowPassCulling( false );
			builder.SetRenderFunc( static ( PassData data, RasterGraphContext context ) =>
			{
				for ( int i = 0; i < data.renderers.Count; i++ )
				{
					Renderer renderer = data.renderers[ i ];
					if ( renderer == null )
						continue;

					int submeshCount = 1;
					if ( renderer.sharedMaterials != null )
						submeshCount = Mathf.Max( 1, renderer.sharedMaterials.Length );

					for ( int submesh = 0; submesh < submeshCount; submesh++ )
						context.cmd.DrawRenderer( renderer, data.maskMaterial, submesh, 0 );
				}
			} );
		}
	}

	sealed class PassData
	{
		public Material maskMaterial;
		public IReadOnlyList<Renderer> renderers;
	}
}
