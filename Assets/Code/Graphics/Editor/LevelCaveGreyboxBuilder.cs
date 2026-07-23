using System.Collections.Generic;
using System.IO;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds <c>Assets/Scenes/Level.unity</c> as a large greyboxed cave for prototyping.
/// Runs once automatically if the scene is missing; rebuild via DragonLoot menu.
/// </summary>
public static class LevelCaveGreyboxBuilder
{
	const string ScenePath = "Assets/Scenes/Level.unity";
	const string MaterialsFolder = "Assets/Materials";
	const string CoinPilePrefab = "Assets/ThirdParty/REAL_DEDICATED/BasicTreasureCoins/Prefabs/CoinPile_Gold.prefab";

	[MenuItem( "DragonLoot/Rebuild Level Cave Greybox" )]
	public static void RebuildFromMenu()
	{
		BuildLevelScene( force: true );
	}

	/// <summary>Unity batchmode entry: -executeMethod LevelCaveGreyboxBuilder.RebuildFromCommandLine</summary>
	public static void RebuildFromCommandLine()
	{
		BuildLevelScene( force: true );
	}

	[InitializeOnLoadMethod]
	static void EnsureLevelSceneExists()
	{
		if ( File.Exists( ScenePath ) )
			return;

		EditorApplication.delayCall += () =>
		{
			if ( File.Exists( ScenePath ) )
				return;
			BuildLevelScene( force: false );
		};
	}

