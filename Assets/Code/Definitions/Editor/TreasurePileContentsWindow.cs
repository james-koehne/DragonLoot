#if UNITY_EDITOR
using System;
using System.Collections.Generic;

using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor window for authoring TreasurePileDefinition coin and treasure contents.
/// Menu: DragonLoot → Treasure → Pile Contents
/// </summary>
public class TreasurePileContentsWindow : EditorWindow
{
	enum ContentsSection
	{
		Coins,
		Treasure
	}

	enum SortColumn
	{
		Name,
		Category,
		Count,
		MixPercent,
		UnitValue,
		TotalValue
	}

	enum RoundMode
	{
		None,
		Nearest1,
		Nearest5,
		Nearest10,
		Nearest100
	}

	enum BulkScope
	{
		AllRows,
		FilteredRows,
		SelectedRows
	}

	const string CoinsProp = "coinContents";
	const string TreasureProp = "treasureContents";

	TreasurePileDefinition _definition;
	SerializedObject _serialized;
	bool _followSelection = true;
	bool _lockedTarget;
	int _treasureContentsHash;

	float _scaleFactor = 2f;
	RoundMode _roundMode = RoundMode.None;
	BulkScope _bulkScope = BulkScope.AllRows;
	int _setTotalCoins;
	int _setTotalTreasure = 150;
	TreasurePileDefinition _copyFrom;
	float _copyScale = 1f;

	string _search = string.Empty;
	int _categoryFilterIndex;
	SortColumn _sortColumn = SortColumn.Count;
	bool _sortAscending;

	bool _foldCoins = true;
	bool _foldTreasure = true;
	bool _foldCatalog = true;
	bool _foldBulk = true;
	bool _foldCleanup = true;

	readonly HashSet<int> _selectedCoinRows = new HashSet<int>();
	readonly HashSet<int> _selectedTreasureRows = new HashSet<int>();

	Vector2 _scroll;
	List<TreasureDefinition> _catalog;
	Dictionary<TreasureCategory, List<TreasureDefinition>> _catalogByCategory;

	[MenuItem( DragonLootMenus.TreasurePileContents )]
	public static void Open()
	{
		TreasurePileContentsWindow window = GetWindow<TreasurePileContentsWindow>( "Pile Contents" );
		window.minSize = new Vector2( 520f, 480f );
		window.Show();
	}

	public static void Open( TreasurePileDefinition definition )
	{
		TreasurePileContentsWindow window = GetWindow<TreasurePileContentsWindow>( "Pile Contents" );
		window.minSize = new Vector2( 520f, 480f );
		window._definition = definition;
		window._lockedTarget = definition != null;
		window._followSelection = definition == null;
		window.BindSerialized();
		window.Show();
	}

	void OnEnable()
	{
		Selection.selectionChanged += OnSelectionChanged;
		RefreshCatalog();
		TryFollowSelection();
	}

	void OnDisable()
	{
		Selection.selectionChanged -= OnSelectionChanged;
		_serialized = null;
	}

	void OnSelectionChanged()
	{
		if ( !_followSelection || _lockedTarget )
			return;

		TryFollowSelection();
		Repaint();
	}

	void TryFollowSelection()
	{
		if ( Selection.activeObject is TreasurePileDefinition pile )
		{
			if ( pile != _definition )
			{
				_definition = pile;
				BindSerialized();
			}
		}
	}

	void BindSerialized()
	{
		_serialized = _definition != null ? new SerializedObject( _definition ) : null;
		_treasureContentsHash = _definition != null ? _definition.HashLargePropContents() : 0;
		_selectedCoinRows.Clear();
		_selectedTreasureRows.Clear();
	}

	void OnGUI()
	{
		_scroll = EditorGUILayout.BeginScrollView( _scroll );
		DrawTargetBar();

		if ( _definition == null || _serialized == null )
		{
			EditorGUILayout.HelpBox( "Select or assign a TreasurePileDefinition asset.", MessageType.Info );
			EditorGUILayout.EndScrollView();
			return;
		}

		_serialized.Update();
		DrawSummary();
		DrawBulkBar();
		DrawContentsSection( ContentsSection.Coins, _foldCoins, ref _foldCoins, CoinsProp, _selectedCoinRows );
		DrawContentsSection( ContentsSection.Treasure, _foldTreasure, ref _foldTreasure, TreasureProp, _selectedTreasureRows );
		DrawCatalog();
		DrawCleanup();
		_serialized.ApplyModifiedProperties();
		EditorGUILayout.EndScrollView();
	}

