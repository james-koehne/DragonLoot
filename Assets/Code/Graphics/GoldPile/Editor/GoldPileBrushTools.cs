#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

/// <summary>Shared base for gold pile sculpt EditorTools.</summary>
public abstract class GoldPileBrushTool : EditorTool
{
	protected abstract GoldPileEditorBrushMode Mode { get; }

	protected abstract string IconName { get; }

	GUIContent _toolbarIcon;

	public override GUIContent toolbarIcon
	{
		get
		{
			if ( _toolbarIcon == null )
			{
				GUIContent builtIn = EditorGUIUtility.IconContent( IconName );
				_toolbarIcon = new GUIContent(
					builtIn != null && builtIn.image != null ? builtIn.image : null,
					Mode.ToString() );
				if ( _toolbarIcon.image == null )
					_toolbarIcon.text = Mode.ToString().Substring( 0, 1 );
			}

			return _toolbarIcon;
		}
	}

	public override void OnActivated()
	{
		GoldPileSculptToolContext.Painter.SetBrushMode( Mode );
		GoldPileSculptToolContext.Painter.ResetStrokeState();
	}

	public override void OnWillBeDeactivated()
	{
		GoldPileSculptToolContext.Painter.ResetStrokeState();
	}

	public override void OnToolGUI( EditorWindow window )
	{
		if ( Application.isPlaying )
			return;

		TreasurePileVisual visual = target as TreasurePileVisual;
		if ( visual == null )
			visual = GoldPileSculptToolContext.ResolveVisual();
		if ( visual == null )
			return;

		GoldPileSculptToolContext.Painter.SetBrushMode( Mode );
		GoldPileSculptToolContext.Painter.OnToolGUI( visual );
	}
}

// --- Height variant group ---

[EditorTool( "Raise", typeof( TreasurePileVisual ), typeof( GoldPileSculptToolContext ), 10, typeof( GoldPileHeightBrushVariant ), 0 )]
public sealed class GoldPileRaiseBrushTool : GoldPileBrushTool
{
	protected override GoldPileEditorBrushMode Mode => GoldPileEditorBrushMode.Raise;
	protected override string IconName => "TerrainInspector.TerrainToolRaise";
}

[EditorTool( "Lower", typeof( TreasurePileVisual ), typeof( GoldPileSculptToolContext ), 10, typeof( GoldPileHeightBrushVariant ), 1 )]
public sealed class GoldPileLowerBrushTool : GoldPileBrushTool
{
	protected override GoldPileEditorBrushMode Mode => GoldPileEditorBrushMode.Lower;
	protected override string IconName => "TerrainInspector.TerrainToolSetHeight";
}

[EditorTool( "Smooth", typeof( TreasurePileVisual ), typeof( GoldPileSculptToolContext ), 10, typeof( GoldPileHeightBrushVariant ), 2 )]
public sealed class GoldPileSmoothBrushTool : GoldPileBrushTool
{
	protected override GoldPileEditorBrushMode Mode => GoldPileEditorBrushMode.Smooth;
	protected override string IconName => "TerrainInspector.TerrainToolSmoothHeight";
}

[EditorTool( "Flatten", typeof( TreasurePileVisual ), typeof( GoldPileSculptToolContext ), 10, typeof( GoldPileHeightBrushVariant ), 3 )]
public sealed class GoldPileFlattenBrushTool : GoldPileBrushTool
{
	protected override GoldPileEditorBrushMode Mode => GoldPileEditorBrushMode.Flatten;
	protected override string IconName => "d_TerrainInspector.TerrainToolSetHeight";
}

sealed class GoldPileHeightBrushVariant { }

// --- Deform variant group ---

[EditorTool( "Inflate", typeof( TreasurePileVisual ), typeof( GoldPileSculptToolContext ), 20, typeof( GoldPileDeformBrushVariant ), 0 )]
public sealed class GoldPileInflateBrushTool : GoldPileBrushTool
{
	protected override GoldPileEditorBrushMode Mode => GoldPileEditorBrushMode.Inflate;
	protected override string IconName => "TerrainInspector.TerrainToolSplat";
}

[EditorTool( "Pinch", typeof( TreasurePileVisual ), typeof( GoldPileSculptToolContext ), 20, typeof( GoldPileDeformBrushVariant ), 1 )]
public sealed class GoldPilePinchBrushTool : GoldPileBrushTool
{
	protected override GoldPileEditorBrushMode Mode => GoldPileEditorBrushMode.Pinch;
	protected override string IconName => "d_ScaleTool";
}

