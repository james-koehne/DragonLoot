#if UNITY_EDITOR
using UnityEditor;

using UnityEngine;

/// <summary>
/// Ensures <see cref="BuildModeDefinition"/> exists, is Addressable, and has the Build Ghost shader assigned.
/// </summary>
public static class BuildModeBootstrap
{
	public const string DefinitionPath = "Assets/Definitions/BuildModeDefinition.asset";
	const string ShaderPath = "Assets/Materials/Shaders/Placement/BuildGhost.shader";
	const string MaterialPath = "Assets/Materials/Shaders/Placement/M_BuildGhost.mat";

	[InitializeOnLoadMethod]
	static void QueueEnsure()
	{
		EditorApplication.delayCall += () => EnsureBuildModeDefinition();
	}

	[MenuItem( DragonLootMenus.DefinitionsEnsureBuildMode )]
	static void MenuEnsure()
	{
		EnsureBuildModeDefinition();
		Debug.Log( "BuildModeDefinition ensured and registered as Addressable." );
	}

	public static BuildModeDefinition EnsureBuildModeDefinition()
	{
		AddressableEditorUtil.EnsureFolder( "Assets/Definitions" );
		AddressableEditorUtil.EnsureFolder( "Assets/Materials/Shaders/Placement" );

		Shader shader = AssetDatabase.LoadAssetAtPath<Shader>( ShaderPath );
		Material material = AssetDatabase.LoadAssetAtPath<Material>( MaterialPath );
		if ( material == null && shader != null )
		{
			material = new Material( shader );
			material.name = "M_BuildGhost";
			AssetDatabase.CreateAsset( material, MaterialPath );
			AssetDatabase.SaveAssets();
		}

		BuildModeDefinition def = AssetDatabase.LoadAssetAtPath<BuildModeDefinition>( DefinitionPath );
		if ( def == null && System.IO.File.Exists( DefinitionPath ) )
		{
			AssetDatabase.ImportAsset( DefinitionPath );
			def = AssetDatabase.LoadAssetAtPath<BuildModeDefinition>( DefinitionPath );
		}

		if ( def == null )
		{
			def = ScriptableObject.CreateInstance<BuildModeDefinition>();
			def.name = "BuildModeDefinition";
			ApplyDefaults( def, shader );
			AssetDatabase.CreateAsset( def, DefinitionPath );
			AssetDatabase.SaveAssets();
		}
		else if ( def.ghostShader == null && shader != null )
		{
			def.ghostShader = shader;
			EditorUtility.SetDirty( def );
			AssetDatabase.SaveAssets();
		}

		AddressableEditorUtil.TryRegister( DefinitionPath, DefinitionPath );
		return def;
	}

	static void ApplyDefaults( BuildModeDefinition def, Shader shader )
	{
		def.showRadius = 20f;
		def.fadeStart = 12f;
		def.fadeEnd = 18f;
		def.buildHoldSeconds = 2f;
		def.aimMaxDistance = 0f;
		def.ghostShader = shader;
		def.ghostColor = new Color( 0.35f, 0.75f, 1f, 0.4f );
		def.ghostFresnelPower = 2.4f;
		def.ghostFresnelBoost = 0.7f;
		def.ghostPulseAmount = 0.12f;
		def.ghostPulseSpeed = 0.85f;
		def.ghostRimIntensity = 1.15f;
		def.ghostCoreIntensity = 0.28f;
		def.hammerLocalOffset = new Vector3( 0.05f, -0.05f, 0.08f );
		def.hammerLocalEuler = new Vector3( 10f, 0f, -20f );
		def.hammerLocalScale = 0.35f;
	}
}
#endif
