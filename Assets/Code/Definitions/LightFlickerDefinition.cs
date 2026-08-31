using System;

using UnityEngine;

/// <summary>
/// Named light-flicker presets for <see cref="LightFlicker"/>.
/// Asset name must be <c>LightFlickerDefinition</c> for <see cref="GameInstance.GetDefinition{T}"/>.
/// </summary>
[CreateAssetMenu( fileName = "LightFlickerDefinition", menuName = "Definitions/LightFlickerDefinition" )]
public class LightFlickerDefinition : ScriptableObject
{
	[Tooltip( "Named flicker styles. Component selects one by id (case-insensitive)." )]
	public LightFlickerPreset[] presets = CreateDefaultPresets();

	public bool TryGetPreset( string id, out LightFlickerPreset preset )
	{
		preset = null;
		if ( string.IsNullOrEmpty( id ) || presets == null )
			return false;

		for ( int i = 0; i < presets.Length; i++ )
		{
			LightFlickerPreset candidate = presets[i];
			if ( candidate == null || string.IsNullOrEmpty( candidate.id ) )
				continue;

			if ( string.Equals( candidate.id, id, StringComparison.OrdinalIgnoreCase ) )
			{
				preset = candidate;
				return true;
			}
		}

		return false;
	}

	public LightFlickerPreset GetPresetOrDefault( string id )
	{
		if ( TryGetPreset( id, out LightFlickerPreset preset ) )
			return preset;

		if ( presets != null )
		{
			for ( int i = 0; i < presets.Length; i++ )
			{
				if ( presets[i] != null )
					return presets[i];
			}
		}

		return LightFlickerPreset.Lantern();
	}

	void OnValidate()
	{
		if ( presets == null || presets.Length == 0 )
			presets = CreateDefaultPresets();

		for ( int i = 0; i < presets.Length; i++ )
		{
			if ( presets[i] == null )
				presets[i] = LightFlickerPreset.Lantern();
			else
				presets[i].Validate();
		}
	}

	public static LightFlickerPreset[] CreateDefaultPresets()
	{
		return new[]
		{
			LightFlickerPreset.Lantern(),
			LightFlickerPreset.Flame(),
			LightFlickerPreset.Torch(),
			LightFlickerPreset.Candle(),
			LightFlickerPreset.Campfire(),
			LightFlickerPreset.Magical(),
			LightFlickerPreset.Ember()
		};
	}
}

public enum LightFlickerPresetKind
{
	Lantern = 0,
	Flame = 1,
	Torch = 2,
	Candle = 3,
	Campfire = 4,
	Magical = 5,
	Ember = 6,
	Custom = 100
}

[Serializable]
public class LightFlickerPreset
{
	[Tooltip( "Lookup id used by LightFlicker (e.g. Lantern, Flame)." )]
	public string id = "Lantern";

	[Header( "Intensity" )]
	[Tooltip( "Minimum intensity as a multiplier of the light's authored intensity." )]
	[Range( 0f, 2f )]
	public float intensityMin = 0.92f;

	[Tooltip( "Maximum intensity as a multiplier of the light's authored intensity." )]
	[Range( 0f, 2f )]
	public float intensityMax = 1.05f;

	[Tooltip( "How fast the noise evolves. Higher = snappier flicker." )]
	[Min( 0f )]
	public float speed = 1.2f;

	[Tooltip( "Blend toward the noise target each second. 0 = hard cuts, higher = smoother." )]
	[Min( 0f )]
	public float smoothness = 12f;

	[Header( "Noise Shape" )]
	[Tooltip( "Weight of the slow primary noise layer." )]
	[Range( 0f, 1f )]
	public float primaryWeight = 0.55f;

	[Tooltip( "Weight of the medium secondary layer." )]
	[Range( 0f, 1f )]
	public float secondaryWeight = 0.3f;

	[Tooltip( "Weight of the fast tertiary layer (sparks / sputters)." )]
	[Range( 0f, 1f )]
	public float tertiaryWeight = 0.15f;

	[Tooltip( "Secondary layer speed relative to primary." )]
	[Min( 1f )]
	public float secondarySpeed = 2.15f;

	[Tooltip( "Tertiary layer speed relative to primary." )]
	[Min( 1f )]
	public float tertiarySpeed = 4.4f;

	[Header( "Color" )]
	public bool affectColor = false;

	[Tooltip( "How far light color oscillates between tint A and B. 0 = midpoint, 1 = full A-B." )]
	[Range( 0f, 1f )]
	public float colorAmount = 0.2f;

	[Tooltip( "Color written to the Light." )]
	[ColorUsage( false, true )]
	public Color tintA = new Color( 1f, 0.92f, 0.75f, 1f );

	[Tooltip( "Color written to the Light." )]
	[ColorUsage( false, true )]
	public Color tintB = new Color( 1f, 0.78f, 0.45f, 1f );