	void DrawTargetBar()
	{
		EditorGUILayout.LabelField( "Target", EditorStyles.boldLabel );

		EditorGUI.BeginChangeCheck();
		TreasurePileDefinition next = (TreasurePileDefinition)EditorGUILayout.ObjectField(
			"Pile Definition",
			_definition,
			typeof( TreasurePileDefinition ),
			false );
		if ( EditorGUI.EndChangeCheck() )
		{
			_definition = next;
			_lockedTarget = _definition != null;
			_followSelection = !_lockedTarget;
			BindSerialized();
		}

		EditorGUILayout.BeginHorizontal();
		_followSelection = EditorGUILayout.ToggleLeft( "Follow Selection", _followSelection );
		if ( _followSelection )
			_lockedTarget = false;
		GUI.enabled = _definition != null;
		if ( GUILayout.Button( "Ping", GUILayout.Width( 56f ) ) )
		{
			EditorGUIUtility.PingObject( _definition );
			Selection.activeObject = _definition;
		}
		GUI.enabled = true;
		EditorGUILayout.EndHorizontal();
		EditorGUILayout.Space( 4f );
	}

	void DrawSummary()
	{
		EditorGUILayout.LabelField( "Summary", EditorStyles.boldLabel );

		int coinUnits = _definition.TotalCoinUnits();
		int treasureUnits = _definition.TotalTreasureUnits();
		long coinValue = SumValue( _definition.coinContents );
		long treasureValue = SumValue( _definition.treasureContents );

		EditorGUILayout.LabelField( "Coin mix weight (inventory)", coinUnits.ToString( "N0" ) );
		EditorGUILayout.LabelField( "Treasure units (latent seats)", treasureUnits.ToString( "N0" ) );
		EditorGUILayout.LabelField( "Total value", ( coinValue + treasureValue ).ToString( "N0" ) );

		DrawCoinMixSummary();
		DrawTreasureCategorySummary();

		int steady = _definition.SteadyCoinVisibleBudget();
		EditorGUILayout.LabelField(
			"GPU coin seats",
			$"max { _definition.maxVisibleTotal:N0}  steady ~{steady:N0}  buffer { _definition.DigCoinBufferSeats():N0}" );
		EditorGUILayout.HelpBox(
			"Coin counts are mix weights for densify + dig economy, not 1:1 with drawn GPU coins.",
			MessageType.None );

		int currentHash = _definition.HashLargePropContents();
		if ( currentHash != _treasureContentsHash )
		{
			EditorGUILayout.HelpBox(
				"Treasure contents changed — rebake latent bakes on affected TreasurePileVisual instances.",
				MessageType.Warning );
		}
	}

	void DrawCoinMixSummary()
	{
		TreasurePileEntry[] entries = _definition.coinContents;
		if ( entries == null || entries.Length == 0 )
			return;

		int total = _definition.TotalCoinUnits();
		if ( total <= 0 )
			return;

		EditorGUILayout.LabelField( "Coin mix", EditorStyles.miniBoldLabel );
		for ( int i = 0; i < entries.Length; i++ )
		{
			TreasurePileEntry entry = entries[ i ];
			if ( entry.treasure == null || entry.count <= 0 )
				continue;

			float pct = entry.count / ( float )total * 100f;
			string label = string.IsNullOrEmpty( entry.treasure.displayName )
				? entry.treasure.name
				: entry.treasure.displayName;
			EditorGUILayout.LabelField( $"  {label}", $"{entry.count:N0}  ({pct:0.1}%)" );
		}
	}

	void DrawTreasureCategorySummary()
	{
		TreasurePileEntry[] entries = _definition.treasureContents;
		if ( entries == null || entries.Length == 0 )
			return;

		Dictionary<TreasureCategory, int> byCategory = new Dictionary<TreasureCategory, int>();
		for ( int i = 0; i < entries.Length; i++ )
		{
			TreasurePileEntry entry = entries[ i ];
			if ( entry.treasure == null || entry.count <= 0 )
				continue;

			if ( !byCategory.ContainsKey( entry.treasure.category ) )
				byCategory[ entry.treasure.category ] = 0;
			byCategory[ entry.treasure.category ] += entry.count;
		}

		if ( byCategory.Count == 0 )
			return;

		EditorGUILayout.LabelField( "Treasure by category", EditorStyles.miniBoldLabel );
		foreach ( KeyValuePair<TreasureCategory, int> pair in byCategory )
			EditorGUILayout.LabelField( $"  {pair.Key}", pair.Value.ToString( "N0" ) );
	}

