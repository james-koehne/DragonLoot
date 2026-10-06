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
	const string StationLampMaterialPath = "Assets/Materials/Minecart/M_MinecartStationLamp.mat";
	const string StationPoleMaterialPath = "Assets/Materials/Minecart/M_MinecartStationPole.mat";

	[DidReloadScripts]
	static void AutoBuildOnceAfterCompile()
	{
		if ( EditorApplication.isPlayingOrWillChangePlaymode )
			return;

		EditorApplication.delayCall += () =>
		{
			try
			{
				EnsureAutomationButtonsOnPrefabs();
				EnsureRoleVisualsOnPrefabs();
			}
			catch ( System.Exception ex )
			{
				Debug.LogWarning( "MinecartStationPrefabBuilder buttons/role: " + ex.Message );
			}

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
		EnsureAutomationButtonsOnPrefabs();
		EnsureRoleVisualsOnPrefabs();
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log( "MinecartStationPrefabBuilder: stations + auto controller ready." );
	}

	public static void EnsureAutomationButtonsOnPrefabs()
	{
		bool dirty = false;
		if ( File.Exists( InputPrefabPath ) )
			dirty |= EnsureAutomationButtonsOnPrefab( InputPrefabPath );
		if ( File.Exists( OutputPrefabPath ) )
			dirty |= EnsureAutomationButtonsOnPrefab( OutputPrefabPath );
		if ( dirty )
		{
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
		}
	}

	public static void EnsureRoleVisualsOnPrefabs()
	{
		bool dirty = false;
		if ( File.Exists( InputPrefabPath ) )
			dirty |= EnsureRoleVisualsOnPrefab( InputPrefabPath, isInput: true );
		if ( File.Exists( OutputPrefabPath ) )
			dirty |= EnsureRoleVisualsOnPrefab( OutputPrefabPath, isInput: false );
		if ( dirty )
		{
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
		}
	}

	static bool EnsureAutomationButtonsOnPrefab( string path )
	{
		GameObject root = PrefabUtility.LoadPrefabContents( path );
		if ( root == null )
			return false;

		bool changed = false;
		try
		{
			MinecartStationBase station = root.GetComponent<MinecartStationBase>();
			if ( station == null )
				return false;

			changed |= EnsureAutomationButton(
				root.transform,
				station,
				"StopButton",
				MinecartStationAutomationButton.ButtonMode.Stop,
				new Vector3( -0.55f, 1.35f, 0.7f ) );
			changed |= EnsureAutomationButton(
				root.transform,
				station,
				"ResumeButton",
				MinecartStationAutomationButton.ButtonMode.ResumeAndSend,
				new Vector3( 0.55f, 1.35f, 0.7f ) );

			if ( changed )
				PrefabUtility.SaveAsPrefabAsset( root, path );
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents( root );
		}

		return changed;
	}

	static bool EnsureRoleVisualsOnPrefab( string path, bool isInput )
	{
		GameObject root = PrefabUtility.LoadPrefabContents( path );
		if ( root == null )
			return false;

		bool changed = false;
		try
		{
			MinecartStationBase station = root.GetComponent<MinecartStationBase>();
			if ( station == null )
				return false;

			changed |= RemoveRoleArrow( root.transform );
			if ( !isInput )
				changed |= EnsureRoleLamp( root.transform );
			else
				changed |= EnsureChildUsesLampMaterial( root.transform, "Lamp" );

			changed |= EnsureChildUsesMaterial( root.transform, "Pole", StationPoleMaterialPath );
			changed |= EnsureChildUsesLampMaterial( root.transform, "StopButton" );
			changed |= EnsureChildUsesLampMaterial( root.transform, "ResumeButton" );
			changed |= EnsureDockPoint( root.transform, station );

			string expectedName = isInput ? "Send loaded cart" : "Dismiss cart";
			SerializedObject so = new SerializedObject( station );
			SerializedProperty nameProp = so.FindProperty( "interactionName" );
			if ( nameProp != null && nameProp.stringValue != expectedName )
			{
				nameProp.stringValue = expectedName;
				so.ApplyModifiedPropertiesWithoutUndo();
				changed = true;
			}

			if ( changed )
				PrefabUtility.SaveAsPrefabAsset( root, path );
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents( root );
		}

		return changed;
	}

	static bool EnsureAutomationButton(
		Transform root,
		MinecartStationBase station,
		string childName,
		MinecartStationAutomationButton.ButtonMode mode,
		Vector3 localPosition )
	{
		Transform existing = root.Find( childName );
		GameObject go;
		bool created = existing == null;
		if ( created )
		{
			go = GameObject.CreatePrimitive( PrimitiveType.Cube );
			go.name = childName;
			go.transform.SetParent( root, false );
			MeshCollider meshCol = go.GetComponent<MeshCollider>();
			if ( meshCol != null )
				Object.DestroyImmediate( meshCol );
		}
		else
			go = existing.gameObject;

		go.transform.localPosition = localPosition;
		go.transform.localRotation = Quaternion.identity;
		go.transform.localScale = new Vector3( 0.22f, 0.22f, 0.12f );

		BoxCollider col = go.GetComponent<BoxCollider>();
		if ( col == null )
			col = go.AddComponent<BoxCollider>();
		col.isTrigger = false;
		col.size = Vector3.one;
		col.center = Vector3.zero;

		MinecartStationAutomationButton button = go.GetComponent<MinecartStationAutomationButton>();
		if ( button == null )
			button = go.AddComponent<MinecartStationAutomationButton>();

		button.EditorSetMode( mode );
		button.EditorSetStation( station );

		SerializedObject so = new SerializedObject( button );
		SerializedProperty modeProp = so.FindProperty( "mode" );
		SerializedProperty stationProp = so.FindProperty( "station" );
		SerializedProperty nameProp = so.FindProperty( "interactionName" );
		bool changed = created;
		if ( modeProp != null && modeProp.enumValueIndex != (int)mode )
		{
			modeProp.enumValueIndex = (int)mode;
			changed = true;
		}

		if ( stationProp != null && stationProp.objectReferenceValue != station )
		{
			stationProp.objectReferenceValue = station;
			changed = true;
		}

		string expectedName = mode == MinecartStationAutomationButton.ButtonMode.Stop
			? "Stop Automation"
			: "Resume & Send";
		if ( nameProp != null && nameProp.stringValue != expectedName )
		{
			nameProp.stringValue = expectedName;
			changed = true;
		}

		if ( changed )
			so.ApplyModifiedPropertiesWithoutUndo();

		changed |= AssignStationLampMaterial( go );
		return changed || created;
	}

	static bool RemoveRoleArrow( Transform root )
	{
		Transform existing = root.Find( "RoleArrow" );
		if ( existing == null )
			return false;

		Object.DestroyImmediate( existing.gameObject );
		return true;
	}

	static bool EnsureDockPoint( Transform root, MinecartStationBase station )
	{
		Transform existing = root.Find( "DockPoint" );
		bool created = existing == null;
		GameObject go;
		if ( created )
		{
			go = new GameObject( "DockPoint" );
			go.transform.SetParent( root, false );
			go.transform.localPosition = Vector3.zero;
			go.transform.localRotation = Quaternion.identity;
			go.transform.localScale = Vector3.one;
		}
		else
			go = existing.gameObject;

		bool changed = created;
		SerializedObject so = new SerializedObject( station );
		SerializedProperty dockProp = so.FindProperty( "dockPoint" );
		if ( dockProp != null && dockProp.objectReferenceValue != go.transform )
		{
			dockProp.objectReferenceValue = go.transform;
			so.ApplyModifiedPropertiesWithoutUndo();
			changed = true;
		}

		return changed;
	}

	static bool EnsureRoleLamp( Transform root )
	{
		Transform existing = root.Find( "RoleLamp" );
		bool created = existing == null;
		GameObject lamp;
		if ( created )
		{
			lamp = GameObject.CreatePrimitive( PrimitiveType.Cube );
			lamp.name = "RoleLamp";
			lamp.transform.SetParent( root, false );
			Collider col = lamp.GetComponent<Collider>();
			if ( col != null )
				Object.DestroyImmediate( col );
		}
		else
			lamp = existing.gameObject;

		Vector3 targetPos = new Vector3( 0f, 2.25f, 0f );
		Vector3 targetScale = new Vector3( 0.28f, 0.18f, 0.28f );
		bool changed = created;
		if ( lamp.transform.localPosition != targetPos )
		{
			lamp.transform.localPosition = targetPos;
			changed = true;
		}

		if ( lamp.transform.localRotation != Quaternion.identity )
		{
			lamp.transform.localRotation = Quaternion.identity;
			changed = true;
		}

		if ( lamp.transform.localScale != targetScale )
		{
			lamp.transform.localScale = targetScale;
			changed = true;
		}

		Collider leftover = lamp.GetComponent<Collider>();
		if ( leftover != null )
		{
			Object.DestroyImmediate( leftover );
			changed = true;
		}

		changed |= AssignStationLampMaterial( lamp );
		return changed;
	}

	static bool EnsureChildUsesLampMaterial( Transform root, string childName )
	{
		return EnsureChildUsesMaterial( root, childName, StationLampMaterialPath );
	}

	static bool EnsureChildUsesMaterial( Transform root, string childName, string materialPath )
	{
		Transform child = root.Find( childName );
		if ( child == null )
			return false;

		return AssignMaterial( child.gameObject, materialPath );
	}

	static bool AssignStationLampMaterial( GameObject go )
	{
		return AssignMaterial( go, StationLampMaterialPath );
	}

	static bool AssignMaterial( GameObject go, string materialPath )
	{
		if ( go == null )
			return false;

		Renderer renderer = go.GetComponent<Renderer>();
		if ( renderer == null )
			return false;

		Material mat = AssetDatabase.LoadAssetAtPath<Material>( materialPath );
		if ( mat == null )
			return false;

		if ( renderer.sharedMaterial == mat )
			return false;

		renderer.sharedMaterial = mat;
		return true;
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
			bool isInput = stationType == typeof( MinecartInputStation );

			GameObject pole = GameObject.CreatePrimitive( PrimitiveType.Cylinder );
			pole.name = "Pole";
			Object.DestroyImmediate( pole.GetComponent<Collider>() );
			pole.transform.SetParent( root.transform, false );
			pole.transform.localPosition = new Vector3( 0f, 1.1f, 0f );
			pole.transform.localScale = new Vector3( 0.12f, 1.1f, 0.12f );
			AssignMaterial( pole, StationPoleMaterialPath );

			if ( includeLamp )
			{
				GameObject lamp = GameObject.CreatePrimitive( PrimitiveType.Cube );
				lamp.name = "Lamp";
				Object.DestroyImmediate( lamp.GetComponent<Collider>() );
				lamp.transform.SetParent( root.transform, false );
				lamp.transform.localPosition = new Vector3( 0f, 2.25f, 0f );
				lamp.transform.localScale = new Vector3( 0.28f, 0.18f, 0.28f );
				AssignStationLampMaterial( lamp );
			}
			else
				EnsureRoleLamp( root.transform );

			EnsureDockPoint( root.transform, station as MinecartStationBase );

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

				SerializedObject so = new SerializedObject( stationBase );
				SerializedProperty nameProp = so.FindProperty( "interactionName" );
				if ( nameProp != null )
				{
					nameProp.stringValue = isInput ? "Send loaded cart" : "Dismiss cart";
					so.ApplyModifiedPropertiesWithoutUndo();
				}

				EnsureAutomationButton(
					root.transform,
					stationBase,
					"StopButton",
					MinecartStationAutomationButton.ButtonMode.Stop,
					new Vector3( -0.55f, 1.35f, 0.7f ) );
				EnsureAutomationButton(
					root.transform,
					stationBase,
					"ResumeButton",
					MinecartStationAutomationButton.ButtonMode.ResumeAndSend,
					new Vector3( 0.55f, 1.35f, 0.7f ) );
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
