#if UNITY_EDITOR
using System.IO;

using FeedbackSystem;

using UnityEditor;

using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Builds cube grinder, coffee cup treasure definition, and wires Cafe_Coffee_Machine_1
/// with sockets, nested parts, Feedbacks, and CoffeeMachineInteractable.
/// </summary>
public static class CoffeeMachinePrefabBuilder
{
	public const string CoffeeFolder = "Assets/Prefabs/Interaction/Coffee";
	public const string MachinePath = CoffeeFolder + "/Cafe_Coffee_Machine_1.prefab";
	public const string GrinderPath = CoffeeFolder + "/Cafe_Coffee_Grinder_1.prefab";
	public const string PortafilterPath = CoffeeFolder + "/Cafe_Portafilter_1.prefab";
	public const string TampPath = CoffeeFolder + "/Cafe_Tamper_1.prefab";
	public const string TampMatPath = CoffeeFolder + "/Cafe_Tamper_Mat_1.prefab";
	public const string CupEmptyPath = CoffeeFolder + "/Cafe_Cup_1.prefab";
	public const string CupPath = CoffeeFolder + "/Cafe_Cup_2.prefab";
	public const string CupDefinitionPath = "Assets/Definitions/Treasure/Coffee_Cup.asset";
	public const string MachineDefinitionPath = "Assets/Definitions/CoffeeMachineDefinition.asset";

	const string GrindClipPath = "Assets/Audio/SFX/Machines/pixabay_grinding-coffee-beans.wav";
	const string BrewClipPath = "Assets/Audio/SFX/Machines/pixabay_coffee-machine-40834.mp3";
	const string SipClipPath = "Assets/Audio/SFX/Machines/pixabay_sipping-coffee-6063.mp3";
	const string SlurpClipPath = "Assets/Audio/SFX/Machines/pixabay_slurping-coffee-46191.mp3";

	[MenuItem( DragonLootMenus.StationsCoffeeSetup )]
	public static void SetupFromMenu()
	{
		// Does not rebuild Cafe_Coffee_Machine_1 layout — only support assets + missing sockets / nested animated cup.
		SetupCoffeeSupportAssets();
		EnsureMissingMachineSockets();
		Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>( MachinePath );
		Debug.Log( "CoffeeMachinePrefabBuilder: support assets + missing sockets ensured (machine layout preserved)." );
	}

	[MenuItem( DragonLootMenus.DefinitionsEnsureCoffeeMachine )]
	public static void EnsureDefinitionFromMenu()
	{
		CoffeeMachineDefinition def = EnsureMachineDefinition();
		Selection.activeObject = def;
		AssetDatabase.SaveAssets();
		Debug.Log( "CoffeeMachinePrefabBuilder: CoffeeMachineDefinition ready at " + MachineDefinitionPath );
	}

	public static void BuildFromCommandLine()
	{
		SetupCoffeeSupportAssets();
		EnsureMissingMachineSockets();
		Debug.Log( "CoffeeMachinePrefabBuilder: command-line setup complete." );
	}

	public static void SetupCoffeeMachine()
	{
		SetupCoffeeSupportAssets();
		EnsureMissingMachineSockets();
	}

	static void SetupCoffeeSupportAssets()
	{
		AddressableEditorUtil.EnsureFolder( CoffeeFolder );
		AddressableEditorUtil.EnsureFolder( "Assets/Definitions/Treasure" );
		AddressableEditorUtil.EnsureFolder( "Assets/Definitions" );

		EnsureGrinderPrefab();
		TreasureDefinition cupDef = EnsureCupDefinition();
		SetupCupPrefab( cupDef );
		EnsureMachineDefinition();

		AddressableEditorUtil.TryRegister( GrindClipPath, GrindClipPath );
		AddressableEditorUtil.TryRegister( BrewClipPath, BrewClipPath );
		AddressableEditorUtil.TryRegister( SipClipPath, SipClipPath );
		AddressableEditorUtil.TryRegister( SlurpClipPath, SlurpClipPath );

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
	}