	[Header( "Emission" )]
	[Tooltip( "When on, writes emissionTintA/B directly to _EmissionColor. When off, pulses the material's authored emission." )]
	public bool affectEmissionColor = true;

	[Tooltip( "How far emission oscillates between tint A and B. 0 = midpoint, 1 = full A-B." )]
	[Range( 0f, 1f )]
	public float emissionColorAmount = 0.45f;

	[Tooltip( "HDR color written to the emissive shader." )]
	[ColorUsage( false, true )]
	public Color emissionTintA = new Color( 1.8f, 1.1f, 0.4f, 1f );

	[Tooltip( "HDR color written to the emissive shader." )]
	[ColorUsage( false, true )]
	public Color emissionTintB = new Color( 2.2f, 0.65f, 0.12f, 1f );

	[Header( "Range" )]
	public bool affectRange = false;

	[Tooltip( "Minimum range as a multiplier of the light's authored range." )]
	[Range( 0.25f, 2f )]
	public float rangeMin = 0.95f;

	[Tooltip( "Maximum range as a multiplier of the light's authored range." )]
	[Range( 0.25f, 2f )]
	public float rangeMax = 1.05f;

	public void Validate()
	{
		if ( string.IsNullOrWhiteSpace( id ) )
			id = "Lantern";

		intensityMin = Mathf.Clamp( intensityMin, 0f, 2f );
		intensityMax = Mathf.Clamp( Mathf.Max( intensityMin, intensityMax ), 0f, 2f );
		speed = Mathf.Max( 0f, speed );
		smoothness = Mathf.Max( 0f, smoothness );
		primaryWeight = Mathf.Clamp01( primaryWeight );
		secondaryWeight = Mathf.Clamp01( secondaryWeight );
		tertiaryWeight = Mathf.Clamp01( tertiaryWeight );
		secondarySpeed = Mathf.Max( 1f, secondarySpeed );
		tertiarySpeed = Mathf.Max( 1f, tertiarySpeed );
		colorAmount = Mathf.Clamp01( colorAmount );
		emissionColorAmount = Mathf.Clamp01( emissionColorAmount );
		rangeMin = Mathf.Clamp( rangeMin, 0.25f, 2f );
		rangeMax = Mathf.Clamp( Mathf.Max( rangeMin, rangeMax ), 0.25f, 2f );
	}

	public Color GetLightColor( float noise )
	{
		float t = Mathf.Lerp( 0.5f, Mathf.Clamp01( noise ), colorAmount );
		return Color.Lerp( tintA, tintB, t );
	}

	public Color GetEmissionColor( float noise )
	{
		float t = Mathf.Lerp( 0.5f, Mathf.Clamp01( noise ), emissionColorAmount );
		return Color.Lerp( emissionTintA, emissionTintB, t );
	}

	public static LightFlickerPreset Lantern()
	{
		return new LightFlickerPreset
		{
			id = nameof( LightFlickerPresetKind.Lantern ),
			intensityMin = 0.92f,
			intensityMax = 1.05f,
			speed = 1.1f,
			smoothness = 14f,
			primaryWeight = 0.6f,
			secondaryWeight = 0.28f,
			tertiaryWeight = 0.12f,
			affectColor = true,
			colorAmount = 0.12f,
			tintA = new Color( 1f, 0.94f, 0.8f, 1f ),
			tintB = new Color( 1f, 0.86f, 0.62f, 1f ),
			affectEmissionColor = true,
			emissionColorAmount = 0.45f,
			emissionTintA = new Color( 1.8f, 1.1f, 0.4f, 1f ),
			emissionTintB = new Color( 2.2f, 0.65f, 0.12f, 1f ),
			affectRange = true,
			rangeMin = 0.95f,
			rangeMax = 1.05f
		};
	}

	public static LightFlickerPreset Flame()
	{
		return new LightFlickerPreset
		{
			id = nameof( LightFlickerPresetKind.Flame ),
			intensityMin = 0.7f,
			intensityMax = 1.18f,
			speed = 3.2f,
			smoothness = 18f,
			primaryWeight = 0.45f,
			secondaryWeight = 0.35f,
			tertiaryWeight = 0.2f,
			secondarySpeed = 2.4f,
			tertiarySpeed = 5.2f,
			affectColor = true,
			colorAmount = 0.28f,
			tintA = new Color( 1f, 0.9f, 0.55f, 1f ),
			tintB = new Color( 1f, 0.55f, 0.2f, 1f ),
			affectEmissionColor = true,
			emissionColorAmount = 0.55f,
			emissionTintA = new Color( 2.2f, 1.2f, 0.25f, 1f ),
			emissionTintB = new Color( 2.8f, 0.45f, 0.05f, 1f ),
			affectRange = true,
			rangeMin = 0.88f,
			rangeMax = 1.08f
		};
	}

