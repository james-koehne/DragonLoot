#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Scene-view stroke handling for gold pile heightfield sculpt tools.
/// </summary>
public sealed class GoldPileSculptPainter
{
	const int RidgePointCap = 64;

	GoldPileEditorBrushMode _brushMode = GoldPileEditorBrushMode.Raise;
	bool _painting;
	bool _paintUndoRegistered;
	bool _hasBrushHit;
	bool _hasPrevBrushLocal;
	Vector3 _brushHit;
	Vector3 _brushNormal = Vector3.up;
	Vector2 _prevBrushLocal;
	readonly Vector2[] _ridgePoints = new Vector2[ RidgePointCap ];
	int _ridgePointCount;
	int _previewRevision = -1;
	int _dragSampleCount;

	public bool HasBrushHit => _hasBrushHit;
	public Vector3 BrushHit => _brushHit;
	public int PreviewRevision => _previewRevision;

	public void SetBrushMode( GoldPileEditorBrushMode mode )
	{
		_brushMode = mode;
		GoldPileSculptSettings.BrushMode = mode;
	}

	public void ResetStrokeState()
	{
		_painting = false;
		_paintUndoRegistered = false;
		_hasPrevBrushLocal = false;
		_ridgePointCount = 0;
	}

	public void EnsurePreview( TreasurePileVisual visual )
	{
		if ( visual == null || Application.isPlaying )
			return;

		if ( visual.AuthoredRevision != _previewRevision
			|| visual.Heightfield == null
			|| !visual.Heightfield.IsInitialized )
			RebuildPreview( visual );
	}

	public void RebuildPreview( TreasurePileVisual visual )
	{
		if ( visual == null || Application.isPlaying )
			return;

		visual.EnsureEditorPreview();
		_previewRevision = visual.AuthoredRevision;
		SceneView.RepaintAll();
	}

	public void SyncPreviewRevision( TreasurePileVisual visual )
	{
		if ( visual != null )
			_previewRevision = visual.AuthoredRevision;
	}

	public bool HandleShortcuts()
	{
		Event e = Event.current;
		if ( e.type != EventType.KeyDown )
			return false;

		bool used = false;
		if ( e.keyCode == KeyCode.LeftBracket )
		{
			GoldPileSculptSettings.BrushRadius = Mathf.Max(
				GoldPileSculptSettings.MinRadius,
				GoldPileSculptSettings.BrushRadius * 0.85f );
			used = true;
		}
		else if ( e.keyCode == KeyCode.RightBracket )
		{
			GoldPileSculptSettings.BrushRadius = Mathf.Min(
				GoldPileSculptSettings.MaxRadius,
				GoldPileSculptSettings.BrushRadius * 1.15f );
			used = true;
		}
		else if ( e.keyCode == KeyCode.Minus || e.keyCode == KeyCode.KeypadMinus )
		{
			GoldPileSculptSettings.BrushStrength = Mathf.Max(
				GoldPileSculptSettings.MinStrength,
				GoldPileSculptSettings.BrushStrength * 0.85f );
			used = true;
		}
		else if ( e.keyCode == KeyCode.Equals || e.keyCode == KeyCode.KeypadPlus || e.keyCode == KeyCode.Plus )
		{
			GoldPileSculptSettings.BrushStrength = Mathf.Min(
				GoldPileSculptSettings.MaxStrength,
				GoldPileSculptSettings.BrushStrength * 1.15f );
			used = true;
		}

		if ( used )
			e.Use();

		return used;
	}

	public void OnToolGUI( TreasurePileVisual visual )
	{
		if ( visual == null || Application.isPlaying )
			return;

		EnsurePreview( visual );
		HandleShortcuts();
		HandleBrushInput( visual );
	}

