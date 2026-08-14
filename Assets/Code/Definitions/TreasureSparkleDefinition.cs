using System.Runtime.InteropServices;

using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Global treasure sparkle settings for <see cref="TreasureSparkleRendererFeature"/>.
/// Asset name must be <c>TreasureSparkleDefinition</c> for <see cref="GameInstance.GetDefinition{T}"/>.
/// Discovery settings drive where/when glints spawn; Stamp / Look settings drive the quad draw.
/// </summary>
[CreateAssetMenu( fileName = "TreasureSparkleDefinition", menuName = "Definitions/TreasureSparkleDefinition" )]
public class TreasureSparkleDefinition : ScriptableObject
{
	public enum SparkleSourceKind
	{
		Pile = 0,
		Coin = 1,
		Gem = 2,
		Artifact = 3
	}

	public const int StencilBitValue = 64;
	public const int DefaultMaxGlints = 2048;
	public const int DefaultMaxVolumes = 256;
	public const int DefaultMaxDiscoverCells = 65536;

	[Header( "Feature" )]
	public bool enableSparkles = false;

	[Tooltip( "Depth-tested redraw of registered treasure MeshRenderers / GPU instances into the mask." )]
	public bool useFallbackRegistrar = true;

	[Tooltip( "Fullscreen stencil resolve. Off by default — can bleed through occluders." )]
	public bool useStencilResolve = false;

	[Header( "Mask Performance" )]
	[Range( 0.25f, 1f )]
	[Tooltip( "Reserved. Mask currently always matches camera size so hardware depth test stays valid." )]
	public float maskResolutionScale = 1f;

	[Tooltip( "When off, skip redrawing GPU loot instances into the mask (pile terrain still masks)." )]
	public bool maskLootInstances = false;

	[Header( "Affected Treasure" )]
	public bool sparkleOnPiles = true;
	public bool sparkleOnCoins = true;
	public bool sparkleOnGems = true;
	public bool sparkleOnArtifacts = true;

	[Header( "Discovery — Density" )]
	[Min( 0.02f )]
	[Tooltip( "World cell size in meters. Smaller = more candidate glint sites." )]
	public float cellSize = 0.5f;

	[Range( 0.001f, 1f )]
	[Tooltip( "Fraction of cells that can host a glint." )]
	public float density = 0.35f;

	public int randomSeed = 17;

	[Range( 2f, 48f )]
	[Tooltip( "Full density when a cell covers about this many screen pixels. Higher = fewer distant glints." )]
	public float screenDensityRefPixels = 22f;

	[Min( 1f )]
	[Tooltip( "Extra glint occupancy on gems vs piles/coins." )]
	public float gemDensityMultiplier = 2.5f;

	[Min( 1f )]
	[Tooltip( "Extra glint occupancy on artifacts vs piles/coins." )]
	public float artifactDensityMultiplier = 3f;

	[Header( "Discovery — Flash Response" )]
	[Range( 0f, 1.5f )]
	[Tooltip( "Random facet tilt from the surface normal." )]
	public float coinFacetJitter = 0.85f;

	[Range( 2f, 128f )]
	[Tooltip( "Specular sharpness. Higher = tinier hotter flashes." )]
	public float metallicSharpness = 64f;

	[Range( 0f, 16f )]
	[Tooltip( "Brightness of a sun-catch flash (discover intensity)." )]
	public float litBoost = 6f;

	[Range( 0f, 1f )]
	[Tooltip( "How hard misaligned facets are killed. 1 = only sun catches." )]
	public float lightKillStrength = 0.85f;

	[Range( 0f, 1f )]
	[Tooltip( "Opens glints across the pile surface (density + residual intensity). 0 = specular-gated only." )]
	public float baseCoverage = 0f;

	[Range( 0f, 1f )]
	[Tooltip( "Concentrates occupancy and brightness around metallic specular catches. 0 = even, 1 = hotspot-weighted." )]
	public float specularBoost = 1f;

	[Range( 0f, 8f )]
	[Tooltip( "Extra glint spawn density inside specular hotspots (multiplies occupancy where light catches)." )]
	public float specularDensity = 3f;

