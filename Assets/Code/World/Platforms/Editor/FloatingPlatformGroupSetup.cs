#if UNITY_EDITOR
using System.Collections.Generic;

using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates a <see cref="FloatingPlatformGroup"/> from the selection (or a placeholder cube).
/// </summary>
public static class FloatingPlatformGroupSetup
{
	[MenuItem( DragonLootMenus.WorldCreateFloatingPlatformGroup )]
	[MenuItem( DragonLootMenus.GameObjectFloatingPlatformGroup )]
	public static void MenuCreateGroup()
	{
		List<GameObject> sources = CollectSourceObjects();
		bool createdPlaceholder = false;
		if ( sources.Count == 0 )
		{
			GameObject cube = GameObject.CreatePrimitive( PrimitiveType.Cube );
			cube.name = "FloatingPlatform";
			cube.transform.localScale = new Vector3( 2f, 0.25f, 2f );
			Undo.RegisterCreatedObjectUndo( cube, "Create Floating Platform Placeholder" );
			sources.Add( cube );
			createdPlaceholder = true;
		}

		GameObject groupGo = new GameObject( "FloatingPlatformGroup" );
		Undo.RegisterCreatedObjectUndo( groupGo, "Create Floating Platform Group" );

		Vector3 center = Vector3.zero;
		for ( int i = 0; i < sources.Count; i++ )
			center += sources[ i ].transform.position;
		center /= sources.Count;
		groupGo.transform.position = center;

		FloatingPlatform[] platforms = new FloatingPlatform[ sources.Count ];
		for ( int i = 0; i < sources.Count; i++ )
		{
			GameObject source = sources[ i ];
			Undo.SetTransformParent( source.transform, groupGo.transform, "Parent Floating Platform" );
			ApplyWalkableLayer( source );

			FloatingPlatform platform = source.GetComponent<FloatingPlatform>();
			if ( platform == null )
				platform = Undo.AddComponent<FloatingPlatform>( source );

			platform.EditorCaptureRestPose();
			platforms[ i ] = platform;

			if ( createdPlaceholder )
				Selection.activeGameObject = source;
		}

		FloatingPlatformGroup group = Undo.AddComponent<FloatingPlatformGroup>( groupGo );
		group.EditorSetPlatforms( platforms );
		group.EditorCaptureAllRestPoses();

		Selection.activeGameObject = groupGo;
		EditorGUIUtility.PingObject( groupGo );
		Debug.Log( "FloatingPlatformGroupSetup: created group with " + platforms.Length + " platform(s)." );
	}

	static List<GameObject> CollectSourceObjects()
	{
		List<GameObject> result = new List<GameObject>();
		GameObject[] selected = Selection.gameObjects;
		if ( selected == null )
			return result;

		for ( int i = 0; i < selected.Length; i++ )
		{
			GameObject go = selected[ i ];
			if ( go == null )
				continue;
			if ( go.GetComponent<FloatingPlatformGroup>() != null )
				continue;
			result.Add( go );
		}

		return result;
	}

	static void ApplyWalkableLayer( GameObject root )
	{
		if ( root == null )
			return;

		int walkable = PhysicsLayers.WalkableLayer;
		if ( walkable < 0 )
			walkable = 0;

		Undo.RecordObject( root, "Set Walkable Layer" );
		root.layer = walkable;
		EditorUtility.SetDirty( root );
	}
}
#endif
