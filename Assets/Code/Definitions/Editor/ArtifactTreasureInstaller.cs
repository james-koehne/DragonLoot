using System.Collections.Generic;
using System.IO;

using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Builds Fantasy Treasure Pack artifact visuals, TreasureDefinitions, Addressables
/// entries, and mixed-rarity pile contents. Idempotent; safe to re-run from the menu.
/// </summary>
public static class ArtifactTreasureInstaller
{
	const string SourceFolder = "Assets/ThirdParty/LowlyPoly/Fantasy Treasure Pack/Prefab";
	const string VisualFolder = "Assets/Addressables/Treasure/Artifacts";
	const string DefinitionFolder = "Assets/Definitions/Treasure/Artifacts";
	const int CollectableLayer = 6;

	static readonly string[] PileDefinitionPaths =
	{
		"Assets/Definitions/Treasure/Pile/TreasurePileDefinition.asset",
		"Assets/Definitions/Treasure/Pile/TreasurePileLargeDefinition.asset",
		"Assets/Definitions/Treasure/Pile/TreasurePileGiganticDefinition.asset",
	};

	struct ArtifactSpec
	{
		public string SourceName;
		public string DefName;
		public string DisplayName;
		public string Variant;
		public int Value;
		public int Weight;
		public bool UsesHeavyThrow;
		public float Scale;
		public float Mass;
		public int PileCount;
		public int MaxVisible;
	}

	static readonly ArtifactSpec[] Catalog =
	{
		// Books — scroll rolls
		Spec( "roll_small_006", "AncientTome", "Ancient Scroll", "Scroll", 45, 1, false, 0.28f, 0.18f, 40, 4 ),
		Spec( "roll_small_007", "GildedGrimoire", "Gilded Scroll", "Scroll", 50, 1, false, 0.28f, 0.18f, 40, 4 ),
		Spec( "roll_small_008", "SealedCodex", "Sealed Scroll", "Scroll", 52, 1, false, 0.28f, 0.18f, 40, 4 ),
		Spec( "roll_small_005", "RuneLedger", "Rune Scroll", "Scroll", 55, 1, false, 0.28f, 0.18f, 40, 4 ),

		// Pendants — common
		Spec( "pendant_01", "SapphirePendant", "Sapphire Pendant", "Pendant", 55, 1, false, 0.22f, 0.12f, 35, 4 ),
		Spec( "pendant_06", "RubyHeartPendant", "Ruby Heart Pendant", "Pendant", 65, 1, false, 0.22f, 0.12f, 35, 4 ),
		Spec( "pendant_11", "ObsidianAmulet", "Obsidian Amulet", "Pendant", 75, 1, false, 0.22f, 0.12f, 35, 4 ),

		// Cups — mid
		Spec( "cup_02", "BronzeChalice", "Bronze Chalice", "Cup", 70, 1, false, 0.26f, 0.2f, 25, 3 ),
		Spec( "cup_05", "EmeraldGoblet", "Emerald Goblet", "Cup", 80, 1, false, 0.26f, 0.2f, 25, 3 ),
		Spec( "cup_08", "RoyalWineCup", "Royal Wine Cup", "Cup", 90, 2, false, 0.26f, 0.22f, 25, 3 ),

		// Candelabra — uncommon
		Spec( "candelabrum_01", "BrassCandelabrum", "Brass Candelabrum", "Candelabrum", 85, 2, true, 0.32f, 0.35f, 18, 2 ),
		Spec( "candelabrum_03", "OrnateCandelabrum", "Ornate Candelabrum", "Candelabrum", 95, 2, true, 0.32f, 0.35f, 18, 2 ),
		Spec( "candelabrum_05", "CrystalCandelabrum", "Crystal Candelabrum", "Candelabrum", 110, 3, true, 0.32f, 0.4f, 18, 2 ),

		// Coronas — rare
		Spec( "corona_001", "IronCorona", "Iron Corona", "Corona", 120, 2, false, 0.3f, 0.28f, 12, 2 ),
		Spec( "corona_005", "JewelledCorona", "Jewelled Corona", "Corona", 135, 2, false, 0.3f, 0.28f, 12, 2 ),
		Spec( "corona_010", "GoldenCorona", "Golden Corona", "Corona", 145, 3, false, 0.3f, 0.3f, 12, 2 ),
		Spec( "corona_0015", "RoyalCorona", "Royal Corona", "Corona", 150, 3, false, 0.3f, 0.3f, 12, 2 ),

		// Swords — rarest
		Spec( "sword_01", "RustyShortsword", "Rusty Shortsword", "Sword", 100, 2, true, 0.35f, 0.4f, 10, 1 ),
		Spec( "sword_04", "CeremonialBlade", "Ceremonial Blade", "Sword", 120, 3, true, 0.35f, 0.45f, 10, 1 ),
		Spec( "sword_07", "DragonsteelSword", "Dragonsteel Sword", "Sword", 140, 3, true, 0.35f, 0.5f, 10, 1 ),
	};

