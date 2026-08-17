#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor( typeof( TreasurePileVisual ) )]
public class TreasurePileVisualEditor : Editor
{
	const int RidgePointCap = 64;
	const int SettleItersPerDrag = 4;
	const int ErodeItersPerDrag = 8;
	const int SettleItersStrokeEnd = 12;
	const int ErodeItersStrokeEnd = 24;

	GoldPileEditorBrushMode _brushMode = GoldPileEditorBrushMode.Raise;
	float _brushRadius = 1.5f;
	float _brushStrength = 0.08f;
	float _brushFalloff = 1f;
	float _flattenTarget = 0.5f;
	Texture2D _stampMask;
	bool _stampInvert;
	bool _stampFullFootprint;
	float _angleOfRepose = 35f;
	int _iterations = 8;
	float _noiseFrequency = 2f;
	int _noiseOctaves = 3;
	int _noiseSeed;
	float _peakHeight = 0.35f;
	bool _settleAfterPeak = true;
	float _ridgeWidth = 1.2f;
	float _profileFalloff = 0.85f;
	float _splineSmooth = 0.5f;

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

	void OnEnable()
	{
		Undo.undoRedoPerformed += OnUndoRedo;
		TreasurePileVisual visual = target as TreasurePileVisual;
		if ( visual != null && !Application.isPlaying )
		{
			RebuildPreview( visual );
			if ( visual.LatentBake != null && visual.LatentBake.PoseCount > 0 )
				RefreshLatentBakePreview( visual );
		}
	}

	void OnDisable()
	{
		Undo.undoRedoPerformed -= OnUndoRedo;
		_painting = false;
		_paintUndoRegistered = false;
		_hasPrevBrushLocal = false;
		_ridgePointCount = 0;
	}

	void OnUndoRedo()
	{
		TreasurePileVisual visual = target as TreasurePileVisual;
		if ( visual != null && !Application.isPlaying )
			RebuildPreview( visual );
	}

	public override void OnInspectorGUI()
	{
		serializedObject.Update();
		DrawDefaultInspector();

		TreasurePileVisual visual = ( TreasurePileVisual )target;
		EditorGUILayout.Space( 8f );
		EditorGUILayout.LabelField( "Artifact Latent Bake", EditorStyles.boldLabel );
		EditorGUILayout.HelpBox(
			"Bakes deterministic latent poses from this pile's authored height + layout seed. "
			+ "Curated props under _AuthoredLoot reserve occupancy; bake stores the auto-fill remainder. "
			+ "Each pile needs its own bake (heightmaps differ).",
			MessageType.None );

		int authoredCount = visual.CountAuthoredItems();
		if ( authoredCount > 0 )
			EditorGUILayout.LabelField( "Curated authored props", authoredCount.ToString() );

		if ( !Application.isPlaying && visual.IsLatentBakeStale() )
		{
			EditorGUILayout.HelpBox(
				"Authored items or mound changed — rebake to update fill.",
				MessageType.Warning );
		}

		if ( GUILayout.Button( "Bake Latents For This Pile" ) )
			BakeLatentsForVisual( visual );

		EditorGUILayout.Space( 8f );
		EditorGUILayout.LabelField( "Pile Sculpt (Edit Mode)", EditorStyles.boldLabel );

		if ( Application.isPlaying )
		{
			EditorGUILayout.HelpBox( "Sculpt tools are available in Edit Mode only.", MessageType.Info );
			serializedObject.ApplyModifiedProperties();
			return;
		}

		_brushMode = ( GoldPileEditorBrushMode )EditorGUILayout.EnumPopup( "Brush Mode", _brushMode );
		DrawModeControls();

		EditorGUILayout.HelpBox(
			"LMB sculpt in Scene view. [ ] radius, - = strength.\n"
			+ "Shift = invert (where applicable). Ctrl = temporary Smooth.\n"
			+ "Shape saves on stroke end for play-mode start.",
			MessageType.Info );

		EditorGUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Rebuild Preview" ) )
		{
			RebuildPreview( visual );
			EditorUtility.SetDirty( visual );
		}

		if ( GUILayout.Button( "Reset To Mound" ) )
		{
			Undo.RecordObject( visual, "Reset Gold Pile Mound" );
			visual.ResetAuthoredToMound();
			RebuildPreview( visual );
			EditorUtility.SetDirty( visual );
		}
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Apply Settle Pass" ) )
		{
			Undo.RecordObject( visual, "Settle Gold Pile" );
			ApplyOneShot( visual, GoldPileEditorBrushMode.Settle, SettleItersStrokeEnd );
		}

		if ( GUILayout.Button( "Clear Authored Height" ) )
		{
			Undo.RecordObject( visual, "Clear Authored Gold Pile Height" );
			visual.ClearAuthoredHeight();
			RebuildPreview( visual );
			EditorUtility.SetDirty( visual );
		}
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.LabelField(
			visual.HasAuthoredHeight
				? $"Authored: {visual.AuthoredResolution}²  rev {visual.AuthoredRevision}"
				: "Authored: (none — preview seeds a mound)" );

		serializedObject.ApplyModifiedProperties();
	}