	[Range( 0f, 1f )]
	[Tooltip( "Pose-stable view modulation. 0 = light only, 1 = stronger swap while walking." )]
	public float viewSwapStrength = 0.75f;

	[Range( 0f, 0.95f )]
	[Tooltip( "How centered the preferred view must stay; higher = shorter visible arc while walking." )]
	public float viewSwapThreshold = 0.45f;

	[Range( 1f, 16f )]
	[Tooltip( "Harder falloff once you leave the preferred view." )]
	public float viewSwapSharpness = 6f;

	[Range( 0.01f, 0.9f )]
	[Tooltip( "Softness of the on→off ramp while turning at the reference distance. Lower = snappier glitter." )]
	public float viewCrossfade = 0.05f;

	[Min( 1f )]
	[Tooltip( "Camera distance (meters) where View Crossfade applies as authored. Farther glints snap faster." )]
	public float viewCrossfadeRefDistance = 15f;

	[Range( 0f, 1f )]
	[Tooltip( "Glints below this intensity are discarded (hard cut for crisp glitter)." )]
	public float minIntensity = 0.25f;

	[Header( "Discovery — Distance" )]
	[Min( 0f )]
	[Tooltip( "Glint intensity fades to zero when the camera is closer than this (meters)." )]
	public float closeFadeDistance = 0.35f;

	[Min( 0.05f )]
	[Tooltip( "Full glint intensity resumes beyond this distance (meters)." )]
	public float closeFadeFullDistance = 1.5f;

	[Min( 1f )]
	[Tooltip( "Intensity begins fading beyond this distance (meters)." )]
	public float farFadeStart = 35f;

	[Min( 1f )]
	[Tooltip( "Intensity reaches zero at this distance (meters)." )]
	public float farFadeEnd = 90f;

	[Header( "Discovery — Animation" )]
	[Range( 0f, 1f )]
	public float twinkleStrength = 0.2f;

	[Min( 0f )]
	public float twinkleSpeed = 0.85f;

	[Header( "Stamp — Size / Texture (live on quads)" )]
	[Tooltip( "Optional stamp texture. Leave empty for procedural discs." )]
	public Texture2D glintTexture;

	[Range( 0.25f, 6f )]
	[Tooltip( "Procedural disc diameter in screen pixels (when no glint texture)." )]
	public float targetPixelSize = 0.9f;

	[Range( 0.25f, 32f )]
	[Tooltip( "Glint texture stamp diameter in screen pixels when close to the camera." )]
	public float glintTextureSizeNear = 16f;

	[Range( 0.25f, 32f )]
	[Tooltip( "Glint texture stamp diameter in screen pixels at far size distance." )]
	public float glintTextureSizeFar = 4f;

	[Min( 0.05f )]
	[Tooltip( "Camera distance (meters) where stamp uses the near texture size." )]
	public float glintSizeNearDistance = 1.5f;

	[Min( 0.1f )]
	[Tooltip( "Camera distance (meters) where stamp reaches the far texture size." )]
	public float glintSizeFarDistance = 35f;

	[Range( 0.5f, 4f )]
	[Tooltip( "Falloff power for procedural discs." )]
	public float spotFalloff = 1.8f;

	[Header( "Stamp — Look (live on quads)" )]
	[ColorUsage( true, true )]
	public Color sparkleColor = new Color( 8f, 6.2f, 2.8f, 1f );

	[Range( 0f, 8f )]
	[Tooltip( "Scales HDR output into bloom." )]
	public float bloomContribution = 4f;

	[Range( 0f, 1f )]
	[Tooltip( "How much of the stamp center goes white-hot vs gold tint." )]
	public float coreHotness = 0.75f;

	[Header( "Budget" )]
	[Min( 64 )]
	[Tooltip( "Hard cap on discovered glint quads per frame." )]
	public int maxGlints = DefaultMaxGlints;

	[Min( 8 )]
	[Tooltip( "Hard cap on frustum-culled sparkle volumes uploaded for discovery." )]
	public int maxVolumes = DefaultMaxVolumes;

	[Min( 1024 )]
	[Tooltip( "Max world cells walked by discover compute per frame." )]
	public int maxDiscoverCells = DefaultMaxDiscoverCells;