[EditorTool( "Scrape", typeof( TreasurePileVisual ), typeof( GoldPileSculptToolContext ), 20, typeof( GoldPileDeformBrushVariant ), 2 )]
public sealed class GoldPileScrapeBrushTool : GoldPileBrushTool
{
	protected override GoldPileEditorBrushMode Mode => GoldPileEditorBrushMode.Scrape;
	protected override string IconName => "TerrainInspector.TerrainToolTrees";
}

[EditorTool( "Flow", typeof( TreasurePileVisual ), typeof( GoldPileSculptToolContext ), 20, typeof( GoldPileDeformBrushVariant ), 3 )]
public sealed class GoldPileFlowBrushTool : GoldPileBrushTool
{
	protected override GoldPileEditorBrushMode Mode => GoldPileEditorBrushMode.Flow;
	protected override string IconName => "d_WindZone Icon";
}

sealed class GoldPileDeformBrushVariant { }

// --- Shape variant group ---

[EditorTool( "Peak", typeof( TreasurePileVisual ), typeof( GoldPileSculptToolContext ), 30, typeof( GoldPileShapeBrushVariant ), 0 )]
public sealed class GoldPilePeakBrushTool : GoldPileBrushTool
{
	protected override GoldPileEditorBrushMode Mode => GoldPileEditorBrushMode.Peak;
	protected override string IconName => "TerrainInspector.TerrainToolRaise";
}

[EditorTool( "Ridge", typeof( TreasurePileVisual ), typeof( GoldPileSculptToolContext ), 30, typeof( GoldPileShapeBrushVariant ), 1 )]
public sealed class GoldPileRidgeBrushTool : GoldPileBrushTool
{
	protected override GoldPileEditorBrushMode Mode => GoldPileEditorBrushMode.Ridge;
	protected override string IconName => "d_TerrainInspector.TerrainToolRaise";
}

[EditorTool( "Stamp", typeof( TreasurePileVisual ), typeof( GoldPileSculptToolContext ), 30, typeof( GoldPileShapeBrushVariant ), 2 )]
public sealed class GoldPileStampBrushTool : GoldPileBrushTool
{
	protected override GoldPileEditorBrushMode Mode => GoldPileEditorBrushMode.Stamp;
	protected override string IconName => "d_Texture Icon";
}

sealed class GoldPileShapeBrushVariant { }

// --- Process variant group ---

[EditorTool( "Settle", typeof( TreasurePileVisual ), typeof( GoldPileSculptToolContext ), 40, typeof( GoldPileProcessBrushVariant ), 0 )]
public sealed class GoldPileSettleBrushTool : GoldPileBrushTool
{
	protected override GoldPileEditorBrushMode Mode => GoldPileEditorBrushMode.Settle;
	protected override string IconName => "d_SceneViewOrtho";
}

[EditorTool( "Erode", typeof( TreasurePileVisual ), typeof( GoldPileSculptToolContext ), 40, typeof( GoldPileProcessBrushVariant ), 1 )]
public sealed class GoldPileErodeBrushTool : GoldPileBrushTool
{
	protected override GoldPileEditorBrushMode Mode => GoldPileEditorBrushMode.Erode;
	protected override string IconName => "d_TerrainInspector.TerrainToolSmoothHeight";
}

[EditorTool( "Fill", typeof( TreasurePileVisual ), typeof( GoldPileSculptToolContext ), 40, typeof( GoldPileProcessBrushVariant ), 2 )]
public sealed class GoldPileFillBrushTool : GoldPileBrushTool
{
	protected override GoldPileEditorBrushMode Mode => GoldPileEditorBrushMode.Fill;
	protected override string IconName => "d_Grid.FillTool";
}

[EditorTool( "Noise", typeof( TreasurePileVisual ), typeof( GoldPileSculptToolContext ), 40, typeof( GoldPileProcessBrushVariant ), 3 )]
public sealed class GoldPileNoiseBrushTool : GoldPileBrushTool
{
	protected override GoldPileEditorBrushMode Mode => GoldPileEditorBrushMode.Noise;
	protected override string IconName => "d_SceneViewFx";
}

sealed class GoldPileProcessBrushVariant { }
#endif
