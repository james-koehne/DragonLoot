#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
static class MinecartDefinitionInstaller
{
	const string DefinitionPath = "Assets/Definitions/MinecartDefinition.asset";

	static MinecartDefinitionInstaller()
	{
		EditorApplication.delayCall += EnsureInstalled;
	}

	static void EnsureInstalled()
	{
		EnsureFolder( "Assets/Definitions" );

		MinecartDefinition existing = AssetDatabase.LoadAssetAtPath<MinecartDefinition>( DefinitionPath );
		if ( existing != null )
			return;

		MinecartDefinition def = ScriptableObject.CreateInstance<MinecartDefinition>();
		def.maxWeight = 40;
		def.emptyPushSpeed = 3.5f;
		def.fullPushSpeed = 1.25f;
		def.pushAttachRadius = 2.5f;
		def.gridColumns = 4;
		def.gridRows = 3;
		def.cellSpacing = 0.22f;
		def.maxStackPerCell = 0;
		def.unloadDetectRadius = 3f;
		def.unloadScatterRadius = 0.35f;

		AssetDatabase.CreateAsset( def, DefinitionPath );
		AssetDatabase.SaveAssets();
		Debug.Log( "Created " + DefinitionPath );
	}

	static void EnsureFolder( string path )
	{
		if ( AssetDatabase.IsValidFolder( path ) )
			return;

		string parent = System.IO.Path.GetDirectoryName( path )?.Replace( '\\', '/' );
		string leaf = System.IO.Path.GetFileName( path );
		if ( string.IsNullOrEmpty( parent ) || string.IsNullOrEmpty( leaf ) )
			return;

		if ( !AssetDatabase.IsValidFolder( parent ) )
			EnsureFolder( parent );

		AssetDatabase.CreateFolder( parent, leaf );
	}

	[MenuItem( DragonLootMenus.GameObjectMinecartSetup, false, 20 )]
	[MenuItem( DragonLootMenus.MinecartCreateSetup, false, 100 )]
	static void CreateMinecartSetup()
	{
		EnsureInstalled();
		MinecartDefinition def = AssetDatabase.LoadAssetAtPath<MinecartDefinition>( DefinitionPath );

		GameObject root = new GameObject( "Minecart" );
		Undo.RegisterCreatedObjectUndo( root, "Create Minecart Setup" );

		BoxCollider body = root.AddComponent<BoxCollider>();
		body.size = new Vector3( 1.2f, 0.6f, 1.6f );
		body.center = new Vector3( 0f, 0.3f, 0f );

		Rigidbody rb = root.AddComponent<Rigidbody>();
		rb.isKinematic = true;
		rb.useGravity = false;
		rb.interpolation = RigidbodyInterpolation.Interpolate;

		GameObject cargo = new GameObject( "CargoRoot" );
		Undo.RegisterCreatedObjectUndo( cargo, "Create Minecart CargoRoot" );
		cargo.transform.SetParent( root.transform, false );
		cargo.transform.localPosition = new Vector3( 0f, 0.55f, 0f );

		MinecartInteractable cart = root.AddComponent<MinecartInteractable>();
		SerializedObject so = new SerializedObject( cart );
		so.FindProperty( "definition" ).objectReferenceValue = def;
		so.FindProperty( "cargoRoot" ).objectReferenceValue = cargo.transform;
		so.ApplyModifiedPropertiesWithoutUndo();

		Selection.activeGameObject = root;
	}

	[MenuItem( DragonLootMenus.GameObjectMinecartUnloadPoint, false, 21 )]
	[MenuItem( DragonLootMenus.MinecartCreateUnloadPoint, false, 101 )]
	static void CreateUnloadPoint()
	{
		EnsureInstalled();
		MinecartDefinition def = AssetDatabase.LoadAssetAtPath<MinecartDefinition>( DefinitionPath );

		GameObject root = new GameObject( "MinecartUnloadPoint" );
		Undo.RegisterCreatedObjectUndo( root, "Create Minecart Unload Point" );

		BoxCollider col = root.AddComponent<BoxCollider>();
		col.isTrigger = false;
		col.size = new Vector3( 1.5f, 2f, 1.5f );
		col.center = new Vector3( 0f, 1f, 0f );

		GameObject spout = new GameObject( "Spout" );
		Undo.RegisterCreatedObjectUndo( spout, "Create Unload Spout" );
		spout.transform.SetParent( root.transform, false );
		spout.transform.localPosition = new Vector3( 0f, 0.2f, 1f );

		MinecartDropWorldUnloadReceiver drop = root.AddComponent<MinecartDropWorldUnloadReceiver>();
		SerializedObject dropSo = new SerializedObject( drop );
		dropSo.FindProperty( "spout" ).objectReferenceValue = spout.transform;
		dropSo.ApplyModifiedPropertiesWithoutUndo();

		MinecartUnloadPoint point = root.AddComponent<MinecartUnloadPoint>();
		SerializedObject pointSo = new SerializedObject( point );
		pointSo.FindProperty( "definition" ).objectReferenceValue = def;
		pointSo.FindProperty( "unloadReceiver" ).objectReferenceValue = drop;
		pointSo.ApplyModifiedPropertiesWithoutUndo();

		Selection.activeGameObject = root;
	}
}
#endif
