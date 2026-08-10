#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[InitializeOnLoad]
static class TreasureSparkleRendererFeatureInstaller
{
	const string RendererAssetPath = "Assets/Settings/PC_Renderer.asset";
	const string DefinitionAssetPath = "Assets/Definitions/TreasureSparkleDefinition.asset";

	static TreasureSparkleRendererFeatureInstaller()
	{
		EditorApplication.delayCall += EnsureInstalled;
	}

	static void EnsureInstalled()
	{
		EnsureDefinitionAsset();
		EnsureFeatureOnRenderer();
	}

	static TreasureSparkleDefinition EnsureDefinitionAsset()
	{
		TreasureSparkleDefinition definition = AssetDatabase.LoadAssetAtPath<TreasureSparkleDefinition>( DefinitionAssetPath );
		if ( definition == null )
		{
			definition = ScriptableObject.CreateInstance<TreasureSparkleDefinition>();
			definition.name = "TreasureSparkleDefinition";
			AssetDatabase.CreateAsset( definition, DefinitionAssetPath );
			AssetDatabase.SaveAssets();
		}

		RegisterDefinitionAddressable( DefinitionAssetPath );
		return definition;
	}

	static void EnsureFeatureOnRenderer()
	{
		UniversalRendererData rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>( RendererAssetPath );
		if ( rendererData == null )
			return;

		TreasureSparkleDefinition definition = EnsureDefinitionAsset();
		Shader resolveShader = Shader.Find( "DragonLoot/Treasure Sparkle Mask Resolve" );
		Shader fallbackShader = Shader.Find( "DragonLoot/Treasure Sparkle Fallback Mask" );
		Shader sparkleShader = Shader.Find( "DragonLoot/Treasure Sparkle" );
		ComputeShader discoverCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(
			"Assets/Materials/Shaders/TreasureSparkle/TreasureSparkleDiscover.compute" );

		for ( int i = 0; i < rendererData.rendererFeatures.Count; i++ )
		{
			if ( rendererData.rendererFeatures[ i ] is TreasureSparkleRendererFeature existing )
			{
				SerializedObject so = new SerializedObject( existing );
				SerializedProperty defProp = so.FindProperty( "definition" );
				SerializedProperty resolveProp = so.FindProperty( "maskResolveShader" );
				SerializedProperty fallbackProp = so.FindProperty( "fallbackMaskShader" );
				SerializedProperty sparkleProp = so.FindProperty( "sparkleShader" );
				SerializedProperty discoverProp = so.FindProperty( "discoverCompute" );
				bool dirty = false;
				if ( defProp != null && defProp.objectReferenceValue == null && definition != null )
				{
					defProp.objectReferenceValue = definition;
					dirty = true;
				}
				if ( resolveProp != null && resolveProp.objectReferenceValue == null && resolveShader != null )
				{
					resolveProp.objectReferenceValue = resolveShader;
					dirty = true;
				}
				if ( fallbackProp != null && fallbackProp.objectReferenceValue == null && fallbackShader != null )
				{
					fallbackProp.objectReferenceValue = fallbackShader;
					dirty = true;
				}
				if ( sparkleProp != null && sparkleProp.objectReferenceValue == null && sparkleShader != null )
				{
					sparkleProp.objectReferenceValue = sparkleShader;
					dirty = true;
				}
				if ( discoverProp != null && discoverProp.objectReferenceValue == null && discoverCompute != null )
				{
					discoverProp.objectReferenceValue = discoverCompute;
					dirty = true;
				}
				else if ( discoverProp != null && discoverCompute != null && discoverProp.objectReferenceValue != discoverCompute )
				{
					discoverProp.objectReferenceValue = discoverCompute;
					dirty = true;
				}
				if ( dirty )
				{
					so.ApplyModifiedPropertiesWithoutUndo();
					EditorUtility.SetDirty( existing );
					EditorUtility.SetDirty( rendererData );
					AssetDatabase.SaveAssets();
				}
				return;
			}
		}

		TreasureSparkleRendererFeature feature = ScriptableObject.CreateInstance<TreasureSparkleRendererFeature>();
		feature.name = "TreasureSparkle";
		AssetDatabase.AddObjectToAsset( feature, rendererData );
		rendererData.rendererFeatures.Add( feature );

		SerializedObject featureSo = new SerializedObject( feature );
		SerializedProperty definitionProp = featureSo.FindProperty( "definition" );
		SerializedProperty maskResolveProp = featureSo.FindProperty( "maskResolveShader" );
		SerializedProperty fallbackMaskProp = featureSo.FindProperty( "fallbackMaskShader" );
		SerializedProperty sparklePropNew = featureSo.FindProperty( "sparkleShader" );
		SerializedProperty discoverPropNew = featureSo.FindProperty( "discoverCompute" );
		if ( definitionProp != null )
			definitionProp.objectReferenceValue = definition;
		if ( maskResolveProp != null )
			maskResolveProp.objectReferenceValue = resolveShader;
		if ( fallbackMaskProp != null )
			fallbackMaskProp.objectReferenceValue = fallbackShader;
		if ( sparklePropNew != null )
			sparklePropNew.objectReferenceValue = sparkleShader;
		if ( discoverPropNew != null )
			discoverPropNew.objectReferenceValue = discoverCompute;
		featureSo.ApplyModifiedPropertiesWithoutUndo();

		EditorUtility.SetDirty( rendererData );
		AssetDatabase.SaveAssets();
	}

	static void RegisterDefinitionAddressable( string assetPath )
	{
		AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
		if ( settings == null )
			return;

		string guid = AssetDatabase.AssetPathToGUID( assetPath );
		if ( string.IsNullOrEmpty( guid ) )
			return;

		AddressableAssetEntry entry = settings.FindAssetEntry( guid );
		if ( entry == null )
			entry = settings.CreateOrMoveEntry( guid, settings.DefaultGroup );

		entry.SetAddress( "TreasureSparkleDefinition" );
		if ( !entry.labels.Contains( "Definition" ) )
			entry.SetLabel( "Definition", true, true );
		settings.SetDirty( AddressableAssetSettings.ModificationEvent.EntryModified, entry, true );
	}
}
#endif
