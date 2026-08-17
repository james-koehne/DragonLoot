using UnityEngine;

[System.Serializable]
public class HoverOutlineVisualSettings
{
	static readonly int OutlineColorId = Shader.PropertyToID( "_OutlineColor" );
	static readonly int OutlineIntensityId = Shader.PropertyToID( "_OutlineIntensity" );
	static readonly int HdrBoostId = Shader.PropertyToID( "_HdrBoost" );
	static readonly int ScaleId = Shader.PropertyToID( "_Scale" );
	static readonly int MaskDilatePixelsId = Shader.PropertyToID( "_MaskDilatePixels" );
	static readonly int DepthThresholdId = Shader.PropertyToID( "_DepthThreshold" );
	static readonly int NormalThresholdId = Shader.PropertyToID( "_NormalThreshold" );
	static readonly int DepthNormalThresholdId = Shader.PropertyToID( "_DepthNormalThreshold" );
	static readonly int DepthNormalThresholdScaleId = Shader.PropertyToID( "_DepthNormalThresholdScale" );
	static readonly int NormalEdgeWeightId = Shader.PropertyToID( "_NormalEdgeWeight" );
	static readonly int PulseSpeedId = Shader.PropertyToID( "_PulseSpeed" );
	static readonly int PulseAmountId = Shader.PropertyToID( "_PulseAmount" );

	[ColorUsage( true, true )]
	public Color outlineColor = new Color( 1f, 0.85f, 0.35f, 1f );

	[Range( 0f, 2f )]
	public float outlineIntensity = 1f;

	[Range( 0f, 8f )]
	public float hdrBoost = 1.35f;

	[Range( 1f, 8f )]
	public float scalePixels = 3f;

	[Range( 0f, 8f )]
	public float maskDilatePixels = 2f;

	[Range( 0f, 10f )]
	public float depthThreshold = 1.5f;

	[Range( 0f, 1f )]
	public float normalThreshold = 0.4f;

	[Range( 0f, 1f )]
	public float depthNormalThreshold = 0.5f;

	[Range( 0f, 20f )]
	public float depthNormalThresholdScale = 7f;

	[Range( 0f, 1f )]
	public float normalEdgeWeight = 1f;

	[Min( 0f )]
	public float pulseSpeed = 1.1f;

	[Range( 0f, 1f )]
	public float pulseAmount = 0.1f;

	public static HoverOutlineVisualSettings DefaultPickable()
	{
		HoverOutlineVisualSettings settings = new HoverOutlineVisualSettings();
		Color pickable = PlacementFeedbackColors.PickableHighlight;
		settings.outlineColor = new Color( pickable.r * 1.2f, pickable.g * 1.15f, pickable.b, 1f );
		settings.outlineIntensity = 1f;
		settings.hdrBoost = 1.5f;
		settings.scalePixels = 3f;
		settings.maskDilatePixels = 2f;
		settings.depthThreshold = 1.5f;
		settings.normalThreshold = 0.4f;
		settings.depthNormalThreshold = 0.5f;
		settings.depthNormalThresholdScale = 7f;
		settings.normalEdgeWeight = 1f;
		settings.pulseSpeed = 1.1f;
		settings.pulseAmount = 0.1f;
		return settings;
	}

	public static HoverOutlineVisualSettings DefaultStack()
	{
		HoverOutlineVisualSettings settings = new HoverOutlineVisualSettings();
		Color valid = PlacementFeedbackColors.ValidRgb;
		settings.outlineColor = new Color( valid.r * 1.2f, valid.g * 1.15f, valid.b, 1f );
		settings.outlineIntensity = 1f;
		settings.hdrBoost = 1.35f;
		settings.scalePixels = 4f;
		settings.maskDilatePixels = 3f;
		settings.depthThreshold = 1.5f;
		settings.normalThreshold = 0.4f;
		settings.depthNormalThreshold = 0.5f;
		settings.depthNormalThresholdScale = 7f;
		settings.normalEdgeWeight = 1f;
		settings.pulseSpeed = 0.85f;
		settings.pulseAmount = 0.12f;
		return settings;
	}

