#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor( typeof( TreasureSurfaceAuthoring ) )]
public class TreasureSurfaceAuthoringEditor : Editor
{
	enum PaintMode
	{
		None,
		Traversable,
		NonTraversable,
		Material,
		Height,
		BakeHeight
	}

	BoxBoundsHandle _boundsHandle;
	PaintMode _paintMode = PaintMode.Traversable;
	TreasureSurfaceMaterial _paintMaterial = TreasureSurfaceMaterial.Stone;
	float _brushRadius = 0.5f;
	float _paintHeight;
	bool _paintHeightFromHit = true;
	bool _showCellOverlay;
	bool _painting;
	bool _paintUndoRegistered;
	bool _hasBrushHit;
	Vector3 _brushHit;

	void OnEnable()
	{
		_boundsHandle = new BoxBoundsHandle();
		TreasureSurfaceAuthoring authoring = target as TreasureSurfaceAuthoring;
		if ( authoring != null )
			_paintHeight = authoring.BaseHeight;

		_showCellOverlay = TreasureSurfaceAuthoringOverlay.GetSessionVisible();
		if ( _showCellOverlay && authoring != null )
			TreasureSurfaceAuthoringOverlay.Show( authoring );
	}

	void OnDisable()
	{
		TreasureSurfaceAuthoringOverlay.SetDeferTextureRebuild( false );

		TreasureSurfaceAuthoring authoring = target as TreasureSurfaceAuthoring;
		if ( authoring != null && _painting )
			authoring.EndPaintNotifyBatch();

		// Do not Hide here — inspector rebuilds would destroy the overlay.
		// Visibility is owned by the toggle / SessionState.
	}

