using System;
using System.Collections.Generic;

using UnityEngine;

public class DebugGoldPileSection : DebugOverlaySection
{
	static int s_carveAmount = 1;
	static int s_takeAmount = 1;
	static bool s_overrideGameplayPull;
	static int s_gameplayPullOverride = 1;
	static int s_selectedPile;
	static string s_lastStatus = "";
	static List<TreasureDefinition> s_artifactGemCatalog;

	public string Title => "Gold Pile";

	public void Draw()
	{
		IReadOnlyList<GoldPileLootStreamDebug> piles = GoldPileLootStreamDebug.ActiveInstances;
		if ( piles == null || piles.Count == 0 )
		{
			GUILayout.Label( "No gold piles registered" );
			return;
		}

		GUILayout.Label( $"Piles: {piles.Count}" );
		GoldPileEditTiming.Enabled = GUILayout.Toggle( GoldPileEditTiming.Enabled, "Log edit timings" );
		if ( GoldPileEditTiming.Enabled )
		{
			GoldPileEditTiming.Verbose = GUILayout.Toggle( GoldPileEditTiming.Verbose, "Verbose phase logs" );
			if ( !string.IsNullOrEmpty( GoldPileEditTiming.LastSummary ) )
				GUILayout.Label( GoldPileEditTiming.LastSummary );
		}
		DrawStreamingControls( piles );

		GUILayout.Space( 4f );
		DrawGameplayPullControls();
		DrawCarveAmountControls();
		DrawTakeAmountControls();

		if ( s_selectedPile >= piles.Count )
			s_selectedPile = 0;

		GUILayout.Label( $"Selected pile: {s_selectedPile}" );
		s_selectedPile = Mathf.RoundToInt( GUILayout.HorizontalSlider( s_selectedPile, 0, Mathf.Max( 0, piles.Count - 1 ) ) );

		GoldPileLootStreamDebug selectedDebug = piles[ s_selectedPile ];
		TreasurePileVisual visual = ResolveVisual( selectedDebug );
		if ( visual == null )
		{
			GUILayout.Label( "No TreasurePileVisual on selected pile" );
			return;
		}

		TreasurePileInteractable interactable = visual.GetComponent<TreasurePileInteractable>();
		if ( interactable == null )
			interactable = visual.GetComponentInParent<TreasurePileInteractable>();

		GUILayout.Label( $"Name: {visual.gameObject.name}" );
		GUILayout.Label( $"Remaining: {visual.TotalRemainingLoot}" );
		GUILayout.Label( $"Carve R: {visual.CarveRadius:0.00}" );
		GoldPileCarveSettings globalCarve = GoldPileCarveSettings.FromGlobalDefinition().ResolvedForPile(
			visual.Heightfield != null && visual.Heightfield.IsInitialized
				? visual.Heightfield.WorldSize
				: 6f );
		GUILayout.Label(
			$"Global carve R:{globalCarve.radius:0.00} " +
			$"Blur pad:{globalCarve.blurPadCells} x{globalCarve.blurPasses} str:{globalCarve.blurStrength:0.##} fall:{globalCarve.falloffSharpness:0.##}" );
		if ( interactable != null )
			GUILayout.Label( $"Interactable Remaining: {interactable.RemainingCount}" );

		Vector3 aim = ResolveAimPoint( visual );

		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( $"Carve {s_carveAmount}" ) )
		{
			bool ok = visual.TryDebugCarveAmount( aim, s_carveAmount, globalCarve );
			s_lastStatus = ok ? $"Carved {s_carveAmount} at aim" : "Carve failed";
		}
		if ( GUILayout.Button( $"Deposit {s_carveAmount}" ) )
		{
			bool ok = visual.TryDebugDepositAmount( aim, s_carveAmount, globalCarve );
			s_lastStatus = ok ? $"Deposited {s_carveAmount} at aim" : "Deposit failed";
		}
		GUILayout.EndHorizontal();

		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( $"Take {s_takeAmount} (no carry)" ) )
		{
			PlayerController player = DebugOverlay.GetPlayer();
			int taken = visual.DebugTakeUnits( s_takeAmount, aim, player, false );
			s_lastStatus = $"Took {taken}/{s_takeAmount} (consume+carve)";
		}
		if ( GUILayout.Button( $"Take {s_takeAmount} into hands" ) )
		{
			PlayerController player = DebugOverlay.GetPlayer();
			int taken = visual.DebugTakeUnits( s_takeAmount, aim, player, true );
			s_lastStatus = $"Took {taken}/{s_takeAmount} into hands";
		}
		GUILayout.EndHorizontal();

		if ( GUILayout.Button( "Sync Remaining" ) )
		{
			if ( interactable != null )
			{
				interactable.SyncRemainingFromVisual();
				s_lastStatus = $"Synced remaining = {interactable.RemainingCount}";
			}
			else
			{
				s_lastStatus = "No TreasurePileInteractable";
			}
		}

		GUILayout.Space( 4f );
		DrawArtifactGemSpawnControls( visual );

		if ( !string.IsNullOrEmpty( s_lastStatus ) )
			GUILayout.Label( s_lastStatus );

		GUILayout.Space( 4f );
		for ( int i = 0; i < piles.Count; i++ )
		{
			GoldPileLootStreamDebug pile = piles[ i ];
			if ( pile == null )
				continue;
			TreasurePileVisual v = ResolveVisual( pile );
			int remaining = v != null ? v.TotalRemainingLoot : -1;
			GUILayout.Label( $"[{i}] {pile.gameObject.name}: rem={remaining} stream={( pile.StreamingEnabled ? "ON" : "OFF" )} loot={( pile.LootEnabled ? "ON" : "OFF" )}" );
		}
	}

	static void DrawStreamingControls( IReadOnlyList<GoldPileLootStreamDebug> piles )
	{
		if ( GUILayout.Button( "Toggle Streaming (all)" ) )
		{
			for ( int i = 0; i < piles.Count; i++ )
			{
				if ( piles[ i ] != null )
					piles[ i ].ToggleStreaming();
			}
		}

		if ( GUILayout.Button( "Toggle Loot Instances (all)" ) )
		{
			for ( int i = 0; i < piles.Count; i++ )
			{
				if ( piles[ i ] != null )
					piles[ i ].ToggleLoot();
			}
		}

		bool anyOverlay = false;
		for ( int i = 0; i < piles.Count; i++ )
		{
			if ( piles[ i ] != null && piles[ i ].DrawOverlay )
			{
				anyOverlay = true;
				break;
			}
		}

		bool nextOverlay = GUILayout.Toggle( anyOverlay, "Draw Overlay Stats" );
		if ( nextOverlay != anyOverlay )
		{
			for ( int i = 0; i < piles.Count; i++ )
			{
				if ( piles[ i ] != null )
					piles[ i ].SetDrawOverlay( nextOverlay );
			}
		}
	}

	static void DrawGameplayPullControls()
	{
		PlayerController player = DebugOverlay.GetPlayer();
		int playerPull = TreasurePilePull.DefaultUnitsPerInteract;
		if ( player != null && player.PilePull != null )
			playerPull = player.PilePull.UnitsPerInteract;

		int effective = s_overrideGameplayPull
			? s_gameplayPullOverride
			: TreasurePilePull.ResolveUnitsPerInteract( player );

		GUILayout.Label( $"Gameplay pull / interact: {effective}  (player: {playerPull})" );
		s_overrideGameplayPull = GUILayout.Toggle(
			s_overrideGameplayPull,
			"Override gameplay pull amount" );
		if ( s_overrideGameplayPull )
		{
			GUILayout.BeginHorizontal();
			if ( GUILayout.Button( "1" ) )
				s_gameplayPullOverride = 1;
			if ( GUILayout.Button( "5" ) )
				s_gameplayPullOverride = 5;
			if ( GUILayout.Button( "10" ) )
				s_gameplayPullOverride = 10;
			GUILayout.EndHorizontal();
			s_gameplayPullOverride = Mathf.RoundToInt( GUILayout.HorizontalSlider( s_gameplayPullOverride, 1, 100 ) );
		}
	}

	public static bool TryGetGameplayPullOverride( out int units )
	{
		if ( s_overrideGameplayPull && s_gameplayPullOverride > 0 )
		{
			units = s_gameplayPullOverride;
			return true;
		}

		units = 0;
		return false;
	}

	/// <summary>Debug carve/deposit slider. Values above 1 also drive multi-unit pile grabs.</summary>
	public static int CarveAmount => Mathf.Max( 1, s_carveAmount );

	static void DrawCarveAmountControls()
	{
		GUILayout.Label( $"Debug carve/deposit count: {s_carveAmount}  (grab uses this when > 1)" );
		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "1" ) )
			s_carveAmount = 1;
		if ( GUILayout.Button( "10" ) )
			s_carveAmount = 10;
		if ( GUILayout.Button( "50" ) )
			s_carveAmount = 50;
		if ( GUILayout.Button( "100" ) )
			s_carveAmount = 100;
		GUILayout.EndHorizontal();
		s_carveAmount = Mathf.RoundToInt( GUILayout.HorizontalSlider( s_carveAmount, 1, 500 ) );
	}

	static void DrawTakeAmountControls()
	{
		GUILayout.Space( 4f );
		GUILayout.Label( $"Debug take count: {s_takeAmount}" );
		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "1" ) )
			s_takeAmount = 1;
		if ( GUILayout.Button( "10" ) )
			s_takeAmount = 10;
		if ( GUILayout.Button( "50" ) )
			s_takeAmount = 50;
		if ( GUILayout.Button( "100" ) )
			s_takeAmount = 100;
		GUILayout.EndHorizontal();
		s_takeAmount = Mathf.RoundToInt( GUILayout.HorizontalSlider( s_takeAmount, 1, 500 ) );
	}

	static void DrawArtifactGemSpawnControls( TreasurePileVisual visual )
	{
		GoldPileArtifactProps props = visual != null ? visual.ArtifactProps : null;
		int latent = props != null ? props.LatentCount : 0;
		int live = props != null ? props.LivePropCount : 0;
		GUILayout.Label( $"Artifacts/gems: latent={latent} live={live}" );

		if ( GUILayout.Button( "Spawn existing artifacts/gems" ) )
		{
			if ( visual == null || props == null )
			{
				s_lastStatus = "No ArtifactProps on selected pile";
			}
			else
			{
				s_lastStatus = "Force-spawning existing artifacts/gems...";
				visual.DebugForceSpawnExistingArtifactsAndGems( ( spawned, attempted ) =>
				{
					s_lastStatus = $"Spawned existing {spawned}/{attempted} artifacts/gems";
				} );
			}
		}

		EnsureArtifactGemCatalog();
		int catalogCount = s_artifactGemCatalog != null ? s_artifactGemCatalog.Count : 0;
		if ( GUILayout.Button( $"Spawn all artifacts & gems inside ({catalogCount})" ) )
		{
			if ( visual == null || props == null )
			{
				s_lastStatus = "No ArtifactProps on selected pile";
			}
			else if ( catalogCount == 0 )
			{
				s_lastStatus = "No gem/artifact definitions found";
			}
			else
			{
				s_lastStatus = $"Spawning {catalogCount} artifacts/gems inside pile...";
				visual.DebugSpawnArtifactsAndGemsInside( s_artifactGemCatalog, ( spawned, attempted ) =>
				{
					s_lastStatus = $"Spawned {spawned}/{attempted} artifacts/gems inside pile";
				} );
			}
		}
	}

	static void EnsureArtifactGemCatalog()
	{
		if ( s_artifactGemCatalog != null && s_artifactGemCatalog.Count > 0 )
			return;

		HashSet<TreasureDefinition> seen = new HashSet<TreasureDefinition>();
		s_artifactGemCatalog = new List<TreasureDefinition>();

#if UNITY_EDITOR
		string[] guids = UnityEditor.AssetDatabase.FindAssets( "t:TreasureDefinition" );
		for ( int i = 0; i < guids.Length; i++ )
		{
			string path = UnityEditor.AssetDatabase.GUIDToAssetPath( guids[ i ] );
			TreasureDefinition def = UnityEditor.AssetDatabase.LoadAssetAtPath<TreasureDefinition>( path );
			TryAddArtifactOrGem( def, seen );
		}
#endif

		TreasureDefinition[] loaded = Resources.FindObjectsOfTypeAll<TreasureDefinition>();
		for ( int i = 0; i < loaded.Length; i++ )
			TryAddArtifactOrGem( loaded[ i ], seen );

		IReadOnlyList<GoldPileLootStreamDebug> piles = GoldPileLootStreamDebug.ActiveInstances;
		if ( piles != null )
		{
			for ( int i = 0; i < piles.Count; i++ )
			{
				GoldPileLootStreamDebug pile = piles[ i ];
				if ( pile == null )
					continue;

				TreasurePileInteractable interactable = pile.GetComponent<TreasurePileInteractable>();
				if ( interactable == null )
					interactable = pile.GetComponentInParent<TreasurePileInteractable>();
				if ( interactable == null || interactable.PileDefinition == null )
					continue;

				AddTreasureEntries( interactable.PileDefinition.treasureContents, seen );
			}
		}

		s_artifactGemCatalog.Sort( CompareDefs );
	}

	static void AddTreasureEntries( TreasurePileEntry[] contents, HashSet<TreasureDefinition> seen )
	{
		if ( contents == null )
			return;

		for ( int i = 0; i < contents.Length; i++ )
			TryAddArtifactOrGem( contents[ i ].treasure, seen );
	}

	static void TryAddArtifactOrGem( TreasureDefinition def, HashSet<TreasureDefinition> seen )
	{
		if ( def == null || !seen.Add( def ) )
			return;
		if ( def.category != TreasureCategory.Gem && def.category != TreasureCategory.Artifact )
			return;
		s_artifactGemCatalog.Add( def );
	}

	static int CompareDefs( TreasureDefinition a, TreasureDefinition b )
	{
		int cat = a.category.CompareTo( b.category );
		if ( cat != 0 )
			return cat;
		return string.Compare( FormatDefLabel( a ), FormatDefLabel( b ), StringComparison.OrdinalIgnoreCase );
	}

	static string FormatDefLabel( TreasureDefinition def )
	{
		if ( def == null )
			return "(null)";
		if ( !string.IsNullOrEmpty( def.displayName ) )
			return def.displayName;
		if ( !string.IsNullOrEmpty( def.id ) )
			return def.id;
		return def.name;
	}

	static TreasurePileVisual ResolveVisual( GoldPileLootStreamDebug debug )
	{
		if ( debug == null )
			return null;

		TreasurePileVisual visual = debug.GetComponent<TreasurePileVisual>();
		if ( visual != null )
			return visual;

		return debug.GetComponentInParent<TreasurePileVisual>();
	}

	static Vector3 ResolveAimPoint( TreasurePileVisual visual )
	{
		Vector3 fallback = visual.transform.position + Vector3.up * 0.5f;
		FirstPersonCameraController cameraLook = DebugOverlay.GetCameraLook();
		if ( cameraLook == null )
			return fallback;

		Ray ray = new Ray( cameraLook.transform.position, cameraLook.GetCameraForward() );
		RaycastHit[] hits = Physics.RaycastAll( ray, 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore );
		float bestDist = float.MaxValue;
		Vector3 best = fallback;
		bool found = false;

		for ( int i = 0; i < hits.Length; i++ )
		{
			RaycastHit hit = hits[ i ];
			if ( hit.collider == null )
				continue;

			Transform t = hit.collider.transform;
			if ( t != visual.transform && !t.IsChildOf( visual.transform ) )
				continue;

			if ( hit.distance < bestDist )
			{
				bestDist = hit.distance;
				best = hit.point;
				found = true;
			}
		}

		return found ? best : fallback;
	}

	public static TreasurePileVisual FindNearestPileVisual( Vector3 from )
	{
		IReadOnlyList<GoldPileLootStreamDebug> piles = GoldPileLootStreamDebug.ActiveInstances;
		if ( piles == null || piles.Count == 0 )
			return null;

		TreasurePileVisual best = null;
		float bestSqr = float.MaxValue;
		for ( int i = 0; i < piles.Count; i++ )
		{
			TreasurePileVisual visual = ResolveVisual( piles[ i ] );
			if ( visual == null )
				continue;

			float sqr = ( visual.transform.position - from ).sqrMagnitude;
			if ( sqr < bestSqr )
			{
				bestSqr = sqr;
				best = visual;
			}
		}

		return best;
	}

	public static int FillAmount => s_takeAmount;
}
