#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[InitializeOnLoad]
static class AreaLightingRendererFeatureInstaller
{
	const string RendererAssetPath = "Assets/Settings/PC_Renderer.asset";
	const string DefinitionAssetPath = "Assets/Definitions/AreaLightingDefinition.asset";
	const string ComputeAssetPath = "Assets/Materials/Shaders/AreaLighting/AreaLightingFill.compute";

	static AreaLightingRendererFeatureInstaller()
	{
		EditorApplication.delayCall += EnsureInstalled;
	}

	[MenuItem( DragonLootMenus.GraphicsAreaLightingInstall, priority = 201 )]
	static void InstallFromMenu()
	{
		AreaLightingDefinition definition = EnsureDefinitionAsset();
		EnsureFeatureOnRenderer();
		if ( definition != null )
		{
			Selection.activeObject = definition;
			EditorGUIUtility.PingObject( definition );
		}
	}

	static void EnsureInstalled()
	{
		EnsureDefinitionAsset();
		EnsureFeatureOnRenderer();
	}

	static AreaLightingDefinition EnsureDefinitionAsset()
	{
		AreaLightingDefinition definition = AssetDatabase.LoadAssetAtPath<AreaLightingDefinition>( DefinitionAssetPath );
		if ( definition == null )
		{
			definition = ScriptableObject.CreateInstance<AreaLightingDefinition>();
			definition.name = "AreaLightingDefinition";
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

		AreaLightingDefinition definition = EnsureDefinitionAsset();
		ComputeShader fillCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>( ComputeAssetPath );

		for ( int i = 0; i < rendererData.rendererFeatures.Count; i++ )
		{
			if ( rendererData.rendererFeatures[ i ] is AreaLightingRendererFeature existing )
			{
				SerializedObject so = new SerializedObject( existing );
				SerializedProperty defProp = so.FindProperty( "definition" );
				SerializedProperty computeProp = so.FindProperty( "fillCompute" );
				bool dirty = false;
				if ( defProp != null && defProp.objectReferenceValue == null && definition != null )
				{
					defProp.objectReferenceValue = definition;
					dirty = true;
				}
				if ( computeProp != null && computeProp.objectReferenceValue == null && fillCompute != null )
				{
					computeProp.objectReferenceValue = fillCompute;
					dirty = true;
				}
				else if ( computeProp != null && fillCompute != null && computeProp.objectReferenceValue != fillCompute )
				{
					computeProp.objectReferenceValue = fillCompute;
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

		AreaLightingRendererFeature feature = ScriptableObject.CreateInstance<AreaLightingRendererFeature>();
		feature.name = "AreaLighting";
		AssetDatabase.AddObjectToAsset( feature, rendererData );
		rendererData.rendererFeatures.Add( feature );

		SerializedObject featureSo = new SerializedObject( feature );
		SerializedProperty definitionProp = featureSo.FindProperty( "definition" );
		SerializedProperty fillComputeProp = featureSo.FindProperty( "fillCompute" );
		if ( definitionProp != null )
			definitionProp.objectReferenceValue = definition;
		if ( fillComputeProp != null )
			fillComputeProp.objectReferenceValue = fillCompute;
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

		entry.SetAddress( "AreaLightingDefinition" );
		if ( !entry.labels.Contains( "Definition" ) )
			entry.SetLabel( "Definition", true, true );
		settings.SetDirty( AddressableAssetSettings.ModificationEvent.EntryModified, entry, true );
	}
}
#endif
