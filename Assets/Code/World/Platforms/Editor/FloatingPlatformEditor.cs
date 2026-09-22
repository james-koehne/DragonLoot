#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CanEditMultipleObjects]
[CustomEditor( typeof( FloatingPlatform ) )]
public class FloatingPlatformEditor : Editor
{
	public override void OnInspectorGUI()
	{
		DrawDefaultInspector();

		EditorGUILayout.Space( 6f );
		EditorGUI.BeginDisabledGroup( Application.isPlaying );
		if ( GUILayout.Button( "Reset Rest Pose" ) )
		{
			for ( int i = 0; i < targets.Length; i++ )
			{
				FloatingPlatform platform = targets[ i ] as FloatingPlatform;
				if ( platform == null )
					continue;

				Undo.RecordObject( platform, "Reset Rest Pose" );
				platform.EditorRecaptureRestPoseFromCurrent();
			}
		}

		EditorGUILayout.HelpBox(
			"Stores the current transform as the authored rest pose. Use this while the platform is at its intended height, not while sunk.",
			MessageType.None );
		EditorGUI.EndDisabledGroup();
	}
}
#endif
