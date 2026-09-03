#if UNITY_EDITOR
using System.Collections.Generic;

using UnityEditor;
using UnityEngine;

[CanEditMultipleObjects]
[CustomEditor( typeof( LightFlicker ) )]
public class LightFlickerEditor : Editor
{
	SerializedProperty _definition;
	SerializedProperty _preset;
	SerializedProperty _customPresetName;
	SerializedProperty _intensityScale;
	SerializedProperty _speedScale;
	SerializedProperty _playInEditMode;
	SerializedProperty _light;
	SerializedProperty _emissiveRenderers;
	SerializedProperty _emissiveMaterialIndex;

	readonly Dictionary<int, int> _fingerprints = new Dictionary<int, int>();

	void OnEnable()
	{
		_definition = serializedObject.FindProperty( "_definition" );
		_preset = serializedObject.FindProperty( "_preset" );
		_customPresetName = serializedObject.FindProperty( "_customPresetName" );
		_intensityScale = serializedObject.FindProperty( "_intensityScale" );
		_speedScale = serializedObject.FindProperty( "_speedScale" );
		_playInEditMode = serializedObject.FindProperty( "_playInEditMode" );
		_light = serializedObject.FindProperty( "_light" );
		_emissiveRenderers = serializedObject.FindProperty( "_emissiveRenderers" );
		_emissiveMaterialIndex = serializedObject.FindProperty( "_emissiveMaterialIndex" );

		EditorApplication.update += EditorUpdate;
	}

	void OnDisable()
	{
		EditorApplication.update -= EditorUpdate;
		_fingerprints.Clear();
	}

	void EditorUpdate()
	{
		if ( Application.isPlaying )
			return;

		for ( int i = 0; i < targets.Length; i++ )
		{
			LightFlicker flicker = targets[ i ] as LightFlicker;
			if ( flicker == null || !flicker.PlayInEditMode || !flicker.isActiveAndEnabled )
				continue;

			int instanceId = flicker.GetInstanceID();
			int fingerprint = flicker.GetPresetFingerprint();
			if ( _fingerprints.TryGetValue( instanceId, out int lastFingerprint ) && lastFingerprint == fingerprint )
				continue;

			_fingerprints[ instanceId ] = fingerprint;
			flicker.RefreshEditorPreview();
		}
	}

	public override void OnInspectorGUI()
	{
		serializedObject.Update();

		EditorGUILayout.LabelField( "Preset", EditorStyles.boldLabel );
		EditorGUI.BeginChangeCheck();
		EditorGUILayout.PropertyField( _preset );
		bool showCustomPreset = _customPresetName.hasMultipleDifferentValues
			|| ( !_preset.hasMultipleDifferentValues
				&& (LightFlickerPresetKind)_preset.enumValueIndex == LightFlickerPresetKind.Custom );
		if ( showCustomPreset )
			EditorGUILayout.PropertyField( _customPresetName );
		bool presetChanged = EditorGUI.EndChangeCheck();

		EditorGUILayout.Space( 4f );
		EditorGUILayout.LabelField( "Tuning", EditorStyles.boldLabel );
		EditorGUILayout.PropertyField( _intensityScale );
		EditorGUILayout.PropertyField( _speedScale );

		EditorGUILayout.Space( 4f );
		EditorGUILayout.LabelField( "Preview", EditorStyles.boldLabel );
		EditorGUI.BeginChangeCheck();
		EditorGUILayout.PropertyField( _playInEditMode, new GUIContent( "Play In Edit Mode" ) );
		bool previewToggleChanged = EditorGUI.EndChangeCheck();

		EditorGUILayout.HelpBox( "Edit-mode flicker is visual only; Light values restore on save.", MessageType.None );

		EditorGUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Recapture Light Base" ) )
		{
			ForEachTarget( flicker => Undo.RecordObject( flicker, "Recapture Light Base" ) );
			ForEachTarget( flicker =>
			{
				flicker.RecaptureLightBase();
				_fingerprints[ flicker.GetInstanceID() ] = flicker.GetPresetFingerprint();
			} );
		}

		if ( GUILayout.Button( "Refresh Preview" ) )
		{
			ForEachTarget( flicker =>
			{
				flicker.RefreshEditorPreview();
				_fingerprints[ flicker.GetInstanceID() ] = flicker.GetPresetFingerprint();
			} );
		}

		if ( GUILayout.Button( "Open Definition" ) )
		{
			LightFlickerDefinition definition = AssetDatabase.LoadAssetAtPath<LightFlickerDefinition>( LightFlickerDefinition.DefaultAssetPath );
			if ( definition != null )
			{
				Selection.activeObject = definition;
				EditorGUIUtility.PingObject( definition );
			}
			else
				Debug.LogWarning( "LightFlickerDefinition not found at " + LightFlickerDefinition.DefaultAssetPath );
		}
		EditorGUILayout.EndHorizontal();

		DrawIntensityReadout();

		EditorGUILayout.Space( 4f );
		EditorGUILayout.LabelField( "References", EditorStyles.boldLabel );
		EditorGUILayout.PropertyField( _definition );
		EditorGUILayout.PropertyField( _light );
		EditorGUILayout.PropertyField( _emissiveRenderers, true );
		EditorGUILayout.PropertyField( _emissiveMaterialIndex );

		serializedObject.ApplyModifiedProperties();

		if ( presetChanged || previewToggleChanged )
		{
			ForEachTarget( flicker =>
			{
				if ( previewToggleChanged && !flicker.PlayInEditMode )
					flicker.RestoreAuthoredState();
				else
					flicker.RefreshEditorPreview();

				_fingerprints[ flicker.GetInstanceID() ] = flicker.GetPresetFingerprint();
			} );
		}
	}

	void ForEachTarget( System.Action<LightFlicker> action )
	{
		for ( int i = 0; i < targets.Length; i++ )
		{
			LightFlicker flicker = targets[ i ] as LightFlicker;
			if ( flicker != null )
				action( flicker );
		}
	}

	void DrawIntensityReadout()
	{
		if ( targets.Length > 1 )
		{
			EditorGUILayout.HelpBox(
				targets.Length + " LightFlickers selected. Select one to view authored intensity and preset range.",
				MessageType.Info );
			return;
		}

		DrawIntensityReadoutSingle( (LightFlicker)target );
	}

	static void DrawIntensityReadoutSingle( LightFlicker flicker )
	{
		if ( flicker == null || !flicker.HasAuthoredBase )
		{
			EditorGUILayout.HelpBox( "Recapture Light Base to show authored intensity and preset range.", MessageType.Info );
			return;
		}

		LightFlickerPreset preset = flicker.ActivePreset;
		if ( preset == null )
			return;

		float baseIntensity = flicker.AuthoredIntensity;
		float minMul = preset.intensityMin;
		float maxMul = preset.intensityMax;
		EditorGUILayout.LabelField(
			"Authored Intensity",
			baseIntensity.ToString( "0.###" ) );
		EditorGUILayout.LabelField(
			"Preset Intensity Range",
			( baseIntensity * minMul ).ToString( "0.###" ) + " – " + ( baseIntensity * maxMul ).ToString( "0.###" ) );
		EditorGUILayout.LabelField(
			"Authored Range",
			flicker.AuthoredRange.ToString( "0.###" ) );
	}
}
#endif