	void DrawModeControls()
	{
		GoldPileEditorBrushMode mode = _brushMode;
		bool showRadius = mode != GoldPileEditorBrushMode.None && mode != GoldPileEditorBrushMode.ResetMound;
		bool showStrength = NeedsStrength( mode );
		bool showFalloff = NeedsFalloff( mode );

		if ( showRadius )
			_brushRadius = EditorGUILayout.Slider( "Radius (m)", _brushRadius, 0.1f, 32f );

		if ( mode == GoldPileEditorBrushMode.Ridge )
			_ridgeWidth = EditorGUILayout.Slider( "Width (m)", _ridgeWidth, 0.1f, 16f );

		if ( showStrength )
			_brushStrength = EditorGUILayout.Slider( "Strength", _brushStrength, 0.001f, 0.5f );

		if ( mode == GoldPileEditorBrushMode.Peak || mode == GoldPileEditorBrushMode.Ridge )
			_peakHeight = EditorGUILayout.Slider( "Height", _peakHeight, -1f, 1f );

		if ( showFalloff )
			_brushFalloff = EditorGUILayout.Slider( "Falloff", _brushFalloff, 0.2f, 4f );

		if ( mode == GoldPileEditorBrushMode.Peak )
		{
			_profileFalloff = EditorGUILayout.Slider( "Profile Falloff", _profileFalloff, 0.1f, 0.99f );
			_settleAfterPeak = EditorGUILayout.Toggle( "Settle After", _settleAfterPeak );
		}

		if ( mode == GoldPileEditorBrushMode.Ridge )
			_splineSmooth = EditorGUILayout.Slider( "Spline Smoothing", _splineSmooth, 0f, 1f );

		if ( mode == GoldPileEditorBrushMode.Flatten )
			_flattenTarget = EditorGUILayout.Slider( "Flatten Target", _flattenTarget, 0f, 1f );

		if ( mode == GoldPileEditorBrushMode.Settle )
		{
			_iterations = EditorGUILayout.IntSlider( "Iterations", _iterations, 4, 20 );
			_angleOfRepose = EditorGUILayout.Slider( "Angle Of Repose", _angleOfRepose, 5f, 60f );
		}

		if ( mode == GoldPileEditorBrushMode.Erode )
			_iterations = EditorGUILayout.IntSlider( "Iterations", _iterations, 10, 50 );

		if ( mode == GoldPileEditorBrushMode.Noise )
		{
			_noiseFrequency = EditorGUILayout.Slider( "Frequency", _noiseFrequency, 0.1f, 16f );
			_noiseOctaves = EditorGUILayout.IntSlider( "Octaves", _noiseOctaves, 1, 8 );
			_noiseSeed = EditorGUILayout.IntField( "Seed", _noiseSeed );
		}

		if ( mode == GoldPileEditorBrushMode.Stamp )
		{
			_stampMask = ( Texture2D )EditorGUILayout.ObjectField( "Stamp Mask", _stampMask, typeof( Texture2D ), false );
			_stampInvert = EditorGUILayout.Toggle( "Invert (Lower)", _stampInvert );
			_stampFullFootprint = EditorGUILayout.Toggle( "Full Footprint", _stampFullFootprint );
			EditorGUILayout.HelpBox(
				"Mask must be Read/Write enabled. Grayscale drives raise/lower weight.",
				MessageType.None );
		}
	}