	public static LightFlickerPreset Torch()
	{
		return new LightFlickerPreset
		{
			id = nameof( LightFlickerPresetKind.Torch ),
			intensityMin = 0.62f,
			intensityMax = 1.22f,
			speed = 2.6f,
			smoothness = 10f,
			primaryWeight = 0.4f,
			secondaryWeight = 0.35f,
			tertiaryWeight = 0.25f,
			secondarySpeed = 2.8f,
			tertiarySpeed = 6f,
			affectColor = true,
			colorAmount = 0.22f,
			tintA = new Color( 1f, 0.88f, 0.6f, 1f ),
			tintB = new Color( 1f, 0.62f, 0.28f, 1f ),
			affectEmissionColor = true,
			emissionColorAmount = 0.5f,
			emissionTintA = new Color( 2.0f, 1.15f, 0.32f, 1f ),
			emissionTintB = new Color( 2.5f, 0.55f, 0.08f, 1f ),
			affectRange = true,
			rangeMin = 0.85f,
			rangeMax = 1.12f
		};
	}

	public static LightFlickerPreset Candle()
	{
		return new LightFlickerPreset
		{
			id = nameof( LightFlickerPresetKind.Candle ),
			intensityMin = 0.9f,
			intensityMax = 1.08f,
			speed = 0.85f,
			smoothness = 8f,
			primaryWeight = 0.7f,
			secondaryWeight = 0.22f,
			tertiaryWeight = 0.08f,
			affectColor = true,
			colorAmount = 0.1f,
			tintA = new Color( 1f, 0.95f, 0.82f, 1f ),
			tintB = new Color( 1f, 0.88f, 0.65f, 1f ),
			affectEmissionColor = true,
			emissionColorAmount = 0.28f,
			emissionTintA = new Color( 1.5f, 1.15f, 0.55f, 1f ),
			emissionTintB = new Color( 1.8f, 0.85f, 0.28f, 1f ),
			affectRange = false
		};
	}

	public static LightFlickerPreset Campfire()
	{
		return new LightFlickerPreset
		{
			id = nameof( LightFlickerPresetKind.Campfire ),
			intensityMin = 0.55f,
			intensityMax = 1.28f,
			speed = 1.6f,
			smoothness = 7f,
			primaryWeight = 0.5f,
			secondaryWeight = 0.3f,
			tertiaryWeight = 0.2f,
			secondarySpeed = 2.1f,
			tertiarySpeed = 3.8f,
			affectColor = true,
			colorAmount = 0.35f,
			tintA = new Color( 1f, 0.82f, 0.4f, 1f ),
			tintB = new Color( 1f, 0.4f, 0.12f, 1f ),
			affectEmissionColor = true,
			emissionColorAmount = 0.6f,
			emissionTintA = new Color( 2.4f, 1.05f, 0.18f, 1f ),
			emissionTintB = new Color( 3.0f, 0.35f, 0.04f, 1f ),
			affectRange = true,
			rangeMin = 0.8f,
			rangeMax = 1.18f
		};
	}

	public static LightFlickerPreset Magical()
	{
		return new LightFlickerPreset
		{
			id = nameof( LightFlickerPresetKind.Magical ),
			intensityMin = 0.82f,
			intensityMax = 1.12f,
			speed = 1.4f,
			smoothness = 6f,
			primaryWeight = 0.55f,
			secondaryWeight = 0.3f,
			tertiaryWeight = 0.15f,
			affectColor = true,
			colorAmount = 0.55f,
			tintA = new Color( 0.55f, 0.85f, 1.4f, 1f ),
			tintB = new Color( 1.1f, 0.45f, 1.35f, 1f ),
			affectEmissionColor = true,
			emissionColorAmount = 0.7f,
			emissionTintA = new Color( 0.35f, 1.4f, 2.4f, 1f ),
			emissionTintB = new Color( 2.2f, 0.35f, 2.1f, 1f ),
			affectRange = true,
			rangeMin = 0.92f,
			rangeMax = 1.08f
		};
	}

	public static LightFlickerPreset Ember()
	{
		return new LightFlickerPreset
		{
			id = nameof( LightFlickerPresetKind.Ember ),
			intensityMin = 0.35f,
			intensityMax = 1.05f,
			speed = 0.55f,
			smoothness = 4f,
			primaryWeight = 0.75f,
			secondaryWeight = 0.2f,
			tertiaryWeight = 0.05f,
			secondarySpeed = 1.6f,
			tertiarySpeed = 3.2f,
			affectColor = true,
			colorAmount = 0.4f,
			tintA = new Color( 1f, 0.45f, 0.12f, 1f ),
			tintB = new Color( 0.55f, 0.12f, 0.02f, 1f ),
			affectEmissionColor = true,
			emissionColorAmount = 0.65f,
			emissionTintA = new Color( 2.6f, 0.55f, 0.08f, 1f ),
			emissionTintB = new Color( 1.2f, 0.12f, 0.02f, 1f ),
			affectRange = true,
			rangeMin = 0.7f,
			rangeMax = 1.05f
		};
	}
}