	static CoffeeMachineDefinition EnsureMachineDefinition()
	{
		CoffeeMachineDefinition def = AssetDatabase.LoadAssetAtPath<CoffeeMachineDefinition>( MachineDefinitionPath );
		if ( def == null )
		{
			def = ScriptableObject.CreateInstance<CoffeeMachineDefinition>();
			AssetDatabase.CreateAsset( def, MachineDefinitionPath );
		}

		AudioClip sip = AssetDatabase.LoadAssetAtPath<AudioClip>( SipClipPath );
		AudioClip slurp = AssetDatabase.LoadAssetAtPath<AudioClip>( SlurpClipPath );
		if ( def.sipClipA == null && sip != null )
			def.sipClipA = sip;
		if ( def.sipClipB == null && slurp != null )
			def.sipClipB = slurp;

		def.sipVolumeMin = 1.5f;
		def.sipVolumeMax = 1.5f;
		EditorUtility.SetDirty( def );
		return def;
	}

	/// <summary>
	/// Adds only missing socket empties and wires null serialized refs. Never deletes/rebuilds nested parts.
	/// </summary>
	static void EnsureMissingMachineSockets()
	{
		if ( !File.Exists( MachinePath ) )
			return;

		GameObject root = PrefabUtility.LoadPrefabContents( MachinePath );
		try
		{
			CoffeeMachineInteractable machine = root.GetComponent<CoffeeMachineInteractable>();
			if ( machine == null )
				machine = root.AddComponent<CoffeeMachineInteractable>();

			Transform sockets = EnsureChild( root.transform, "Sockets" );
			Transform portafilterTamp = EnsurePoseIfMissing( sockets, "PortafilterTamp", new Vector3( 0.55f, 0.28f, 0.2f ), Quaternion.identity );
			Transform cupReady = EnsurePoseIfMissing( sockets, "CupReady", new Vector3( 0f, 0.18f, 0.38f ), Quaternion.identity );
			Transform cupPark = sockets.Find( "CupPark" );

			GameObject animatedInstance = EnsureNestedAnimatedCup( root.transform, cupPark );

			SerializedObject so = new SerializedObject( machine );
			AssignIfNull( so, "portafilterTamp", portafilterTamp );
			AssignIfNull( so, "cupReady", cupReady );

			TreasureDefinition cupDef = AssetDatabase.LoadAssetAtPath<TreasureDefinition>( CupDefinitionPath );
			AssignIfNull( so, "coffeeCupDefinition", cupDef );

			CoffeeMachineDefinition machineDef = AssetDatabase.LoadAssetAtPath<CoffeeMachineDefinition>( MachineDefinitionPath );
			AssignIfNull( so, "definition", machineDef );

			SerializedProperty animatedProp = so.FindProperty( "animatedCup" );
			if ( animatedProp != null && animatedInstance != null )
			{
				// Always prefer a nested instance over a prefab-asset reference.
				bool needsReplace = animatedProp.objectReferenceValue == null;
				GameObject current = animatedProp.objectReferenceValue as GameObject;
				if ( current != null && !current.scene.IsValid() )
					needsReplace = true;
				if ( needsReplace )
					animatedProp.objectReferenceValue = animatedInstance;
			}

			so.ApplyModifiedPropertiesWithoutUndo();

			PrefabUtility.SaveAsPrefabAsset( root, MachinePath );
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents( root );
		}
	}

	static GameObject EnsureNestedAnimatedCup( Transform machineRoot, Transform cupPark )
	{
		Transform existing = machineRoot.Find( "AnimatedCup" );
		if ( existing != null )
			return existing.gameObject;

		GameObject cupPrefab = AssetDatabase.LoadAssetAtPath<GameObject>( CupEmptyPath );
		if ( cupPrefab == null )
			return null;

		GameObject instance = PrefabUtility.InstantiatePrefab( cupPrefab, machineRoot ) as GameObject;
		if ( instance == null )
			return null;

		instance.name = "AnimatedCup";
		if ( cupPark != null )
		{
			instance.transform.position = cupPark.position;
			instance.transform.rotation = cupPark.rotation;
		}

		return instance;
	}

