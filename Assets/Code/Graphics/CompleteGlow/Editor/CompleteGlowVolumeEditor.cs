#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[CustomEditor( typeof( CompleteGlowVolume ) )]
public class CompleteGlowVolumeEditor : Editor
{
	SerializedProperty _sourceMeshFilter;
	SerializedProperty _preset;
	SerializedProperty _settings;
	SerializedProperty _meshAssetId;
	SerializedProperty _meshFilter;
	SerializedProperty _meshRenderer;
	SerializedProperty _pointLight;
	SerializedProperty _dustMotesInstance;

	bool _autoRebuild = true;
	bool _rebuildQueued;

	void OnEnable()
	{
		_sourceMeshFilter = serializedObject.FindProperty( "_sourceMeshFilter" );
		_preset = serializedObject.FindProperty( "_preset" );
		_settings = serializedObject.FindProperty( "_settings" );
		_meshAssetId = serializedObject.FindProperty( "_meshAssetId" );
		_meshFilter = serializedObject.FindProperty( "_meshFilter" );
		_meshRenderer = serializedObject.FindProperty( "_meshRenderer" );
		_pointLight = serializedObject.FindProperty( "_pointLight" );
		_dustMotesInstance = serializedObject.FindProperty( "_dustMotesInstance" );
	}

	public override void OnInspectorGUI()
	{
		serializedObject.Update();
		CompleteGlowVolume volume = (CompleteGlowVolume)target;

		EditorGUILayout.PropertyField( _sourceMeshFilter );
		EditorGUILayout.PropertyField( _preset );
		EditorGUILayout.PropertyField( _settings, true );

		EditorGUILayout.Space();
		EditorGUILayout.LabelField( "Generated", EditorStyles.boldLabel );
		using ( new EditorGUI.DisabledScope( true ) )
		{
			EditorGUILayout.PropertyField( _meshAssetId );
			EditorGUILayout.PropertyField( _meshFilter );
			EditorGUILayout.PropertyField( _meshRenderer );
			EditorGUILayout.PropertyField( _pointLight );
			EditorGUILayout.PropertyField( _dustMotesInstance );
		}

		if ( volume.Settings != null && volume.Settings.DepthFadeDistance > 0f )
		{
			if ( !IsUrpDepthTextureEnabled() )
				EditorGUILayout.HelpBox( "Depth Fade needs Depth Texture enabled on the active URP asset. SoftShaft _DEPTH_FADE also needs to be on the material.", MessageType.Warning );
		}

		EditorGUILayout.Space();
		using ( new EditorGUILayout.HorizontalScope() )
		{
			if ( GUILayout.Button( "Rebuild", GUILayout.Height( 28f ) ) )
				Rebuild( volume );

			_autoRebuild = GUILayout.Toggle( _autoRebuild, "Auto Rebuild", GUILayout.Width( 110f ), GUILayout.Height( 28f ) );
		}

		using ( new EditorGUILayout.HorizontalScope() )
		{
			using ( new EditorGUI.DisabledScope( volume.Preset == null ) )
			{
				if ( GUILayout.Button( "Apply Preset" ) )
				{
					Undo.RecordObject( volume, "Apply Complete Glow Preset" );
					volume.ApplyPresetSettings();
					EditorUtility.SetDirty( volume );
					Rebuild( volume );
				}

				if ( GUILayout.Button( "Revert To Preset" ) )
				{
					Undo.RecordObject( volume, "Revert Complete Glow Preset" );
					volume.ApplyPresetSettings();
					EditorUtility.SetDirty( volume );
					Rebuild( volume );
				}
			}

			if ( GUILayout.Button( "Save Settings To New Preset" ) )
				SaveSettingsToNewPreset( volume );
		}

		if ( GUILayout.Button( "Select Mesh Asset" ) )
		{
			string path = CompleteGlowMenu.MeshFolder + "/CompleteGlow_" + volume.MeshAssetId + ".asset";
			Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>( path );
			if ( mesh != null )
				Selection.activeObject = mesh;
			else
				Debug.LogWarning( "CompleteGlowVolumeEditor: mesh asset not found at " + path );
		}

		if ( serializedObject.ApplyModifiedProperties() )
		{
			volume.ApplyAppearance();
			if ( _autoRebuild )
				QueueRebuild( volume );
		}
	}

