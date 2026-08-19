using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public sealed class HoverOutlineMaskPass : ScriptableRenderPass
{
	static readonly int MaskTextureId = Shader.PropertyToID( "_HoverOutlineMask" );

	readonly ProfilingSampler _profilingSampler;
	readonly Material _maskMaterial;
	readonly bool _questChannel;
	readonly List<MaskDraw> _drawScratch = new List<MaskDraw>( 8 );

	RTHandle _maskHandle;
	TextureHandle _maskTextureHandle;

	struct MaskDraw
	{
		public Renderer Renderer;
		public Mesh Mesh;
		public Matrix4x4 Matrix;
		public MaterialPropertyBlock Properties;
	}

	public HoverOutlineMaskPass( Material maskMaterial, bool questChannel )
	{
		_maskMaterial = maskMaterial;
		_questChannel = questChannel;
		_profilingSampler = new ProfilingSampler( questChannel ? "QuestOutlineMask" : "HoverOutlineMask" );
		renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
	}

	public void Dispose()
	{
		_maskHandle?.Release();
		_maskHandle = null;
	}

	bool HasTarget => _questChannel ? QuestOutlineRegistrar.HasTarget : HoverOutlineRegistrar.HasTarget;

	IReadOnlyList<Renderer> ActiveRenderers =>
		_questChannel ? QuestOutlineRegistrar.Renderers : HoverOutlineRegistrar.Renderers;

#if URP_COMPATIBILITY_MODE
#pragma warning disable 618, 672

	public override void OnCameraSetup( CommandBuffer cmd, ref RenderingData renderingData )
	{
		RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
		desc.depthBufferBits = 0;
		desc.msaaSamples = 1;
		desc.graphicsFormat = GraphicsFormat.R8_UNorm;
		string name = _questChannel ? "_QuestOutlineMask" : "_HoverOutlineMask";
		RenderingUtils.ReAllocateIfNeeded( ref _maskHandle, desc, FilterMode.Point, TextureWrapMode.Clamp, name: name );
		ConfigureTarget( _maskHandle );
		ConfigureClear( ClearFlag.Color, Color.black );
	}

	public override void Execute( ScriptableRenderContext context, ref RenderingData renderingData )
	{
		if ( !HasTarget || _maskMaterial == null )
			return;

		BuildDrawList( ActiveRenderers, _drawScratch );

		CommandBuffer cmd = CommandBufferPool.Get();
		using ( new ProfilingScope( cmd, _profilingSampler ) )
		{
			DrawTargets( cmd, _drawScratch, _maskMaterial );
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
		if ( !HasTarget || _maskMaterial == null )
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
		textureDesc.name = _questChannel ? "_QuestOutlineMask" : "_HoverOutlineMask";
		textureDesc.filterMode = FilterMode.Point;
		textureDesc.wrapMode = TextureWrapMode.Clamp;
		textureDesc.clearBuffer = true;
		textureDesc.clearColor = Color.black;

		_maskTextureHandle = renderGraph.CreateTexture( textureDesc );
		if ( _questChannel )
			outlineData.questMaskTexture = _maskTextureHandle;
		else
			outlineData.maskTexture = _maskTextureHandle;

		BuildDrawList( ActiveRenderers, _drawScratch );

		using ( var builder = renderGraph.AddRasterRenderPass<PassData>( _profilingSampler.name, out PassData passData, _profilingSampler ) )
		{
			passData.maskMaterial = _maskMaterial;
			passData.draws = _drawScratch;
			builder.SetRenderAttachment( _maskTextureHandle, 0, AccessFlags.Write );
			builder.SetRenderAttachmentDepth( resourceData.activeDepthTexture, AccessFlags.Read );
			builder.SetGlobalTextureAfterPass( _maskTextureHandle, MaskTextureId );
			builder.AllowPassCulling( false );
			builder.SetRenderFunc( static ( PassData data, RasterGraphContext context ) =>
			{
				DrawTargets( context.cmd, data.draws, data.maskMaterial );
			} );
		}
	}

	static void BuildDrawList( IReadOnlyList<Renderer> renderers, List<MaskDraw> destination )
	{
		destination.Clear();
		if ( renderers == null )
			return;

		for ( int i = 0; i < renderers.Count; i++ )
		{
			Renderer renderer = renderers[ i ];
			if ( renderer == null )
				continue;

			GoldPileTerrainMesh pileMesh = renderer.GetComponentInParent<GoldPileTerrainMesh>();
			if ( pileMesh != null
				&& pileMesh.PileRenderer == renderer
				&& pileMesh.TryGetDeformedOutlineDraw( out Mesh deformMesh, out Matrix4x4 deformMatrix, out MaterialPropertyBlock deformMpb ) )
			{
				destination.Add( new MaskDraw
				{
					Mesh = deformMesh,
					Matrix = deformMatrix,
					Properties = deformMpb
				} );
				continue;
			}

			destination.Add( new MaskDraw { Renderer = renderer } );
		}
	}

	static void DrawTargets( RasterCommandBuffer cmd, List<MaskDraw> draws, Material maskMaterial )
	{
		if ( cmd == null || draws == null || maskMaterial == null )
			return;

		for ( int i = 0; i < draws.Count; i++ )
		{
			MaskDraw draw = draws[ i ];
			if ( draw.Mesh != null )
			{
				int submeshCount = Mathf.Max( 1, draw.Mesh.subMeshCount );
				for ( int submesh = 0; submesh < submeshCount; submesh++ )
					cmd.DrawMesh( draw.Mesh, draw.Matrix, maskMaterial, submesh, 0, draw.Properties );
				continue;
			}

			DrawRendererFallback( cmd, draw.Renderer, maskMaterial );
		}
	}

	static void DrawRendererFallback( RasterCommandBuffer cmd, Renderer renderer, Material maskMaterial )
	{
		if ( renderer == null )
			return;

		MeshFilter filter = renderer.GetComponent<MeshFilter>();
		if ( filter != null && filter.sharedMesh != null )
		{
			Mesh mesh = filter.sharedMesh;
			int submeshCount = Mathf.Max( 1, mesh.subMeshCount );
			Matrix4x4 matrix = renderer.localToWorldMatrix;
			for ( int submesh = 0; submesh < submeshCount; submesh++ )
				cmd.DrawMesh( mesh, matrix, maskMaterial, submesh, 0 );
			return;
		}

		int materialSlots = renderer.sharedMaterials != null
			? Mathf.Max( 1, renderer.sharedMaterials.Length )
			: 1;
		for ( int submesh = 0; submesh < materialSlots; submesh++ )
			cmd.DrawRenderer( renderer, maskMaterial, submesh, 0 );
	}

#if URP_COMPATIBILITY_MODE
	static void DrawTargets( CommandBuffer cmd, List<MaskDraw> draws, Material maskMaterial )
	{
		if ( cmd == null || draws == null || maskMaterial == null )
			return;

		for ( int i = 0; i < draws.Count; i++ )
		{
			MaskDraw draw = draws[ i ];
			if ( draw.Mesh != null )
			{
				int submeshCount = Mathf.Max( 1, draw.Mesh.subMeshCount );
				for ( int submesh = 0; submesh < submeshCount; submesh++ )
					cmd.DrawMesh( draw.Mesh, draw.Matrix, maskMaterial, submesh, 0, draw.Properties );
				continue;
			}

			DrawRendererFallback( cmd, draw.Renderer, maskMaterial );
		}
	}

	static void DrawRendererFallback( CommandBuffer cmd, Renderer renderer, Material maskMaterial )
	{
		if ( renderer == null )
			return;

		MeshFilter filter = renderer.GetComponent<MeshFilter>();
		if ( filter != null && filter.sharedMesh != null )
		{
			Mesh mesh = filter.sharedMesh;
			int submeshCount = Mathf.Max( 1, mesh.subMeshCount );
			Matrix4x4 matrix = renderer.localToWorldMatrix;
			for ( int submesh = 0; submesh < submeshCount; submesh++ )
				cmd.DrawMesh( mesh, matrix, maskMaterial, submesh, 0 );
			return;
		}

		int materialSlots = renderer.sharedMaterials != null
			? Mathf.Max( 1, renderer.sharedMaterials.Length )
			: 1;
		for ( int submesh = 0; submesh < materialSlots; submesh++ )
			cmd.DrawRenderer( renderer, maskMaterial, submesh, 0 );
	}
#endif

	sealed class PassData
	{
		public Material maskMaterial;
		public List<MaskDraw> draws;
	}
}