	static Transform EnsurePoseIfMissing( Transform parent, string name, Vector3 localPos, Quaternion localRot )
	{
		Transform existing = parent.Find( name );
		if ( existing != null )
			return existing;

		return EnsurePose( parent, name, localPos, localRot );
	}

	static void AssignIfNull( SerializedObject so, string propertyName, Object value )
	{
		SerializedProperty prop = so.FindProperty( propertyName );
		if ( prop == null || prop.objectReferenceValue != null || value == null )
			return;

		prop.objectReferenceValue = value;
	}

	static void EnsureGrinderPrefab()
	{
		GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>( GrinderPath );
		if ( existing != null )
			return;

		GameObject root = new GameObject( "Cafe_Coffee_Grinder_1" );
		try
		{
			BuildGrinderVisual( root.transform );
			PrefabUtility.SaveAsPrefabAsset( root, GrinderPath );
		}
		finally
		{
			Object.DestroyImmediate( root );
		}
	}

	static void BuildGrinderVisual( Transform root )
	{
		Material mat = ResolveDarkMaterial();

		CreateCube( root, "Body", new Vector3( 0f, 0.28f, 0f ), new Vector3( 0.28f, 0.4f, 0.28f ), mat );
		CreateCube( root, "Hopper", new Vector3( 0f, 0.58f, 0f ), new Vector3( 0.34f, 0.22f, 0.34f ), mat );
		CreateCube( root, "HopperLip", new Vector3( 0f, 0.7f, 0f ), new Vector3( 0.38f, 0.04f, 0.38f ), mat );
		CreateCube( root, "Chute", new Vector3( 0f, 0.08f, 0.18f ), new Vector3( 0.12f, 0.08f, 0.16f ), mat );
		CreateCube( root, "Base", new Vector3( 0f, 0.02f, 0f ), new Vector3( 0.32f, 0.04f, 0.32f ), mat );
		CreateCube( root, "Dial", new Vector3( 0.14f, 0.32f, 0.12f ), new Vector3( 0.06f, 0.06f, 0.04f ), mat );
	}

	static void CreateCube( Transform parent, string name, Vector3 localPos, Vector3 localScale, Material mat )
	{
		GameObject cube = GameObject.CreatePrimitive( PrimitiveType.Cube );
		cube.name = name;
		cube.transform.SetParent( parent, false );
		cube.transform.localPosition = localPos;
		cube.transform.localRotation = Quaternion.identity;
		cube.transform.localScale = localScale;

		Collider col = cube.GetComponent<Collider>();
		if ( col != null )
			Object.DestroyImmediate( col );

		MeshRenderer renderer = cube.GetComponent<MeshRenderer>();
		if ( renderer != null && mat != null )
			renderer.sharedMaterial = mat;
	}

	static Material ResolveDarkMaterial()
	{
		Material temp = AssetDatabase.LoadAssetAtPath<Material>( "Assets/Materials/Temp/MetalCrank.mat" );
		if ( temp != null )
			return temp;

		string[] guids = AssetDatabase.FindAssets( "t:Material M_BuildGhost" );
		if ( guids != null && guids.Length > 0 )
		{
			string path = AssetDatabase.GUIDToAssetPath( guids[ 0 ] );
			Material found = AssetDatabase.LoadAssetAtPath<Material>( path );
			if ( found != null )
				return found;
		}

		return AssetDatabase.GetBuiltinExtraResource<Material>( "Default-Material.mat" );
	}

