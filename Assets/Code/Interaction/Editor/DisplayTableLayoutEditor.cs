using UnityEditor;
using UnityEngine;

/// <summary>
/// Scene handles for display-table row/column layout while configuring in the Inspector.
/// </summary>
[CanEditMultipleObjects]
[CustomEditor( typeof( MixedDisplayTableInteractable ) )]
public class MixedDisplayTableInteractableEditor : Editor
{
	void OnSceneGUI()
	{
		MixedDisplayTableInteractable table = target as MixedDisplayTableInteractable;
		if ( table == null )
			return;

		table.DrawLayoutSceneHandles();
	}
}

[CanEditMultipleObjects]
[CustomEditor( typeof( TypedDisplayTableInteractable ), true )]
public class TypedDisplayTableInteractableEditor : Editor
{
	void OnSceneGUI()
	{
		TypedDisplayTableInteractable table = target as TypedDisplayTableInteractable;
		if ( table == null )
			return;

		table.DrawLayoutSceneHandles();
	}
}