	void DrawBulkBar()
	{
		_foldBulk = EditorGUILayout.BeginFoldoutHeaderGroup( _foldBulk, "Bulk Operations" );
		if ( !_foldBulk )
		{
			EditorGUILayout.EndFoldoutHeaderGroup();
			return;
		}

		_scaleFactor = EditorGUILayout.FloatField( "Scale Factor", _scaleFactor );
		_roundMode = (RoundMode)EditorGUILayout.EnumPopup( "After Scale Rounding", _roundMode );
		_bulkScope = (BulkScope)EditorGUILayout.EnumPopup( "Scope", _bulkScope );

		EditorGUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Scale Coins" ) )
			ApplyScale( ContentsSection.Coins, _scaleFactor );
		if ( GUILayout.Button( "Scale Treasure" ) )
			ApplyScale( ContentsSection.Treasure, _scaleFactor );
		if ( GUILayout.Button( "Scale Both" ) )
		{
			ApplyScale( ContentsSection.Coins, _scaleFactor );
			ApplyScale( ContentsSection.Treasure, _scaleFactor );
		}
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.Space( 4f );
		EditorGUILayout.LabelField( "Set Total (keep mix)", EditorStyles.miniBoldLabel );
		EditorGUILayout.BeginHorizontal();
		_setTotalCoins = EditorGUILayout.IntField( "Coins", _setTotalCoins );
		if ( GUILayout.Button( "Apply", GUILayout.Width( 64f ) ) )
			ApplySetTotal( ContentsSection.Coins, _setTotalCoins );
		EditorGUILayout.EndHorizontal();
		EditorGUILayout.BeginHorizontal();
		_setTotalTreasure = EditorGUILayout.IntField( "Treasure", _setTotalTreasure );
		if ( GUILayout.Button( "Apply", GUILayout.Width( 64f ) ) )
			ApplySetTotal( ContentsSection.Treasure, _setTotalTreasure );
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.Space( 4f );
		EditorGUILayout.LabelField( "Copy Contents From", EditorStyles.miniBoldLabel );
		_copyFrom = (TreasurePileDefinition)EditorGUILayout.ObjectField( "Source Pile", _copyFrom, typeof( TreasurePileDefinition ), false );
		_copyScale = EditorGUILayout.FloatField( "Then Scale", _copyScale );
		EditorGUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Copy Coins" ) )
			CopyContents( ContentsSection.Coins );
		if ( GUILayout.Button( "Copy Treasure" ) )
			CopyContents( ContentsSection.Treasure );
		if ( GUILayout.Button( "Copy Both" ) )
		{
			CopyContents( ContentsSection.Coins );
			CopyContents( ContentsSection.Treasure );
		}
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.EndFoldoutHeaderGroup();
	}

	void DrawContentsSection(
		ContentsSection section,
		bool foldout,
		ref bool foldoutRef,
		string propertyName,
		HashSet<int> selectedRows )
	{
		string title = section == ContentsSection.Coins ? "Coins" : "Treasure";
		foldoutRef = EditorGUILayout.BeginFoldoutHeaderGroup( foldoutRef, title );
		if ( !foldoutRef )
		{
			EditorGUILayout.EndFoldoutHeaderGroup();
			return;
		}

		Rect dropRect = GUILayoutUtility.GetRect( 0f, 22f, GUILayout.ExpandWidth( true ) );
		GUI.Box( dropRect, "Drop TreasureDefinition assets here", EditorStyles.helpBox );
		HandleDragAndDrop( dropRect, section );

		SerializedProperty arrayProp = _serialized.FindProperty( propertyName );
		if ( arrayProp == null )
		{
			EditorGUILayout.EndFoldoutHeaderGroup();
			return;
		}

		DrawSearchAndFilter();
		DrawSortHeader();

		List<int> visibleIndices = BuildVisibleIndices( arrayProp, section );
		int totalWeight = SumVisibleWeight( arrayProp, visibleIndices );

		for ( int vi = 0; vi < visibleIndices.Count; vi++ )
		{
			int index = visibleIndices[ vi ];
			DrawEntryRow( arrayProp, index, section, selectedRows, totalWeight );
		}

		EditorGUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Add Row" ) )
		{
			Undo.RecordObject( _definition, "Add Pile Entry" );
			arrayProp.InsertArrayElementAtIndex( arrayProp.arraySize );
			SerializedProperty element = arrayProp.GetArrayElementAtIndex( arrayProp.arraySize - 1 );
			element.FindPropertyRelative( "treasure" ).objectReferenceValue = null;
			element.FindPropertyRelative( "count" ).intValue = 1;
			_serialized.ApplyModifiedProperties();
		}
		if ( GUILayout.Button( "Select All Visible" ) )
		{
			selectedRows.Clear();
			for ( int i = 0; i < visibleIndices.Count; i++ )
				selectedRows.Add( visibleIndices[ i ] );
		}
		if ( GUILayout.Button( "Clear Selection" ) )
			selectedRows.Clear();
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.EndFoldoutHeaderGroup();
	}

	void DrawSearchAndFilter()
	{
		EditorGUILayout.BeginHorizontal();
		_search = EditorGUILayout.TextField( "Search", _search );
		_categoryFilterIndex = EditorGUILayout.Popup( "Category", _categoryFilterIndex, GetCategoryFilterLabels() );
		EditorGUILayout.EndHorizontal();
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
		return (TreasureCategory)( _categoryFilterIndex - 1 );
	}