	static TreasureDefinition EnsureCupDefinition()
	{
		TreasureDefinition existing = AssetDatabase.LoadAssetAtPath<TreasureDefinition>( CupDefinitionPath );
		if ( existing == null )
		{
			existing = ScriptableObject.CreateInstance<TreasureDefinition>();
			AssetDatabase.CreateAsset( existing, CupDefinitionPath );
		}

		existing.id = "coffee_cup";
		existing.displayName = "Coffee";
		existing.category = TreasureCategory.General;
		existing.value = 1;
		existing.exclusiveCarry = true;
		existing.cannotThrow = false;
		existing.suppressDiscoveryPopup = true;
		existing.worldScale = Vector3.one;
		existing.heldScale = Vector3.one * 0.85f;
		existing.pickupRadius = 0.35f;
		existing.nonCoinPickupHoldDelayOverride = 0.35f;

		string cupGuid = AssetDatabase.AssetPathToGUID( CupPath );
		if ( !string.IsNullOrEmpty( cupGuid ) )
		{
			existing.prefab = new AssetReferenceGameObject( cupGuid );
			AddressableEditorUtil.TryRegister( CupPath, CupPath );
		}

		EditorUtility.SetDirty( existing );
		return existing;
	}

	static void SetupCupPrefab( TreasureDefinition cupDef )
	{
		if ( !File.Exists( CupPath ) )
		{
			Debug.LogWarning( "CoffeeMachinePrefabBuilder: missing cup prefab at " + CupPath );
			return;
		}

		GameObject root = PrefabUtility.LoadPrefabContents( CupPath );
		try
		{
			Rigidbody body = root.GetComponent<Rigidbody>();
			if ( body == null )
				body = root.AddComponent<Rigidbody>();
			body.isKinematic = true;
			body.useGravity = false;

			TreasureItem item = root.GetComponent<TreasureItem>();
			if ( item == null )
				item = root.AddComponent<TreasureItem>();
			item.Bind( cupDef, viaAddressables: false );

			if ( root.GetComponent<TreasureItemInteractable>() == null )
				root.AddComponent<TreasureItemInteractable>();

			if ( root.GetComponent<CoffeeCupState>() == null )
				root.AddComponent<CoffeeCupState>();

			MeshCollider meshCol = root.GetComponent<MeshCollider>();
			if ( meshCol != null )
				meshCol.convex = true;

			PrefabUtility.SaveAsPrefabAsset( root, CupPath );
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents( root );
		}
	}