	int _settingsVersion;

	public int SettingsVersion => _settingsVersion;

	static readonly int CellSizeId = Shader.PropertyToID( "_SparkleCellSize" );
	static readonly int DensityId = Shader.PropertyToID( "_SparkleDensity" );
	static readonly int TargetPixelSizeId = Shader.PropertyToID( "_SparkleTargetPixelSize" );
	static readonly int ScreenDensityBlendId = Shader.PropertyToID( "_SparkleScreenDensityBlend" );
	static readonly int ScreenDensityRefPixelsId = Shader.PropertyToID( "_SparkleScreenDensityRefPixels" );
	static readonly int GlintTextureId = Shader.PropertyToID( "_SparkleGlintTex" );
	static readonly int UseGlintTextureId = Shader.PropertyToID( "_SparkleUseGlintTex" );
	static readonly int GlintTextureBlendId = Shader.PropertyToID( "_SparkleGlintTexBlend" );
	static readonly int GlintTextureSizeNearId = Shader.PropertyToID( "_SparkleGlintTexSizeNear" );
	static readonly int GlintTextureSizeFarId = Shader.PropertyToID( "_SparkleGlintTexSizeFar" );
	static readonly int GlintSizeNearDistanceId = Shader.PropertyToID( "_SparkleGlintSizeNearDistance" );
	static readonly int GlintSizeFarDistanceId = Shader.PropertyToID( "_SparkleGlintSizeFarDistance" );
	static readonly int CameraPositionId = Shader.PropertyToID( "_SparkleCameraPosition" );
	static readonly int CoreHotnessId = Shader.PropertyToID( "_SparkleCoreHotness" );
	static readonly int ViewCrossfadeId = Shader.PropertyToID( "_SparkleViewCrossfade" );
	static readonly int ViewCrossfadeRefDistanceId = Shader.PropertyToID( "_SparkleViewCrossfadeRefDistance" );
	static readonly int ViewSwapStrengthId = Shader.PropertyToID( "_SparkleViewSwapStrength" );
	static readonly int ViewSwapSharpnessId = Shader.PropertyToID( "_SparkleViewSwapSharpness" );
	static readonly int ViewSwapThresholdId = Shader.PropertyToID( "_SparkleViewSwapThreshold" );
	static readonly int BaseGlintId = Shader.PropertyToID( "_SparkleBaseGlint" );
	static readonly int LitBoostId = Shader.PropertyToID( "_SparkleLitBoost" );
	static readonly int BrightnessJitterId = Shader.PropertyToID( "_SparkleBrightnessJitter" );
	static readonly int CoinFacetJitterId = Shader.PropertyToID( "_SparkleCoinFacetJitter" );
	static readonly int MetallicSharpnessId = Shader.PropertyToID( "_SparkleMetallicSharpness" );
	static readonly int LightKillStrengthId = Shader.PropertyToID( "_SparkleLightKillStrength" );
	static readonly int BaseCoverageId = Shader.PropertyToID( "_SparkleBaseCoverage" );
	static readonly int SpecularBoostId = Shader.PropertyToID( "_SparkleSpecularBoost" );
	static readonly int SpecularDensityId = Shader.PropertyToID( "_SparkleSpecularDensity" );
	static readonly int MinIntensityId = Shader.PropertyToID( "_SparkleMinIntensity" );
	static readonly int SoftLobeAmountId = Shader.PropertyToID( "_SparkleSoftLobeAmount" );
	static readonly int SeedId = Shader.PropertyToID( "_SparkleSeed" );
	static readonly int AlignmentThresholdId = Shader.PropertyToID( "_SparkleAlignmentThreshold" );
	static readonly int FadeAngleId = Shader.PropertyToID( "_SparkleFadeAngle" );
	static readonly int NearGlintStartId = Shader.PropertyToID( "_SparkleNearGlintStart" );
	static readonly int NearGlintFullId = Shader.PropertyToID( "_SparkleNearGlintFull" );
	static readonly int FarGlintStartId = Shader.PropertyToID( "_SparkleFarGlintStart" );
	static readonly int FarGlintEndId = Shader.PropertyToID( "_SparkleFarGlintEnd" );
	static readonly int NearDensityScaleId = Shader.PropertyToID( "_SparkleNearDensityScale" );
	static readonly int FarDensityScaleId = Shader.PropertyToID( "_SparkleFarDensityScale" );
	static readonly int NearIntensityFloorId = Shader.PropertyToID( "_SparkleNearIntensityFloor" );
	static readonly int ScreenEdgeFadeId = Shader.PropertyToID( "_SparkleScreenEdgeFade" );
	static readonly int TwinkleStrengthId = Shader.PropertyToID( "_SparkleTwinkleStrength" );
	static readonly int TwinkleSpeedId = Shader.PropertyToID( "_SparkleTwinkleSpeed" );
	static readonly int TwinkleDesyncId = Shader.PropertyToID( "_SparkleTwinkleDesync" );
	static readonly int BloomContributionId = Shader.PropertyToID( "_SparkleBloomContribution" );
	static readonly int MaxActiveId = Shader.PropertyToID( "_SparkleMaxActive" );
	static readonly int ColorId = Shader.PropertyToID( "_SparkleColor" );
	static readonly int GemDensityMulId = Shader.PropertyToID( "_SparkleGemDensityMul" );
	static readonly int ArtifactDensityMulId = Shader.PropertyToID( "_SparkleArtifactDensityMul" );
	static readonly int MinSizeId = Shader.PropertyToID( "_SparkleMinSize" );
	static readonly int MaxSizeId = Shader.PropertyToID( "_SparkleMaxSize" );
	static readonly int SpotFalloffId = Shader.PropertyToID( "_SparkleSpotFalloff" );
	static readonly int MaxGlintsId = Shader.PropertyToID( "_SparkleMaxGlints" );
	static readonly int SurfaceSlackId = Shader.PropertyToID( "_SparkleSurfaceSlack" );

