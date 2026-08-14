using System;
using System.Collections.Generic;

using UnityEngine;

public class DebugCarrySection : DebugOverlaySection
{
	static int s_fillAmount = 1;
	static int s_giveAmount = 1;
	static int s_assortmentAmount = 10;
	static int s_selectedIndex;
	static int s_categoryFilter = -1;
	static string s_lastStatus = "";

	static List<TreasureDefinition> s_catalog;
	static readonly List<TreasureDefinition> s_filtered = new List<TreasureDefinition>();
	static readonly List<TreasureDefinition> s_pool = new List<TreasureDefinition>();

	public string Title => "Carry";

	public void Draw()
	{
		PlayerController player = DebugOverlay.GetPlayer();
		PlayerCarry carry = player != null ? player.Carry : null;
		if ( carry == null )
		{
			GUILayout.Label( "No carry" );
			return;
		}

		GUILayout.Label( $"Weight: {carry.UsedWeight} (burden {carry.CarryBurden01:P0}, move x{carry.MoveSpeedMultiplier:0.##})" );
		GUILayout.Label( $"Reference weight: {carry.MaxCarryWeight}" );
		GUILayout.Label( $"Selected: {carry.SelectedBucket}  Total: {carry.TotalCount}" );
		GUILayout.Label( $"Count: {carry.Count}  Held: {carry.HeldCount}" );
		GUILayout.Label( $"Has Active: {carry.HasActive}" );
		GUILayout.Label( $"Buckets C/G/A: {carry.GetBucketCount( CarryBucketKind.Coin )}/{carry.GetBucketCount( CarryBucketKind.Gem )}/{carry.GetBucketCount( CarryBucketKind.Artifact )}" );

		if ( carry.TryPeekActive( out TreasureDefinition def ) && def != null )
			GUILayout.Label( $"Active: {def.name}" );

		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Coins" ) )
			carry.TrySetSelectedBucket( CarryBucketKind.Coin );
		if ( GUILayout.Button( "Gems" ) )
			carry.TrySetSelectedBucket( CarryBucketKind.Gem );
		if ( GUILayout.Button( "Artifacts" ) )
			carry.TrySetSelectedBucket( CarryBucketKind.Artifact );
		GUILayout.EndHorizontal();

		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Clear Hands" ) )
			carry.Clear();
		if ( GUILayout.Button( "Cycle +" ) )
			carry.CycleActive( 1 );
		if ( GUILayout.Button( "Cycle -" ) )
			carry.CycleActive( -1 );
		GUILayout.EndHorizontal();

		GUILayout.Space( 4f );
		DrawGiveTreasure( carry );
		GUILayout.Space( 4f );
		DrawFillFromPile( player );

		if ( !string.IsNullOrEmpty( s_lastStatus ) )
			GUILayout.Label( s_lastStatus );
	}

