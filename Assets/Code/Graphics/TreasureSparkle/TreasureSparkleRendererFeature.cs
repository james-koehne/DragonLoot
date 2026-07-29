using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public sealed class TreasureSparkleRendererFeature : ScriptableRendererFeature
{
	public sealed class FrameData : ContextItem
	{
		public TextureHandle maskTexture = TextureHandle.nullHandle;

		public override void Reset()
		{
			maskTexture = TextureHandle.nullHandle;
		}
	}

	[SerializeField]
	TreasureSparkleDefinition definition;

	[SerializeField]
	Shader maskResolveShader;

	[SerializeField]
	Shader fallbackMaskShader;

	[SerializeField]
	Shader sparkleShader;

	Material _resolveMaterial;
	Material _fallbackMaterial;
	Material _sparkleMaterial;
	TreasureSparkleMaskPass _maskPass;
	TreasureSparklePass _sparklePass;
	TreasureSparkleDefinition _resolvedDefinition;

	public static TreasureSparkleDefinition ActiveDefinition { get; private set; }

	public override void Create()
	{
		if ( maskResolveShader == null )
			maskResolveShader = Shader.Find( "DragonLoot/Treasure Sparkle Mask Resolve" );
		if ( fallbackMaskShader == null )
			fallbackMaskShader = Shader.Find( "DragonLoot/Treasure Sparkle Fallback Mask" );
		if ( sparkleShader == null )
			sparkleShader = Shader.Find( "DragonLoot/Treasure Sparkle" );

		if ( maskResolveShader != null && _resolveMaterial == null )
			_resolveMaterial = CoreUtils.CreateEngineMaterial( maskResolveShader );
		if ( fallbackMaskShader != null && _fallbackMaterial == null )
			_fallbackMaterial = CoreUtils.CreateEngineMaterial( fallbackMaskShader );
		if ( sparkleShader != null && _sparkleMaterial == null )
			_sparkleMaterial = CoreUtils.CreateEngineMaterial( sparkleShader );

		_maskPass = new TreasureSparkleMaskPass( _resolveMaterial, _fallbackMaterial );
		_sparklePass = new TreasureSparklePass( _sparkleMaterial );
	}

	public override void AddRenderPasses( ScriptableRenderer renderer, ref RenderingData renderingData )
	{
		ActiveDefinition = ResolveDefinition();
		if ( ActiveDefinition == null || !ActiveDefinition.enableSparkles )
			return;

		if ( renderingData.cameraData.cameraType != CameraType.Game )
			return;

		if ( _resolveMaterial == null || _sparkleMaterial == null )
			return;

		renderer.EnqueuePass( _maskPass );
		renderer.EnqueuePass( _sparklePass );
	}

	TreasureSparkleDefinition ResolveDefinition()
	{
		if ( definition != null )
			return definition;

		_resolvedDefinition = RuntimeDefinition.Resolve( ref _resolvedDefinition );
		return _resolvedDefinition;
	}

	protected override void Dispose( bool disposing )
	{
		_maskPass = null;
		_sparklePass = null;
		ActiveDefinition = null;

		CoreUtils.Destroy( _resolveMaterial );
		CoreUtils.Destroy( _fallbackMaterial );
		CoreUtils.Destroy( _sparkleMaterial );
		_resolveMaterial = null;
		_fallbackMaterial = null;
		_sparkleMaterial = null;
	}
}
