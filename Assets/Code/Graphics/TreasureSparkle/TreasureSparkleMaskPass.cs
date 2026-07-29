using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Builds <c>_TreasureSparkleMask</c> (R8) with depth-tested treasure redraws so
/// occluders in front of piles/coins block sparkles. Optional stencil resolve is off by default.
/// </summary>
public sealed class TreasureSparkleMaskPass : ScriptableRenderPass
{
	public static readonly int MaskTextureId = Shader.PropertyToID( "_TreasureSparkleMask" );
	public static readonly int MaskWriteValueId = Shader.PropertyToID( "_MaskWriteValue" );

	readonly ProfilingSampler _profilingSampler = new ProfilingSampler( "TreasureSparkleMask" );
	readonly Material _resolveMaterial;
	readonly Material _fallbackMaterial;
	readonly List<TreasureSparkleMaskRegistrar.RendererEntry> _entryScratch =
		new List<TreasureSparkleMaskRegistrar.RendererEntry>( 64 );
	readonly List<TreasureSparkleMaskRegistrar.IInstanceMaskSource> _instanceScratch =
		new List<TreasureSparkleMaskRegistrar.IInstanceMaskSource>( 8 );
	static MaterialPropertyBlock s_maskPropertyBlock;

	public TreasureSparkleMaskPass( Material resolveMaterial, Material fallbackMaterial )
	{
		_resolveMaterial = resolveMaterial;
		_fallbackMaterial = fallbackMaterial;
		renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
	}

	public override void RecordRenderGraph( RenderGraph renderGraph, ContextContainer frameData )
	{
		TreasureSparkleDefinition definition = TreasureSparkleRendererFeature.ActiveDefinition;
		if ( definition == null || !definition.enableSparkles )
			return;

		if ( _fallbackMaterial == null && ( _resolveMaterial == null || !definition.useStencilResolve ) )
			return;

		UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
		UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
		TreasureSparkleRendererFeature.FrameData sparkleData = frameData.GetOrCreate<TreasureSparkleRendererFeature.FrameData>();

		RenderTextureDescriptor cameraDesc = cameraData.cameraTargetDescriptor;
		int width = Mathf.Max( 1, cameraDesc.width );
		int height = Mathf.Max( 1, cameraDesc.height );

		TextureDesc textureDesc = new TextureDesc( width, height );
		textureDesc.colorFormat = GraphicsFormat.R8_UNorm;
		textureDesc.depthBufferBits = DepthBits.None;
		textureDesc.msaaSamples = MSAASamples.None;
		textureDesc.name = "_TreasureSparkleMask";
		textureDesc.filterMode = FilterMode.Bilinear;
		textureDesc.wrapMode = TextureWrapMode.Clamp;
		textureDesc.clearBuffer = true;
		textureDesc.clearColor = Color.black;

		TextureHandle maskTexture = renderGraph.CreateTexture( textureDesc );
		sparkleData.maskTexture = maskTexture;

		TreasureSparkleMaskRegistrar.CompactNulls();
		_entryScratch.Clear();
		_instanceScratch.Clear();

		if ( definition.useFallbackRegistrar )
		{
			TreasureSparkleMaskRegistrar.CollectEntries( definition, _entryScratch );
			TreasureSparkleMaskRegistrar.CollectInstanceSources( definition, _instanceScratch );
		}

		using ( var builder = renderGraph.AddRasterRenderPass<PassData>( "TreasureSparkleMask", out PassData passData, _profilingSampler ) )
		{
			passData.resolveMaterial = definition.useStencilResolve ? _resolveMaterial : null;
			passData.fallbackMaterial = _fallbackMaterial;
			passData.entries = _entryScratch;
			passData.instanceSources = _instanceScratch;

			builder.SetRenderAttachment( maskTexture, 0, AccessFlags.Write );
			builder.SetRenderAttachmentDepth( resourceData.activeDepthTexture, AccessFlags.Read );
			builder.SetGlobalTextureAfterPass( maskTexture, MaskTextureId );
			builder.AllowPassCulling( false );
			builder.SetRenderFunc( static ( PassData data, RasterGraphContext context ) =>
			{
				ExecutePass( context.cmd, data );
			} );
		}
	}