	public void ApplyOneShot( TreasurePileVisual visual, GoldPileEditorBrushMode mode, int iterations )
	{
		if ( visual == null || Application.isPlaying )
			return;

		if ( visual.Heightfield == null || !visual.Heightfield.IsInitialized )
			RebuildPreview( visual );

		GoldPileBrushParams p = GoldPileSculptSettings.BuildParams( iterations );
		Vector3 center = visual.transform.position;
		if ( _hasBrushHit )
			center = _brushHit;
		else if ( visual.Heightfield != null && visual.Heightfield.IsInitialized )
		{
			p.radius = Mathf.Max( p.radius, visual.Heightfield.WorldSize * 0.5f );
			center = visual.transform.TransformPoint(
				new Vector3( 0f, visual.Heightfield.MaxHeight * 0.5f, 0f ) );
		}

		visual.TryEditorBrushWorld( center, mode, p );
		visual.CommitEditorStroke();
		_previewRevision = visual.AuthoredRevision;
		EditorUtility.SetDirty( visual );
		SceneView.RepaintAll();
	}

	void HandleBrushInput( TreasurePileVisual visual )
	{
		if ( _brushMode == GoldPileEditorBrushMode.None || _brushMode == GoldPileEditorBrushMode.ResetMound )
			return;

		Event e = Event.current;
		int controlId = GUIUtility.GetControlID( FocusType.Passive );
		HandleUtility.AddDefaultControl( controlId );

		if ( e.type == EventType.MouseMove || e.type == EventType.MouseDrag )
			HandleUtility.Repaint();

		UpdateBrushHit( visual, e.mousePosition );

		GoldPileEditorBrushMode activeMode = e.control ? GoldPileEditorBrushMode.Smooth : _brushMode;
		float discRadius = activeMode == GoldPileEditorBrushMode.Ridge
			? GoldPileSculptSettings.RidgeWidth
			: GoldPileSculptSettings.BrushRadius;

		if ( e.type == EventType.Repaint && _hasBrushHit )
		{
			Handles.color = GoldPileSculptSettings.BrushColor( activeMode );
			Handles.DrawSolidDisc( _brushHit, _brushNormal, discRadius );
			Handles.color = Color.white;
			Handles.DrawWireDisc( _brushHit, _brushNormal, discRadius );
		}

		bool paintEvent = ( e.type == EventType.MouseDown || e.type == EventType.MouseDrag )
			&& e.button == 0
			&& !e.alt;

		if ( e.type == EventType.MouseDown && e.button == 0 && !e.alt )
		{
			_painting = true;
			_paintUndoRegistered = false;
			_hasPrevBrushLocal = false;
			_ridgePointCount = 0;
			_dragSampleCount = 0;
		}

		if ( e.type == EventType.MouseUp && e.button == 0 )
		{
			if ( _painting )
			{
				if ( _brushMode == GoldPileEditorBrushMode.Settle && _hasBrushHit )
				{
					GoldPileBrushParams finish = GoldPileSculptSettings.BuildParams(
						GoldPileSculptSettings.SettleItersStrokeEnd );
					finish.invert = e.shift;
					visual.TryEditorBrushWorld( _brushHit, GoldPileEditorBrushMode.Settle, finish );
				}
				else if ( _brushMode == GoldPileEditorBrushMode.Erode && _hasBrushHit )
				{
					GoldPileBrushParams finish = GoldPileSculptSettings.BuildParams(
						GoldPileSculptSettings.ErodeItersStrokeEnd );
					finish.invert = e.shift;
					visual.TryEditorBrushWorld( _brushHit, GoldPileEditorBrushMode.Erode, finish );
				}

				visual.CommitEditorStroke();
				_previewRevision = visual.AuthoredRevision;
				EditorUtility.SetDirty( visual );
			}

			_painting = false;
			_paintUndoRegistered = false;
			_hasPrevBrushLocal = false;
			_ridgePointCount = 0;
		}

		if ( paintEvent && _painting && _hasBrushHit )
		{
			bool skipDragStamp = activeMode == GoldPileEditorBrushMode.Stamp
				&& GoldPileSculptSettings.StampFullFootprint
				&& e.type == EventType.MouseDrag;
			bool skipDragPeak = activeMode == GoldPileEditorBrushMode.Peak && e.type == EventType.MouseDrag;
			if ( skipDragStamp || skipDragPeak )
				return;

			if ( !_paintUndoRegistered )
			{
				Undo.RecordObject( visual, "Sculpt Gold Pile" );
				_paintUndoRegistered = true;
			}

			Vector2 local = ToLocalXz( visual, _brushHit );
			GoldPileBrushParams p = GoldPileSculptSettings.BuildParamsForDrag( activeMode );
			p.invert = e.shift;

			if ( activeMode == GoldPileEditorBrushMode.Scrape || activeMode == GoldPileEditorBrushMode.Ridge )
			{
				if ( _hasPrevBrushLocal )
				{
					p.moveDeltaX = local.x - _prevBrushLocal.x;
					p.moveDeltaZ = local.y - _prevBrushLocal.y;
				}
				else
				{
					p.moveDeltaX = 0f;
					p.moveDeltaZ = 0f;
				}
			}

			if ( activeMode == GoldPileEditorBrushMode.Ridge )
			{
				TryAppendRidgePoint( local );
				if ( !_hasPrevBrushLocal )
				{
					_prevBrushLocal = local;
					_hasPrevBrushLocal = true;
					return;
				}
			}

			if ( activeMode == GoldPileEditorBrushMode.Scrape && !_hasPrevBrushLocal )
			{
				_prevBrushLocal = local;
				_hasPrevBrushLocal = true;
				return;
			}

			bool applied = visual.TryEditorBrushWorld( _brushHit, activeMode, p );
			_prevBrushLocal = local;
			_hasPrevBrushLocal = true;
			_dragSampleCount++;

			if ( applied )
			{
				int res = visual.Heightfield != null ? visual.Heightfield.Resolution : 64;
				int stride = res >= 192 ? 2 : 1;
				if ( e.type == EventType.MouseDown || ( _dragSampleCount % stride ) == 0 )
					visual.RefreshEditorPreviewFromHeightfield();
				e.Use();
			}
		}
	}

