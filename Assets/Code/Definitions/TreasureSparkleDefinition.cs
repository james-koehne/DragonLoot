using UnityEngine;

/// <summary>
/// Global screen-space treasure sparkle settings for <see cref="TreasureSparkleRendererFeature"/>.
/// Asset name must be <c>TreasureSparkleDefinition</c> for <see cref="GameInstance.GetDefinition{T}"/>.
/// </summary>
[CreateAssetMenu( fileName = "TreasureSparkleDefinition", menuName = "Definitions/TreasureSparkleDefinition" )]
public class TreasureSparkleDefinition : ScriptableObject
{
	public enum DebugMode
	{
		Off = 0,
		Mask = 1,
		Cells = 2,
		ActiveGlints = 3,
		AlignedOnly = 4,
		DensityHeatmap = 5
	}

	public enum SparkleSourceKind
	{
		Pile = 0,
		Coin = 1,
		Gem = 2,
		Artifact = 3
	}

	public const int StencilBitValue = 64;

	[Header( "Mask" )]
	public bool enableSparkles = true;

	[Tooltip( "Depth-tested redraw of registered treasure MeshRenderers / GPU instances into the mask." )]
	public bool useFallbackRegistrar = true;

	[Tooltip( "Fullscreen stencil resolve. Off by default — can bleed through occluders." )]
	public bool useStencilResolve = false;

	[Header( "Affected Treasure" )]
	public bool sparkleOnPiles = true;
	public bool sparkleOnCoins = true;
	public bool sparkleOnGems = true;
	public bool sparkleOnArtifacts = true;

	[Header( "Density" )]
	[Min( 0.02f )]
	[Tooltip( "World cell size in meters. Smaller = more candidate glint sites." )]
	public float cellSize = 0.5f;

	[Range( 0.001f, 1f )]
	[Tooltip( "Fraction of cells that can host a glint." )]
	public float density = 0.35f;

	[Range( 1, 3 )]
	[Tooltip( "Extra overlay layers at different cell scales." )]
	public int layerCount = 1;

	public int randomSeed = 17;

	[Header( "Size / Stamp" )]
	[Range( 0.25f, 6f )]
	[Tooltip( "Procedural disc diameter in screen pixels (when no glint texture)." )]
	public float targetPixelSize = 0.9f;

	[Range( 2f, 48f )]
	[Tooltip( "Full density when a cell covers about this many screen pixels. Higher = fewer distant glints." )]
	public float screenDensityRefPixels = 22f;

	[Tooltip( "Optional stamp texture. Leave empty for procedural discs." )]
	public Texture2D glintTexture;

	[Range( 0.25f, 12f )]
	[Tooltip( "Camera-aligned stamp diameter in screen pixels." )]
	public float glintTextureSize = 1.1f;

	[Header( "Brightness" )]
	[ColorUsage( true, true )]
	public Color sparkleColor = new Color( 8f, 6.2f, 2.8f, 1f );

	[Range( 0f, 16f )]
	[Tooltip( "Brightness of a sun-catch flash." )]
	public float litBoost = 6f;

	[Range( 0f, 8f )]
	public float bloomContribution = 4f;

	[Range( 0f, 1f )]
	[Tooltip( "How much of the stamp center goes white-hot vs gold tint." )]
	public float coreHotness = 0.75f;

	[Header( "Flash Response" )]
	[Range( 0f, 1.5f )]
	[Tooltip( "Random facet tilt from the surface normal." )]
	public float coinFacetJitter = 0.85f;

	[Range( 2f, 128f )]
	[Tooltip( "Specular sharpness. Higher = tinier hotter flashes." )]
	public float metallicSharpness = 64f;

	[Range( 0f, 1f )]
	[Tooltip( "How hard misaligned facets are killed. 1 = only sun catches." )]
	public float lightKillStrength = 0.85f;

	[Range( 0f, 1f )]
	[Tooltip( "Pose-stable view modulation. 0 = light only, 1 = stronger swap while walking." )]
	public float viewSwapStrength = 0.75f;

