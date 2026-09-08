#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;

using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Idempotent create/update for a single treasure: visual prefab, Addressable, definition, category extras.
/// Does not wire piles, museum slots, or quests.
/// </summary>
public static class TreasureCreationPipeline
{
	public const string AddressablesRoot = "Assets/Addressables/Treasure";
	public const string DefinitionsRoot = "Assets/Definitions/Treasure";
	public const string ChestDefsFolder = DefinitionsRoot + "/Chests/ChestDefs";

	public enum VisualSourceMode
	{
		GenerateFromPrefab,
		GenerateFromMesh,
		UseExistingVisual
	}

	public class Request
	{
		public string Id;
		public string DisplayName;
		public TreasureCategory Category;
		public string Variant;

		public int Value = 1;
		public int Weight = 1;
		public bool ExclusiveCarry;
		public bool UsesHeavyThrow;
		public bool CannotThrow;
		public bool PlaceOnGroundOrArtifactSlotOnly;
		public float ThrowForceScale = 1f;
		public float ThrowUpBiasScale = 1f;

		public float RigidbodyMass = 0.1f;
		public float Drag = 0.5f;
		public float AngularDrag = 0.5f;
		public float SleepThreshold = 0.01f;
		public float AutoToppleStrength = 2.5f;
		public float MaxUprightAngle = 35f;
		public float PhysicsStabilizationDelay = 0.35f;
		public float PickupRadius = 0.35f;
		public bool CollideWithPlayerOnPile;

		public KeyType KeyType = KeyType.Iron;
		public bool IsSkeletonKey;

		public string ChestType = "Wood";
		public bool ChestStartsLocked = true;
		public bool ChestDestroyOnOpen;
		public float ChestLockpickDuration = 8f;
		public ChestContentEntry[] ChestContents;

		public Sprite Icon;
		public Mesh MeshOverride;
		public Material MaterialOverride;
		public Vector3 WorldScale = Vector3.one * 0.35f;
		public Vector3 HeldScale = Vector3.one * 0.08f;
		public Vector3 ActiveHeldLocalEuler = Vector3.zero;
		public Vector3 HeldStackLocalEuler = Vector3.zero;

		public TreasureCleaningRequirement CleaningRequirement = TreasureCleaningRequirement.Auto;
		public bool CanStack = true;
		public float CoinThickness = 0.04f;
		public Vector2Int CartGridSize = Vector2Int.one;

		public AudioClip[] PickupClips;
		public float PickupVolumeMin = 1f;
		public float PickupVolumeMax = 1f;
		public float PickupPitchMin = 1f;
		public float PickupPitchMax = 1f;
		public AudioClip[] PlaceClips;
		public float PlaceVolumeMin = 1f;
		public float PlaceVolumeMax = 1f;
		public float PlacePitchMin = 1f;
		public float PlacePitchMax = 1f;

		public VisualSourceMode VisualMode = VisualSourceMode.GenerateFromPrefab;
		public GameObject SourcePrefab;
		public Mesh SourceMesh;
		public Material[] SourceMaterials;
		public GameObject ExistingVisualPrefab;

		public bool ConvertArtifactMaterials = true;
		public Material GemMaterial;
		public bool CloneGemMaterial;

		public string VisualPathOverride;
		public string DefinitionPathOverride;
		public string ChestDefinitionPathOverride;
	}

	public class Result
	{
		public bool Success;
		public string Error;
		public string VisualPath;
		public string DefinitionPath;
		public string ChestDefinitionPath;
		public TreasureDefinition Definition;
		public string Summary;
	}

	public static string CategoryFolderName( TreasureCategory category )
	{
		switch ( category )
		{
			case TreasureCategory.Coin:
				return "Coins";
			case TreasureCategory.Gem:
				return "Gems";
			case TreasureCategory.Key:
				return "Keys";
			case TreasureCategory.Chest:
				return "Chests";
			default:
				return "Artifacts";
		}
	}

