#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CompleteGlowMenu
{
	public const string MeshFolder = "Assets/Meshes/Generated/CompleteGlow";
	public const string PresetFolder = "Assets/Definitions/Graphics/CompleteGlow";
	public const string DefaultPresetPath = PresetFolder + "/CompleteGlow_Default.asset";
	public const string MagicalPresetPath = PresetFolder + "/CompleteGlow_Magical.asset";
	public const string SoftSpotlightPresetPath = PresetFolder + "/CompleteGlow_SoftSpotlight.asset";
	public const string VfxFolder = "Assets/Prefabs/VFX/CompleteGlow";
	public const string BurstPrefabPath = VfxFolder + "/CompleteGlowBurst.prefab";
	public const string MotesPrefabPath = VfxFolder + "/CompleteGlowMotes.prefab";
	public const string DustMoteMaterialPath = "Assets/Materials/Shaders/DustMote/M_DustMote.mat";

	static bool _queuedRevealParticles;
	const string RevealParticlesConfiguredPrefsKey = "DragonLoot.CompleteGlow.RevealParticlesConfigured.v1";

	[InitializeOnLoadMethod]
	static void QueueEnsureRevealParticlePrefabs()
	{
		if ( _queuedRevealParticles )
			return;
		_queuedRevealParticles = true;
		EditorApplication.delayCall += EnsureRevealParticlePrefabsOnLoad;
	}

	static void EnsureRevealParticlePrefabsOnLoad()
	{
		bool missingBurst = AssetDatabase.LoadAssetAtPath<GameObject>( BurstPrefabPath ) == null;
		bool missingMotes = AssetDatabase.LoadAssetAtPath<GameObject>( MotesPrefabPath ) == null;
		bool needsConfigure = !EditorPrefs.GetBool( RevealParticlesConfiguredPrefsKey, false );
		if ( !missingBurst && !missingMotes && !needsConfigure )
			return;

		EnsureRevealParticlePrefabs();
		EditorPrefs.SetBool( RevealParticlesConfiguredPrefsKey, true );
	}

	[MenuItem( DragonLootMenus.GameObjectBuildCompleteGlow, false, 20 )]
	static void BuildFromSelection()
	{
		EnsurePresets();
		CompleteGlowPreset defaultPreset = AssetDatabase.LoadAssetAtPath<CompleteGlowPreset>( DefaultPresetPath );
		Material material = AssetDatabase.LoadAssetAtPath<Material>( CompleteGlowVolume.DefaultMaterialPath );
		if ( material == null )
		{
			Debug.LogError( "CompleteGlowMenu: missing material at " + CompleteGlowVolume.DefaultMaterialPath );
			return;
		}

		GameObject[] selection = Selection.gameObjects;
		List<GameObject> created = new List<GameObject>();
		for ( int i = 0; i < selection.Length; i++ )
		{
			GameObject go = selection[ i ];
			if ( go == null )
				continue;

			MeshFilter sourceFilter = go.GetComponent<MeshFilter>();
			if ( sourceFilter == null || sourceFilter.sharedMesh == null )
				continue;

			CompleteGlowVolume volume = BuildOrReplaceOn( go, sourceFilter, defaultPreset, material );
			if ( volume != null )
				created.Add( volume.gameObject );
		}

		if ( created.Count == 0 )
		{
			Debug.LogWarning( "CompleteGlowMenu: select one or more GameObjects with a MeshFilter + mesh." );
			return;
		}

		Selection.objects = created.ToArray();
		Debug.Log( "CompleteGlowMenu: built " + created.Count + " complete glow(s)." );
	}

	[MenuItem( DragonLootMenus.GameObjectBuildCompleteGlow, true )]
	static bool ValidateBuildFromSelection()
	{
		GameObject[] selection = Selection.gameObjects;
		if ( selection == null || selection.Length == 0 )
			return false;

		for ( int i = 0; i < selection.Length; i++ )
		{
			GameObject go = selection[ i ];
			if ( go == null )
				continue;
			MeshFilter filter = go.GetComponent<MeshFilter>();
			if ( filter != null && filter.sharedMesh != null )
				return true;
		}

		return false;
	}

	[MenuItem( DragonLootMenus.GraphicsCompleteGlowEnsurePresets, priority = 240 )]
	static void EnsurePresetsMenu()
	{
		EnsurePresets();
		Selection.activeObject = AssetDatabase.LoadAssetAtPath<CompleteGlowPreset>( DefaultPresetPath );
		Debug.Log( "CompleteGlowMenu: presets ensured under " + PresetFolder );
	}

	[MenuItem( DragonLootMenus.GraphicsCompleteGlowEnsureRevealParticles, priority = 242 )]
	static void EnsureRevealParticlePrefabsMenu()
	{
		EnsureRevealParticlePrefabs();
	}

	/// <summary>Unity batchmode entry: -executeMethod CompleteGlowMenu.EnsureRevealParticlePrefabsBatch</summary>
	public static void EnsureRevealParticlePrefabsBatch()
	{
		EnsureRevealParticlePrefabs();
	}

	public static void EnsureRevealParticlePrefabs()
	{
		GameObject burst = EnsureBurstPrefab();
		GameObject motes = EnsureMotesPrefab();
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();

		if ( burst != null )
			Selection.activeObject = burst;
		else if ( motes != null )
			Selection.activeObject = motes;

		Debug.Log( "CompleteGlowMenu: reveal particle prefabs ensured under " + VfxFolder );
	}

	[MenuItem( DragonLootMenus.GraphicsCompleteGlowRebuildAll, priority = 241 )]
	static void RebuildAllInOpenScenes()
	{
		EnsurePresets();
		Material material = AssetDatabase.LoadAssetAtPath<Material>( CompleteGlowVolume.DefaultMaterialPath );
		int count = 0;
		for ( int s = 0; s < SceneManager.sceneCount; s++ )
		{
			Scene scene = SceneManager.GetSceneAt( s );
			if ( !scene.isLoaded )
				continue;

			GameObject[] roots = scene.GetRootGameObjects();
			for ( int r = 0; r < roots.Length; r++ )
			{
				CompleteGlowVolume[] volumes = roots[ r ].GetComponentsInChildren<CompleteGlowVolume>( true );
				for ( int v = 0; v < volumes.Length; v++ )
				{
					if ( RebuildVolume( volumes[ v ], material ) )
						count++;
				}
			}
		}

		Debug.Log( "CompleteGlowMenu: rebuilt " + count + " complete glow(s)." );
	}

	public static CompleteGlowVolume BuildOrReplaceOn( GameObject source, MeshFilter sourceFilter, CompleteGlowPreset preset, Material material )
	{
		if ( source == null || sourceFilter == null )
			return null;

		Transform existing = source.transform.Find( CompleteGlowVolume.ChildName );
		GameObject glowGo;
		if ( existing != null )
		{
			glowGo = existing.gameObject;
			Undo.RegisterFullObjectHierarchyUndo( glowGo, "Build Complete Glow" );
		}
		else
		{
			glowGo = new GameObject( CompleteGlowVolume.ChildName );
			Undo.RegisterCreatedObjectUndo( glowGo, "Build Complete Glow" );
			Undo.SetTransformParent( glowGo.transform, source.transform, "Build Complete Glow" );
		}

		CompleteGlowVolume volume = glowGo.GetComponent<CompleteGlowVolume>();
		if ( volume == null )
			volume = Undo.AddComponent<CompleteGlowVolume>( glowGo );

		Undo.RecordObject( volume, "Build Complete Glow" );
		volume.SourceMeshFilter = sourceFilter;
		if ( preset != null )
		{
			volume.Preset = preset;
			volume.ApplyPresetSettings();
		}

		if ( string.IsNullOrEmpty( volume.MeshAssetId ) || IsMeshIdShared( volume ) )
			volume.MeshAssetId = Guid.NewGuid().ToString( "N" );

		volume.EnsureComponents();
		RebuildVolume( volume, material );
		EditorUtility.SetDirty( volume );
		EditorSceneManager.MarkSceneDirty( glowGo.scene );
		return volume;
	}

	public static bool RebuildVolume( CompleteGlowVolume volume, Material material = null )
	{
		if ( volume == null || volume.Settings == null )
			return false;

		if ( material == null )
			material = AssetDatabase.LoadAssetAtPath<Material>( CompleteGlowVolume.DefaultMaterialPath );

		Undo.RecordObject( volume, "Rebuild Complete Glow" );
		Undo.RecordObject( volume.transform, "Rebuild Complete Glow" );

		volume.EnsureComponents();
		if ( volume.MeshFilter == null || volume.MeshRenderer == null )
			return false;

		if ( string.IsNullOrEmpty( volume.MeshAssetId ) || IsMeshIdShared( volume ) )
			volume.MeshAssetId = Guid.NewGuid().ToString( "N" );

		Vector2 footprint = volume.ResolveFootprintWorld();
		Mesh meshAsset = GetOrCreateMeshAsset( volume.MeshAssetId );
		CompleteGlowMeshBuilder.Build( volume.Settings, footprint, meshAsset );
		EditorUtility.SetDirty( meshAsset );

		Undo.RecordObject( volume.MeshFilter, "Rebuild Complete Glow" );
		Undo.RecordObject( volume.MeshRenderer, "Rebuild Complete Glow" );
		volume.MeshFilter.sharedMesh = meshAsset;

		Material mat = volume.Settings.MaterialOverride != null ? volume.Settings.MaterialOverride : material;
		volume.MeshRenderer.sharedMaterial = mat;
		volume.transform.localPosition = volume.ResolveLocalAnchorPosition();
		volume.transform.localRotation = Quaternion.identity;
		volume.transform.localScale = volume.ResolveCompensatedLocalScale();

		SyncPointLight( volume );
		SyncDustMotes( volume );
		volume.ApplyAppearance();

		EditorUtility.SetDirty( volume );
		EditorUtility.SetDirty( volume.gameObject );
		if ( volume.gameObject.scene.IsValid() )
			EditorSceneManager.MarkSceneDirty( volume.gameObject.scene );
		return true;
	}

	static void SyncPointLight( CompleteGlowVolume volume )
	{
		CompleteGlowSettings settings = volume.Settings;
		if ( settings == null )
			return;

		Light light = volume.PointLight;
		if ( settings.AddPointLight )
		{
			if ( light == null )
			{
				GameObject lightGo = new GameObject( "PointLight" );
				Undo.RegisterCreatedObjectUndo( lightGo, "Rebuild Complete Glow" );
				Undo.SetTransformParent( lightGo.transform, volume.transform, "Rebuild Complete Glow" );
				light = Undo.AddComponent<Light>( lightGo );
				light.type = LightType.Point;
				volume.PointLight = light;
			}

			Undo.RecordObject( light, "Rebuild Complete Glow" );
			Undo.RecordObject( light.transform, "Rebuild Complete Glow" );
			light.enabled = true;
			light.type = LightType.Point;
			light.intensity = settings.LightIntensity;
			light.range = settings.LightRange;
			if ( settings.LightColorFromGlow )
				light.color = settings.Color;
			light.transform.localPosition = new Vector3( 0f, settings.Height * settings.LightHeightT, 0f );
			light.transform.localRotation = Quaternion.identity;
			light.transform.localScale = Vector3.one;
		}
		else if ( light != null )
		{
			Undo.DestroyObjectImmediate( light.gameObject );
			volume.PointLight = null;
		}
	}

	static void SyncDustMotes( CompleteGlowVolume volume )
	{
		CompleteGlowSettings settings = volume.Settings;
		if ( settings == null )
			return;

		GameObject existing = volume.DustMotesInstance;
		if ( settings.DustMotesPrefab == null )
		{
			if ( existing != null )
			{
				Undo.DestroyObjectImmediate( existing );
				volume.DustMotesInstance = null;
			}
			return;
		}

		bool needsNew = existing == null;
		if ( !needsNew )
		{
			GameObject source = PrefabUtility.GetCorrespondingObjectFromSource( existing );
			if ( source != settings.DustMotesPrefab )
				needsNew = true;
		}

		if ( needsNew )
		{
			if ( existing != null )
				Undo.DestroyObjectImmediate( existing );

			GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab( settings.DustMotesPrefab, volume.transform );
			if ( instance == null )
				instance = (GameObject)UnityEngine.Object.Instantiate( settings.DustMotesPrefab, volume.transform );
			Undo.RegisterCreatedObjectUndo( instance, "Rebuild Complete Glow" );
			instance.name = "DustMotes";
			volume.DustMotesInstance = instance;
			existing = instance;
		}

		if ( existing != null )
		{
			Undo.RecordObject( existing.transform, "Rebuild Complete Glow" );
			existing.transform.localPosition = new Vector3( 0f, settings.Height * 0.5f, 0f );
			existing.transform.localRotation = Quaternion.identity;
			Vector2 footprint = volume.ResolveFootprintWorld();
			float scale = Mathf.Max( footprint.x, footprint.y, settings.Height ) * 0.5f;
			existing.transform.localScale = Vector3.one * Mathf.Max( 0.01f, scale );
		}
	}

	static Mesh GetOrCreateMeshAsset( string id )
	{
		EnsureFolder( "Assets/Meshes" );
		EnsureFolder( "Assets/Meshes/Generated" );
		EnsureFolder( MeshFolder );

		string path = MeshFolder + "/CompleteGlow_" + id + ".asset";
		Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>( path );
		if ( existing != null )
			return existing;

		Mesh mesh = new Mesh();
		mesh.name = "CompleteGlow_" + id;
		AssetDatabase.CreateAsset( mesh, path );
		AssetDatabase.SaveAssets();
		return mesh;
	}

	static bool IsMeshIdShared( CompleteGlowVolume volume )
	{
		if ( volume == null || string.IsNullOrEmpty( volume.MeshAssetId ) )
			return false;

		CompleteGlowVolume[] all = UnityEngine.Object.FindObjectsByType<CompleteGlowVolume>( FindObjectsInactive.Include, FindObjectsSortMode.None );
		for ( int i = 0; i < all.Length; i++ )
		{
			CompleteGlowVolume other = all[ i ];
			if ( other == null || other == volume )
				continue;
			if ( other.MeshAssetId == volume.MeshAssetId )
				return true;
		}

		return false;
	}

	public static void EnsurePresets()
	{
		EnsureFolder( "Assets/Definitions" );
		EnsureFolder( "Assets/Definitions/Graphics" );
		EnsureFolder( PresetFolder );

		if ( AssetDatabase.LoadAssetAtPath<CompleteGlowPreset>( DefaultPresetPath ) == null )
		{
			CompleteGlowPreset preset = ScriptableObject.CreateInstance<CompleteGlowPreset>();
			preset.Settings.CopyFrom( CreateDefaultSettings() );
			AssetDatabase.CreateAsset( preset, DefaultPresetPath );
		}

		if ( AssetDatabase.LoadAssetAtPath<CompleteGlowPreset>( MagicalPresetPath ) == null )
		{
			CompleteGlowPreset preset = ScriptableObject.CreateInstance<CompleteGlowPreset>();
			preset.Settings.CopyFrom( CreateMagicalSettings() );
			AssetDatabase.CreateAsset( preset, MagicalPresetPath );
		}

		if ( AssetDatabase.LoadAssetAtPath<CompleteGlowPreset>( SoftSpotlightPresetPath ) == null )
		{
			CompleteGlowPreset preset = ScriptableObject.CreateInstance<CompleteGlowPreset>();
			preset.Settings.CopyFrom( CreateSoftSpotlightSettings() );
			AssetDatabase.CreateAsset( preset, SoftSpotlightPresetPath );
		}

		AssetDatabase.SaveAssets();
	}

	static CompleteGlowSettings CreateDefaultSettings()
	{
		CompleteGlowSettings s = CompleteGlowSettings.CreateDefault();
		s.Height = 1.5f;
		s.FlareAngleDegrees = 15f;
		s.LayerCount = 2;
		s.LayerOutwardStep = 0.03f;
		s.InnerCore = true;
		s.RayCardCount = 2;
		s.RayCardAlpha = 0.3f;
		s.Color = new Color( 1f, 0.72f, 0.28f, 1f );
		s.Exposure = 1.5f;
		s.NoiseStrength = 0.3f;
		return s;
	}

	static CompleteGlowSettings CreateMagicalSettings()
	{
		CompleteGlowSettings s = CompleteGlowSettings.CreateDefault();
		s.Height = 2f;
		s.FlareAngleDegrees = 18f;
		s.LayerCount = 3;
		s.LayerOutwardStep = 0.05f;
		s.LayerAlphaFalloff = 0.5f;
		s.InnerCore = true;
		s.CoreIntensity = 2f;
		s.RayCardCount = 4;
		s.RayCardAlpha = 0.4f;
		s.Color = new Color( 0.35f, 0.85f, 1f, 1f );
		s.Exposure = 2f;
		s.NoiseStrength = 0.55f;
		s.NoiseScrollSpeed = 0.25f;
		s.PulseAmount = 0.2f;
		s.PulseSpeed = 0.5f;
		s.AddPointLight = true;
		s.LightIntensity = 1.5f;
		s.LightRange = 4f;
		return s;
	}

	static CompleteGlowSettings CreateSoftSpotlightSettings()
	{
		CompleteGlowSettings s = CompleteGlowSettings.CreateDefault();
		s.Height = 1.25f;
		s.FlareAngleDegrees = 8f;
		s.LayerCount = 1;
		s.InnerCore = false;
		s.RayCardCount = 0;
		s.EdgeSoftness = 0.2f;
		s.Color = new Color( 1f, 0.95f, 0.85f, 1f );
		s.Exposure = 1.1f;
		s.NoiseStrength = 0.15f;
		s.CrossSection = CompleteGlowCrossSection.RoundedSquare;
		s.CornerRadius = 0.15f;
		return s;
	}

	static GameObject EnsureBurstPrefab()
	{
		EnsureFolder( "Assets/Prefabs" );
		EnsureFolder( "Assets/Prefabs/VFX" );
		EnsureFolder( VfxFolder );

		GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>( BurstPrefabPath );
		if ( existing != null )
		{
			string contentsPath = BurstPrefabPath;
			GameObject root = PrefabUtility.LoadPrefabContents( contentsPath );
			ParticleSystem ps = root.GetComponent<ParticleSystem>();
			if ( ps == null )
				ps = root.AddComponent<ParticleSystem>();
			ConfigureBurstParticles( ps );
			ApplyDustMoteMaterial( ps );
			PrefabUtility.SaveAsPrefabAsset( root, BurstPrefabPath );
			PrefabUtility.UnloadPrefabContents( root );
			return AssetDatabase.LoadAssetAtPath<GameObject>( BurstPrefabPath );
		}

		GameObject go = new GameObject( "CompleteGlowBurst" );
		ParticleSystem newPs = go.AddComponent<ParticleSystem>();
		ConfigureBurstParticles( newPs );
		ApplyDustMoteMaterial( newPs );

		GameObject prefab = PrefabUtility.SaveAsPrefabAsset( go, BurstPrefabPath );
		UnityEngine.Object.DestroyImmediate( go );
		return prefab;
	}

	static GameObject EnsureMotesPrefab()
	{
		EnsureFolder( "Assets/Prefabs" );
		EnsureFolder( "Assets/Prefabs/VFX" );
		EnsureFolder( VfxFolder );

		GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>( MotesPrefabPath );
		if ( existing != null )
		{
			GameObject root = PrefabUtility.LoadPrefabContents( MotesPrefabPath );
			ParticleSystem ps = root.GetComponent<ParticleSystem>();
			if ( ps == null )
				ps = root.AddComponent<ParticleSystem>();
			ConfigureMoteParticles( ps );
			ApplyDustMoteMaterial( ps );
			PrefabUtility.SaveAsPrefabAsset( root, MotesPrefabPath );
			PrefabUtility.UnloadPrefabContents( root );
			return AssetDatabase.LoadAssetAtPath<GameObject>( MotesPrefabPath );
		}

		GameObject go = new GameObject( "CompleteGlowMotes" );
		ParticleSystem newPs = go.AddComponent<ParticleSystem>();
		ConfigureMoteParticles( newPs );
		ApplyDustMoteMaterial( newPs );

		GameObject prefab = PrefabUtility.SaveAsPrefabAsset( go, MotesPrefabPath );
		UnityEngine.Object.DestroyImmediate( go );
		return prefab;
	}

	static void ConfigureBurstParticles( ParticleSystem ps )
	{
		ps.Stop( true, ParticleSystemStopBehavior.StopEmittingAndClear );

		ParticleSystem.MainModule main = ps.main;
		main.loop = false;
		main.playOnAwake = false;
		main.duration = 0.35f;
		main.startLifetime = new ParticleSystem.MinMaxCurve( 0.35f, 0.7f );
		main.startSpeed = new ParticleSystem.MinMaxCurve( 0.6f, 1.8f );
		main.startSize = new ParticleSystem.MinMaxCurve( 0.04f, 0.12f );
		main.startColor = new Color( 1.4f, 1.05f, 0.45f, 0.9f );
		main.maxParticles = 64;
		main.simulationSpace = ParticleSystemSimulationSpace.World;
		main.gravityModifier = 0.15f;

		ParticleSystem.EmissionModule emission = ps.emission;
		emission.rateOverTime = 0f;
		emission.SetBursts( new[] { new ParticleSystem.Burst( 0f, 28, 40 ) } );

		ParticleSystem.ShapeModule shape = ps.shape;
		shape.enabled = true;
		shape.shapeType = ParticleSystemShapeType.Hemisphere;
		shape.radius = 0.35f;

		ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
		color.enabled = true;
		Gradient gradient = new Gradient();
		gradient.SetKeys(
			new[]
			{
				new GradientColorKey( new Color( 1.6f, 1.2f, 0.5f ), 0f ),
				new GradientColorKey( new Color( 1f, 0.65f, 0.2f ), 1f )
			},
			new[]
			{
				new GradientAlphaKey( 0f, 0f ),
				new GradientAlphaKey( 1f, 0.1f ),
				new GradientAlphaKey( 0f, 1f )
			} );
		color.color = gradient;

		ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
		size.enabled = true;
		size.size = new ParticleSystem.MinMaxCurve( 1f, AnimationCurve.EaseInOut( 0f, 1f, 1f, 0f ) );

		ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
		velocity.enabled = true;
		velocity.space = ParticleSystemSimulationSpace.Local;
		// XYZ must share the same MinMaxCurve mode.
		velocity.x = new ParticleSystem.MinMaxCurve( 0f, 0f );
		velocity.y = new ParticleSystem.MinMaxCurve( 0.4f, 1.2f );
		velocity.z = new ParticleSystem.MinMaxCurve( 0f, 0f );
	}

	static void ConfigureMoteParticles( ParticleSystem ps )
	{
		ps.Stop( true, ParticleSystemStopBehavior.StopEmittingAndClear );

		ParticleSystem.MainModule main = ps.main;
		main.loop = true;
		main.playOnAwake = false;
		main.duration = 4f;
		main.startLifetime = new ParticleSystem.MinMaxCurve( 2.5f, 4.5f );
		main.startSpeed = new ParticleSystem.MinMaxCurve( 0.02f, 0.08f );
		main.startSize = new ParticleSystem.MinMaxCurve( 0.03f, 0.08f );
		main.startColor = new Color( 1.2f, 0.95f, 0.55f, 0.55f );
		main.maxParticles = 48;
		main.simulationSpace = ParticleSystemSimulationSpace.Local;
		main.gravityModifier = 0f;

		ParticleSystem.EmissionModule emission = ps.emission;
		emission.rateOverTime = 6f;
		emission.SetBursts( System.Array.Empty<ParticleSystem.Burst>() );

		ParticleSystem.ShapeModule shape = ps.shape;
		shape.enabled = true;
		shape.shapeType = ParticleSystemShapeType.Box;
		shape.scale = new Vector3( 1.2f, 1.4f, 1.2f );

		ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
		color.enabled = true;
		Gradient gradient = new Gradient();
		gradient.SetKeys(
			new[]
			{
				new GradientColorKey( new Color( 1.3f, 1f, 0.6f ), 0f ),
				new GradientColorKey( new Color( 1f, 0.75f, 0.35f ), 1f )
			},
			new[]
			{
				new GradientAlphaKey( 0f, 0f ),
				new GradientAlphaKey( 0.65f, 0.25f ),
				new GradientAlphaKey( 0f, 1f )
			} );
		color.color = gradient;

		ParticleSystem.NoiseModule noise = ps.noise;
		noise.enabled = true;
		noise.strength = 0.12f;
		noise.frequency = 0.35f;
		noise.scrollSpeed = 0.08f;
		noise.octaveCount = 2;

		ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
		velocity.enabled = true;
		velocity.space = ParticleSystemSimulationSpace.Local;
		// XYZ must share the same MinMaxCurve mode.
		velocity.x = new ParticleSystem.MinMaxCurve( 0f, 0f );
		velocity.y = new ParticleSystem.MinMaxCurve( 0.04f, 0.12f );
		velocity.z = new ParticleSystem.MinMaxCurve( 0f, 0f );
	}

	static void ApplyDustMoteMaterial( ParticleSystem ps )
	{
		ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
		if ( renderer == null )
			return;

		renderer.renderMode = ParticleSystemRenderMode.Billboard;
		Material mat = AssetDatabase.LoadAssetAtPath<Material>( DustMoteMaterialPath );
		if ( mat == null )
			mat = AssetDatabase.GetBuiltinExtraResource<Material>( "Default-Particle.mat" );
		if ( mat != null )
			renderer.sharedMaterial = mat;
	}

	static void EnsureFolder( string path )
	{
		if ( AssetDatabase.IsValidFolder( path ) )
			return;

		string parent = Path.GetDirectoryName( path ).Replace( '\\', '/' );
		string name = Path.GetFileName( path );
		if ( !AssetDatabase.IsValidFolder( parent ) )
			EnsureFolder( parent );
		AssetDatabase.CreateFolder( parent, name );
	}
}
#endif
