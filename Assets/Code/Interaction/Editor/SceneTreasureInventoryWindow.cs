#if UNITY_EDITOR
using System;
using System.Collections.Generic;

using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor overview of authored treasure in loaded scenes versus display capacity,
/// so you can see which types still need tables/slots.
/// Menu: DragonLoot → Treasure → Scene Inventory
/// </summary>
public class SceneTreasureInventoryWindow : EditorWindow
{
	enum SortColumn
	{
		Name,
		Category,
		SceneCount,
		DisplayCapacity,
		Shortfall
	}

	enum SourceKind
	{
		Pile,
		Authored,
		GroundCoverage,
		CoinStack
	}

	struct SourceHit
	{
		public SourceKind Kind;
		public string Label;
		public UnityEngine.Object Target;
		public int Count;
	}

	struct DisplayHit
	{
		public string Label;
		public UnityEngine.Object Target;
		public int Capacity;
		public bool Uncapped;
	}

	sealed class InventoryRow
	{
		public TreasureDefinition Definition;
		public int SceneCount;
		public int DisplayCapacity;
		public bool HasUncappedDisplay;
		public readonly List<SourceHit> Sources = new List<SourceHit>( 4 );
		public readonly List<DisplayHit> Displays = new List<DisplayHit>( 4 );

		public int Shortfall
		{
			get
			{
				if ( HasUncappedDisplay || SceneCount <= 0 )
					return 0;
				return Mathf.Max( 0, SceneCount - DisplayCapacity );
			}
		}
	}

	string _search = string.Empty;
	int _categoryFilterIndex;
	SortColumn _sortColumn = SortColumn.Shortfall;
	bool _sortAscending;
	bool _shortfallsOnly;
	bool _groupByCategory = true;
	bool _includeCoins = true;
	bool _includeNonCoins = true;

	Vector2 _scroll;
	readonly List<InventoryRow> _rows = new List<InventoryRow>( 64 );
	readonly Dictionary<TreasureDefinition, InventoryRow> _byDefinition =
		new Dictionary<TreasureDefinition, InventoryRow>();
	readonly List<DisplayHit> _mixedDisplays = new List<DisplayHit>( 8 );
	readonly List<string> _warnings = new List<string>( 8 );

	int _pileCount;
	int _coverageZoneCount;
	int _typedDisplayCount;
	int _artifactTableCount;
	int _mixedTableCount;
	int _totalSceneUnits;
	int _totalShortfallUnits;
	int _typesWithShortfall;

	[MenuItem( DragonLootMenus.TreasureSceneInventory )]
	public static void Open()
	{
		SceneTreasureInventoryWindow window = GetWindow<SceneTreasureInventoryWindow>( "Scene Treasure" );
		window.minSize = new Vector2( 640f, 420f );
		window.Show();
		window.Refresh();
	}

	void OnEnable()
	{
		UnityEditor.SceneManagement.EditorSceneManager.sceneOpened += OnSceneOpened;
		UnityEditor.SceneManagement.EditorSceneManager.sceneClosed += OnSceneClosed;
		Refresh();
	}

	void OnDisable()
	{
		UnityEditor.SceneManagement.EditorSceneManager.sceneOpened -= OnSceneOpened;
		UnityEditor.SceneManagement.EditorSceneManager.sceneClosed -= OnSceneClosed;
	}

	void OnFocus()
	{
		Refresh();
	}

	void OnSceneOpened( Scene scene, UnityEditor.SceneManagement.OpenSceneMode mode )
	{
		Refresh();
	}

	void OnSceneClosed( Scene scene )
	{
		Refresh();
	}

	void OnGUI()
	{
		DrawToolbar();
		DrawSummary();
		DrawFilters();
		DrawMixedDisplays();
		DrawWarnings();
		DrawTable();
	}