	static void BuildLevelScene( bool force )
	{
		if ( !force && File.Exists( ScenePath ) )
			return;

		EnsureFolders();

		Material floorMat = GetOrCreateGreyMat( "GreyboxFloor", new Color( 0.45f, 0.44f, 0.42f ) );
		Material wallMat = GetOrCreateGreyMat( "GreyboxWall", new Color( 0.35f, 0.34f, 0.32f ) );
		Material ceilingMat = GetOrCreateGreyMat( "GreyboxCeiling", new Color( 0.28f, 0.28f, 0.27f ) );
		Material rockMat = GetOrCreateGreyMat( "GreyboxRock", new Color( 0.40f, 0.39f, 0.37f ) );

		Scene previousActive = EditorSceneManager.GetActiveScene();
		Scene scene = EditorSceneManager.NewScene( NewSceneSetup.EmptyScene, NewSceneMode.Additive );
		scene.name = "Level";
		EditorSceneManager.SetActiveScene( scene );

		RenderSettings.fog = true;
		RenderSettings.fogMode = FogMode.ExponentialSquared;
		RenderSettings.fogDensity = 0.018f;
		RenderSettings.fogColor = new Color( 0.12f, 0.13f, 0.14f );
		RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
		RenderSettings.ambientLight = new Color( 0.22f, 0.23f, 0.25f );
		RenderSettings.skybox = null;

		GameObject root = new GameObject( "GreyboxCave" );
		Transform geometry = new GameObject( "Geometry" ).transform;
		geometry.SetParent( root.transform, false );
		Transform lighting = new GameObject( "Lighting" ).transform;
		lighting.SetParent( root.transform, false );
		Transform treasure = new GameObject( "Treasure" ).transform;
		treasure.SetParent( root.transform, false );
		GameObject markersGo = new GameObject( "Markers" );
		Transform markers = markersGo.transform;
		markers.SetParent( root.transform, false );
		LevelSceneMarkers markerComponent = markersGo.AddComponent<LevelSceneMarkers>();

		const float wallH = 16f;
		float wallY = wallH * 0.5f;
		const float ceilY = 16f;

		AddCube( geometry, "Floor_Main", new Vector3( 0f, -1f, 0f ), new Vector3( 80f, 2f, 48f ), floorMat );
		AddCube( geometry, "Floor_NorthWing", new Vector3( 8f, -0.75f, 34f ), new Vector3( 36f, 1.5f, 22f ), floorMat );
		AddCube( geometry, "Floor_EastChamber", new Vector3( 48f, -0.5f, 4f ), new Vector3( 28f, 1f, 30f ), floorMat );
		AddCube( geometry, "Floor_WestAlcove", new Vector3( -42f, -0.5f, -6f ), new Vector3( 22f, 1f, 24f ), floorMat );
		AddCube( geometry, "Floor_Entrance", new Vector3( 0f, -1f, -42f ), new Vector3( 18f, 2f, 28f ), floorMat );
		AddCube( geometry, "Floor_RaisedNorth", new Vector3( -6f, 0.75f, 12f ), new Vector3( 16f, 0.5f, 10f ), floorMat );
		AddCube( geometry, "Floor_RaisedEast", new Vector3( 22f, 1f, -8f ), new Vector3( 12f, 0.5f, 14f ), floorMat );
		AddCube( geometry, "Floor_StepSouth", new Vector3( 0f, -0.25f, -22f ), new Vector3( 24f, 0.5f, 8f ), floorMat );

		AddCube( geometry, "Wall_S_L", new Vector3( -22f, wallY, -25f ), new Vector3( 28f, wallH, 4f ), wallMat );
		AddCube( geometry, "Wall_S_R", new Vector3( 22f, wallY, -25f ), new Vector3( 28f, wallH, 4f ), wallMat );
		AddCube( geometry, "Wall_S_EntranceL", new Vector3( -12f, wallY, -30f ), new Vector3( 6f, wallH, 10f ), wallMat );
		AddCube( geometry, "Wall_S_EntranceR", new Vector3( 12f, wallY, -30f ), new Vector3( 6f, wallH, 10f ), wallMat );

		AddCube( geometry, "Wall_N_A", new Vector3( -24f, wallY, 25f ), new Vector3( 30f, wallH, 5f ), wallMat );
		AddCube( geometry, "Wall_N_B", new Vector3( 20f, wallY + 1f, 27f ), new Vector3( 26f, wallH + 2f, 6f ), wallMat );
		AddCube( geometry, "Wall_N_WingL", new Vector3( -4f, wallY, 44f ), new Vector3( 4f, wallH, 18f ), wallMat );
		AddCube( geometry, "Wall_N_WingR", new Vector3( 24f, wallY, 44f ), new Vector3( 4f, wallH, 18f ), wallMat );
		AddCube( geometry, "Wall_N_WingBack", new Vector3( 10f, wallY, 52f ), new Vector3( 32f, wallH, 4f ), wallMat );

		AddCube( geometry, "Wall_W_A", new Vector3( -41f, wallY, -8f ), new Vector3( 5f, wallH, 36f ), wallMat );
		AddCube( geometry, "Wall_W_B", new Vector3( -43f, wallY + 0.5f, 14f ), new Vector3( 6f, wallH + 1f, 20f ), wallMat );
		AddCube( geometry, "Wall_W_AlcoveN", new Vector3( -52f, wallY, 6f ), new Vector3( 4f, wallH, 16f ), wallMat );
		AddCube( geometry, "Wall_W_AlcoveS", new Vector3( -52f, wallY, -16f ), new Vector3( 4f, wallH, 14f ), wallMat );
		AddCube( geometry, "Wall_W_AlcoveBack", new Vector3( -54f, wallY, -4f ), new Vector3( 4f, wallH, 28f ), wallMat );

		AddCube( geometry, "Wall_E_A", new Vector3( 41f, wallY, -10f ), new Vector3( 5f, wallH, 32f ), wallMat );
		AddCube( geometry, "Wall_E_B", new Vector3( 43f, wallY + 1f, 16f ), new Vector3( 6f, wallH + 2f, 18f ), wallMat );
		AddCube( geometry, "Wall_E_ChamberN", new Vector3( 60f, wallY, 18f ), new Vector3( 4f, wallH, 16f ), wallMat );
		AddCube( geometry, "Wall_E_ChamberS", new Vector3( 60f, wallY, -12f ), new Vector3( 4f, wallH, 18f ), wallMat );
		AddCube( geometry, "Wall_E_ChamberBack", new Vector3( 62f, wallY, 2f ), new Vector3( 4f, wallH, 36f ), wallMat );

		AddCube( geometry, "Wall_Corner_0", new Vector3( -34f, wallY, -20f ), new Vector3( 10f, wallH + 1f, 8f ), wallMat, Quaternion.Euler( 0f, -35f, 0f ) );
		AddCube( geometry, "Wall_Corner_1", new Vector3( 34f, wallY, -20f ), new Vector3( 10f, wallH + 1f, 8f ), wallMat, Quaternion.Euler( 0f, 35f, 0f ) );
		AddCube( geometry, "Wall_Corner_2", new Vector3( -34f, wallY, 20f ), new Vector3( 10f, wallH + 1f, 8f ), wallMat, Quaternion.Euler( 0f, -145f, 0f ) );
		AddCube( geometry, "Wall_Corner_3", new Vector3( 34f, wallY, 20f ), new Vector3( 10f, wallH + 1f, 8f ), wallMat, Quaternion.Euler( 0f, 145f, 0f ) );

		AddCube( geometry, "Ceiling_SW", new Vector3( -22f, ceilY, -8f ), new Vector3( 36f, 2f, 28f ), ceilingMat );
		AddCube( geometry, "Ceiling_SE", new Vector3( 24f, ceilY, -10f ), new Vector3( 32f, 2f, 26f ), ceilingMat );
		AddCube( geometry, "Ceiling_NW", new Vector3( -18f, ceilY, 16f ), new Vector3( 40f, 2f, 20f ), ceilingMat );
		AddCube( geometry, "Ceiling_NE", new Vector3( 22f, ceilY, 18f ), new Vector3( 34f, 2f, 18f ), ceilingMat );
		AddCube( geometry, "Ceiling_NorthWing", new Vector3( 10f, ceilY, 40f ), new Vector3( 36f, 2f, 20f ), ceilingMat );
		AddCube( geometry, "Ceiling_EastChamber", new Vector3( 50f, ceilY, 2f ), new Vector3( 26f, 2f, 28f ), ceilingMat );
		AddCube( geometry, "Ceiling_WestAlcove", new Vector3( -48f, ceilY, -4f ), new Vector3( 20f, 2f, 24f ), ceilingMat );
		AddCube( geometry, "Ceiling_Entrance", new Vector3( 0f, ceilY - 2f, -40f ), new Vector3( 16f, 2f, 24f ), ceilingMat );
		AddCube( geometry, "Ceiling_WellRim_N", new Vector3( 4f, ceilY + 1.5f, 6f ), new Vector3( 14f, 1f, 4f ), ceilingMat );
		AddCube( geometry, "Ceiling_WellRim_S", new Vector3( 4f, ceilY + 1.5f, -6f ), new Vector3( 14f, 1f, 4f ), ceilingMat );
		AddCube( geometry, "Ceiling_WellRim_E", new Vector3( 12f, ceilY + 1.5f, 0f ), new Vector3( 4f, 1f, 10f ), ceilingMat );
		AddCube( geometry, "Ceiling_WellRim_W", new Vector3( -4f, ceilY + 1.5f, 0f ), new Vector3( 4f, 1f, 10f ), ceilingMat );

		AddCube( geometry, "Rock_Pillar_A", new Vector3( -14f, 5f, 4f ), new Vector3( 5f, 10f, 5f ), rockMat );
		AddCube( geometry, "Rock_Pillar_B", new Vector3( 16f, 4.5f, 10f ), new Vector3( 4f, 9f, 4.5f ), rockMat );
		AddCube( geometry, "Rock_Pillar_C", new Vector3( 8f, 3f, -12f ), new Vector3( 6f, 6f, 4f ), rockMat );
		AddCube( geometry, "Rock_Cluster_N", new Vector3( -18f, 2f, 18f ), new Vector3( 8f, 4f, 6f ), rockMat );
		AddCube( geometry, "Rock_Cluster_E", new Vector3( 30f, 2.5f, -4f ), new Vector3( 7f, 5f, 8f ), rockMat );
		AddCube( geometry, "Rock_Cluster_W", new Vector3( -28f, 2f, -2f ), new Vector3( 6f, 4f, 7f ), rockMat );
		AddCube( geometry, "Rock_Stalagmite_1", new Vector3( -6f, 2f, -6f ), new Vector3( 2.5f, 4f, 2.5f ), rockMat );
		AddCube( geometry, "Rock_Stalagmite_2", new Vector3( 10f, 1.75f, 16f ), new Vector3( 2f, 3.5f, 2f ), rockMat );
		AddCube( geometry, "Rock_Stalagmite_3", new Vector3( 36f, 2.25f, 8f ), new Vector3( 3f, 4.5f, 3f ), rockMat );
		AddCube( geometry, "Rock_Boulder_Entrance", new Vector3( -8f, 1.5f, -34f ), new Vector3( 5f, 3f, 4f ), rockMat );
		AddCube( geometry, "Rock_Boulder_East", new Vector3( 44f, 1.5f, -8f ), new Vector3( 4f, 3f, 5f ), rockMat );
		AddCube( geometry, "Rock_Overhang_N", new Vector3( 6f, 12f, 30f ), new Vector3( 10f, 3f, 6f ), rockMat );

		GameObject spawn = new GameObject( "PlayerSpawn" );
		spawn.transform.SetParent( markers, false );
		spawn.transform.position = new Vector3( 0f, 0.1f, -8f );

		markerComponent.playerSpawn = spawn.transform;

		GameObject sunGo = new GameObject( "Directional Light" );
		sunGo.transform.SetParent( lighting, false );
		sunGo.transform.position = new Vector3( 0f, 30f, 0f );
		sunGo.transform.rotation = Quaternion.Euler( 50f, -45f, 0f );
		Light sun = sunGo.AddComponent<Light>();
		sun.type = LightType.Directional;
		sun.color = new Color( 0.78f, 0.86f, 0.95f );
		sun.intensity = 0.55f;
		sun.shadows = LightShadows.Soft;
		markerComponent.sunLight = sun;

		AddPointLight( lighting, "LightWell_Point", new Vector3( 4f, 14f, 0f ), new Color( 1f, 0.92f, 0.75f ), 4.5f, 35f );
		AddPointLight( lighting, "Entrance_Point", new Vector3( 0f, 6f, -40f ), new Color( 0.9f, 0.95f, 1f ), 3f, 28f );
		AddPointLight( lighting, "EastChamber_Point", new Vector3( 50f, 7f, 2f ), new Color( 0.85f, 0.9f, 1f ), 2.5f, 22f );
		AddPointLight( lighting, "NorthWing_Point", new Vector3( 10f, 7f, 38f ), new Color( 0.8f, 0.88f, 1f ), 2.2f, 20f );

		PlaceTreasurePiles( treasure );

		Directory.CreateDirectory( Path.GetDirectoryName( ScenePath ) );
		bool saved = EditorSceneManager.SaveScene( scene, ScenePath );
		if ( !saved )
		{
			Debug.LogError( "LevelCaveGreyboxBuilder: failed to save " + ScenePath );
			EditorSceneManager.CloseScene( scene, true );
			if ( previousActive.IsValid() )
				EditorSceneManager.SetActiveScene( previousActive );
			return;
		}

		EditorSceneManager.CloseScene( scene, true );
		if ( previousActive.IsValid() && previousActive.isLoaded )
			EditorSceneManager.SetActiveScene( previousActive );

		AssetDatabase.Refresh();
		AddSceneToBuildSettings( ScenePath );
		Debug.Log( "LevelCaveGreyboxBuilder: wrote greyboxed cave scene at " + ScenePath );
	}

