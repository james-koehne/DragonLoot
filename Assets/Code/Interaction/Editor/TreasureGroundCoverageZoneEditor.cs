#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor( typeof( TreasureGroundCoverageZone ) )]
public class TreasureGroundCoverageZoneEditor : Editor
{
	TreasureGroundCoveragePlacement.Result _previewResult;
	int _previewFingerprint = int.MinValue;
	bool _previewValid;

	void OnEnable()
	{
		SceneView.duringSceneGui += DrawScenePreview;
		EditorApplication.update += EditorUpdate;
		Undo.undoRedoPerformed += InvalidatePreview;
		RebuildPreview( ( TreasureGroundCoverageZone )target, force: true );
	}

	void OnDisable()
	{
		SceneView.duringSceneGui -= DrawScenePreview;
		EditorApplication.update -= EditorUpdate;
		Undo.undoRedoPerformed -= InvalidatePreview;
	}

	void InvalidatePreview()
	{
		_previewFingerprint = int.MinValue;
		SceneView.RepaintAll();
		Repaint();
	}

	void EditorUpdate()
	{
		TreasureGroundCoverageZone zone = ( TreasureGroundCoverageZone )target;
		if ( zone == null )
			return;

		int fingerprint = ComputePreviewFingerprint( zone );
		if ( fingerprint == _previewFingerprint )
			return;

		RebuildPreview( zone, force: true );
		SceneView.RepaintAll();
		Repaint();
	}

	public override void OnInspectorGUI()
	{
		serializedObject.Update();
		EditorGUI.BeginChangeCheck();
		DrawDefaultInspector();
		bool inspectorChanged = EditorGUI.EndChangeCheck();
		serializedObject.ApplyModifiedProperties();

		TreasureGroundCoverageZone zone = ( TreasureGroundCoverageZone )target;
		EditorGUILayout.Space( 8f );
		EditorGUILayout.LabelField( "Scene Preview", EditorStyles.boldLabel );

		if ( zone.Group == null )
		{
			EditorGUILayout.HelpBox( "Assign a TreasureGroupDefinition to preview and spawn floor loot.", MessageType.Info );
		}

		if ( TreasureSurfaceAuthoring.Instance == null )
		{
			EditorGUILayout.HelpBox(
				"No TreasureSurfaceAuthoring in this scene. Paint traversable floor cells before previewing or spawning.",
				MessageType.Warning );
		}

		EditorGUILayout.HelpBox(
			"Layout is previewed in the Scene view only. Loot spawns at runtime as normal collectible treasure.",
			MessageType.None );

		bool preview = EditorGUILayout.Toggle( "Show Scene Preview", zone.ShowPreview );
		if ( preview != zone.ShowPreview )
		{
			Undo.RecordObject( zone, "Toggle Ground Coverage Preview" );
			zone.SetShowPreview( preview );
			EditorUtility.SetDirty( zone );
			SceneView.RepaintAll();
		}

		if ( inspectorChanged )
			InvalidatePreview();

		RebuildPreview( zone, force: false );
		if ( _previewValid )
		{
			int stackCount = _previewResult.coinStacks != null ? _previewResult.coinStacks.Length : 0;
			int propCount = _previewResult.props != null ? _previewResult.props.Length : 0;
			EditorGUILayout.LabelField( "Preview Counts", EditorStyles.miniBoldLabel );
			EditorGUILayout.LabelField(
				$"Coin stacks: {stackCount} ({_previewResult.placedCoins}/{_previewResult.requestedCoins} coins)    "
				+ $"Props: {propCount} ({_previewResult.placedProps}/{_previewResult.requestedProps})",
				EditorStyles.miniLabel );
		}
		else if ( !string.IsNullOrEmpty( _previewResult.error ) )
		{
			EditorGUILayout.HelpBox( _previewResult.error, MessageType.None );
		}
	}

	void DrawScenePreview( SceneView view )
	{
		TreasureGroundCoverageZone zone = ( TreasureGroundCoverageZone )target;
		if ( zone == null || !zone.ShowPreview )
			return;

		Transform t = zone.transform;
		if ( t.hasChanged )
		{
			t.hasChanged = false;
			InvalidatePreview();
		}

		RebuildPreview( zone, force: false );
		if ( !_previewValid )
			return;

		DrawPreviewGizmos( _previewResult );
	}

	void RebuildPreview( TreasureGroundCoverageZone zone, bool force )
	{
		if ( zone == null )
			return;

		int fingerprint = ComputePreviewFingerprint( zone );
		if ( !force && fingerprint == _previewFingerprint )
			return;

		_previewFingerprint = fingerprint;
		_previewResult = TreasureGroundCoveragePlacement.Compute( zone.BuildPlacementRequest() );
		_previewValid = _previewResult.success;
	}

	static int ComputePreviewFingerprint( TreasureGroundCoverageZone zone )
	{
		if ( zone == null )
			return 0;

		unchecked
		{
			int h = zone.ComputeSettingsFingerprint();
			Transform t = zone.transform;
			h = h * 31 + t.localPosition.GetHashCode();
			h = h * 31 + t.localRotation.GetHashCode();
			h = h * 31 + t.localScale.GetHashCode();

			TreasureSurfaceAuthoring surface = TreasureSurfaceAuthoring.Instance;
			if ( surface != null )
				h = h * 31 + surface.PaintRevision;

			return h;
		}
	}

	static void DrawPreviewGizmos( TreasureGroundCoveragePlacement.Result result )
	{
		if ( result.coinStacks != null )
		{
			Handles.color = new Color( 0.95f, 0.78f, 0.15f, 0.85f );
			for ( int i = 0; i < result.coinStacks.Length; i++ )
			{
				TreasureGroundCoveragePlacement.CoinStackPlacement stack = result.coinStacks[ i ];
				if ( stack.Count <= 0 )
					continue;
				float radius = GroundCoinStack.DefaultJoinRadius;
				TreasureDefinition primary = stack.PrimaryDefinition;
				if ( primary != null )
					radius = GroundCoinStack.ResolveJoinRadius( primary );
				float height = Mathf.Max( 0.05f, stack.Count * 0.012f );
				Vector3 basePos = stack.worldPosition;
				Vector3 mid = basePos + Vector3.up * ( height * 0.5f );
				Handles.DrawWireDisc( mid, Vector3.up, radius );
				Handles.DrawLine( basePos, basePos + Vector3.up * height );
			}
		}

		if ( result.props != null )
		{
			Handles.color = new Color( 0.35f, 0.85f, 0.95f, 0.85f );
			for ( int i = 0; i < result.props.Length; i++ )
			{
				TreasureGroundCoveragePlacement.PropPlacement prop = result.props[ i ];
				using ( new Handles.DrawingScope( Matrix4x4.TRS( prop.worldPosition, prop.rotation, Vector3.one ) ) )
					Handles.DrawWireCube( Vector3.up * 0.08f, new Vector3( 0.25f, 0.16f, 0.25f ) );
			}
		}
	}
}
#endif