	void DrawToolbar()
	{
		EditorGUILayout.BeginHorizontal( EditorStyles.toolbar );
		if ( GUILayout.Button( "Refresh", EditorStyles.toolbarButton, GUILayout.Width( 70f ) ) )
			Refresh();
		GUILayout.FlexibleSpace();
		_shortfallsOnly = GUILayout.Toggle( _shortfallsOnly, "Shortfalls Only", EditorStyles.toolbarButton );
		_groupByCategory = GUILayout.Toggle( _groupByCategory, "Group By Category", EditorStyles.toolbarButton );
		EditorGUILayout.EndHorizontal();
	}

	void DrawSummary()
	{
		EditorGUILayout.Space( 4f );
		EditorGUILayout.LabelField( "Summary", EditorStyles.boldLabel );
		EditorGUILayout.LabelField(
			$"Piles {_pileCount}  ·  Coverage zones {_coverageZoneCount}  ·  Typed displays {_typedDisplayCount}  ·  Artifact tables {_artifactTableCount}  ·  Mixed {_mixedTableCount}" );
		EditorGUILayout.LabelField(
			$"Treasure types {_rows.Count}  ·  Scene units {_totalSceneUnits:N0}  ·  Types short {_typesWithShortfall}  ·  Units short {_totalShortfallUnits:N0}" );
		EditorGUILayout.HelpBox(
			"Scene counts come from pile definitions, authored pile props, ground coverage groups, and coin stacks. "
			+ "Display capacity sums typed tables / gold-bar tables / artifact presentation slots for that type. "
			+ "Mixed tables are shared and listed separately (not attributed per type).",
			MessageType.None );
	}

	void DrawFilters()
	{
		EditorGUILayout.BeginHorizontal();
		_search = EditorGUILayout.TextField( "Search", _search );
		_categoryFilterIndex = EditorGUILayout.Popup( "Category", _categoryFilterIndex, GetCategoryFilterLabels() );
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.BeginHorizontal();
		_includeCoins = EditorGUILayout.ToggleLeft( "Coins", _includeCoins, GUILayout.Width( 70f ) );
		_includeNonCoins = EditorGUILayout.ToggleLeft( "Non-coins", _includeNonCoins, GUILayout.Width( 90f ) );
		EditorGUILayout.EndHorizontal();
		EditorGUILayout.Space( 4f );
	}

	void DrawMixedDisplays()
	{
		if ( _mixedDisplays.Count == 0 )
			return;

		EditorGUILayout.LabelField( "Mixed Display Tables (shared)", EditorStyles.boldLabel );
		for ( int i = 0; i < _mixedDisplays.Count; i++ )
		{
			DisplayHit hit = _mixedDisplays[ i ];
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField( hit.Label, GUILayout.MinWidth( 180f ) );
			EditorGUILayout.LabelField(
				hit.Uncapped ? $"{hit.Capacity} slots (uncapped stack)" : $"{hit.Capacity} capacity",
				GUILayout.Width( 160f ) );
			if ( GUILayout.Button( "Ping", GUILayout.Width( 48f ) ) && hit.Target != null )
			{
				EditorGUIUtility.PingObject( hit.Target );
				Selection.activeObject = hit.Target;
			}
			EditorGUILayout.EndHorizontal();
		}

		EditorGUILayout.Space( 4f );
	}

	void DrawWarnings()
	{
		for ( int i = 0; i < _warnings.Count; i++ )
			EditorGUILayout.HelpBox( _warnings[ i ], MessageType.Warning );
	}

	void DrawTable()
	{
		DrawSortHeader();
		_scroll = EditorGUILayout.BeginScrollView( _scroll );

		List<InventoryRow> visible = BuildVisibleRows();
		if ( visible.Count == 0 )
		{
			EditorGUILayout.HelpBox( "No matching treasure in loaded scenes.", MessageType.Info );
			EditorGUILayout.EndScrollView();
			return;
		}

		TreasureCategory? lastCategory = null;
		for ( int i = 0; i < visible.Count; i++ )
		{
			InventoryRow row = visible[ i ];
			if ( _groupByCategory && row.Definition != null )
			{
				TreasureCategory category = row.Definition.category;
				if ( !lastCategory.HasValue || lastCategory.Value != category )
				{
					lastCategory = category;
					EditorGUILayout.Space( 4f );
					EditorGUILayout.LabelField( category.ToString(), EditorStyles.boldLabel );
				}
			}

			DrawRow( row );
		}

		EditorGUILayout.EndScrollView();
	}

