using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public sealed class HoverOutlineRendererFeature : ScriptableRendererFeature
{
	public sealed class FrameData : ContextItem
	{
		public TextureHandle maskTexture = TextureHandle.nullHandle;
		public TextureHandle questMaskTexture = TextureHandle.nullHandle;

		public override void Reset()
		{
			maskTexture = TextureHandle.nullHandle;
			questMaskTexture = TextureHandle.nullHandle;
		}
	}

	[SerializeField]
	Shader maskShader;

	[SerializeField]
	Shader compositeShader;

	Material _maskMaterial;
	Material _hoverCompositeMaterial;
	Material _questCompositeMaterial;
	HoverOutlineMaskPass _hoverMaskPass;
	HoverOutlineMaskPass _questMaskPass;
	HoverOutlineCompositePass _hoverCompositePass;
	HoverOutlineCompositePass _questCompositePass;

	public override void Create()
	{
		if ( maskShader == null )
			maskShader = Shader.Find( "DragonLoot/Hover Outline Mask" );
		if ( compositeShader == null )
			compositeShader = Shader.Find( "DragonLoot/Hover Outline Composite" );

		if ( maskShader != null && _maskMaterial == null )
			_maskMaterial = CoreUtils.CreateEngineMaterial( maskShader );
		if ( compositeShader != null && _hoverCompositeMaterial == null )
			_hoverCompositeMaterial = CoreUtils.CreateEngineMaterial( compositeShader );
		if ( compositeShader != null && _questCompositeMaterial == null )
			_questCompositeMaterial = CoreUtils.CreateEngineMaterial( compositeShader );

		_hoverMaskPass = new HoverOutlineMaskPass( _maskMaterial, false );
		_questMaskPass = new HoverOutlineMaskPass( _maskMaterial, true );
		_hoverCompositePass = new HoverOutlineCompositePass( _hoverCompositeMaterial, false );
		_questCompositePass = new HoverOutlineCompositePass( _questCompositeMaterial, true );
	}

	public override void AddRenderPasses( ScriptableRenderer renderer, ref RenderingData renderingData )
	{
		bool hover = HoverOutlineRegistrar.HasTarget;
		bool quest = QuestOutlineRegistrar.HasTarget;
		CameraType camType = renderingData.cameraData.cameraType;
		bool matsOk = _maskMaterial != null && _hoverCompositeMaterial != null;

		if ( camType != CameraType.Game )
			return;

		if ( !matsOk )
			return;

		// Quest first so hover draws on top when both target the same pixels.
		if ( quest && _questCompositeMaterial != null )
		{
			renderer.EnqueuePass( _questMaskPass );
			renderer.EnqueuePass( _questCompositePass );
		}

		if ( hover )
		{
			renderer.EnqueuePass( _hoverMaskPass );
			renderer.EnqueuePass( _hoverCompositePass );
		}
	}

	protected override void Dispose( bool disposing )
	{
		_hoverMaskPass?.Dispose();
		_questMaskPass?.Dispose();
		_hoverMaskPass = null;
		_questMaskPass = null;
		_hoverCompositePass = null;
		_questCompositePass = null;

		CoreUtils.Destroy( _maskMaterial );
		CoreUtils.Destroy( _hoverCompositeMaterial );
		CoreUtils.Destroy( _questCompositeMaterial );
		_maskMaterial = null;
		_hoverCompositeMaterial = null;
		_questCompositeMaterial = null;
	}
}
