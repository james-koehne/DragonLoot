#if UNITY_EDITOR
using UnityEditor;

using UnityEngine;

/// <summary>
/// Wraps selected GameObjects into <see cref="BuildableObject"/> hierarchies with Built + Ghost children.
/// </summary>
public static class BuildableObjectWrapMenu
{
	[MenuItem( DragonLootMenus.BuildWrapSelectionAsBuildable, true )]
	static bool ValidateWrapSelection()
	{
		return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
	}

	[MenuItem( DragonLootMenus.BuildWrapSelectionAsBuildable )]
	static void WrapSelectionAsBuildable()
	{
		BuildModeDefinition def = AssetDatabase.LoadAssetAtPath<BuildModeDefinition>( BuildModeBootstrap.DefinitionPath );
		GameObject[] selected = Selection.gameObjects;
		for ( int i = 0; i < selected.Length; i++ )
		{
			GameObject go = selected[ i ];
			if ( go == null )
				continue;
			Wrap( go, def );
		}

		AssetDatabase.SaveAssets();
	}

	public static BuildableObject Wrap( GameObject source, BuildModeDefinition def )
	{
		if ( source == null )
			return null;

		BuildableObject existing = source.GetComponent<BuildableObject>();
		if ( existing != null )
		{
			existing.EnsureHierarchy();
			existing.RebuildGhostsFromBuilt( def );
			EditorUtility.SetDirty( existing );
			return existing;
		}

		// If already under a BuildableObject, rebuild that one.
		BuildableObject parentBuildable = source.GetComponentInParent<BuildableObject>();
		if ( parentBuildable != null && parentBuildable.gameObject != source )
		{
			parentBuildable.EnsureHierarchy();
			parentBuildable.RebuildGhostsFromBuilt( def );
			EditorUtility.SetDirty( parentBuildable );
			return parentBuildable;
		}

		Transform originalParent = source.transform.parent;
		int siblingIndex = source.transform.GetSiblingIndex();
		Vector3 worldPos = source.transform.position;
		Quaternion worldRot = source.transform.rotation;
		Vector3 worldScale = source.transform.lossyScale;

		GameObject root = new GameObject( source.name + "_Buildable" );
		Undo.RegisterCreatedObjectUndo( root, "Wrap As Buildable" );
		root.transform.SetParent( originalParent, false );
		root.transform.SetSiblingIndex( siblingIndex );
		root.transform.position = worldPos;
		root.transform.rotation = worldRot;
		root.transform.localScale = Vector3.one;

		BuildableObject buildable = Undo.AddComponent<BuildableObject>( root );
		buildable.EnsureHierarchy();

		Transform built = buildable.BuiltRoot;
		Undo.SetTransformParent( source.transform, built, "Wrap As Buildable" );
		source.transform.localPosition = Vector3.zero;
		source.transform.localRotation = Quaternion.identity;
		// Preserve approximate world scale under identity parent.
		source.transform.localScale = worldScale;

		built.gameObject.SetActive( false );
		buildable.RebuildGhostsFromBuilt( def );
		if ( buildable.GhostRoot != null )
			buildable.GhostRoot.gameObject.SetActive( false );

		EditorUtility.SetDirty( buildable );
		Selection.activeGameObject = root;
		return buildable;
	}
}
#endif
