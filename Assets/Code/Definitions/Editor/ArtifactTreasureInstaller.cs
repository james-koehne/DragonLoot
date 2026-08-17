using System.Collections.Generic;
using System.IO;

using UnityEditor;
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

			string visualGuid = TreasureVisualPrefabBuilder.CreateOrUpdateFromPrefab( source, visualPath );
			if ( string.IsNullOrEmpty( visualGuid ) )
			{
				Debug.LogError( "ArtifactTreasureInstaller: failed visual for " + spec.DefName );
				continue;
			}

			AddressableEditorUtil.TryRegister( visualPath, visualPath );

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

	static void EnsureFolder( string path )
	{
		AddressableEditorUtil.EnsureFolder( path );
	}
}
