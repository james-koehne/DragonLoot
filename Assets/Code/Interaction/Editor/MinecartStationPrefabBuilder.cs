#if UNITY_EDITOR
using System.IO;

using FeedbackSystem;

using UnityEditor;
using UnityEditor.Callbacks;

using UnityEngine;

/// <summary>
/// Builds Input/Output station prefabs, station definition, and ensures cargo carts have auto controller.
/// </summary>
public static class MinecartStationPrefabBuilder
{
	const string Folder = "Assets/Addressables/Minecart";
	const string DefinitionsFolder = "Assets/Definitions";
	const string DefinitionPath = DefinitionsFolder + "/MinecartStationDefinition.asset";
	const string InputPrefabPath = Folder + "/MinecartInputStation.prefab";
	const string OutputPrefabPath = Folder + "/MinecartOutputStation.prefab";
	const string CargoPrefabPath = Folder + "/Minecart.prefab";
	const string MixedTablePrefabPath = "Assets/Addressables/Tables/MixedDisplayTable.prefab";
	const string AutoBuildKey = "DragonLoot.MinecartStations.AutoBuilt";

	[DidReloadScripts]
	static void AutoBuildOnceAfterCompile()
	{
		if ( EditorApplication.isPlayingOrWillChangePlaymode )
			return;

		if ( SessionState.GetBool( AutoBuildKey, false ) )
			return;

		EditorApplication.delayCall += () =>
		{
			if ( SessionState.GetBool( AutoBuildKey, false ) )
				return;

			SessionState.SetBool( AutoBuildKey, true );
			try
			{
				EnsureEverything();
			}
			catch ( System.Exception ex )
			{
				Debug.LogWarning( "MinecartStationPrefabBuilder auto-build: " + ex.Message );
			}
		};
	}

	[MenuItem( DragonLootMenus.MinecartBuildStations )]
	public static void BuildFromMenu()
	{
		EnsureEverything();
	}

	public static void EnsureEverything()
	{
		EnsureFolders();
		MinecartStationDefinition def = EnsureDefinition();
		EnsureCargoAutoController();
		BuildStationPrefab( InputPrefabPath, typeof( MinecartInputStation ), "MinecartInputStation", def, includeLamp: true );
		BuildStationPrefab( OutputPrefabPath, typeof( MinecartOutputStation ), "MinecartOutputStation", def, includeLamp: false );
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log( "MinecartStationPrefabBuilder: stations + auto controller ready." );
	}

	static void EnsureFolders()
	{
		if ( !AssetDatabase.IsValidFolder( "Assets/Addressables" ) )
			AssetDatabase.CreateFolder( "Assets", "Addressables" );
		if ( !AssetDatabase.IsValidFolder( Folder ) )
			AssetDatabase.CreateFolder( "Assets/Addressables", "Minecart" );
		if ( !AssetDatabase.IsValidFolder( DefinitionsFolder ) )
			AssetDatabase.CreateFolder( "Assets", "Definitions" );
	}

	static MinecartStationDefinition EnsureDefinition()
	{
		MinecartStationDefinition def = AssetDatabase.LoadAssetAtPath<MinecartStationDefinition>( DefinitionPath );
		if ( def != null )
			return def;

		def = ScriptableObject.CreateInstance<MinecartStationDefinition>();
		def.trackSnapRadius = 6f;
		def.transferInterval = 0.25f;
		def.inactivitySeconds = 10f;
		AssetDatabase.CreateAsset( def, DefinitionPath );
		return def;
	}

	static void EnsureCargoAutoController()
	{
		if ( !File.Exists( CargoPrefabPath ) )
		{
			Debug.LogWarning( "MinecartStationPrefabBuilder: missing " + CargoPrefabPath );
			return;
		}

		GameObject root = PrefabUtility.LoadPrefabContents( CargoPrefabPath );
		try
		{
			if ( root.GetComponent<MinecartAutoController>() == null )
			{
				root.AddComponent<MinecartAutoController>();
				PrefabUtility.SaveAsPrefabAsset( root, CargoPrefabPath );
			}
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents( root );
		}
	}

	static void BuildStationPrefab(
		string path,
		System.Type stationType,
		string rootName,
		MinecartStationDefinition def,
		bool includeLamp )
	{
		GameObject root = new GameObject( rootName );
		try
		{
			BoxCollider col = root.AddComponent<BoxCollider>();
			col.size = new Vector3( 1.2f, 2.2f, 1.2f );
			col.center = new Vector3( 0f, 1.1f, 0f );

			Component station = root.AddComponent( stationType );

			GameObject pole = GameObject.CreatePrimitive( PrimitiveType.Cylinder );
			pole.name = "Pole";
			Object.DestroyImmediate( pole.GetComponent<Collider>() );
			pole.transform.SetParent( root.transform, false );
			pole.transform.localPosition = new Vector3( 0f, 1.1f, 0f );
			pole.transform.localScale = new Vector3( 0.12f, 1.1f, 0.12f );

			if ( includeLamp )
			{
				GameObject lamp = GameObject.CreatePrimitive( PrimitiveType.Cube );
				lamp.name = "Lamp";
				Object.DestroyImmediate( lamp.GetComponent<Collider>() );
				lamp.transform.SetParent( root.transform, false );
				lamp.transform.localPosition = new Vector3( 0f, 2.25f, 0f );
				lamp.transform.localScale = new Vector3( 0.28f, 0.18f, 0.28f );
			}

			GameObject storageHost = new GameObject( "Storage" );
			storageHost.transform.SetParent( root.transform, false );
			storageHost.transform.localPosition = new Vector3( 1.6f, 0f, 0f );

			GameObject mixedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>( MixedTablePrefabPath );
			MixedDisplayTableInteractable storage = null;
			if ( mixedPrefab != null )
			{
				GameObject nested = (GameObject)PrefabUtility.InstantiatePrefab( mixedPrefab );
				nested.name = "MixedDisplayTable";
				nested.transform.SetParent( storageHost.transform, false );
				storage = nested.GetComponent<MixedDisplayTableInteractable>();
			}
			else
			{
				storage = storageHost.AddComponent<MixedDisplayTableInteractable>();
				BoxCollider storageCol = storageHost.AddComponent<BoxCollider>();
				storageCol.size = new Vector3( 1.4f, 0.4f, 1.0f );
				storageCol.center = new Vector3( 0f, 0.2f, 0f );
			}

			Feedbacks onCall = EnsureFeedbackChild( root.transform, "OnCallFeedbacks" );
			Feedbacks onArrive = EnsureFeedbackChild( root.transform, "OnArriveFeedbacks" );
			Feedbacks onIdle = EnsureFeedbackChild( root.transform, "OnIdleFeedbacks" );

			MinecartStationBase stationBase = station as MinecartStationBase;
			if ( stationBase != null )
			{
				stationBase.EditorSetDefinition( def );
				stationBase.EditorSetStorage( storage );
				stationBase.EditorSetFeedbacks( onCall, onArrive, onIdle );
			}

			PrefabUtility.SaveAsPrefabAsset( root, path );
		}
		finally
		{
			Object.DestroyImmediate( root );
		}
	}

	static Feedbacks EnsureFeedbackChild( Transform parent, string childName )
	{
		GameObject go = new GameObject( childName );
		go.transform.SetParent( parent, false );
		return go.AddComponent<Feedbacks>();
	}
}
#endif
