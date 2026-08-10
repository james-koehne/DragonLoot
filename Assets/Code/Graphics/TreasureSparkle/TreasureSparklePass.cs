using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Draws discovered treasure glints as additive camera-facing quads.
/// Stamp look (color/size/texture) comes live from <see cref="TreasureSparkleDefinition"/>.
/// </summary>
public sealed class TreasureSparklePass : ScriptableRenderPass
{
	static readonly int GlintsId = Shader.PropertyToID( "_SparkleGlints" );
	static readonly int ResolutionId = Shader.PropertyToID( "_SparkleResolution" );

	readonly ProfilingSampler _profilingSampler = new ProfilingSampler( "TreasureSparkleQuads" );
	readonly Material _sparkleMaterial;

	int _appliedSettingsVersion = int.MinValue;
	int _appliedPixelWidth = -1;
	int _appliedPixelHeight = -1;
	Texture _appliedGlintTexture;

	public TreasureSparklePass( Material sparkleMaterial )
	{
		_sparkleMaterial = sparkleMaterial;
		renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
	}

	public override void RecordRenderGraph( RenderGraph renderGraph, ContextContainer frameData )
	{
		TreasureSparkleDefinition definition = TreasureSparkleRendererFeature.ActiveDefinition;
		if ( definition == null || !definition.enableSparkles || _sparkleMaterial == null )
			return;

		if ( !TreasureSparkleMaskRegistrar.HasTargets )
			return;

		if ( !frameData.Contains<TreasureSparkleRendererFeature.FrameData>() )
			return;

		TreasureSparkleRendererFeature.FrameData sparkleData = frameData.Get<TreasureSparkleRendererFeature.FrameData>();
		if ( !sparkleData.hasDiscoveredGlints || sparkleData.glintsBuffer == null || sparkleData.argsBuffer == null )
			return;

		UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
		UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
		TextureHandle source = resourceData.activeColorTexture;
		if ( !source.IsValid() || cameraData.camera == null )
			return;

		ApplySettings( cameraData.camera, definition, sparkleData.glintsBuffer );

		using ( var builder = renderGraph.AddRasterRenderPass<PassData>( "TreasureSparkleQuads", out PassData passData, _profilingSampler ) )
		{
			passData.material = _sparkleMaterial;
			passData.glints = sparkleData.glintsBuffer;
			passData.args = sparkleData.argsBuffer;
			builder.SetRenderAttachment( source, 0, AccessFlags.Write );
			builder.AllowPassCulling( false );
			builder.SetRenderFunc( static ( PassData data, RasterGraphContext context ) =>
			{
				if ( data.material == null || data.glints == null || data.args == null )
					return;

				data.material.SetBuffer( GlintsId, data.glints );
				context.cmd.DrawProceduralIndirect(
					Matrix4x4.identity,
					data.material,
					0,
					MeshTopology.Triangles,
					data.args,
					0 );
			} );
		}
	}

	void ApplySettings( Camera camera, TreasureSparkleDefinition definition, GraphicsBuffer glints )
	{
		if ( camera == null || definition == null || _sparkleMaterial == null )
			return;

		bool settingsDirty = _appliedSettingsVersion != definition.SettingsVersion
			|| _appliedGlintTexture != definition.glintTexture;
		bool resolutionDirty = _appliedPixelWidth != camera.pixelWidth
			|| _appliedPixelHeight != camera.pixelHeight;

		if ( settingsDirty )
		{
			definition.Validate();
			definition.ApplyToMaterial( _sparkleMaterial );
			_appliedSettingsVersion = definition.SettingsVersion;
			_appliedGlintTexture = definition.glintTexture;
		}

		if ( settingsDirty || resolutionDirty )
		{
			_sparkleMaterial.SetVector(
				ResolutionId,
				new Vector4( camera.pixelWidth, camera.pixelHeight, 0f, 0f ) );
			_appliedPixelWidth = camera.pixelWidth;
			_appliedPixelHeight = camera.pixelHeight;
		}

		// Camera position changes every frame while moving — keep this cheap set.
		definition.ApplyCameraToMaterial( _sparkleMaterial, camera );
		_sparkleMaterial.SetBuffer( GlintsId, glints );
	}

	sealed class PassData
	{
		public Material material;
		public GraphicsBuffer glints;
		public GraphicsBuffer args;
	}
}
