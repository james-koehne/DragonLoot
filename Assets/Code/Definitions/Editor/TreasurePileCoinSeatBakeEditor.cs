using UnityEditor;
using UnityEngine;

[CustomEditor( typeof( TreasurePileCoinSeatBake ) )]
public class TreasurePileCoinSeatBakeEditor : Editor
{
	public override void OnInspectorGUI()
	{
		TreasurePileCoinSeatBake bake = ( TreasurePileCoinSeatBake )target;
		EditorGUILayout.HelpBox(
			"GPU coin seat bake for one pile. Seat arrays are hidden (thousands of entries). "
			+ "Generate via TreasurePileVisual → Bake Latents For This Pile (coins bake with latents).",
			MessageType.Info );
		DrawDefaultInspector();
		EditorGUILayout.LabelField( "Seat count", bake.SeatCount.ToString() );
	}
}