	static ArtifactSpec Spec(
		string sourceName,
		string defName,
		string displayName,
		string variant,
		int value,
		int weight,
		bool usesHeavyThrow,
		float scale,
		float mass,
		int pileCount,
		int maxVisible )
	{
		return new ArtifactSpec
		{
			SourceName = sourceName,
			DefName = defName,
			DisplayName = displayName,
			Variant = variant,
			Value = value,
			Weight = weight,
			UsesHeavyThrow = usesHeavyThrow,
			Scale = scale,
			Mass = mass,
			PileCount = pileCount,
			MaxVisible = maxVisible,
		};
	}

	const string AutoInstallFlagPath = "Temp/InstallFantasyPackArtifacts.flag";

	[MenuItem( DragonLootMenus.TreasureInstallArtifacts )]
	public static void InstallFromMenu()
	{
		Install( showDialog: true );
	}

	/// <summary>Unity batchmode entry: -executeMethod ArtifactTreasureInstaller.InstallFromCommandLine</summary>
	public static void InstallFromCommandLine()
	{
		Install( showDialog: false );
	}

	[InitializeOnLoadMethod]
	static void AutoInstallIfFlagged()
	{
		EditorApplication.delayCall += () =>
		{
			string flagPath = Path.GetFullPath( AutoInstallFlagPath );
			if ( !File.Exists( flagPath ) )
				return;

			try
			{
				File.Delete( flagPath );
			}
			catch ( System.Exception ex )
			{
				Debug.LogWarning( "ArtifactTreasureInstaller: could not delete auto-install flag: " + ex.Message );
				return;
			}

			Install( showDialog: false );
		};
	}

	public static void Install( bool showDialog )
	{
		EnsureFolder( VisualFolder );
		EnsureFolder( DefinitionFolder );

		var createdDefs = new List<TreasureDefinition>( Catalog.Length );

		for ( int i = 0; i < Catalog.Length; i++ )
		{
			ArtifactSpec spec = Catalog[ i ];
			string visualPath = VisualFolder + "/" + spec.DefName + "Visual.prefab";
			string defPath = DefinitionFolder + "/" + spec.DefName + ".asset";
			string sourcePath = SourceFolder + "/" + spec.SourceName + ".prefab";

			GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>( sourcePath );
			if ( source == null )
			{
				Debug.LogError( "ArtifactTreasureInstaller: missing source prefab " + sourcePath );
				continue;
			}

			string visualGuid = CreateOrUpdateVisual( source, visualPath );
			if ( string.IsNullOrEmpty( visualGuid ) )
			{
				Debug.LogError( "ArtifactTreasureInstaller: failed visual for " + spec.DefName );
				continue;
			}

			RegisterAddressable( visualPath );

			TreasureDefinition def = CreateOrUpdateDefinition( spec, defPath, visualGuid );
			if ( def != null )
				createdDefs.Add( def );
		}

		UpdateAllPiles( createdDefs );

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();

		string message = "Installed " + createdDefs.Count + " artifact treasures and updated "
			+ PileDefinitionPaths.Length + " pile definitions.";
		Debug.Log( "ArtifactTreasureInstaller: " + message );
		if ( showDialog )
			EditorUtility.DisplayDialog( "Fantasy Pack Artifacts", message, "OK" );
	}

	static string CreateOrUpdateVisual( GameObject source, string visualPath )
	{
		GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab( source );
		if ( instance == null )
			instance = Object.Instantiate( source );

		PrefabUtility.UnpackPrefabInstance(
			instance,
			PrefabUnpackMode.Completely,
			InteractionMode.AutomatedAction );

		string visualName = Path.GetFileNameWithoutExtension( visualPath );
		instance.name = visualName;
		instance.transform.SetPositionAndRotation( Vector3.zero, Quaternion.identity );
		instance.transform.localScale = Vector3.one;

		StripAnimators( instance );
		SetLayerRecursive( instance, CollectableLayer );
		EnsurePhysicsComponents( instance );

		GameObject saved = PrefabUtility.SaveAsPrefabAsset( instance, visualPath );
		Object.DestroyImmediate( instance );

		if ( saved == null )
			return null;

		return AssetDatabase.AssetPathToGUID( visualPath );
	}