	static bool NeedsStrength( GoldPileEditorBrushMode mode )
	{
		switch ( mode )
		{
			case GoldPileEditorBrushMode.Raise:
			case GoldPileEditorBrushMode.Lower:
			case GoldPileEditorBrushMode.Smooth:
			case GoldPileEditorBrushMode.Flatten:
			case GoldPileEditorBrushMode.Stamp:
			case GoldPileEditorBrushMode.Flow:
			case GoldPileEditorBrushMode.Inflate:
			case GoldPileEditorBrushMode.Scrape:
			case GoldPileEditorBrushMode.Pinch:
			case GoldPileEditorBrushMode.Noise:
			case GoldPileEditorBrushMode.Erode:
			case GoldPileEditorBrushMode.Fill:
				return true;
			default:
				return false;
		}
	}

	static bool NeedsFalloff( GoldPileEditorBrushMode mode )
	{
		switch ( mode )
		{
			case GoldPileEditorBrushMode.Raise:
			case GoldPileEditorBrushMode.Lower:
			case GoldPileEditorBrushMode.Smooth:
			case GoldPileEditorBrushMode.Flatten:
			case GoldPileEditorBrushMode.Stamp:
			case GoldPileEditorBrushMode.Flow:
			case GoldPileEditorBrushMode.Inflate:
			case GoldPileEditorBrushMode.Scrape:
			case GoldPileEditorBrushMode.Noise:
			case GoldPileEditorBrushMode.Fill:
			case GoldPileEditorBrushMode.Ridge:
				return true;
			default:
				return false;
		}
	}

	void OnSceneGUI()
	{
		TreasurePileVisual visual = target as TreasurePileVisual;
		if ( visual == null || Application.isPlaying )
			return;

		if ( visual.AuthoredRevision != _previewRevision || visual.Heightfield == null || !visual.Heightfield.IsInitialized )
			RebuildPreview( visual );

		HandleShortcuts();
		HandleBrushInput( visual );
	}

	void HandleShortcuts()
	{
		Event e = Event.current;
		if ( e.type != EventType.KeyDown )
			return;

		bool used = false;
		if ( e.keyCode == KeyCode.LeftBracket )
		{
			_brushRadius = Mathf.Max( 0.1f, _brushRadius * 0.85f );
			used = true;
		}
		else if ( e.keyCode == KeyCode.RightBracket )
		{
			_brushRadius = Mathf.Min( 32f, _brushRadius * 1.15f );
			used = true;
		}
		else if ( e.keyCode == KeyCode.Minus || e.keyCode == KeyCode.KeypadMinus )
		{
			_brushStrength = Mathf.Max( 0.001f, _brushStrength * 0.85f );
			used = true;
		}
		else if ( e.keyCode == KeyCode.Equals || e.keyCode == KeyCode.KeypadPlus || e.keyCode == KeyCode.Plus )
		{
			_brushStrength = Mathf.Min( 0.5f, _brushStrength * 1.15f );
			used = true;
		}

		if ( used )
		{
			e.Use();
			Repaint();
		}
	}

	void RebuildPreview( TreasurePileVisual visual )
	{
		visual.EnsureEditorPreview();
		_previewRevision = visual.AuthoredRevision;
		SceneView.RepaintAll();
	}

