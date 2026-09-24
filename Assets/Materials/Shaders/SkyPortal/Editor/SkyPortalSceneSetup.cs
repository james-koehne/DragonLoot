using UnityEditor;
using UnityEngine;

/// <summary>
/// Sky Portal test setup and Time of Day asset helpers.
/// </summary>
public static class SkyPortalSceneSetup
{
	const string MaterialPath = "Assets/Materials/Shaders/SkyPortal/M_SkyPortal.mat";
	const string ShaderName = "DragonLoot/SkyPortal";
	const string TimeOfDayDefinitionPath = "Assets/Code/Graphics/TimeOfDay/TimeOfDayDefinition.asset";

	[MenuItem( "DragonLoot/Scene/Setup Sky Portal Test" )]
	public static void SetupSkyPortalTest()
	{
		Material material = AssetDatabase.LoadAssetAtPath<Material>( MaterialPath );
		if ( material == null )
		{
			Debug.LogError( "SkyPortalSceneSetup: missing material at " + MaterialPath );
			return;
		}

		Shader shader = Shader.Find( ShaderName );
		if ( shader == null )
		{
			Debug.LogError( "SkyPortalSceneSetup: shader not found: " + ShaderName );
			return;
		}

		if ( material.shader != shader )
			material.shader = shader;

		GameObject quad = GameObject.CreatePrimitive( PrimitiveType.Quad );
		quad.name = "SkyPortal_Test";
		Object.DestroyImmediate( quad.GetComponent<Collider>() );

		MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
		renderer.sharedMaterial = material;

		quad.transform.position = new Vector3( 0f, 8f, 0f );
		quad.transform.rotation = Quaternion.Euler( 90f, 0f, 0f );
		quad.transform.localScale = new Vector3( 6f, 6f, 1f );

		EnsureTimeOfDayControllerInScene();

		Selection.activeGameObject = quad;
		Undo.RegisterCreatedObjectUndo( quad, "Create Sky Portal Test" );
		EditorUtility.SetDirty( material );

		Debug.Log(
			"SkyPortal test quad created (procedural sky). " +
			"A TimeOfDayController was ensured in the scene. " +
			"Use Portal Normal (default) for ceiling quads; enable View Ray for camera-aligned sampling. " +
			"Scrub Normalized Time or set Day Length Override for a fast cycle test." );
	}

	[MenuItem( "DragonLoot/Scene/Create Default Time Of Day Definition" )]
	public static void CreateDefaultTimeOfDayDefinition()
	{
		TimeOfDayDefinition existing = AssetDatabase.LoadAssetAtPath<TimeOfDayDefinition>( TimeOfDayDefinitionPath );
		if ( existing != null )
		{
			Selection.activeObject = existing;
			EditorGUIUtility.PingObject( existing );
			Debug.Log( "SkyPortalSceneSetup: TimeOfDayDefinition already exists at " + TimeOfDayDefinitionPath );
			return;
		}

		string directory = System.IO.Path.GetDirectoryName( TimeOfDayDefinitionPath );
		if ( !string.IsNullOrEmpty( directory ) && !AssetDatabase.IsValidFolder( directory ) )
		{
			// Folder should already exist from scripts; create asset path parent if needed.
			System.IO.Directory.CreateDirectory( directory );
			AssetDatabase.Refresh();
		}

		TimeOfDayDefinition definition = ScriptableObject.CreateInstance<TimeOfDayDefinition>();
		definition.dayLengthSeconds = 600f;
		definition.startNormalizedTime = 0.3f;
		definition.sunAzimuthDegrees = 25f;
		definition.moonPhaseOffset = 0.5f;
		AssetDatabase.CreateAsset( definition, TimeOfDayDefinitionPath );
		AssetDatabase.SaveAssets();

		Selection.activeObject = definition;
		EditorGUIUtility.PingObject( definition );
		Debug.Log( "SkyPortalSceneSetup: created " + TimeOfDayDefinitionPath + ". Assign it on a TimeOfDayController in your level." );
	}

	[MenuItem( "DragonLoot/Scene/Add Time Of Day Controller" )]
	public static void AddTimeOfDayController()
	{
		TimeOfDayController controller = EnsureTimeOfDayControllerInScene();
		Selection.activeGameObject = controller.gameObject;
		Debug.Log( "SkyPortalSceneSetup: TimeOfDayController ready on '" + controller.gameObject.name + "'." );
	}

	static TimeOfDayController EnsureTimeOfDayControllerInScene()
	{
		TimeOfDayController[] controllers = Resources.FindObjectsOfTypeAll<TimeOfDayController>();
		for ( int i = 0; i < controllers.Length; i++ )
		{
			TimeOfDayController candidate = controllers[ i ];
			if ( candidate == null )
				continue;
			if ( EditorUtility.IsPersistent( candidate ) )
				continue;
			AssignDefinitionIfMissing( candidate );
			return candidate;
		}

		GameObject go = new GameObject( "TimeOfDay" );
		TimeOfDayController controller = go.AddComponent<TimeOfDayController>();
		AssignDefinitionIfMissing( controller );
		Undo.RegisterCreatedObjectUndo( go, "Create Time Of Day Controller" );
		return controller;
	}

	static void AssignDefinitionIfMissing( TimeOfDayController controller )
	{
		SerializedObject so = new SerializedObject( controller );
		SerializedProperty definitionProp = so.FindProperty( "_definition" );
		if ( definitionProp == null || definitionProp.objectReferenceValue != null )
			return;

		TimeOfDayDefinition definition = AssetDatabase.LoadAssetAtPath<TimeOfDayDefinition>( TimeOfDayDefinitionPath );
		if ( definition == null )
			return;

		definitionProp.objectReferenceValue = definition;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty( controller );
	}
}
