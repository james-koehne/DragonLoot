#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class ProjectTasksWindow : EditorWindow
{
	static readonly Color TintGreen = new Color( 0.18f, 0.32f, 0.18f, 1f );
	static readonly Color TintAmber = new Color( 0.38f, 0.30f, 0.12f, 1f );
	static readonly Color TintHighlight = new Color( 0.28f, 0.28f, 0.15f, 1f );

	enum FeatureSortColumn
	{
		Name,
		Status,
		Priority,
		Assignee,
		LastSync,
		Verification
	}

	enum CompactPane
	{
		Categories,
		Features,
		Details
	}

	const float CompactWidthBreakpoint = 900f;

	ProjectTasksCatalog _catalog;
	ProjectTasksLocalStore _localStore;
	string _outputRoot;

	List<ImportedCategory> _visibleCategories = new List<ImportedCategory>();
	List<ImportedFeature> _visibleFeatures = new List<ImportedFeature>();
	List<PendingMilanoteItem> _pendingQueue = new List<PendingMilanoteItem>();

	ImportedCategory _selectedCategory;
	ImportedFeature _selectedFeature;

	string _globalSearch = string.Empty;
	string _categorySearch = string.Empty;
	string _filterStatus = "All";
	string _filterPriority = "All";
	string _filterAssignee = "All";
	string _filterVerification = "All";
	string _filterLocal = "All";
	FeatureSortColumn _sortColumn = FeatureSortColumn.Name;
	bool _sortAscending = true;
	bool _showingQueue;
	bool _compactMode;
	CompactPane _compactPane = CompactPane.Categories;
	float _lastLayoutWidth = -1f;

	bool _busy;
	string _statusText = "Ready.";
	CancellationTokenSource _cts;

	ListView _categoryList;
	ListView _featureList;
	ScrollView _detailsScroll;
	Label _statusLabel;
	Label _syncInfoLabel;
	Button _queueButton;
	VisualElement _toolbar;
	VisualElement _filterBar;
	readonly List<ToolbarAction> _toolbarActions = new List<ToolbarAction>();
	VisualElement _contentHost;
	VisualElement _categoriesPanel;
	VisualElement _featuresPanel;
	VisualElement _detailsPanel;
	VisualElement _compactNavBar;
	Label _compactContextLabel;
	Button _compactBackButton;
	Button _compactCategoriesButton;
	Button _compactFeaturesButton;
	Button _compactDetailsButton;
	VisualElement _queuePanel;
	ListView _queueList;

	sealed class ToolbarAction
	{
		public Button Button;
		public string WideLabel;
		public string CompactLabel;
	}

	TextField _globalSearchField;
	TextField _categorySearchField;
	DropdownField _statusFilter;
	DropdownField _priorityFilter;
	DropdownField _assigneeFilter;
	DropdownField _verificationFilter;
	DropdownField _localFilter;

	[MenuItem( "Tools/MilanoteSync/Tasks" )]
	public static void Open()
	{
		ProjectTasksWindow window = GetWindow<ProjectTasksWindow>();
		window.titleContent = new GUIContent( "Project Tasks" );
		window.minSize = new Vector2( 380f, 480f );
		window.Show();
	}

	/// <summary>
	/// Reloads generated markdown into any open Project Tasks window (e.g. after Pull &amp; Generate).
	/// </summary>
	public static void RefreshIfOpen()
	{
		ProjectTasksWindow[] windows = Resources.FindObjectsOfTypeAll<ProjectTasksWindow>();
		if ( windows == null || windows.Length == 0 )
			return;

		for ( int i = 0; i < windows.Length; i++ )
		{
			ProjectTasksWindow window = windows[i];
			if ( window == null )
				continue;
			window.ReloadFromDisk();
			window.RefreshAllLists();
			window.RebuildDetails();
			window.UpdateSyncInfoLabel();
			window.UpdateQueueButton();
			window.SetStatus( "Refreshed after Milanote sync." );
			window.Repaint();
		}
	}

	void OnEnable()
	{
		ReloadFromDisk();
	}

	void OnDisable()
	{
		PersistLocalStore();
		CancelBusy();
	}

	void CreateGUI()
	{
		rootVisualElement.Clear();
		rootVisualElement.style.flexGrow = 1;
		rootVisualElement.UnregisterCallback<GeometryChangedEvent>( OnRootGeometryChanged );
		rootVisualElement.RegisterCallback<GeometryChangedEvent>( OnRootGeometryChanged );

		rootVisualElement.Add( BuildToolbar() );
		rootVisualElement.Add( BuildFilterBar() );

		_categoriesPanel = BuildCategoriesPanel();
		_featuresPanel = BuildFeatureListPanel();
		_detailsPanel = BuildDetailsPanel();
		_compactNavBar = BuildCompactNavBar();

		_contentHost = new VisualElement();
		_contentHost.style.flexGrow = 1;
		rootVisualElement.Add( _contentHost );

		_queuePanel = BuildQueuePanel();
		_queuePanel.style.display = DisplayStyle.None;
		rootVisualElement.Add( _queuePanel );

		_statusLabel = new Label( _statusText );
		_statusLabel.style.paddingLeft = 8;
		_statusLabel.style.paddingRight = 8;
		_statusLabel.style.paddingTop = 4;
		_statusLabel.style.paddingBottom = 4;
		_statusLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
		_statusLabel.style.flexShrink = 0;
		rootVisualElement.Add( _statusLabel );

		float width = position.width > 1f ? position.width : CompactWidthBreakpoint;
		_compactMode = width < CompactWidthBreakpoint;
		_lastLayoutWidth = width;
		ApplyChromeDensity();
		RebuildContentLayout();

		RefreshAllLists();
		RebuildDetails();
		UpdateQueueButton();
		ApplyContentMode();
	}

	void OnRootGeometryChanged( GeometryChangedEvent evt )
	{
		float width = evt.newRect.width;
		if ( width <= 1f )
			return;
		if ( Mathf.Abs( width - _lastLayoutWidth ) < 0.5f )
			return;

		_lastLayoutWidth = width;
		bool wantCompact = width < CompactWidthBreakpoint;
		if ( wantCompact == _compactMode )
			return;

		_compactMode = wantCompact;
		if ( !_compactMode )
			_compactPane = CompactPane.Categories;
		ApplyChromeDensity();
		RebuildContentLayout();
		ApplyContentMode();
	}

	VisualElement BuildToolbar()
	{
		_toolbarActions.Clear();
		_toolbar = new VisualElement();
		_toolbar.style.flexDirection = FlexDirection.Row;
		_toolbar.style.flexWrap = Wrap.Wrap;
		_toolbar.style.flexShrink = 0;
		_toolbar.style.alignItems = Align.Center;
		_toolbar.style.paddingLeft = 6;
		_toolbar.style.paddingRight = 6;
		_toolbar.style.paddingTop = 4;
		_toolbar.style.paddingBottom = 4;
		_toolbar.style.borderBottomWidth = 1;
		_toolbar.style.borderBottomColor = new Color( 0.2f, 0.2f, 0.2f );

		AddToolbarAction( _toolbar, "Sync Milanote", "Sync", () => StartSync() );
		AddToolbarAction( _toolbar, "Import Latest Export", "Import", () => ImportLatestExport() );
		AddToolbarAction( _toolbar, "Refresh", "Refresh", () =>
		{
			PersistLocalStore();
			ReloadFromDisk();
			RefreshAllLists();
			RebuildDetails();
			UpdateQueueButton();
			SetStatus( "Refreshed from disk." );
		} );
		AddToolbarAction( _toolbar, "Clear Generated Data…", "Clear…", () => ClearGeneratedData() );
		AddToolbarAction( _toolbar, "Open Cursor Folder", "Folder", () =>
		{
			EnsureOutputExists();
			EditorUtility.RevealInFinder( _outputRoot );
		} );
		AddToolbarAction( _toolbar, "Open CURRENT.md", "CURRENT", () => OpenPath( Path.Combine( _outputRoot, "CURRENT.md" ) ) );
		AddToolbarAction( _toolbar, "Settings", "Settings", () => MilanoteSyncWindow.Open() );

		_queueButton = AddToolbarAction( _toolbar, "Milanote Queue (0)", "Queue (0)", () => ToggleQueueView() );

		_syncInfoLabel = new Label();
		_syncInfoLabel.style.flexGrow = 1;
		_syncInfoLabel.style.minWidth = 80;
		_syncInfoLabel.style.unityTextAlign = TextAnchor.MiddleRight;
		_syncInfoLabel.style.paddingRight = 8;
		_syncInfoLabel.style.overflow = Overflow.Hidden;
		_syncInfoLabel.style.textOverflow = TextOverflow.Ellipsis;
		_syncInfoLabel.style.whiteSpace = WhiteSpace.NoWrap;
		UpdateSyncInfoLabel();
		_toolbar.Add( _syncInfoLabel );

		return _toolbar;
	}

	Button AddToolbarAction( VisualElement bar, string wideLabel, string compactLabel, Action onClick )
	{
		Button button = MakeToolbarButton( wideLabel, onClick );
		_toolbarActions.Add( new ToolbarAction
		{
			Button = button,
			WideLabel = wideLabel,
			CompactLabel = compactLabel
		} );
		bar.Add( button );
		return button;
	}

	void ApplyChromeDensity()
	{
		bool compact = _compactMode;

		for ( int i = 0; i < _toolbarActions.Count; i++ )
		{
			ToolbarAction action = _toolbarActions[i];
			if ( action.Button == null )
				continue;
			if ( action.Button == _queueButton )
				continue;
			action.Button.text = compact ? action.CompactLabel : action.WideLabel;
		}

		UpdateQueueButton();

		if ( _syncInfoLabel != null )
			_syncInfoLabel.style.display = compact ? DisplayStyle.None : DisplayStyle.Flex;

		if ( _globalSearchField != null )
		{
			_globalSearchField.style.minWidth = compact ? 120 : 180;
			_globalSearchField.style.width = compact ? 140 : 220;
		}

		if ( _filterBar != null )
			_filterBar.style.display = DisplayStyle.Flex;
	}

	VisualElement BuildFilterBar()
	{
		_filterBar = new VisualElement();
		_filterBar.style.flexDirection = FlexDirection.Row;
		_filterBar.style.flexWrap = Wrap.Wrap;
		_filterBar.style.flexShrink = 0;
		_filterBar.style.alignItems = Align.Center;
		_filterBar.style.paddingLeft = 6;
		_filterBar.style.paddingRight = 6;
		_filterBar.style.paddingTop = 4;
		_filterBar.style.paddingBottom = 4;
		_filterBar.style.borderBottomWidth = 1;
		_filterBar.style.borderBottomColor = new Color( 0.2f, 0.2f, 0.2f );

		// BaseField labels detach under flex-wrap; keep caption + control as separate siblings.
		var searchCell = MakeFilterCell( "Search" );
		_globalSearchField = new TextField { value = _globalSearch };
		_globalSearchField.style.minWidth = 180;
		_globalSearchField.style.width = 220;
		_globalSearchField.RegisterValueChangedCallback( evt =>
		{
			_globalSearch = evt.newValue ?? string.Empty;
			ApplyFilters();
		} );
		searchCell.Add( _globalSearchField );
		_filterBar.Add( searchCell );

		_statusFilter = AddFilterDropdown(
			_filterBar,
			"Status",
			new[] { "All", "Ready", "In Progress", "Complete" },
			_filterStatus,
			v => { _filterStatus = v; ApplyFilters(); } );

		_priorityFilter = AddFilterDropdown(
			_filterBar,
			"Priority",
			BuildPriorityChoices(),
			_filterPriority,
			v => { _filterPriority = v; ApplyFilters(); } );

		_assigneeFilter = AddFilterDropdown(
			_filterBar,
			"Assignee",
			BuildAssigneeChoices(),
			_filterAssignee,
			v => { _filterAssignee = v; ApplyFilters(); } );

		_verificationFilter = AddFilterDropdown(
			_filterBar,
			"Verification",
			new[] { "All", "Not Tested", "Testing", "Verified", "Failed" },
			_filterVerification,
			v => { _filterVerification = v; ApplyFilters(); } );

		_localFilter = AddFilterDropdown(
			_filterBar,
			"Local",
			new[] { "All", "Pending Milanote", "Blocked", "Needs Review", "Has Highlights" },
			_filterLocal,
			v => { _filterLocal = v; ApplyFilters(); } );

		return _filterBar;
	}

	static VisualElement MakeFilterCell( string caption )
	{
		var cell = new VisualElement();
		cell.style.flexDirection = FlexDirection.Row;
		cell.style.alignItems = Align.Center;
		cell.style.flexShrink = 0;
		cell.style.marginRight = 10;
		cell.style.marginTop = 2;
		cell.style.marginBottom = 2;

		var label = new Label( caption );
		label.style.marginRight = 4;
		label.style.unityTextAlign = TextAnchor.MiddleLeft;
		cell.Add( label );
		return cell;
	}

	DropdownField AddFilterDropdown(
		VisualElement bar,
		string caption,
		IEnumerable<string> choicesSource,
		string current,
		Action<string> onChanged )
	{
		var choices = new List<string>( choicesSource );
		if ( !choices.Contains( current ) )
			choices.Insert( 0, current );

		var cell = MakeFilterCell( caption );
		var field = new DropdownField( choices, Mathf.Max( 0, choices.IndexOf( current ) ) );
		field.style.minWidth = 110;
		field.RegisterValueChangedCallback( evt => onChanged( evt.newValue ) );
		cell.Add( field );
		bar.Add( cell );
		return field;
	}

	VisualElement BuildCategoriesPanel()
	{
		var panel = new VisualElement();
		panel.style.minWidth = 160;
		panel.style.flexGrow = 1;

		var header = new Label( "Categories" );
		header.style.unityFontStyleAndWeight = FontStyle.Bold;
		header.style.paddingLeft = 8;
		header.style.paddingTop = 6;
		panel.Add( header );

		_categorySearchField = new TextField( "Filter" );
		_categorySearchField.style.paddingLeft = 6;
		_categorySearchField.style.paddingRight = 6;
		_categorySearchField.RegisterValueChangedCallback( evt =>
		{
			_categorySearch = evt.newValue ?? string.Empty;
			ApplyFilters();
		} );
		panel.Add( _categorySearchField );

		_categoryList = new ListView();
		_categoryList.style.flexGrow = 1;
		_categoryList.selectionType = SelectionType.Single;
		_categoryList.fixedItemHeight = 28;
		_categoryList.makeItem = () =>
		{
			var label = new Label();
			label.style.paddingLeft = 8;
			label.style.paddingRight = 8;
			label.style.paddingTop = 4;
			label.style.paddingBottom = 4;
			label.style.overflow = Overflow.Hidden;
			label.style.textOverflow = TextOverflow.Ellipsis;
			label.style.whiteSpace = WhiteSpace.NoWrap;
			label.style.unityTextAlign = TextAnchor.MiddleLeft;
			label.style.borderLeftWidth = 4;
			label.style.borderLeftColor = Color.clear;
			return label;
		};
		_categoryList.bindItem = ( element, index ) =>
		{
			var label = (Label)element;
			if ( index < 0 || index >= _visibleCategories.Count )
			{
				label.text = string.Empty;
				ApplyTintBorder( label, ProjectTasksTint.None );
				return;
			}

			ImportedCategory category = _visibleCategories[index];
			ImportedCategory tintSource = ResolveCatalogCategory( category.Name ) ?? category;
			int pending = CountPendingInCategory( tintSource );
			string pendingSuffix = pending > 0 ? " ↑" + pending : string.Empty;
			label.text = category.Name + " (" + category.ActiveCount + "/" + category.Features.Count + ")" + pendingSuffix;
			ApplyTintBorder( label, _localStore.GetCategoryTint( tintSource ) );
		};
		_categoryList.selectedIndicesChanged += indices =>
		{
			ImportedCategory selected = null;
			foreach ( int index in indices )
			{
				if ( index >= 0 && index < _visibleCategories.Count )
				{
					selected = _visibleCategories[index];
					break;
				}
			}

			_selectedCategory = selected;
			_selectedFeature = null;
			RebuildFeatureList();
			if ( !_compactMode && _visibleFeatures.Count > 0 )
			{
				_selectedFeature = _visibleFeatures[0];
				_featureList.SetSelectionWithoutNotify( new[] { 0 } );
			}
			else if ( _featureList != null )
			{
				_featureList.ClearSelection();
			}

			RebuildDetails();
			if ( _compactMode && selected != null )
				ShowCompactPane( CompactPane.Features );
			else
				UpdateCompactNav();
		};
		panel.Add( _categoryList );
		panel.style.flexGrow = 1;
		return panel;
	}

	VisualElement BuildFeatureListPanel()
	{
		var panel = new VisualElement();
		panel.style.minWidth = 160;
		panel.style.flexGrow = 1;

		var headerRow = new VisualElement();
		headerRow.style.flexDirection = FlexDirection.Row;
		headerRow.style.paddingLeft = 6;
		headerRow.style.paddingTop = 6;
		headerRow.style.paddingBottom = 4;
		headerRow.Add( MakeSortButton( "Order", FeatureSortColumn.Name ) );
		headerRow.Add( MakeSortButton( "Status", FeatureSortColumn.Status ) );
		headerRow.Add( MakeSortButton( "Priority", FeatureSortColumn.Priority ) );
		headerRow.Add( MakeSortButton( "Verify", FeatureSortColumn.Verification ) );
		panel.Add( headerRow );

		_featureList = new ListView();
		_featureList.style.flexGrow = 1;
		_featureList.selectionType = SelectionType.Single;
		_featureList.fixedItemHeight = 44;
		_featureList.makeItem = () =>
		{
			var root = new VisualElement();
			root.style.paddingLeft = 8;
			root.style.paddingRight = 8;
			root.style.paddingTop = 4;
			root.style.paddingBottom = 4;
			root.style.justifyContent = Justify.Center;
			root.style.overflow = Overflow.Hidden;
			root.style.borderLeftWidth = 4;
			root.style.borderLeftColor = Color.clear;

			var name = new Label { name = "name" };
			name.style.unityFontStyleAndWeight = FontStyle.Bold;
			name.style.overflow = Overflow.Hidden;
			name.style.textOverflow = TextOverflow.Ellipsis;
			name.style.whiteSpace = WhiteSpace.NoWrap;
			var meta = new Label { name = "meta" };
			meta.style.fontSize = 10;
			meta.style.color = new Color( 0.7f, 0.7f, 0.7f );
			meta.style.overflow = Overflow.Hidden;
			meta.style.textOverflow = TextOverflow.Ellipsis;
			meta.style.whiteSpace = WhiteSpace.NoWrap;
			root.Add( name );
			root.Add( meta );
			return root;
		};
		_featureList.bindItem = ( element, index ) =>
		{
			if ( index < 0 || index >= _visibleFeatures.Count )
				return;

			ImportedFeature feature = _visibleFeatures[index];
			LocalFeatureMetadata local = _localStore.GetOrCreate( feature.FeatureId );
			int pending = _localStore.CountPendingMilanote( feature );
			int localProgress = _localStore.CountLocalProgress( feature );
			var name = element.Q<Label>( "name" );
			var meta = element.Q<Label>( "meta" );
			string pendingBadge = pending > 0 ? "  ↑" + pending : string.Empty;
			name.text = feature.Name + pendingBadge;
			meta.text = feature.Status
				+ " · " + feature.MilanoteProgressLabel
				+ " · L:" + localProgress + "/" + feature.TotalTaskCount
				+ " · " + ( string.IsNullOrEmpty( local.Priority ) ? "-" : local.Priority )
				+ " · " + FormatVerification( local.VerificationStatus )
				+ ( string.IsNullOrEmpty( local.AssignedDeveloper ) ? "" : " · " + local.AssignedDeveloper );
			ApplyTintBorder( element, _localStore.GetFeatureTint( feature ) );
		};
		_featureList.selectedIndicesChanged += indices =>
		{
			ImportedFeature selected = null;
			foreach ( int index in indices )
			{
				if ( index >= 0 && index < _visibleFeatures.Count )
				{
					selected = _visibleFeatures[index];
					break;
				}
			}

			_selectedFeature = selected;
			RebuildDetails();
			if ( _compactMode && selected != null )
				ShowCompactPane( CompactPane.Details );
			else
				UpdateCompactNav();
		};
		panel.Add( _featureList );
		panel.style.flexGrow = 1;
		return panel;
	}

	VisualElement BuildDetailsPanel()
	{
		var panel = new VisualElement();
		panel.style.minWidth = 160;
		panel.style.flexGrow = 1;

		var header = new Label( "Feature Details" );
		header.style.unityFontStyleAndWeight = FontStyle.Bold;
		header.style.paddingLeft = 8;
		header.style.paddingTop = 6;
		panel.Add( header );

		_detailsScroll = new ScrollView( ScrollViewMode.Vertical );
		_detailsScroll.style.flexGrow = 1;
		panel.Add( _detailsScroll );
		return panel;
	}

	VisualElement BuildQueuePanel()
	{
		var panel = new VisualElement();
		panel.style.flexGrow = 1;
		panel.style.paddingLeft = 8;
		panel.style.paddingRight = 8;
		panel.style.paddingTop = 6;

		var headerRow = new VisualElement();
		headerRow.style.flexDirection = FlexDirection.Row;
		headerRow.style.marginBottom = 6;

		var title = new Label( "Milanote Queue — locally done, not yet ticked in Milanote" );
		title.style.unityFontStyleAndWeight = FontStyle.Bold;
		title.style.flexGrow = 1;
		headerRow.Add( title );

		headerRow.Add( MakeToolbarButton( "Copy Checklist", () => CopyMilanoteQueueChecklist() ) );
		headerRow.Add( MakeToolbarButton( "Back to Features", () =>
		{
			_showingQueue = false;
			ApplyContentMode();
		} ) );
		panel.Add( headerRow );

		_queueList = new ListView();
		_queueList.style.flexGrow = 1;
		_queueList.selectionType = SelectionType.Single;
		_queueList.fixedItemHeight = 52;
		_queueList.makeItem = () =>
		{
			var root = new VisualElement();
			root.style.paddingLeft = 8;
			root.style.paddingRight = 8;
			root.style.paddingTop = 4;
			root.style.paddingBottom = 4;
			root.style.justifyContent = Justify.Center;
			root.style.backgroundColor = TintAmber;

			var line = new Label { name = "line" };
			line.style.unityFontStyleAndWeight = FontStyle.Bold;
			line.style.whiteSpace = WhiteSpace.Normal;
			var comment = new Label { name = "comment" };
			comment.style.fontSize = 10;
			comment.style.color = new Color( 0.75f, 0.75f, 0.75f );
			root.Add( line );
			root.Add( comment );
			return root;
		};
		_queueList.bindItem = ( element, index ) =>
		{
			if ( index < 0 || index >= _pendingQueue.Count )
				return;

			PendingMilanoteItem item = _pendingQueue[index];
			var line = element.Q<Label>( "line" );
			var comment = element.Q<Label>( "comment" );
			line.text = item.Feature.Category + " · " + item.Feature.Name
				+ " · [" + item.Task.Section + "] " + item.Task.Text;
			string note = item.TaskMeta != null ? item.TaskMeta.Comment : string.Empty;
			comment.text = string.IsNullOrWhiteSpace( note ) ? "" : note;
			comment.style.display = string.IsNullOrWhiteSpace( note ) ? DisplayStyle.None : DisplayStyle.Flex;
		};
		_queueList.selectedIndicesChanged += indices =>
		{
			PendingMilanoteItem selected = null;
			foreach ( int index in indices )
			{
				if ( index >= 0 && index < _pendingQueue.Count )
				{
					selected = _pendingQueue[index];
					break;
				}
			}

			if ( selected == null || selected.Feature == null )
				return;

			SelectFeature( selected.Feature );
			_showingQueue = false;
			ApplyContentMode();
			RebuildDetails();
		};
		panel.Add( _queueList );
		return panel;
	}

	void RebuildDetails()
	{
		if ( _detailsScroll == null )
			return;

		_detailsScroll.Clear();
		if ( _selectedFeature == null )
		{
			_detailsScroll.Add( new Label( "Select a feature." ) );
			return;
		}

		ImportedFeature feature = _selectedFeature;
		LocalFeatureMetadata local = _localStore.GetOrCreate( feature.FeatureId );

		var title = new Label( feature.Name );
		title.style.unityFontStyleAndWeight = FontStyle.Bold;
		title.style.fontSize = 16;
		title.style.paddingLeft = 8;
		title.style.paddingTop = 4;
		_detailsScroll.Add( title );

		_detailsScroll.Add( new Label( "Category: " + feature.Category ) );
		_detailsScroll.Add( new Label( "Status: " + feature.Status + ( feature.IsCompleteFolder ? " (in _complete)" : "" ) ) );
		_detailsScroll.Add( new Label(
			"Progress: " + feature.MilanoteProgressLabel
			+ " · L:" + _localStore.CountLocalProgress( feature ) + "/" + feature.TotalTaskCount
			+ " · Pending Milanote: " + _localStore.CountPendingMilanote( feature ) ) );
		_detailsScroll.Add( new Label( "Last Sync: " + ( feature.LastSync ?? "-" ) ) );
		_detailsScroll.Add( new Label( "ID: " + feature.FeatureId ) );

		_detailsScroll.Add( BuildPlanningEditors( local ) );
		_detailsScroll.Add( BuildActionRow( feature ) );

		AddTasksFoldout( feature, local );
		AddMarkdownFoldout( "Requirements", feature.Requirements );
		AddMarkdownFoldout( "Notes", feature.Notes );
		AddMarkdownFoldout( "Bugs", feature.Bugs );
		AddMarkdownFoldout( "Review", feature.Review );
		AddMarkdownFoldout( "AI Context", feature.AiContext );
		AddTextFoldout( "Cursor Implementation", feature.CursorImplementation );
		AddFilesFoldout( feature.FilesModified );
		AddTextFoldout( "Testing Instructions", feature.TestingInstructions );
		AddTextFoldout( "Cursor Notes", feature.CursorNotes );
		_detailsScroll.Add( BuildVerificationEditors( feature, local ) );
		AddTextFoldout( "Developer Verification (from Markdown)", feature.DeveloperVerificationMarkdown );
	}

	VisualElement BuildPlanningEditors( LocalFeatureMetadata local )
	{
		var box = new VisualElement();
		box.style.flexDirection = FlexDirection.Row;
		box.style.flexWrap = Wrap.Wrap;
		box.style.paddingLeft = 4;
		box.style.paddingTop = 4;
		box.style.paddingBottom = 4;

		var priority = new TextField( "Priority" ) { value = local.Priority ?? string.Empty };
		priority.style.minWidth = 160;
		priority.RegisterValueChangedCallback( evt =>
		{
			local.Priority = evt.newValue ?? string.Empty;
			PersistLocalStore();
			RebuildFeatureList();
		} );
		box.Add( priority );

		var assignee = new TextField( "Assignee" ) { value = local.AssignedDeveloper ?? string.Empty };
		assignee.style.minWidth = 160;
		assignee.RegisterValueChangedCallback( evt =>
		{
			local.AssignedDeveloper = evt.newValue ?? string.Empty;
			PersistLocalStore();
			RebuildFeatureList();
		} );
		box.Add( assignee );

		return box;
	}

	VisualElement BuildActionRow( ImportedFeature feature )
	{
		var row = new VisualElement();
		row.style.flexDirection = FlexDirection.Row;
		row.style.flexWrap = Wrap.Wrap;
		row.style.paddingTop = 6;
		row.style.paddingBottom = 6;
		row.style.paddingLeft = 4;

		row.Add( MakeToolbarButton( "Open Feature Markdown", () => OpenPath( feature.AbsolutePath ) ) );
		row.Add( MakeToolbarButton( "Reveal Feature Folder", () =>
		{
			string dir = Path.GetDirectoryName( feature.AbsolutePath );
			if ( !string.IsNullOrEmpty( dir ) )
				EditorUtility.RevealInFinder( dir );
		} ) );
		row.Add( MakeToolbarButton( "Copy Cursor Context", () =>
		{
			EditorGUIUtility.systemCopyBuffer = BuildCursorContext( feature );
			SetStatus( "Copied Cursor context to clipboard." );
		} ) );
		return row;
	}

	VisualElement BuildVerificationEditors( ImportedFeature feature, LocalFeatureMetadata local )
	{
		var box = new Foldout { text = "Developer Verification (Local)", value = true };

		var status = new EnumField( "Status", local.VerificationStatus );
		status.RegisterValueChangedCallback( evt =>
		{
			local.VerificationStatus = (LocalVerificationStatus)evt.newValue;
			if ( local.VerificationStatus == LocalVerificationStatus.Verified
			     || local.VerificationStatus == LocalVerificationStatus.Failed )
				local.VerificationDate = DateTime.UtcNow.ToString( "yyyy-MM-dd" );
			PersistLocalStore();
			RebuildFeatureList();
		} );
		box.Add( status );

		var comments = new TextField( "Comments" )
		{
			value = local.VerificationComments ?? string.Empty,
			multiline = true
		};
		comments.style.minHeight = 70;
		comments.RegisterValueChangedCallback( evt =>
		{
			local.VerificationComments = evt.newValue ?? string.Empty;
			PersistLocalStore();
		} );
		box.Add( comments );
		return box;
	}

	void AddMarkdownFoldout( string title, List<string> bullets )
	{
		var foldout = new Foldout { text = title + " (" + ( bullets != null ? bullets.Count : 0 ) + ")", value = false };
		if ( bullets == null || bullets.Count == 0 )
		{
			foldout.Add( new Label( "_(none)_" ) );
		}
		else
		{
			for ( int i = 0; i < bullets.Count; i++ )
				foldout.Add( new Label( "• " + bullets[i] ) );
		}

		_detailsScroll.Add( foldout );
	}

	void AddTextFoldout( string title, string body )
	{
		var foldout = new Foldout { text = title, value = false };
		var label = new Label( string.IsNullOrWhiteSpace( body ) ? "_(empty)_" : body.TrimEnd() );
		label.style.whiteSpace = WhiteSpace.Normal;
		foldout.Add( label );
		_detailsScroll.Add( foldout );
	}

	void AddFilesFoldout( string filesModified )
	{
		var foldout = new Foldout { text = "Files Modified", value = true };
		List<string> files = ParseFileList( filesModified );
		if ( files.Count == 0 )
		{
			foldout.Add( new Label( "_(none)_" ) );
		}
		else
		{
			for ( int i = 0; i < files.Count; i++ )
			{
				string file = files[i];
				var row = new VisualElement();
				row.style.flexDirection = FlexDirection.Row;
				var button = new Button( () => OpenCodeFile( file ) ) { text = file };
				button.style.unityTextAlign = TextAnchor.MiddleLeft;
				button.style.flexGrow = 1;
				row.Add( button );
				foldout.Add( row );
			}
		}

		_detailsScroll.Add( foldout );
	}

	void AddTasksFoldout( ImportedFeature feature, LocalFeatureMetadata local )
	{
		var foldout = new Foldout
		{
			text = "Tasks (" + feature.TaskProgressLabel + ") — Milanote completion + local work status",
			value = true
		};

		if ( feature.AllTasks.Count == 0 )
		{
			foldout.Add( new Label( "_(none)_" ) );
			_detailsScroll.Add( foldout );
			return;
		}

		for ( int i = 0; i < feature.AllTasks.Count; i++ )
		{
			ImportedTask task = feature.AllTasks[i];
			LocalTaskMetadata taskMeta = local.GetOrCreateTask( task.Key );

			var card = new VisualElement();
			card.style.marginBottom = 6;
			card.style.paddingLeft = 6;
			card.style.paddingRight = 6;
			card.style.paddingTop = 4;
			card.style.paddingBottom = 4;
			card.style.borderBottomWidth = 1;
			card.style.borderBottomColor = new Color( 0.25f, 0.25f, 0.25f );
			ApplyTaskCardTint( card, task, taskMeta );

			string mark = task.IsComplete ? "[x]" : "[ ]";
			var title = new Label( mark + " [" + task.Section + "] " + task.Text );
			title.style.whiteSpace = WhiteSpace.Normal;
			card.Add( title );

			var milanoteComplete = new Toggle( "Milanote complete" ) { value = task.IsComplete };
			milanoteComplete.SetEnabled( false );
			card.Add( milanoteComplete );

			LocalTaskMetadata capturedMeta = taskMeta;
			ImportedTask capturedTask = task;
			bool locallyDone = capturedMeta.WorkStatus == LocalTaskWorkStatus.Implemented || capturedTask.IsComplete;
			var locallyDoneToggle = new Toggle( "Locally done" ) { value = locallyDone };
			if ( capturedTask.IsComplete )
				locallyDoneToggle.SetEnabled( false );

			var status = new EnumField( "Local Status", capturedMeta.WorkStatus );

			locallyDoneToggle.RegisterValueChangedCallback( evt =>
			{
				if ( capturedTask.IsComplete )
					return;

				if ( evt.newValue )
					capturedMeta.WorkStatus = LocalTaskWorkStatus.Implemented;
				else if ( capturedMeta.WorkStatus == LocalTaskWorkStatus.Implemented )
					capturedMeta.WorkStatus = LocalTaskWorkStatus.None;

				status.SetValueWithoutNotify( capturedMeta.WorkStatus );
				PersistLocalStore();
				OnLocalTaskChanged();
			} );
			card.Add( locallyDoneToggle );

			status.RegisterValueChangedCallback( evt =>
			{
				capturedMeta.WorkStatus = (LocalTaskWorkStatus)evt.newValue;
				bool done = capturedMeta.WorkStatus == LocalTaskWorkStatus.Implemented || capturedTask.IsComplete;
				locallyDoneToggle.SetValueWithoutNotify( done );
				PersistLocalStore();
				OnLocalTaskChanged();
			} );
			card.Add( status );

			var highlight = new Toggle( "Highlight" ) { value = taskMeta.Highlighted };
			highlight.RegisterValueChangedCallback( evt =>
			{
				capturedMeta.Highlighted = evt.newValue;
				PersistLocalStore();
				RebuildDetails();
			} );
			card.Add( highlight );

			var comment = new TextField( "Comment" ) { value = taskMeta.Comment ?? string.Empty };
			comment.RegisterValueChangedCallback( evt =>
			{
				capturedMeta.Comment = evt.newValue ?? string.Empty;
				PersistLocalStore();
			} );
			card.Add( comment );

			foldout.Add( card );
		}

		_detailsScroll.Add( foldout );
	}

	void OnLocalTaskChanged()
	{
		UpdateQueueButton();
		RebuildCategoryList();
		RebuildFeatureList();
		RebuildDetails();
		if ( _showingQueue )
			RebuildQueueList();
	}

	void ApplyTaskCardTint( VisualElement card, ImportedTask task, LocalTaskMetadata taskMeta )
	{
		if ( taskMeta != null && taskMeta.Highlighted && !task.IsComplete
		     && !ProjectTasksLocalStore.IsLocallyDonePendingMilanote( task, taskMeta ) )
		{
			card.style.backgroundColor = TintHighlight;
			return;
		}

		if ( task.IsComplete )
		{
			card.style.backgroundColor = TintGreen;
			return;
		}

		if ( ProjectTasksLocalStore.IsLocallyDonePendingMilanote( task, taskMeta ) )
		{
			card.style.backgroundColor = TintAmber;
			return;
		}

		card.style.backgroundColor = StyleKeyword.Null;
	}

	static void ApplyTintBorder( VisualElement element, ProjectTasksTint tint )
	{
		if ( element == null )
			return;

		element.style.borderLeftWidth = 4;
		switch ( tint )
		{
			case ProjectTasksTint.Green:
				element.style.borderLeftColor = TintGreen;
				break;
			case ProjectTasksTint.Amber:
				element.style.borderLeftColor = TintAmber;
				break;
			default:
				element.style.borderLeftColor = Color.clear;
				break;
		}
	}

	void ReloadFromDisk()
	{
		_outputRoot = MilanoteSyncService.ResolveOutputRoot();
		_catalog = FeatureMarkdownReader.Load( _outputRoot );
		_localStore = ProjectTasksLocalStore.Load( _outputRoot );
		if ( _localStore.ApplyMilanoteCompletionAssumptions( _catalog ) )
			PersistLocalStore();
		ApplyFilters( refreshUi: false );
		UpdateSyncInfoLabel();
		UpdateQueueButton();

		for ( int i = 0; i < _catalog.LoadWarnings.Count; i++ )
			Debug.LogWarning( "[Project Tasks] " + _catalog.LoadWarnings[i] );
	}

	void PersistLocalStore()
	{
		if ( _localStore == null || string.IsNullOrEmpty( _outputRoot ) )
			return;
		_localStore.Save( _outputRoot );
	}

	void RefreshAllLists()
	{
		ApplyFilters();
		UpdateQueueButton();
	}

	void ApplyFilters( bool refreshUi = true )
	{
		if ( _catalog == null )
			return;

		_visibleCategories = new List<ImportedCategory>();
		for ( int i = 0; i < _catalog.Categories.Count; i++ )
		{
			ImportedCategory category = _catalog.Categories[i];
			if ( !string.IsNullOrWhiteSpace( _categorySearch )
			     && category.Name.IndexOf( _categorySearch, StringComparison.OrdinalIgnoreCase ) < 0 )
				continue;

			var filteredFeatures = FilterFeaturesOnly( category.Features );
			if ( filteredFeatures.Count == 0 )
				continue;

			var viewCategory = new ImportedCategory { Name = category.Name };
			viewCategory.Features.AddRange( filteredFeatures );
			_visibleCategories.Add( viewCategory );
		}

		if ( _selectedCategory != null )
		{
			ImportedCategory match = null;
			for ( int i = 0; i < _visibleCategories.Count; i++ )
			{
				if ( string.Equals( _visibleCategories[i].Name, _selectedCategory.Name, StringComparison.OrdinalIgnoreCase ) )
				{
					match = _visibleCategories[i];
					break;
				}
			}

			_selectedCategory = match;
		}

		if ( refreshUi )
		{
			RebuildCategoryList();
			RebuildFeatureList();
			UpdateCompactNav();
		}
	}

	List<ImportedFeature> FilterFeaturesOnly( List<ImportedFeature> features )
	{
		var list = new List<ImportedFeature>();
		for ( int i = 0; i < features.Count; i++ )
		{
			if ( FeatureMatchesFilters( features[i] ) )
				list.Add( features[i] );
		}

		return list;
	}

	bool FeatureMatchesFilters( ImportedFeature feature )
	{
		LocalFeatureMetadata local = _localStore.GetOrCreate( feature.FeatureId );

		if ( !string.Equals( _filterStatus, "All", StringComparison.OrdinalIgnoreCase ) )
		{
			if ( feature.Status == null
			     || feature.Status.IndexOf( _filterStatus, StringComparison.OrdinalIgnoreCase ) < 0 )
				return false;
		}

		if ( !string.Equals( _filterPriority, "All", StringComparison.OrdinalIgnoreCase ) )
		{
			if ( !string.Equals( local.Priority ?? string.Empty, _filterPriority, StringComparison.OrdinalIgnoreCase ) )
				return false;
		}

		if ( !string.Equals( _filterAssignee, "All", StringComparison.OrdinalIgnoreCase ) )
		{
			if ( !string.Equals( local.AssignedDeveloper ?? string.Empty, _filterAssignee, StringComparison.OrdinalIgnoreCase ) )
				return false;
		}

		if ( !string.Equals( _filterVerification, "All", StringComparison.OrdinalIgnoreCase ) )
		{
			if ( !string.Equals( FormatVerification( local.VerificationStatus ), _filterVerification, StringComparison.OrdinalIgnoreCase ) )
				return false;
		}

		if ( !_localStore.FeatureMatchesLocalFilter( feature, _filterLocal ) )
			return false;

		if ( string.IsNullOrWhiteSpace( _globalSearch ) )
			return true;

		return FeatureMatchesSearch( feature, _globalSearch );
	}

	static bool FeatureMatchesSearch( ImportedFeature feature, string search )
	{
		if ( Contains( feature.Name, search )
		     || Contains( feature.Category, search )
		     || Contains( feature.Status, search )
		     || Contains( feature.CursorNotes, search )
		     || Contains( feature.CursorImplementation, search )
		     || Contains( feature.TestingInstructions, search ) )
			return true;

		if ( ListContains( feature.Requirements, search )
		     || ListContains( feature.Tasks, search )
		     || ListContains( feature.Notes, search )
		     || ListContains( feature.Bugs, search )
		     || ListContains( feature.Review, search )
		     || ListContains( feature.AiContext, search ) )
			return true;

		return false;
	}

	static bool ListContains( List<string> items, string search )
	{
		if ( items == null )
			return false;
		for ( int i = 0; i < items.Count; i++ )
		{
			if ( Contains( items[i], search ) )
				return true;
		}

		return false;
	}

	static bool Contains( string value, string search )
	{
		return !string.IsNullOrEmpty( value )
		       && value.IndexOf( search, StringComparison.OrdinalIgnoreCase ) >= 0;
	}

	void RebuildCategoryList()
	{
		if ( _categoryList == null )
			return;

		_categoryList.itemsSource = _visibleCategories;
		_categoryList.RefreshItems();

		if ( _selectedCategory != null )
		{
			int index = IndexOfCategoryByName( _selectedCategory.Name );
			if ( index >= 0 )
			{
				_selectedCategory = _visibleCategories[index];
				_categoryList.SetSelectionWithoutNotify( new[] { index } );
			}
		}
	}

	void RebuildFeatureList()
	{
		if ( _featureList == null )
			return;

		_visibleFeatures = new List<ImportedFeature>();
		if ( _selectedCategory != null )
			_visibleFeatures.AddRange( _selectedCategory.Features );
		else if ( !string.IsNullOrWhiteSpace( _globalSearch ) )
		{
			for ( int i = 0; i < _visibleCategories.Count; i++ )
				_visibleFeatures.AddRange( _visibleCategories[i].Features );
		}

		SortFeatures( _visibleFeatures );
		_featureList.itemsSource = _visibleFeatures;
		_featureList.RefreshItems();

		if ( _selectedFeature != null )
		{
			int index = -1;
			for ( int i = 0; i < _visibleFeatures.Count; i++ )
			{
				if ( string.Equals( _visibleFeatures[i].FeatureId, _selectedFeature.FeatureId, StringComparison.Ordinal ) )
				{
					index = i;
					_selectedFeature = _visibleFeatures[i];
					break;
				}
			}

			if ( index >= 0 )
				_featureList.SetSelectionWithoutNotify( new[] { index } );
			else
				_selectedFeature = null;
		}
	}

	int IndexOfCategoryByName( string name )
	{
		if ( string.IsNullOrEmpty( name ) )
			return -1;

		for ( int i = 0; i < _visibleCategories.Count; i++ )
		{
			if ( string.Equals( _visibleCategories[i].Name, name, StringComparison.OrdinalIgnoreCase ) )
				return i;
		}

		return -1;
	}

	void SortFeatures( List<ImportedFeature> features )
	{
		features.Sort( ( a, b ) =>
		{
			int cmp = CompareFeatures( a, b, _sortColumn );
			return _sortAscending ? cmp : -cmp;
		} );
	}

	int CompareFeatures( ImportedFeature a, ImportedFeature b, FeatureSortColumn column )
	{
		LocalFeatureMetadata la = _localStore.GetOrCreate( a.FeatureId );
		LocalFeatureMetadata lb = _localStore.GetOrCreate( b.FeatureId );
		switch ( column )
		{
			case FeatureSortColumn.Status:
				return string.Compare( a.Status, b.Status, StringComparison.OrdinalIgnoreCase );
			case FeatureSortColumn.Priority:
				return string.Compare( la.Priority, lb.Priority, StringComparison.OrdinalIgnoreCase );
			case FeatureSortColumn.Assignee:
				return string.Compare( la.AssignedDeveloper, lb.AssignedDeveloper, StringComparison.OrdinalIgnoreCase );
			case FeatureSortColumn.LastSync:
				return string.Compare( a.LastSync, b.LastSync, StringComparison.OrdinalIgnoreCase );
			case FeatureSortColumn.Verification:
				return la.VerificationStatus.CompareTo( lb.VerificationStatus );
			default:
			{
				int cat = KeywordHierarchyParser.CompareSortOrder(
					a.CategorySortScore, a.CategorySortIndex, a.Category,
					b.CategorySortScore, b.CategorySortIndex, b.Category );
				if ( cat != 0 )
					return cat;
				return KeywordHierarchyParser.CompareSortOrder(
					a.SortScore, a.SortIndex, a.Name,
					b.SortScore, b.SortIndex, b.Name );
			}
		}
	}

	Button MakeSortButton( string label, FeatureSortColumn column )
	{
		var button = new Button( () =>
		{
			if ( _sortColumn == column )
				_sortAscending = !_sortAscending;
			else
			{
				_sortColumn = column;
				_sortAscending = true;
			}

			RebuildFeatureList();
		} )
		{
			text = label
		};
		button.style.marginRight = 4;
		return button;
	}

	List<string> BuildPriorityChoices()
	{
		var set = new HashSet<string>( StringComparer.OrdinalIgnoreCase ) { "All" };
		if ( _localStore != null )
		{
			foreach ( KeyValuePair<string, LocalFeatureMetadata> pair in _localStore.Features )
			{
				if ( !string.IsNullOrWhiteSpace( pair.Value.Priority ) )
					set.Add( pair.Value.Priority.Trim() );
			}
		}

		var list = set.ToList();
		list.Sort( StringComparer.OrdinalIgnoreCase );
		if ( !list.Contains( "All" ) )
			list.Insert( 0, "All" );
		else
		{
			list.Remove( "All" );
			list.Insert( 0, "All" );
		}

		return list;
	}

	List<string> BuildAssigneeChoices()
	{
		var set = new HashSet<string>( StringComparer.OrdinalIgnoreCase ) { "All" };
		if ( _localStore != null )
		{
			foreach ( KeyValuePair<string, LocalFeatureMetadata> pair in _localStore.Features )
			{
				if ( !string.IsNullOrWhiteSpace( pair.Value.AssignedDeveloper ) )
					set.Add( pair.Value.AssignedDeveloper.Trim() );
			}
		}

		var list = set.ToList();
		list.Sort( StringComparer.OrdinalIgnoreCase );
		list.Remove( "All" );
		list.Insert( 0, "All" );
		return list;
	}

	Button MakeToolbarButton( string text, Action onClick )
	{
		var button = new Button( onClick ) { text = text };
		button.style.marginRight = 4;
		button.style.flexShrink = 0;
		button.SetEnabled( !_busy );
		return button;
	}

	void ToggleQueueView()
	{
		_showingQueue = !_showingQueue;
		ApplyContentMode();
	}

	void ApplyContentMode()
	{
		if ( _contentHost == null || _queuePanel == null )
			return;

		if ( _showingQueue )
		{
			RebuildQueueList();
			_contentHost.style.display = DisplayStyle.None;
			_queuePanel.style.display = DisplayStyle.Flex;
		}
		else
		{
			_queuePanel.style.display = DisplayStyle.None;
			_contentHost.style.display = DisplayStyle.Flex;
			if ( _contentHost.childCount == 0 )
				RebuildContentLayout();
		}
	}

	VisualElement BuildCompactNavBar()
	{
		var bar = new VisualElement();
		bar.style.flexDirection = FlexDirection.Row;
		bar.style.flexWrap = Wrap.Wrap;
		bar.style.flexShrink = 0;
		bar.style.alignItems = Align.Center;
		bar.style.paddingLeft = 6;
		bar.style.paddingRight = 6;
		bar.style.paddingTop = 4;
		bar.style.paddingBottom = 4;
		bar.style.borderBottomWidth = 1;
		bar.style.borderBottomColor = new Color( 0.2f, 0.2f, 0.2f );

		_compactBackButton = MakeToolbarButton( "Back", () => CompactGoBack() );
		bar.Add( _compactBackButton );

		_compactCategoriesButton = MakeToolbarButton( "Categories", () => ShowCompactPane( CompactPane.Categories ) );
		bar.Add( _compactCategoriesButton );
		_compactFeaturesButton = MakeToolbarButton( "Features", () => ShowCompactPane( CompactPane.Features ) );
		bar.Add( _compactFeaturesButton );
		_compactDetailsButton = MakeToolbarButton( "Details", () => ShowCompactPane( CompactPane.Details ) );
		bar.Add( _compactDetailsButton );

		_compactContextLabel = new Label();
		_compactContextLabel.style.flexGrow = 1;
		_compactContextLabel.style.unityTextAlign = TextAnchor.MiddleRight;
		_compactContextLabel.style.paddingLeft = 8;
		_compactContextLabel.style.paddingRight = 4;
		_compactContextLabel.style.overflow = Overflow.Hidden;
		_compactContextLabel.style.textOverflow = TextOverflow.Ellipsis;
		_compactContextLabel.style.whiteSpace = WhiteSpace.NoWrap;
		bar.Add( _compactContextLabel );

		return bar;
	}

	void RebuildContentLayout()
	{
		if ( _contentHost == null || _categoriesPanel == null || _featuresPanel == null || _detailsPanel == null )
			return;

		_contentHost.Clear();

		if ( _compactMode )
		{
			if ( _compactNavBar != null )
				_contentHost.Add( _compactNavBar );
			ApplyCompactPane();
		}
		else
		{
			var mainSplits = new TwoPaneSplitView( 0, 220f, TwoPaneSplitViewOrientation.Horizontal );
			mainSplits.style.flexGrow = 1;

			var rightSplit = new TwoPaneSplitView( 0, 340f, TwoPaneSplitViewOrientation.Horizontal );
			rightSplit.Add( _featuresPanel );
			rightSplit.Add( _detailsPanel );

			mainSplits.Add( _categoriesPanel );
			mainSplits.Add( rightSplit );
			_contentHost.Add( mainSplits );

			_categoriesPanel.style.display = DisplayStyle.Flex;
			_featuresPanel.style.display = DisplayStyle.Flex;
			_detailsPanel.style.display = DisplayStyle.Flex;

			RefreshListViewsAfterReparent();
		}
	}

	void ShowCompactPane( CompactPane pane )
	{
		if ( pane == CompactPane.Details && _selectedFeature == null )
			pane = _selectedCategory != null ? CompactPane.Features : CompactPane.Categories;
		else if ( pane == CompactPane.Features && _selectedCategory == null )
			pane = CompactPane.Categories;

		_compactPane = pane;
		if ( _compactMode )
			ApplyCompactPane();
		else
			UpdateCompactNav();
	}

	void CompactGoBack()
	{
		if ( _compactPane == CompactPane.Details )
			ShowCompactPane( CompactPane.Features );
		else if ( _compactPane == CompactPane.Features )
			ShowCompactPane( CompactPane.Categories );
	}

	void ApplyCompactPane()
	{
		if ( _contentHost == null || !_compactMode )
			return;

		if ( _compactNavBar != null && _compactNavBar.parent != _contentHost )
		{
			_contentHost.Clear();
			_contentHost.Add( _compactNavBar );
		}

		_categoriesPanel.RemoveFromHierarchy();
		_featuresPanel.RemoveFromHierarchy();
		_detailsPanel.RemoveFromHierarchy();

		VisualElement active = _categoriesPanel;
		switch ( _compactPane )
		{
			case CompactPane.Features:
				active = _featuresPanel;
				break;
			case CompactPane.Details:
				active = _detailsPanel;
				break;
		}

		active.style.display = DisplayStyle.Flex;
		active.style.flexGrow = 1;
		_contentHost.Add( active );

		UpdateCompactNav();
		RefreshListViewsAfterReparent();
	}

	void UpdateCompactNav()
	{
		if ( _compactBackButton == null )
			return;

		_compactBackButton.SetEnabled( _compactPane != CompactPane.Categories );
		_compactFeaturesButton.SetEnabled( _selectedCategory != null );
		_compactDetailsButton.SetEnabled( _selectedFeature != null );

		SetCompactSegmentStyle( _compactCategoriesButton, _compactPane == CompactPane.Categories );
		SetCompactSegmentStyle( _compactFeaturesButton, _compactPane == CompactPane.Features );
		SetCompactSegmentStyle( _compactDetailsButton, _compactPane == CompactPane.Details );

		if ( _compactPane == CompactPane.Details && _selectedFeature != null )
			_compactContextLabel.text = _selectedFeature.Name;
		else if ( _compactPane == CompactPane.Features && _selectedCategory != null )
			_compactContextLabel.text = _selectedCategory.Name;
		else
			_compactContextLabel.text = string.Empty;
	}

	static void SetCompactSegmentStyle( Button button, bool active )
	{
		if ( button == null )
			return;
		button.style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
	}

	void RefreshListViewsAfterReparent()
	{
		if ( _categoryList != null )
			_categoryList.RefreshItems();
		if ( _featureList != null )
			_featureList.RefreshItems();
	}

	void RebuildQueueList()
	{
		_pendingQueue = _localStore != null
			? _localStore.CollectPendingMilanote( _catalog )
			: new List<PendingMilanoteItem>();

		if ( _queueList == null )
			return;

		_queueList.itemsSource = _pendingQueue;
		_queueList.RefreshItems();
	}

	void UpdateQueueButton()
	{
		if ( _queueButton == null || _localStore == null )
			return;

		int count = _localStore.CountPendingMilanoteAll( _catalog );
		_queueButton.text = _compactMode
			? "Queue (" + count + ")"
			: "Milanote Queue (" + count + ")";
	}

	void CopyMilanoteQueueChecklist()
	{
		List<PendingMilanoteItem> items = _localStore.CollectPendingMilanote( _catalog );
		if ( items.Count == 0 )
		{
			EditorGUIUtility.systemCopyBuffer = "(No pending Milanote tasks)";
			SetStatus( "Milanote queue is empty." );
			return;
		}

		var sb = new StringBuilder();
		sb.AppendLine( "# Milanote Queue" );
		sb.AppendLine( "Locally done — tick these in Milanote, then Sync." );
		sb.AppendLine();

		string lastFeatureId = null;
		for ( int i = 0; i < items.Count; i++ )
		{
			PendingMilanoteItem item = items[i];
			if ( !string.Equals( lastFeatureId, item.Feature.FeatureId, StringComparison.Ordinal ) )
			{
				lastFeatureId = item.Feature.FeatureId;
				sb.AppendLine( "## " + item.Feature.Category + " / " + item.Feature.Name );
			}

			sb.AppendLine( "- [ ] [" + item.Task.Section + "] " + item.Task.Text );
			if ( item.TaskMeta != null && !string.IsNullOrWhiteSpace( item.TaskMeta.Comment ) )
				sb.AppendLine( "  - note: " + item.TaskMeta.Comment );
		}

		EditorGUIUtility.systemCopyBuffer = sb.ToString();
		SetStatus( "Copied " + items.Count + " pending Milanote task(s) to clipboard." );
	}

	int CountPendingInCategory( ImportedCategory category )
	{
		if ( category == null )
			return 0;

		int count = 0;
		for ( int i = 0; i < category.Features.Count; i++ )
			count += _localStore.CountPendingMilanote( category.Features[i] );
		return count;
	}

	ImportedCategory ResolveCatalogCategory( string name )
	{
		if ( _catalog == null || string.IsNullOrEmpty( name ) )
			return null;

		for ( int i = 0; i < _catalog.Categories.Count; i++ )
		{
			if ( string.Equals( _catalog.Categories[i].Name, name, StringComparison.OrdinalIgnoreCase ) )
				return _catalog.Categories[i];
		}

		return null;
	}

	void SelectFeature( ImportedFeature feature )
	{
		if ( feature == null || _catalog == null )
			return;

		ImportedCategory category = null;
		for ( int i = 0; i < _catalog.Categories.Count; i++ )
		{
			if ( string.Equals( _catalog.Categories[i].Name, feature.Category, StringComparison.OrdinalIgnoreCase ) )
			{
				category = _catalog.Categories[i];
				break;
			}
		}

		ApplyFilters( refreshUi: false );

		ImportedCategory visibleCategory = null;
		if ( category != null )
		{
			for ( int i = 0; i < _visibleCategories.Count; i++ )
			{
				if ( string.Equals( _visibleCategories[i].Name, category.Name, StringComparison.OrdinalIgnoreCase ) )
				{
					visibleCategory = _visibleCategories[i];
					break;
				}
			}
		}

		if ( visibleCategory == null )
		{
			_filterLocal = "All";
			_filterStatus = "All";
			_globalSearch = string.Empty;
			ApplyFilters( refreshUi: false );
			for ( int i = 0; i < _visibleCategories.Count; i++ )
			{
				if ( string.Equals( _visibleCategories[i].Name, feature.Category, StringComparison.OrdinalIgnoreCase ) )
				{
					visibleCategory = _visibleCategories[i];
					break;
				}
			}
		}

		_selectedCategory = visibleCategory;
		RebuildCategoryList();
		RebuildFeatureList();

		_selectedFeature = null;
		for ( int i = 0; i < _visibleFeatures.Count; i++ )
		{
			if ( string.Equals( _visibleFeatures[i].FeatureId, feature.FeatureId, StringComparison.Ordinal ) )
			{
				_selectedFeature = _visibleFeatures[i];
				_featureList.SetSelectionWithoutNotify( new[] { i } );
				break;
			}
		}

		if ( _selectedFeature == null )
			_selectedFeature = feature;
	}

	void StartSync()
	{
		if ( _busy )
			return;

		PersistLocalStore();
		CancelBusy();
		_cts = new CancellationTokenSource();
		_busy = true;
		SetStatus( "Syncing Milanote…" );

		MilanoteSyncService.SyncContext syncContext = MilanoteSyncService.CaptureContext();
		CancellationToken token = _cts.Token;
		Task.Run( async () =>
		{
			MilanoteSyncService.SyncResult result = await MilanoteSyncService.PullAndGenerateAsync( syncContext, token )
				.ConfigureAwait( false );

			EditorApplication.delayCall += () =>
			{
				_busy = false;
				MilanoteSyncService.ApplyLastSyncUtc( result );
				AssetDatabase.Refresh();
				ReloadFromDisk();
				RefreshAllLists();
				RebuildDetails();
				UpdateSyncInfoLabel();
				UpdateQueueButton();
				if ( _showingQueue )
					RebuildQueueList();
				SetStatus( result.Message + " (" + result.Duration.TotalSeconds.ToString( "0.0" ) + "s)" );
			};
		} );
	}

	void ImportLatestExport()
	{
		if ( _busy )
			return;

		PersistLocalStore();
		_busy = true;
		SetStatus( "Importing latest Milanote markdown export…" );

		MilanoteSyncService.SyncContext syncContext = MilanoteSyncService.CaptureContext();
		Task.Run( () =>
		{
			MilanoteSyncService.SyncResult result = MilanoteSyncService.ImportLatestMarkdownExport( syncContext );

			EditorApplication.delayCall += () =>
			{
				_busy = false;
				MilanoteSyncService.ApplyLastSyncUtc( result );
				AssetDatabase.Refresh();
				ReloadFromDisk();
				RefreshAllLists();
				RebuildDetails();
				UpdateSyncInfoLabel();
				UpdateQueueButton();
				if ( _showingQueue )
					RebuildQueueList();
				SetStatus( result.Message + " (" + result.Duration.TotalSeconds.ToString( "0.0" ) + "s)" );
			};
		} );
	}

	void ClearGeneratedData()
	{
		bool confirmed = EditorUtility.DisplayDialog(
			"Clear Generated Data",
			"Delete all generated Milanote markdown and local Project Tasks metadata?\n\n"
			+ "This removes FEATURES/, CURRENT.md, INDEX.md, .sync-manifest.json, "
			+ ".raw-pull.json, and .unity-dev-metadata.json.\n\n"
			+ "Cookies and Board ID are kept.",
			"Clear",
			"Cancel" );
		if ( !confirmed )
			return;

		MilanoteSyncService.SyncResult result = MilanoteSyncService.ClearGeneratedData();
		_localStore = new ProjectTasksLocalStore();
		ReloadFromDisk();
		RefreshAllLists();
		RebuildDetails();
		UpdateSyncInfoLabel();
		UpdateQueueButton();
		SetStatus( result.Message );
	}

	void CancelBusy()
	{
		if ( _cts == null )
			return;
		try
		{
			_cts.Cancel();
		}
		catch
		{
			// ignored
		}

		_cts.Dispose();
		_cts = null;
	}

	void UpdateSyncInfoLabel()
	{
		if ( _syncInfoLabel == null )
			return;
		string last = MilanoteSyncSettings.LastSyncUtc;
		int count = _catalog != null ? _catalog.AllFeatures.Count : 0;
		_syncInfoLabel.text = "Features: " + count + "   Last Sync: " + ( string.IsNullOrEmpty( last ) ? "Never" : last );
	}

	void SetStatus( string message )
	{
		_statusText = message ?? string.Empty;
		if ( _statusLabel != null )
			_statusLabel.text = _statusText;
	}

	void EnsureOutputExists()
	{
		if ( string.IsNullOrEmpty( _outputRoot ) )
			_outputRoot = MilanoteSyncService.ResolveOutputRoot();
		if ( !Directory.Exists( _outputRoot ) )
			Directory.CreateDirectory( _outputRoot );
	}

	static void OpenPath( string path )
	{
		if ( string.IsNullOrEmpty( path ) || !File.Exists( path ) )
		{
			EditorUtility.DisplayDialog( "Project Tasks", "File not found:\n" + path, "OK" );
			return;
		}

		InternalEditorUtilityOpenFile( path );
	}

	static void InternalEditorUtilityOpenFile( string path )
	{
		string projectRoot = Directory.GetParent( Application.dataPath ).FullName;
		string full = Path.GetFullPath( path );
		if ( full.StartsWith( Path.GetFullPath( projectRoot ), StringComparison.OrdinalIgnoreCase ) )
		{
			UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
				MakeAssetsRelative( full, projectRoot ) );
			if ( asset != null )
			{
				AssetDatabase.OpenAsset( asset );
				return;
			}
		}

		EditorUtility.OpenWithDefaultApp( path );
	}

	static string MakeAssetsRelative( string fullPath, string projectRoot )
	{
		string relative = fullPath.Substring( Path.GetFullPath( projectRoot ).Length )
			.TrimStart( Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar )
			.Replace( '\\', '/' );
		return relative;
	}

	void OpenCodeFile( string fileEntry )
	{
		string cleaned = ( fileEntry ?? string.Empty ).Trim().TrimStart( '-', ' ', '\t' );
		if ( string.IsNullOrEmpty( cleaned ) )
			return;

		string projectRoot = Directory.GetParent( Application.dataPath ).FullName;
		string candidate = cleaned.Replace( '\\', '/' );
		string[] probes =
		{
			Path.Combine( projectRoot, candidate ),
			Path.Combine( Application.dataPath, candidate ),
			Path.Combine( projectRoot, "Assets", candidate ),
			Path.Combine( projectRoot, "Assets", "Code", candidate )
		};

		for ( int i = 0; i < probes.Length; i++ )
		{
			string full = Path.GetFullPath( probes[i] );
			if ( File.Exists( full ) )
			{
				OpenPath( full );
				return;
			}
		}

		string[] guids = AssetDatabase.FindAssets( Path.GetFileNameWithoutExtension( cleaned ) );
		for ( int i = 0; i < guids.Length; i++ )
		{
			string assetPath = AssetDatabase.GUIDToAssetPath( guids[i] );
			if ( assetPath.EndsWith( Path.GetFileName( cleaned ), StringComparison.OrdinalIgnoreCase ) )
			{
				UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>( assetPath );
				if ( asset != null )
				{
					AssetDatabase.OpenAsset( asset );
					return;
				}
			}
		}

		SetStatus( "Missing file: " + cleaned );
		Debug.LogWarning( "[Project Tasks] Missing file referenced in Files Modified: " + cleaned );
	}

	static List<string> ParseFileList( string filesModified )
	{
		var files = new List<string>();
		if ( string.IsNullOrWhiteSpace( filesModified ) )
			return files;

		string[] lines = filesModified.Replace( "\r\n", "\n" ).Split( '\n' );
		for ( int i = 0; i < lines.Length; i++ )
		{
			string line = lines[i].Trim();
			if ( string.IsNullOrEmpty( line ) )
				continue;
			if ( line.StartsWith( "- ", StringComparison.Ordinal ) )
				line = line.Substring( 2 ).Trim();
			if ( !string.IsNullOrEmpty( line ) )
				files.Add( line );
		}

		return files;
	}

	static string BuildCursorContext( ImportedFeature feature )
	{
		var sb = new StringBuilder();
		sb.AppendLine( "# " + feature.Name );
		sb.AppendLine( "Category: " + feature.Category );
		sb.AppendLine( "Status: " + feature.Status );
		sb.AppendLine();
		sb.AppendLine( "## Requirements" );
		AppendBullets( sb, feature.Requirements );
		sb.AppendLine( "## Tasks" );
		AppendBullets( sb, feature.Tasks );
		sb.AppendLine( "## Notes" );
		AppendBullets( sb, feature.Notes );
		sb.AppendLine( "## Bugs" );
		AppendBullets( sb, feature.Bugs );
		sb.AppendLine( "## AI Context" );
		AppendBullets( sb, feature.AiContext );
		sb.AppendLine( "## Testing Instructions" );
		sb.AppendLine( feature.TestingInstructions );
		sb.AppendLine( "## Cursor Notes" );
		sb.AppendLine( feature.CursorNotes );
		sb.AppendLine();
		sb.AppendLine( "Markdown: " + feature.RelativePath );
		return sb.ToString();
	}

	static void AppendBullets( StringBuilder sb, List<string> items )
	{
		if ( items == null || items.Count == 0 )
		{
			sb.AppendLine( "- _(none)_" );
			return;
		}

		for ( int i = 0; i < items.Count; i++ )
			sb.AppendLine( "- " + items[i] );
	}

	static string FormatVerification( LocalVerificationStatus status )
	{
		switch ( status )
		{
			case LocalVerificationStatus.Testing:
				return "Testing";
			case LocalVerificationStatus.Verified:
				return "Verified";
			case LocalVerificationStatus.Failed:
				return "Failed";
			default:
				return "Not Tested";
		}
	}
}
#endif
