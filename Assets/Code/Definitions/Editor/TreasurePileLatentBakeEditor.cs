#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Pure-data bake inspector — never draws pose entries (that would freeze the editor on large bakes).
/// Never runs placement or instantiates props on select.
/// </summary>
[CustomEditor( typeof( TreasurePileLatentBake ) )]
public class TreasurePileLatentBakeEditor : Editor
{
	public override void OnInspectorGUI()
	{
		serializedObject.Update();

		TreasurePileLatentBake bake = ( TreasurePileLatentBake )target;
		EditorGUILayout.HelpBox(
			"Pure data bake. Pose entries are hidden — expanding them would hitch/crash the editor.\n"
			+ "Generate via TreasurePileVisual → Bake Latents For This Pile.",
			MessageType.Info );

		EditorGUI.BeginDisabledGroup( true );
		EditorGUILayout.IntField( "Pose Count", bake.PoseCount );
		EditorGUILayout.PropertyField( serializedObject.FindProperty( "authoredLayoutSeed" ) );
		EditorGUILayout.PropertyField( serializedObject.FindProperty( "effectivePileSeed" ) );
		EditorGUILayout.PropertyField( serializedObject.FindProperty( "heightFingerprint" ) );
		EditorGUILayout.PropertyField( serializedObject.FindProperty( "contentsFingerprint" ) );
		EditorGUILayout.PropertyField( serializedObject.FindProperty( "sourceDefinitionName" ) );
		EditorGUILayout.PropertyField( serializedObject.FindProperty( "volumeMaxAttempts" ) );
		EditorGUILayout.PropertyField( serializedObject.FindProperty( "usedSpatialHash" ) );
		EditorGUILayout.PropertyField( serializedObject.FindProperty( "avoidedCoinSeats" ) );
		EditorGUI.EndDisabledGroup();

		// Do NOT DrawDefaultInspector / PropertyField(poses) — poses stay HideInInspector.
		serializedObject.ApplyModifiedProperties();
	}
}
#endif