	void DrawSortHeader()
	{
		EditorGUILayout.BeginHorizontal();
		DrawSortButton( "Name", SortColumn.Name, 160f );
		DrawSortButton( "Cat", SortColumn.Category, 70f );
		DrawSortButton( "Scene", SortColumn.SceneCount, 64f );
		DrawSortButton( "Display", SortColumn.DisplayCapacity, 72f );
		DrawSortButton( "Short", SortColumn.Shortfall, 56f );
		GUILayout.Label( "Sources / Displays", GUILayout.MinWidth( 120f ) );
		EditorGUILayout.EndHorizontal();
	}

	void DrawSortButton( string label, SortColumn column, float width )
	{
		string prefix = _sortColumn == column ? ( _sortAscending ? "▲ " : "▼ " ) : string.Empty;
		if ( GUILayout.Button( prefix + label, EditorStyles.miniButton, GUILayout.Width( width ) ) )
		{
			if ( _sortColumn == column )
				_sortAscending = !_sortAscending;
			else
			{
				_sortColumn = column;
				_sortAscending = column == SortColumn.Name || column == SortColumn.Category;
			}
		}
	}

	void DrawRow( InventoryRow row )
	{
		TreasureDefinition def = row.Definition;
		bool shortfall = row.Shortfall > 0;
		Color prev = GUI.backgroundColor;
		if ( shortfall )
			GUI.backgroundColor = new Color( 1f, 0.72f, 0.55f );

		EditorGUILayout.BeginVertical( EditorStyles.helpBox );
		EditorGUILayout.BeginHorizontal();

		Texture icon = def != null && def.icon != null ? def.icon.texture : null;
		GUILayout.Label( icon, GUILayout.Width( 20f ), GUILayout.Height( 20f ) );

		string name = GetDisplayName( def );
		EditorGUILayout.ObjectField( def, typeof( TreasureDefinition ), false, GUILayout.MinWidth( 140f ) );
		GUILayout.Label( def != null ? def.category.ToString() : "—", GUILayout.Width( 70f ) );
		GUILayout.Label( row.SceneCount.ToString( "N0" ), GUILayout.Width( 64f ) );
		GUILayout.Label(
			row.HasUncappedDisplay ? "∞" : row.DisplayCapacity.ToString( "N0" ),
			GUILayout.Width( 72f ) );
		GUILayout.Label( row.Shortfall.ToString( "N0" ), GUILayout.Width( 56f ) );

		if ( GUILayout.Button( "Focus", GUILayout.Width( 48f ) ) && def != null )
		{
			EditorGUIUtility.PingObject( def );
			Selection.activeObject = def;
		}

		EditorGUILayout.EndHorizontal();

		if ( !string.IsNullOrEmpty( name ) && def != null && name != def.name )
			EditorGUILayout.LabelField( name, EditorStyles.miniLabel );

		DrawSourceLine( row );
		DrawDisplayLine( row );

		if ( shortfall )
		{
			string hint = BuildShortfallHint( row );
			EditorGUILayout.LabelField( hint, EditorStyles.wordWrappedMiniLabel );
		}

		EditorGUILayout.EndVertical();
		GUI.backgroundColor = prev;
	}

	void DrawSourceLine( InventoryRow row )
	{
		if ( row.Sources.Count == 0 )
			return;

		EditorGUILayout.BeginHorizontal();
		EditorGUILayout.LabelField( "Sources", GUILayout.Width( 54f ) );
		for ( int i = 0; i < row.Sources.Count; i++ )
		{
			SourceHit source = row.Sources[ i ];
			string label = $"{source.Label} ×{source.Count}";
			if ( GUILayout.Button( label, EditorStyles.miniButton ) && source.Target != null )
			{
				EditorGUIUtility.PingObject( source.Target );
				Selection.activeObject = source.Target;
			}
		}
		EditorGUILayout.EndHorizontal();
	}

