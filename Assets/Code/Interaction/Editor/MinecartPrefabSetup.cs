#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Places existing Addressable minecart prefabs and appends consist cars.
/// Prefabs are authored in <c>Assets/Addressables/Minecart/</c> — this menu does not create or patch them.
/// </summary>
public static class MinecartPrefabSetup
{
	const string PrefabPath = "Assets/Addressables/Minecart/Minecart.prefab";
	const string DrivePrefabPath = "Assets/Addressables/Minecart/MinecartDrive.prefab";
	const string CallPostPrefabPath = "Assets/Addressables/Minecart/CallPost.prefab";

	[MenuItem( DragonLootMenus.MinecartCreateSetup )]
	[MenuItem( DragonLootMenus.GameObjectMinecartSetup )]
	public static void MenuCreate()
	{
		PlaceInActiveScene( LoadPrefab( PrefabPath ), "Minecart" );
	}

	[MenuItem( DragonLootMenus.MinecartCreateDriveSetup )]
	[MenuItem( DragonLootMenus.GameObjectMinecartDriveSetup )]
	public static void MenuCreateDrive()
	{
		PlaceInActiveScene( LoadPrefab( DrivePrefabPath ), "Drive Minecart" );
	}

	[MenuItem( DragonLootMenus.MinecartCreateCallPost )]
	[MenuItem( DragonLootMenus.GameObjectMinecartCallPost )]
	public static void MenuCreateCallPost()
	{
		GameObject prefab = LoadPrefab( CallPostPrefabPath );
		Scene scene = SceneManager.GetActiveScene();
		if ( !scene.IsValid() || !scene.isLoaded || prefab == null )
			return;

		GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab( prefab, scene );
		instance.name = "MinecartCallPost";
		Undo.RegisterCreatedObjectUndo( instance, "Place Minecart Call Post" );
		Selection.activeGameObject = instance;
	}

	public static void AddCarBehind( MinecartInteractable cart, bool drive )
	{
		if ( cart == null )
			return;

		MinecartInteractable lead = cart.ConsistLead;
		if ( Application.isPlaying )
		{
			MinecartInteractable spawned;
			MinecartConsistUtility.TryAddCar( lead, drive, out spawned );
			return;
		}

		GameObject prefab = LoadPrefab( drive ? DrivePrefabPath : PrefabPath );
		if ( prefab == null )
			return;

		Scene scene = SceneManager.GetActiveScene();
		if ( !scene.IsValid() || !scene.isLoaded )
			return;

		GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab( prefab, scene );
		instance.name = drive ? "Drive Minecart" : "Minecart";
		Undo.RegisterCreatedObjectUndo( instance, drive ? "Add Drive Cart" : "Add Cargo Cart" );
		MinecartInteractable follower = instance.GetComponent<MinecartInteractable>();
		Undo.RecordObject( lead, "Add Consist Car" );
		if ( follower != null )
			Undo.RecordObject( follower, "Add Consist Car" );

		lead.TryAttachFollower( follower );
		EditorUtility.SetDirty( lead );
		if ( follower != null )
			EditorUtility.SetDirty( follower );
	}

	public static void RemoveLastCar( MinecartInteractable cart )
	{
		if ( cart == null )
			return;

		MinecartInteractable lead = cart.ConsistLead;
		if ( Application.isPlaying )
		{
			lead.TryRemoveLastFollower();
			return;
		}

		Undo.RecordObject( lead, "Remove Consist Car" );
		MinecartInteractable follower;
		if ( !lead.TryDetachLastFollower( out follower ) || follower == null )
			return;

		Undo.DestroyObjectImmediate( follower.gameObject );
		EditorUtility.SetDirty( lead );
	}

	static GameObject LoadPrefab( string path )
	{
		GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>( path );
		if ( prefab == null )
			Debug.LogWarning( "Minecart prefab missing: " + path );

		return prefab;
	}

	static void PlaceInActiveScene( GameObject prefab, string instanceName )
	{
		Scene scene = SceneManager.GetActiveScene();
		if ( !scene.IsValid() || !scene.isLoaded || prefab == null )
			return;

		GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab( prefab, scene );
		instance.name = instanceName;
		Undo.RegisterCreatedObjectUndo( instance, "Place " + instanceName );
		MinecartInteractable cart = instance.GetComponent<MinecartInteractable>();
		if ( cart != null )
			MinecartInteractableEditor.TrySnap( cart );
		Selection.activeGameObject = instance;
	}
}
#endif