	public static string DefaultVisualPath( string id, TreasureCategory category )
	{
		return AddressablesRoot + "/" + CategoryFolderName( category ) + "/" + id + "Visual.prefab";
	}

	public static string DefaultDefinitionPath( string id, TreasureCategory category )
	{
		return DefinitionsRoot + "/" + CategoryFolderName( category ) + "/" + id + ".asset";
	}

	public static string DefaultChestDefinitionPath( string id )
	{
		return ChestDefsFolder + "/" + id + "Definition.asset";
	}

	public static string SanitizeId( string raw )
	{
		if ( string.IsNullOrEmpty( raw ) )
			return string.Empty;

		var sb = new StringBuilder( raw.Length );
		for ( int i = 0; i < raw.Length; i++ )
		{
			char c = raw[ i ];
			if ( char.IsLetterOrDigit( c ) || c == '_' )
				sb.Append( c );
			else if ( c == ' ' || c == '-' )
				sb.Append( '_' );
		}

		string result = sb.ToString();
		if ( result.Length > 0 && char.IsDigit( result[ 0 ] ) )
			result = "T_" + result;
		return result;
	}

	public static void ApplyCategoryDefaults( Request request )
	{
		if ( request == null )
			return;

		switch ( request.Category )
		{
			case TreasureCategory.Coin:
				request.ExclusiveCarry = false;
				request.UsesHeavyThrow = false;
				request.CanStack = true;
				request.RigidbodyMass = 0.08f;
				request.Drag = 0.5f;
				request.AngularDrag = 0.5f;
				request.AutoToppleStrength = 2.5f;
				request.MaxUprightAngle = 35f;
				request.PickupRadius = 0.35f;
				request.CollideWithPlayerOnPile = false;
				request.WorldScale = Vector3.one * 0.3f;
				request.HeldScale = Vector3.one * 0.2f;
				request.CoinThickness = 0.04f;
				request.CartGridSize = Vector2Int.one;
				request.CleaningRequirement = TreasureCleaningRequirement.Auto;
				if ( string.IsNullOrEmpty( request.Variant ) )
					request.Variant = "Gold";
				break;

			case TreasureCategory.Gem:
				request.ExclusiveCarry = false;
				request.UsesHeavyThrow = false;
				request.CanStack = false;
				request.RigidbodyMass = 0.25f;
				request.Drag = 0.5f;
				request.AngularDrag = 0.5f;
				request.AutoToppleStrength = 0f;
				request.PickupRadius = 0.3f;
				request.CollideWithPlayerOnPile = false;
				request.WorldScale = new Vector3( 0.25f, 0.2f, 0.25f );
				request.HeldScale = new Vector3( 0.08f, 0.06f, 0.08f );
				request.CoinThickness = 0.06f;
				request.CartGridSize = Vector2Int.one;
				request.CleaningRequirement = TreasureCleaningRequirement.Auto;
				break;

			case TreasureCategory.Key:
				request.ExclusiveCarry = true;
				request.UsesHeavyThrow = false;
				request.CanStack = false;
				request.RigidbodyMass = 0.1f;
				request.Drag = 0.6f;
				request.AngularDrag = 0.6f;
				request.AutoToppleStrength = 0f;
				request.PickupRadius = 0.35f;
				request.CollideWithPlayerOnPile = false;
				request.WorldScale = Vector3.one * 0.22f;
				request.HeldScale = Vector3.one * 0.18f;
				request.CoinThickness = 0.08f;
				request.CartGridSize = Vector2Int.one;
				request.Value = request.IsSkeletonKey ? 200 : 40;
				request.Weight = 1;
				request.CleaningRequirement = TreasureCleaningRequirement.NotRequired;
				if ( string.IsNullOrEmpty( request.Variant ) )
					request.Variant = request.IsSkeletonKey ? "Skeleton" : request.KeyType.ToString();
				break;

			case TreasureCategory.Chest:
				request.ExclusiveCarry = true;
				request.UsesHeavyThrow = true;
				request.CanStack = false;
				request.RigidbodyMass = 2f;
				request.Drag = 0.8f;
				request.AngularDrag = 0.8f;
				request.AutoToppleStrength = 0f;
				request.PickupRadius = 0.6f;
				request.CollideWithPlayerOnPile = true;
				request.WorldScale = Vector3.one * 0.55f;
				request.HeldScale = Vector3.one * 0.35f;
				request.CoinThickness = 0.4f;
				request.CartGridSize = new Vector2Int( 2, 2 );
				request.Value = 150;
				request.Weight = 10;
				request.CleaningRequirement = TreasureCleaningRequirement.NotRequired;
				if ( string.IsNullOrEmpty( request.Variant ) )
					request.Variant = request.ChestType;
				break;

			default:
				// Artifact / Crown / Goblet / Helmet
				request.ExclusiveCarry = false;
				request.CanStack = false;
				request.RigidbodyMass = 0.2f;
				request.Drag = 0.6f;
				request.AngularDrag = 0.6f;
				request.AutoToppleStrength = 0f;
				request.PickupRadius = 0.45f;
				request.CollideWithPlayerOnPile = false;
				request.WorldScale = Vector3.one * 0.26f;
				request.HeldScale = Vector3.one * 0.26f;
				request.CoinThickness = 0.156f;
				request.CartGridSize = new Vector2Int( 2, 2 );
				request.CleaningRequirement = TreasureCleaningRequirement.Auto;
				request.ConvertArtifactMaterials = true;
				break;
		}
	}