	void QueueRebuild( CompleteGlowVolume volume )
	{
		if ( _rebuildQueued || volume == null )
			return;

		_rebuildQueued = true;
		EditorApplication.delayCall += () =>
		{
			_rebuildQueued = false;
			if ( volume == null )
				return;
			Rebuild( volume );
		};
	}

	static void Rebuild( CompleteGlowVolume volume )
	{
		CompleteGlowMenu.RebuildVolume( volume );
		SceneView.RepaintAll();
	}

	static void SaveSettingsToNewPreset( CompleteGlowVolume volume )
	{
		CompleteGlowMenu.EnsurePresets();
		string path = EditorUtility.SaveFilePanelInProject( "Save Complete Glow Preset", "CompleteGlow_Custom", "asset", "Choose a location for the new preset.", CompleteGlowMenu.PresetFolder );
		if ( string.IsNullOrEmpty( path ) )
			return;

		CompleteGlowPreset preset = ScriptableObject.CreateInstance<CompleteGlowPreset>();
		preset.Settings.CopyFrom( volume.Settings );
		AssetDatabase.CreateAsset( preset, path );
		AssetDatabase.SaveAssets();
		volume.Preset = preset;
		EditorUtility.SetDirty( volume );
		Selection.activeObject = preset;
	}

	static bool IsUrpDepthTextureEnabled()
	{
		RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
		if ( pipeline == null )
			return false;

		SerializedObject so = new SerializedObject( pipeline );
		SerializedProperty prop = so.FindProperty( "m_RequireDepthTexture" );
		if ( prop == null )
			prop = so.FindProperty( "supportsCameraDepthTexture" );
		if ( prop != null && prop.propertyType == SerializedPropertyType.Boolean )
			return prop.boolValue;

		return true;
	}

	[DrawGizmo( GizmoType.Selected | GizmoType.Active )]
	static void DrawWireFrustum( CompleteGlowVolume volume, GizmoType gizmoType )
	{
		if ( volume == null || volume.Settings == null )
			return;

		Vector2 footprint = volume.ResolveFootprintWorld();
		float halfX = footprint.x * 0.5f;
		float halfZ = footprint.y * 0.5f;
		float height = Mathf.Max( 0.01f, volume.Settings.Height );
		float flare = Mathf.Tan( volume.Settings.FlareAngleDegrees * Mathf.Deg2Rad ) * height;
		float topX = halfX + flare;
		float topZ = halfZ + flare;

		Matrix4x4 matrix = volume.transform.localToWorldMatrix;
		Gizmos.color = new Color( 1f, 0.85f, 0.3f, 0.85f );
		Vector3 b0 = matrix.MultiplyPoint3x4( new Vector3( -halfX, 0f, -halfZ ) );
		Vector3 b1 = matrix.MultiplyPoint3x4( new Vector3( halfX, 0f, -halfZ ) );
		Vector3 b2 = matrix.MultiplyPoint3x4( new Vector3( halfX, 0f, halfZ ) );
		Vector3 b3 = matrix.MultiplyPoint3x4( new Vector3( -halfX, 0f, halfZ ) );
		Vector3 t0 = matrix.MultiplyPoint3x4( new Vector3( -topX, height, -topZ ) );
		Vector3 t1 = matrix.MultiplyPoint3x4( new Vector3( topX, height, -topZ ) );
		Vector3 t2 = matrix.MultiplyPoint3x4( new Vector3( topX, height, topZ ) );
		Vector3 t3 = matrix.MultiplyPoint3x4( new Vector3( -topX, height, topZ ) );

		Gizmos.DrawLine( b0, b1 );
		Gizmos.DrawLine( b1, b2 );
		Gizmos.DrawLine( b2, b3 );
		Gizmos.DrawLine( b3, b0 );
		Gizmos.DrawLine( t0, t1 );
		Gizmos.DrawLine( t1, t2 );
		Gizmos.DrawLine( t2, t3 );
		Gizmos.DrawLine( t3, t0 );
		Gizmos.DrawLine( b0, t0 );
		Gizmos.DrawLine( b1, t1 );
		Gizmos.DrawLine( b2, t2 );
		Gizmos.DrawLine( b3, t3 );
	}
}
#endif