	public const float MaskPile = 0.25f;
	public const float MaskCoin = 0.5f;
	public const float MaskGem = 0.75f;
	public const float MaskArtifact = 1f;

	[StructLayout( LayoutKind.Sequential )]
	public struct GlintInstance
	{
		public Vector3 CenterWS;
		public float Intensity;
		public float SizeJitter;
		public float Pad0;
		public float Pad1;
		public float Pad2;
	}

	[StructLayout( LayoutKind.Sequential )]
	public struct GpuVolume
	{
		public int CellMinX;
		public int CellMinY;
		public int CellMinZ;
		public int SizeX;
		public int SizeY;
		public int SizeZ;
		public int FlatOffset;
		public float KindMask;
	}

	public static float MaskWriteValueForKind( SparkleSourceKind kind )
	{
		switch ( kind )
		{
			case SparkleSourceKind.Pile:
				return MaskPile;
			case SparkleSourceKind.Coin:
				return MaskCoin;
			case SparkleSourceKind.Gem:
				return MaskGem;
			case SparkleSourceKind.Artifact:
				return MaskArtifact;
			default:
				return MaskCoin;
		}
	}

	public static SparkleSourceKind KindFromCategory( TreasureCategory category )
	{
		if ( category == TreasureCategory.Coin )
			return SparkleSourceKind.Coin;
		if ( category == TreasureCategory.Gem )
			return SparkleSourceKind.Gem;
		return SparkleSourceKind.Artifact;
	}

	public bool Allows( SparkleSourceKind kind )
	{
		switch ( kind )
		{
			case SparkleSourceKind.Pile:
				return sparkleOnPiles;
			case SparkleSourceKind.Coin:
				return sparkleOnCoins;
			case SparkleSourceKind.Gem:
				return sparkleOnGems;
			case SparkleSourceKind.Artifact:
				return sparkleOnArtifacts;
			default:
				return false;
		}
	}