	public static void CopyFromDefinition( Request request, TreasureDefinition source )
	{
		if ( request == null || source == null )
			return;

		request.Id = source.id;
		request.DisplayName = source.displayName;
		request.Category = source.category;
		request.Variant = source.variant;
		request.Value = source.value;
		request.Weight = source.weight;
		request.ExclusiveCarry = source.exclusiveCarry;
		request.UsesHeavyThrow = source.usesHeavyThrow;
		request.CannotThrow = source.cannotThrow;
		request.PlaceOnGroundOrArtifactSlotOnly = source.placeOnGroundOrArtifactSlotOnly;
		request.ThrowForceScale = source.throwForceScale;
		request.ThrowUpBiasScale = source.throwUpBiasScale;
		request.RigidbodyMass = source.rigidbodyMass;
		request.Drag = source.drag;
		request.AngularDrag = source.angularDrag;
		request.SleepThreshold = source.sleepThreshold;
		request.AutoToppleStrength = source.autoToppleStrength;
		request.MaxUprightAngle = source.maxUprightAngle;
		request.PhysicsStabilizationDelay = source.physicsStabilizationDelay;
		request.PickupRadius = source.pickupRadius;
		request.CollideWithPlayerOnPile = source.collideWithPlayerOnPile;
		request.KeyType = source.keyType;
		request.IsSkeletonKey = source.isSkeletonKey;
		request.Icon = source.icon;
		request.MeshOverride = source.meshOverride;
		request.MaterialOverride = source.materialOverride;
		request.WorldScale = source.worldScale;
		request.HeldScale = source.heldScale;
		request.ActiveHeldLocalEuler = source.activeHeldLocalEuler;
		request.HeldStackLocalEuler = source.heldStackLocalEuler;
		request.CleaningRequirement = source.cleaningRequirement;
		request.CanStack = source.canStack;
		request.CoinThickness = source.coinThickness;
		request.CartGridSize = source.cartGridSize;
		request.PickupClips = source.pickupClips;
		request.PickupVolumeMin = source.pickupVolumeMin;
		request.PickupVolumeMax = source.pickupVolumeMax;
		request.PickupPitchMin = source.pickupPitchMin;
		request.PickupPitchMax = source.pickupPitchMax;
		request.PlaceClips = source.placeClips;
		request.PlaceVolumeMin = source.placeVolumeMin;
		request.PlaceVolumeMax = source.placeVolumeMax;
		request.PlacePitchMin = source.placePitchMin;
		request.PlacePitchMax = source.placePitchMax;

		if ( source.chestDefinition != null )
		{
			ChestDefinition chest = source.chestDefinition;
			request.ChestType = chest.chestType;
			request.ChestStartsLocked = chest.startsLocked;
			request.ChestDestroyOnOpen = chest.destroyOnOpen;
			request.ChestLockpickDuration = chest.lockpickDuration;
			request.ChestContents = chest.contents;
		}

		if ( source.prefab != null && source.prefab.RuntimeKeyIsValid() )
		{
			string guid = source.prefab.AssetGUID;
			string path = AssetDatabase.GUIDToAssetPath( guid );
			GameObject visual = AssetDatabase.LoadAssetAtPath<GameObject>( path );
			if ( visual != null )
			{
				request.VisualMode = VisualSourceMode.UseExistingVisual;
				request.ExistingVisualPrefab = visual;
			}
		}
	}