	static void BakeLatentsForVisual( TreasurePileVisual visual )
	{
		if ( visual == null )
			return;

		visual.EnsureEditorPreview();
		TreasurePileDefinition definition = visual.ResolveDefinitionForEditor();
		if ( definition == null )
		{
			EditorUtility.DisplayDialog(
				"Bake Latents",
				"No TreasurePileDefinition on this visual / interactable.",
				"OK" );
			return;
		}

		if ( visual.Heightfield == null || !visual.Heightfield.IsInitialized )
		{
			EditorUtility.DisplayDialog( "Bake Latents", "Heightfield is not initialized.", "OK" );
			return;
		}

		TreasurePileLatentBake bake = visual.LatentBake;
		if ( bake == null )
		{
			string scenePath = visual.gameObject.scene.path;
			string folder = "Assets";
			if ( !string.IsNullOrEmpty( scenePath ) )
			{
				string sceneDir = System.IO.Path.GetDirectoryName( scenePath );
				if ( !string.IsNullOrEmpty( sceneDir ) )
					folder = sceneDir.Replace( '\\', '/' );
			}

			string safeName = visual.name.Replace( '/', '_' ).Replace( '\\', '_' );
			string path = AssetDatabase.GenerateUniqueAssetPath(
				$"{folder}/{safeName}_LatentBake.asset" );
			bake = ScriptableObject.CreateInstance<TreasurePileLatentBake>();
			AssetDatabase.CreateAsset( bake, path );
			Undo.RecordObject( visual, "Assign Treasure Pile Latent Bake" );
			visual.SetLatentBake( bake );
			EditorUtility.SetDirty( visual );
		}

		GoldPileArtifactProps props = visual.ArtifactProps;
		if ( props == null )
		{
			props = visual.GetComponent<GoldPileArtifactProps>();
			if ( props == null )
				props = visual.gameObject.AddComponent<GoldPileArtifactProps>();
		}

		Undo.RecordObject( bake, "Bake Treasure Pile Latents" );
		props.BakeLatentsInto(
			visual,
			definition,
			visual.Heightfield,
			visual.transform,
			visual.LootInstances,
			visual.LootLayoutSeed,
			bake );
		EditorUtility.SetDirty( bake );
		AssetDatabase.SaveAssets();
		RefreshLatentBakePreview( visual );
		EditorUtility.DisplayDialog(
			"Bake Latents",
			$"Wrote {bake.PoseCount} remainder poses to {bake.name} for pile '{visual.name}' "
			+ $"(+ {visual.CountAuthoredItems()} curated authored).",
			"OK" );
	}

	static void RefreshLatentBakePreview( TreasurePileVisual visual )
	{
		if ( visual == null || Application.isPlaying )
			return;

		ClearLatentBakePreview( visual );
		TreasurePileLatentBake bake = visual.LatentBake;
		if ( bake == null || bake.poses == null || bake.poses.Length == 0 )
			return;

		Transform root = EnsureLatentBakePreviewRoot( visual );
		for ( int i = 0; i < bake.poses.Length; i++ )
		{
			TreasurePileLatentBake.Pose pose = bake.poses[ i ];
			if ( pose.definition == null )
				continue;

			Vector3 worldPos = visual.transform.TransformPoint( pose.localPos );
			Quaternion worldRot = visual.transform.rotation * pose.localRot;
			TreasureItem item = TreasureItemFactory.SpawnSync( pose.definition, worldPos, worldRot, root );
			if ( item == null )
				continue;

			ConfigureBakePreviewItem( item, pose.scale );
		}

		SceneView.RepaintAll();
	}

