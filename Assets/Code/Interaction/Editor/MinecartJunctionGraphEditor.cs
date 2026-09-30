#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor( typeof( MinecartJunctionGraph ) )]
public class MinecartJunctionGraphEditor : Editor
{
	public override void OnInspectorGUI()
	{
		serializedObject.Update();

		EditorGUILayout.HelpBox(
			"Rebuild Junctions detects cross-track overlaps, end-to-end joins (curve on bends / straight when colinear), " +
			"and self-junctions (mid-touch + end-to-mid). Kind + Corner/Through toggles are preserved. " +
			"MinSelfSeparation filters trivial self nearest-point hits.",
			MessageType.Info );

		DrawDefaultInspector();

		EditorGUILayout.Space();
		DrawPathOptionQuickToggles();

		EditorGUILayout.Space();
		if ( GUILayout.Button( "Rebuild Junctions" ) )
		{
			MinecartJunctionGraph graph = (MinecartJunctionGraph)target;
			MinecartJunctionGraphBaker.Bake( graph );
		}

		if ( serializedObject.ApplyModifiedProperties() )
		{
			MinecartJunctionGraph graph = (MinecartJunctionGraph)target;
			MinecartTrackMeshBuilder.RebuildAllWithJunctionGaps();
			MinecartJunctionMeshBuilder.Rebuild( graph );
		}
	}

	void DrawPathOptionQuickToggles()
	{
		MinecartJunctionGraph graph = (MinecartJunctionGraph)target;
		if ( graph.Junctions == null || graph.Junctions.Count == 0 )
			return;

		for ( int j = 0; j < graph.Junctions.Count; j++ )
		{
			MinecartJunction junction = graph.Junctions[ j ];
			if ( junction == null )
				continue;

			bool hasThrough = junction.throughOptions != null && junction.throughOptions.Count > 0;
			bool hasCorners = junction.cornerOptions != null && junction.cornerOptions.Count > 0;
			if ( !hasThrough && !hasCorners )
				continue;

			EditorGUILayout.LabelField( $"Junction {j} ({junction.resolvedKind})", EditorStyles.boldLabel );
			EditorGUI.indentLevel++;

			if ( hasThrough )
			{
				EditorGUILayout.LabelField( "Straight / Through", EditorStyles.miniBoldLabel );
				DrawOptionList( graph, junction.throughOptions );
			}

			if ( hasCorners )
			{
				EditorGUILayout.LabelField( "Corners", EditorStyles.miniBoldLabel );
				DrawOptionList( graph, junction.cornerOptions );
			}

			EditorGUI.indentLevel--;
			EditorGUILayout.Space( 4f );
		}
	}

	void DrawOptionList( MinecartJunctionGraph graph, System.Collections.Generic.List<MinecartJunctionCornerOption> options )
	{
		for ( int c = 0; c < options.Count; c++ )
		{
			MinecartJunctionCornerOption opt = options[ c ];
			if ( opt == null )
				continue;

			EditorGUI.BeginChangeCheck();
			bool enabled = EditorGUILayout.ToggleLeft( opt.label, opt.enabled );
			if ( EditorGUI.EndChangeCheck() )
			{
				Undo.RecordObject( graph, "Toggle Junction Path" );
				opt.enabled = enabled;
				EditorUtility.SetDirty( graph );
				MinecartJunctionMeshBuilder.Rebuild( graph );
			}
		}
	}
}
#endif
