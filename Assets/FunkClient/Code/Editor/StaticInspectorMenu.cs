using UnityEngine;
using UnityEditor;

public static class StaticInspectorMenu
{
	[MenuItem( "Assets/Open in Static Inspector", true )]
	private static bool ValidateSelection()
	{
		return Selection.activeObject != null;
	}

	[MenuItem( "Assets/Open in Static Inspector %#i" )]
	private static void OpenSelected()
	{
		foreach ( var obj in Selection.objects )
		{
			StaticInspectorWindow.OpenForObject( obj );
		}
	}
}
