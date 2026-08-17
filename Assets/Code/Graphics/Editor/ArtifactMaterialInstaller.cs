#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;

using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// Converts artifact visual materials to <c>DragonLoot/Artifact</c> (dirt-capable) and
/// ensures <see cref="TreasureCleaningDefinition"/> is Addressable.
/// </summary>
public static class ArtifactMaterialInstaller
{
	const string ShaderName = "DragonLoot/Artifact";
	const string MaterialFolder = "Assets/Materials/Shaders/Artifact";
	const string VisualFolder = "Assets/Addressables/Treasure/Artifacts";
	const string CleaningDefinitionPath = "Assets/Definitions/TreasureCleaningDefinition.asset";

	[InitializeOnLoadMethod]
	static void AutoInstallIfMissing()
	{
		EditorApplication.delayCall += () =>
		{
			if ( EditorApplication.isPlayingOrWillChangePlaymode )
				return;
			if ( Shader.Find( ShaderName ) == null )
				return;
			if ( AssetDatabase.LoadAssetAtPath<TreasureCleaningDefinition>( CleaningDefinitionPath ) != null
				&& Directory.Exists( MaterialFolder )
				&& Directory.GetFiles( MaterialFolder, "M_Artifact_*.mat" ).Length > 0 )
				return;

			TryInstall( forceAssignPrefabs: true );
		};
	}

	[MenuItem( DragonLootMenus.GraphicsArtifactsInstall )]
	public static void MenuInstall()
	{
		TryInstall( forceAssignPrefabs: true );
		Debug.Log( "Artifact materials installed (DragonLoot/Artifact) and cleaning definition registered." );
	}

	/// <summary>
	/// Converts materials on a single visual prefab to <c>DragonLoot/Artifact</c>. Returns true if any material was assigned.
	/// </summary>
	public static bool ConvertPrefabMaterials( string prefabPath )
	{
		if ( string.IsNullOrEmpty( prefabPath ) )
			return false;

		Shader shader = Shader.Find( ShaderName );
		if ( shader == null )
		{
			Debug.LogWarning( "Artifact shader not found: " + ShaderName );
			return false;
		}

		EnsureFolder( "Assets/Materials" );
		EnsureFolder( "Assets/Materials/Shaders" );
		EnsureFolder( MaterialFolder );
		EnsureCleaningDefinition();

		GameObject prefabRoot = PrefabUtility.LoadPrefabContents( prefabPath );
		if ( prefabRoot == null )
			return false;

		bool dirty = false;
		try
		{
			var cache = new Dictionary<Material, Material>();
			dirty = ConvertRenderersOnRoot( prefabRoot, shader, cache, forceAssign: true );
			if ( dirty )
				PrefabUtility.SaveAsPrefabAsset( prefabRoot, prefabPath );
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents( prefabRoot );
		}

		return dirty;
	}

	static bool ConvertRenderersOnRoot(
		GameObject prefabRoot,
		Shader shader,
		Dictionary<Material, Material> sourceToArtifactMat,
		bool forceAssign )
	{
		bool dirty = false;
		Renderer[] renderers = prefabRoot.GetComponentsInChildren<Renderer>( true );
		for ( int r = 0; r < renderers.Length; r++ )
		{
			Renderer renderer = renderers[ r ];
			if ( renderer == null )
				continue;

			Material[] shared = renderer.sharedMaterials;
			if ( shared == null || shared.Length == 0 )
				continue;

			Material[] next = null;
			for ( int m = 0; m < shared.Length; m++ )
			{
				Material source = shared[ m ];
				if ( source == null )
					continue;
				if ( source.shader != null && source.shader.name == ShaderName )
					continue;

				Material artifactMat = GetOrCreateArtifactMaterial( source, shader, sourceToArtifactMat );
				if ( artifactMat == null || artifactMat == source )
					continue;

				if ( next == null )
				{
					next = new Material[ shared.Length ];
					for ( int c = 0; c < shared.Length; c++ )
						next[ c ] = shared[ c ];
				}

				next[ m ] = artifactMat;
			}

			if ( next != null && forceAssign )
			{
				renderer.sharedMaterials = next;
				dirty = true;
			}
		}

		return dirty;
	}

	public static void TryInstall( bool forceAssignPrefabs )
	{
		Shader shader = Shader.Find( ShaderName );
		if ( shader == null )
		{
			Debug.LogWarning( "Artifact shader not found: " + ShaderName + " (wait for Unity import, then re-run)." );
			return;
		}

		EnsureFolder( "Assets/Materials" );
		EnsureFolder( "Assets/Materials/Shaders" );
		EnsureFolder( MaterialFolder );
		EnsureFolder( "Assets/Definitions" );

		EnsureCleaningDefinition();
		FixExistingArtifactMaterials();

		string[] prefabGuids = AssetDatabase.FindAssets( "t:Prefab", new[] { VisualFolder } );
		var sourceToArtifactMat = new Dictionary<Material, Material>();

		for ( int i = 0; i < prefabGuids.Length; i++ )
		{
			string path = AssetDatabase.GUIDToAssetPath( prefabGuids[ i ] );
			if ( string.IsNullOrEmpty( path ) )
				continue;

			GameObject prefabRoot = PrefabUtility.LoadPrefabContents( path );
			if ( prefabRoot == null )
				continue;

			bool dirty = false;
			try
			{
				dirty = ConvertRenderersOnRoot( prefabRoot, shader, sourceToArtifactMat, forceAssignPrefabs );
				if ( dirty )
					PrefabUtility.SaveAsPrefabAsset( prefabRoot, path );
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents( prefabRoot );
			}
		}

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
	}