	static void ExecutePass( RasterCommandBuffer cmd, PassData data )
	{
		if ( cmd == null )
			return;

		// Optional: stencil resolve uses ZTest Always and can bleed through occluders.
		if ( data.resolveMaterial != null )
			cmd.DrawProcedural( Matrix4x4.identity, data.resolveMaterial, 0, MeshTopology.Triangles, 3, 1 );

		if ( data.fallbackMaterial == null )
			return;

		if ( data.entries != null )
		{
			for ( int i = 0; i < data.entries.Count; i++ )
			{
				TreasureSparkleMaskRegistrar.RendererEntry entry = data.entries[ i ];
				Renderer renderer = entry.Renderer;
				if ( renderer == null )
					continue;

				float maskValue = TreasureSparkleDefinition.MaskWriteValueForKind( entry.Kind );
				MeshFilter filter = renderer.GetComponent<MeshFilter>();
				if ( filter != null && filter.sharedMesh != null )
				{
					Mesh mesh = filter.sharedMesh;
					int submeshCount = Mathf.Max( 1, mesh.subMeshCount );
					Matrix4x4 matrix = renderer.localToWorldMatrix;
					for ( int submesh = 0; submesh < submeshCount; submesh++ )
						DrawMaskMesh( cmd, mesh, matrix, data.fallbackMaterial, submesh, maskValue );
					continue;
				}

				int materialSlots = renderer.sharedMaterials != null
					? Mathf.Max( 1, renderer.sharedMaterials.Length )
					: 1;
				for ( int submesh = 0; submesh < materialSlots; submesh++ )
					DrawMaskRenderer( cmd, renderer, data.fallbackMaterial, submesh, maskValue );
			}
		}

		if ( data.instanceSources == null )
			return;

		for ( int i = 0; i < data.instanceSources.Count; i++ )
		{
			TreasureSparkleMaskRegistrar.IInstanceMaskSource source = data.instanceSources[ i ];
			if ( source != null )
				source.DrawSparkleMask( cmd, data.fallbackMaterial );
		}
	}

	static void DrawMaskMesh(
		RasterCommandBuffer cmd,
		Mesh mesh,
		Matrix4x4 matrix,
		Material material,
		int submesh,
		float maskValue )
	{
		if ( cmd == null || mesh == null || material == null )
			return;

		if ( s_maskPropertyBlock == null )
			s_maskPropertyBlock = new MaterialPropertyBlock();

		s_maskPropertyBlock.SetFloat( MaskWriteValueId, maskValue );
		cmd.DrawMesh( mesh, matrix, material, submesh, 0, s_maskPropertyBlock );
	}

	static void DrawMaskRenderer(
		RasterCommandBuffer cmd,
		Renderer renderer,
		Material material,
		int submesh,
		float maskValue )
	{
		if ( cmd == null || renderer == null || material == null )
			return;

		Mesh mesh = null;
		Matrix4x4 matrix = renderer.localToWorldMatrix;
		MeshFilter filter = renderer.GetComponent<MeshFilter>();
		if ( filter != null && filter.sharedMesh != null )
			mesh = filter.sharedMesh;
		else if ( renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null )
			mesh = skinned.sharedMesh;

		if ( mesh != null )
		{
			DrawMaskMesh( cmd, mesh, matrix, material, submesh, maskValue );
			return;
		}

		if ( s_maskPropertyBlock == null )
			s_maskPropertyBlock = new MaterialPropertyBlock();

		s_maskPropertyBlock.SetFloat( MaskWriteValueId, maskValue );
		renderer.SetPropertyBlock( s_maskPropertyBlock );
		cmd.DrawRenderer( renderer, material, submesh, 0 );
	}

	sealed class PassData
	{
		public Material resolveMaterial;
		public Material fallbackMaterial;
		public List<TreasureSparkleMaskRegistrar.RendererEntry> entries;
		public List<TreasureSparkleMaskRegistrar.IInstanceMaskSource> instanceSources;
	}
}
