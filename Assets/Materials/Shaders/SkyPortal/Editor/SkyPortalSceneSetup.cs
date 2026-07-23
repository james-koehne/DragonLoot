using UnityEditor;
using UnityEngine;

/// <summary>
/// Assigns sky cubemap from the scene skybox material and spawns a test quad.
/// </summary>
public static class SkyPortalSceneSetup
{
	const string MaterialPath = "Assets/Materials/Shaders/SkyPortal/M_SkyPortal.mat";
	const string ShaderName = "DragonLoot/SkyPortal";
	const string SkyboxCubemapProperty = "_Tex";

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

		TryCopySkyCubemapFromRenderSettings( material );

		GameObject quad = GameObject.CreatePrimitive( PrimitiveType.Quad );
		quad.name = "SkyPortal_Test";
		Object.DestroyImmediate( quad.GetComponent<Collider>() );

		MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
		renderer.sharedMaterial = material;

		quad.transform.position = new Vector3( 0f, 8f, 0f );
		quad.transform.rotation = Quaternion.Euler( 90f, 0f, 0f );
		quad.transform.localScale = new Vector3( 6f, 6f, 1f );

		Selection.activeGameObject = quad;
		Undo.RegisterCreatedObjectUndo( quad, "Create Sky Portal Test" );
		EditorUtility.SetDirty( material );

		Debug.Log(
			"SkyPortal test quad created. Sky Cubemap on M_SkyPortal should match your skybox _Tex. " +
			"Use Portal Normal (default) for ceiling quads; enable View Ray for camera-aligned sampling." );
	}

	[MenuItem( "DragonLoot/Scene/Sync Sky Portal Cubemap From Skybox" )]
	public static void SyncSkyCubemapFromSkybox()
	{
		Material material = AssetDatabase.LoadAssetAtPath<Material>( MaterialPath );
		if ( material == null )
		{
			Debug.LogError( "SkyPortalSceneSetup: missing material at " + MaterialPath );
			return;
		}

		if ( !TryCopySkyCubemapFromRenderSettings( material ) )
			Debug.LogWarning( "SkyPortalSceneSetup: no cubemap found on RenderSettings.skybox (_Tex)." );

		EditorUtility.SetDirty( material );
	}

	static bool TryCopySkyCubemapFromRenderSettings( Material portalMaterial )
	{
		Material skyboxMaterial = RenderSettings.skybox;
		if ( skyboxMaterial == null )
			return false;

		if ( !skyboxMaterial.HasProperty( SkyboxCubemapProperty ) )
			return false;

		Texture cubemap = skyboxMaterial.GetTexture( SkyboxCubemapProperty );
		if ( cubemap == null )
			return false;

		portalMaterial.SetTexture( "_SkyCubemap", cubemap );

		if ( skyboxMaterial.HasProperty( "_Exposure" ) )
			portalMaterial.SetFloat( "_SkyExposure", skyboxMaterial.GetFloat( "_Exposure" ) );

		if ( skyboxMaterial.HasProperty( "_Rotation" ) )
			portalMaterial.SetFloat( "_SkyRotation", skyboxMaterial.GetFloat( "_Rotation" ) );

		return true;
	}
}