	static void FixExistingArtifactMaterials()
	{
		Shader shader = Shader.Find( ShaderName );
		if ( shader == null )
			return;

		string[] matGuids = AssetDatabase.FindAssets( "t:Material", new[] { MaterialFolder } );
		for ( int i = 0; i < matGuids.Length; i++ )
		{
			string path = AssetDatabase.GUIDToAssetPath( matGuids[ i ] );
			Material mat = AssetDatabase.LoadAssetAtPath<Material>( path );
			if ( mat == null )
				continue;

			if ( mat.shader != shader )
				mat.shader = shader;

			mat.enableInstancing = false;
			if ( mat.HasProperty( "_DirtMapStrength" ) )
				mat.SetFloat( "_DirtMapStrength", Mathf.Max( mat.GetFloat( "_DirtMapStrength" ), 1f ) );
			EnsureDirtMapBlackFallback( mat );
			EditorUtility.SetDirty( mat );
		}
	}

	static Material GetOrCreateArtifactMaterial(
		Material source,
		Shader shader,
		Dictionary<Material, Material> cache )
	{
		if ( source == null || shader == null )
			return null;

		if ( cache.TryGetValue( source, out Material existing ) && existing != null )
			return existing;

		string safeName = "M_Artifact_" + SanitizeFileName( source.name );
		string matPath = MaterialFolder + "/" + safeName + ".mat";
		Material mat = AssetDatabase.LoadAssetAtPath<Material>( matPath );
		if ( mat == null )
		{
			mat = new Material( shader );
			mat.name = safeName;
			AssetDatabase.CreateAsset( mat, matPath );
		}
		else if ( mat.shader != shader )
		{
			mat.shader = shader;
		}

		CopyMapsFromSource( source, mat );
		mat.SetFloat( "_DirtStrength", 0f );
		mat.SetFloat( "_DirtMapStrength", 1f );
		ApplyDirtPropertyDefaults( mat );
		ApplyShinePropertyDefaults( mat );
		mat.enableInstancing = false;
		EditorUtility.SetDirty( mat );
		cache[ source ] = mat;
		return mat;
	}

	static void EnsureDirtMapBlackFallback( Material mat )
	{
		if ( mat == null || !mat.HasProperty( "_DirtMap" ) )
			return;

		// Black = full dirt coverage when no blotch texture is assigned (mask polarity: black=dirt, white=clean).
		// Also migrate the previous installer white fallback so dirty artifacts still show dirt.
		Texture dirtMap = mat.GetTexture( "_DirtMap" );
		if ( dirtMap == null || dirtMap == Texture2D.whiteTexture )
			mat.SetTexture( "_DirtMap", Texture2D.blackTexture );
	}

	static void ApplyDirtPropertyDefaults( Material mat )
	{
		if ( mat == null )
			return;

		EnsureDirtMapBlackFallback( mat );
		if ( mat.HasProperty( "_DirtColor" ) )
			mat.SetColor( "_DirtColor", new Color( 0.18f, 0.12f, 0.07f, 1f ) );
		if ( mat.HasProperty( "_DirtContrast" ) )
			mat.SetFloat( "_DirtContrast", 1f );
		if ( mat.HasProperty( "_DirtMetallicLoss" ) )
			mat.SetFloat( "_DirtMetallicLoss", 0.85f );
		if ( mat.HasProperty( "_DirtSmoothnessLoss" ) )
			mat.SetFloat( "_DirtSmoothnessLoss", 0.75f );
		if ( mat.HasProperty( "_DirtAlbedoMultiply" ) )
			mat.SetFloat( "_DirtAlbedoMultiply", 0.55f );
	}

	static void ApplyShinePropertyDefaults( Material mat )
	{
		if ( mat == null )
			return;

		if ( mat.HasProperty( "_CleanSmoothnessBoost" ) )
			mat.SetFloat( "_CleanSmoothnessBoost", 0.15f );
		if ( mat.HasProperty( "_CleanFresnelBoost" ) )
			mat.SetFloat( "_CleanFresnelBoost", 0.6f );
		if ( mat.HasProperty( "_MatCapColor" ) )
			mat.SetColor( "_MatCapColor", new Color( 1f, 0.92f, 0.75f, 1f ) );
		if ( mat.HasProperty( "_MatCapIntensity" ) )
			mat.SetFloat( "_MatCapIntensity", 0.45f );
		if ( mat.HasProperty( "_MatCapPower" ) )
			mat.SetFloat( "_MatCapPower", 2.5f );
		if ( mat.HasProperty( "_ShineColor" ) )
			mat.SetColor( "_ShineColor", new Color( 1.2f, 0.95f, 0.65f, 1f ) );
		if ( mat.HasProperty( "_ShineBoost" ) )
			mat.SetFloat( "_ShineBoost", 0.4f );
	}

