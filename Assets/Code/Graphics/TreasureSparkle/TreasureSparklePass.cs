using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Fullscreen world-space procedural sparkles, masked to treasure pixels.
/// </summary>
public sealed class TreasureSparklePass : ScriptableRenderPass
{
	static readonly int CameraPositionId = Shader.PropertyToID( "_SparkleCameraPosition" );
	static readonly int ViewProjId = Shader.PropertyToID( "_SparkleViewProj" );
	static readonly int InvViewProjId = Shader.PropertyToID( "_SparkleInvViewProj" );
	static readonly int MaskTexelSizeId = Shader.PropertyToID( "_TreasureSparkleMask_TexelSize" );

	readonly ProfilingSampler _profilingSampler = new ProfilingSampler( "TreasureSparkle" );
	readonly Material _sparkleMaterial;

	public TreasureSparklePass( Material sparkleMaterial )
	{
		_sparkleMaterial = sparkleMaterial;
		renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
		ConfigureInput( ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal );
	}

	public override void RecordRenderGraph( RenderGraph renderGraph, ContextContainer frameData )
	{
		TreasureSparkleDefinition definition = TreasureSparkleRendererFeature.ActiveDefinition;
		if ( definition == null || !definition.enableSparkles || _sparkleMaterial == null )
			return;

		if ( !frameData.Contains<TreasureSparkleRendererFeature.FrameData>() )
			return;

		TreasureSparkleRendererFeature.FrameData sparkleData = frameData.Get<TreasureSparkleRendererFeature.FrameData>();
		if ( !sparkleData.maskTexture.IsValid() )
			return;

		UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
		UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
		TextureHandle source = resourceData.activeColorTexture;
		if ( !source.IsValid() )
			return;

		TextureHandle depthTexture = resourceData.cameraDepthTexture;
		TextureHandle normalsTexture = resourceData.cameraNormalsTexture;

		ApplySettings( cameraData.camera, definition );

		bool halfRes = definition.halfResolution;
		int passIndex = 0;

		if ( !halfRes )
		{
			using ( var builder = renderGraph.AddRasterRenderPass<PassData>( "TreasureSparkle", out PassData passData, _profilingSampler ) )
			{
				passData.material = _sparkleMaterial;
				passData.passIndex = passIndex;
				passData.mask = sparkleData.maskTexture;
				builder.SetRenderAttachment( source, 0, AccessFlags.Write );
				builder.UseTexture( sparkleData.maskTexture, AccessFlags.Read );
				if ( depthTexture.IsValid() )
					builder.UseTexture( depthTexture, AccessFlags.Read );
				if ( normalsTexture.IsValid() )
					builder.UseTexture( normalsTexture, AccessFlags.Read );
				builder.AllowGlobalStateModification( true );
				builder.AllowPassCulling( false );
				builder.SetRenderFunc( static ( PassData data, RasterGraphContext context ) =>
				{
					if ( data.material == null )
						return;

					context.cmd.SetGlobalTexture( TreasureSparkleMaskPass.MaskTextureId, data.mask );
					context.cmd.DrawProcedural(
						Matrix4x4.identity,
						data.material,
						data.passIndex,
						MeshTopology.Triangles,
						3,
						1 );
				} );
			}

			return;
		}

		RenderTextureDescriptor cameraDesc = cameraData.cameraTargetDescriptor;
		int width = Mathf.Max( 1, cameraDesc.width / 2 );
		int height = Mathf.Max( 1, cameraDesc.height / 2 );

		TextureDesc halfDesc = new TextureDesc( width, height );
		halfDesc.colorFormat = cameraDesc.graphicsFormat;
		halfDesc.depthBufferBits = DepthBits.None;
		halfDesc.msaaSamples = MSAASamples.None;
		halfDesc.name = "_TreasureSparkleHalf";
		halfDesc.filterMode = FilterMode.Bilinear;
		halfDesc.wrapMode = TextureWrapMode.Clamp;
		halfDesc.clearBuffer = true;
		halfDesc.clearColor = Color.clear;

		TextureHandle halfTarget = renderGraph.CreateTexture( halfDesc );

		using ( var builder = renderGraph.AddRasterRenderPass<PassData>( "TreasureSparkleHalf", out PassData passData, _profilingSampler ) )
		{
			passData.material = _sparkleMaterial;
			passData.passIndex = 0;
			passData.mask = sparkleData.maskTexture;
			builder.SetRenderAttachment( halfTarget, 0, AccessFlags.Write );
			builder.UseTexture( sparkleData.maskTexture, AccessFlags.Read );
			if ( depthTexture.IsValid() )
				builder.UseTexture( depthTexture, AccessFlags.Read );
			if ( normalsTexture.IsValid() )
				builder.UseTexture( normalsTexture, AccessFlags.Read );
			builder.AllowGlobalStateModification( true );
			builder.AllowPassCulling( false );
			builder.SetRenderFunc( static ( PassData data, RasterGraphContext context ) =>
			{
				if ( data.material == null )
					return;

				context.cmd.SetGlobalTexture( TreasureSparkleMaskPass.MaskTextureId, data.mask );
				context.cmd.DrawProcedural(
					Matrix4x4.identity,
					data.material,
					data.passIndex,
					MeshTopology.Triangles,
					3,
					1 );
			} );
		}

		using ( var builder = renderGraph.AddRasterRenderPass<UpsamplePassData>( "TreasureSparkleUpsample", out UpsamplePassData passData, _profilingSampler ) )
		{
			passData.material = _sparkleMaterial;
			passData.source = halfTarget;
			builder.SetRenderAttachment( source, 0, AccessFlags.Write );
			builder.UseTexture( halfTarget, AccessFlags.Read );
			builder.AllowGlobalStateModification( true );
			builder.AllowPassCulling( false );
			builder.SetRenderFunc( static ( UpsamplePassData data, RasterGraphContext context ) =>
			{
				if ( data.material == null )
					return;

				Blitter.BlitTexture( context.cmd, data.source, Vector2.one, data.material, 1 );
			} );
		}
	}

	void ApplySettings( Camera camera, TreasureSparkleDefinition definition )
	{
		if ( camera == null || definition == null || _sparkleMaterial == null )
			return;

		definition.ApplyToMaterial( _sparkleMaterial );

		_sparkleMaterial.SetVector( CameraPositionId, camera.transform.position );

		Matrix4x4 view = camera.worldToCameraMatrix;
		Matrix4x4 proj = GL.GetGPUProjectionMatrix( camera.projectionMatrix, true );
		Matrix4x4 viewProj = proj * view;
		_sparkleMaterial.SetMatrix( ViewProjId, viewProj );
		_sparkleMaterial.SetMatrix( InvViewProjId, viewProj.inverse );

		float width = Mathf.Max( 1, camera.pixelWidth );
		float height = Mathf.Max( 1, camera.pixelHeight );
		_sparkleMaterial.SetVector( MaskTexelSizeId, new Vector4( 1f / width, 1f / height, width, height ) );
	}

	sealed class PassData
	{
		public Material material;
		public int passIndex;
		public TextureHandle mask;
	}

	sealed class UpsamplePassData
	{
		public Material material;
		public TextureHandle source;
	}
}