	void DrawDisplayLine( InventoryRow row )
	{
		if ( row.Displays.Count == 0 )
		{
			if ( row.SceneCount > 0 )
				EditorGUILayout.LabelField( "No typed / presentation display for this type.", EditorStyles.miniLabel );
			return;
		}

		EditorGUILayout.BeginHorizontal();
		EditorGUILayout.LabelField( "Displays", GUILayout.Width( 54f ) );
		for ( int i = 0; i < row.Displays.Count; i++ )
		{
			DisplayHit display = row.Displays[ i ];
			string cap = display.Uncapped ? "∞" : display.Capacity.ToString( "N0" );
			string label = $"{display.Label} ({cap})";
			if ( GUILayout.Button( label, EditorStyles.miniButton ) && display.Target != null )
			{
				EditorGUIUtility.PingObject( display.Target );
				Selection.activeObject = display.Target;
			}
		}
		EditorGUILayout.EndHorizontal();
	}

	static string BuildShortfallHint( InventoryRow row )
	{
		if ( row.Definition == null )
			return string.Empty;

		TreasureCategory category = row.Definition.category;
		int shortfall = row.Shortfall;
		switch ( category )
		{
			case TreasureCategory.Coin:
				return $"Need ~{shortfall:N0} more coin display capacity (typed coin table slots × max stack).";
			case TreasureCategory.Gem:
				return $"Need {shortfall:N0} more gem display slot(s) for this gem type.";
			case TreasureCategory.Artifact:
				if ( GoldBarStack.IsStackable( row.Definition ) )
					return $"Need ~{shortfall:N0} more gold-bar display capacity.";
				return $"Need {shortfall:N0} more artifact presentation slot(s) requiring this artifact.";
			default:
				return $"Need {shortfall:N0} more display capacity for this type (typed table, presentation, or mixed).";
		}
	}

	List<InventoryRow> BuildVisibleRows()
	{
		List<InventoryRow> visible = new List<InventoryRow>( _rows.Count );
		for ( int i = 0; i < _rows.Count; i++ )
		{
			InventoryRow row = _rows[ i ];
			if ( !PassesFilter( row ) )
				continue;
			visible.Add( row );
		}

		visible.Sort( CompareRows );
		if ( !_sortAscending )
			visible.Reverse();
		return visible;
	}

	bool PassesFilter( InventoryRow row )
	{
		if ( row == null || row.Definition == null )
			return false;

		if ( _shortfallsOnly && row.Shortfall <= 0 )
			return false;

		bool isCoin = row.Definition.category == TreasureCategory.Coin;
		if ( isCoin && !_includeCoins )
			return false;
		if ( !isCoin && !_includeNonCoins )
			return false;

		TreasureCategory? categoryFilter = GetActiveCategoryFilter();
		if ( categoryFilter.HasValue && row.Definition.category != categoryFilter.Value )
			return false;

		if ( string.IsNullOrEmpty( _search ) )
			return true;

		string needle = _search.Trim();
		if ( row.Definition.name.IndexOf( needle, StringComparison.OrdinalIgnoreCase ) >= 0 )
			return true;
		if ( !string.IsNullOrEmpty( row.Definition.displayName )
			&& row.Definition.displayName.IndexOf( needle, StringComparison.OrdinalIgnoreCase ) >= 0 )
			return true;
		if ( !string.IsNullOrEmpty( row.Definition.variant )
			&& row.Definition.variant.IndexOf( needle, StringComparison.OrdinalIgnoreCase ) >= 0 )
			return true;
		if ( !string.IsNullOrEmpty( row.Definition.id )
			&& row.Definition.id.IndexOf( needle, StringComparison.OrdinalIgnoreCase ) >= 0 )
			return true;
		return false;
	}