	static void CopyMapsFromSource( Material source, Material dest )
	{
		if ( source == null || dest == null )
			return;

		TryCopyTexture( source, dest, "_BaseMap", "_BaseMap", "_MainTex" );
		TryCopyColor( source, dest, "_BaseColor", "_BaseColor", "_Color" );
		TryCopyTexture( source, dest, "_BumpMap", "_BumpMap", "_NormalMap" );
		TryCopyFloat( source, dest, "_BumpScale", "_BumpScale", "_NormalScale" );
		TryCopyTexture( source, dest, "_MetallicGlossMap", "_MetallicGlossMap", "_MaskMap" );
		TryCopyFloat( source, dest, "_Metallic", "_Metallic" );
		TryCopyFloat( source, dest, "_Smoothness", "_Smoothness", "_Glossiness" );
		TryCopyTexture( source, dest, "_OcclusionMap", "_OcclusionMap" );
		TryCopyFloat( source, dest, "_OcclusionStrength", "_OcclusionStrength" );
	}

	static void TryCopyTexture( Material source, Material dest, string destProp, params string[] sourceProps )
	{
		if ( !dest.HasProperty( destProp ) )
			return;

		for ( int i = 0; i < sourceProps.Length; i++ )
		{
			if ( !source.HasProperty( sourceProps[ i ] ) )
				continue;
			Texture tex = source.GetTexture( sourceProps[ i ] );
			if ( tex != null )
			{
				dest.SetTexture( destProp, tex );
				return;
			}
		}
	}

	static void TryCopyColor( Material source, Material dest, string destProp, params string[] sourceProps )
	{
		if ( !dest.HasProperty( destProp ) )
			return;

		for ( int i = 0; i < sourceProps.Length; i++ )
		{
			if ( !source.HasProperty( sourceProps[ i ] ) )
				continue;
			dest.SetColor( destProp, source.GetColor( sourceProps[ i ] ) );
			return;
		}
	}

	static void TryCopyFloat( Material source, Material dest, string destProp, params string[] sourceProps )
	{
		if ( !dest.HasProperty( destProp ) )
			return;

		for ( int i = 0; i < sourceProps.Length; i++ )
		{
			if ( !source.HasProperty( sourceProps[ i ] ) )
				continue;
			dest.SetFloat( destProp, source.GetFloat( sourceProps[ i ] ) );
			return;
		}
	}

	static void EnsureCleaningDefinition()
	{
		TreasureCleaningDefinition def = AssetDatabase.LoadAssetAtPath<TreasureCleaningDefinition>( CleaningDefinitionPath );
		if ( def == null )
		{
			def = ScriptableObject.CreateInstance<TreasureCleaningDefinition>();
			AssetDatabase.CreateAsset( def, CleaningDefinitionPath );
		}

		RegisterDefinitionAddressable( CleaningDefinitionPath );
		EditorUtility.SetDirty( def );
	}

	static void RegisterDefinitionAddressable( string assetPath )
	{
		AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
		if ( settings == null )
		{
			Debug.LogWarning( "AddressableAssetSettings missing; skipped registering " + assetPath );
			return;
		}

		string guid = AssetDatabase.AssetPathToGUID( assetPath );
		if ( string.IsNullOrEmpty( guid ) )
			return;

		AddressableAssetGroup group = settings.DefaultGroup;
		AddressableAssetEntry entry = settings.FindAssetEntry( guid );
		if ( entry == null )
			entry = settings.CreateOrMoveEntry( guid, group, readOnly: false, postEvent: false );

		entry.SetAddress( Path.GetFileNameWithoutExtension( assetPath ) );
		entry.SetLabel( "Definition", true, true );
		settings.SetDirty( AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true );
	}

	static void EnsureFolder( string path )
	{
		if ( AssetDatabase.IsValidFolder( path ) )
			return;

		string parent = Path.GetDirectoryName( path )?.Replace( '\\', '/' );
		string leaf = Path.GetFileName( path );
		if ( string.IsNullOrEmpty( parent ) || string.IsNullOrEmpty( leaf ) )
			return;

		if ( !AssetDatabase.IsValidFolder( parent ) )
			EnsureFolder( parent );

		AssetDatabase.CreateFolder( parent, leaf );
	}

	static string SanitizeFileName( string name )
	{
		if ( string.IsNullOrEmpty( name ) )
			return "Material";

		char[] invalid = Path.GetInvalidFileNameChars();
		char[] chars = name.ToCharArray();
		for ( int i = 0; i < chars.Length; i++ )
		{
			for ( int j = 0; j < invalid.Length; j++ )
			{
				if ( chars[ i ] == invalid[ j ] || chars[ i ] == ' ' )
				{
					chars[ i ] = '_';
					break;
				}
			}
		}

		return new string( chars );
	}
}
#endif
