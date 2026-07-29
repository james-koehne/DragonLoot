#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[InitializeOnLoad]
static class HoverOutlineRendererFeatureInstaller
{
	const string RendererAssetPath = "Assets/Settings/PC_Renderer.asset";

	static HoverOutlineRendererFeatureInstaller()
	{
		EditorApplication.delayCall += EnsureFeatureOnRenderer;
	}

	static void EnsureFeatureOnRenderer()
	{
		UniversalRendererData rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>( RendererAssetPath );
		if ( rendererData == null )
			return;

		for ( int i = 0; i < rendererData.rendererFeatures.Count; i++ )
		{
			if ( rendererData.rendererFeatures[ i ] is HoverOutlineRendererFeature )
				return;
		}

		HoverOutlineRendererFeature feature = ScriptableObject.CreateInstance<HoverOutlineRendererFeature>();
		feature.name = "HoverOutline";
		AssetDatabase.AddObjectToAsset( feature, rendererData );
		rendererData.rendererFeatures.Add( feature );
		EditorUtility.SetDirty( rendererData );
		AssetDatabase.SaveAssets();
	}
}
#endif
