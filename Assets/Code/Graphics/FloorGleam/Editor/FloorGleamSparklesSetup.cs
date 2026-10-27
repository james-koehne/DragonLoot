#if UNITY_EDITOR
using System.IO;

using UnityEditor;

using UnityEngine;

/// <summary>
/// Ensures the FloorGleam sparkle particle prefab and wires instances onto gleam roots.
/// </summary>
public static class FloorGleamSparklesSetup
{
	public const string VfxFolder = "Assets/Prefabs/VFX/FloorGleam";
	public const string PrefabPath = VfxFolder + "/FloorGleamSparkles.prefab";
	public const string DustMoteMaterialPath = "Assets/Materials/Shaders/DustMote/M_DustMote.mat";
	public const string ChildName = "FloorGleamSparkles";

	[MenuItem( DragonLootMenus.GraphicsFloorGleamEnsureSparkles, priority = 250 )]
	static void MenuEnsurePrefab()
	{
		GameObject prefab = EnsurePrefab();
		if ( prefab != null )
			Selection.activeObject = prefab;
		Debug.Log( "FloorGleamSparklesSetup: sparkle prefab ensured at " + PrefabPath );
	}

	public static GameObject EnsurePrefab()
	{
		EnsureFolder( "Assets/Prefabs" );
		EnsureFolder( "Assets/Prefabs/VFX" );
		EnsureFolder( VfxFolder );

		GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>( PrefabPath );
		if ( existing != null )
		{
			GameObject root = PrefabUtility.LoadPrefabContents( PrefabPath );
			ParticleSystem ps = root.GetComponent<ParticleSystem>();
			if ( ps == null )
				ps = root.AddComponent<ParticleSystem>();
			ConfigureSparkleParticles( ps );
			ApplyDustMoteMaterial( ps );
			PrefabUtility.SaveAsPrefabAsset( root, PrefabPath );
			PrefabUtility.UnloadPrefabContents( root );
			return AssetDatabase.LoadAssetAtPath<GameObject>( PrefabPath );
		}

		GameObject go = new GameObject( ChildName );
		ParticleSystem newPs = go.AddComponent<ParticleSystem>();
		ConfigureSparkleParticles( newPs );
		ApplyDustMoteMaterial( newPs );

		GameObject prefab = PrefabUtility.SaveAsPrefabAsset( go, PrefabPath );
		Object.DestroyImmediate( go );
		return prefab;
	}

	/// <summary>
	/// Instantiates (or replaces) sparkles under a gleam root and returns the ParticleSystem.
	/// </summary>
	public static ParticleSystem AttachTo( GameObject gleamRoot )
	{
		if ( gleamRoot == null )
			return null;

		GameObject prefab = EnsurePrefab();
		if ( prefab == null )
		{
			Debug.LogWarning( "FloorGleamSparklesSetup: missing prefab at " + PrefabPath );
			return null;
		}

		Transform existing = gleamRoot.transform.Find( ChildName );
		if ( existing != null )
			Undo.DestroyObjectImmediate( existing.gameObject );

		GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab( prefab, gleamRoot.transform );
		if ( instance == null )
			instance = (GameObject)Object.Instantiate( prefab, gleamRoot.transform );

		Undo.RegisterCreatedObjectUndo( instance, ChildName );
		instance.name = ChildName;
		instance.transform.localPosition = Vector3.zero;
		instance.transform.localRotation = Quaternion.identity;
		instance.transform.localScale = Vector3.one;

		ParticleSystem ps = instance.GetComponent<ParticleSystem>();
		if ( ps != null )
			ps.Stop( true, ParticleSystemStopBehavior.StopEmittingAndClear );

		EditorUtility.SetDirty( gleamRoot );
		return ps;
	}

	static void ConfigureSparkleParticles( ParticleSystem ps )
	{
		ps.Stop( true, ParticleSystemStopBehavior.StopEmittingAndClear );

		ParticleSystem.MainModule main = ps.main;
		main.loop = true;
		main.playOnAwake = false;
		main.duration = 2f;
		main.startLifetime = new ParticleSystem.MinMaxCurve( 0.7f, 1.4f );
		main.startSpeed = new ParticleSystem.MinMaxCurve( 0.05f, 0.2f );
		main.startSize = new ParticleSystem.MinMaxCurve( 0.04f, 0.12f );
		main.startColor = new Color( 1.6f, 1.2f, 0.55f, 0.85f );
		main.maxParticles = 120;
		main.simulationSpace = ParticleSystemSimulationSpace.World;
		main.gravityModifier = -0.05f;

		ParticleSystem.EmissionModule emission = ps.emission;
		emission.rateOverTime = 8f;
		emission.SetBursts( System.Array.Empty<ParticleSystem.Burst>() );

		ParticleSystem.ShapeModule shape = ps.shape;
		shape.enabled = true;
		shape.shapeType = ParticleSystemShapeType.Box;
		shape.scale = new Vector3( 8f, 0.15f, 8f );

		ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
		color.enabled = true;
		Gradient gradient = new Gradient();
		gradient.SetKeys(
			new[]
			{
				new GradientColorKey( new Color( 1.8f, 1.4f, 0.7f ), 0f ),
				new GradientColorKey( new Color( 1.2f, 0.85f, 0.35f ), 1f )
			},
			new[]
			{
				new GradientAlphaKey( 0f, 0f ),
				new GradientAlphaKey( 1f, 0.12f ),
				new GradientAlphaKey( 0.75f, 0.45f ),
				new GradientAlphaKey( 0f, 1f )
			} );
		color.color = gradient;

		ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
		size.enabled = true;
		AnimationCurve sizeCurve = new AnimationCurve(
			new Keyframe( 0f, 0.35f ),
			new Keyframe( 0.15f, 1.15f ),
			new Keyframe( 1f, 0f ) );
		size.size = new ParticleSystem.MinMaxCurve( 1f, sizeCurve );

		ParticleSystem.NoiseModule noise = ps.noise;
		noise.enabled = true;
		noise.strength = 0.25f;
		noise.frequency = 0.55f;
		noise.scrollSpeed = 0.2f;
		noise.octaveCount = 2;

		ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
		velocity.enabled = true;
		velocity.space = ParticleSystemSimulationSpace.World;
		// XYZ must share the same MinMaxCurve mode.
		velocity.x = new ParticleSystem.MinMaxCurve( -0.15f, 0.15f );
		velocity.y = new ParticleSystem.MinMaxCurve( 0.6f, 1.8f );
		velocity.z = new ParticleSystem.MinMaxCurve( -0.15f, 0.15f );
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
