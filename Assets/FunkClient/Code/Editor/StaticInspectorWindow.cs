using UnityEngine;
using UnityEditor;
using System;

public class StaticInspectorWindow : EditorWindow
{
	[SerializeField] private UnityEngine.Object targetObject;
	[SerializeField] private string windowId; // Unique ID per window

	private Editor cachedEditor;
	private Vector2 scrollPosition;

	private const string EditorPrefsKeyPrefix = "StaticInspectorWindow_ObjectPath_";
	private string EditorPrefsKey => EditorPrefsKeyPrefix + windowId;

	// Open a new inspector window for a given object
	public static void OpenForObject( UnityEngine.Object obj )
	{
		if ( obj == null )
			return;

		var window = CreateInstance<StaticInspectorWindow>();
		window.windowId = Guid.NewGuid().ToString();
		window.SetTarget( obj );
		window.Show();
	}

	// Optional: open an empty inspector window
	[MenuItem( "Window/Static Inspector" )]
	public static void OpenEmpty()
	{
		var window = CreateInstance<StaticInspectorWindow>();
		window.windowId = Guid.NewGuid().ToString();
		window.titleContent = new GUIContent( "Static Inspector" );
		window.Show();
	}

	private void SetTarget( UnityEngine.Object obj )
	{
		targetObject = obj;

		// Set window title based on object name or default
		titleContent = new GUIContent( obj != null ? obj.name : "Static Inspector" );

		if ( AssetDatabase.Contains( obj ) )
		{
			string path = AssetDatabase.GetAssetPath( obj );
			EditorPrefs.SetString( EditorPrefsKey, path );
		}
		else
		{
			EditorPrefs.DeleteKey( EditorPrefsKey );
		}

		RecreateEditor();
	}

	private void OnEnable()
	{
		if ( string.IsNullOrEmpty( windowId ) )
		{
			windowId = Guid.NewGuid().ToString();
		}

		if ( targetObject == null && EditorPrefs.HasKey( EditorPrefsKey ) )
		{
			string path = EditorPrefs.GetString( EditorPrefsKey );
			targetObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>( path );
		}

		if ( targetObject != null )
		{
			titleContent = new GUIContent( targetObject.name );
			RecreateEditor();
		}
		else
		{
			titleContent = new GUIContent( "Static Inspector" );
		}
	}

	private void OnDisable()
	{
		if ( cachedEditor != null )
			DestroyImmediate( cachedEditor );
	}

	private void RecreateEditor()
	{
		if ( cachedEditor != null )
			DestroyImmediate( cachedEditor );

		if ( targetObject != null )
			cachedEditor = Editor.CreateEditor( targetObject );
	}

	private void OnGUI()
	{
		scrollPosition = EditorGUILayout.BeginScrollView( scrollPosition );

		EditorGUI.BeginChangeCheck();
		targetObject = EditorGUILayout.ObjectField( "Target Object", targetObject, typeof( UnityEngine.Object ), false );

		if ( EditorGUI.EndChangeCheck() )
		{
			SetTarget( targetObject );
		}

		if ( targetObject == null )
		{
			EditorGUILayout.LabelField( "No object assigned." );
			EditorGUILayout.EndScrollView();
			return;
		}

		if ( cachedEditor == null )
			RecreateEditor();

		if ( cachedEditor != null )
			cachedEditor.OnInspectorGUI();

		EditorGUILayout.EndScrollView();
	}
}