	static void PlaceTreasurePiles( Transform parent )
	{
		GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>( CoinPilePrefab );
		if ( prefab == null )
		{
			Debug.LogWarning( "LevelCaveGreyboxBuilder: CoinPile_Gold prefab missing at " + CoinPilePrefab );
			return;
		}

		Vector3[] positions =
		{
			new Vector3( -13.5f, 0f, 37f ),
			new Vector3( 10f, 0f, 38f ),
			new Vector3( 48f, 0.5f, 4f ),
			new Vector3( -42f, 0.5f, -6f ),
			new Vector3( 22f, 1.25f, -8f ),
			new Vector3( 0f, 0f, 8f )
		};

		for ( int i = 0; i < positions.Length; i++ )
		{
			GameObject instance = ( GameObject )PrefabUtility.InstantiatePrefab( prefab );
			instance.name = i == 0 ? "CoinPile" : "CoinPile_" + i;
			instance.transform.SetParent( parent, false );
			instance.transform.position = positions[ i ];
		}
	}

	static void EnsureFolders()
	{
		if ( !AssetDatabase.IsValidFolder( "Assets/Scenes" ) )
			AssetDatabase.CreateFolder( "Assets", "Scenes" );
		if ( !AssetDatabase.IsValidFolder( MaterialsFolder ) )
			AssetDatabase.CreateFolder( "Assets", "Materials" );
	}

