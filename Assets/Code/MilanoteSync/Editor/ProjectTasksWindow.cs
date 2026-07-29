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
	enum FeatureSortColumn
	{
		Name,
		Status,
		Priority,
		Assignee,
		LastSync,
		Verification
	}

	ProjectTasksCatalog _catalog;
	ProjectTasksLocalStore _localStore;
	string _outputRoot;

	List<ImportedCategory> _visibleCategories = new List<ImportedCategory>();
	List<ImportedFeature> _visibleFeatures = new List<ImportedFeature>();

	ImportedCategory _selectedCategory;
	ImportedFeature _selectedFeature;

	string _globalSearch = string.Empty;
	string _categorySearch = string.Empty;
	string _filterStatus = "All";
	string _filterPriority = "All";
	string _filterAssignee = "All";
	string _filterVerification = "All";
	FeatureSortColumn _sortColumn = FeatureSortColumn.Name;
	bool _sortAscending = true;

	bool _busy;
	string _statusText = "Ready.";
	CancellationTokenSource _cts;

	ListView _categoryList;
	ListView _featureList;
	ScrollView _detailsScroll;
	Label _statusLabel;
	Label _syncInfoLabel;

	TextField _globalSearchField;
	TextField _categorySearchField;
	DropdownField _statusFilter;
	DropdownField _priorityFilter;
	DropdownField _assigneeFilter;
	DropdownField _verificationFilter;

	[MenuItem( "Tools/Project Tasks" )]
	public static void Open()
	{
		ProjectTasksWindow window = GetWindow<ProjectTasksWindow>();
		window.titleContent = new GUIContent( "Project Tasks" );
		window.minSize = new Vector2( 960f, 560f );
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

		rootVisualElement.Add( BuildToolbar() );
		rootVisualElement.Add( BuildFilterBar() );

		var splits = new TwoPaneSplitView( 0, 220f, TwoPaneSplitViewOrientation.Horizontal );
		splits.style.flexGrow = 1;

		var left = BuildCategoriesPanel();
		var rightSplit = new TwoPaneSplitView( 0, 340f, TwoPaneSplitViewOrientation.Horizontal );
		rightSplit.Add( BuildFeatureListPanel() );
		rightSplit.Add( BuildDetailsPanel() );

		splits.Add( left );
		splits.Add( rightSplit );
		rootVisualElement.Add( splits );

		_statusLabel = new Label( _statusText );
		_statusLabel.style.paddingLeft = 8;
		_statusLabel.style.paddingRight = 8;
		_statusLabel.style.paddingTop = 4;
		_statusLabel.style.paddingBottom = 4;
		_statusLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
		rootVisualElement.Add( _statusLabel );

		RefreshAllLists();
		RebuildDetails();
	}

	VisualElement BuildToolbar()
	{
		var bar = new VisualElement();
		bar.style.flexDirection = FlexDirection.Row;
		bar.style.paddingLeft = 6;
		bar.style.paddingRight = 6;
		bar.style.paddingTop = 4;
		bar.style.paddingBottom = 4;
		bar.style.borderBottomWidth = 1;
		bar.style.borderBottomColor = new Color( 0.2f, 0.2f, 0.2f );

		bar.Add( MakeToolbarButton( "Sync Milanote", () => StartSync() ) );
		bar.Add( MakeToolbarButton( "Import Latest Export", () => ImportLatestExport() ) );
		bar.Add( MakeToolbarButton( "Refresh", () =>
		{
			PersistLocalStore();
			ReloadFromDisk();
			RefreshAllLists();
			RebuildDetails();
			SetStatus( "Refreshed from disk." );
		} ) );
		bar.Add( MakeToolbarButton( "Clear Generated Data…", () => ClearGeneratedData() ) );
		bar.Add( MakeToolbarButton( "Open Cursor Folder", () =>
		{
			EnsureOutputExists();
			EditorUtility.RevealInFinder( _outputRoot );
		} ) );
		bar.Add( MakeToolbarButton( "Open CURRENT.md", () => OpenPath( Path.Combine( _outputRoot, "CURRENT.md" ) ) ) );
		bar.Add( MakeToolbarButton( "Settings", () => MilanoteSyncWindow.Open() ) );

		_syncInfoLabel = new Label();
		_syncInfoLabel.style.flexGrow = 1;
		_syncInfoLabel.style.unityTextAlign = TextAnchor.MiddleRight;
		_syncInfoLabel.style.paddingRight = 8;
		UpdateSyncInfoLabel();
		bar.Add( _syncInfoLabel );

		return bar;
	}

	VisualElement BuildFilterBar()
	{
		var bar = new VisualElement();
		bar.style.flexDirection = FlexDirection.Row;
		bar.style.flexWrap = Wrap.Wrap;
		bar.style.paddingLeft = 6;
		bar.style.paddingRight = 6;
		bar.style.paddingTop = 4;
		bar.style.paddingBottom = 4;

		_globalSearchField = new TextField( "Search" );
		_globalSearchField.value = _globalSearch;
		_globalSearchField.style.minWidth = 220;
		_globalSearchField.style.flexGrow = 1;
		_globalSearchField.RegisterValueChangedCallback( evt =>
		{
			_globalSearch = evt.newValue ?? string.Empty;
			ApplyFilters();
		} );
		bar.Add( _globalSearchField );

		_statusFilter = MakeFilterDropdown( "Status", new[] { "All", "Ready", "In Progress", "Complete" }, _filterStatus,
			v => { _filterStatus = v; ApplyFilters(); } );
		bar.Add( _statusFilter );

		_priorityFilter = MakeFilterDropdown( "Priority", BuildPriorityChoices(), _filterPriority,
			v => { _filterPriority = v; ApplyFilters(); } );
		bar.Add( _priorityFilter );

		_assigneeFilter = MakeFilterDropdown( "Assignee", BuildAssigneeChoices(), _filterAssignee,
			v => { _filterAssignee = v; ApplyFilters(); } );
		bar.Add( _assigneeFilter );

		_verificationFilter = MakeFilterDropdown(
			"Verification",
			new[] { "All", "Not Tested", "Testing", "Verified", "Failed" },
			_filterVerification,
			v => { _filterVerification = v; ApplyFilters(); } );
		bar.Add( _verificationFilter );

		return bar;
	}

	VisualElement BuildCategoriesPanel()
	{
		var panel = new VisualElement();
		panel.style.minWidth = 160;

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
		_categoryList.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
		_categoryList.makeItem = () =>
		{
			var row = new Label();
			row.style.paddingLeft = 8;
			row.style.paddingTop = 4;
			row.style.paddingBottom = 4;
			return row;
		};
		_categoryList.bindItem = ( element, index ) =>
		{
			var label = (Label)element;
			if ( index < 0 || index >= _visibleCategories.Count )
			{
				label.text = string.Empty;
				return;
			}

			ImportedCategory category = _visibleCategories[index];
			label.text = category.Name + " (" + category.ActiveCount + "/" + category.Features.Count + ")";
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
			RebuildFeatureList();
			if ( _visibleFeatures.Count > 0 )
			{
				_featureList.SetSelection( 0 );
				_selectedFeature = _visibleFeatures[0];
			}
			else
			{
				_selectedFeature = null;
			}

			RebuildDetails();
		};
		panel.Add( _categoryList );
		return panel;
	}

	VisualElement BuildFeatureListPanel()
	{
		var panel = new VisualElement();
		panel.style.minWidth = 240;

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

			var name = new Label { name = "name" };
			name.style.unityFontStyleAndWeight = FontStyle.Bold;
			var meta = new Label { name = "meta" };
			meta.style.fontSize = 10;
			meta.style.color = new Color( 0.7f, 0.7f, 0.7f );
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
			var name = element.Q<Label>( "name" );
			var meta = element.Q<Label>( "meta" );
			name.text = feature.Name + "  (" + feature.TaskProgressLabel + ")";
			meta.text = feature.Status
				+ " · " + ( string.IsNullOrEmpty( local.Priority ) ? "-" : local.Priority )
				+ " · " + FormatVerification( local.VerificationStatus )
				+ ( string.IsNullOrEmpty( local.AssignedDeveloper ) ? "" : " · " + local.AssignedDeveloper );
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
		};
		panel.Add( _featureList );
		return panel;
	}

	VisualElement BuildDetailsPanel()
	{
		var panel = new VisualElement();
		panel.style.minWidth = 320;
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
		_detailsScroll.Add( new Label( "Progress: " + feature.TaskProgressLabel + " Milanote tasks complete" ) );
		_detailsScroll.Add( new Label( "Last Sync: " + ( feature.LastSync ?? "-" ) ) );
		_detailsScroll.Add( new Label( "ID: " + feature.FeatureId ) );

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
			if ( taskMeta.Highlighted )
				card.style.backgroundColor = new Color( 0.28f, 0.28f, 0.15f );

			string mark = task.IsComplete ? "[x]" : "[ ]";
			var title = new Label( mark + " [" + task.Section + "] " + task.Text );
			if ( task.IsComplete )
				title.style.color = new Color( 0.55f, 0.75f, 0.55f );
			card.Add( title );

			var milanoteComplete = new Toggle( "Milanote complete" ) { value = task.IsComplete };
			milanoteComplete.SetEnabled( false );
			card.Add( milanoteComplete );

			var status = new EnumField( "Local Status", taskMeta.WorkStatus );
			LocalTaskMetadata capturedMeta = taskMeta;
			status.RegisterValueChangedCallback( evt =>
			{
				capturedMeta.WorkStatus = (LocalTaskWorkStatus)evt.newValue;
				PersistLocalStore();
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

	void ReloadFromDisk()
	{
		_outputRoot = MilanoteSyncService.ResolveOutputRoot();
		_catalog = FeatureMarkdownReader.Load( _outputRoot );
		_localStore = ProjectTasksLocalStore.Load( _outputRoot );
		if ( _localStore.ApplyMilanoteCompletionAssumptions( _catalog ) )
			PersistLocalStore();
		ApplyFilters( refreshUi: false );
		UpdateSyncInfoLabel();

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
			int index = _visibleCategories.IndexOf( _selectedCategory );
			if ( index >= 0 )
				_categoryList.SetSelection( index );
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
				_featureList.SetSelection( index );
			else
				_selectedFeature = null;
		}
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

	DropdownField MakeFilterDropdown( string label, List<string> choices, string current, Action<string> onChanged )
	{
		if ( !choices.Contains( current ) )
			choices.Insert( 0, current );

		var field = new DropdownField( label, choices, Mathf.Max( 0, choices.IndexOf( current ) ) );
		field.style.minWidth = 140;
		field.RegisterValueChangedCallback( evt => onChanged( evt.newValue ) );
		return field;
	}

	DropdownField MakeFilterDropdown( string label, string[] choices, string current, Action<string> onChanged )
	{
		return MakeFilterDropdown( label, new List<string>( choices ), current, onChanged );
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

	void RefreshFilterChoices()
	{
		// RecreateGUI is heavy; values still work with free text fields for priority/assignee.
	}

	Button MakeToolbarButton( string text, Action onClick )
	{
		var button = new Button( onClick ) { text = text };
		button.style.marginRight = 4;
		button.SetEnabled( !_busy );
		return button;
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
		// Prefer Unity's open-asset for project files; otherwise system open.
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