	public static HoverOutlineVisualSettings DefaultQuest()
	{
		HoverOutlineVisualSettings settings = new HoverOutlineVisualSettings();
		Color quest = PlacementFeedbackColors.QuestObjectiveHighlight;
		settings.outlineColor = new Color( quest.r * 1.15f, quest.g * 1.2f, quest.b * 1.25f, 1f );
		settings.outlineIntensity = 1.1f;
		settings.hdrBoost = 1.6f;
		settings.scalePixels = 3.5f;
		settings.maskDilatePixels = 2.5f;
		settings.depthThreshold = 1.5f;
		settings.normalThreshold = 0.4f;
		settings.depthNormalThreshold = 0.5f;
		settings.depthNormalThresholdScale = 7f;
		settings.normalEdgeWeight = 1f;
		settings.pulseSpeed = 1.35f;
		settings.pulseAmount = 0.14f;
		return settings;
	}

	public void Validate()
	{
		outlineIntensity = Mathf.Clamp( outlineIntensity, 0f, 2f );
		hdrBoost = Mathf.Clamp( hdrBoost, 0f, 8f );
		scalePixels = Mathf.Clamp( scalePixels, 1f, 8f );
		maskDilatePixels = Mathf.Clamp( maskDilatePixels, 0f, 8f );
		depthThreshold = Mathf.Clamp( depthThreshold, 0f, 10f );
		normalThreshold = Mathf.Clamp01( normalThreshold );
		depthNormalThreshold = Mathf.Clamp01( depthNormalThreshold );
		depthNormalThresholdScale = Mathf.Clamp( depthNormalThresholdScale, 0f, 20f );
		normalEdgeWeight = Mathf.Clamp01( normalEdgeWeight );
		pulseSpeed = Mathf.Max( 0f, pulseSpeed );
		pulseAmount = Mathf.Clamp01( pulseAmount );
	}

	public HoverOutlineVisualSettings WithRgbTint( Color rgb )
	{
		HoverOutlineVisualSettings tinted = Clone();
		tinted.outlineColor = new Color(
			tinted.outlineColor.r * rgb.r,
			tinted.outlineColor.g * rgb.g,
			tinted.outlineColor.b * rgb.b,
			tinted.outlineColor.a );
		return tinted;
	}

	public HoverOutlineVisualSettings Clone()
	{
		return new HoverOutlineVisualSettings
		{
			outlineColor = outlineColor,
			outlineIntensity = outlineIntensity,
			hdrBoost = hdrBoost,
			scalePixels = scalePixels,
			maskDilatePixels = maskDilatePixels,
			depthThreshold = depthThreshold,
			normalThreshold = normalThreshold,
			depthNormalThreshold = depthNormalThreshold,
			depthNormalThresholdScale = depthNormalThresholdScale,
			normalEdgeWeight = normalEdgeWeight,
			pulseSpeed = pulseSpeed,
			pulseAmount = pulseAmount
		};
	}

	public void ApplyToMaterial( Material material )
	{
		if ( material == null )
			return;

		material.SetColor( OutlineColorId, outlineColor );
		material.SetFloat( OutlineIntensityId, outlineIntensity );
		material.SetFloat( HdrBoostId, hdrBoost );
		material.SetFloat( ScaleId, scalePixels );
		material.SetFloat( MaskDilatePixelsId, maskDilatePixels );
		material.SetFloat( DepthThresholdId, depthThreshold );
		material.SetFloat( NormalThresholdId, normalThreshold );
		material.SetFloat( DepthNormalThresholdId, depthNormalThreshold );
		material.SetFloat( DepthNormalThresholdScaleId, depthNormalThresholdScale );
		material.SetFloat( NormalEdgeWeightId, normalEdgeWeight );
		material.SetFloat( PulseSpeedId, pulseSpeed );
		material.SetFloat( PulseAmountId, pulseAmount );
	}

	static readonly int QuestOutlineColorId = Shader.PropertyToID( "_QuestOutlineColor" );
	static readonly int QuestOutlineIntensityId = Shader.PropertyToID( "_QuestOutlineIntensity" );
	static readonly int QuestHdrBoostId = Shader.PropertyToID( "_QuestHdrBoost" );
	static readonly int QuestPulseSpeedId = Shader.PropertyToID( "_QuestPulseSpeed" );
	static readonly int QuestPulseAmountId = Shader.PropertyToID( "_QuestPulseAmount" );

	public void ApplyQuestToMaterial( Material material )
	{
		if ( material == null )
			return;

		material.SetColor( QuestOutlineColorId, outlineColor );
		material.SetFloat( QuestOutlineIntensityId, outlineIntensity );
		material.SetFloat( QuestHdrBoostId, hdrBoost );
		material.SetFloat( QuestPulseSpeedId, pulseSpeed );
		material.SetFloat( QuestPulseAmountId, pulseAmount );
	}
}
