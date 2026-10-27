#if UNITY_EDITOR
using System.IO;

using FeedbackSystem;

using UnityEditor;

using UnityEngine;

/// <summary>
/// Builds Input/Output station prefabs, station definition, and ensures cargo carts have auto controller.
/// Menu-only. Creates missing assets; does not rewrite existing prefab transforms.
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
	const string StationLampMaterialPath = "Assets/Materials/Minecart/M_MinecartStationLamp.mat";
	const string StationPoleMaterialPath = "Assets/Materials/Minecart/M_MinecartStationPole.mat";
	const string GoButtonMeshPath = Folder + "/Meshes/MinecartStationGoButton.asset";

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
		// Only create missing station prefabs. Never rewrite existing layout/authorship.
		if ( !File.Exists( InputPrefabPath ) )
			BuildStationPrefab( InputPrefabPath, typeof( MinecartInputStation ), "MinecartInputStation", def, includeLamp: true );
		if ( !File.Exists( OutputPrefabPath ) )
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
				new Vector3( 0f, -0.5f, 0.14f ) );
			changed |= EnsureAutomationButton(
				root.transform,
				station,
				"ResumeButton",
				MinecartStationAutomationButton.ButtonMode.Resume,
				new Vector3( 0f, 0f, 0.14f ) );
			changed |= EnsureAutomationButton(
				root.transform,
				station,
				"GoButton",
				MinecartStationAutomationButton.ButtonMode.GoNext,
				new Vector3( 0f, 0.5f, 0.14f ) );

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
			changed |= EnsureNamedUsesLampMaterial( root.transform, "StopButton" );
			changed |= EnsureNamedUsesLampMaterial( root.transform, "ResumeButton" );
			changed |= EnsureNamedUsesLampMaterial( root.transform, "GoButton" );
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

	static string ResolveButtonInteractionName( MinecartStationAutomationButton.ButtonMode mode )
	{
		if ( mode == MinecartStationAutomationButton.ButtonMode.Stop )
			return "Stop Automation";
		if ( mode == MinecartStationAutomationButton.ButtonMode.Resume )
			return "Resume Automation";
		return "Send to Next Station";
	}

	static bool EnsureAutomationButton(
		Transform root,
		MinecartStationBase station,
		string childName,
		MinecartStationAutomationButton.ButtonMode mode,
		Vector3 localPositionOnMount )
	{
		Transform mount = EnsurePoleButtonMount( root );
		Transform existing = FindAutomationButton( root, childName );
		GameObject go;
		bool created = existing == null;
		bool changed = false;
		if ( created )
		{
			go = GameObject.CreatePrimitive( PrimitiveType.Cube );
			go.name = childName;
			Transform parent = mount != null ? mount : root;
			go.transform.SetParent( parent, false );
			go.transform.localPosition = localPositionOnMount;
			go.transform.localRotation = Quaternion.identity;
			go.transform.localScale = mode == MinecartStationAutomationButton.ButtonMode.GoNext
				? new Vector3( 0.28f, 0.26f, 0.12f )
				: new Vector3( 0.22f, 0.22f, 0.12f );
			MeshCollider meshCol = go.GetComponent<MeshCollider>();
			if ( meshCol != null )
				Object.DestroyImmediate( meshCol );
			changed = true;
		}
		else
			go = existing.gameObject;

		// Existing button transforms are authored on the prefab and must not be overwritten.

		changed |= EnsureButtonMesh( go, mode );

		BoxCollider col = go.GetComponent<BoxCollider>();
		if ( col == null )
		{
			col = go.AddComponent<BoxCollider>();
			changed = true;
		}
		if ( col.isTrigger )
		{
			col.isTrigger = false;
			changed = true;
		}

		MinecartStationAutomationButton button = go.GetComponent<MinecartStationAutomationButton>();
		if ( button == null )
		{
			button = go.AddComponent<MinecartStationAutomationButton>();
			changed = true;
		}

		SerializedObject so = new SerializedObject( button );
		SerializedProperty modeProp = so.FindProperty( "mode" );
		SerializedProperty stationProp = so.FindProperty( "station" );
		SerializedProperty nameProp = so.FindProperty( "interactionName" );
		bool soChanged = false;
		if ( modeProp != null && modeProp.enumValueIndex != (int)mode )
		{
			modeProp.enumValueIndex = (int)mode;
			soChanged = true;
		}

		if ( stationProp != null && stationProp.objectReferenceValue != station )
		{
			stationProp.objectReferenceValue = station;
			soChanged = true;
		}

		string expectedName = ResolveButtonInteractionName( mode );
		if ( nameProp != null && nameProp.stringValue != expectedName )
		{
			nameProp.stringValue = expectedName;
			soChanged = true;
		}

		if ( soChanged )
		{
			so.ApplyModifiedPropertiesWithoutUndo();
			changed = true;
		}

		changed |= AssignStationLampMaterial( go );
		return changed;
	}

	static Transform EnsurePoleButtonMount( Transform root )
	{
		Transform pole = root.Find( "Pole" );
		if ( pole == null )
			return null;

		Transform mount = pole.Find( "ButtonMount" );
		if ( mount != null )
			return mount;

		GameObject go = new GameObject( "ButtonMount" );
		go.transform.SetParent( pole, false );
		go.transform.localPosition = Vector3.zero;
		go.transform.localRotation = Quaternion.identity;
		// Cancel pole non-uniform scale so buttons keep authored world proportions.
		Vector3 poleScale = pole.localScale;
		go.transform.localScale = new Vector3(
			poleScale.x != 0f ? 1f / poleScale.x : 1f,
			poleScale.y != 0f ? 1f / poleScale.y : 1f,
			poleScale.z != 0f ? 1f / poleScale.z : 1f );
		return go.transform;
	}

	static Transform FindAutomationButton( Transform root, string childName )
	{
		Transform direct = root.Find( childName );
		if ( direct != null )
			return direct;

		Transform pole = root.Find( "Pole" );
		if ( pole == null )
			return null;

		Transform underPole = pole.Find( childName );
		if ( underPole != null )
			return underPole;

		Transform mount = pole.Find( "ButtonMount" );
		if ( mount != null )
			return mount.Find( childName );

		return null;
	}

	static bool EnsureButtonMesh( GameObject go, MinecartStationAutomationButton.ButtonMode mode )
	{
		if ( go == null )
			return false;

		MeshFilter filter = go.GetComponent<MeshFilter>();
		if ( filter == null )
			filter = go.AddComponent<MeshFilter>();

		Mesh expected = mode == MinecartStationAutomationButton.ButtonMode.GoNext
			? EnsureGoButtonTriangleMesh()
			: GetBuiltinCubeMesh();

		if ( expected == null || filter.sharedMesh == expected )
			return false;

		filter.sharedMesh = expected;
		return true;
	}

	static Mesh GetBuiltinCubeMesh()
	{
		GameObject temp = GameObject.CreatePrimitive( PrimitiveType.Cube );
		Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
		Object.DestroyImmediate( temp );
		return mesh;
	}

	/// <summary>
	/// Fast-forward (>>) double chevron prisms in local XY (tips +X). Bounds stay unit cube (±0.5).
	/// </summary>
	static Mesh EnsureGoButtonTriangleMesh()
	{
		string folder = Folder + "/Meshes";
		if ( !AssetDatabase.IsValidFolder( folder ) )
		{
			if ( !AssetDatabase.IsValidFolder( Folder ) )
				AssetDatabase.CreateFolder( "Assets/Addressables", "Minecart" );
			AssetDatabase.CreateFolder( Folder, "Meshes" );
		}

		Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>( GoButtonMeshPath );
		if ( mesh != null )
			return mesh;

		mesh = new Mesh { name = "MinecartStationGoButton" };
		ApplyGoButtonFastForwardMesh( mesh );
		AssetDatabase.CreateAsset( mesh, GoButtonMeshPath );
		return mesh;
	}

	static Mesh BuildGoButtonTriangleMesh()
	{
		Mesh mesh = new Mesh { name = "MinecartStationGoButton" };
		ApplyGoButtonFastForwardMesh( mesh );
		return mesh;
	}

	static void ApplyGoButtonFastForwardMesh( Mesh mesh )
	{
		const float halfDepth = 0.5f;
		// Small gap between the two chevrons so it reads as >> not a single diamond.
		const float gap = 0.04f;

		// Left chevron: base at -X, tip toward center.
		Vector3 lTipF = new Vector3( -gap, 0f, halfDepth );
		Vector3 lTopF = new Vector3( -0.5f, 0.5f, halfDepth );
		Vector3 lBotF = new Vector3( -0.5f, -0.5f, halfDepth );
		Vector3 lTipB = new Vector3( -gap, 0f, -halfDepth );
		Vector3 lTopB = new Vector3( -0.5f, 0.5f, -halfDepth );
		Vector3 lBotB = new Vector3( -0.5f, -0.5f, -halfDepth );

		// Right chevron: tip at +X, base near center.
		Vector3 rTipF = new Vector3( 0.5f, 0f, halfDepth );
		Vector3 rTopF = new Vector3( gap, 0.5f, halfDepth );
		Vector3 rBotF = new Vector3( gap, -0.5f, halfDepth );
		Vector3 rTipB = new Vector3( 0.5f, 0f, -halfDepth );
		Vector3 rTopB = new Vector3( gap, 0.5f, -halfDepth );
		Vector3 rBotB = new Vector3( gap, -0.5f, -halfDepth );

		Vector3[] vertices =
		{
			lTipF, lTopF, lBotF, lTipB, lTopB, lBotB,
			rTipF, rTopF, rBotF, rTipB, rTopB, rBotB
		};

		int[] triangles =
		{
			// Left prism
			0, 1, 2,
			3, 5, 4,
			0, 3, 4, 0, 4, 1,
			0, 2, 5, 0, 5, 3,
			1, 4, 5, 1, 5, 2,
			// Right prism
			6, 7, 8,
			9, 11, 10,
			6, 9, 10, 6, 10, 7,
			6, 8, 11, 6, 11, 9,
			7, 10, 11, 7, 11, 8
		};

		mesh.name = "MinecartStationGoButton";
		mesh.Clear();
		mesh.vertices = vertices;
		mesh.triangles = triangles;
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();
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
			lamp.transform.localPosition = new Vector3( 0f, 2.25f, 0f );
			lamp.transform.localRotation = Quaternion.identity;
			lamp.transform.localScale = new Vector3( 0.28f, 0.18f, 0.28f );
			Collider col = lamp.GetComponent<Collider>();
			if ( col != null )
				Object.DestroyImmediate( col );
		}
		else
			lamp = existing.gameObject;

		bool changed = created;
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

	static bool EnsureNamedUsesLampMaterial( Transform root, string childName )
	{
		Transform child = FindAutomationButton( root, childName );
		if ( child == null )
			return false;

		return AssignMaterial( child.gameObject, StationLampMaterialPath );
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

			if ( storage != null )
				storage.ConfigureAsStationStorage();

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
					new Vector3( 0f, -0.5f, 0.14f ) );
				EnsureAutomationButton(
					root.transform,
					stationBase,
					"ResumeButton",
					MinecartStationAutomationButton.ButtonMode.Resume,
					new Vector3( 0f, 0f, 0.14f ) );
				EnsureAutomationButton(
					root.transform,
					stationBase,
					"GoButton",
					MinecartStationAutomationButton.ButtonMode.GoNext,
					new Vector3( 0f, 0.5f, 0.14f ) );
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
