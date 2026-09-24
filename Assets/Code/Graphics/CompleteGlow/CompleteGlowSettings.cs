using System;
using UnityEngine;

public enum CompleteGlowFootprintSource
{
	MeshBounds = 0,
	Custom = 1
}

public enum CompleteGlowAnchor
{
	BoundsTop = 0,
	BoundsBottom = 1,
	BoundsCenter = 2
}

public enum CompleteGlowCrossSection
{
	Square = 0,
	RoundedSquare = 1,
	Circle = 2
}

/// <summary>
/// Serializable config for a procedurally generated complete-glow frustum mesh.
/// </summary>
[Serializable]
public class CompleteGlowSettings
{
	[Header( "Footprint & Placement" )]
	public CompleteGlowFootprintSource FootprintSource = CompleteGlowFootprintSource.MeshBounds;
	public Vector2 CustomFootprint = new Vector2( 1f, 1f );
	public Vector2 FootprintPadding = Vector2.zero;
	public CompleteGlowAnchor Anchor = CompleteGlowAnchor.BoundsTop;
	public float AnchorOffsetY;
	public bool CompensateParentScale = true;

	[Header( "Shape" )]
	[Min( 0.01f )]
	public float Height = 1.5f;
	[Range( 0f, 80f )]
	public float FlareAngleDegrees = 15f;
	public AnimationCurve FlareCurve = AnimationCurve.Linear( 0f, 0f, 1f, 1f );
	public CompleteGlowCrossSection CrossSection = CompleteGlowCrossSection.Square;
	[Min( 0f )]
	public float CornerRadius = 0.1f;
	[Range( 1, 8 )]
	public int CornerSegments = 2;
	[Range( 8, 64 )]
	public int CircleSegments = 24;
	[Range( 1, 32 )]
	public int HeightSegments = 8;
	public bool BottomCap;

	[Header( "Layers" )]
	[Range( 1, 6 )]
	public int LayerCount = 1;
	[Min( 0f )]
	public float LayerOutwardStep = 0.04f;
	[Min( 0f )]
	public float LayerHeightScaleStep = 0.08f;
	[Range( 0.05f, 1f )]
	public float LayerAlphaFalloff = 0.55f;
	[Range( 0f, 30f )]
	public float LayerFlareStep = 3f;
	public bool InnerCore;
	[Range( 0.05f, 1f )]
	public float CoreScale = 0.45f;
	[Range( 0.1f, 1f )]
	public float CoreHeightScale = 0.7f;
	[Min( 0f )]
	public float CoreIntensity = 1.6f;

	[Header( "Light-Ray Cards" )]
	[Range( 0, 8 )]
	public int RayCardCount;
	[Min( 0.05f )]
	public float RayCardWidthScale = 1f;
	[Min( 0.05f )]
	public float RayCardHeightScale = 1f;
	[Range( 0f, 1f )]
	public float RayCardAlpha = 0.35f;
	[Range( 0f, 360f )]
	public float RayCardRandomRotation;
	public int Seed = 1;

	[Header( "Baked Vertex Fades" )]
	[Min( 0f )]
	public float BaseFadeIn = 0.05f;
	public AnimationCurve HeightFalloffCurve = AnimationCurve.Linear( 0f, 1f, 1f, 0f );
	[Range( 0f, 0.5f )]
	public float EdgeSoftness = 0.12f;

	[Header( "Appearance" )]
	public Material MaterialOverride;
	[ColorUsage( true, true )]
	public Color Color = new Color( 1f, 0.72f, 0.28f, 1f );
	[Min( 0f )]
	public float Exposure = 1.5f;
	[Min( 0f )]
	public float AlphaMultiplier = 1f;
	[Range( 0f, 1f )]
	public float NoiseStrength = 0.35f;
	[Min( 0f )]
	public float NoiseScrollSpeed = 0.15f;
	[Min( 0.01f )]
	public float NoiseTiling = 4f;
	[Min( 0f )]
	public float PulseSpeed = 0.4f;
	[Range( 0f, 1f )]
	public float PulseAmount = 0.15f;
	[Min( 0f )]
	public float DepthFadeDistance = 0.25f;

