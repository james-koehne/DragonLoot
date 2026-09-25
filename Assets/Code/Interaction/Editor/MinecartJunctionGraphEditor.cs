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
			"Per junction Kind: Auto (detect Cross vs Tee from ports), Cross (X), or Tee (T). " +
			"Tee stubs never exit straight into nothing. Rebuild after changing tracks or Kind. " +
			"Carts follow baked ride curves through turns.",
			MessageType.Info );

		DrawDefaultInspector();

		EditorGUILayout.Space();
		if ( GUILayout.Button( "Rebuild Junctions" ) )
		{
			MinecartJunctionGraph graph = (MinecartJunctionGraph)target;
			MinecartJunctionGraphBaker.Bake( graph );
		}

		if ( serializedObject.ApplyModifiedProperties() )
		{
			MinecartJunctionGraph graph = (MinecartJunctionGraph)target;
			MinecartJunctionMeshBuilder.Rebuild( graph );
		}
	}
}
#endif
