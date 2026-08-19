using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public sealed class HoverOutlineCompositePass : ScriptableRenderPass
{
	static readonly int ClipToViewId = Shader.PropertyToID( "_ClipToView" );
	static readonly int MaskTexelSizeId = Shader.PropertyToID( "_HoverOutlineMask_TexelSize" );
	static readonly int MaskTextureId = Shader.PropertyToID( "_HoverOutlineMask" );
	static readonly Vector4 IdentityScaleBias = new Vector4( 1f, 1f, 0f, 0f );

	readonly ProfilingSampler _profilingSampler;
	readonly Material _compositeMaterial;
	readonly bool _questChannel;

	public HoverOutlineCompositePass( Material compositeMaterial, bool questChannel )
	{
		_compositeMaterial = compositeMaterial;
		_questChannel = questChannel;
		_profilingSampler = new ProfilingSampler( questChannel ? "QuestOutlineComposite" : "HoverOutlineComposite" );
		renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
		ConfigureInput( ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal );
	}

	bool HasTarget => _questChannel ? QuestOutlineRegistrar.HasTarget : HoverOutlineRegistrar.HasTarget;

#if URP_COMPATIBILITY_MODE
#pragma warning disable 618, 672

	public override void Execute( ScriptableRenderContext context, ref RenderingData renderingData )
	{
		if ( !HasTarget || _compositeMaterial == null )
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
		if ( !HasTarget || _compositeMaterial == null )
			return;

		UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
		UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
		if ( !frameData.Contains<HoverOutlineRendererFeature.FrameData>() )
			return;

		HoverOutlineRendererFeature.FrameData outlineData = frameData.Get<HoverOutlineRendererFeature.FrameData>();
		TextureHandle mask = _questChannel ? outlineData.questMaskTexture : outlineData.maskTexture;
		if ( !mask.IsValid() )
			return;

		ApplySettings( cameraData.camera );

		TextureHandle color = resourceData.activeColorTexture;
		if ( !color.IsValid() )
			return;

		using ( var builder = renderGraph.AddRasterRenderPass<PassData>( _profilingSampler.name, out PassData passData, _profilingSampler ) )
		{
			passData.material = _compositeMaterial;
			passData.mask = mask;
			builder.UseTexture( mask, AccessFlags.Read );
			builder.SetRenderAttachment( color, 0, AccessFlags.ReadWrite );
			builder.AllowGlobalStateModification( true );
			builder.AllowPassCulling( false );
			builder.SetRenderFunc( static ( PassData data, RasterGraphContext context ) =>
			{
				if ( data.material == null )
					return;

				context.cmd.SetGlobalTexture( MaskTextureId, data.mask );
				Blitter.BlitTexture( context.cmd, IdentityScaleBias, data.material, 0 );
			} );
		}
	}

	void ApplySettings( Camera camera )
	{
		if ( _compositeMaterial == null )
			return;

		HoverOutlineVisualSettings settings = _questChannel
			? QuestOutlineRegistrar.Settings
			: HoverOutlineRegistrar.Settings;
		if ( settings == null )
			settings = _questChannel
				? HoverOutlineVisualSettings.DefaultQuest()
				: HoverOutlineVisualSettings.DefaultPickable();

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

	sealed class PassData
	{
		public Material material;
		public TextureHandle mask;
	}
}
