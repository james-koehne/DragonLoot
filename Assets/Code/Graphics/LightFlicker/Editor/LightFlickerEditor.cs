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
	SerializedProperty _emissiveMode;
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
		_emissiveMode = serializedObject.FindProperty( "_emissiveMode" );
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
				&& (LightFlickerPresetKind)_preset.intValue == LightFlickerPresetKind.Custom );
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

		EditorGUILayout.Space( 4f );
		EditorGUILayout.LabelField( "Emissive Mesh", EditorStyles.boldLabel );
		EditorGUI.BeginChangeCheck();
		EditorGUILayout.PropertyField( _emissiveMode, new GUIContent( "Emissive Mode" ) );
		LightFlickerEmissiveMode mode = (LightFlickerEmissiveMode)_emissiveMode.intValue;
		if ( _emissiveMode.hasMultipleDifferentValues )
		{
			DrawExactEmissiveRendererList();
		}
		else if ( mode == LightFlickerEmissiveMode.None )
		{
			EditorGUILayout.HelpBox( "Light flickers only. No mesh emission is applied.", MessageType.None );
		}
		else if ( mode == LightFlickerEmissiveMode.Manual )
		{
			DrawExactEmissiveRendererList();
			EditorGUILayout.HelpBox( "Assign the glow object's MeshRenderer. Drag that child — not the parent — or Unity will pick the first mesh (usually the base).", MessageType.None );
		}
		else
		{
			DrawExactEmissiveRendererList();
			EditorGUILayout.HelpBox( "Empty: find nearby emissive meshes. Assigned: use only those renderers and skip the base light mesh.", MessageType.None );
		}

		if ( mode != LightFlickerEmissiveMode.None || _emissiveMode.hasMultipleDifferentValues )
			EditorGUILayout.PropertyField( _emissiveMaterialIndex, new GUIContent( "Emissive Material Index", "Slot with the emissive material. -1 picks the first emissive slot on each renderer." ) );

		if ( !_emissiveMode.hasMultipleDifferentValues && targets.Length == 1 )
			DrawResolvedEmissiveReadout( (LightFlicker)target );

		bool emissiveChanged = EditorGUI.EndChangeCheck();

		serializedObject.ApplyModifiedProperties();

		if ( presetChanged || previewToggleChanged || emissiveChanged )
		{
			ForEachTarget( flicker =>
			{
				if ( !Application.isPlaying && !flicker.PlayInEditMode )
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

	void DrawExactEmissiveRendererList()
	{
		EditorGUI.indentLevel++;
		int size = Mathf.Max( 0, _emissiveRenderers.arraySize );
		int newSize = EditorGUILayout.IntField( "Emissive Renderers", size );
		if ( newSize != size )
			_emissiveRenderers.arraySize = newSize;

		for ( int i = 0; i < _emissiveRenderers.arraySize; i++ )
		{
			SerializedProperty element = _emissiveRenderers.GetArrayElementAtIndex( i );
			MeshRenderer current = element.objectReferenceValue as MeshRenderer;
			GameObject currentObject = current != null ? current.gameObject : null;

			EditorGUI.BeginChangeCheck();
			GameObject picked = (GameObject)EditorGUILayout.ObjectField( "Element " + i, currentObject, typeof( GameObject ), true );
			if ( !EditorGUI.EndChangeCheck() )
				continue;

			if ( picked == null )
			{
				element.objectReferenceValue = null;
				continue;
			}

			MeshRenderer exact = picked.GetComponent<MeshRenderer>();
			element.objectReferenceValue = exact;
			if ( exact == null )
				Debug.LogWarning( "LightFlicker: '" + picked.name + "' has no MeshRenderer on that object. Assign the glow child, not a parent." );
		}

		EditorGUI.indentLevel--;
	}

	static void DrawResolvedEmissiveReadout( LightFlicker flicker )
	{
		if ( flicker == null )
			return;

		int count = flicker.ResolvedEmissiveRendererCount;
		if ( count <= 0 )
		{
			if ( flicker.EmissiveMode != LightFlickerEmissiveMode.None )
				EditorGUILayout.HelpBox( "No emissive renderer is active.", MessageType.Info );
			return;
		}

		System.Text.StringBuilder builder = new System.Text.StringBuilder();
		builder.Append( "Active emissive renderer" );
		if ( count != 1 )
			builder.Append( 's' );
		builder.Append( ": " );
		for ( int i = 0; i < count; i++ )
		{
			MeshRenderer renderer = flicker.GetResolvedEmissiveRenderer( i );
			if ( i > 0 )
				builder.Append( ", " );
			builder.Append( renderer != null ? renderer.name : "(missing)" );
		}

		EditorGUILayout.HelpBox( builder.ToString(), MessageType.None );
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
