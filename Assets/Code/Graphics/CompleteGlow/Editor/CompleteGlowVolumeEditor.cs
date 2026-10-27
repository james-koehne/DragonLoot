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

		float height = Mathf.Max( 0.01f, volume.Settings.Height );
		float flare = Mathf.Tan( volume.Settings.FlareAngleDegrees * Mathf.Deg2Rad ) * height;
		Matrix4x4 matrix = volume.transform.localToWorldMatrix;
		Gizmos.color = new Color( 1f, 0.85f, 0.3f, 0.85f );

		Vector2[] ring = null;
		if ( volume.Settings.FootprintSource == CompleteGlowFootprintSource.SourceMesh )
			ring = volume.ResolveSourceMeshFootprintRing();

		if ( ring != null && ring.Length >= 3 )
		{
			for ( int i = 0; i < ring.Length; i++ )
			{
				int next = ( i + 1 ) % ring.Length;
				Vector2 a = ring[ i ];
				Vector2 b = ring[ next ];
				Vector2 aTop = ExpandFromCentroid( ring, a, flare );
				Vector2 bTop = ExpandFromCentroid( ring, b, flare );
				Vector3 b0 = matrix.MultiplyPoint3x4( new Vector3( a.x, 0f, a.y ) );
				Vector3 b1 = matrix.MultiplyPoint3x4( new Vector3( b.x, 0f, b.y ) );
				Vector3 t0 = matrix.MultiplyPoint3x4( new Vector3( aTop.x, height, aTop.y ) );
				Vector3 t1 = matrix.MultiplyPoint3x4( new Vector3( bTop.x, height, bTop.y ) );
				Gizmos.DrawLine( b0, b1 );
				Gizmos.DrawLine( t0, t1 );
				Gizmos.DrawLine( b0, t0 );
			}

			return;
		}

		Vector2 footprint = volume.ResolveFootprintWorld();
		float halfX = footprint.x * 0.5f;
		float halfZ = footprint.y * 0.5f;
		float topX = halfX + flare;
		float topZ = halfZ + flare;

		Vector3 c0 = matrix.MultiplyPoint3x4( new Vector3( -halfX, 0f, -halfZ ) );
		Vector3 c1 = matrix.MultiplyPoint3x4( new Vector3( halfX, 0f, -halfZ ) );
		Vector3 c2 = matrix.MultiplyPoint3x4( new Vector3( halfX, 0f, halfZ ) );
		Vector3 c3 = matrix.MultiplyPoint3x4( new Vector3( -halfX, 0f, halfZ ) );
		Vector3 u0 = matrix.MultiplyPoint3x4( new Vector3( -topX, height, -topZ ) );
		Vector3 u1 = matrix.MultiplyPoint3x4( new Vector3( topX, height, -topZ ) );
		Vector3 u2 = matrix.MultiplyPoint3x4( new Vector3( topX, height, topZ ) );
		Vector3 u3 = matrix.MultiplyPoint3x4( new Vector3( -topX, height, topZ ) );

		Gizmos.DrawLine( c0, c1 );
		Gizmos.DrawLine( c1, c2 );
		Gizmos.DrawLine( c2, c3 );
		Gizmos.DrawLine( c3, c0 );
		Gizmos.DrawLine( u0, u1 );
		Gizmos.DrawLine( u1, u2 );
		Gizmos.DrawLine( u2, u3 );
		Gizmos.DrawLine( u3, u0 );
		Gizmos.DrawLine( c0, u0 );
		Gizmos.DrawLine( c1, u1 );
		Gizmos.DrawLine( c2, u2 );
		Gizmos.DrawLine( c3, u3 );
	}

	static Vector2 ExpandFromCentroid( Vector2[] ring, Vector2 point, float outward )
	{
		if ( outward <= 0.0001f || ring == null || ring.Length == 0 )
			return point;

		Vector2 centroid = Vector2.zero;
		for ( int i = 0; i < ring.Length; i++ )
			centroid += ring[ i ];
		centroid /= ring.Length;

		Vector2 fromCenter = point - centroid;
		float len = fromCenter.magnitude;
		Vector2 dir = len > 0.0001f ? fromCenter / len : Vector2.right;
		return centroid + dir * ( len + outward );
	}
}
#endif