	public override void OnInspectorGUI()
	{
		serializedObject.Update();
		DrawDefaultInspector();

		TreasureSurfaceAuthoring authoring = ( TreasureSurfaceAuthoring )target;

		EditorGUILayout.Space( 8f );
		EditorGUILayout.LabelField( "Scene Authoring", EditorStyles.boldLabel );

		using ( new EditorGUI.DisabledScope( authoring.PaintAsset == null ) )
		{
			EditorGUILayout.ObjectField( "Paint Asset", authoring.PaintAsset, typeof( TreasureSurfacePaintAsset ), false );
		}

		if ( GUILayout.Button( "Migrate / Ensure Paint Asset" ) )
		{
			Undo.RecordObject( authoring, "Migrate Treasure Surface Paint" );
			authoring.EditorMigratePaintToAsset( saveAssets: true );
			EditorUtility.SetDirty( authoring );
			if ( authoring.PaintAsset != null )
				authoring.PaintAsset.MarkDirty();
		}

		_paintMode = ( PaintMode )EditorGUILayout.EnumPopup( "Paint Mode", _paintMode );
		_brushRadius = EditorGUILayout.Slider( "Brush Radius (m)", _brushRadius, authoring.CellSize * 0.5f, 8f );
		_paintMaterial = ( TreasureSurfaceMaterial )EditorGUILayout.EnumPopup( "Paint Material", _paintMaterial );

		using ( new EditorGUI.DisabledScope( _paintMode != PaintMode.Height ) )
		{
			_paintHeightFromHit = EditorGUILayout.Toggle( "Height From Cursor Hit", _paintHeightFromHit );
			_paintHeight = EditorGUILayout.FloatField( "Absolute Paint Height", _paintHeight );
		}

		EditorGUI.BeginChangeCheck();
		_showCellOverlay = EditorGUILayout.Toggle( "Show Height Overlay", _showCellOverlay );
		if ( EditorGUI.EndChangeCheck() )
			TreasureSurfaceAuthoringOverlay.SetVisible( authoring, _showCellOverlay );

		if ( _showCellOverlay )
		{
			TreasureSurfaceAuthoringOverlay.GetBuildStatus(
				out int travChunks,
				out int builtChunks,
				out int pendingChunks,
				out int paintTravCells );
			EditorGUILayout.HelpBox(
				$"Overlay: paintTrav={paintTravCells}  travChunks={travChunks}  built={builtChunks}  pending={pendingChunks}",
				MessageType.None );
		}

		EditorGUILayout.HelpBox(
			"Drag the green box handles to move/resize bounds.\n"
			+ "Paint with LMB in the Scene view (snaps to cells).\n"
			+ "Height mode sets absolute world Y. Bake Height mode raycasts within the brush.\n"
			+ "Bake Heights button raycasts all (traversable) cells.\n"
			+ "Overlay is a HideAndDontSave transparent green height mesh.",
			MessageType.Info );

		EditorGUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Fill Traversable" ) )
		{
			RecordPaintUndo( authoring, "Fill Traversable" );
			authoring.FillAll( true, _paintMaterial );
			MarkPaintDirty( authoring );
			TreasureSurfaceAuthoringOverlay.Invalidate();
			if ( _showCellOverlay )
				TreasureSurfaceAuthoringOverlay.Show( authoring );
		}

		if ( GUILayout.Button( "Fill Blocked" ) )
		{
			RecordPaintUndo( authoring, "Fill Blocked" );
			authoring.FillAll( false, _paintMaterial );
			MarkPaintDirty( authoring );
			TreasureSurfaceAuthoringOverlay.Invalidate();
			if ( _showCellOverlay )
				TreasureSurfaceAuthoringOverlay.Show( authoring );
		}
		EditorGUILayout.EndHorizontal();

		if ( GUILayout.Button( "Fill All Heights (Paint Height)" ) )
		{
			RecordPaintUndo( authoring, "Fill Treasure Surface Heights" );
			authoring.FillAllHeights( _paintHeight );
			MarkPaintDirty( authoring );
			TreasureSurfaceAuthoringOverlay.Invalidate();
			if ( _showCellOverlay )
				TreasureSurfaceAuthoringOverlay.Show( authoring );
		}

		if ( GUILayout.Button( "Bake Heights From Raycasts" ) )
		{
			RecordPaintUndo( authoring, "Bake Treasure Surface Heights" );
			authoring.ResolveBakeRay( out float startY, out float distance );
			int hits = authoring.BakeHeightsFromRaycasts();
			MarkPaintDirty( authoring );
			TreasureSurfaceAuthoringOverlay.Invalidate();
			if ( _showCellOverlay )
				TreasureSurfaceAuthoringOverlay.Show( authoring );
			Debug.Log(
				"TreasureSurfaceAuthoring: baked heights for " + hits + " cell(s). "
				+ "startY=" + startY.ToString( "0.###" )
				+ " dist=" + distance.ToString( "0.###" )
				+ " mask=" + authoring.HeightBakeMask.value );
		}

		EditorGUILayout.HelpBox(
			"Height Overlay builds full-resolution chunk meshes across editor frames (Overlay Chunks Per Frame on the component).",
			MessageType.None );

		if ( GUILayout.Button( "Pull Layout From Definition" ) )
		{
			if ( authoring.Definition != null )
			{
				Undo.RecordObject( authoring, "Pull Layout From Definition" );
				authoring.SetDefinition( authoring.Definition );
				EditorUtility.SetDirty( authoring );
				TreasureSurfaceAuthoringOverlay.Invalidate();
				if ( _showCellOverlay )
					TreasureSurfaceAuthoringOverlay.Show( authoring );
			}
		}

		if ( GUILayout.Button( "Push Layout To Definition Asset" ) )
		{
			if ( authoring.Definition != null )
			{
				Undo.RecordObject( authoring.Definition, "Push Layout To Definition" );
				authoring.ApplyLayoutToDefinition( authoring.Definition );
				EditorUtility.SetDirty( authoring.Definition );
				AssetDatabase.SaveAssets();
			}
		}

		serializedObject.ApplyModifiedProperties();
	}

	void OnSceneGUI()
	{
		TreasureSurfaceAuthoring authoring = target as TreasureSurfaceAuthoring;
		if ( authoring == null )
			return;

		DrawBoundsHandles( authoring );

		Event e = Event.current;
		if ( _showCellOverlay )
		{
			if ( e.type == EventType.Repaint )
				TreasureSurfaceAuthoringOverlay.MaintainVisible( authoring );
		}

		HandlePaintInput( authoring );
	}