	static Transform EnsureLatentBakePreviewRoot( TreasurePileVisual visual )
	{
		Transform existing = visual.FindLatentBakePreviewRoot();
		if ( existing != null )
		{
			SetHideAndDontSaveRecursive( existing );
			return existing;
		}

		GameObject go = new GameObject( TreasurePileVisual.LatentBakePreviewRootName );
		go.transform.SetParent( visual.transform, false );
		go.transform.localPosition = Vector3.zero;
		go.transform.localRotation = Quaternion.identity;
		go.transform.localScale = Vector3.one;
		SetHideAndDontSaveRecursive( go.transform );
		return go.transform;
	}

	static void ClearLatentBakePreview( TreasurePileVisual visual )
	{
		Transform root = visual != null ? visual.FindLatentBakePreviewRoot() : null;
		if ( root == null )
			return;

		for ( int i = root.childCount - 1; i >= 0; i-- )
		{
			Transform child = root.GetChild( i );
			if ( child == null )
				continue;

			TreasureItem item = child.GetComponent<TreasureItem>();
			GameObject go = child.gameObject;
			if ( item != null )
			{
				bool viaAddressables = item.ReleasedViaAddressables;
				item.OnDespawned();
				if ( viaAddressables )
					UnityEngine.AddressableAssets.Addressables.ReleaseInstance( go );
				else
					Object.DestroyImmediate( go );
			}
			else
				Object.DestroyImmediate( go );
		}
	}

	static void ConfigureBakePreviewItem( TreasureItem item, float scale )
	{
		if ( item == null )
			return;

		GameObject go = item.gameObject;
		SetHideAndDontSaveRecursive( go.transform );

		Rigidbody body = item.Body;
		if ( body != null )
		{
			body.isKinematic = true;
			body.detectCollisions = false;
		}

		Collider[] cols = go.GetComponentsInChildren<Collider>( true );
		for ( int i = 0; i < cols.Length; i++ )
		{
			if ( cols[ i ] != null )
				cols[ i ].enabled = false;
		}

		Behaviour[] behaviours = go.GetComponentsInChildren<Behaviour>( true );
		for ( int i = 0; i < behaviours.Length; i++ )
		{
			Behaviour b = behaviours[ i ];
			if ( b == null || b is TreasureItem )
				continue;
			b.enabled = false;
		}

		if ( scale > 0.01f )
			go.transform.localScale = Vector3.one * scale;
	}

	static void SetHideAndDontSaveRecursive( Transform root )
	{
		if ( root == null )
			return;

		root.gameObject.hideFlags = HideFlags.HideAndDontSave;
		for ( int i = 0; i < root.childCount; i++ )
			SetHideAndDontSaveRecursive( root.GetChild( i ) );
	}

	void ApplyOneShot( TreasurePileVisual visual, GoldPileEditorBrushMode mode, int iterations )
	{
		if ( visual.Heightfield == null || !visual.Heightfield.IsInitialized )
			RebuildPreview( visual );

		GoldPileBrushParams p = BuildParams( iterations );
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
		float discRadius = activeMode == GoldPileEditorBrushMode.Ridge ? _ridgeWidth : _brushRadius;

		if ( e.type == EventType.Repaint && _hasBrushHit )
		{
			Handles.color = BrushColor( activeMode );
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
				// Finish Settle/Erode with a stronger pass for responsiveness during drag.
				if ( _brushMode == GoldPileEditorBrushMode.Settle && _hasBrushHit )
				{
					GoldPileBrushParams finish = BuildParams( SettleItersStrokeEnd );
					finish.invert = e.shift;
					visual.TryEditorBrushWorld( _brushHit, GoldPileEditorBrushMode.Settle, finish );
				}
				else if ( _brushMode == GoldPileEditorBrushMode.Erode && _hasBrushHit )
				{
					GoldPileBrushParams finish = BuildParams( ErodeItersStrokeEnd );
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
				&& _stampFullFootprint
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
			GoldPileBrushParams p = BuildParamsForDrag( activeMode );
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
				// Throttle preview uploads on large fields while dragging.
				int res = visual.Heightfield != null ? visual.Heightfield.Resolution : 64;
				int stride = res >= 192 ? 2 : 1;
				if ( e.type == EventType.MouseDown || ( _dragSampleCount % stride ) == 0 )
					visual.RefreshEditorPreviewFromHeightfield();
				e.Use();
			}
		}
	}