	void DrawSortHeader()
	{
		EditorGUILayout.BeginHorizontal();
		DrawSortButton( "Name", SortColumn.Name, 120f );
		DrawSortButton( "Cat", SortColumn.Category, 52f );
		DrawSortButton( "Count", SortColumn.Count, 52f );
		DrawSortButton( "Mix%", SortColumn.MixPercent, 44f );
		DrawSortButton( "Val", SortColumn.UnitValue, 40f );
		DrawSortButton( "Total", SortColumn.TotalValue, 48f );
		GUILayout.Label( "", GUILayout.Width( 88f ) );
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
				_sortAscending = true;
			}
		}
	}

	void DrawEntryRow(
		SerializedProperty arrayProp,
		int index,
		ContentsSection section,
		HashSet<int> selectedRows,
		int totalWeight )
	{
		SerializedProperty element = arrayProp.GetArrayElementAtIndex( index );
		SerializedProperty treasureProp = element.FindPropertyRelative( "treasure" );
		SerializedProperty countProp = element.FindPropertyRelative( "count" );
		TreasureDefinition treasure = treasureProp.objectReferenceValue as TreasureDefinition;
		int count = countProp.intValue;

		bool selected = selectedRows.Contains( index );
		Color prev = GUI.backgroundColor;
		if ( selected )
			GUI.backgroundColor = new Color( 0.55f, 0.75f, 1f );

		EditorGUILayout.BeginHorizontal( EditorStyles.helpBox );

		if ( GUILayout.Button( selected ? "●" : "○", GUILayout.Width( 22f ) ) )
		{
			if ( selected )
				selectedRows.Remove( index );
			else
				selectedRows.Add( index );
		}

		Texture icon = treasure != null && treasure.icon != null ? treasure.icon.texture : null;
		GUILayout.Label( icon, GUILayout.Width( 20f ), GUILayout.Height( 20f ) );

		EditorGUI.BeginChangeCheck();
		EditorGUILayout.PropertyField( treasureProp, GUIContent.none, GUILayout.MinWidth( 100f ) );
		int newCount = EditorGUILayout.IntField( count, GUILayout.Width( 52f ) );
		if ( EditorGUI.EndChangeCheck() )
		{
			countProp.intValue = Mathf.Max( 0, newCount );
			_serialized.ApplyModifiedProperties();
			count = countProp.intValue;
			treasure = treasureProp.objectReferenceValue as TreasureDefinition;
		}

		string category = treasure != null ? treasure.category.ToString() : "—";
		GUILayout.Label( category, GUILayout.Width( 52f ) );

		float mix = totalWeight > 0 && count > 0 ? count / ( float )totalWeight * 100f : 0f;
		int unitValue = treasure != null ? treasure.value : 0;
		long rowValue = ( long )unitValue * count;
		GUILayout.Label( $"{mix:0.#}%", GUILayout.Width( 44f ) );
		GUILayout.Label( unitValue.ToString(), GUILayout.Width( 40f ) );
		GUILayout.Label( rowValue.ToString( "N0" ), GUILayout.Width( 48f ) );

		if ( GUILayout.Button( "↑", GUILayout.Width( 22f ) ) && index > 0 )
			SwapArrayElements( arrayProp, index, index - 1 );
		if ( GUILayout.Button( "↓", GUILayout.Width( 22f ) ) && index < arrayProp.arraySize - 1 )
			SwapArrayElements( arrayProp, index, index + 1 );
		if ( GUILayout.Button( "×", GUILayout.Width( 22f ) ) )
		{
			Undo.RecordObject( _definition, "Remove Pile Entry" );
			arrayProp.DeleteArrayElementAtIndex( index );
			selectedRows.Remove( index );
			_serialized.ApplyModifiedProperties();
		}

		EditorGUILayout.EndHorizontal();
		GUI.backgroundColor = prev;

		if ( treasure != null )
		{
			bool wrongSection = section == ContentsSection.Coins
				? treasure.category != TreasureCategory.Coin
				: treasure.category == TreasureCategory.Coin;
			if ( wrongSection )
			{
				EditorGUILayout.HelpBox(
					$"{treasure.name} is in the wrong contents list for category {treasure.category}.",
					MessageType.Warning );
			}
		}
	}

	void DrawCatalog()
	{
		_foldCatalog = EditorGUILayout.BeginFoldoutHeaderGroup( _foldCatalog, "Catalog" );
		if ( !_foldCatalog )
		{
			EditorGUILayout.EndFoldoutHeaderGroup();
			return;
		}

		if ( GUILayout.Button( "Refresh Catalog" ) )
			RefreshCatalog();

		EditorGUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Add All Missing Coins" ) )
			AddAllMissingByCategory( TreasureCategory.Coin, ContentsSection.Coins );
		if ( GUILayout.Button( "Add All Missing Gems" ) )
			AddAllMissingByCategory( TreasureCategory.Gem, ContentsSection.Treasure );
		EditorGUILayout.EndHorizontal();

		if ( _catalogByCategory == null )
			RefreshCatalog();

		HashSet<TreasureDefinition> inPile = BuildInPileSet();
		foreach ( TreasureCategory category in Enum.GetValues( typeof( TreasureCategory ) ) )
		{
			if ( !_catalogByCategory.TryGetValue( category, out List<TreasureDefinition> defs ) || defs.Count == 0 )
				continue;

			EditorGUILayout.LabelField( category.ToString(), EditorStyles.miniBoldLabel );
			for ( int i = 0; i < defs.Count; i++ )
			{
				TreasureDefinition def = defs[ i ];
				bool present = inPile.Contains( def );
				EditorGUILayout.BeginHorizontal();
				GUI.enabled = !present;
				EditorGUILayout.ObjectField( def, typeof( TreasureDefinition ), false );
				if ( GUILayout.Button( present ? "In pile" : "Add", GUILayout.Width( 64f ) ) && !present )
					AddCatalogEntry( def );
				GUI.enabled = true;
				EditorGUILayout.EndHorizontal();
			}
		}

		EditorGUILayout.EndFoldoutHeaderGroup();
	}

	void DrawCleanup()
	{
		_foldCleanup = EditorGUILayout.BeginFoldoutHeaderGroup( _foldCleanup, "Cleanup" );
		if ( !_foldCleanup )
		{
			EditorGUILayout.EndFoldoutHeaderGroup();
			return;
		}

		EditorGUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Merge Duplicates (Coins)" ) )
			MergeDuplicates( CoinsProp );
		if ( GUILayout.Button( "Merge Duplicates (Treasure)" ) )
			MergeDuplicates( TreasureProp );
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Remove Null / Zero (Coins)" ) )
			RemoveNullOrZero( CoinsProp );
		if ( GUILayout.Button( "Remove Null / Zero (Treasure)" ) )
			RemoveNullOrZero( TreasureProp );
		EditorGUILayout.EndHorizontal();

		DrawMisplacedWarnings();

		EditorGUILayout.EndFoldoutHeaderGroup();
	}

	void DrawMisplacedWarnings()
	{
		WarnMisplaced( _definition.coinContents, ContentsSection.Coins );
		WarnMisplaced( _definition.treasureContents, ContentsSection.Treasure );
	}

	void WarnMisplaced( TreasurePileEntry[] entries, ContentsSection expected )
	{
		if ( entries == null )
			return;

		for ( int i = 0; i < entries.Length; i++ )
		{
			TreasureDefinition treasure = entries[ i ].treasure;
			if ( treasure == null )
				continue;

			bool misplaced = expected == ContentsSection.Coins
				? treasure.category != TreasureCategory.Coin
				: treasure.category == TreasureCategory.Coin;
			if ( misplaced )
			{
				EditorGUILayout.HelpBox(
					$"Misplaced: {treasure.name} ({treasure.category}) in {( expected == ContentsSection.Coins ? "coin" : "treasure" )} contents.",
					MessageType.Warning );
			}
		}
	}

	void ApplyScale( ContentsSection section, float factor )
	{
		string propName = section == ContentsSection.Coins ? CoinsProp : TreasureProp;
		SerializedProperty arrayProp = _serialized.FindProperty( propName );
		if ( arrayProp == null )
			return;

		HashSet<int> scope = GetScopeIndices( section, arrayProp );
		Undo.RecordObject( _definition, "Scale Pile Contents" );
		for ( int i = 0; i < arrayProp.arraySize; i++ )
		{
			if ( !scope.Contains( i ) )
				continue;

			SerializedProperty element = arrayProp.GetArrayElementAtIndex( i );
			SerializedProperty countProp = element.FindPropertyRelative( "count" );
			int scaled = ScaleCount( countProp.intValue, factor );
			countProp.intValue = scaled;
		}

		_serialized.ApplyModifiedProperties();
		EditorUtility.SetDirty( _definition );
	}

	void ApplySetTotal( ContentsSection section, int total )
	{
		total = Mathf.Max( 0, total );
		string propName = section == ContentsSection.Coins ? CoinsProp : TreasureProp;
		SerializedProperty arrayProp = _serialized.FindProperty( propName );
		if ( arrayProp == null )
			return;

		HashSet<int> scope = GetScopeIndices( section, arrayProp );
		List<int> scopeList = new List<int>( scope );
		scopeList.Sort();

		TreasurePileEntry[] scopedEntries = new TreasurePileEntry[ scopeList.Count ];
		for ( int i = 0; i < scopeList.Count; i++ )
		{
			int index = scopeList[ i ];
			SerializedProperty element = arrayProp.GetArrayElementAtIndex( index );
			scopedEntries[ i ] = new TreasurePileEntry
			{
				treasure = element.FindPropertyRelative( "treasure" ).objectReferenceValue as TreasureDefinition,
				count = element.FindPropertyRelative( "count" ).intValue
			};
		}

		int[] quotas = TreasurePileDefinition.ComputeLargestRemainderQuotas( scopedEntries, total );
		Undo.RecordObject( _definition, "Set Pile Total" );
		for ( int i = 0; i < scopeList.Count && i < quotas.Length; i++ )
			arrayProp.GetArrayElementAtIndex( scopeList[ i ] ).FindPropertyRelative( "count" ).intValue = quotas[ i ];

		_serialized.ApplyModifiedProperties();
		EditorUtility.SetDirty( _definition );
	}

	void CopyContents( ContentsSection section )
	{
		if ( _copyFrom == null || _copyFrom == _definition )
			return;

		string propName = section == ContentsSection.Coins ? CoinsProp : TreasureProp;
		TreasurePileEntry[] source = section == ContentsSection.Coins
			? _copyFrom.coinContents
			: _copyFrom.treasureContents;

		Undo.RecordObject( _definition, "Copy Pile Contents" );
		SerializedProperty arrayProp = _serialized.FindProperty( propName );
		arrayProp.ClearArray();
		if ( source != null )
		{
			for ( int i = 0; i < source.Length; i++ )
			{
				arrayProp.InsertArrayElementAtIndex( i );
				SerializedProperty element = arrayProp.GetArrayElementAtIndex( i );
				element.FindPropertyRelative( "treasure" ).objectReferenceValue = source[ i ].treasure;
				int count = ScaleCount( source[ i ].count, _copyScale );
				element.FindPropertyRelative( "count" ).intValue = count;
			}
		}

		_serialized.ApplyModifiedProperties();
		EditorUtility.SetDirty( _definition );
		if ( section == ContentsSection.Coins )
			_selectedCoinRows.Clear();
		else
			_selectedTreasureRows.Clear();
	}

	int ScaleCount( int count, float factor )
	{
		if ( count <= 0 )
			return 0;

		int scaled = Mathf.Max( 0, Mathf.RoundToInt( count * factor ) );
		return ApplyRounding( scaled );
	}

	int ApplyRounding( int value )
	{
		switch ( _roundMode )
		{
			case RoundMode.Nearest1:
				return value;
			case RoundMode.Nearest5:
				return Mathf.Max( 0, Mathf.RoundToInt( value / 5f ) * 5 );
			case RoundMode.Nearest10:
				return Mathf.Max( 0, Mathf.RoundToInt( value / 10f ) * 10 );
			case RoundMode.Nearest100:
				return Mathf.Max( 0, Mathf.RoundToInt( value / 100f ) * 100 );
			default:
				return value;
		}
	}

	HashSet<int> GetScopeIndices( ContentsSection section, SerializedProperty arrayProp )
	{
		HashSet<int> result = new HashSet<int>();
		if ( _bulkScope == BulkScope.AllRows )
		{
			for ( int i = 0; i < arrayProp.arraySize; i++ )
				result.Add( i );
			return result;
		}

		List<int> visible = BuildVisibleIndices( arrayProp, section );
		if ( _bulkScope == BulkScope.FilteredRows )
		{
			for ( int i = 0; i < visible.Count; i++ )
				result.Add( visible[ i ] );
			return result;
		}

		HashSet<int> selected = section == ContentsSection.Coins ? _selectedCoinRows : _selectedTreasureRows;
		for ( int i = 0; i < visible.Count; i++ )
		{
			int index = visible[ i ];
			if ( selected.Contains( index ) )
				result.Add( index );
		}

		return result;
	}

	List<int> BuildVisibleIndices( SerializedProperty arrayProp, ContentsSection section )
	{
		List<int> indices = new List<int>();
		for ( int i = 0; i < arrayProp.arraySize; i++ )
		{
			SerializedProperty element = arrayProp.GetArrayElementAtIndex( i );
			TreasureDefinition treasure = element.FindPropertyRelative( "treasure" ).objectReferenceValue as TreasureDefinition;
			if ( !PassesFilter( treasure ) )
				continue;

			indices.Add( i );
		}

		indices.Sort( ( a, b ) => CompareIndices( arrayProp, a, b ) );
		if ( !_sortAscending )
			indices.Reverse();
		return indices;
	}

	bool PassesFilter( TreasureDefinition treasure )
	{
		if ( treasure == null )
			return string.IsNullOrEmpty( _search );

		TreasureCategory? categoryFilter = GetActiveCategoryFilter();
		if ( categoryFilter.HasValue && treasure.category != categoryFilter.Value )
			return false;

		if ( string.IsNullOrEmpty( _search ) )
			return true;

		string needle = _search.Trim();
		if ( treasure.name.IndexOf( needle, StringComparison.OrdinalIgnoreCase ) >= 0 )
			return true;
		if ( !string.IsNullOrEmpty( treasure.displayName )
			&& treasure.displayName.IndexOf( needle, StringComparison.OrdinalIgnoreCase ) >= 0 )
			return true;
		if ( !string.IsNullOrEmpty( treasure.variant )
			&& treasure.variant.IndexOf( needle, StringComparison.OrdinalIgnoreCase ) >= 0 )
			return true;
		return false;
	}

	int CompareIndices( SerializedProperty arrayProp, int a, int b )
	{
		SerializedProperty elA = arrayProp.GetArrayElementAtIndex( a );
		SerializedProperty elB = arrayProp.GetArrayElementAtIndex( b );
		TreasureDefinition tA = elA.FindPropertyRelative( "treasure" ).objectReferenceValue as TreasureDefinition;
		TreasureDefinition tB = elB.FindPropertyRelative( "treasure" ).objectReferenceValue as TreasureDefinition;
		int cA = elA.FindPropertyRelative( "count" ).intValue;
		int cB = elB.FindPropertyRelative( "count" ).intValue;

		int result = 0;
		switch ( _sortColumn )
		{
			case SortColumn.Name:
				result = string.Compare( GetDisplayName( tA ), GetDisplayName( tB ), StringComparison.OrdinalIgnoreCase );
				break;
			case SortColumn.Category:
				result = CompareCategory( tA, tB );
				break;
			case SortColumn.Count:
				result = cA.CompareTo( cB );
				break;
			case SortColumn.MixPercent:
			case SortColumn.TotalValue:
				result = ( ( long )GetValue( tA ) * cA ).CompareTo( ( long )GetValue( tB ) * cB );
				break;
			case SortColumn.UnitValue:
				result = GetValue( tA ).CompareTo( GetValue( tB ) );
				break;
		}

		return result != 0 ? result : a.CompareTo( b );
	}

	static int CompareCategory( TreasureDefinition a, TreasureDefinition b )
	{
		int ca = a != null ? ( int )a.category : -1;
		int cb = b != null ? ( int )b.category : -1;
		return ca.CompareTo( cb );
	}

	static string GetDisplayName( TreasureDefinition treasure )
	{
		if ( treasure == null )
			return string.Empty;
		if ( !string.IsNullOrEmpty( treasure.displayName ) )
			return treasure.displayName;
		return treasure.name;
	}

	static int GetValue( TreasureDefinition treasure )
	{
		return treasure != null ? treasure.value : 0;
	}

	int SumVisibleWeight( SerializedProperty arrayProp, List<int> visibleIndices )
	{
		int total = 0;
		for ( int i = 0; i < visibleIndices.Count; i++ )
		{
			int index = visibleIndices[ i ];
			int count = arrayProp.GetArrayElementAtIndex( index ).FindPropertyRelative( "count" ).intValue;
			if ( count > 0 )
				total += count;
		}

		return total;
	}

	void SwapArrayElements( SerializedProperty arrayProp, int a, int b )
	{
		Undo.RecordObject( _definition, "Reorder Pile Entry" );
		arrayProp.MoveArrayElement( a, b );
		_serialized.ApplyModifiedProperties();
	}

	void HandleDragAndDrop( Rect rect, ContentsSection section )
	{
		Event evt = Event.current;
		if ( !rect.Contains( evt.mousePosition ) )
			return;

		if ( evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform )
			return;

		if ( !TryGetDraggedTreasure( out TreasureDefinition dragged ) )
			return;

		ContentsSection targetSection = dragged.category == TreasureCategory.Coin
			? ContentsSection.Coins
			: ContentsSection.Treasure;
		DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

		if ( evt.type == EventType.DragPerform )
		{
			DragAndDrop.AcceptDrag();
			AddOrIncrementEntry( targetSection, dragged, 1 );
			if ( targetSection != section )
			{
				Debug.LogWarning(
					$"TreasurePileContents: {dragged.name} ({dragged.category}) routed to "
					+ $"{( targetSection == ContentsSection.Coins ? "coin" : "treasure" )} contents." );
			}
		}

		evt.Use();
	}

	static bool TryGetDraggedTreasure( out TreasureDefinition treasure )
	{
		treasure = null;
		if ( DragAndDrop.objectReferences == null || DragAndDrop.objectReferences.Length == 0 )
			return false;

		for ( int i = 0; i < DragAndDrop.objectReferences.Length; i++ )
		{
			treasure = DragAndDrop.objectReferences[ i ] as TreasureDefinition;
			if ( treasure != null )
				return true;
		}

		return false;
	}

	void AddCatalogEntry( TreasureDefinition def )
	{
		if ( def == null )
			return;

		ContentsSection section = def.category == TreasureCategory.Coin
			? ContentsSection.Coins
			: ContentsSection.Treasure;
		AddOrIncrementEntry( section, def, 1 );
	}

	void AddAllMissingByCategory( TreasureCategory category, ContentsSection section )
	{
		if ( _catalogByCategory == null )
			RefreshCatalog();

		if ( !_catalogByCategory.TryGetValue( category, out List<TreasureDefinition> defs ) )
			return;

		HashSet<TreasureDefinition> inPile = BuildInPileSet();
		for ( int i = 0; i < defs.Count; i++ )
		{
			TreasureDefinition def = defs[ i ];
			if ( !inPile.Contains( def ) )
				AddOrIncrementEntry( section, def, 1 );
		}
	}

	void AddOrIncrementEntry( ContentsSection section, TreasureDefinition treasure, int delta )
	{
		string propName = section == ContentsSection.Coins ? CoinsProp : TreasureProp;
		SerializedProperty arrayProp = _serialized.FindProperty( propName );
		if ( arrayProp == null )
			return;

		Undo.RecordObject( _definition, "Add Pile Entry" );
		for ( int i = 0; i < arrayProp.arraySize; i++ )
		{
			SerializedProperty element = arrayProp.GetArrayElementAtIndex( i );
			if ( element.FindPropertyRelative( "treasure" ).objectReferenceValue == treasure )
			{
				SerializedProperty countProp = element.FindPropertyRelative( "count" );
				countProp.intValue = Mathf.Max( 0, countProp.intValue + delta );
				_serialized.ApplyModifiedProperties();
				EditorUtility.SetDirty( _definition );
				return;
			}
		}

		int index = arrayProp.arraySize;
		arrayProp.InsertArrayElementAtIndex( index );
		SerializedProperty newElement = arrayProp.GetArrayElementAtIndex( index );
		newElement.FindPropertyRelative( "treasure" ).objectReferenceValue = treasure;
		newElement.FindPropertyRelative( "count" ).intValue = Mathf.Max( 1, delta );
		_serialized.ApplyModifiedProperties();
		EditorUtility.SetDirty( _definition );
	}

	void MergeDuplicates( string propertyName )
	{
		SerializedProperty arrayProp = _serialized.FindProperty( propertyName );
		if ( arrayProp == null || arrayProp.arraySize <= 1 )
			return;

		Dictionary<TreasureDefinition, int> merged = new Dictionary<TreasureDefinition, int>();
		List<TreasureDefinition> order = new List<TreasureDefinition>();
		for ( int i = 0; i < arrayProp.arraySize; i++ )
		{
			SerializedProperty element = arrayProp.GetArrayElementAtIndex( i );
			TreasureDefinition treasure = element.FindPropertyRelative( "treasure" ).objectReferenceValue as TreasureDefinition;
			int count = element.FindPropertyRelative( "count" ).intValue;
			if ( treasure == null )
				continue;

			if ( !merged.ContainsKey( treasure ) )
			{
				merged[ treasure ] = 0;
				order.Add( treasure );
			}

			merged[ treasure ] += Mathf.Max( 0, count );
		}

		Undo.RecordObject( _definition, "Merge Pile Duplicates" );
		arrayProp.ClearArray();
		for ( int i = 0; i < order.Count; i++ )
		{
			TreasureDefinition treasure = order[ i ];
			arrayProp.InsertArrayElementAtIndex( i );
			SerializedProperty element = arrayProp.GetArrayElementAtIndex( i );
			element.FindPropertyRelative( "treasure" ).objectReferenceValue = treasure;
			element.FindPropertyRelative( "count" ).intValue = merged[ treasure ];
		}

		_serialized.ApplyModifiedProperties();
		EditorUtility.SetDirty( _definition );
	}

	void RemoveNullOrZero( string propertyName )
	{
		SerializedProperty arrayProp = _serialized.FindProperty( propertyName );
		if ( arrayProp == null )
			return;

		Undo.RecordObject( _definition, "Clean Pile Contents" );
		for ( int i = arrayProp.arraySize - 1; i >= 0; i-- )
		{
			SerializedProperty element = arrayProp.GetArrayElementAtIndex( i );
			TreasureDefinition treasure = element.FindPropertyRelative( "treasure" ).objectReferenceValue as TreasureDefinition;
			int count = element.FindPropertyRelative( "count" ).intValue;
			if ( treasure == null || count <= 0 )
				arrayProp.DeleteArrayElementAtIndex( i );
		}

		_serialized.ApplyModifiedProperties();
		EditorUtility.SetDirty( _definition );
	}

	HashSet<TreasureDefinition> BuildInPileSet()
	{
		HashSet<TreasureDefinition> set = new HashSet<TreasureDefinition>();
		AddEntriesToSet( _definition.coinContents, set );
		AddEntriesToSet( _definition.treasureContents, set );
		return set;
	}

	static void AddEntriesToSet( TreasurePileEntry[] entries, HashSet<TreasureDefinition> set )
	{
		if ( entries == null )
			return;

		for ( int i = 0; i < entries.Length; i++ )
		{
			if ( entries[ i ].treasure != null )
				set.Add( entries[ i ].treasure );
		}
	}

	void RefreshCatalog()
	{
		_catalog = new List<TreasureDefinition>();
		_catalogByCategory = new Dictionary<TreasureCategory, List<TreasureDefinition>>();
		string[] guids = AssetDatabase.FindAssets( "t:TreasureDefinition" );
		for ( int i = 0; i < guids.Length; i++ )
		{
			string path = AssetDatabase.GUIDToAssetPath( guids[ i ] );
			TreasureDefinition def = AssetDatabase.LoadAssetAtPath<TreasureDefinition>( path );
			if ( def == null )
				continue;

			_catalog.Add( def );
			if ( !_catalogByCategory.ContainsKey( def.category ) )
				_catalogByCategory[ def.category ] = new List<TreasureDefinition>();
			_catalogByCategory[ def.category ].Add( def );
		}

		foreach ( KeyValuePair<TreasureCategory, List<TreasureDefinition>> pair in _catalogByCategory )
			pair.Value.Sort( ( a, b ) => string.Compare( a.name, b.name, StringComparison.OrdinalIgnoreCase ) );
	}

	static long SumValue( TreasurePileEntry[] entries )
	{
		if ( entries == null )
			return 0;

		long total = 0;
		for ( int i = 0; i < entries.Length; i++ )
		{
			TreasurePileEntry entry = entries[ i ];
			if ( entry.treasure != null && entry.count > 0 )
				total += ( long )entry.treasure.value * entry.count;
		}

		return total;
	}
}
#endif