	void DrawBoundsHandles( TreasureSurfaceAuthoring authoring )
	{
		Bounds bounds = authoring.WorldBounds;
		_boundsHandle.center = bounds.center;
		_boundsHandle.size = new Vector3( bounds.size.x, 0.05f, bounds.size.z );
		_boundsHandle.axes = PrimitiveBoundsHandle.Axes.X | PrimitiveBoundsHandle.Axes.Z;
		_boundsHandle.wireframeColor = new Color( 0.2f, 1f, 0.45f, 0.95f );
		_boundsHandle.handleColor = new Color( 0.2f, 1f, 0.45f, 1f );

		EditorGUI.BeginChangeCheck();
		_boundsHandle.DrawHandle();
		if ( EditorGUI.EndChangeCheck() )
		{
			Undo.RecordObject( authoring, "Move Treasure Surface Bounds" );
			Vector3 center = _boundsHandle.center;
			Vector3 size = _boundsHandle.size;
			authoring.SetLayout(
				new Vector3( center.x, authoring.BaseHeight, center.z ),
				Mathf.Max( 8f, size.x ),
				Mathf.Max( 8f, size.z ),
				authoring.ChunkSize,
				authoring.CellsPerChunk,
				authoring.BaseHeight );
			EditorUtility.SetDirty( authoring );
			TreasureSurfaceAuthoringOverlay.Invalidate();
		}

		EditorGUI.BeginChangeCheck();
		Vector3 newOrigin = Handles.PositionHandle(
			new Vector3( authoring.WorldOrigin.x, authoring.BaseHeight, authoring.WorldOrigin.z ),
			Quaternion.identity );
		if ( EditorGUI.EndChangeCheck() )
		{
			Undo.RecordObject( authoring, "Move Treasure Surface Origin" );
			authoring.SetLayout(
				new Vector3( newOrigin.x, authoring.BaseHeight, newOrigin.z ),
				authoring.WorldSizeX,
				authoring.WorldSizeZ,
				authoring.ChunkSize,
				authoring.CellsPerChunk,
				authoring.BaseHeight );
			EditorUtility.SetDirty( authoring );
			TreasureSurfaceAuthoringOverlay.Invalidate();
		}

		Handles.Label(
			new Vector3( authoring.WorldOrigin.x, authoring.BaseHeight + 0.5f, authoring.WorldOrigin.z ),
			$"Treasure Surface  {authoring.WorldSizeX:0.#}×{authoring.WorldSizeZ:0.#}m  "
			+ $"cell {authoring.CellSize:0.###}m" );
	}

	void HandlePaintInput( TreasureSurfaceAuthoring authoring )
	{
		if ( _paintMode == PaintMode.None )
			return;

		Event e = Event.current;
		int controlId = GUIUtility.GetControlID( FocusType.Passive );
		HandleUtility.AddDefaultControl( controlId );

		if ( e.type == EventType.MouseMove || e.type == EventType.MouseDrag )
			HandleUtility.Repaint();

		UpdateBrushHit( authoring, e.mousePosition );

		if ( e.type == EventType.Repaint && _hasBrushHit )
		{
			Handles.color = _paintMode == PaintMode.NonTraversable
				? new Color( 1f, 0.2f, 0.2f, 0.4f )
				: _paintMode == PaintMode.Height || _paintMode == PaintMode.BakeHeight
					? new Color( 0.35f, 0.85f, 1f, 0.4f )
					: new Color( 0.2f, 1f, 0.35f, 0.4f );
			Handles.DrawSolidDisc( _brushHit, Vector3.up, _brushRadius );
			Handles.color = Color.white;
			Handles.DrawWireDisc( _brushHit, Vector3.up, _brushRadius );
		}

		bool paintEvent = ( e.type == EventType.MouseDown || e.type == EventType.MouseDrag )
			&& e.button == 0
			&& !e.alt;

		if ( e.type == EventType.MouseDown && e.button == 0 && !e.alt )
		{
			_painting = true;
			_paintUndoRegistered = false;
			authoring.BeginPaintNotifyBatch();
			TreasureSurfaceAuthoringOverlay.SetDeferTextureRebuild( true );
		}

		if ( e.type == EventType.MouseUp && e.button == 0 )
		{
			if ( _painting )
			{
				authoring.EndPaintNotifyBatch();
				TreasureSurfaceAuthoringOverlay.SetDeferTextureRebuild( false );
				TreasureSurfaceAuthoringOverlay.Invalidate();
				if ( _showCellOverlay )
					TreasureSurfaceAuthoringOverlay.Show( authoring );

				EditorUtility.SetDirty( authoring );
				if ( authoring.PaintAsset != null )
					authoring.PaintAsset.MarkDirty();
			}

			_painting = false;
			_paintUndoRegistered = false;
		}

		if ( paintEvent && _painting && _hasBrushHit )
		{
			if ( !_paintUndoRegistered )
			{
				RecordPaintUndo( authoring, "Paint Treasure Surface" );
				_paintUndoRegistered = true;
			}

			if ( _paintMode == PaintMode.BakeHeight )
			{
				authoring.PaintBrushBakeHeights( _brushHit, _brushRadius );
			}
			else if ( _paintMode == PaintMode.Height )
			{
				float h = _paintHeightFromHit ? _brushHit.y : _paintHeight;
				authoring.PaintBrush(
					_brushHit,
					_brushRadius,
					true,
					_paintMaterial,
					paintTraversable: false,
					paintMaterial: false,
					paintHeight: true,
					height: h );
			}
			else
			{
				bool trav = _paintMode != PaintMode.NonTraversable;
				bool paintTrav = _paintMode == PaintMode.Traversable || _paintMode == PaintMode.NonTraversable;
				bool paintMat = _paintMode == PaintMode.Material || _paintMode == PaintMode.Traversable;
				if ( e.shift )
				{
					trav = false;
					paintTrav = true;
				}

				authoring.PaintBrush( _brushHit, _brushRadius, trav, _paintMaterial, paintTrav, paintMat );
			}

			e.Use();
		}
	}

