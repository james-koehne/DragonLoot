#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>Quick-start shapes for the procedural gold pile generator.</summary>
public enum GoldPileProceduralPreset
{
	Custom = 0,
	SingleMound = 1,
	TwinPeaks = 2,
	ScatteredHeaps = 3,
	DragonHoard = 4,
	CrescentBank = 5,
	MessySpill = 6
}

/// <summary>
/// Session-persistent settings + generation for procedural gold pile heightmaps.
/// Exposed from the Gold Pile Sculpt overlay.
/// </summary>
public static class GoldPileProceduralGenerator
{
	const string PrefPrefix = "DragonLoot.GoldPileProcGen.";
	const int MaxPiles = 48;
	const float MinWorldSize = 1f;
	const float MaxWorldSize = 128f;

	struct PileSeed
	{
		public float x;
		public float z;
		public float radiusX;
		public float radiusZ;
		public float rotation;
		public float height;
	}

	public static bool AdvancedFoldout
	{
		get => EditorPrefs.GetBool( PrefPrefix + "AdvancedFoldout", false );
		set => EditorPrefs.SetBool( PrefPrefix + "AdvancedFoldout", value );
	}

	public static GoldPileProceduralPreset Preset
	{
		get => ( GoldPileProceduralPreset )EditorPrefs.GetInt( PrefPrefix + "Preset", ( int )GoldPileProceduralPreset.DragonHoard );
		set => EditorPrefs.SetInt( PrefPrefix + "Preset", ( int )value );
	}