	[Header( "Distance" )]
	[Min( 0f )]
	[Tooltip( "Glint intensity fades to zero when the camera is closer than this (meters)." )]
	public float closeFadeDistance = 0.35f;

	[Min( 0.05f )]
	[Tooltip( "Full glint intensity resumes beyond this distance (meters)." )]
	public float closeFadeFullDistance = 1.5f;

	[Header( "Source Density" )]
	[Min( 1f )]
	[Tooltip( "Extra glint occupancy on gems vs piles/coins." )]
	public float gemDensityMultiplier = 2.5f;

	[Min( 1f )]
	[Tooltip( "Extra glint occupancy on artifacts vs piles/coins." )]
	public float artifactDensityMultiplier = 3f;

	[Header( "Stability" )]
	[Range( 0f, 1f )]
	[Tooltip( "Pulls sparkles inward from silhouettes / depth edges." )]
	public float silhouetteFade = 0.85f;

	[Range( 0, 3 )]
	[Tooltip( "Mask erode radius in texels." )]
	public int maskErodePixels = 1;

	[Header( "Animation" )]
	[Range( 0f, 1f )]
	public float twinkleStrength = 0.2f;

	[Min( 0f )]
	public float twinkleSpeed = 0.85f;

	[Header( "Quality" )]
	public bool halfResolution = false;

	[Header( "Debug" )]
	public DebugMode debugMode = DebugMode.Off;

	static readonly int EnableId = Shader.PropertyToID( "_SparkleEnable" );
	static readonly int CellSizeId = Shader.PropertyToID( "_SparkleCellSize" );
	static readonly int DensityId = Shader.PropertyToID( "_SparkleDensity" );
	static readonly int LayerCountId = Shader.PropertyToID( "_SparkleLayerCount" );
	static readonly int LayerScaleId = Shader.PropertyToID( "_SparkleLayerScale" );
	static readonly int MinSizeId = Shader.PropertyToID( "_SparkleMinSize" );
	static readonly int MaxSizeId = Shader.PropertyToID( "_SparkleMaxSize" );
	static readonly int SpotFalloffId = Shader.PropertyToID( "_SparkleSpotFalloff" );
	static readonly int ScreenSpaceBlendId = Shader.PropertyToID( "_SparkleScreenSpaceBlend" );
	static readonly int TargetPixelSizeId = Shader.PropertyToID( "_SparkleTargetPixelSize" );
	static readonly int ScreenDensityBlendId = Shader.PropertyToID( "_SparkleScreenDensityBlend" );
	static readonly int ScreenDensityRefPixelsId = Shader.PropertyToID( "_SparkleScreenDensityRefPixels" );
	static readonly int GlintTextureId = Shader.PropertyToID( "_SparkleGlintTex" );
	static readonly int UseGlintTextureId = Shader.PropertyToID( "_SparkleUseGlintTex" );
	static readonly int GlintTextureBlendId = Shader.PropertyToID( "_SparkleGlintTexBlend" );
	static readonly int GlintTextureSizeId = Shader.PropertyToID( "_SparkleGlintTexSize" );
	static readonly int CoreHotnessId = Shader.PropertyToID( "_SparkleCoreHotness" );
	static readonly int ViewCrossfadeId = Shader.PropertyToID( "_SparkleViewCrossfade" );
	static readonly int ViewSwapStrengthId = Shader.PropertyToID( "_SparkleViewSwapStrength" );
	static readonly int ViewSwapSharpnessId = Shader.PropertyToID( "_SparkleViewSwapSharpness" );
	static readonly int ViewSwapThresholdId = Shader.PropertyToID( "_SparkleViewSwapThreshold" );
	static readonly int BaseGlintId = Shader.PropertyToID( "_SparkleBaseGlint" );
	static readonly int LitBoostId = Shader.PropertyToID( "_SparkleLitBoost" );
	static readonly int BrightnessJitterId = Shader.PropertyToID( "_SparkleBrightnessJitter" );
	static readonly int CoinFacetJitterId = Shader.PropertyToID( "_SparkleCoinFacetJitter" );
	static readonly int CoinFacingThresholdId = Shader.PropertyToID( "_SparkleCoinFacingThreshold" );
	static readonly int CoinFacingPowerId = Shader.PropertyToID( "_SparkleCoinFacingPower" );
	static readonly int MetallicSharpnessId = Shader.PropertyToID( "_SparkleMetallicSharpness" );
	static readonly int LightKillStrengthId = Shader.PropertyToID( "_SparkleLightKillStrength" );
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
	static readonly int FacingThresholdId = Shader.PropertyToID( "_SparkleFacingThreshold" );
	static readonly int SlopeLimitId = Shader.PropertyToID( "_SparkleSlopeLimit" );
	static readonly int ScreenEdgeFadeId = Shader.PropertyToID( "_SparkleScreenEdgeFade" );
	static readonly int SilhouetteFadeId = Shader.PropertyToID( "_SparkleSilhouetteFade" );
	static readonly int MaskErodePixelsId = Shader.PropertyToID( "_SparkleMaskErodePixels" );
	static readonly int TwinkleStrengthId = Shader.PropertyToID( "_SparkleTwinkleStrength" );
	static readonly int TwinkleSpeedId = Shader.PropertyToID( "_SparkleTwinkleSpeed" );
	static readonly int TwinkleDesyncId = Shader.PropertyToID( "_SparkleTwinkleDesync" );
	static readonly int BloomContributionId = Shader.PropertyToID( "_SparkleBloomContribution" );
	static readonly int MaxActiveId = Shader.PropertyToID( "_SparkleMaxActive" );
	static readonly int ColorId = Shader.PropertyToID( "_SparkleColor" );
	static readonly int GemDensityMulId = Shader.PropertyToID( "_SparkleGemDensityMul" );
	static readonly int ArtifactDensityMulId = Shader.PropertyToID( "_SparkleArtifactDensityMul" );
	static readonly int DebugModeId = Shader.PropertyToID( "_SparkleDebugMode" );

