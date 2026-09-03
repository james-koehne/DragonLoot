#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Pure-data bake inspector — never draws pose entries (that would freeze the editor on large bakes).
/// Never runs placement or instantiates props on select.
/// Read-only: do not ApplyModifiedProperties, or a stale SerializedObject can overwrite a just-written bake.
/// </summary>
[CustomEditor( typeof( TreasurePileLatentBake ) )]
public class TreasurePileLatentBakeEditor : Editor
{
	public override void OnInspectorGUI()
	{
		TreasurePileLatentBake bake = ( TreasurePileLatentBake )target;
		EditorGUILayout.HelpBox(
			"Pure data bake. Pose entries are hidden — expanding them would hitch/crash the editor.\n"
			+ "Placement policy is TreasurePileLatentBakeSettings on the pile visual. Generate via TreasurePileVisual → Bake Latents For This Pile.",
			MessageType.Info );

		EditorGUI.BeginDisabledGroup( true );
		EditorGUILayout.IntField( "Pose Count", bake.PoseCount );
		EditorGUILayout.IntField( "Authored Layout Seed", bake.authoredLayoutSeed );
		EditorGUILayout.IntField( "Effective Pile Seed", bake.effectivePileSeed );
		EditorGUILayout.IntField( "Height Fingerprint", bake.heightFingerprint );
		EditorGUILayout.IntField( "Contents Fingerprint", bake.contentsFingerprint );
		EditorGUILayout.TextField( "Source Definition", bake.sourceDefinitionName ?? "" );
		EditorGUILayout.IntField( "Volume Max Attempts", bake.volumeMaxAttempts );
		EditorGUILayout.Toggle( "Used Spatial Hash", bake.usedSpatialHash );
		EditorGUILayout.Toggle( "Avoided Coin Seats", bake.avoidedCoinSeats );
		EditorGUILayout.Toggle( "Baked Near Surface", bake.bakedNearSurface );
		EditorGUILayout.IntField( "Authored Fingerprint", bake.authoredFingerprint );
		EditorGUI.EndDisabledGroup();
	}
}
#endif
