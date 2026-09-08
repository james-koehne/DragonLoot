#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;

[CustomEditor( typeof( TreasureDefinition ) )]
public class TreasureDefinitionEditor : Editor
{
	const float PreviewHeight = 180f;

	Editor _previewEditor;
	Object _previewTarget;

	public override void OnInspectorGUI()
	{
		DrawPrefabPreview();
		EditorGUILayout.Space( 8f );
		DrawDefaultInspector();
	}

	void OnDisable()
	{
		DestroyPreviewEditor();
	}

	void DrawPrefabPreview()
	{
		GameObject prefab = LoadPrefab( ( TreasureDefinition )target );
		if ( prefab == null )
		{
			DestroyPreviewEditor();
			EditorGUILayout.HelpBox( "Assign an Addressable prefab to preview it here.", MessageType.Info );
			return;
		}

		if ( _previewTarget != prefab )
		{
			DestroyPreviewEditor();
			_previewTarget = prefab;
			_previewEditor = CreateEditor( prefab );
		}

		if ( _previewEditor == null )
			return;

		Rect rect = GUILayoutUtility.GetRect( PreviewHeight, PreviewHeight, GUILayout.ExpandWidth( true ) );
		if ( _previewEditor.HasPreviewGUI() )
			_previewEditor.OnInteractivePreviewGUI( rect, EditorStyles.helpBox );
		else
			_previewEditor.OnPreviewGUI( rect, EditorStyles.helpBox );
	}

	static GameObject LoadPrefab( TreasureDefinition definition )
	{
		if ( definition == null )
			return null;

		AssetReferenceGameObject prefabRef = definition.prefab;
		if ( prefabRef == null || !prefabRef.RuntimeKeyIsValid() )
			return null;

		string path = AssetDatabase.GUIDToAssetPath( prefabRef.AssetGUID );
		if ( string.IsNullOrEmpty( path ) )
			return null;

		return AssetDatabase.LoadAssetAtPath<GameObject>( path );
	}

	void DestroyPreviewEditor()
	{
		if ( _previewEditor != null )
			DestroyImmediate( _previewEditor );

		_previewEditor = null;
		_previewTarget = null;
	}
}
#endif
