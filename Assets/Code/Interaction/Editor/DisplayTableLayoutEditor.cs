using UnityEditor;
using UnityEngine;

/// <summary>
/// Scene handles for display-table row/column layout while configuring in the Inspector.
/// </summary>
[CustomEditor( typeof( MixedDisplayTableInteractable ) )]
public class MixedDisplayTableInteractableEditor : Editor
{
	void OnSceneGUI()
	{
		MixedDisplayTableInteractable table = ( MixedDisplayTableInteractable )target;
		if ( table == null )
			return;

		table.DrawLayoutSceneHandles();
	}
}

[CustomEditor( typeof( TypedDisplayTableInteractable ), true )]
public class TypedDisplayTableInteractableEditor : Editor
{
	void OnSceneGUI()
	{
		TypedDisplayTableInteractable table = ( TypedDisplayTableInteractable )target;
		if ( table == null )
			return;

		table.DrawLayoutSceneHandles();
	}
}