	public static string Validate( Request request )
	{
		if ( request == null )
			return "Request is null.";

		string id = SanitizeId( request.Id );
		if ( string.IsNullOrEmpty( id ) )
			return "Id is required (letters, digits, underscore).";

		switch ( request.VisualMode )
		{
			case VisualSourceMode.GenerateFromPrefab:
				if ( request.SourcePrefab == null )
					return "Source prefab is required.";
				break;
			case VisualSourceMode.GenerateFromMesh:
				if ( request.SourceMesh == null )
					return "Source mesh is required.";
				break;
			case VisualSourceMode.UseExistingVisual:
				if ( request.ExistingVisualPrefab == null )
					return "Existing visual prefab is required.";
				break;
		}

		if ( request.Category == TreasureCategory.Chest && string.IsNullOrEmpty( request.ChestType ) )
			return "Chest type is required for chests.";

		return null;
	}

	public static Result CreateOrUpdate( Request request, bool overwriteConfirmed )
	{
		var result = new Result();
		string error = Validate( request );
		if ( error != null )
		{
			result.Error = error;
			return result;
		}

		string id = SanitizeId( request.Id );
		request.Id = id;
		if ( string.IsNullOrEmpty( request.DisplayName ) )
			request.DisplayName = id;

		string visualPath = string.IsNullOrEmpty( request.VisualPathOverride )
			? DefaultVisualPath( id, request.Category )
			: request.VisualPathOverride;
		string defPath = string.IsNullOrEmpty( request.DefinitionPathOverride )
			? DefaultDefinitionPath( id, request.Category )
			: request.DefinitionPathOverride;
		string chestDefPath = string.IsNullOrEmpty( request.ChestDefinitionPathOverride )
			? DefaultChestDefinitionPath( id )
			: request.ChestDefinitionPathOverride;

		bool visualExists = AssetDatabase.LoadAssetAtPath<GameObject>( visualPath ) != null
			&& request.VisualMode != VisualSourceMode.UseExistingVisual;
		bool defExists = AssetDatabase.LoadAssetAtPath<TreasureDefinition>( defPath ) != null;
		if ( ( visualExists || defExists ) && !overwriteConfirmed )
		{
			result.Error = "Assets already exist. Confirm overwrite to continue.";
			result.VisualPath = visualPath;
			result.DefinitionPath = defPath;
			return result;
		}

		string visualParent = Path.GetDirectoryName( visualPath );
		string defParent = Path.GetDirectoryName( defPath );
		if ( !string.IsNullOrEmpty( visualParent ) )
			AddressableEditorUtil.EnsureFolder( visualParent.Replace( '\\', '/' ) );
		if ( !string.IsNullOrEmpty( defParent ) )
			AddressableEditorUtil.EnsureFolder( defParent.Replace( '\\', '/' ) );

		string visualGuid = null;
		switch ( request.VisualMode )
		{
			case VisualSourceMode.GenerateFromPrefab:
				visualGuid = TreasureVisualPrefabBuilder.CreateOrUpdateFromPrefab( request.SourcePrefab, visualPath );
				break;
			case VisualSourceMode.GenerateFromMesh:
				visualGuid = TreasureVisualPrefabBuilder.CreateOrUpdateFromMesh(
					request.SourceMesh,
					request.SourceMaterials,
					visualPath );
				break;
			case VisualSourceMode.UseExistingVisual:
				visualPath = AssetDatabase.GetAssetPath( request.ExistingVisualPrefab );
				visualGuid = AssetDatabase.AssetPathToGUID( visualPath );
				break;
		}

		if ( string.IsNullOrEmpty( visualGuid ) )
		{
			result.Error = "Failed to create or resolve visual prefab.";
			return result;
		}

		AddressableEditorUtil.TryRegister( visualPath, visualPath );

		if ( request.Category == TreasureCategory.Gem && request.GemMaterial != null )
			ApplyGemMaterial( visualPath, request.GemMaterial, request.CloneGemMaterial, id );

		bool isArtifactLike = request.Category == TreasureCategory.Artifact
			|| request.Category == TreasureCategory.Crown
			|| request.Category == TreasureCategory.Goblet
			|| request.Category == TreasureCategory.Helmet;
		if ( isArtifactLike && request.ConvertArtifactMaterials
			&& request.VisualMode != VisualSourceMode.UseExistingVisual )
			ArtifactMaterialInstaller.ConvertPrefabMaterials( visualPath );

		ChestDefinition chestDef = null;
		if ( request.Category == TreasureCategory.Chest )
		{
			AddressableEditorUtil.EnsureFolder( ChestDefsFolder );
			chestDef = CreateOrUpdateChestDefinition( request, chestDefPath );
			result.ChestDefinitionPath = chestDefPath;
		}

		TreasureDefinition def = CreateOrUpdateDefinition( request, defPath, visualGuid, chestDef );
		bool registerDef = request.Category == TreasureCategory.Key || request.Category == TreasureCategory.Chest;
		if ( registerDef )
			AddressableEditorUtil.TryRegister( defPath, Path.GetFileNameWithoutExtension( defPath ), "Definition" );

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();

		result.Success = true;
		result.VisualPath = visualPath;
		result.DefinitionPath = defPath;
		result.Definition = def;
		result.Summary = "Created " + id + " (" + request.Category + ")\nDefinition: " + defPath + "\nVisual: " + visualPath
			+ ( chestDef != null ? "\nChestDef: " + chestDefPath : string.Empty );
		return result;
	}