	static void StripAnimators( GameObject root )
	{
		Animator[] animators = root.GetComponentsInChildren<Animator>( true );
		for ( int i = 0; i < animators.Length; i++ )
		{
			if ( animators[ i ] != null )
				Object.DestroyImmediate( animators[ i ] );
		}
	}

	static void SetLayerRecursive( GameObject root, int layer )
	{
		Transform[] transforms = root.GetComponentsInChildren<Transform>( true );
		for ( int i = 0; i < transforms.Length; i++ )
			transforms[ i ].gameObject.layer = layer;
	}

	static void EnsurePhysicsComponents( GameObject root )
	{
		Collider[] existing = root.GetComponentsInChildren<Collider>( true );
		for ( int i = 0; i < existing.Length; i++ )
		{
			if ( existing[ i ] != null )
				Object.DestroyImmediate( existing[ i ] );
		}

		Rigidbody[] bodies = root.GetComponentsInChildren<Rigidbody>( true );
		for ( int i = 0; i < bodies.Length; i++ )
		{
			if ( bodies[ i ] != null && bodies[ i ].gameObject != root )
				Object.DestroyImmediate( bodies[ i ] );
		}

		AddColliders( root );

		Rigidbody body = root.GetComponent<Rigidbody>();
		if ( body == null )
			body = root.AddComponent<Rigidbody>();
		body.mass = 0.25f;
		body.linearDamping = 0.6f;
		body.angularDamping = 0.6f;
		body.useGravity = true;
		body.isKinematic = true;
		body.interpolation = RigidbodyInterpolation.Interpolate;
		body.collisionDetectionMode = CollisionDetectionMode.Discrete;
	}

	static void AddColliders( GameObject root )
	{
		MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>( true );
		int added = 0;
		for ( int i = 0; i < filters.Length; i++ )
		{
			MeshFilter filter = filters[ i ];
			if ( filter == null || filter.sharedMesh == null )
				continue;

			Mesh mesh = filter.sharedMesh;
			if ( mesh.vertexCount > 0 && mesh.vertexCount <= 255 )
			{
				MeshCollider meshCollider = filter.gameObject.AddComponent<MeshCollider>();
				meshCollider.sharedMesh = mesh;
				meshCollider.convex = true;
				added++;
			}
			else
			{
				Renderer renderer = filter.GetComponent<Renderer>();
				Bounds localBounds = mesh.bounds;
				BoxCollider box = filter.gameObject.AddComponent<BoxCollider>();
				box.center = localBounds.center;
				box.size = localBounds.size;
				if ( renderer == null )
				{
					// keep mesh-local bounds
				}
				added++;
			}
		}

		if ( added == 0 )
		{
			Bounds bounds = CalculateRendererBounds( root );
			BoxCollider fallback = root.AddComponent<BoxCollider>();
			if ( bounds.size.sqrMagnitude > 0.0001f )
			{
				fallback.center = root.transform.InverseTransformPoint( bounds.center );
				Vector3 lossy = root.transform.lossyScale;
				fallback.size = new Vector3(
					SafeDiv( bounds.size.x, lossy.x ),
					SafeDiv( bounds.size.y, lossy.y ),
					SafeDiv( bounds.size.z, lossy.z ) );
			}
			else
			{
				fallback.size = Vector3.one * 0.5f;
			}
		}
	}

	static Bounds CalculateRendererBounds( GameObject root )
	{
		Renderer[] renderers = root.GetComponentsInChildren<Renderer>( true );
		if ( renderers == null || renderers.Length == 0 )
			return new Bounds( root.transform.position, Vector3.zero );

		Bounds bounds = renderers[ 0 ].bounds;
		for ( int i = 1; i < renderers.Length; i++ )
		{
			if ( renderers[ i ] != null )
				bounds.Encapsulate( renderers[ i ].bounds );
		}
		return bounds;
	}

	static float SafeDiv( float a, float b )
	{
		return Mathf.Abs( b ) < 0.0001f ? a : a / b;
	}