	public static float WorldSizeX
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "WorldSizeX", 8f );
		set => EditorPrefs.SetFloat( PrefPrefix + "WorldSizeX", Mathf.Clamp( value, MinWorldSize, MaxWorldSize ) );
	}

	public static float WorldSizeZ
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "WorldSizeZ", 8f );
		set => EditorPrefs.SetFloat( PrefPrefix + "WorldSizeZ", Mathf.Clamp( value, MinWorldSize, MaxWorldSize ) );
	}

	public static bool LockSquare
	{
		get => EditorPrefs.GetBool( PrefPrefix + "LockSquare", true );
		set => EditorPrefs.SetBool( PrefPrefix + "LockSquare", value );
	}

	public static int PileCount
	{
		get => EditorPrefs.GetInt( PrefPrefix + "PileCount", 5 );
		set => EditorPrefs.SetInt( PrefPrefix + "PileCount", Mathf.Clamp( value, 1, MaxPiles ) );
	}

	public static int Seed
	{
		get => EditorPrefs.GetInt( PrefPrefix + "Seed", 1 );
		set => EditorPrefs.SetInt( PrefPrefix + "Seed", value );
	}

	public static float PeakHeightMin
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "PeakHeightMin", 0.7f );
		set => EditorPrefs.SetFloat( PrefPrefix + "PeakHeightMin", Mathf.Clamp( value, 0.05f, 16f ) );
	}

	public static float PeakHeightMax
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "PeakHeightMax", 1.6f );
		set => EditorPrefs.SetFloat( PrefPrefix + "PeakHeightMax", Mathf.Clamp( value, 0.05f, 16f ) );
	}

	public static float RadiusMin
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "RadiusMin", 1.1f );
		set => EditorPrefs.SetFloat( PrefPrefix + "RadiusMin", Mathf.Clamp( value, 0.2f, 64f ) );
	}

	public static float RadiusMax
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "RadiusMax", 2.4f );
		set => EditorPrefs.SetFloat( PrefPrefix + "RadiusMax", Mathf.Clamp( value, 0.2f, 64f ) );
	}

	public static float Spread
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "Spread", 0.72f );
		set => EditorPrefs.SetFloat( PrefPrefix + "Spread", Mathf.Clamp01( value ) );
	}

	public static float Clustering
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "Clustering", 0.4f );
		set => EditorPrefs.SetFloat( PrefPrefix + "Clustering", Mathf.Clamp01( value ) );
	}

	public static float Separation
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "Separation", 0.55f );
		set => EditorPrefs.SetFloat( PrefPrefix + "Separation", Mathf.Clamp( value, 0.1f, 1.5f ) );
	}

	public static float ProfileFalloff
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "ProfileFalloff", 0.78f );
		set => EditorPrefs.SetFloat( PrefPrefix + "ProfileFalloff", Mathf.Clamp( value, 0.1f, 0.99f ) );
	}

	public static float Elongation
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "Elongation", 0.35f );
		set => EditorPrefs.SetFloat( PrefPrefix + "Elongation", Mathf.Clamp01( value ) );
	}

	public static float EdgeMargin
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "EdgeMargin", 0.12f );
		set => EditorPrefs.SetFloat( PrefPrefix + "EdgeMargin", Mathf.Clamp( value, 0f, 0.4f ) );
	}

	public static bool UseMaxBlend
	{
		get => EditorPrefs.GetBool( PrefPrefix + "UseMaxBlend", true );
		set => EditorPrefs.SetBool( PrefPrefix + "UseMaxBlend", value );
	}

	public static float BaseMoundHeight
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "BaseMoundHeight", 0.25f );
		set => EditorPrefs.SetFloat( PrefPrefix + "BaseMoundHeight", Mathf.Clamp( value, 0f, 8f ) );
	}

	public static float BaseMoundFalloff
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "BaseMoundFalloff", 0.92f );
		set => EditorPrefs.SetFloat( PrefPrefix + "BaseMoundFalloff", Mathf.Clamp( value, 0.1f, 0.99f ) );
	}

	public static float RidgeStrength
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "RidgeStrength", 0.35f );
		set => EditorPrefs.SetFloat( PrefPrefix + "RidgeStrength", Mathf.Clamp01( value ) );
	}

	public static float RidgeWidth
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "RidgeWidth", 0.85f );
		set => EditorPrefs.SetFloat( PrefPrefix + "RidgeWidth", Mathf.Clamp( value, 0.1f, 8f ) );
	}

	public static float NoiseAmount
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "NoiseAmount", 0.08f );
		set => EditorPrefs.SetFloat( PrefPrefix + "NoiseAmount", Mathf.Clamp( value, 0f, 2f ) );
	}

	public static float NoiseFrequency
	{
		get => EditorPrefs.GetFloat( PrefPrefix + "NoiseFrequency", 2.4f );
		set => EditorPrefs.SetFloat( PrefPrefix + "NoiseFrequency", Mathf.Clamp( value, 0.1f, 16f ) );
	}

	public static int NoiseOctaves
	{
		get => EditorPrefs.GetInt( PrefPrefix + "NoiseOctaves", 3 );
		set => EditorPrefs.SetInt( PrefPrefix + "NoiseOctaves", Mathf.Clamp( value, 1, 8 ) );
	}

	public static void DrawControls( TreasurePileVisual visual )
	{
		EditorGUILayout.LabelField( "Procedural Generate", EditorStyles.boldLabel );

		EditorGUI.BeginChangeCheck();
		GoldPileProceduralPreset nextPreset = ( GoldPileProceduralPreset )EditorGUILayout.EnumPopup( "Preset", Preset );
		if ( EditorGUI.EndChangeCheck() && nextPreset != Preset )
		{
			Preset = nextPreset;
			if ( nextPreset != GoldPileProceduralPreset.Custom )
				ApplyPreset( nextPreset );
		}

		LockSquare = EditorGUILayout.Toggle( "Square Footprint", LockSquare );
		float sizeX = EditorGUILayout.Slider( "World Size X (m)", WorldSizeX, MinWorldSize, MaxWorldSize );
		if ( !Mathf.Approximately( sizeX, WorldSizeX ) )
		{
			WorldSizeX = sizeX;
			if ( LockSquare )
				WorldSizeZ = sizeX;
			MarkCustom();
		}

		using ( new EditorGUI.DisabledScope( LockSquare ) )
		{
			float sizeZ = EditorGUILayout.Slider( "World Size Z (m)", WorldSizeZ, MinWorldSize, MaxWorldSize );
			if ( !Mathf.Approximately( sizeZ, WorldSizeZ ) )
			{
				WorldSizeZ = sizeZ;
				MarkCustom();
			}
		}

		DrawInt( "Pile Count", PileCount, 1, MaxPiles, v => PileCount = v );
		EditorGUILayout.BeginHorizontal();
		DrawInt( "Seed", Seed, int.MinValue, int.MaxValue, v => Seed = v );
		if ( GUILayout.Button( "Rnd", GUILayout.Width( 40f ) ) )
		{
			Seed = Random.Range( 1, int.MaxValue );
			MarkCustom();
		}
		EditorGUILayout.EndHorizontal();

		if ( visual != null )
		{
			TreasurePileDefinition def = visual.ResolveDefinitionForEditor();
			float cell = def != null ? def.ResolveCellSize() : 0.125f;
			int resX = def != null
				? def.ResolveResolutionForExtent( WorldSizeX )
				: Mathf.NextPowerOfTwo( Mathf.RoundToInt( WorldSizeX / cell ) );
			int resZ = def != null
				? def.ResolveResolutionForExtent( WorldSizeZ )
				: Mathf.NextPowerOfTwo( Mathf.RoundToInt( WorldSizeZ / cell ) );
			float actualX = cell * ( resX - 1 );
			float actualZ = cell * ( resZ - 1 );
			EditorGUILayout.LabelField(
				"Snapped layout",
				$"{resX}×{resZ}  →  {actualX:0.###}m×{actualZ:0.###}m" );
		}
		else
		{
			EditorGUILayout.HelpBox( "Select a TreasurePileVisual / enter Gold Pile sculpt context.", MessageType.Info );
		}

		using ( new EditorGUI.DisabledScope( visual == null ) )
		{
			EditorGUILayout.BeginHorizontal();
			if ( GUILayout.Button( "Generate", GUILayout.Height( 24f ) ) )
				Generate( visual, randomizeSeed: false );
			if ( GUILayout.Button( "New Seed", GUILayout.Height( 24f ) ) )
				Generate( visual, randomizeSeed: true );
			EditorGUILayout.EndHorizontal();
		}

		AdvancedFoldout = EditorGUILayout.Foldout( AdvancedFoldout, "Advanced Shape Controls", true );
		if ( !AdvancedFoldout )
			return;

		EditorGUI.indentLevel++;

		EditorGUILayout.LabelField( "Piles", EditorStyles.boldLabel );
		DrawRange( "Peak Height (m)", PeakHeightMin, PeakHeightMax, 0.05f, 16f, ( a, b ) =>
		{
			PeakHeightMin = a;
			PeakHeightMax = b;
		} );
		DrawRange( "Radius (m)", RadiusMin, RadiusMax, 0.2f, 64f, ( a, b ) =>
		{
			RadiusMin = a;
			RadiusMax = b;
		} );

		DrawFloat01( "Spread", Spread, v => Spread = v );
		DrawFloat01( "Clustering", Clustering, v => Clustering = v );
		DrawFloat( "Separation", Separation, 0.1f, 1.5f, v => Separation = v );
		DrawFloat( "Profile Falloff", ProfileFalloff, 0.1f, 0.99f, v => ProfileFalloff = v );
		DrawFloat01( "Elongation", Elongation, v => Elongation = v );
		DrawFloat( "Edge Margin", EdgeMargin, 0f, 0.4f, v => EdgeMargin = v );
		DrawBool( "Max Blend (overlap)", UseMaxBlend, v => UseMaxBlend = v );

		EditorGUILayout.Space( 2f );
		EditorGUILayout.LabelField( "Shape Juice", EditorStyles.boldLabel );
		DrawFloat( "Base Mound (m)", BaseMoundHeight, 0f, 8f, v => BaseMoundHeight = v );
		using ( new EditorGUI.DisabledScope( BaseMoundHeight <= 1e-4f ) )
			DrawFloat( "Base Falloff", BaseMoundFalloff, 0.1f, 0.99f, v => BaseMoundFalloff = v );
		DrawFloat01( "Ridge Bridges", RidgeStrength, v => RidgeStrength = v );
		using ( new EditorGUI.DisabledScope( RidgeStrength <= 1e-4f ) )
			DrawFloat( "Ridge Width (m)", RidgeWidth, 0.1f, 8f, v => RidgeWidth = v );
		DrawFloat( "Surface Noise (m)", NoiseAmount, 0f, 2f, v => NoiseAmount = v );
		using ( new EditorGUI.DisabledScope( NoiseAmount <= 1e-4f ) )
		{
			DrawFloat( "Noise Frequency", NoiseFrequency, 0.1f, 16f, v => NoiseFrequency = v );
			DrawInt( "Noise Octaves", NoiseOctaves, 1, 8, v => NoiseOctaves = v );
		}

		EditorGUILayout.HelpBox(
			"Tips: Max Blend looks most like piled coin heaps. "
			+ "Raise Clustering for a central hoard; lower it + Separation for distinct mounds. "
			+ "Ridge Bridges knit nearby piles into banks.",
			MessageType.None );

		EditorGUI.indentLevel--;
	}

	public static void Generate( TreasurePileVisual visual, bool randomizeSeed )
	{
		if ( visual == null || Application.isPlaying )
			return;

		if ( randomizeSeed )
			Seed = Random.Range( 1, int.MaxValue );

		Undo.RecordObject( visual, "Procedural Gold Pile" );

		float wantX = WorldSizeX;
		float wantZ = LockSquare ? WorldSizeX : WorldSizeZ;
		visual.ResizeAuthoredToWorldSize( wantX, wantZ, out float sizeX, out float sizeZ );

		GoldPileHeightfield hf = visual.Heightfield;
		if ( hf == null || !hf.IsInitialized )
		{
			GoldPileSculptToolContext.Painter.RebuildPreview( visual );
			hf = visual.Heightfield;
		}

		if ( hf == null || !hf.IsInitialized )
			return;

		hf.FillZeros();

		if ( BaseMoundHeight > 1e-4f )
			hf.FillMound( BaseMoundHeight, BaseMoundFalloff );

		System.Random rng = new System.Random( Seed );
		PileSeed[] piles = PlacePiles( rng, sizeX, sizeZ );

		for ( int i = 0; i < piles.Length; i++ )
		{
			PileSeed p = piles[ i ];
			hf.PeakEllipseAtLocal(
				p.x,
				p.z,
				p.radiusX,
				p.radiusZ,
				p.rotation,
				p.height,
				ProfileFalloff,
				UseMaxBlend );
		}

		if ( RidgeStrength > 1e-4f && piles.Length >= 2 )
			StampRidges( hf, piles, rng );

		if ( NoiseAmount > 1e-4f )
		{
			float cover = Mathf.Max( sizeX, sizeZ ) * 0.55f;
			hf.NoiseAtLocal( 0f, 0f, cover, NoiseAmount, 1.1f, NoiseFrequency, NoiseOctaves, Seed ^ 0x5f3759df );
		}

		visual.CommitEditorStroke();

		GoldPileSculptToolContext.Painter.RebuildPreview( visual );
		EditorUtility.SetDirty( visual );
		SceneView.RepaintAll();
	}

	static PileSeed[] PlacePiles( System.Random rng, float sizeX, float sizeZ )
	{
		int count = Mathf.Clamp( PileCount, 1, MaxPiles );
		PileSeed[] piles = new PileSeed[ count ];
		float halfX = sizeX * 0.5f;
		float halfZ = sizeZ * 0.5f;
		float usableHalfX = halfX * ( 1f - EdgeMargin );
		float usableHalfZ = halfZ * ( 1f - EdgeMargin );
		float maxSpread = Mathf.Min( usableHalfX, usableHalfZ ) * Spread;
		float heightMin = Mathf.Min( PeakHeightMin, PeakHeightMax );
		float heightMax = Mathf.Max( PeakHeightMin, PeakHeightMax );
		float radiusMin = Mathf.Min( RadiusMin, RadiusMax );
		float radiusMax = Mathf.Max( RadiusMin, RadiusMax );
		float clusterPow = 1f + Clustering * 2.5f;
		GoldPileProceduralPreset layoutHint = Preset;

		for ( int i = 0; i < count; i++ )
		{
			float bestX = 0f;
			float bestZ = 0f;
			float bestRadius = Mathf.Lerp( radiusMin, radiusMax, ( float )rng.NextDouble() );
			bool placed = false;

			for ( int attempt = 0; attempt < 28; attempt++ )
			{
				SamplePlacement(
					rng,
					layoutHint,
					i,
					count,
					maxSpread,
					usableHalfX,
					usableHalfZ,
					out float x,
					out float z );

				float radius = Mathf.Lerp( radiusMin, radiusMax, ( float )rng.NextDouble() );
				float pad = radius * 0.65f;
				x = Mathf.Clamp( x, -usableHalfX + pad, usableHalfX - pad );
				z = Mathf.Clamp( z, -usableHalfZ + pad, usableHalfZ - pad );

				if ( !OverlapsExisting( piles, i, x, z, radius ) )
				{
					bestX = x;
					bestZ = z;
					bestRadius = radius;
					placed = true;
					break;
				}

				bestX = x;
				bestZ = z;
				bestRadius = radius;
			}

			if ( !placed && i > 0 )
			{
				PileSeed anchor = piles[ rng.Next( i ) ];
				float jitter = bestRadius * 0.85f;
				bestX = Mathf.Clamp( anchor.x + Signed( rng ) * jitter, -usableHalfX, usableHalfX );
				bestZ = Mathf.Clamp( anchor.z + Signed( rng ) * jitter, -usableHalfZ, usableHalfZ );
			}

			float aspect = 1f - Elongation * ( float )rng.NextDouble() * 0.55f;
			float radiusX = bestRadius;
			float radiusZ = bestRadius * aspect;
			if ( rng.NextDouble() < 0.5 )
			{
				float tmp = radiusX;
				radiusX = radiusZ;
				radiusZ = tmp;
			}

			piles[ i ] = new PileSeed
			{
				x = bestX,
				z = bestZ,
				radiusX = radiusX,
				radiusZ = radiusZ,
				rotation = ( float )( rng.NextDouble() * Mathf.PI ),
				height = Mathf.Lerp( heightMin, heightMax, ( float )rng.NextDouble() )
			};
		}

		return piles;
	}

	static void SamplePlacement(
		System.Random rng,
		GoldPileProceduralPreset layoutHint,
		int index,
		int count,
		float maxSpread,
		float usableHalfX,
		float usableHalfZ,
		out float x,
		out float z )
	{
		if ( layoutHint == GoldPileProceduralPreset.TwinPeaks && count >= 2 && index < 2 )
		{
			float side = index == 0 ? -1f : 1f;
			x = side * maxSpread * 0.72f;
			z = Signed( rng ) * maxSpread * 0.12f;
			return;
		}

		if ( layoutHint == GoldPileProceduralPreset.CrescentBank && count > 1 )
		{
			float t = count <= 1 ? 0.5f : index / ( float )( count - 1 );
			float angle = Mathf.Lerp( -0.85f, 0.85f, t ) * Mathf.PI;
			float radius = maxSpread * Mathf.Lerp( 0.75f, 0.95f, ( float )rng.NextDouble() );
			x = Mathf.Cos( angle ) * radius;
			z = Mathf.Sin( angle ) * radius * 0.55f;
			x += Signed( rng ) * usableHalfX * 0.04f;
			z += Signed( rng ) * usableHalfZ * 0.04f;
			return;
		}

		float clusterPow = 1f + Clustering * 2.5f;
		float sampleAngle = ( float )( rng.NextDouble() * Mathf.PI * 2.0 );
		float distNorm = Mathf.Pow( ( float )rng.NextDouble(), clusterPow );
		float dist = maxSpread * distNorm;
		x = Mathf.Cos( sampleAngle ) * dist;
		z = Mathf.Sin( sampleAngle ) * dist;
	}

	static bool OverlapsExisting( PileSeed[] piles, int count, float x, float z, float radius )
	{
		float sep = Separation;
		for ( int i = 0; i < count; i++ )
		{
			float dx = piles[ i ].x - x;
			float dz = piles[ i ].z - z;
			float minDist = ( piles[ i ].radiusX + piles[ i ].radiusZ ) * 0.5f + radius;
			minDist *= sep;
			if ( dx * dx + dz * dz < minDist * minDist )
				return true;
		}

		return false;
	}

	static void StampRidges( GoldPileHeightfield hf, PileSeed[] piles, System.Random rng )
	{
		int count = piles.Length;
		float avgHeight = 0f;
		for ( int i = 0; i < count; i++ )
			avgHeight += piles[ i ].height;
		avgHeight /= Mathf.Max( 1, count );

		for ( int i = 0; i < count; i++ )
		{
			int nearest = -1;
			float nearestDistSq = float.MaxValue;
			for ( int j = 0; j < count; j++ )
			{
				if ( j == i )
					continue;
				float dx = piles[ j ].x - piles[ i ].x;
				float dz = piles[ j ].z - piles[ i ].z;
				float dSq = dx * dx + dz * dz;
				if ( dSq < nearestDistSq )
				{
					nearestDistSq = dSq;
					nearest = j;
				}
			}

			if ( nearest < 0 || nearest < i )
				continue;

			float joinChance = RidgeStrength;
			float dist = Mathf.Sqrt( nearestDistSq );
			float reach = ( piles[ i ].radiusX + piles[ nearest ].radiusX ) * 1.35f;
			if ( dist > reach )
				joinChance *= 0.35f;
			if ( rng.NextDouble() > joinChance )
				continue;

			float ridgeH = avgHeight * 0.45f * RidgeStrength;
			hf.RidgeStampSegmentAtLocal(
				piles[ i ].x,
				piles[ i ].z,
				piles[ nearest ].x,
				piles[ nearest ].z,
				RidgeWidth,
				ridgeH,
				ProfileFalloff,
				0.65f );
		}
	}

	static float Signed( System.Random rng )
	{
		return ( float )( rng.NextDouble() * 2.0 - 1.0 );
	}

	static void MarkCustom()
	{
		if ( Preset != GoldPileProceduralPreset.Custom )
			Preset = GoldPileProceduralPreset.Custom;
	}

	static void DrawBool( string label, bool value, System.Action<bool> set )
	{
		bool next = EditorGUILayout.Toggle( label, value );
		if ( next != value )
		{
			set( next );
			MarkCustom();
		}
	}

	static void DrawFloat( string label, float value, float min, float max, System.Action<float> set )
	{
		float next = EditorGUILayout.Slider( label, value, min, max );
		if ( !Mathf.Approximately( next, value ) )
		{
			set( next );
			MarkCustom();
		}
	}

	static void DrawFloat01( string label, float value, System.Action<float> set )
	{
		DrawFloat( label, value, 0f, 1f, set );
	}

	static void DrawInt( string label, int value, int min, int max, System.Action<int> set )
	{
		int next = min == int.MinValue && max == int.MaxValue
			? EditorGUILayout.IntField( label, value )
			: EditorGUILayout.IntSlider( label, value, min, max );
		if ( next != value )
		{
			set( next );
			MarkCustom();
		}
	}

	static void DrawRange(
		string label,
		float minValue,
		float maxValue,
		float absMin,
		float absMax,
		System.Action<float, float> set )
	{
		EditorGUILayout.MinMaxSlider( label, ref minValue, ref maxValue, absMin, absMax );
		EditorGUILayout.BeginHorizontal();
		float a = EditorGUILayout.FloatField( minValue );
		float b = EditorGUILayout.FloatField( maxValue );
		EditorGUILayout.EndHorizontal();
		a = Mathf.Clamp( a, absMin, absMax );
		b = Mathf.Clamp( b, absMin, absMax );
		if ( a > b )
		{
			float t = a;
			a = b;
			b = t;
		}

		if ( !Mathf.Approximately( a, minValue ) || !Mathf.Approximately( b, maxValue ) )
		{
			set( a, b );
			MarkCustom();
		}
	}

	public static void ApplyPreset( GoldPileProceduralPreset preset )
	{
		switch ( preset )
		{
			case GoldPileProceduralPreset.SingleMound:
				LockSquare = true;
				WorldSizeX = WorldSizeZ = 6f;
				PileCount = 1;
				PeakHeightMin = 1.1f;
				PeakHeightMax = 1.35f;
				RadiusMin = 2.2f;
				RadiusMax = 2.6f;
				Spread = 0f;
				Clustering = 1f;
				Separation = 1f;
				ProfileFalloff = 0.82f;
				Elongation = 0.1f;
				EdgeMargin = 0.08f;
				UseMaxBlend = true;
				BaseMoundHeight = 0.15f;
				BaseMoundFalloff = 0.9f;
				RidgeStrength = 0f;
				NoiseAmount = 0.05f;
				NoiseFrequency = 2f;
				NoiseOctaves = 2;
				break;

			case GoldPileProceduralPreset.TwinPeaks:
				LockSquare = true;
				WorldSizeX = WorldSizeZ = 8f;
				PileCount = 2;
				PeakHeightMin = 1.0f;
				PeakHeightMax = 1.5f;
				RadiusMin = 1.6f;
				RadiusMax = 2.2f;
				Spread = 0.55f;
				Clustering = 0.15f;
				Separation = 0.85f;
				ProfileFalloff = 0.76f;
				Elongation = 0.25f;
				EdgeMargin = 0.12f;
				UseMaxBlend = true;
				BaseMoundHeight = 0.2f;
				BaseMoundFalloff = 0.93f;
				RidgeStrength = 0.45f;
				RidgeWidth = 0.9f;
				NoiseAmount = 0.07f;
				NoiseFrequency = 2.2f;
				NoiseOctaves = 3;
				break;

			case GoldPileProceduralPreset.ScatteredHeaps:
				LockSquare = false;
				WorldSizeX = 12f;
				WorldSizeZ = 9f;
				PileCount = 7;
				PeakHeightMin = 0.55f;
				PeakHeightMax = 1.25f;
				RadiusMin = 0.9f;
				RadiusMax = 1.8f;
				Spread = 0.88f;
				Clustering = 0.1f;
				Separation = 0.75f;
				ProfileFalloff = 0.72f;
				Elongation = 0.4f;
				EdgeMargin = 0.1f;
				UseMaxBlend = true;
				BaseMoundHeight = 0.08f;
				BaseMoundFalloff = 0.95f;
				RidgeStrength = 0.15f;
				RidgeWidth = 0.7f;
				NoiseAmount = 0.06f;
				NoiseFrequency = 2.8f;
				NoiseOctaves = 3;
				break;

			case GoldPileProceduralPreset.DragonHoard:
				LockSquare = true;
				WorldSizeX = WorldSizeZ = 10f;
				PileCount = 8;
				PeakHeightMin = 0.85f;
				PeakHeightMax = 1.9f;
				RadiusMin = 1.4f;
				RadiusMax = 2.8f;
				Spread = 0.65f;
				Clustering = 0.55f;
				Separation = 0.45f;
				ProfileFalloff = 0.78f;
				Elongation = 0.3f;
				EdgeMargin = 0.1f;
				UseMaxBlend = true;
				BaseMoundHeight = 0.35f;
				BaseMoundFalloff = 0.92f;
				RidgeStrength = 0.4f;
				RidgeWidth = 1.0f;
				NoiseAmount = 0.1f;
				NoiseFrequency = 2.4f;
				NoiseOctaves = 3;
				break;

			case GoldPileProceduralPreset.CrescentBank:
				LockSquare = false;
				WorldSizeX = 11f;
				WorldSizeZ = 7f;
				PileCount = 6;
				PeakHeightMin = 0.7f;
				PeakHeightMax = 1.4f;
				RadiusMin = 1.2f;
				RadiusMax = 2.0f;
				Spread = 0.8f;
				Clustering = 0.25f;
				Separation = 0.5f;
				ProfileFalloff = 0.74f;
				Elongation = 0.55f;
				EdgeMargin = 0.12f;
				UseMaxBlend = true;
				BaseMoundHeight = 0.18f;
				BaseMoundFalloff = 0.9f;
				RidgeStrength = 0.7f;
				RidgeWidth = 1.1f;
				NoiseAmount = 0.07f;
				NoiseFrequency = 2.1f;
				NoiseOctaves = 3;
				break;

			case GoldPileProceduralPreset.MessySpill:
				LockSquare = true;
				WorldSizeX = WorldSizeZ = 9f;
				PileCount = 10;
				PeakHeightMin = 0.35f;
				PeakHeightMax = 1.7f;
				RadiusMin = 0.7f;
				RadiusMax = 2.2f;
				Spread = 0.85f;
				Clustering = 0.2f;
				Separation = 0.35f;
				ProfileFalloff = 0.68f;
				Elongation = 0.5f;
				EdgeMargin = 0.08f;
				UseMaxBlend = true;
				BaseMoundHeight = 0.12f;
				BaseMoundFalloff = 0.94f;
				RidgeStrength = 0.25f;
				RidgeWidth = 0.75f;
				NoiseAmount = 0.16f;
				NoiseFrequency = 3.2f;
				NoiseOctaves = 4;
				break;
		}
	}
}
#endif