	static TreasureDefinition CreateOrUpdateDefinition(
		Request request,
		string defPath,
		string visualGuid,
		ChestDefinition chestDef )
	{
		TreasureDefinition def = AssetDatabase.LoadAssetAtPath<TreasureDefinition>( defPath );
		if ( def == null )
		{
			def = ScriptableObject.CreateInstance<TreasureDefinition>();
			AssetDatabase.CreateAsset( def, defPath );
		}

		def.id = request.Id;
		def.displayName = request.DisplayName;
		def.category = request.Category;
		def.variant = request.Variant;
		def.value = request.Value;
		def.weight = Mathf.Max( 1, request.Weight );
		def.exclusiveCarry = request.ExclusiveCarry;
		def.usesHeavyThrow = request.UsesHeavyThrow;
		def.cannotThrow = request.CannotThrow;
		def.placeOnGroundOrArtifactSlotOnly = request.PlaceOnGroundOrArtifactSlotOnly;
		def.throwForceScale = request.ThrowForceScale;
		def.throwUpBiasScale = request.ThrowUpBiasScale;
		def.rigidbodyMass = request.RigidbodyMass;
		def.drag = request.Drag;
		def.angularDrag = request.AngularDrag;
		def.sleepThreshold = request.SleepThreshold;
		def.autoToppleStrength = request.AutoToppleStrength;
		def.maxUprightAngle = request.MaxUprightAngle;
		def.physicsStabilizationDelay = request.PhysicsStabilizationDelay;
		def.pickupRadius = request.PickupRadius;
		def.collideWithPlayerOnPile = request.CollideWithPlayerOnPile;
		def.keyType = request.KeyType;
		def.isSkeletonKey = request.IsSkeletonKey;
		def.chestDefinition = chestDef;
		def.icon = request.Icon;
		def.meshOverride = request.MeshOverride;
		def.materialOverride = request.MaterialOverride;
		def.prefab = new AssetReferenceGameObject( visualGuid );
		def.worldScale = request.WorldScale;
		def.heldScale = request.HeldScale;
		def.activeHeldLocalEuler = request.ActiveHeldLocalEuler;
		def.heldStackLocalEuler = request.HeldStackLocalEuler;
		def.cleaningRequirement = request.CleaningRequirement;
		def.canStack = request.CanStack;
		def.coinThickness = request.CoinThickness;
		def.cartGridSize = request.CartGridSize;
		def.pickupClips = request.PickupClips;
		def.pickupVolumeMin = request.PickupVolumeMin;
		def.pickupVolumeMax = request.PickupVolumeMax;
		def.pickupPitchMin = request.PickupPitchMin;
		def.pickupPitchMax = request.PickupPitchMax;
		def.placeClips = request.PlaceClips;
		def.placeVolumeMin = request.PlaceVolumeMin;
		def.placeVolumeMax = request.PlaceVolumeMax;
		def.placePitchMin = request.PlacePitchMin;
		def.placePitchMax = request.PlacePitchMax;
		def.EnsurePhysicsDefaults();

		EditorUtility.SetDirty( def );
		return def;
	}