	static TreasureDefinition CreateOrUpdateDefinition( ArtifactSpec spec, string defPath, string visualGuid )
	{
		TreasureDefinition def = AssetDatabase.LoadAssetAtPath<TreasureDefinition>( defPath );
		if ( def == null )
		{
			def = ScriptableObject.CreateInstance<TreasureDefinition>();
			AssetDatabase.CreateAsset( def, defPath );
		}

		def.id = spec.DefName;
		def.displayName = spec.DisplayName;
		def.category = TreasureCategory.Artifact;
		def.variant = spec.Variant;
		def.value = spec.Value;
		def.weight = spec.Weight;
		def.exclusiveCarry = false;
		def.usesHeavyThrow = spec.UsesHeavyThrow;
		def.rigidbodyMass = spec.Mass;
		def.drag = 0.6f;
		def.angularDrag = 0.6f;
		def.sleepThreshold = 0.01f;
		def.autoToppleStrength = 0f;
		def.maxUprightAngle = 35f;
		def.physicsStabilizationDelay = 0.35f;
		def.pickupRadius = 0.45f;
		def.worldScale = Vector3.one * spec.Scale;
		def.heldScale = Vector3.one * spec.Scale;
		def.canStack = false;
		def.cartGridSize = new Vector2Int( 2, 2 );
		def.coinThickness = spec.Scale * 0.6f;
		def.prefab = new AssetReferenceGameObject( visualGuid );

		EditorUtility.SetDirty( def );
		return def;
	}

	static void UpdateAllPiles( List<TreasureDefinition> artifactDefs )
	{
		if ( artifactDefs == null || artifactDefs.Count == 0 )
			return;

		for ( int i = 0; i < PileDefinitionPaths.Length; i++ )
		{
			TreasurePileDefinition pile = AssetDatabase.LoadAssetAtPath<TreasurePileDefinition>( PileDefinitionPaths[ i ] );
			if ( pile == null )
			{
				Debug.LogError( "ArtifactTreasureInstaller: missing pile " + PileDefinitionPaths[ i ] );
				continue;
			}

			UpdatePileContents( pile, artifactDefs );
			EditorUtility.SetDirty( pile );
		}
	}

	static void UpdatePileContents( TreasurePileDefinition pile, List<TreasureDefinition> artifactDefs )
	{
		var kept = new List<TreasurePileEntry>();
		TreasurePileEntry[] existing = pile.treasureContents;
		if ( existing != null )
		{
			for ( int i = 0; i < existing.Length; i++ )
			{
				TreasureDefinition treasure = existing[ i ].treasure;
				if ( treasure == null )
					continue;
				if ( treasure.category == TreasureCategory.Artifact )
					continue;
				kept.Add( existing[ i ] );
			}
		}

		for ( int i = 0; i < Catalog.Length; i++ )
		{
			ArtifactSpec spec = Catalog[ i ];
			TreasureDefinition match = FindDef( artifactDefs, spec.DefName );
			if ( match == null )
				continue;

			kept.Add( new TreasurePileEntry
			{
				treasure = match,
				count = spec.PileCount,
			} );
		}

		pile.treasureContents = kept.ToArray();
	}

	static TreasureDefinition FindDef( List<TreasureDefinition> defs, string id )
	{
		for ( int i = 0; i < defs.Count; i++ )
		{
			if ( defs[ i ] != null && defs[ i ].id == id )
				return defs[ i ];
		}
		return null;
	}

	static void RegisterAddressable( string assetPath )
	{
		AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
		if ( settings == null )
		{
			Debug.LogWarning( "AddressableAssetSettings missing; skipped registering " + assetPath );
			return;
		}

		string guid = AssetDatabase.AssetPathToGUID( assetPath );
		if ( string.IsNullOrEmpty( guid ) )
			return;

		AddressableAssetGroup group = settings.DefaultGroup;
		AddressableAssetEntry entry = settings.FindAssetEntry( guid );
		if ( entry == null )
			entry = settings.CreateOrMoveEntry( guid, group, readOnly: false, postEvent: false );

		entry.SetAddress( assetPath );
		settings.SetDirty( AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true );
	}

	static void EnsureFolder( string path )
	{
		if ( AssetDatabase.IsValidFolder( path ) )
			return;

		string parent = Path.GetDirectoryName( path ).Replace( '\\', '/' );
		string name = Path.GetFileName( path );
		if ( !AssetDatabase.IsValidFolder( parent ) )
			EnsureFolder( parent );

		AssetDatabase.CreateFolder( parent, name );
	}
}