	[Header( "Extras" )]
	public bool AddPointLight;
	public bool LightColorFromGlow = true;
	[Min( 0f )]
	public float LightIntensity = 1.2f;
	[Min( 0.01f )]
	public float LightRange = 3f;
	[Range( 0f, 1f )]
	public float LightHeightT = 0.25f;
	public GameObject DustMotesPrefab;
	public int SortingOrder;

	public static CompleteGlowSettings CreateDefault()
	{
		return new CompleteGlowSettings();
	}

	public CompleteGlowSettings Clone()
	{
		CompleteGlowSettings clone = (CompleteGlowSettings)MemberwiseClone();
		if ( FlareCurve != null )
			clone.FlareCurve = new AnimationCurve( FlareCurve.keys );
		if ( HeightFalloffCurve != null )
			clone.HeightFalloffCurve = new AnimationCurve( HeightFalloffCurve.keys );
		return clone;
	}

	public void CopyFrom( CompleteGlowSettings other )
	{
		if ( other == null )
			return;

		FootprintSource = other.FootprintSource;
		CustomFootprint = other.CustomFootprint;
		FootprintPadding = other.FootprintPadding;
		Anchor = other.Anchor;
		AnchorOffsetY = other.AnchorOffsetY;
		CompensateParentScale = other.CompensateParentScale;
		Height = other.Height;
		FlareAngleDegrees = other.FlareAngleDegrees;
		FlareCurve = other.FlareCurve != null ? new AnimationCurve( other.FlareCurve.keys ) : AnimationCurve.Linear( 0f, 0f, 1f, 1f );
		CrossSection = other.CrossSection;
		CornerRadius = other.CornerRadius;
		CornerSegments = other.CornerSegments;
		CircleSegments = other.CircleSegments;
		HeightSegments = other.HeightSegments;
		BottomCap = other.BottomCap;
		LayerCount = other.LayerCount;
		LayerOutwardStep = other.LayerOutwardStep;
		LayerHeightScaleStep = other.LayerHeightScaleStep;
		LayerAlphaFalloff = other.LayerAlphaFalloff;
		LayerFlareStep = other.LayerFlareStep;
		InnerCore = other.InnerCore;
		CoreScale = other.CoreScale;
		CoreHeightScale = other.CoreHeightScale;
		CoreIntensity = other.CoreIntensity;
		RayCardCount = other.RayCardCount;
		RayCardWidthScale = other.RayCardWidthScale;
		RayCardHeightScale = other.RayCardHeightScale;
		RayCardAlpha = other.RayCardAlpha;
		RayCardRandomRotation = other.RayCardRandomRotation;
		Seed = other.Seed;
		BaseFadeIn = other.BaseFadeIn;
		HeightFalloffCurve = other.HeightFalloffCurve != null ? new AnimationCurve( other.HeightFalloffCurve.keys ) : AnimationCurve.Linear( 0f, 1f, 1f, 0f );
		EdgeSoftness = other.EdgeSoftness;
		MaterialOverride = other.MaterialOverride;
		Color = other.Color;
		Exposure = other.Exposure;
		AlphaMultiplier = other.AlphaMultiplier;
		NoiseStrength = other.NoiseStrength;
		NoiseScrollSpeed = other.NoiseScrollSpeed;
		NoiseTiling = other.NoiseTiling;
		PulseSpeed = other.PulseSpeed;
		PulseAmount = other.PulseAmount;
		DepthFadeDistance = other.DepthFadeDistance;
		AddPointLight = other.AddPointLight;
		LightColorFromGlow = other.LightColorFromGlow;
		LightIntensity = other.LightIntensity;
		LightRange = other.LightRange;
		LightHeightT = other.LightHeightT;
		DustMotesPrefab = other.DustMotesPrefab;
		SortingOrder = other.SortingOrder;
	}
}