	static ChestDefinition CreateOrUpdateChestDefinition( Request request, string path )
	{
		ChestDefinition def = AssetDatabase.LoadAssetAtPath<ChestDefinition>( path );
		if ( def == null )
		{
			def = ScriptableObject.CreateInstance<ChestDefinition>();
			AssetDatabase.CreateAsset( def, path );
		}

		def.id = request.Id;
		def.displayName = request.DisplayName;
		def.chestType = request.ChestType;
		def.keyType = request.KeyType;
		def.startsLocked = request.ChestStartsLocked;
		def.destroyOnOpen = request.ChestDestroyOnOpen;
		def.lockpickDuration = request.ChestLockpickDuration;
		def.contents = request.ChestContents ?? Array.Empty<ChestContentEntry>();

		EditorUtility.SetDirty( def );
		return def;
	}

	static void ApplyGemMaterial( string visualPath, Material gemMaterial, bool clone, string id )
	{
		Material toAssign = gemMaterial;
		if ( clone && gemMaterial != null )
		{
			string matFolder = "Assets/Materials/Shaders/Gem";
			AddressableEditorUtil.EnsureFolder( matFolder );
			string matPath = matFolder + "/M_Gem_" + id + ".mat";
			Material existing = AssetDatabase.LoadAssetAtPath<Material>( matPath );
			if ( existing == null )
			{
				toAssign = new Material( gemMaterial );
				toAssign.name = "M_Gem_" + id;
				AssetDatabase.CreateAsset( toAssign, matPath );
			}
			else
			{
				EditorUtility.CopySerialized( gemMaterial, existing );
				toAssign = existing;
				EditorUtility.SetDirty( existing );
			}
		}

		GameObject root = PrefabUtility.LoadPrefabContents( visualPath );
		if ( root == null )
			return;

		try
		{
			Renderer[] renderers = root.GetComponentsInChildren<Renderer>( true );
			for ( int i = 0; i < renderers.Length; i++ )
			{
				if ( renderers[ i ] == null )
					continue;
				Material[] mats = renderers[ i ].sharedMaterials;
				if ( mats == null || mats.Length == 0 )
				{
					renderers[ i ].sharedMaterial = toAssign;
					continue;
				}

				for ( int m = 0; m < mats.Length; m++ )
					mats[ m ] = toAssign;
				renderers[ i ].sharedMaterials = mats;
			}

			PrefabUtility.SaveAsPrefabAsset( root, visualPath );
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents( root );
		}
	}
}
#endif
