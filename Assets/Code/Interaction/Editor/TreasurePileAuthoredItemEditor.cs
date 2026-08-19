#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor( typeof( TreasurePileAuthoredItem ) )]
public class TreasurePileAuthoredItemEditor : Editor
{
	public override void OnInspectorGUI()
	{
		TreasurePileAuthoredItem authored = ( TreasurePileAuthoredItem )target;
		serializedObject.Update();

		EditorGUILayout.HelpBox(
			"Curated pile prop. Keep this object visible while you position it.\n"
			+ "Required: Treasure Definition (the loot type, e.g. CrystalCandelabrum).\n"
			+ "The Addressable *Visual prefab is enough for the mesh — TreasureItem is optional in the editor.",
			MessageType.Info );

		EditorGUILayout.PropertyField( serializedObject.FindProperty( "definition" ) );
		EditorGUILayout.PropertyField( serializedObject.FindProperty( "treasureItem" ) );

		TreasureDefinition def = authored.Definition;
		if ( def == null )
		{
			EditorGUILayout.HelpBox(
				"Assign a Treasure Definition. Without it, bake/play ignore this object.",
				MessageType.Error );
		}
		else if ( !TreasurePileAuthoredItem.IsCuratable( def ) )
		{
			EditorGUILayout.HelpBox(
				"This definition is not curatable (coins and gems stay procedural).",
				MessageType.Warning );
		}

		serializedObject.ApplyModifiedProperties();
	}
}
#endif
