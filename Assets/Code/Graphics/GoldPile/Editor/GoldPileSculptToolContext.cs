#if UNITY_EDITOR
using System;
using System.Collections.Generic;

using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

/// <summary>
/// Component tool context for gold pile sculpt brushes.
/// Does not replace Move/Rotate/Scale — additional brush tools appear beside them.
/// </summary>
[EditorToolContext( "Gold Pile", typeof( TreasurePileVisual ) )]
public sealed class GoldPileSculptToolContext : EditorToolContext
{
	static GoldPileSculptPainter s_painter;
	static GoldPileSculptOverlay s_overlay;
	static bool s_active;

	public static bool IsActive => s_active;
	public static GoldPileSculptPainter Painter => s_painter ?? ( s_painter = new GoldPileSculptPainter() );

	public static void ActivateAndRestoreBrush()
	{
		ToolManager.SetActiveContext<GoldPileSculptToolContext>();
		Type toolType = ResolveToolType( GoldPileSculptSettings.BrushMode );
		if ( toolType != null )
			ToolManager.SetActiveTool( toolType );
	}

	public static Type ResolveToolType( GoldPileEditorBrushMode mode )
	{
		switch ( mode )
		{
			case GoldPileEditorBrushMode.Raise: return typeof( GoldPileRaiseBrushTool );
			case GoldPileEditorBrushMode.Lower: return typeof( GoldPileLowerBrushTool );
			case GoldPileEditorBrushMode.Smooth: return typeof( GoldPileSmoothBrushTool );
			case GoldPileEditorBrushMode.Flatten: return typeof( GoldPileFlattenBrushTool );
			case GoldPileEditorBrushMode.Inflate: return typeof( GoldPileInflateBrushTool );
			case GoldPileEditorBrushMode.Pinch: return typeof( GoldPilePinchBrushTool );
			case GoldPileEditorBrushMode.Scrape: return typeof( GoldPileScrapeBrushTool );
			case GoldPileEditorBrushMode.Flow: return typeof( GoldPileFlowBrushTool );
			case GoldPileEditorBrushMode.Peak: return typeof( GoldPilePeakBrushTool );
			case GoldPileEditorBrushMode.Ridge: return typeof( GoldPileRidgeBrushTool );
			case GoldPileEditorBrushMode.Stamp: return typeof( GoldPileStampBrushTool );
			case GoldPileEditorBrushMode.Settle: return typeof( GoldPileSettleBrushTool );
			case GoldPileEditorBrushMode.Erode: return typeof( GoldPileErodeBrushTool );
			case GoldPileEditorBrushMode.Fill: return typeof( GoldPileFillBrushTool );
			case GoldPileEditorBrushMode.Noise: return typeof( GoldPileNoiseBrushTool );
			default: return typeof( GoldPileRaiseBrushTool );
		}
	}

	public override IEnumerable<Type> GetAdditionalToolTypes()
	{
		yield return typeof( GoldPileRaiseBrushTool );
		yield return typeof( GoldPileLowerBrushTool );
		yield return typeof( GoldPileSmoothBrushTool );
		yield return typeof( GoldPileFlattenBrushTool );
		yield return typeof( GoldPileInflateBrushTool );
		yield return typeof( GoldPilePinchBrushTool );
		yield return typeof( GoldPileScrapeBrushTool );
		yield return typeof( GoldPileFlowBrushTool );
		yield return typeof( GoldPilePeakBrushTool );
		yield return typeof( GoldPileRidgeBrushTool );
		yield return typeof( GoldPileStampBrushTool );
		yield return typeof( GoldPileSettleBrushTool );
		yield return typeof( GoldPileErodeBrushTool );
		yield return typeof( GoldPileFillBrushTool );
		yield return typeof( GoldPileNoiseBrushTool );
	}

	public override void OnActivated()
	{
		s_active = true;
		if ( s_painter == null )
			s_painter = new GoldPileSculptPainter();

		TreasurePileVisual visual = ResolveVisual();
		if ( visual != null && !Application.isPlaying )
			s_painter.RebuildPreview( visual );

		if ( s_overlay == null )
			s_overlay = new GoldPileSculptOverlay();
		SceneView.AddOverlayToActiveView( s_overlay );
	}

	public override void OnWillBeDeactivated()
	{
		s_active = false;
		if ( s_painter != null )
			s_painter.ResetStrokeState();

		if ( s_overlay != null )
		{
			SceneView.RemoveOverlayFromActiveView( s_overlay );
			s_overlay = null;
		}
	}

	public static TreasurePileVisual ResolveVisual()
	{
		UnityEngine.Object active = Selection.activeObject;
		if ( active is TreasurePileVisual direct )
			return direct;

		GameObject go = Selection.activeGameObject;
		if ( go == null )
			return null;

		return go.GetComponent<TreasurePileVisual>();
	}
}
#endif