	void UpdateBrushHit( TreasureSurfaceAuthoring authoring, Vector2 guiPoint )
	{
		Ray ray = HandleUtility.GUIPointToWorldRay( guiPoint );

		// Prefer walkable physics hit so height paint samples floors, ramps, and stairs — not walls.
		if ( authoring.TryRaycastWalkableSurface( ray, 5000f, out RaycastHit hit ) )
		{
			Vector3 p = hit.point;
			if ( authoring.TryWorldToCell( p, out int cx, out int cz ) )
			{
				Vector3 cell = authoring.CellCenterWorld( cx, cz );
				_brushHit = new Vector3( cell.x, p.y, cell.z );
			}
			else
				_brushHit = p;
			_hasBrushHit = true;
			return;
		}

		Plane ground = new Plane( Vector3.up, new Vector3( 0f, authoring.BaseHeight, 0f ) );
		if ( !ground.Raycast( ray, out float enter ) )
		{
			_hasBrushHit = false;
			return;
		}

		Vector3 planeHit = ray.GetPoint( enter );
		if ( authoring.TryWorldToCell( planeHit, out int cellX, out int cellZ ) )
			planeHit = authoring.CellCenterWorld( cellX, cellZ );

		_brushHit = planeHit;
		_hasBrushHit = true;
	}

	static void RecordPaintUndo( TreasureSurfaceAuthoring authoring, string undoName )
	{
		if ( authoring.PaintAsset != null )
			authoring.PaintAsset.EditorPushUndo( undoName );
		Undo.RecordObject( authoring, undoName );
	}

	static void MarkPaintDirty( TreasureSurfaceAuthoring authoring )
	{
		EditorUtility.SetDirty( authoring );
		if ( authoring.PaintAsset != null )
			authoring.PaintAsset.MarkDirty();
	}
}

public static class TreasureSurfaceAuthoringMenu
{
	[MenuItem( DragonLootMenus.TreasureSurfaceCreateAuthoring )]
	public static void CreateInActiveScene()
	{
		if ( TreasureSurfaceAuthoring.Instance != null )
		{
			Selection.activeGameObject = TreasureSurfaceAuthoring.Instance.gameObject;
			EditorGUIUtility.PingObject( TreasureSurfaceAuthoring.Instance.gameObject );
			return;
		}

		GameObject go = new GameObject( "TreasureSurfaceAuthoring" );
		TreasureSurfaceAuthoring authoring = go.AddComponent<TreasureSurfaceAuthoring>();
		TreasureSurfaceDefinition def = AssetDatabase.LoadAssetAtPath<TreasureSurfaceDefinition>(
			"Assets/Definitions/TreasureSurface/TreasureSurfaceDefinition.asset" );
		if ( def != null )
			authoring.SetDefinition( def );

		authoring.EditorMigratePaintToAsset( saveAssets: true );

		Undo.RegisterCreatedObjectUndo( go, "Create Treasure Surface Authoring" );
		Selection.activeGameObject = go;
	}

	[MenuItem( DragonLootMenus.TreasureSurfaceMigratePaint, priority = 22 )]
	public static void MigratePaintFromOpenScenes()
	{
		int migrated = 0;
		for ( int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++ )
		{
			UnityEngine.SceneManagement.Scene scene =
				UnityEngine.SceneManagement.SceneManager.GetSceneAt( s );
			if ( !scene.IsValid() || !scene.isLoaded )
				continue;

			GameObject[] roots = scene.GetRootGameObjects();
			for ( int r = 0; r < roots.Length; r++ )
			{
				TreasureSurfaceAuthoring[] authorings =
					roots[ r ].GetComponentsInChildren<TreasureSurfaceAuthoring>( true );
				for ( int i = 0; i < authorings.Length; i++ )
				{
					TreasureSurfaceAuthoring authoring = authorings[ i ];
					if ( authoring == null )
						continue;

					Undo.RecordObject( authoring, "Migrate Treasure Surface Paint" );
					if ( authoring.EditorMigratePaintToAsset( saveAssets: false ) )
					{
						EditorUtility.SetDirty( authoring );
						if ( authoring.PaintAsset != null )
							authoring.PaintAsset.MarkDirty();
						EditorSceneManager.MarkSceneDirty( authoring.gameObject.scene );
						migrated++;
					}
				}
			}
		}

		AssetDatabase.SaveAssets();
		Debug.Log( "TreasureSurfaceAuthoring: migrated paint for " + migrated + " authoring object(s)." );
	}
}
#endif
