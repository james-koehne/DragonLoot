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
			"Rebuild detects overlaps and consolidates Y/T joins into single nodes. " +
			"Bake proposes connections; toggle Used to curate which arms exist. " +
			"Runtime = Used AND both tracks travel-ready (built/active). " +
			"Scene gizmos: green = used+ready, amber = used but unbuilt, grey = unused proposal.",
			MessageType.Info );

		DrawDefaultInspector();

		EditorGUILayout.Space();
		DrawConnectionCurator();

		EditorGUILayout.Space();
		if ( GUILayout.Button( "Rebuild Junctions" ) )
		{
			MinecartJunctionGraph graph = (MinecartJunctionGraph)target;
			ClearHighlight();
			MinecartJunctionGraphBaker.Bake( graph );
		}

		if ( serializedObject.ApplyModifiedProperties() )
		{
			MinecartJunctionGraph graph = (MinecartJunctionGraph)target;
			MinecartTrackMeshBuilder.RebuildAllWithJunctionGaps();
			MinecartJunctionMeshBuilder.Rebuild( graph );
		}
	}

	void OnDisable()
	{
		ClearHighlight();
	}

	static void ClearHighlight()
	{
		MinecartJunctionGraph.EditorHighlightJunction = -1;
		MinecartJunctionGraph.EditorHighlightOption = -1;
	}

	void DrawConnectionCurator()
	{
		MinecartJunctionGraph graph = (MinecartJunctionGraph)target;
		if ( graph.Junctions == null || graph.Junctions.Count == 0 )
		{
			EditorGUILayout.LabelField( "No junctions baked yet.", EditorStyles.miniLabel );
			return;
		}

		EditorGUILayout.LabelField( "Connections (propose → curate)", EditorStyles.boldLabel );
		EditorGUILayout.LabelField(
			"Hover a row to highlight it in the Scene view.",
			EditorStyles.miniLabel );

		bool anyHover = false;
		for ( int j = 0; j < graph.Junctions.Count; j++ )
		{
			MinecartJunction junction = graph.Junctions[ j ];
			if ( junction == null )
				continue;

			int throughCount = junction.throughOptions != null ? junction.throughOptions.Count : 0;
			int cornerCount = junction.cornerOptions != null ? junction.cornerOptions.Count : 0;
			int portCount = junction.ports != null ? junction.ports.Count : 0;
			if ( throughCount == 0 && cornerCount == 0 )
				continue;

			EditorGUILayout.BeginVertical( EditorStyles.helpBox );
			EditorGUILayout.LabelField(
				$"Junction {j} · {junction.resolvedKind} · {portCount} port(s)",
				EditorStyles.boldLabel );

			if ( throughCount > 0 )
			{
				EditorGUILayout.LabelField( "Through", EditorStyles.miniBoldLabel );
				if ( DrawConnectionList( graph, j, junction.throughOptions, isThrough: true ) )
					anyHover = true;
			}

			if ( cornerCount > 0 )
			{
				EditorGUILayout.LabelField( "Turns", EditorStyles.miniBoldLabel );
				if ( DrawConnectionList( graph, j, junction.cornerOptions, isThrough: false ) )
					anyHover = true;
			}

			EditorGUILayout.EndVertical();
			EditorGUILayout.Space( 4f );
		}

		if ( !anyHover )
			ClearHighlight();
	}

	bool DrawConnectionList( MinecartJunctionGraph graph, int junctionIndex, System.Collections.Generic.List<MinecartJunctionCornerOption> options, bool isThrough )
	{
		bool anyHover = false;
		for ( int c = 0; c < options.Count; c++ )
		{
			MinecartJunctionCornerOption opt = options[ c ];
			if ( opt == null )
				continue;

			EditorGUILayout.BeginHorizontal();
			EditorGUI.BeginChangeCheck();
			bool used = EditorGUILayout.ToggleLeft( opt.label, opt.enabled );
			if ( EditorGUI.EndChangeCheck() )
			{
				Undo.RecordObject( graph, "Toggle Junction Connection" );
				opt.enabled = used;
				EditorUtility.SetDirty( graph );
				MinecartJunctionMeshBuilder.Rebuild( graph );
				SceneView.RepaintAll();
			}

			string status;
			Color statusColor;
			if ( !opt.enabled )
			{
				status = "unused";
				statusColor = new Color( 0.6f, 0.6f, 0.6f );
			}
			else if ( !opt.BothTracksTravelReady )
			{
				status = "unbuilt";
				statusColor = new Color( 1f, 0.65f, 0.2f );
			}
			else
			{
				status = "ready";
				statusColor = new Color( 0.3f, 0.85f, 0.4f );
			}

			Color prev = GUI.color;
			GUI.color = statusColor;
			GUILayout.Label( status, EditorStyles.miniLabel, GUILayout.Width( 52f ) );
			GUI.color = prev;

			EditorGUILayout.EndHorizontal();

			Rect row = GUILayoutUtility.GetLastRect();
			if ( row.Contains( Event.current.mousePosition ) )
			{
				anyHover = true;
				MinecartJunctionGraph.EditorHighlightJunction = junctionIndex;
				MinecartJunctionGraph.EditorHighlightOption = c;
				MinecartJunctionGraph.EditorHighlightIsThrough = isThrough;
				if ( Event.current.type == EventType.MouseMove || Event.current.type == EventType.Repaint )
					SceneView.RepaintAll();
			}
		}

		return anyHover;
	}
}
#endif