	static void WireMachinePrefab( TreasureDefinition cupDef )
	{
		if ( !File.Exists( MachinePath ) )
		{
			Debug.LogWarning( "CoffeeMachinePrefabBuilder: missing machine prefab at " + MachinePath );
			return;
		}

		GameObject root = PrefabUtility.LoadPrefabContents( MachinePath );
		try
		{
			Transform rootXf = root.transform;
			ClearGeneratedChildren( rootXf );

			CoffeeMachineInteractable machine = root.GetComponent<CoffeeMachineInteractable>();
			if ( machine == null )
				machine = root.AddComponent<CoffeeMachineInteractable>();

			BoxCollider box = root.GetComponent<BoxCollider>();
			if ( box == null && root.GetComponent<Collider>() == null )
			{
				box = root.AddComponent<BoxCollider>();
				box.center = new Vector3( 0f, 0.45f, 0f );
				box.size = new Vector3( 1.2f, 1.0f, 0.7f );
			}

			Transform sockets = EnsureChild( rootXf, "Sockets" );
			Transform portafilterRest = EnsurePose( sockets, "PortafilterRest", new Vector3( 0.45f, 0.35f, 0.1f ), Quaternion.Euler( 0f, 20f, 0f ) );
			Transform portafilterLift = EnsurePose( sockets, "PortafilterLift", new Vector3( 0.35f, 0.55f, 0.15f ), Quaternion.Euler( -10f, 10f, 0f ) );
			Transform portafilterUnderGrinder = EnsurePose( sockets, "PortafilterUnderGrinder", new Vector3( 0.55f, 0.22f, 0.25f ), Quaternion.Euler( 0f, 0f, 0f ) );
			Transform portafilterApproachHead = EnsurePose( sockets, "PortafilterApproachHead", new Vector3( 0f, 0.42f, 0.22f ), Quaternion.Euler( 0f, 0f, -20f ) );
			Transform portafilterLocked = EnsurePose( sockets, "PortafilterLocked", new Vector3( 0f, 0.38f, 0.18f ), Quaternion.Euler( 0f, 0f, -70f ) );

			Transform tampMat = EnsurePose( sockets, "TampMat", new Vector3( 0.55f, 0.32f, -0.05f ), Quaternion.identity );
			Transform tampLift = EnsurePose( sockets, "TampLift", new Vector3( 0.55f, 0.5f, -0.05f ), Quaternion.identity );
			Transform tampHover = EnsurePose( sockets, "TampHover", new Vector3( 0.55f, 0.42f, 0.25f ), Quaternion.identity );
			Transform tampPress = EnsurePose( sockets, "TampPress", new Vector3( 0.55f, 0.3f, 0.25f ), Quaternion.identity );

			Transform cupPark = EnsurePose( sockets, "CupPark", new Vector3( -0.35f, 0.2f, 0.15f ), Quaternion.identity );
			Transform cupUnderSpout = EnsurePose( sockets, "CupUnderSpout", new Vector3( 0f, 0.18f, 0.32f ), Quaternion.identity );

			GameObject grinder = NestPrefab( rootXf, GrinderPath, "Grinder", new Vector3( 0.55f, 0f, 0.25f ), Quaternion.identity );
			GameObject portafilterGo = NestPrefab( rootXf, PortafilterPath, "Portafilter", portafilterRest.localPosition, portafilterRest.localRotation );
			GameObject tampMatGo = NestPrefab( rootXf, TampMatPath, "TamperMat", new Vector3( 0.55f, 0.3f, -0.05f ), Quaternion.identity );
			GameObject tampGo = NestPrefab( rootXf, TampPath, "Tamp", tampMat.localPosition, tampMat.localRotation );
			GameObject cupGo = NestPrefab( rootXf, CupPath, "Cup", cupPark.localPosition, cupPark.localRotation );

			if ( tampMatGo != null )
				tampMat.position = tampMatGo.transform.position;

			Feedbacks grindFb = EnsureSfxFeedback( rootXf, "OnGrindFeedbacks", GrindClipPath, spatial: true );
			Feedbacks tampFb = EnsureSfxFeedback( rootXf, "OnTampFeedbacks", null, spatial: true );
			Feedbacks lockFb = EnsureSfxFeedback( rootXf, "OnLockFeedbacks", null, spatial: true );
			Feedbacks brewFb = EnsureSfxFeedback( rootXf, "OnBrewFeedbacks", BrewClipPath, spatial: true );

			SerializedObject so = new SerializedObject( machine );
			SetRef( so, "portafilter", portafilterGo != null ? portafilterGo.transform : null );
			SetRef( so, "tamp", tampGo != null ? tampGo.transform : null );
			SetRef( so, "cupRoot", cupGo != null ? cupGo.transform : null );
			SetRef( so, "portafilterRest", portafilterRest );
			SetRef( so, "portafilterLift", portafilterLift );
			SetRef( so, "portafilterUnderGrinder", portafilterUnderGrinder );
			SetRef( so, "portafilterApproachHead", portafilterApproachHead );
			SetRef( so, "portafilterLocked", portafilterLocked );
			SetRef( so, "tampMat", tampMat );
			SetRef( so, "tampLift", tampLift );
			SetRef( so, "tampHover", tampHover );
			SetRef( so, "tampPress", tampPress );
			SetRef( so, "cupPark", cupPark );
			SetRef( so, "cupUnderSpout", cupUnderSpout );
			SetRef( so, "grindFeedbacks", grindFb );
			SetRef( so, "tampFeedbacks", tampFb );
			SetRef( so, "lockFeedbacks", lockFb );
			SetRef( so, "brewFeedbacks", brewFb );
			SetRef( so, "coffeeCupDefinition", cupDef );
			so.ApplyModifiedPropertiesWithoutUndo();

			if ( cupGo != null )
			{
				TreasureItem cupItem = cupGo.GetComponent<TreasureItem>();
				if ( cupItem != null )
					cupItem.Bind( cupDef, viaAddressables: false );

				CoffeeCupState cupState = cupGo.GetComponent<CoffeeCupState>();
				if ( cupState != null )
					cupState.DisablePickup();
			}

			if ( portafilterGo != null )
				portafilterGo.transform.SetPositionAndRotation( portafilterRest.position, portafilterRest.rotation );
			if ( tampGo != null )
				tampGo.transform.SetPositionAndRotation( tampMat.position, tampMat.rotation );

			PrefabUtility.SaveAsPrefabAsset( root, MachinePath );
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents( root );
		}
	}