	public void Validate()
	{
		cellSize = Mathf.Max( 0.02f, cellSize );
		density = Mathf.Clamp( density, 0.001f, 1f );
		targetPixelSize = Mathf.Clamp( targetPixelSize, 0.25f, 6f );
		screenDensityRefPixels = Mathf.Clamp( screenDensityRefPixels, 2f, 48f );
		glintTextureSizeNear = Mathf.Clamp( glintTextureSizeNear, 0.25f, 32f );
		glintTextureSizeFar = Mathf.Clamp( glintTextureSizeFar, 0.25f, 32f );
		glintSizeNearDistance = Mathf.Max( 0.05f, glintSizeNearDistance );
		glintSizeFarDistance = Mathf.Max( glintSizeNearDistance + 0.05f, glintSizeFarDistance );
		spotFalloff = Mathf.Clamp( spotFalloff, 0.5f, 4f );
		coreHotness = Mathf.Clamp01( coreHotness );
		litBoost = Mathf.Clamp( litBoost, 0f, 16f );
		bloomContribution = Mathf.Clamp( bloomContribution, 0f, 8f );
		coinFacetJitter = Mathf.Clamp( coinFacetJitter, 0f, 1.5f );
		metallicSharpness = Mathf.Clamp( metallicSharpness, 2f, 128f );
		lightKillStrength = Mathf.Clamp01( lightKillStrength );
		baseCoverage = Mathf.Clamp01( baseCoverage );
		specularBoost = Mathf.Clamp01( specularBoost );
		specularDensity = Mathf.Clamp( specularDensity, 0f, 8f );
		minIntensity = Mathf.Clamp01( minIntensity );
		viewSwapStrength = Mathf.Clamp01( viewSwapStrength );
		viewSwapThreshold = Mathf.Clamp( viewSwapThreshold, 0f, 0.95f );
		viewSwapSharpness = Mathf.Clamp( viewSwapSharpness, 1f, 16f );
		viewCrossfade = Mathf.Clamp( viewCrossfade, 0.01f, 0.9f );
		viewCrossfadeRefDistance = Mathf.Max( 1f, viewCrossfadeRefDistance );
		twinkleStrength = Mathf.Clamp01( twinkleStrength );
		twinkleSpeed = Mathf.Max( 0f, twinkleSpeed );
		closeFadeDistance = Mathf.Max( 0f, closeFadeDistance );
		closeFadeFullDistance = Mathf.Max( closeFadeDistance + 0.05f, closeFadeFullDistance );
		farFadeStart = Mathf.Max( closeFadeFullDistance + 0.5f, farFadeStart );
		farFadeEnd = Mathf.Max( farFadeStart + 0.5f, farFadeEnd );
		gemDensityMultiplier = Mathf.Max( 1f, gemDensityMultiplier );
		artifactDensityMultiplier = Mathf.Max( 1f, artifactDensityMultiplier );
		maxGlints = Mathf.Clamp( maxGlints, 64, 16384 );
		maxVolumes = Mathf.Clamp( maxVolumes, 8, 1024 );
		maxDiscoverCells = Mathf.Clamp( maxDiscoverCells, 1024, 262144 );
		maskResolutionScale = Mathf.Clamp( maskResolutionScale, 0.25f, 1f );
	}

	void OnValidate()
	{
		Validate();
		_settingsVersion++;
	}

	/// <summary>Applies stamp / look uniforms used by the quad draw material.</summary>
	public void ApplyToMaterial( Material material )
	{
		if ( material == null )
			return;

		bool useGlintTex = glintTexture != null;
		material.SetFloat( UseGlintTextureId, useGlintTex ? 1f : 0f );
		material.SetFloat( GlintTextureBlendId, useGlintTex ? 1f : 0f );
		material.SetFloat( TargetPixelSizeId, targetPixelSize );
		material.SetFloat( GlintTextureSizeNearId, glintTextureSizeNear );
		material.SetFloat( GlintTextureSizeFarId, glintTextureSizeFar );
		material.SetFloat( GlintSizeNearDistanceId, glintSizeNearDistance );
		material.SetFloat( GlintSizeFarDistanceId, glintSizeFarDistance );
		material.SetFloat( SpotFalloffId, spotFalloff );
		material.SetTexture( GlintTextureId, useGlintTex ? glintTexture : Texture2D.whiteTexture );

		material.SetColor( ColorId, sparkleColor );
		material.SetFloat( BloomContributionId, bloomContribution );
		material.SetFloat( CoreHotnessId, coreHotness );
	}

	public void ApplyCameraToMaterial( Material material, Camera camera )
	{
		if ( material == null || camera == null )
			return;

		material.SetVector( CameraPositionId, camera.transform.position );
	}

