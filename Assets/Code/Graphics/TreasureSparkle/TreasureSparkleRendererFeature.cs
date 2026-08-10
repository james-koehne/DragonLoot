using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public sealed class TreasureSparkleRendererFeature : ScriptableRendererFeature
{
	public sealed class FrameData : ContextItem
	{
		public TextureHandle maskTexture = TextureHandle.nullHandle;
		public GraphicsBuffer glintsBuffer;
		public GraphicsBuffer argsBuffer;
		public bool hasDiscoveredGlints;

		public override void Reset()
		{
			maskTexture = TextureHandle.nullHandle;
			glintsBuffer = null;
			argsBuffer = null;
			hasDiscoveredGlints = false;
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

	[SerializeField]
	ComputeShader discoverCompute;

	Material _resolveMaterial;
	Material _fallbackMaterial;
	Material _sparkleMaterial;
	TreasureSparkleMaskPass _maskPass;
	TreasureSparkleDiscoverPass _discoverPass;
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

		if ( _fallbackMaterial != null )
			_fallbackMaterial.enableInstancing = true;

		_maskPass = new TreasureSparkleMaskPass( _resolveMaterial, _fallbackMaterial );
		_discoverPass?.Dispose();
		_discoverPass = new TreasureSparkleDiscoverPass( discoverCompute );
		_sparklePass = new TreasureSparklePass( _sparkleMaterial );
	}

	public override void AddRenderPasses( ScriptableRenderer renderer, ref RenderingData renderingData )
	{
		ActiveDefinition = ResolveDefinition();
		if ( ActiveDefinition == null || !ActiveDefinition.enableSparkles )
			return;

		if ( renderingData.cameraData.cameraType != CameraType.Game )
			return;

		if ( !TreasureSparkleMaskRegistrar.HasTargets )
			return;

		if ( _sparkleMaterial == null || discoverCompute == null || !SystemInfo.supportsComputeShaders )
			return;

		if ( _fallbackMaterial == null && ( _resolveMaterial == null || !ActiveDefinition.useStencilResolve ) )
			return;

		if ( _discoverPass == null || !_discoverPass.IsReady )
		{
			_discoverPass?.Dispose();
			_discoverPass = new TreasureSparkleDiscoverPass( discoverCompute );
		}

		if ( !_discoverPass.IsReady )
			return;

		renderer.EnqueuePass( _maskPass );
		renderer.EnqueuePass( _discoverPass );
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
		_discoverPass?.Dispose();
		_maskPass?.Dispose();
		_maskPass = null;
		_discoverPass = null;
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