	void DrawGiveTreasure( PlayerCarry carry )
	{
		EnsureCatalog();
		if ( s_catalog == null || s_catalog.Count == 0 )
		{
			GUILayout.Label( "No treasure definitions loaded" );
			return;
		}

		GUILayout.Label( "Give Treasure" );
		DrawCategoryFilter();
		BuildFiltered();
		if ( s_filtered.Count == 0 )
		{
			GUILayout.Label( "No items in filter" );
			return;
		}

		if ( s_selectedIndex >= s_filtered.Count )
			s_selectedIndex = 0;

		TreasureDefinition selected = s_filtered[ s_selectedIndex ];
		string selectedLabel = FormatLabel( selected );

		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "◀", GUILayout.Width( 28f ) ) )
			s_selectedIndex = ( s_selectedIndex + s_filtered.Count - 1 ) % s_filtered.Count;
		GUILayout.Label( selectedLabel, GUILayout.MinWidth( 80f ) );
		if ( GUILayout.Button( "▶", GUILayout.Width( 28f ) ) )
			s_selectedIndex = ( s_selectedIndex + 1 ) % s_filtered.Count;
		GUILayout.EndHorizontal();

		GUILayout.Label( $"Give Amount: {s_giveAmount}" );
		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "1" ) )
			s_giveAmount = 1;
		if ( GUILayout.Button( "5" ) )
			s_giveAmount = 5;
		if ( GUILayout.Button( "10" ) )
			s_giveAmount = 10;
		if ( GUILayout.Button( "50" ) )
			s_giveAmount = 50;
		GUILayout.EndHorizontal();
		s_giveAmount = Mathf.RoundToInt( GUILayout.HorizontalSlider( s_giveAmount, 1, 100 ) );

		if ( GUILayout.Button( $"Add {s_giveAmount}x {selectedLabel}" ) )
		{
			int added = TryAddMany( carry, selected, s_giveAmount );
			s_lastStatus = $"Added {added}/{s_giveAmount} {selectedLabel}";
		}

		GUILayout.Space( 2f );
		GUILayout.Label( $"Assortment Amount: {s_assortmentAmount}" );
		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "5" ) )
			s_assortmentAmount = 5;
		if ( GUILayout.Button( "10" ) )
			s_assortmentAmount = 10;
		if ( GUILayout.Button( "25" ) )
			s_assortmentAmount = 25;
		if ( GUILayout.Button( "50" ) )
			s_assortmentAmount = 50;
		GUILayout.EndHorizontal();
		s_assortmentAmount = Mathf.RoundToInt( GUILayout.HorizontalSlider( s_assortmentAmount, 1, 100 ) );

		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( $"Add {s_assortmentAmount} coins" ) )
		{
			int added = TryAddAssortment( carry, TreasureCategory.Coin, s_assortmentAmount );
			s_lastStatus = $"Added {added}/{s_assortmentAmount} mixed coins";
		}
		if ( GUILayout.Button( $"Add {s_assortmentAmount} gems" ) )
		{
			int added = TryAddAssortment( carry, TreasureCategory.Gem, s_assortmentAmount );
			s_lastStatus = $"Added {added}/{s_assortmentAmount} mixed gems";
		}
		GUILayout.EndHorizontal();
	}

	void DrawFillFromPile( PlayerController player )
	{
		GUILayout.Label( $"Fill Amount: {s_fillAmount}" );
		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "1" ) )
			s_fillAmount = 1;
		if ( GUILayout.Button( "5" ) )
			s_fillAmount = 5;
		if ( GUILayout.Button( "10" ) )
			s_fillAmount = 10;
		if ( GUILayout.Button( "Use Pile Take" ) )
			s_fillAmount = DebugGoldPileSection.FillAmount;
		GUILayout.EndHorizontal();
		s_fillAmount = Mathf.RoundToInt( GUILayout.HorizontalSlider( s_fillAmount, 1, 100 ) );

		if ( GUILayout.Button( $"Fill {s_fillAmount} from nearest pile" ) )
		{
			TreasurePileVisual pile = DebugGoldPileSection.FindNearestPileVisual( player.transform.position );
			if ( pile == null )
			{
				s_lastStatus = "No pile nearby";
			}
			else
			{
				Vector3 aim = pile.transform.position + Vector3.up * 0.5f;
				int taken = pile.DebugTakeUnits( s_fillAmount, aim, player, true );
				s_lastStatus = $"Filled {taken}/{s_fillAmount} from {pile.gameObject.name}";
			}
		}
	}

	static void DrawCategoryFilter()
	{
		GUILayout.BeginHorizontal();
		DrawFilterButton( "All", -1 );
		DrawFilterButton( "Coin", (int)TreasureCategory.Coin );
		DrawFilterButton( "Gem", (int)TreasureCategory.Gem );
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		DrawFilterButton( "Artifact", (int)TreasureCategory.Artifact );
		DrawFilterButton( "Crown", (int)TreasureCategory.Crown );
		DrawFilterButton( "Goblet", (int)TreasureCategory.Goblet );
		DrawFilterButton( "Helmet", (int)TreasureCategory.Helmet );
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		DrawFilterButton( "Key", (int)TreasureCategory.Key );
		DrawFilterButton( "Chest", (int)TreasureCategory.Chest );
		GUILayout.EndHorizontal();
	}

	static void DrawFilterButton( string label, int filter )
	{
		bool selected = s_categoryFilter == filter;
		if ( GUILayout.Toggle( selected, label, GUI.skin.button ) && !selected )
		{
			s_categoryFilter = filter;
			s_selectedIndex = 0;
		}
	}

	static void BuildFiltered()
	{
		s_filtered.Clear();
		for ( int i = 0; i < s_catalog.Count; i++ )
		{
			TreasureDefinition def = s_catalog[ i ];
			if ( def == null )
				continue;
			if ( s_categoryFilter >= 0 && (int)def.category != s_categoryFilter )
				continue;
			s_filtered.Add( def );
		}
	}

	static int TryAddMany( PlayerCarry carry, TreasureDefinition definition, int amount )
	{
		if ( carry == null || definition == null || amount <= 0 )
			return 0;

		int added = 0;
		for ( int i = 0; i < amount; i++ )
		{
			if ( !carry.TryAdd( definition ) )
				break;
			added++;
		}

		return added;
	}

	static int TryAddAssortment( PlayerCarry carry, TreasureCategory category, int amount )
	{
		if ( carry == null || amount <= 0 )
			return 0;

		EnsureCatalog();
		s_pool.Clear();
		for ( int i = 0; i < s_catalog.Count; i++ )
		{
			TreasureDefinition def = s_catalog[ i ];
			if ( def != null && def.category == category )
				s_pool.Add( def );
		}

		if ( s_pool.Count == 0 )
			return 0;

		int added = 0;
		for ( int i = 0; i < amount; i++ )
		{
			TreasureDefinition pick = s_pool[ UnityEngine.Random.Range( 0, s_pool.Count ) ];
			if ( !carry.TryAdd( pick ) )
				break;
			added++;
		}

		return added;
	}

	static void EnsureCatalog()
	{
		if ( s_catalog != null && s_catalog.Count > 0 )
			return;

		HashSet<TreasureDefinition> seen = new HashSet<TreasureDefinition>();
		s_catalog = new List<TreasureDefinition>();

#if UNITY_EDITOR
		string[] guids = UnityEditor.AssetDatabase.FindAssets( "t:TreasureDefinition" );
		for ( int i = 0; i < guids.Length; i++ )
		{
			string path = UnityEditor.AssetDatabase.GUIDToAssetPath( guids[ i ] );
			TreasureDefinition def = UnityEditor.AssetDatabase.LoadAssetAtPath<TreasureDefinition>( path );
			TryAddToCatalog( def, seen );
		}
#endif

		TreasureDefinition[] loaded = Resources.FindObjectsOfTypeAll<TreasureDefinition>();
		for ( int i = 0; i < loaded.Length; i++ )
			TryAddToCatalog( loaded[ i ], seen );

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

				TreasurePileDefinition pileDef = interactable.PileDefinition;
				AddEntriesToCatalog( pileDef.coinContents, seen );
				AddEntriesToCatalog( pileDef.treasureContents, seen );
			}
		}

		s_catalog.Sort( CompareDefs );
	}

	static void AddEntriesToCatalog( TreasurePileEntry[] contents, HashSet<TreasureDefinition> seen )
	{
		if ( contents == null )
			return;

		for ( int c = 0; c < contents.Length; c++ )
			TryAddToCatalog( contents[ c ].treasure, seen );
	}

	static void TryAddToCatalog( TreasureDefinition def, HashSet<TreasureDefinition> seen )
	{
		if ( def == null || !seen.Add( def ) )
			return;
		s_catalog.Add( def );
	}

	static int CompareDefs( TreasureDefinition a, TreasureDefinition b )
	{
		int cat = a.category.CompareTo( b.category );
		if ( cat != 0 )
			return cat;
		return string.Compare( FormatLabel( a ), FormatLabel( b ), StringComparison.OrdinalIgnoreCase );
	}

	static string FormatLabel( TreasureDefinition def )
	{
		if ( def == null )
			return "(null)";
		if ( !string.IsNullOrEmpty( def.displayName ) )
			return def.displayName;
		if ( !string.IsNullOrEmpty( def.id ) )
			return def.id;
		return def.name;
	}
}
