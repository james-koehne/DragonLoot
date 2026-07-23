#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Lightweight utility window. Level bounds/cell paint lives on TreasureSurfaceAuthoring
/// (select that object — do not rely on this window for heavy overlays).
/// </summary>
public class TreasureSurfaceEditorWindow : EditorWindow
{
	enum PreviewMode
	{
		None,
		Bounds,
		Chunks
	}

	PreviewMode _previewMode = PreviewMode.None;
	bool _alwaysShowBounds;
	TreasureSurfaceDefinition _previewDefinition;

	[MenuItem( "DragonLoot/Treasure Surface Editor" )]
	public static void Open()
	{
		GetWindow<TreasureSurfaceEditorWindow>( "Treasure Surface" );
	}

	[MenuItem( "DragonLoot/Create Treasure Surface Definition" )]
	public static void CreateDefinitionAsset()
	{
		const string folder = "Assets/Definitions/TreasureSurface";
		if ( !AssetDatabase.IsValidFolder( "Assets/Definitions" ) )
			AssetDatabase.CreateFolder( "Assets", "Definitions" );
		if ( !AssetDatabase.IsValidFolder( folder ) )
			AssetDatabase.CreateFolder( "Assets/Definitions", "TreasureSurface" );

		string path = AssetDatabase.GenerateUniqueAssetPath( folder + "/TreasureSurfaceDefinition.asset" );
		TreasureSurfaceDefinition asset = ScriptableObject.CreateInstance<TreasureSurfaceDefinition>();
		asset.EnsureDefaults();
		AssetDatabase.CreateAsset( asset, path );
		AssetDatabase.SaveAssets();
		EditorUtility.FocusProjectWindow();
		Selection.activeObject = asset;
	}

	void OnEnable()
	{
		SceneView.duringSceneGui += OnSceneGUI;
		if ( _previewDefinition == null )
		{
			_previewDefinition = AssetDatabase.LoadAssetAtPath<TreasureSurfaceDefinition>(
				"Assets/Definitions/TreasureSurface/TreasureSurfaceDefinition.asset" );
		}
	}

	void OnDisable()
	{
		SceneView.duringSceneGui -= OnSceneGUI;
	}

	void OnGUI()
	{
		EditorGUILayout.LabelField( "Level Authoring", EditorStyles.boldLabel );
		TreasureSurfaceAuthoring authoring = TreasureSurfaceAuthoring.Instance;
		if ( authoring == null )
		{
			EditorGUILayout.HelpBox(
				"No TreasureSurfaceAuthoring in loaded scenes.\n"
				+ "DragonLoot → Create Treasure Surface Authoring In Active Scene",
				MessageType.Warning );
			if ( GUILayout.Button( "Create Authoring In Active Scene" ) )
				TreasureSurfaceAuthoringMenu.CreateInActiveScene();
		}
		else
		{
			EditorGUILayout.LabelField( $"Authoring: {authoring.gameObject.name}" );
			EditorGUILayout.LabelField(
				$"Size {authoring.WorldSizeX:0}×{authoring.WorldSizeZ:0}m  "
				+ $"Cell {authoring.CellSize:0.###}m" );
			if ( GUILayout.Button( "Select Authoring (paint / move bounds here)" ) )
			{
				Selection.activeGameObject = authoring.gameObject;
				EditorGUIUtility.PingObject( authoring.gameObject );
			}
		}

		EditorGUILayout.Space( 8f );
		_alwaysShowBounds = EditorGUILayout.Toggle( "Show definition bounds in Scene", _alwaysShowBounds );
		_previewMode = ( PreviewMode )EditorGUILayout.EnumPopup( "Scene Preview", _previewMode );
		_previewDefinition = ( TreasureSurfaceDefinition )EditorGUILayout.ObjectField(
			"Definition",
			_previewDefinition,
			typeof( TreasureSurfaceDefinition ),
			false );

		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		if ( world != null && world.IsInitialized )
		{
			EditorGUILayout.Space( 4f );
			EditorGUILayout.LabelField( $"Runtime loaded chunks: {world.LoadedChunkCount}" );
			EditorGUILayout.LabelField( $"Active: {world.ActiveChunkCount}  Dirty: {world.DirtyChunkCount}" );
		}

		EditorGUILayout.HelpBox(
			"Paint and bound editing: select TreasureSurfaceAuthoring.\n"
			+ "Traversable overlay: hidden textured mesh plane in the level scene when enabled on authoring.",
			MessageType.Info );
	}

	void OnSceneGUI( SceneView sceneView )
	{
		if ( Event.current.type != EventType.Repaint )
			return;

		if ( !_alwaysShowBounds && _previewMode == PreviewMode.None )
			return;

		TreasureSurfaceDefinition def = _previewDefinition;
		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		if ( world != null && world.Definition != null )
			def = world.Definition;

		TreasureSurfaceAuthoring authoring = TreasureSurfaceAuthoring.Instance;
		if ( authoring != null && Selection.activeGameObject == authoring.gameObject )
			return; // Authoring editor already draws handles/bounds.

		if ( def == null && authoring == null )
			return;

		if ( _alwaysShowBounds || _previewMode == PreviewMode.Bounds )
		{
			if ( authoring != null )
				DrawWireBounds( authoring.WorldBounds, authoring.BaseHeight );
			else if ( def != null )
				DrawWireBounds( def.WorldBounds, def.baseHeight );
		}

		if ( _previewMode == PreviewMode.Chunks && world != null && world.IsInitialized )
		{
			world.ForEachLoadedChunk( chunk =>
			{
				if ( chunk == null || !chunk.Loaded )
					return;

				Handles.color = chunk.Frozen ? new Color( 0.4f, 0.4f, 0.8f, 0.6f ) : Color.cyan;
				Bounds b = chunk.Bounds;
				Handles.DrawWireCube( b.center, new Vector3( b.size.x, 0.05f, b.size.z ) );
			} );
		}
	}

	static void DrawWireBounds( Bounds wb, float baseHeight )
	{
		Vector3 center = new Vector3( wb.center.x, baseHeight + 0.02f, wb.center.z );
		Vector3 size = new Vector3( wb.size.x, 0.04f, wb.size.z );
		Handles.color = new Color( 0.2f, 1f, 0.45f, 0.9f );
		Handles.DrawWireCube( center, size );
	}
}
#endif