	static void ClearGeneratedChildren( Transform root )
	{
		string[] generated =
		{
			"Sockets", "Grinder", "Portafilter", "TamperMat", "Tamp", "Cup",
			"OnGrindFeedbacks", "OnTampFeedbacks", "OnLockFeedbacks", "OnBrewFeedbacks"
		};

		for ( int i = 0; i < generated.Length; i++ )
		{
			Transform child = root.Find( generated[ i ] );
			if ( child != null )
				Object.DestroyImmediate( child.gameObject );
		}
	}

	static Transform EnsureChild( Transform parent, string name )
	{
		Transform existing = parent.Find( name );
		if ( existing != null )
			return existing;

		GameObject go = new GameObject( name );
		go.transform.SetParent( parent, false );
		return go.transform;
	}

	static Transform EnsurePose( Transform parent, string name, Vector3 localPos, Quaternion localRot )
	{
		Transform pose = EnsureChild( parent, name );
		pose.localPosition = localPos;
		pose.localRotation = localRot;
		pose.localScale = Vector3.one;
		return pose;
	}

	static GameObject NestPrefab( Transform parent, string prefabPath, string childName, Vector3 localPos, Quaternion localRot )
	{
		GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>( prefabPath );
		if ( prefab == null )
		{
			Debug.LogWarning( "CoffeeMachinePrefabBuilder: missing prefab " + prefabPath );
			return null;
		}

		GameObject instance = PrefabUtility.InstantiatePrefab( prefab, parent ) as GameObject;
		if ( instance == null )
			return null;

		instance.name = childName;
		instance.transform.localPosition = localPos;
		instance.transform.localRotation = localRot;
		instance.transform.localScale = Vector3.one;
		return instance;
	}

	static Feedbacks EnsureSfxFeedback( Transform root, string childName, string clipPath, bool spatial )
	{
		Feedbacks feedbacks = EnsureFeedbackChild( root, childName );
		AudioClip clip = string.IsNullOrEmpty( clipPath )
			? null
			: AssetDatabase.LoadAssetAtPath<AudioClip>( clipPath );

		PlaySFXFeedback sfx = null;
		for ( int i = 0; i < feedbacks.FeedbackList.Count; i++ )
		{
			sfx = feedbacks.FeedbackList[ i ] as PlaySFXFeedback;
			if ( sfx != null )
				break;
		}

		if ( sfx == null )
		{
			sfx = new PlaySFXFeedback();
			feedbacks.AddFeedback( sfx );
		}

		sfx.Clip = clip;
		sfx.SpatialBlend = spatial ? 1f : 0f;
		sfx.MinDistance = 1f;
		sfx.MaxDistance = 18f;
		sfx.VolumeMin = 0.85f;
		sfx.VolumeMax = 1f;
		sfx.FollowCallerTransform = true;
		EditorUtility.SetDirty( feedbacks );
		return feedbacks;
	}

	static Feedbacks EnsureFeedbackChild( Transform root, string childName )
	{
		Transform existing = root.Find( childName );
		GameObject go = existing != null ? existing.gameObject : new GameObject( childName );
		if ( existing == null )
			go.transform.SetParent( root, false );

		Feedbacks feedbacks = go.GetComponent<Feedbacks>();
		if ( feedbacks == null )
			feedbacks = go.AddComponent<Feedbacks>();
		return feedbacks;
	}

	static void SetRef( SerializedObject so, string propertyName, Object value )
	{
		SerializedProperty prop = so.FindProperty( propertyName );
		if ( prop == null )
			return;

		prop.objectReferenceValue = value;
	}
}
#endif