	int CompareRows( InventoryRow a, InventoryRow b )
	{
		int result = 0;
		switch ( _sortColumn )
		{
			case SortColumn.Name:
				result = string.Compare( GetDisplayName( a.Definition ), GetDisplayName( b.Definition ), StringComparison.OrdinalIgnoreCase );
				break;
			case SortColumn.Category:
				result = ( ( int )a.Definition.category ).CompareTo( ( int )b.Definition.category );
				if ( result == 0 )
					result = string.Compare( GetDisplayName( a.Definition ), GetDisplayName( b.Definition ), StringComparison.OrdinalIgnoreCase );
				break;
			case SortColumn.SceneCount:
				result = a.SceneCount.CompareTo( b.SceneCount );
				break;
			case SortColumn.DisplayCapacity:
				result = CompareCapacity( a, b );
				break;
			case SortColumn.Shortfall:
				result = a.Shortfall.CompareTo( b.Shortfall );
				break;
		}

		return result != 0 ? result : string.Compare( GetDisplayName( a.Definition ), GetDisplayName( b.Definition ), StringComparison.OrdinalIgnoreCase );
	}

	static int CompareCapacity( InventoryRow a, InventoryRow b )
	{
		if ( a.HasUncappedDisplay != b.HasUncappedDisplay )
			return a.HasUncappedDisplay ? 1 : -1;
		return a.DisplayCapacity.CompareTo( b.DisplayCapacity );
	}

	void Refresh()
	{
		_rows.Clear();
		_byDefinition.Clear();
		_mixedDisplays.Clear();
		_warnings.Clear();
		_pileCount = 0;
		_coverageZoneCount = 0;
		_typedDisplayCount = 0;
		_artifactTableCount = 0;
		_mixedTableCount = 0;
		_totalSceneUnits = 0;
		_totalShortfallUnits = 0;
		_typesWithShortfall = 0;

		ForEachInLoadedScenes<TreasurePileInteractable>( CollectPile );
		ForEachInLoadedScenes<TreasureGroundCoverageZone>( CollectCoverageZone );
		ForEachInLoadedScenes<CoinStackInteractable>( CollectCoinStack );

		ForEachInLoadedScenes<CoinDisplayTableInteractable>( CollectTypedDisplay );
		ForEachInLoadedScenes<GemDisplayTableInteractable>( CollectTypedDisplay );
		ForEachInLoadedScenes<GoldBarDisplayTableInteractable>( CollectTypedDisplay );
		ForEachInLoadedScenes<ArtifactPresentationTableInteractable>( CollectArtifactTable );
		ForEachInLoadedScenes<MixedDisplayTableInteractable>( CollectMixedDisplay );

		_rows.Clear();
		foreach ( KeyValuePair<TreasureDefinition, InventoryRow> pair in _byDefinition )
			_rows.Add( pair.Value );

		for ( int i = 0; i < _rows.Count; i++ )
		{
			InventoryRow row = _rows[ i ];
			_totalSceneUnits += row.SceneCount;
			if ( row.Shortfall > 0 )
			{
				_typesWithShortfall++;
				_totalShortfallUnits += row.Shortfall;
			}
		}

		Repaint();
	}

	void CollectPile( TreasurePileInteractable pile )
	{
		if ( pile == null )
			return;

		_pileCount++;
		TreasurePileDefinition def = pile.PileDefinition;
		if ( def != null )
		{
			AddEntries( def.coinContents, SourceKind.Pile, pile.name, pile );
			AddEntries( def.treasureContents, SourceKind.Pile, pile.name, pile );
		}

		TreasurePileVisual visual = pile.PileVisual;
		if ( visual == null )
			visual = pile.GetComponent<TreasurePileVisual>();
		if ( visual == null )
			return;

		Transform root = visual.FindAuthoredLootRoot();
		if ( root == null )
			return;

		TreasurePileAuthoredItem[] items = root.GetComponentsInChildren<TreasurePileAuthoredItem>( true );
		for ( int i = 0; i < items.Length; i++ )
		{
			TreasurePileAuthoredItem authored = items[ i ];
			if ( authored == null || authored.Definition == null )
				continue;
			if ( !TreasurePileAuthoredItem.IsCuratable( authored.Definition ) )
				continue;

			AddCount( authored.Definition, 1, SourceKind.Authored, pile.name + "/Authored", authored );
		}
	}