	GoldPileBrushParams BuildParams( int iterationsOverride )
	{
		GoldPileBrushParams p = GoldPileBrushParams.Default;
		p.radius = _brushRadius;
		p.strength = _brushStrength;
		p.falloff = _brushFalloff;
		p.flattenTarget = _flattenTarget;
		p.stampMask = _stampMask;
		p.stampInvert = _stampInvert;
		p.stampFullFootprint = _stampFullFootprint;
		p.angleOfReposeDegrees = _angleOfRepose;
		p.iterations = iterationsOverride > 0 ? iterationsOverride : _iterations;
		p.noiseFrequency = _noiseFrequency;
		p.noiseOctaves = _noiseOctaves;
		p.noiseSeed = _noiseSeed;
		p.peakHeight = _peakHeight;
		p.settleAfterPeak = _settleAfterPeak;
		p.ridgeWidth = _ridgeWidth;
		p.profileFalloff = _profileFalloff;
		p.splineSmooth = _splineSmooth;
		return p;
	}

	GoldPileBrushParams BuildParamsForDrag( GoldPileEditorBrushMode mode )
	{
		int iters = _iterations;
		if ( mode == GoldPileEditorBrushMode.Settle )
			iters = Mathf.Min( _iterations, SettleItersPerDrag );
		else if ( mode == GoldPileEditorBrushMode.Erode )
			iters = Mathf.Min( _iterations, ErodeItersPerDrag );
		return BuildParams( iters );
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

	static Color BrushColor( GoldPileEditorBrushMode mode )
	{
		switch ( mode )
		{
			case GoldPileEditorBrushMode.Raise:
				return new Color( 1f, 0.85f, 0.2f, 0.35f );
			case GoldPileEditorBrushMode.Lower:
				return new Color( 1f, 0.35f, 0.15f, 0.35f );
			case GoldPileEditorBrushMode.Smooth:
				return new Color( 0.35f, 0.7f, 1f, 0.35f );
			case GoldPileEditorBrushMode.Flatten:
				return new Color( 0.6f, 0.4f, 1f, 0.35f );
			case GoldPileEditorBrushMode.Stamp:
				return new Color( 0.2f, 1f, 0.55f, 0.35f );
			case GoldPileEditorBrushMode.Settle:
				return new Color( 0.9f, 0.6f, 0.2f, 0.35f );
			case GoldPileEditorBrushMode.Flow:
				return new Color( 0.2f, 0.75f, 1f, 0.35f );
			case GoldPileEditorBrushMode.Inflate:
				return new Color( 1f, 0.5f, 0.8f, 0.35f );
			case GoldPileEditorBrushMode.Scrape:
				return new Color( 0.85f, 0.75f, 0.4f, 0.35f );
			case GoldPileEditorBrushMode.Pinch:
				return new Color( 1f, 0.3f, 0.5f, 0.35f );
			case GoldPileEditorBrushMode.Noise:
				return new Color( 0.5f, 0.9f, 0.4f, 0.35f );
			case GoldPileEditorBrushMode.Erode:
				return new Color( 0.7f, 0.55f, 0.35f, 0.35f );
			case GoldPileEditorBrushMode.Fill:
				return new Color( 0.4f, 0.85f, 0.7f, 0.35f );
			case GoldPileEditorBrushMode.Peak:
				return new Color( 1f, 0.9f, 0.3f, 0.4f );
			case GoldPileEditorBrushMode.Ridge:
				return new Color( 0.95f, 0.7f, 0.25f, 0.4f );
			default:
				return new Color( 1f, 1f, 1f, 0.25f );
		}
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

		// Fallback: flat footprint plane (missed mound silhouette / empty pile).
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