	public const float MaskPile = 0.25f;
	public const float MaskCoin = 0.5f;
	public const float MaskGem = 0.75f;
	public const float MaskArtifact = 1f;

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
		layerCount = Mathf.Clamp( layerCount, 1, 3 );
		targetPixelSize = Mathf.Clamp( targetPixelSize, 0.25f, 6f );
		screenDensityRefPixels = Mathf.Clamp( screenDensityRefPixels, 2f, 48f );
		glintTextureSize = Mathf.Clamp( glintTextureSize, 0.25f, 12f );
		coreHotness = Mathf.Clamp01( coreHotness );
		litBoost = Mathf.Clamp( litBoost, 0f, 16f );
		bloomContribution = Mathf.Clamp( bloomContribution, 0f, 8f );
		coinFacetJitter = Mathf.Clamp( coinFacetJitter, 0f, 1.5f );
		metallicSharpness = Mathf.Clamp( metallicSharpness, 2f, 128f );
		lightKillStrength = Mathf.Clamp01( lightKillStrength );
		viewSwapStrength = Mathf.Clamp01( viewSwapStrength );
		silhouetteFade = Mathf.Clamp01( silhouetteFade );
		maskErodePixels = Mathf.Clamp( maskErodePixels, 0, 3 );
		twinkleStrength = Mathf.Clamp01( twinkleStrength );
		twinkleSpeed = Mathf.Max( 0f, twinkleSpeed );
		closeFadeDistance = Mathf.Max( 0f, closeFadeDistance );
		closeFadeFullDistance = Mathf.Max( closeFadeDistance + 0.05f, closeFadeFullDistance );
		gemDensityMultiplier = Mathf.Max( 1f, gemDensityMultiplier );
		artifactDensityMultiplier = Mathf.Max( 1f, artifactDensityMultiplier );
	}

	public void ApplyToMaterial( Material material )
	{
		if ( material == null )
			return;

		Validate();
		material.SetFloat( EnableId, enableSparkles ? 1f : 0f );
		material.SetFloat( CellSizeId, cellSize );
		material.SetFloat( DensityId, density );
		material.SetFloat( MaxActiveId, density );
		material.SetFloat( LayerCountId, layerCount );
		material.SetFloat( LayerScaleId, 1.7f );
		material.SetFloat( MinSizeId, 0.9f );
		material.SetFloat( MaxSizeId, 1.1f );
		material.SetFloat( SpotFalloffId, 1.8f );
		material.SetFloat( ScreenSpaceBlendId, 1f );
		material.SetFloat( TargetPixelSizeId, targetPixelSize );
		material.SetFloat( ScreenDensityBlendId, 1f );
		material.SetFloat( ScreenDensityRefPixelsId, screenDensityRefPixels );

		bool useGlintTex = glintTexture != null;
		material.SetFloat( UseGlintTextureId, useGlintTex ? 1f : 0f );
		material.SetFloat( GlintTextureBlendId, useGlintTex ? 1f : 0f );
		material.SetFloat( GlintTextureSizeId, glintTextureSize );
		material.SetTexture( GlintTextureId, useGlintTex ? glintTexture : Texture2D.whiteTexture );

		material.SetColor( ColorId, sparkleColor );
		material.SetFloat( LitBoostId, litBoost );
		material.SetFloat( BloomContributionId, bloomContribution );
		material.SetFloat( CoreHotnessId, coreHotness );
		material.SetFloat( BaseGlintId, 0f );
		material.SetFloat( BrightnessJitterId, 0.2f );

		material.SetFloat( CoinFacetJitterId, coinFacetJitter );
		material.SetFloat( CoinFacingThresholdId, 0f );
		material.SetFloat( CoinFacingPowerId, 1f );
		material.SetFloat( MetallicSharpnessId, metallicSharpness );
		material.SetFloat( LightKillStrengthId, lightKillStrength );
		material.SetFloat( SoftLobeAmountId, 0.08f );
		material.SetFloat( AlignmentThresholdId, 0.35f );
		material.SetFloat( FadeAngleId, 18f * Mathf.Deg2Rad );

		material.SetFloat( ViewSwapStrengthId, viewSwapStrength );
		material.SetFloat( ViewSwapSharpnessId, 6f );
		material.SetFloat( ViewSwapThresholdId, 0.45f );
		material.SetFloat( ViewCrossfadeId, 0.3f );

		material.SetFloat( SeedId, randomSeed );
		material.SetFloat( NearGlintStartId, closeFadeDistance );
		material.SetFloat( NearGlintFullId, closeFadeFullDistance );
		material.SetFloat( FarGlintStartId, 35f );
		material.SetFloat( FarGlintEndId, 90f );
		material.SetFloat( NearDensityScaleId, 1f );
		material.SetFloat( FarDensityScaleId, 0.35f );
		material.SetFloat( NearIntensityFloorId, 0f );
		material.SetFloat( GemDensityMulId, gemDensityMultiplier );
		material.SetFloat( ArtifactDensityMulId, artifactDensityMultiplier );
		material.SetFloat( FacingThresholdId, 0f );
		material.SetFloat( SlopeLimitId, 0f );
		material.SetFloat( ScreenEdgeFadeId, 0.015f );
		material.SetFloat( SilhouetteFadeId, silhouetteFade );
		material.SetFloat( MaskErodePixelsId, maskErodePixels );
		material.SetFloat( TwinkleStrengthId, twinkleStrength );
		material.SetFloat( TwinkleSpeedId, twinkleSpeed );
		material.SetFloat( TwinkleDesyncId, 0.85f );
		material.SetFloat( DebugModeId, (float)debugMode );
	}
}
