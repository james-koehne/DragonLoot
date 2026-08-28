using UnityEngine;
using UnityEngine.Rendering.Universal;

#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed class AreaLightingRendererFeature : ScriptableRendererFeature
{
	const string EditorDefinitionPath = "Assets/Definitions/AreaLightingDefinition.asset";

	[SerializeField]
	AreaLightingDefinition definition;

	[SerializeField]
	ComputeShader fillCompute;

	AreaLightingFillPass _fillPass;
	AreaLightingDefinition _resolvedDefinition;

	public static AreaLightingDefinition ActiveDefinition { get; private set; }

	public override void Create()
	{
		_fillPass?.Dispose();
		_fillPass = new AreaLightingFillPass( fillCompute );
	}

	public override void AddRenderPasses( ScriptableRenderer renderer, ref RenderingData renderingData )
	{
		ActiveDefinition = ResolveDefinition();
		if ( ActiveDefinition == null || !ActiveDefinition.enableAreaLighting )
			return;

		CameraType camType = renderingData.cameraData.cameraType;
		if ( camType != CameraType.Game && camType != CameraType.SceneView )
			return;

		if ( _fillPass == null || !_fillPass.IsReady )
			return;

		renderer.EnqueuePass( _fillPass );
	}

	AreaLightingDefinition ResolveDefinition()
	{
		if ( definition != null )
			return definition;

#if UNITY_EDITOR
		if ( !Application.isPlaying )
		{
			AreaLightingDefinition editorDefinition = AssetDatabase.LoadAssetAtPath<AreaLightingDefinition>( EditorDefinitionPath );
			if ( editorDefinition != null )
				return editorDefinition;
		}
#endif

		_resolvedDefinition = RuntimeDefinition.Resolve( ref _resolvedDefinition );
		return _resolvedDefinition;
	}

	protected override void Dispose( bool disposing )
	{
		_fillPass?.Dispose();
		_fillPass = null;
		ActiveDefinition = null;
	}
}
