#if UNITY_EDITOR
using UnityEditor;

using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Ensures <see cref="BuildModeDefinition"/> exists, is Addressable, has the Build Ghost shader,
/// and wires <see cref="BuildModeDefinition.hammerPrefab"/> to HeldHammer when empty.
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

		bool dirty = false;
		if ( def == null )
		{
			def = ScriptableObject.CreateInstance<BuildModeDefinition>();
			def.name = "BuildModeDefinition";
			ApplyDefaults( def, shader );
			AssetDatabase.CreateAsset( def, DefinitionPath );
			dirty = true;
		}
		else
		{
			if ( def.ghostShader == null && shader != null )
			{
				def.ghostShader = shader;
				dirty = true;
			}

			// Migrate old carry-centered defaults to FPS bottom-right if still on the original values.
			if ( Approximately( def.hammerLocalOffset, new Vector3( 0.05f, -0.05f, 0.08f ) ) )
			{
				def.hammerLocalOffset = new Vector3( 0.32f, -0.28f, 0.45f );
				def.hammerLocalEuler = new Vector3( 8f, 25f, -15f );
				def.hammerLocalScale = 0.45f;
				dirty = true;
			}
		}

		GameObject held = WorldHammerPrefabMenu.EnsureHeldHammer();
		if ( held != null && ( def.hammerPrefab == null || !def.hammerPrefab.RuntimeKeyIsValid() ) )
		{
			string guid = AssetDatabase.AssetPathToGUID( WorldHammerPrefabMenu.HeldHammerPath );
			if ( !string.IsNullOrEmpty( guid ) )
			{
				def.hammerPrefab = new AssetReferenceGameObject( guid );
				dirty = true;
			}
		}

		if ( dirty )
		{
			EditorUtility.SetDirty( def );
			AssetDatabase.SaveAssets();
		}

		AddressableEditorUtil.TryRegister( DefinitionPath, "BuildModeDefinition", "Definition" );
		return def;
	}

	static bool Approximately( Vector3 a, Vector3 b )
	{
		return ( a - b ).sqrMagnitude < 0.0001f;
	}

	static void ApplyDefaults( BuildModeDefinition def, Shader shader )
	{
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
		def.hammerLocalOffset = new Vector3( 0.32f, -0.28f, 0.45f );
		def.hammerLocalEuler = new Vector3( 8f, 25f, -15f );
		def.hammerLocalScale = 0.45f;
		def.hammerDrawSeconds = 0.42f;
		def.hammerHolsterSeconds = 0.22f;
		def.hammerHolsterOffset = new Vector3( 0.12f, -0.35f, -0.15f );
		def.hammerHolsterEuler = new Vector3( 40f, 35f, -25f );
		def.hammerDrawTossHeight = 0.14f;
		def.hammerDrawSpinRevolutions = 1.15f;
		def.hammerDrawSpinAxis = new Vector3( 1f, 0.15f, 0.35f );
		def.hammerWorldPickupSeconds = 0.45f;
		def.hammerWorldPickupArcHeight = 0.35f;
		def.hammerHolsterBumpHeight = 0.06f;
	}
}
#endif
