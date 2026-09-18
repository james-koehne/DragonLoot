#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Session-persistent sculpt brush settings for gold pile authoring.
/// Survives inspector rebuilds and pile selection changes.
/// </summary>
static class GoldPileSculptSettings
{
	const string PrefPrefix = "DragonLoot.GoldPileSculpt.";

	static Texture2D s_stampMask;

	public const float MinRadius = 0.1f;
	public const float MaxRadius = 128f;
	public const float MinStrength = 0.01f;
	public const float MaxStrength = 4f;
	public const float MaxFlattenTarget = 32f;
	public const float MinPeakHeight = -16f;
	public const float MaxPeakHeight = 16f;

	public const int SettleItersPerDrag = 4;
	public const int ErodeItersPerDrag = 8;
	public const int SettleItersStrokeEnd = 12;
	public const int ErodeItersStrokeEnd = 24;

	public static GoldPileEditorBrushMode BrushMode
	{
		get => ( GoldPileEditorBrushMode )EditorPrefs.GetInt( PrefPrefix + "BrushMode", ( int )GoldPileEditorBrushMode.Raise );
		set => EditorPrefs.SetInt( PrefPrefix + "BrushMode", ( int )value );
	}

	public static float BrushRadius
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "Radius", 1.5f );
		set => EditorPrefs.SetFloat( PrefPrefix + "Radius", Mathf.Clamp( value, MinRadius, MaxRadius ) );
	}

	public static float BrushStrength
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "Strength", 0.25f );
		set => EditorPrefs.SetFloat( PrefPrefix + "Strength", Mathf.Clamp( value, MinStrength, MaxStrength ) );
	}

	public static float BrushFalloff
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "Falloff", 1f );
		set => EditorPrefs.SetFloat( PrefPrefix + "Falloff", Mathf.Clamp( value, 0.2f, 4f ) );
	}

	public static float FlattenTarget
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "FlattenTarget", 1f );
		set => EditorPrefs.SetFloat( PrefPrefix + "FlattenTarget", Mathf.Clamp( value, 0f, MaxFlattenTarget ) );
	}

	public static Texture2D StampMask
	{
		get
		{
			if ( s_stampMask == null )
			{
				string guid = EditorPrefs.GetString( PrefPrefix + "StampMaskGuid", string.Empty );
				if ( !string.IsNullOrEmpty( guid ) )
				{
					string path = AssetDatabase.GUIDToAssetPath( guid );
					if ( !string.IsNullOrEmpty( path ) )
						s_stampMask = AssetDatabase.LoadAssetAtPath<Texture2D>( path );
				}
			}

			return s_stampMask;
		}
		set
		{
			s_stampMask = value;
			string guid = string.Empty;
			if ( value != null )
			{
				string path = AssetDatabase.GetAssetPath( value );
				if ( !string.IsNullOrEmpty( path ) )
					guid = AssetDatabase.AssetPathToGUID( path );
			}

			EditorPrefs.SetString( PrefPrefix + "StampMaskGuid", guid );
		}
	}

	public static bool StampInvert
	{
		get => EditorPrefs.GetBool( PrefPrefix + "StampInvert", false );
		set => EditorPrefs.SetBool( PrefPrefix + "StampInvert", value );
	}

	public static bool StampFullFootprint
	{
		get => EditorPrefs.GetBool( PrefPrefix + "StampFullFootprint", false );
		set => EditorPrefs.SetBool( PrefPrefix + "StampFullFootprint", value );
	}

	public static float AngleOfRepose
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "AngleOfRepose", 35f );
		set => EditorPrefs.SetFloat( PrefPrefix + "AngleOfRepose", Mathf.Clamp( value, 5f, 60f ) );
	}

	public static int Iterations
	{
		get => EditorPrefs.GetInt( PrefPrefix + "Iterations", 8 );
		set => EditorPrefs.SetInt( PrefPrefix + "Iterations", Mathf.Clamp( value, 1, 50 ) );
	}

	public static float NoiseFrequency
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "NoiseFrequency", 2f );
		set => EditorPrefs.SetFloat( PrefPrefix + "NoiseFrequency", Mathf.Clamp( value, 0.1f, 16f ) );
	}

	public static int NoiseOctaves
	{
		get => EditorPrefs.GetInt( PrefPrefix + "NoiseOctaves", 3 );
		set => EditorPrefs.SetInt( PrefPrefix + "NoiseOctaves", Mathf.Clamp( value, 1, 8 ) );
	}

	public static int NoiseSeed
	{
		get => EditorPrefs.GetInt( PrefPrefix + "NoiseSeed", 0 );
		set => EditorPrefs.SetInt( PrefPrefix + "NoiseSeed", value );
	}

	public static float PeakHeight
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "PeakHeight", 1f );
		set => EditorPrefs.SetFloat( PrefPrefix + "PeakHeight", Mathf.Clamp( value, MinPeakHeight, MaxPeakHeight ) );
	}

	public static bool SettleAfterPeak
	{
		get => EditorPrefs.GetBool( PrefPrefix + "SettleAfterPeak", true );
		set => EditorPrefs.SetBool( PrefPrefix + "SettleAfterPeak", value );
	}

	public static float RidgeWidth
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "RidgeWidth", 1.2f );
		set => EditorPrefs.SetFloat( PrefPrefix + "RidgeWidth", Mathf.Clamp( value, 0.1f, 16f ) );
	}

	public static float ProfileFalloff
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "ProfileFalloff", 0.85f );
		set => EditorPrefs.SetFloat( PrefPrefix + "ProfileFalloff", Mathf.Clamp( value, 0.1f, 0.99f ) );
	}

	public static float SplineSmooth
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "SplineSmooth", 0.5f );
		set => EditorPrefs.SetFloat( PrefPrefix + "SplineSmooth", Mathf.Clamp01( value ) );
	}

	public static bool NeedsStrength( GoldPileEditorBrushMode mode )
	{
		switch ( mode )
		{
			case GoldPileEditorBrushMode.Raise:
			case GoldPileEditorBrushMode.Lower:
			case GoldPileEditorBrushMode.Smooth:
			case GoldPileEditorBrushMode.Flatten:
			case GoldPileEditorBrushMode.Stamp:
			case GoldPileEditorBrushMode.Flow:
			case GoldPileEditorBrushMode.Inflate:
			case GoldPileEditorBrushMode.Scrape:
			case GoldPileEditorBrushMode.Pinch:
			case GoldPileEditorBrushMode.Noise:
			case GoldPileEditorBrushMode.Erode:
			case GoldPileEditorBrushMode.Fill:
				return true;
			default:
				return false;
		}
	}

	public static bool NeedsFalloff( GoldPileEditorBrushMode mode )
	{
		switch ( mode )
		{
			case GoldPileEditorBrushMode.Raise:
			case GoldPileEditorBrushMode.Lower:
			case GoldPileEditorBrushMode.Smooth:
			case GoldPileEditorBrushMode.Flatten:
			case GoldPileEditorBrushMode.Stamp:
			case GoldPileEditorBrushMode.Flow:
			case GoldPileEditorBrushMode.Inflate:
			case GoldPileEditorBrushMode.Scrape:
			case GoldPileEditorBrushMode.Noise:
			case GoldPileEditorBrushMode.Fill:
			case GoldPileEditorBrushMode.Ridge:
				return true;
			default:
				return false;
		}
	}

	public static GoldPileBrushParams BuildParams( int iterationsOverride )
	{
		GoldPileBrushParams p = GoldPileBrushParams.Default;
		p.radius = BrushRadius;
		p.strength = BrushStrength;
		p.falloff = BrushFalloff;
		p.flattenTarget = FlattenTarget;
		p.stampMask = StampMask;
		p.stampInvert = StampInvert;
		p.stampFullFootprint = StampFullFootprint;
		p.angleOfReposeDegrees = AngleOfRepose;
		p.iterations = iterationsOverride > 0 ? iterationsOverride : Iterations;
		p.noiseFrequency = NoiseFrequency;
		p.noiseOctaves = NoiseOctaves;
		p.noiseSeed = NoiseSeed;
		p.peakHeight = PeakHeight;
		p.settleAfterPeak = SettleAfterPeak;
		p.ridgeWidth = RidgeWidth;
		p.profileFalloff = ProfileFalloff;
		p.splineSmooth = SplineSmooth;
		return p;
	}

	public static GoldPileBrushParams BuildParamsForDrag( GoldPileEditorBrushMode mode )
	{
		int iters = Iterations;
		if ( mode == GoldPileEditorBrushMode.Settle )
			iters = Mathf.Min( Iterations, SettleItersPerDrag );
		else if ( mode == GoldPileEditorBrushMode.Erode )
			iters = Mathf.Min( Iterations, ErodeItersPerDrag );
		return BuildParams( iters );
	}

	public static void DrawModeControls( GoldPileEditorBrushMode mode )
	{
		bool showRadius = mode != GoldPileEditorBrushMode.None && mode != GoldPileEditorBrushMode.ResetMound;
		bool showStrength = NeedsStrength( mode );
		bool showFalloff = NeedsFalloff( mode );

		if ( showRadius )
			BrushRadius = EditorGUILayout.Slider( "Radius (m)", BrushRadius, MinRadius, MaxRadius );

		if ( mode == GoldPileEditorBrushMode.Ridge )
			RidgeWidth = EditorGUILayout.Slider( "Width (m)", RidgeWidth, 0.1f, 16f );

		if ( showStrength )
			BrushStrength = EditorGUILayout.Slider( "Strength (m)", BrushStrength, MinStrength, MaxStrength );

		if ( mode == GoldPileEditorBrushMode.Peak || mode == GoldPileEditorBrushMode.Ridge )
			PeakHeight = EditorGUILayout.Slider( "Height (m)", PeakHeight, MinPeakHeight, MaxPeakHeight );

		if ( showFalloff )
			BrushFalloff = EditorGUILayout.Slider( "Falloff", BrushFalloff, 0.2f, 4f );

		if ( mode == GoldPileEditorBrushMode.Peak )
		{
			ProfileFalloff = EditorGUILayout.Slider( "Profile Falloff", ProfileFalloff, 0.1f, 0.99f );
			SettleAfterPeak = EditorGUILayout.Toggle( "Settle After", SettleAfterPeak );
		}

		if ( mode == GoldPileEditorBrushMode.Ridge )
			SplineSmooth = EditorGUILayout.Slider( "Spline Smoothing", SplineSmooth, 0f, 1f );

		if ( mode == GoldPileEditorBrushMode.Flatten )
			FlattenTarget = EditorGUILayout.Slider( "Flatten Target (m)", FlattenTarget, 0f, MaxFlattenTarget );

		if ( mode == GoldPileEditorBrushMode.Settle )
		{
			Iterations = EditorGUILayout.IntSlider( "Iterations", Iterations, 4, 20 );
			AngleOfRepose = EditorGUILayout.Slider( "Angle Of Repose", AngleOfRepose, 5f, 60f );
		}

		if ( mode == GoldPileEditorBrushMode.Erode )
			Iterations = EditorGUILayout.IntSlider( "Iterations", Iterations, 10, 50 );

		if ( mode == GoldPileEditorBrushMode.Noise )
		{
			NoiseFrequency = EditorGUILayout.Slider( "Frequency", NoiseFrequency, 0.1f, 16f );
			NoiseOctaves = EditorGUILayout.IntSlider( "Octaves", NoiseOctaves, 1, 8 );
			NoiseSeed = EditorGUILayout.IntField( "Seed", NoiseSeed );
		}

		if ( mode == GoldPileEditorBrushMode.Stamp )
		{
			StampMask = ( Texture2D )EditorGUILayout.ObjectField( "Stamp Mask", StampMask, typeof( Texture2D ), false );
			StampInvert = EditorGUILayout.Toggle( "Invert (Lower)", StampInvert );
			StampFullFootprint = EditorGUILayout.Toggle( "Full Footprint", StampFullFootprint );
			EditorGUILayout.HelpBox(
				"Mask must be Read/Write enabled. Grayscale drives raise/lower weight.",
				MessageType.None );
		}
	}

	public static Color BrushColor( GoldPileEditorBrushMode mode )
	{
		switch ( mode )
		{
			case GoldPileEditorBrushMode.Raise:
				return new Color( 1f, 0.85f, 0.2f, 0.35f );
			case GoldPileEditorBrushMode.Lower:
				return new Color( 1f, 0.35f, 0.15f, 0.35f );
			case GoldPileEditorBrushMode.Smooth:
				return new Color( 0.35f, 0.7f, 1f, 0.35f );
			case GoldPileEditorBrushMode.Flatten:
				return new Color( 0.6f, 0.4f, 1f, 0.35f );
			case GoldPileEditorBrushMode.Stamp:
				return new Color( 0.2f, 1f, 0.55f, 0.35f );
			case GoldPileEditorBrushMode.Settle:
				return new Color( 0.9f, 0.6f, 0.2f, 0.35f );
			case GoldPileEditorBrushMode.Flow:
				return new Color( 0.2f, 0.75f, 1f, 0.35f );
			case GoldPileEditorBrushMode.Inflate:
				return new Color( 1f, 0.5f, 0.8f, 0.35f );
			case GoldPileEditorBrushMode.Scrape:
				return new Color( 0.85f, 0.75f, 0.4f, 0.35f );
			case GoldPileEditorBrushMode.Pinch:
				return new Color( 1f, 0.3f, 0.5f, 0.35f );
			case GoldPileEditorBrushMode.Noise:
				return new Color( 0.5f, 0.9f, 0.4f, 0.35f );
			case GoldPileEditorBrushMode.Erode:
				return new Color( 0.7f, 0.55f, 0.35f, 0.35f );
			case GoldPileEditorBrushMode.Fill:
				return new Color( 0.4f, 0.85f, 0.7f, 0.35f );
			case GoldPileEditorBrushMode.Peak:
				return new Color( 1f, 0.9f, 0.3f, 0.4f );
			case GoldPileEditorBrushMode.Ridge:
				return new Color( 0.95f, 0.7f, 0.25f, 0.4f );
			default:
				return new Color( 1f, 1f, 1f, 0.25f );
		}
	}
}
#endif
