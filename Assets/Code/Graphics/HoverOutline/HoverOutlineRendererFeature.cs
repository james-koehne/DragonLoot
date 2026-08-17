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
	Material _compositeMaterial;
	HoverOutlineMaskPass _maskPass;
	HoverOutlineCompositePass _compositePass;

	public override void Create()
	{
		if ( maskShader == null )
			maskShader = Shader.Find( "DragonLoot/Hover Outline Mask" );
		if ( compositeShader == null )
			compositeShader = Shader.Find( "DragonLoot/Hover Outline Composite" );

		if ( maskShader != null && _maskMaterial == null )
			_maskMaterial = CoreUtils.CreateEngineMaterial( maskShader );
		if ( compositeShader != null && _compositeMaterial == null )
			_compositeMaterial = CoreUtils.CreateEngineMaterial( compositeShader );

		if ( _compositeMaterial != null )
		{
			_compositeMaterial.SetTexture( "_HoverOutlineMask", Texture2D.blackTexture );
			_compositeMaterial.SetTexture( "_QuestOutlineMask", Texture2D.blackTexture );
		}

		_maskPass = new HoverOutlineMaskPass( _maskMaterial );
		_compositePass = new HoverOutlineCompositePass( _compositeMaterial );
	}

	public override void AddRenderPasses( ScriptableRenderer renderer, ref RenderingData renderingData )
	{
		if ( !HoverOutlineRegistrar.HasTarget && !QuestOutlineRegistrar.HasTarget )
			return;

		if ( renderingData.cameraData.cameraType != CameraType.Game )
			return;

		if ( _maskMaterial == null || _compositeMaterial == null )
			return;

		renderer.EnqueuePass( _maskPass );
		renderer.EnqueuePass( _compositePass );
	}

	protected override void Dispose( bool disposing )
	{
		_maskPass?.Dispose();
		_maskPass = null;
		_compositePass = null;

		CoreUtils.Destroy( _maskMaterial );
		CoreUtils.Destroy( _compositeMaterial );
		_maskMaterial = null;
		_compositeMaterial = null;
	}
}
