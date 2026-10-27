#if UNITY_EDITOR
using UnityEditor;

using UnityEngine;

[CanEditMultipleObjects]
[CustomEditor( typeof( BuildableObject ) )]
public class BuildableObjectEditor : Editor
{
	public override void OnInspectorGUI()
	{
		DrawDefaultInspector();

		EditorGUILayout.Space( 6f );
		EditorGUI.BeginDisabledGroup( Application.isPlaying );
		if ( GUILayout.Button( targets.Length > 1
			? "Update Ghosts (" + targets.Length + ")"
			: "Update Ghosts" ) )
		{
			UpdateGhostsForTargets();
		}

		EditorGUILayout.HelpBox(
			"Rebuilds Ghost mesh parts from the Built hierarchy. Supports multi-select.",
			MessageType.None );
		EditorGUI.EndDisabledGroup();
	}

	void UpdateGhostsForTargets()
	{
		BuildModeDefinition def = AssetDatabase.LoadAssetAtPath<BuildModeDefinition>( BuildModeBootstrap.DefinitionPath );
		for ( int i = 0; i < targets.Length; i++ )
		{
			BuildableObject buildable = targets[ i ] as BuildableObject;
			if ( buildable == null )
				continue;

			Undo.RecordObject( buildable, "Update Buildable Ghosts" );
			if ( buildable.GhostRoot != null )
				Undo.RegisterFullObjectHierarchyUndo( buildable.GhostRoot.gameObject, "Update Buildable Ghosts" );

			buildable.EnsureHierarchy();
			buildable.RebuildGhostsFromBuilt( def );
			if ( buildable.GhostRoot != null )
				buildable.GhostRoot.gameObject.SetActive( false );

			EditorUtility.SetDirty( buildable );
		}
	}
}
#endif