	static Material GetOrCreateGreyMat( string name, Color color )
	{
		string path = MaterialsFolder + "/" + name + ".mat";
		Material existing = AssetDatabase.LoadAssetAtPath<Material>( path );
		if ( existing != null )
		{
			existing.color = color;
			if ( existing.HasProperty( "_BaseColor" ) )
				existing.SetColor( "_BaseColor", color );
			EditorUtility.SetDirty( existing );
			return existing;
		}

		Shader shader = Shader.Find( "Universal Render Pipeline/Lit" );
		if ( shader == null )
			shader = Shader.Find( "Standard" );

		Material mat = new Material( shader );
		mat.name = name;
		mat.color = color;
		if ( mat.HasProperty( "_BaseColor" ) )
			mat.SetColor( "_BaseColor", color );
		if ( mat.HasProperty( "_Smoothness" ) )
			mat.SetFloat( "_Smoothness", 0.15f );

		AssetDatabase.CreateAsset( mat, path );
		return mat;
	}

	static void AddCube( Transform parent, string name, Vector3 position, Vector3 scale, Material mat )
	{
		AddCube( parent, name, position, scale, mat, Quaternion.identity );
	}

	static void AddCube( Transform parent, string name, Vector3 position, Vector3 scale, Material mat, Quaternion rotation )
	{
		GameObject go = GameObject.CreatePrimitive( PrimitiveType.Cube );
		go.name = name;
		go.transform.SetParent( parent, false );
		go.transform.localPosition = position;
		go.transform.localRotation = rotation;
		go.transform.localScale = scale;
		go.isStatic = true;

		MeshRenderer renderer = go.GetComponent<MeshRenderer>();
		renderer.sharedMaterial = mat;
	}

	static void AddPointLight( Transform parent, string name, Vector3 position, Color color, float intensity, float range )
	{
		GameObject go = new GameObject( name );
		go.transform.SetParent( parent, false );
		go.transform.localPosition = position;
		Light light = go.AddComponent<Light>();
		light.type = LightType.Point;
		light.color = color;
		light.intensity = intensity;
		light.range = range;
		light.shadows = LightShadows.Soft;
	}

	static void AddSceneToBuildSettings( string scenePath )
	{
		List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>( EditorBuildSettings.scenes );
		for ( int i = 0; i < scenes.Count; i++ )
		{
			if ( scenes[ i ].path == scenePath )
			{
				scenes[ i ] = new EditorBuildSettingsScene( scenePath, true );
				EditorBuildSettings.scenes = scenes.ToArray();
				return;
			}
		}

		scenes.Add( new EditorBuildSettingsScene( scenePath, true ) );
		EditorBuildSettings.scenes = scenes.ToArray();
	}
}
