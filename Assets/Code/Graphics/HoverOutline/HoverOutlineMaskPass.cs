using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public sealed class HoverOutlineMaskPass : ScriptableRenderPass
{
	static readonly int MaskTextureId = Shader.PropertyToID( "_HoverOutlineMask" );
	static readonly int QuestMaskTextureId = Shader.PropertyToID( "_QuestOutlineMask" );

	readonly ProfilingSampler _hoverSampler = new ProfilingSampler( "HoverOutlineMask" );
	readonly ProfilingSampler _questSampler = new ProfilingSampler( "QuestOutlineMask" );
	readonly Material _maskMaterial;
	readonly List<Renderer> _hoverScratch = new List<Renderer>( 8 );
	readonly List<Renderer> _questScratch = new List<Renderer>( 16 );

	RTHandle _maskHandle;
	RTHandle _questMaskHandle;

	public HoverOutlineMaskPass( Material maskMaterial )
	{
		_maskMaterial = maskMaterial;
		renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
	}

	public void Dispose()
	{
		_maskHandle?.Release();
		_maskHandle = null;
		_questMaskHandle?.Release();
		_questMaskHandle = null;
	}

	static bool HasAnyTarget => HoverOutlineRegistrar.HasTarget || QuestOutlineRegistrar.HasTarget;

#if URP_COMPATIBILITY_MODE
#pragma warning disable 618, 672

	public override void OnCameraSetup( CommandBuffer cmd, ref RenderingData renderingData )
	{
		RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
		desc.depthBufferBits = 0;
		desc.msaaSamples = 1;
		desc.graphicsFormat = GraphicsFormat.R8_UNorm;
		RenderingUtils.ReAllocateIfNeeded( ref _maskHandle, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_HoverOutlineMask" );
		RenderingUtils.ReAllocateIfNeeded( ref _questMaskHandle, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_QuestOutlineMask" );
		ConfigureTarget( _maskHandle );
		ConfigureClear( ClearFlag.Color, Color.black );
	}

	public override void Execute( ScriptableRenderContext context, ref RenderingData renderingData )
	{
		if ( !HasAnyTarget || _maskMaterial == null )
			return;

		CommandBuffer cmd = CommandBufferPool.Get();
		using ( new ProfilingScope( cmd, _hoverSampler ) )
		{
			cmd.SetRenderTarget( _maskHandle, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store );
			cmd.ClearRenderTarget( false, true, Color.black );
			if ( HoverOutlineRegistrar.HasTarget )
				DrawTargets( cmd, HoverOutlineRegistrar.Renderers, _maskMaterial );

			cmd.SetRenderTarget( _questMaskHandle, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store );
			cmd.ClearRenderTarget( false, true, Color.black );
			if ( QuestOutlineRegistrar.HasTarget )
				DrawTargets( cmd, QuestOutlineRegistrar.Renderers, _maskMaterial );

			cmd.SetGlobalTexture( MaskTextureId, _maskHandle.nameID );
			cmd.SetGlobalTexture( QuestMaskTextureId, _questMaskHandle.nameID );
		}

		context.ExecuteCommandBuffer( cmd );
		cmd.Clear();
		CommandBufferPool.Release( cmd );
	}

#pragma warning restore 618, 672
#endif

	public override void RecordRenderGraph( RenderGraph renderGraph, ContextContainer frameData )
	{
		if ( !HasAnyTarget || _maskMaterial == null )
			return;

		UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
		UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
		HoverOutlineRendererFeature.FrameData outlineData = frameData.GetOrCreate<HoverOutlineRendererFeature.FrameData>();

		TextureDesc textureDesc = CreateMaskDesc( cameraData, "_HoverOutlineMask" );
		TextureHandle hoverMask = renderGraph.CreateTexture( textureDesc );
		textureDesc.name = "_QuestOutlineMask";
		TextureHandle questMask = renderGraph.CreateTexture( textureDesc );
		outlineData.maskTexture = hoverMask;
		outlineData.questMaskTexture = questMask;

		_hoverScratch.Clear();
		if ( HoverOutlineRegistrar.HasTarget )
		{
			IReadOnlyList<Renderer> hover = HoverOutlineRegistrar.Renderers;
			for ( int i = 0; i < hover.Count; i++ )
			{
				if ( hover[ i ] != null )
					_hoverScratch.Add( hover[ i ] );
			}
		}

		_questScratch.Clear();
		if ( QuestOutlineRegistrar.HasTarget )
		{
			IReadOnlyList<Renderer> quest = QuestOutlineRegistrar.Renderers;
			for ( int i = 0; i < quest.Count; i++ )
			{
				if ( quest[ i ] != null )
					_questScratch.Add( quest[ i ] );
			}
		}

		RecordMaskPass( renderGraph, resourceData, hoverMask, _hoverScratch, MaskTextureId, "HoverOutlineMask", _hoverSampler );
		RecordMaskPass( renderGraph, resourceData, questMask, _questScratch, QuestMaskTextureId, "QuestOutlineMask", _questSampler );
	}

	void RecordMaskPass(
		RenderGraph renderGraph,
		UniversalResourceData resourceData,
		TextureHandle target,
		List<Renderer> renderers,
		int globalTextureId,
		string passName,
		ProfilingSampler sampler )
	{
		using ( var builder = renderGraph.AddRasterRenderPass<PassData>( passName, out PassData passData, sampler ) )
		{
			passData.maskMaterial = _maskMaterial;
			passData.renderers = renderers;
			builder.SetRenderAttachment( target, 0, AccessFlags.Write );
			builder.SetRenderAttachmentDepth( resourceData.activeDepthTexture, AccessFlags.Read );
			builder.SetGlobalTextureAfterPass( target, globalTextureId );
			builder.AllowPassCulling( false );
			builder.SetRenderFunc( static ( PassData data, RasterGraphContext context ) =>
			{
				DrawTargets( context.cmd, data.renderers, data.maskMaterial );
			} );
		}
	}

	static TextureDesc CreateMaskDesc( UniversalCameraData cameraData, string name )
	{
		RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
		TextureDesc textureDesc = new TextureDesc( desc.width, desc.height );
		textureDesc.colorFormat = GraphicsFormat.R8_UNorm;
		textureDesc.depthBufferBits = DepthBits.None;
		textureDesc.msaaSamples = MSAASamples.None;
		textureDesc.name = name;
		textureDesc.filterMode = FilterMode.Point;
		textureDesc.wrapMode = TextureWrapMode.Clamp;
		textureDesc.clearBuffer = true;
		textureDesc.clearColor = Color.black;
		return textureDesc;
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