	void CollectCoverageZone( TreasureGroundCoverageZone zone )
	{
		if ( zone == null )
			return;

		_coverageZoneCount++;
		TreasureGroupDefinition group = zone.Group;
		if ( group == null || group.entries == null )
		{
			_warnings.Add( $"Coverage zone '{zone.name}' has no TreasureGroupDefinition." );
			return;
		}

		for ( int i = 0; i < group.entries.Length; i++ )
		{
			TreasureGroupEntry entry = group.entries[ i ];
			if ( entry.treasure == null || entry.count <= 0 )
				continue;

			AddCount( entry.treasure, entry.count, SourceKind.GroundCoverage, zone.name, zone );
		}
	}

	void CollectCoinStack( CoinStackInteractable stack )
	{
		if ( stack == null || stack.Treasure == null )
			return;

		int amount = stack.RemainingCount;
		if ( amount <= 0 )
			amount = stack.TotalCount;
		if ( amount <= 0 )
			return;

		AddCount( stack.Treasure, amount, SourceKind.CoinStack, stack.name, stack );
	}

	void CollectTypedDisplay( TypedDisplayTableInteractable table )
	{
		if ( table == null )
			return;

		_typedDisplayCount++;
		TreasureDefinition accepted = table.AcceptedTreasure;
		if ( accepted == null )
		{
			_warnings.Add( $"Display '{table.name}' has no accepted treasure." );
			return;
		}

		ResolveTypedCapacity( table, out int capacity, out bool uncapped );
		InventoryRow row = GetOrCreateRow( accepted );
		row.DisplayCapacity += capacity;
		if ( uncapped )
			row.HasUncappedDisplay = true;

		row.Displays.Add( new DisplayHit
		{
			Label = table.name,
			Target = table,
			Capacity = capacity,
			Uncapped = uncapped
		} );
	}

	void CollectArtifactTable( ArtifactPresentationTableInteractable table )
	{
		if ( table == null )
			return;

		_artifactTableCount++;
		IReadOnlyList<ArtifactPresentationSlotEntry> slots = table.Slots;
		if ( slots == null || slots.Count == 0 )
		{
			_warnings.Add( $"Artifact table '{table.name}' has no slots." );
			return;
		}

		for ( int i = 0; i < slots.Count; i++ )
		{
			TreasureDefinition required = slots[ i ].requiredArtifact;
			if ( required == null )
			{
				_warnings.Add( $"Artifact table '{table.name}' slot {i} has no required artifact." );
				continue;
			}

			InventoryRow row = GetOrCreateRow( required );
			row.DisplayCapacity += 1;

			bool merged = false;
			for ( int d = 0; d < row.Displays.Count; d++ )
			{
				DisplayHit existing = row.Displays[ d ];
				if ( existing.Target != table )
					continue;

				existing.Capacity += 1;
				existing.Label = $"{table.name} ×{existing.Capacity}";
				row.Displays[ d ] = existing;
				merged = true;
				break;
			}

			if ( !merged )
			{
				row.Displays.Add( new DisplayHit
				{
					Label = $"{table.name} ×1",
					Target = table,
					Capacity = 1,
					Uncapped = false
				} );
			}
		}
	}

	void CollectMixedDisplay( MixedDisplayTableInteractable table )
	{
		if ( table == null )
			return;

		_mixedTableCount++;
		ResolveMixedCapacity( table, out int capacity, out bool uncapped );
		_mixedDisplays.Add( new DisplayHit
		{
			Label = table.name,
			Target = table,
			Capacity = capacity,
			Uncapped = uncapped
		} );
	}