	/// <summary>Applies discovery uniforms used by the glint compute pass.</summary>
	public void ApplyToComputeCommand( ComputeCommandBuffer cmd, ComputeShader compute )
	{
		if ( cmd == null || compute == null )
			return;

		cmd.SetComputeFloatParam( compute, CellSizeId, cellSize );
		cmd.SetComputeFloatParam( compute, DensityId, density );
		cmd.SetComputeFloatParam( compute, MaxActiveId, density );
		cmd.SetComputeFloatParam( compute, ScreenDensityBlendId, 1f );
		cmd.SetComputeFloatParam( compute, ScreenDensityRefPixelsId, screenDensityRefPixels );
		cmd.SetComputeFloatParam( compute, LitBoostId, litBoost );
		cmd.SetComputeFloatParam( compute, BaseGlintId, 0f );
		cmd.SetComputeFloatParam( compute, BrightnessJitterId, 0.2f );
		cmd.SetComputeFloatParam( compute, MinSizeId, 0.9f );
		cmd.SetComputeFloatParam( compute, MaxSizeId, 1.1f );
		cmd.SetComputeFloatParam( compute, CoinFacetJitterId, coinFacetJitter );
		cmd.SetComputeFloatParam( compute, MetallicSharpnessId, metallicSharpness );
		cmd.SetComputeFloatParam( compute, LightKillStrengthId, lightKillStrength );
		cmd.SetComputeFloatParam( compute, BaseCoverageId, baseCoverage );
		cmd.SetComputeFloatParam( compute, SpecularBoostId, specularBoost );
		cmd.SetComputeFloatParam( compute, SpecularDensityId, specularDensity );
		cmd.SetComputeFloatParam( compute, MinIntensityId, minIntensity );
		cmd.SetComputeFloatParam( compute, SoftLobeAmountId, 0.08f );
		cmd.SetComputeFloatParam( compute, AlignmentThresholdId, 0.35f );
		cmd.SetComputeFloatParam( compute, FadeAngleId, 18f * Mathf.Deg2Rad );
		cmd.SetComputeFloatParam( compute, ViewSwapStrengthId, viewSwapStrength );
		cmd.SetComputeFloatParam( compute, ViewSwapSharpnessId, viewSwapSharpness );
		cmd.SetComputeFloatParam( compute, ViewSwapThresholdId, viewSwapThreshold );
		cmd.SetComputeFloatParam( compute, ViewCrossfadeId, viewCrossfade );
		cmd.SetComputeFloatParam( compute, ViewCrossfadeRefDistanceId, viewCrossfadeRefDistance );
		cmd.SetComputeFloatParam( compute, SeedId, randomSeed );
		cmd.SetComputeFloatParam( compute, NearGlintStartId, closeFadeDistance );
		cmd.SetComputeFloatParam( compute, NearGlintFullId, closeFadeFullDistance );
		cmd.SetComputeFloatParam( compute, FarGlintStartId, farFadeStart );
		cmd.SetComputeFloatParam( compute, FarGlintEndId, farFadeEnd );
		cmd.SetComputeFloatParam( compute, NearDensityScaleId, 1f );
		cmd.SetComputeFloatParam( compute, FarDensityScaleId, 0.35f );
		cmd.SetComputeFloatParam( compute, NearIntensityFloorId, 0f );
		cmd.SetComputeFloatParam( compute, GemDensityMulId, gemDensityMultiplier );
		cmd.SetComputeFloatParam( compute, ArtifactDensityMulId, artifactDensityMultiplier );
		cmd.SetComputeFloatParam( compute, ScreenEdgeFadeId, 0.015f );
		cmd.SetComputeFloatParam( compute, TwinkleStrengthId, twinkleStrength );
		cmd.SetComputeFloatParam( compute, TwinkleSpeedId, twinkleSpeed );
		cmd.SetComputeFloatParam( compute, TwinkleDesyncId, 0.85f );
		cmd.SetComputeIntParam( compute, MaxGlintsId, maxGlints );
		cmd.SetComputeFloatParam( compute, SurfaceSlackId, cellSize * 0.85f );
	}
}
