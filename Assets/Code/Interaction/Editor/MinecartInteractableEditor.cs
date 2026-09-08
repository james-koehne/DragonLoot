#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CanEditMultipleObjects]
[CustomEditor( typeof( MinecartInteractable ) )]
public class MinecartInteractableEditor : Editor
{
	Vector3 _lastPosition;

	void OnEnable()
	{
		MinecartInteractable cart = target as MinecartInteractable;
		if ( cart != null )
			_lastPosition = cart.transform.position;
	}

	void OnSceneGUI()
	{
		MinecartInteractable cart = target as MinecartInteractable;
		if ( cart == null )
			return;

		cart.DrawLayoutSceneHandles();

		if ( Application.isPlaying )
			return;

		if ( EditorGUIUtility.hotControl != 0 )
			return;

		Vector3 pos = cart.transform.position;
		if ( ( pos - _lastPosition ).sqrMagnitude < 0.0001f )
			return;

		TrySnap( cart );
		_lastPosition = cart.transform.position;
	}

	public override void OnInspectorGUI()
	{
		DrawDefaultInspector();

		MinecartInteractable cart = target as MinecartInteractable;
		if ( cart == null )
			return;

		EditorGUILayout.Space();
		EditorGUILayout.LabelField( "Consist", EditorStyles.boldLabel );
		MinecartInteractable lead = cart.ConsistLead;
		EditorGUILayout.LabelField( "Lead", lead != null ? lead.name : "—" );
		EditorGUILayout.LabelField( "Followers", lead != null ? lead.FollowerCount.ToString() : "0" );

		if ( GUILayout.Button( "Add cargo cart behind" ) )
			MinecartPrefabSetup.AddCarBehind( cart, drive: false );

		if ( GUILayout.Button( "Add drive cart behind" ) )
			MinecartPrefabSetup.AddCarBehind( cart, drive: true );

		GUI.enabled = lead != null && lead.FollowerCount > 0;
		if ( GUILayout.Button( "Remove last car" ) )
			MinecartPrefabSetup.RemoveLastCar( cart );
		GUI.enabled = true;
	}

	public static void TrySnap( MinecartInteractable cart )
	{
		if ( cart == null )
			return;

		MinecartTrack track;
		float distance;
		if ( !MinecartTrack.TryGetNearest( cart.transform.position, cart.TrackSnapRadius, out track, out distance ) )
			return;

		Undo.RecordObject( cart.transform, "Snap Minecart To Track" );
		Undo.RecordObject( cart, "Snap Minecart To Track" );
		cart.EditorSnapToTrack( track, distance );
		EditorUtility.SetDirty( cart );
	}
}
#endif