	void AddEntries( TreasurePileEntry[] entries, SourceKind kind, string label, UnityEngine.Object target )
	{
		if ( entries == null )
			return;

		for ( int i = 0; i < entries.Length; i++ )
		{
			TreasurePileEntry entry = entries[ i ];
			if ( entry.treasure == null || entry.count <= 0 )
				continue;

			AddCount( entry.treasure, entry.count, kind, label, target );
		}
	}

	void AddCount( TreasureDefinition definition, int count, SourceKind kind, string label, UnityEngine.Object target )
	{
		if ( definition == null || count <= 0 )
			return;

		InventoryRow row = GetOrCreateRow( definition );
		row.SceneCount += count;

		for ( int i = 0; i < row.Sources.Count; i++ )
		{
			SourceHit existing = row.Sources[ i ];
			if ( existing.Target == target && existing.Kind == kind )
			{
				existing.Count += count;
				row.Sources[ i ] = existing;
				return;
			}
		}

		row.Sources.Add( new SourceHit
		{
			Kind = kind,
			Label = label,
			Target = target,
			Count = count
		} );
	}

	InventoryRow GetOrCreateRow( TreasureDefinition definition )
	{
		if ( _byDefinition.TryGetValue( definition, out InventoryRow existing ) )
			return existing;

		InventoryRow row = new InventoryRow { Definition = definition };
		_byDefinition[ definition ] = row;
		return row;
	}

	static void ResolveTypedCapacity( TypedDisplayTableInteractable table, out int capacity, out bool uncapped )
	{
		capacity = 0;
		uncapped = false;
		if ( table == null )
			return;

		SerializedObject so = new SerializedObject( table );
		SerializedProperty maxStackProp = so.FindProperty( "maxStackPerSlot" );
		int maxStack = maxStackProp != null ? maxStackProp.intValue : 0;
		int slotCount = table.SlotCount;

		if ( table.AllowsVerticalStack && maxStack <= 0 )
		{
			uncapped = true;
			capacity = slotCount;
			return;
		}

		capacity = table.Capacity;
	}

	static void ResolveMixedCapacity( MixedDisplayTableInteractable table, out int capacity, out bool uncapped )
	{
		capacity = 0;
		uncapped = false;
		if ( table == null )
			return;

		SerializedObject so = new SerializedObject( table );
		SerializedProperty maxStackProp = so.FindProperty( "maxStackPerSlot" );
		int maxStack = maxStackProp != null ? maxStackProp.intValue : 0;
		int slotCount = table.SlotCount;
		if ( maxStack <= 0 )
		{
			uncapped = true;
			capacity = slotCount;
			return;
		}

		capacity = slotCount * maxStack;
	}

	static string[] GetCategoryFilterLabels()
	{
		string[] labels = new string[ 1 + Enum.GetNames( typeof( TreasureCategory ) ).Length ];
		labels[ 0 ] = "Any";
		Array.Copy( Enum.GetNames( typeof( TreasureCategory ) ), 0, labels, 1, labels.Length - 1 );
		return labels;
	}

	TreasureCategory? GetActiveCategoryFilter()
	{
		if ( _categoryFilterIndex <= 0 )
			return null;
		return ( TreasureCategory )( _categoryFilterIndex - 1 );
	}

	static string GetDisplayName( TreasureDefinition treasure )
	{
		if ( treasure == null )
			return string.Empty;
		if ( !string.IsNullOrEmpty( treasure.displayName ) )
			return treasure.displayName;
		return treasure.name;
	}

	static void ForEachInLoadedScenes<T>( Action<T> action ) where T : Component
	{
		if ( action == null )
			return;

		for ( int s = 0; s < SceneManager.sceneCount; s++ )
		{
			Scene scene = SceneManager.GetSceneAt( s );
			if ( !scene.isLoaded )
				continue;

			GameObject[] roots = scene.GetRootGameObjects();
			for ( int r = 0; r < roots.Length; r++ )
			{
				T[] components = roots[ r ].GetComponentsInChildren<T>( true );
				for ( int i = 0; i < components.Length; i++ )
				{
					if ( components[ i ] != null )
						action( components[ i ] );
				}
			}
		}
	}
}
#endif
