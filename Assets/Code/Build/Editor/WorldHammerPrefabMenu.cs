#if UNITY_EDITOR
using UnityEditor;

using UnityEngine;

/// <summary>
/// Creates a simple world hammer prop with <see cref="WorldHammerInteractable"/> for designers to place.
/// </summary>
public static class WorldHammerPrefabMenu
{
	const string PrefabFolder = "Assets/Prefabs/Build";
	const string PrefabPath = "Assets/Prefabs/Build/WorldHammer.prefab";

	[MenuItem( DragonLootMenus.BuildCreateWorldHammerPrefab )]
	static void CreateWorldHammerPrefab()
	{
		AddressableEditorUtil.EnsureFolder( PrefabFolder );

		GameObject root = new GameObject( "WorldHammer" );
		try
		{
			GameObject handle = GameObject.CreatePrimitive( PrimitiveType.Cylinder );
			handle.name = "Handle";
			handle.transform.SetParent( root.transform, false );
			handle.transform.localPosition = new Vector3( 0f, 0.35f, 0f );
			handle.transform.localScale = new Vector3( 0.06f, 0.35f, 0.06f );

			GameObject head = GameObject.CreatePrimitive( PrimitiveType.Cube );
			head.name = "Head";
			head.transform.SetParent( root.transform, false );
			head.transform.localPosition = new Vector3( 0f, 0.72f, 0f );
			head.transform.localScale = new Vector3( 0.28f, 0.12f, 0.12f );

			root.AddComponent<WorldHammerInteractable>();

			// Capsule for aim / interact without relying on child mesh colliders alone.
			CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
			capsule.center = new Vector3( 0f, 0.4f, 0f );
			capsule.radius = 0.2f;
			capsule.height = 1f;

			GameObject prefab = PrefabUtility.SaveAsPrefabAsset( root, PrefabPath );
			AddressableEditorUtil.TryRegister( PrefabPath, PrefabPath );
			Selection.activeObject = prefab;
			Debug.Log( "Created WorldHammer prefab at " + PrefabPath );
		}
		finally
		{
			Object.DestroyImmediate( root );
		}
	}
}
#endif
