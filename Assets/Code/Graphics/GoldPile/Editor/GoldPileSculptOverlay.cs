#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;

/// <summary>
/// Scene overlay for gold pile sculpt brush settings. Visible only while the Gold Pile tool context is active.
/// </summary>
[Overlay( defaultDisplay = true )]
sealed class GoldPileSculptOverlay : IMGUIOverlay, ITransientOverlay
{
	public bool visible => GoldPileSculptToolContext.IsActive && !Application.isPlaying;

	public GoldPileSculptOverlay()
	{
		displayName = "Gold Pile Sculpt";
	}

	public override void OnGUI()
	{
		if ( Application.isPlaying )
		{
			EditorGUILayout.HelpBox( "Sculpt tools are available in Edit Mode only.", MessageType.Info );
			return;
		}

		TreasurePileVisual visual = GoldPileSculptToolContext.ResolveVisual();
		GoldPileEditorBrushMode mode = GoldPileSculptSettings.BrushMode;

		EditorGUILayout.LabelField( "Brush", EditorStyles.boldLabel );
		EditorGUILayout.LabelField( "Mode", mode.ToString() );
		GoldPileSculptSettings.DrawModeControls( mode );

		EditorGUILayout.Space( 4f );
		EditorGUILayout.HelpBox(
			"LMB sculpt in Scene view. [ ] radius, - = strength.\n"
			+ "Shift = invert (where applicable). Ctrl = temporary Smooth.\n"
			+ "Shape saves on stroke end for play-mode start.\n"
			+ "Pick Move/Rotate in this context to place the pile without painting.",
			MessageType.Info );

		using ( new EditorGUI.DisabledScope( visual == null ) )
		{
			EditorGUILayout.BeginHorizontal();
			if ( GUILayout.Button( "Rebuild Preview" ) )
			{
				GoldPileSculptToolContext.Painter.RebuildPreview( visual );
				EditorUtility.SetDirty( visual );
			}

			if ( GUILayout.Button( "Reset To Mound" ) )
			{
				Undo.RecordObject( visual, "Reset Gold Pile Mound" );
				visual.ResetAuthoredToMound();
				GoldPileSculptToolContext.Painter.RebuildPreview( visual );
				EditorUtility.SetDirty( visual );
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginHorizontal();
			if ( GUILayout.Button( "Apply Settle Pass" ) )
			{
				Undo.RecordObject( visual, "Settle Gold Pile" );
				GoldPileSculptToolContext.Painter.ApplyOneShot(
					visual,
					GoldPileEditorBrushMode.Settle,
					GoldPileSculptSettings.SettleItersStrokeEnd );
			}

			if ( GUILayout.Button( "Clear Authored Height" ) )
			{
				Undo.RecordObject( visual, "Clear Authored Gold Pile Height" );
				visual.ClearAuthoredHeight();
				GoldPileSculptToolContext.Painter.RebuildPreview( visual );
				EditorUtility.SetDirty( visual );
			}
			EditorGUILayout.EndHorizontal();

			if ( visual != null )
			{
				EditorGUILayout.LabelField(
					visual.HasAuthoredHeight
						? $"Authored: {visual.AuthoredResolution}²  rev {visual.AuthoredRevision}"
						: "Authored: (none — preview seeds a mound)" );
			}
		}
	}
}
#endif
