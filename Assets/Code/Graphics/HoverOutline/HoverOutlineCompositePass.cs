using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

public sealed class HoverOutlineCompositePass : ScriptableRenderPass
{
	static readonly int ClipToViewId = Shader.PropertyToID( "_ClipToView" );
	static readonly int MaskTexelSizeId = Shader.PropertyToID( "_HoverOutlineMask_TexelSize" );
	static readonly int MaskTextureId = Shader.PropertyToID( "_HoverOutlineMask" );
	static readonly int QuestMaskTextureId = Shader.PropertyToID( "_QuestOutlineMask" );

	readonly ProfilingSampler _profilingSampler = new ProfilingSampler( "HoverOutlineComposite" );
	readonly Material _compositeMaterial;

	public HoverOutlineCompositePass( Material compositeMaterial )
	{
		_compositeMaterial = compositeMaterial;
		renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
		ConfigureInput( ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal );
	}

	static bool HasAnyTarget => HoverOutlineRegistrar.HasTarget || QuestOutlineRegistrar.HasTarget;

#if URP_COMPATIBILITY_MODE
#pragma warning disable 618, 672

	public override void Execute( ScriptableRenderContext context, ref RenderingData renderingData )
	{
		if ( !HasAnyTarget || _compositeMaterial == null )
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
		if ( !HasAnyTarget || _compositeMaterial == null )
			return;

		UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
		UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
		if ( !frameData.Contains<HoverOutlineRendererFeature.FrameData>() )
			return;

		HoverOutlineRendererFeature.FrameData outlineData = frameData.Get<HoverOutlineRendererFeature.FrameData>();
		if ( !outlineData.maskTexture.IsValid() || !outlineData.questMaskTexture.IsValid() )
			return;

		ApplySettings( cameraData.camera );

		TextureHandle source = resourceData.activeColorTexture;
		if ( !source.IsValid() )
			return;

		RenderGraphUtils.BlitMaterialParameters parameters = new RenderGraphUtils.BlitMaterialParameters( source, source, _compositeMaterial, 0 );
		IBaseRenderGraphBuilder blitBuilder = renderGraph.AddBlitPass( parameters, "HoverOutlineComposite", true );
		try
		{
			blitBuilder.UseGlobalTexture( MaskTextureId );
			blitBuilder.UseGlobalTexture( QuestMaskTextureId );
		}
		finally
		{
			blitBuilder.Dispose();
		}
	}

	void ApplySettings( Camera camera )
	{
		if ( _compositeMaterial == null )
			return;

		HoverOutlineVisualSettings hover = HoverOutlineRegistrar.Settings;
		if ( hover != null )
			hover.ApplyToMaterial( _compositeMaterial );
		else
			HoverOutlineVisualSettings.DefaultPickable().ApplyToMaterial( _compositeMaterial );

		HoverOutlineVisualSettings quest = QuestOutlineRegistrar.Settings;
		if ( quest != null )
			quest.ApplyQuestToMaterial( _compositeMaterial );
		else
			HoverOutlineVisualSettings.DefaultQuest().ApplyQuestToMaterial( _compositeMaterial );

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