	void TryAppendRidgePoint( Vector2 local )
	{
		if ( _ridgePointCount == 0 )
		{
			_ridgePoints[ 0 ] = local;
			_ridgePointCount = 1;
			return;
		}

		Vector2 prev = _ridgePoints[ _ridgePointCount - 1 ];
		if ( ( local - prev ).sqrMagnitude < 0.01f )
			return;

		if ( _ridgePointCount < RidgePointCap )
		{
			_ridgePoints[ _ridgePointCount ] = local;
			_ridgePointCount++;
		}
		else
		{
			for ( int i = 1; i < RidgePointCap; i++ )
				_ridgePoints[ i - 1 ] = _ridgePoints[ i ];
			_ridgePoints[ RidgePointCap - 1 ] = local;
		}
	}

	static Vector2 ToLocalXz( TreasurePileVisual visual, Vector3 world )
	{
		Vector3 local = visual.transform.InverseTransformPoint( world );
		return new Vector2( local.x, local.z );
	}

	void UpdateBrushHit( TreasurePileVisual visual, Vector2 guiPoint )
	{
		Ray ray = HandleUtility.GUIPointToWorldRay( guiPoint );
		GoldPileHeightfield heightfield = visual.Heightfield;

		if ( heightfield != null && heightfield.IsInitialized
			&& heightfield.TryRaycast( ray, visual.transform, out Vector3 surfaceHit, out Vector3 surfaceNormal ) )
		{
			_brushHit = surfaceHit;
			_hasBrushHit = true;
			_brushNormal = surfaceNormal;
			return;
		}

		_brushNormal = visual.transform.up;
		Plane ground = new Plane( visual.transform.up, visual.transform.position );
		if ( !ground.Raycast( ray, out float enter ) )
		{
			_hasBrushHit = false;
			return;
		}

		Vector3 hit = ray.GetPoint( enter );
		Vector3 local = visual.transform.InverseTransformPoint( hit );
		float half = heightfield != null && heightfield.IsInitialized
			? heightfield.WorldSize * 0.5f
			: ( visual.AuthoredWorldSize > 0.1f ? visual.AuthoredWorldSize * 0.5f : 3f );

		if ( Mathf.Abs( local.x ) > half || Mathf.Abs( local.z ) > half )
		{
			_hasBrushHit = false;
			return;
		}

		if ( heightfield != null && heightfield.IsInitialized )
		{
			float h = heightfield.SampleNormalized( local.x, local.z ) * heightfield.MaxHeight;
			hit = visual.transform.TransformPoint( new Vector3( local.x, h, local.z ) );
		}

		_brushHit = hit;
		_hasBrushHit = true;
	}
}
#endif
