using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

public sealed class HoverOutlineCompositePass : ScriptableRenderPass
{
	static readonly int ClipToViewId = Shader.PropertyToID( "_ClipToView" );
	static readonly int MaskTexelSizeId = Shader.PropertyToID( "_HoverOutlineMask_TexelSize" );

	readonly ProfilingSampler _profilingSampler = new ProfilingSampler( "HoverOutlineComposite" );
	readonly Material _compositeMaterial;

	public HoverOutlineCompositePass( Material compositeMaterial )
	{
		_compositeMaterial = compositeMaterial;
		renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
		ConfigureInput( ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal );
	}

#if URP_COMPATIBILITY_MODE
#pragma warning disable 618, 672

	public override void Execute( ScriptableRenderContext context, ref RenderingData renderingData )
	{
		if ( !HoverOutlineRegistrar.HasTarget || _compositeMaterial == null )
			return;

		Camera camera = renderingData.cameraData.camera;
		ApplySettings( camera );

		RTHandle source = renderingData.cameraData.renderer.cameraColorTargetHandle;
		CommandBuffer cmd = CommandBufferPool.Get();
		using ( new ProfilingScope( cmd, _profilingSampler ) )
		{
			Blitter.BlitCameraTexture( cmd, source, source, _compositeMaterial, 0 );
		}

		context.ExecuteCommandBuffer( cmd );
		cmd.Clear();
		CommandBufferPool.Release( cmd );
	}

#pragma warning restore 618, 672
#endif

	public override void RecordRenderGraph( RenderGraph renderGraph, ContextContainer frameData )
	{
		if ( !HoverOutlineRegistrar.HasTarget || _compositeMaterial == null )
			return;

		UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
		UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
		if ( !frameData.Contains<HoverOutlineRendererFeature.FrameData>() )
			return;

		HoverOutlineRendererFeature.FrameData outlineData = frameData.Get<HoverOutlineRendererFeature.FrameData>();

		if ( !outlineData.maskTexture.IsValid() )
			return;

		ApplySettings( cameraData.camera );

		TextureHandle source = resourceData.activeColorTexture;
		if ( !source.IsValid() )
			return;

		RenderGraphUtils.BlitMaterialParameters parameters = new RenderGraphUtils.BlitMaterialParameters( source, source, _compositeMaterial, 0 );
		renderGraph.AddBlitPass( parameters, "HoverOutlineComposite" );
	}

	void ApplySettings( Camera camera )
	{
		HoverOutlineVisualSettings settings = HoverOutlineRegistrar.Settings;
		if ( settings == null || _compositeMaterial == null )
			return;

		settings.ApplyToMaterial( _compositeMaterial );

		if ( camera != null )
		{
			Matrix4x4 clipToView = GL.GetGPUProjectionMatrix( camera.projectionMatrix, true ).inverse;
			_compositeMaterial.SetMatrix( ClipToViewId, clipToView );

			float width = Mathf.Max( 1, camera.pixelWidth );
			float height = Mathf.Max( 1, camera.pixelHeight );
			_compositeMaterial.SetVector( MaskTexelSizeId, new Vector4( 1f / width, 1f / height, width, height ) );
		}
	}
}
