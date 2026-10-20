#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Markdown Project Tasks command centre — capture, browse, and copy Cursor commands.
/// Optimised for a narrow docked Editor window.
/// </summary>
public sealed class ProjectTasksWindow : EditorWindow
{
	const string AutoOption = "Auto";
	const string NewFeatureOption = "New feature…";
	const float InboxListMinHeight = 120f;
	const float FeatureListMinHeight = 200f;
	const float DetailMinHeight = 140f;
	const int TabTasks = 0;
	const int TabVerification = 1;

	static readonly Color SectionBg = new Color( 0.16f, 0.16f, 0.18f, 0.92f );
	static readonly Color SectionBorder = new Color( 0.32f, 0.32f, 0.36f, 1f );
	static readonly Color HeaderBg = new Color( 0.13f, 0.13f, 0.15f, 0.95f );
	static readonly Color TabActiveBg = new Color( 0.24f, 0.24f, 0.28f, 1f );
	static readonly Color TabInactiveBg = new Color( 0.14f, 0.14f, 0.16f, 1f );

	ProjectTasksCatalog _catalog;
	string _rootPath = string.Empty;
	string _statusText = "Ready.";
	string _featureChoice = AutoOption;
	string _newFeatureName = string.Empty;
	bool _showRootEditor;
	int _activeTab = TabTasks;

	TextField _rootField;
	TextField _captureField;
	TextField _newFeatureField;
	DropdownField _featureDropdown;
	Label _statusLabel;
	Label _statsLabel;
	Label _rootSummaryLabel;
	Label _featureDetailTitle;
	Label _featureDetailBody;
	VisualElement _featureDetailSwatch;
	Button _planButton;
	Button _initializeButton;
	Button _editRootButton;
	Button _tasksTabButton;
	Button _verificationTabButton;
	VisualElement _rootEditorPanel;
	VisualElement _newFeatureRow;
	VisualElement _tasksTabContent;
	VisualElement _verificationTabContent;
	ListView _inboxList;
	ListView _featureList;
	ListView _verificationList;
	Label _verificationEmptyLabel;

	ProjectTasksInboxEntry _selectedInbox;
	ProjectTasksFeatureEntry _selectedFeature;

	[MenuItem( DragonLootMenus.ToolsProjectTasks )]
	public static void Open()
	{
		ProjectTasksWindow window = GetWindow<ProjectTasksWindow>();
		window.titleContent = new GUIContent( "Project Tasks" );
		window.minSize = new Vector2( 300f, 420f );
		window.Show();
	}

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
			window.RefreshUi();
			window.SetStatus( "Refreshed." );
			window.Repaint();
		}
	}

	void OnEnable()
	{
		_rootPath = ProjectTasksSettings.RootPath;
		_showRootEditor = !HasRootConfigured();
		ReloadFromDisk();
	}

	void CreateGUI()
	{
		rootVisualElement.Clear();
		rootVisualElement.style.flexGrow = 1;

		var chrome = new VisualElement();
		chrome.style.flexShrink = 0;
		chrome.style.paddingLeft = 8;
		chrome.style.paddingRight = 8;
		chrome.style.paddingTop = 8;
		chrome.Add( BuildHeader() );
		chrome.Add( BuildTabBar() );
		rootVisualElement.Add( chrome );

		_tasksTabContent = new ScrollView( ScrollViewMode.Vertical );
		_tasksTabContent.style.flexGrow = 1;
		var tasksScroll = (ScrollView)_tasksTabContent;
		tasksScroll.contentContainer.style.paddingLeft = 8;
		tasksScroll.contentContainer.style.paddingRight = 8;
		tasksScroll.contentContainer.style.paddingTop = 4;
		tasksScroll.contentContainer.style.paddingBottom = 4;
		tasksScroll.Add( BuildCaptureZone() );
		tasksScroll.Add( BuildCommandsRail() );
		tasksScroll.Add( BuildInboxPanel() );
		tasksScroll.Add( BuildFeaturesPanel() );
		tasksScroll.Add( BuildFeatureDetailPanel() );
		rootVisualElement.Add( _tasksTabContent );

		_verificationTabContent = BuildVerificationPanel();
		_verificationTabContent.style.flexGrow = 1;
		_verificationTabContent.style.marginLeft = 8;
		_verificationTabContent.style.marginRight = 8;
		_verificationTabContent.style.marginTop = 4;
		_verificationTabContent.style.marginBottom = 4;
		rootVisualElement.Add( _verificationTabContent );

		_statusLabel = new Label( _statusText );
		_statusLabel.style.flexShrink = 0;
		_statusLabel.style.whiteSpace = WhiteSpace.Normal;
		_statusLabel.style.paddingLeft = 8;
		_statusLabel.style.paddingRight = 8;
		_statusLabel.style.paddingTop = 4;
		_statusLabel.style.paddingBottom = 6;
		_statusLabel.style.backgroundColor = HeaderBg;
		_statusLabel.style.borderTopWidth = 1;
		_statusLabel.style.borderTopColor = SectionBorder;
		rootVisualElement.Add( _statusLabel );

		ApplyActiveTab();
		RefreshUi();
	}

	VisualElement BuildTabBar()
	{
		var bar = new VisualElement();
		bar.style.flexDirection = FlexDirection.Row;
		bar.style.flexShrink = 0;
		bar.style.marginBottom = 4;

		_tasksTabButton = MakeTabButton( "Tasks", () => SetActiveTab( TabTasks ) );
		_verificationTabButton = MakeTabButton( "Verification", () => SetActiveTab( TabVerification ) );
		bar.Add( _tasksTabButton );
		bar.Add( _verificationTabButton );
		return bar;
	}

	static Button MakeTabButton( string text, Action onClick )
	{
		var button = new Button( onClick ) { text = text };
		button.style.flexGrow = 1;
		button.style.height = 26;
		button.style.marginLeft = 0;
		button.style.marginRight = 2;
		button.style.marginTop = 0;
		button.style.marginBottom = 0;
		button.style.borderTopLeftRadius = 4;
		button.style.borderTopRightRadius = 4;
		button.style.borderBottomLeftRadius = 0;
		button.style.borderBottomRightRadius = 0;
		return button;
	}

	void SetActiveTab( int tab )
	{
		_activeTab = tab;
		ApplyActiveTab();
	}

	void ApplyActiveTab()
	{
		bool tasks = _activeTab == TabTasks;
		if ( _tasksTabContent != null )
			_tasksTabContent.style.display = tasks ? DisplayStyle.Flex : DisplayStyle.None;
		if ( _verificationTabContent != null )
			_verificationTabContent.style.display = tasks ? DisplayStyle.None : DisplayStyle.Flex;

		StyleTabButton( _tasksTabButton, tasks );
		StyleTabButton( _verificationTabButton, !tasks );
	}

	static void StyleTabButton( Button button, bool active )
	{
		if ( button == null )
			return;
		button.style.backgroundColor = active ? TabActiveBg : TabInactiveBg;
		button.style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
		button.style.opacity = active ? 1f : 0.75f;
	}

	VisualElement BuildHeader()
	{
		var root = MakeSection( "Workspace" );

		var titleRow = new VisualElement();
		titleRow.style.flexDirection = FlexDirection.Row;
		titleRow.style.alignItems = Align.Center;
		titleRow.style.marginBottom = 4;

		var title = new Label( "Project Tasks" );
		title.style.unityFontStyleAndWeight = FontStyle.Bold;
		title.style.fontSize = 14;
		title.style.flexGrow = 1;
		titleRow.Add( title );
		root.Add( titleRow );

		_statsLabel = new Label();
		_statsLabel.style.whiteSpace = WhiteSpace.Normal;
		_statsLabel.style.fontSize = 11;
		_statsLabel.style.opacity = 0.9f;
		_statsLabel.style.marginBottom = 4;
		root.Add( _statsLabel );

		_rootSummaryLabel = new Label();
		_rootSummaryLabel.style.whiteSpace = WhiteSpace.Normal;
		_rootSummaryLabel.style.fontSize = 11;
		_rootSummaryLabel.style.opacity = 0.75f;
		_rootSummaryLabel.style.marginBottom = 4;
		root.Add( _rootSummaryLabel );

		_rootEditorPanel = new VisualElement();
		_rootEditorPanel.style.marginBottom = 4;

		var pathRow = new VisualElement();
		pathRow.style.flexDirection = FlexDirection.Row;
		pathRow.style.alignItems = Align.Center;
		pathRow.style.marginBottom = 4;

		_rootField = new TextField();
		_rootField.style.flexGrow = 1;
		_rootField.value = _rootPath;
		_rootField.RegisterValueChangedCallback( evt =>
		{
			_rootPath = evt.newValue != null ? evt.newValue.Trim() : string.Empty;
			ProjectTasksSettings.RootPath = _rootPath;
			UpdateWorkspaceChrome();
		} );
		pathRow.Add( _rootField );
		pathRow.Add( MakeButton( "Browse…", BrowseRoot ) );
		_rootEditorPanel.Add( pathRow );
		root.Add( _rootEditorPanel );

		var actionRow = new VisualElement();
		actionRow.style.flexDirection = FlexDirection.Row;
		actionRow.style.flexWrap = Wrap.Wrap;

		_editRootButton = MakeButton( "Edit Root", ToggleRootEditor );
		actionRow.Add( _editRootButton );
		actionRow.Add( MakeButton( "Open Folder", () => ProjectTasksWorkspace.RevealInFinder( _rootPath ) ) );

		_initializeButton = MakeButton( "Initialize Workspace", InitializeWorkspace );
		actionRow.Add( _initializeButton );

		actionRow.Add( MakeButton( "Refresh", () =>
		{
			ReloadFromDisk();
			RefreshUi();
			SetStatus( BuildRefreshStatus() );
		} ) );
		actionRow.Add( MakeButton( "AI Instructions", () =>
		{
			ProjectTasksWorkspace.OpenFile( ProjectTasksWorkspace.AiInstructionsPath( _rootPath ) );
		} ) );
		actionRow.Add( MakeButton( "PROJECT", () =>
		{
			ProjectTasksWorkspace.OpenFile( ProjectTasksWorkspace.ProjectPath( _rootPath ) );
		} ) );
		actionRow.Add( MakeButton( "Archive", () =>
		{
			ProjectTasksWorkspace.RevealInFinder( ProjectTasksWorkspace.ArchiveDir( _rootPath ) );
		} ) );
		root.Add( actionRow );

		return root;
	}

	VisualElement BuildCaptureZone()
	{
		var root = MakeSection( "Capture" );

		_captureField = new TextField();
		_captureField.multiline = true;
		_captureField.style.minHeight = 64;
		_captureField.style.marginBottom = 6;
		_captureField.style.whiteSpace = WhiteSpace.Normal;
		root.Add( _captureField );

		var featureRow = new VisualElement();
		featureRow.style.flexDirection = FlexDirection.Row;
		featureRow.style.alignItems = Align.Center;
		featureRow.style.marginBottom = 4;
		featureRow.style.flexWrap = Wrap.Wrap;

		_featureDropdown = new DropdownField( "Feature", new List<string> { AutoOption }, 0 );
		_featureDropdown.style.flexGrow = 1;
		_featureDropdown.style.minWidth = 140;
		_featureDropdown.RegisterValueChangedCallback( evt =>
		{
			_featureChoice = evt.newValue;
			UpdateNewFeatureRowVisibility();
		} );
		featureRow.Add( _featureDropdown );
		featureRow.Add( MakeButton( "Add Task", TryAddTask ) );
		root.Add( featureRow );

		_newFeatureRow = new VisualElement();
		_newFeatureRow.style.flexDirection = FlexDirection.Row;
		_newFeatureRow.style.alignItems = Align.Center;
		_newFeatureRow.style.display = DisplayStyle.None;
		_newFeatureRow.style.marginBottom = 4;
		_newFeatureField = new TextField( "New name" );
		_newFeatureField.style.flexGrow = 1;
		_newFeatureField.RegisterValueChangedCallback( evt =>
		{
			_newFeatureName = evt.newValue != null ? evt.newValue.Trim() : string.Empty;
		} );
		_newFeatureRow.Add( _newFeatureField );
		root.Add( _newFeatureRow );

		var hint = new Label( "Auto = Cursor chooses during Sort. New feature creates Features/<name>.md if missing." );
		hint.style.opacity = 0.65f;
		hint.style.whiteSpace = WhiteSpace.Normal;
		hint.style.fontSize = 10;
		root.Add( hint );

		return root;
	}

	VisualElement BuildVerificationPanel()
	{
		var panel = MakeSection( "Verification" );
		panel.style.flexGrow = 1;
		panel.style.flexShrink = 1;
		panel.style.marginBottom = 0;

		var hint = new Label( "Tick after playtesting. Click the text to copy a reference. Completed items clear next time that feature is worked on." );
		hint.style.opacity = 0.65f;
		hint.style.whiteSpace = WhiteSpace.Normal;
		hint.style.fontSize = 11;
		hint.style.marginBottom = 6;
		hint.style.flexShrink = 0;
		panel.Add( hint );

		_verificationEmptyLabel = new Label( "No verification items yet. Cursor adds them after implementing a feature." );
		_verificationEmptyLabel.style.opacity = 0.7f;
		_verificationEmptyLabel.style.whiteSpace = WhiteSpace.Normal;
		_verificationEmptyLabel.style.fontSize = 12;
		_verificationEmptyLabel.style.marginBottom = 4;
		_verificationEmptyLabel.style.flexShrink = 0;
		panel.Add( _verificationEmptyLabel );

		_verificationList = new ListView();
		_verificationList.style.flexGrow = 1;
		_verificationList.style.flexShrink = 1;
		_verificationList.style.minHeight = 200f;
		_verificationList.selectionType = SelectionType.None;
		_verificationList.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
		_verificationList.makeItem = () =>
		{
			var row = new VisualElement();
			row.style.flexDirection = FlexDirection.Row;
			row.style.alignItems = Align.FlexStart;
			row.style.paddingLeft = 2;
			row.style.paddingRight = 6;
			row.style.paddingTop = 6;
			row.style.paddingBottom = 6;
			row.style.borderBottomWidth = 1;
			row.style.borderBottomColor = new Color( SectionBorder.r, SectionBorder.g, SectionBorder.b, 0.55f );

			var swatch = new VisualElement { name = "swatch" };
			swatch.style.width = 4;
			swatch.style.minHeight = 44;
			swatch.style.alignSelf = Align.Stretch;
			swatch.style.marginRight = 8;
			swatch.style.flexShrink = 0;
			swatch.style.borderTopLeftRadius = 2;
			swatch.style.borderTopRightRadius = 2;
			swatch.style.borderBottomLeftRadius = 2;
			swatch.style.borderBottomRightRadius = 2;

			var toggle = new Toggle { name = "done" };
			toggle.style.marginRight = 8;
			toggle.style.marginTop = 2;
			toggle.style.flexShrink = 0;
			toggle.RegisterValueChangedCallback( OnVerificationToggleChanged );

			var textCol = new VisualElement { name = "text-col" };
			textCol.style.flexGrow = 1;
			textCol.style.flexShrink = 1;
			textCol.focusable = true;
			textCol.RegisterCallback<ClickEvent>( OnVerificationTextClicked );
			var feature = new Label { name = "feature" };
			feature.style.unityFontStyleAndWeight = FontStyle.Bold;
			feature.style.fontSize = 12;
			feature.style.marginBottom = 2;
			var text = new Label { name = "text" };
			text.style.fontSize = 12;
			text.style.whiteSpace = WhiteSpace.Normal;
			text.style.opacity = 0.9f;
			textCol.Add( feature );
			textCol.Add( text );

			row.Add( swatch );
			row.Add( toggle );
			row.Add( textCol );
			return row;
		};
		_verificationList.bindItem = ( element, index ) =>
		{
			if ( _catalog == null || index < 0 || index >= _catalog.Verification.Count )
				return;
			ProjectTasksVerificationEntry entry = _catalog.Verification[index];
			var swatch = element.Q( "swatch" );
			var toggle = element.Q<Toggle>( "done" );
			var textCol = element.Q( "text-col" );
			var feature = element.Q<Label>( "feature" );
			var text = element.Q<Label>( "text" );
			if ( swatch != null )
				swatch.style.backgroundColor = entry.Colour;
			if ( toggle != null )
			{
				toggle.SetValueWithoutNotify( entry.IsComplete );
				toggle.userData = entry;
			}

			if ( textCol != null )
				textCol.userData = entry;
			if ( feature != null )
				feature.text = entry.FeatureName;
			if ( text != null )
			{
				text.text = entry.Text;
				text.style.opacity = entry.IsComplete ? 0.55f : 0.95f;
			}
		};
		panel.Add( _verificationList );
		return panel;
	}

	void OnVerificationTextClicked( ClickEvent evt )
	{
		var element = evt.currentTarget as VisualElement;
		if ( element == null )
			return;
		var entry = element.userData as ProjectTasksVerificationEntry;
		if ( entry == null )
			return;

		string clipboard = BuildVerificationClipboardText( entry );
		EditorGUIUtility.systemCopyBuffer = clipboard;
		evt.StopPropagation();
		SetStatus( "Copied verification reference: " + entry.FeatureName );
	}

	static string BuildVerificationClipboardText( ProjectTasksVerificationEntry entry )
	{
		if ( entry == null )
			return string.Empty;

		return "Feature: " + entry.FeatureName
		       + "\nVerification: " + entry.Text
		       + "\nFile: " + entry.FeatureAbsolutePath
		       + "\n";
	}

	void OnVerificationToggleChanged( ChangeEvent<bool> evt )
	{
		var toggle = evt.target as Toggle;
		if ( toggle == null )
			return;
		var entry = toggle.userData as ProjectTasksVerificationEntry;
		if ( entry == null )
			return;

		string message;
		if ( !ProjectTasksMarkdown.TrySetVerificationItemChecked(
			    entry.FeatureAbsolutePath,
			    entry.ItemIndexInFeature,
			    evt.newValue,
			    out message ) )
		{
			toggle.SetValueWithoutNotify( !evt.newValue );
			SetStatus( message );
			return;
		}

		ReloadFromDisk();
		RefreshUi();
		SetStatus( entry.FeatureName + ": " + message );
	}

	VisualElement BuildCommandsRail()
	{
		var root = MakeSection( "Cursor Commands" );

		var row = new VisualElement();
		row.style.flexDirection = FlexDirection.Column;

		row.Add( MakeWideButton( "Copy Sort Inbox Command", () =>
		{
			string message;
			ProjectTasksCommands.TryCopySortInboxCommand( _rootPath, out message );
			SetStatus( message );
		} ) );
		row.Add( MakeWideButton( "Copy Discover Features Command", () =>
		{
			string message;
			string projectRoot = Path.GetFullPath( Path.Combine( Application.dataPath, ".." ) );
			ProjectTasksCommands.TryCopyDiscoverFeaturesCommand( _rootPath, projectRoot, out message );
			SetStatus( message );
		} ) );
		_planButton = MakeWideButton( "Copy Plan Feature Command", () =>
		{
			string path = _selectedFeature != null ? _selectedFeature.AbsolutePath : null;
			string message;
			ProjectTasksCommands.TryCopyPlanFeatureCommand( _rootPath, path, out message );
			SetStatus( message );
		} );
		row.Add( _planButton );
		root.Add( row );

		var hint = new Label( "Commands include absolute paths for Cursor." );
		hint.style.opacity = 0.65f;
		hint.style.whiteSpace = WhiteSpace.Normal;
		hint.style.fontSize = 10;
		hint.style.marginTop = 4;
		root.Add( hint );

		return root;
	}

	VisualElement BuildInboxPanel()
	{
		var panel = MakeSection( "Inbox" );

		var header = new VisualElement();
		header.style.flexDirection = FlexDirection.Row;
		header.style.alignItems = Align.Center;
		header.style.marginBottom = 4;
		header.style.flexWrap = Wrap.Wrap;

		var spacer = new VisualElement();
		spacer.style.flexGrow = 1;
		header.Add( spacer );
		header.Add( MakeButton( "Open", () =>
		{
			if ( _selectedInbox != null )
				ProjectTasksWorkspace.OpenFile( _selectedInbox.AbsolutePath );
		} ) );
		header.Add( MakeButton( "Reveal", () =>
		{
			if ( _selectedInbox != null )
				ProjectTasksWorkspace.RevealInFinder( _selectedInbox.AbsolutePath );
		} ) );
		header.Add( MakeButton( "Delete", DeleteSelectedInbox ) );
		panel.Add( header );

		_inboxList = new ListView();
		_inboxList.style.minHeight = InboxListMinHeight;
		_inboxList.style.height = InboxListMinHeight + 40f;
		_inboxList.selectionType = SelectionType.Single;
		_inboxList.fixedItemHeight = 52;
		_inboxList.makeItem = () =>
		{
			var row = new VisualElement();
			row.style.flexDirection = FlexDirection.Row;
			row.style.alignItems = Align.FlexStart;
			row.style.paddingLeft = 2;
			row.style.paddingRight = 4;
			row.style.paddingTop = 3;
			row.style.paddingBottom = 3;

			var swatch = new VisualElement { name = "swatch" };
			swatch.style.width = 4;
			swatch.style.height = 42;
			swatch.style.marginRight = 6;
			swatch.style.flexShrink = 0;
			swatch.style.borderTopLeftRadius = 2;
			swatch.style.borderTopRightRadius = 2;
			swatch.style.borderBottomLeftRadius = 2;
			swatch.style.borderBottomRightRadius = 2;

			var textCol = new VisualElement();
			textCol.style.flexGrow = 1;
			var id = new Label { name = "id" };
			id.style.unityFontStyleAndWeight = FontStyle.Bold;
			id.style.fontSize = 11;
			id.style.whiteSpace = WhiteSpace.Normal;
			var preview = new Label { name = "preview" };
			preview.style.opacity = 0.8f;
			preview.style.fontSize = 11;
			preview.style.whiteSpace = WhiteSpace.Normal;
			textCol.Add( id );
			textCol.Add( preview );

			row.Add( swatch );
			row.Add( textCol );
			return row;
		};
		_inboxList.bindItem = ( element, index ) =>
		{
			if ( _catalog == null || index < 0 || index >= _catalog.Inbox.Count )
				return;
			ProjectTasksInboxEntry entry = _catalog.Inbox[index];
			var id = element.Q<Label>( "id" );
			var preview = element.Q<Label>( "preview" );
			var swatch = element.Q( "swatch" );
			if ( id != null )
				id.text = entry.TaskId + "  ·  " + entry.Feature;
			if ( preview != null )
				preview.text = entry.Preview;
			if ( swatch != null )
				swatch.style.backgroundColor = ResolveFeatureColour( entry.Feature );
		};
		_inboxList.selectedIndicesChanged += indices =>
		{
			_selectedInbox = null;
			foreach ( int index in indices )
			{
				if ( _catalog == null || index < 0 || index >= _catalog.Inbox.Count )
					continue;
				_selectedInbox = _catalog.Inbox[index];
				break;
			}
		};
		panel.Add( _inboxList );
		return panel;
	}

	VisualElement BuildFeaturesPanel()
	{
		var panel = MakeSection( "Features" );

		var header = new VisualElement();
		header.style.flexDirection = FlexDirection.Row;
		header.style.alignItems = Align.Center;
		header.style.marginBottom = 4;
		header.style.flexWrap = Wrap.Wrap;

		var spacer = new VisualElement();
		spacer.style.flexGrow = 1;
		header.Add( spacer );
		header.Add( MakeButton( "Open", () =>
		{
			if ( _selectedFeature != null )
				ProjectTasksWorkspace.OpenFile( _selectedFeature.AbsolutePath );
		} ) );
		header.Add( MakeButton( "Reveal", () =>
		{
			if ( _selectedFeature != null )
				ProjectTasksWorkspace.RevealInFinder( _selectedFeature.AbsolutePath );
		} ) );
		panel.Add( header );

		_featureList = new ListView();
		_featureList.style.minHeight = FeatureListMinHeight;
		_featureList.style.height = FeatureListMinHeight + 80f;
		_featureList.selectionType = SelectionType.Single;
		_featureList.fixedItemHeight = 28;
		_featureList.makeItem = () =>
		{
			var row = new VisualElement();
			row.style.flexDirection = FlexDirection.Row;
			row.style.alignItems = Align.Center;
			row.style.paddingLeft = 2;
			row.style.paddingRight = 4;

			var swatch = new VisualElement { name = "swatch" };
			swatch.style.width = 10;
			swatch.style.height = 10;
			swatch.style.marginRight = 8;
			swatch.style.flexShrink = 0;
			swatch.style.borderTopLeftRadius = 2;
			swatch.style.borderTopRightRadius = 2;
			swatch.style.borderBottomLeftRadius = 2;
			swatch.style.borderBottomRightRadius = 2;

			var name = new Label { name = "name" };
			name.style.flexGrow = 1;
			name.style.unityFontStyleAndWeight = FontStyle.Bold;
			name.style.overflow = Overflow.Hidden;
			name.style.textOverflow = TextOverflow.Ellipsis;
			name.style.whiteSpace = WhiteSpace.NoWrap;

			var count = new Label { name = "count" };
			count.style.flexShrink = 0;
			count.style.opacity = 0.8f;
			count.style.fontSize = 11;
			count.style.marginLeft = 4;

			row.Add( swatch );
			row.Add( name );
			row.Add( count );
			return row;
		};
		_featureList.bindItem = ( element, index ) =>
		{
			if ( _catalog == null || index < 0 || index >= _catalog.Features.Count )
				return;
			ProjectTasksFeatureEntry entry = _catalog.Features[index];
			var name = element.Q<Label>( "name" );
			var count = element.Q<Label>( "count" );
			var swatch = element.Q( "swatch" );
			if ( name != null )
				name.text = entry.Name;
			if ( count != null )
				count.text = entry.OutstandingTaskCount + " open";
			if ( swatch != null )
				swatch.style.backgroundColor = entry.Colour;
		};
		_featureList.selectedIndicesChanged += indices =>
		{
			_selectedFeature = null;
			foreach ( int index in indices )
			{
				if ( _catalog == null || index < 0 || index >= _catalog.Features.Count )
					continue;
				_selectedFeature = _catalog.Features[index];
				break;
			}
			RebuildFeatureDetail();
			UpdatePlanButton();
		};
		panel.Add( _featureList );
		return panel;
	}

	VisualElement BuildFeatureDetailPanel()
	{
		var panel = MakeSection( "Feature Detail" );
		panel.style.minHeight = DetailMinHeight;

		var titleRow = new VisualElement();
		titleRow.style.flexDirection = FlexDirection.Row;
		titleRow.style.alignItems = Align.Center;
		titleRow.style.marginBottom = 4;

		_featureDetailSwatch = new VisualElement();
		_featureDetailSwatch.style.width = 12;
		_featureDetailSwatch.style.height = 12;
		_featureDetailSwatch.style.marginRight = 8;
		_featureDetailSwatch.style.flexShrink = 0;
		_featureDetailSwatch.style.borderTopLeftRadius = 2;
		_featureDetailSwatch.style.borderTopRightRadius = 2;
		_featureDetailSwatch.style.borderBottomLeftRadius = 2;
		_featureDetailSwatch.style.borderBottomRightRadius = 2;
		_featureDetailSwatch.style.display = DisplayStyle.None;
		titleRow.Add( _featureDetailSwatch );

		_featureDetailTitle = new Label( "Select a feature" );
		_featureDetailTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
		_featureDetailTitle.style.whiteSpace = WhiteSpace.Normal;
		_featureDetailTitle.style.flexGrow = 1;
		titleRow.Add( _featureDetailTitle );
		panel.Add( titleRow );

		_featureDetailBody = new Label();
		_featureDetailBody.style.whiteSpace = WhiteSpace.Normal;
		_featureDetailBody.style.opacity = 0.9f;
		_featureDetailBody.style.fontSize = 11;
		panel.Add( _featureDetailBody );
		return panel;
	}

	void ToggleRootEditor()
	{
		_showRootEditor = !_showRootEditor;
		UpdateWorkspaceChrome();
	}

	void BrowseRoot()
	{
		string start = string.IsNullOrWhiteSpace( _rootPath )
			? Environment.GetFolderPath( Environment.SpecialFolder.MyDocuments )
			: _rootPath;
		string selected = EditorUtility.OpenFolderPanel( "Project Tasks Root", start, string.Empty );
		if ( string.IsNullOrEmpty( selected ) )
			return;

		_rootPath = ProjectTasksSettings.NormalizeAbsolutePath( selected );
		ProjectTasksSettings.RootPath = _rootPath;
		if ( _rootField != null )
			_rootField.SetValueWithoutNotify( _rootPath );
		_showRootEditor = !ProjectTasksWorkspace.IsInitialized( _rootPath );
		ReloadFromDisk();
		RefreshUi();
		SetStatus( "Root set to " + _rootPath );
	}

	void InitializeWorkspace()
	{
		if ( string.IsNullOrWhiteSpace( _rootPath ) )
		{
			_showRootEditor = true;
			UpdateWorkspaceChrome();
			BrowseRoot();
			if ( string.IsNullOrWhiteSpace( _rootPath ) )
			{
				SetStatus( "Pick a root folder to initialise." );
				return;
			}
		}

		ProjectTasksWorkspace.InitResult result = ProjectTasksWorkspace.Initialize( _rootPath );
		_rootPath = ProjectTasksSettings.RootPath;
		if ( _rootField != null )
			_rootField.SetValueWithoutNotify( _rootPath );
		_showRootEditor = false;
		ReloadFromDisk();
		RefreshUi();
		SetStatus( result.Message );
	}

	void TryAddTask()
	{
		if ( string.IsNullOrWhiteSpace( _rootPath ) || !Directory.Exists( _rootPath ) )
		{
			SetStatus( "Configure and initialise a Project Tasks root first." );
			return;
		}

		string feature = ResolveCaptureFeature();
		if ( feature == null )
			return;

		string path;
		string message;
		string taskId = ProjectTasksWorkspace.CaptureInboxTask(
			_rootPath,
			_captureField != null ? _captureField.value : string.Empty,
			feature,
			out path,
			out message );
		if ( taskId == null )
		{
			SetStatus( message );
			return;
		}

		if ( _captureField != null )
		{
			_captureField.value = string.Empty;
			_captureField.schedule.Execute( () => _captureField.Focus() );
		}

		ReloadFromDisk();
		RefreshUi();
		SetStatus( message );
	}

	string ResolveCaptureFeature()
	{
		if ( string.Equals( _featureChoice, NewFeatureOption, StringComparison.Ordinal ) )
		{
			if ( string.IsNullOrWhiteSpace( _newFeatureName ) )
			{
				SetStatus( "Enter a new feature name." );
				return null;
			}

			return ProjectTasksMarkdown.SanitizeFeatureFileName( _newFeatureName );
		}

		if ( string.IsNullOrWhiteSpace( _featureChoice ) )
			return AutoOption;
		return _featureChoice;
	}

	void DeleteSelectedInbox()
	{
		if ( _selectedInbox == null || string.IsNullOrEmpty( _selectedInbox.AbsolutePath ) )
		{
			SetStatus( "Select an inbox task to delete." );
			return;
		}

		if ( !EditorUtility.DisplayDialog(
			    "Delete Inbox Task",
			    "Delete " + _selectedInbox.TaskId + " from Inbox?\n\n" + _selectedInbox.AbsolutePath,
			    "Delete",
			    "Cancel" ) )
			return;

		try
		{
			File.Delete( _selectedInbox.AbsolutePath );
			_selectedInbox = null;
			ReloadFromDisk();
			RefreshUi();
			SetStatus( "Inbox task deleted." );
		}
		catch ( Exception ex )
		{
			SetStatus( "Delete failed: " + ex.Message );
		}
	}

	void ReloadFromDisk()
	{
		_rootPath = ProjectTasksSettings.RootPath;
		_catalog = ProjectTasksCatalog.Load( _rootPath );
	}

	void RefreshUi()
	{
		if ( _rootField != null )
			_rootField.SetValueWithoutNotify( _rootPath );

		UpdateWorkspaceChrome();
		UpdateStatsLabel();
		RebuildFeatureDropdown();
		RebuildVerificationList();
		RebuildInboxList();
		RebuildFeatureList();
		RebuildFeatureDetail();
		UpdatePlanButton();
		UpdateNewFeatureRowVisibility();
	}

	void UpdateWorkspaceChrome()
	{
		bool hasRoot = HasRootConfigured();
		bool initialized = ProjectTasksWorkspace.IsInitialized( _rootPath );
		bool showEditor = _showRootEditor || !hasRoot;

		if ( _rootEditorPanel != null )
			_rootEditorPanel.style.display = showEditor ? DisplayStyle.Flex : DisplayStyle.None;

		if ( _editRootButton != null )
		{
			_editRootButton.style.display = hasRoot ? DisplayStyle.Flex : DisplayStyle.None;
			_editRootButton.text = showEditor ? "Hide Root" : "Edit Root";
		}

		if ( _initializeButton != null )
			_initializeButton.style.display = initialized ? DisplayStyle.None : DisplayStyle.Flex;

		if ( _rootSummaryLabel != null )
		{
			if ( !hasRoot )
				_rootSummaryLabel.text = "No root folder configured.";
			else if ( showEditor )
				_rootSummaryLabel.text = "Editing root path.";
			else
				_rootSummaryLabel.text = ShortenPath( _rootPath, 52 );
		}
	}

	void UpdateStatsLabel()
	{
		if ( _statsLabel == null )
			return;

		int inbox = _catalog != null ? _catalog.Inbox.Count : 0;
		int features = _catalog != null ? _catalog.Features.Count : 0;
		int archive = _catalog != null ? _catalog.ArchiveCount : 0;
		int verifyPending = _catalog != null ? _catalog.PendingVerificationCount : 0;
		string refreshed = _catalog != null
			? _catalog.LoadedAtUtc.ToLocalTime().ToString( "HH:mm:ss" )
			: "—";
		_statsLabel.text = "Inbox " + inbox
		                   + "  ·  Verify " + verifyPending
		                   + "  ·  Features " + features
		                   + "  ·  Archive " + archive
		                   + "  ·  " + refreshed;

		if ( _verificationTabButton != null )
		{
			_verificationTabButton.text = verifyPending > 0
				? "Verification (" + verifyPending + ")"
				: "Verification";
		}
	}

	void RebuildVerificationList()
	{
		if ( _verificationList == null )
			return;

		List<ProjectTasksVerificationEntry> items = _catalog != null
			? _catalog.Verification
			: new List<ProjectTasksVerificationEntry>();
		_verificationList.itemsSource = items;
		_verificationList.RefreshItems();

		bool empty = items.Count == 0;
		if ( _verificationEmptyLabel != null )
			_verificationEmptyLabel.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
		_verificationList.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
	}

	string BuildRefreshStatus()
	{
		string status = "Refreshed from disk.";
		if ( _catalog != null && _catalog.ColoursAssigned > 0 )
			status += " Assigned Colour to " + _catalog.ColoursAssigned + " feature(s).";
		return status;
	}

	void RebuildFeatureDropdown()
	{
		if ( _featureDropdown == null )
			return;

		var choices = new List<string>();
		choices.Add( AutoOption );
		if ( _catalog != null )
		{
			for ( int i = 0; i < _catalog.Features.Count; i++ )
			{
				ProjectTasksFeatureEntry feature = _catalog.Features[i];
				if ( feature == null || string.IsNullOrWhiteSpace( feature.Name ) )
					continue;
				if ( !choices.Contains( feature.Name ) )
					choices.Add( feature.Name );
			}
		}

		choices.Add( NewFeatureOption );

		string current = _featureChoice;
		if ( string.IsNullOrEmpty( current ) || !choices.Contains( current ) )
			current = AutoOption;

		_featureDropdown.choices = choices;
		_featureDropdown.SetValueWithoutNotify( current );
		_featureChoice = current;
	}

	void RebuildInboxList()
	{
		if ( _inboxList == null )
			return;

		List<ProjectTasksInboxEntry> items = _catalog != null
			? _catalog.Inbox
			: new List<ProjectTasksInboxEntry>();
		_inboxList.itemsSource = items;
		_inboxList.RefreshItems();

		if ( _selectedInbox != null )
		{
			int index = -1;
			for ( int i = 0; i < items.Count; i++ )
			{
				if ( items[i] != null
				     && string.Equals( items[i].AbsolutePath, _selectedInbox.AbsolutePath, StringComparison.OrdinalIgnoreCase ) )
				{
					index = i;
					_selectedInbox = items[i];
					break;
				}
			}

			if ( index >= 0 )
				_inboxList.SetSelection( index );
			else
			{
				_selectedInbox = null;
				_inboxList.ClearSelection();
			}
		}
	}

	void RebuildFeatureList()
	{
		if ( _featureList == null )
			return;

		List<ProjectTasksFeatureEntry> items = _catalog != null
			? _catalog.Features
			: new List<ProjectTasksFeatureEntry>();
		_featureList.itemsSource = items;
		_featureList.RefreshItems();

		if ( _selectedFeature != null )
		{
			int index = -1;
			for ( int i = 0; i < items.Count; i++ )
			{
				if ( items[i] != null
				     && string.Equals( items[i].AbsolutePath, _selectedFeature.AbsolutePath, StringComparison.OrdinalIgnoreCase ) )
				{
					index = i;
					_selectedFeature = items[i];
					break;
				}
			}

			if ( index >= 0 )
				_featureList.SetSelection( index );
			else
			{
				_selectedFeature = null;
				_featureList.ClearSelection();
			}
		}
	}

	void RebuildFeatureDetail()
	{
		if ( _featureDetailTitle == null || _featureDetailBody == null )
			return;

		if ( _selectedFeature == null )
		{
			_featureDetailTitle.text = "Select a feature";
			_featureDetailBody.text = "Outstanding tasks and summary appear here.";
			if ( _featureDetailSwatch != null )
				_featureDetailSwatch.style.display = DisplayStyle.None;
			return;
		}

		_featureDetailTitle.text = _selectedFeature.Name
		                           + "  (" + _selectedFeature.OutstandingTaskCount + " open, "
		                           + _selectedFeature.PendingVerificationCount + " to verify)";
		if ( _featureDetailSwatch != null )
		{
			_featureDetailSwatch.style.display = DisplayStyle.Flex;
			_featureDetailSwatch.style.backgroundColor = _selectedFeature.Colour;
		}

		string summary = string.IsNullOrWhiteSpace( _selectedFeature.Summary )
			? "(no summary)"
			: _selectedFeature.Summary.Trim();
		string tasks = string.IsNullOrWhiteSpace( _selectedFeature.OutstandingTasksMarkdown )
			? "(no outstanding tasks)"
			: _selectedFeature.OutstandingTasksMarkdown.Trim();
		string verification = BuildFeatureVerificationPreview( _selectedFeature );
		_featureDetailBody.text = "Summary\n" + summary
		                          + "\n\nOutstanding Tasks\n" + tasks
		                          + "\n\nVerification\n" + verification;
	}

	static string BuildFeatureVerificationPreview( ProjectTasksFeatureEntry feature )
	{
		if ( feature == null || feature.Verification.Count == 0 )
			return "(no verification items)";

		var sb = new System.Text.StringBuilder();
		for ( int i = 0; i < feature.Verification.Count; i++ )
		{
			ProjectTasksVerificationEntry item = feature.Verification[i];
			if ( item == null )
				continue;
			sb.Append( item.IsComplete ? "- [x] " : "- [ ] " );
			sb.Append( item.Text );
			if ( i < feature.Verification.Count - 1 )
				sb.Append( '\n' );
		}

		return sb.ToString();
	}

	void UpdatePlanButton()
	{
		if ( _planButton == null )
			return;
		_planButton.SetEnabled( _selectedFeature != null && !string.IsNullOrEmpty( _selectedFeature.AbsolutePath ) );
	}

	void UpdateNewFeatureRowVisibility()
	{
		if ( _newFeatureRow == null )
			return;
		bool show = string.Equals( _featureChoice, NewFeatureOption, StringComparison.Ordinal );
		_newFeatureRow.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
	}

	Color ResolveFeatureColour( string featureName )
	{
		if ( string.Equals( featureName, AutoOption, StringComparison.OrdinalIgnoreCase ) )
			return new Color( 0.45f, 0.45f, 0.5f, 1f );

		ProjectTasksFeatureEntry feature = _catalog != null ? _catalog.FindFeatureByName( featureName ) : null;
		if ( feature != null )
			return feature.Colour;

		return ProjectTasksMarkdown.ColorFromFeatureName( featureName );
	}

	bool HasRootConfigured()
	{
		return !string.IsNullOrWhiteSpace( _rootPath ) && Directory.Exists( _rootPath );
	}

	void SetStatus( string text )
	{
		_statusText = text ?? string.Empty;
		if ( _statusLabel != null )
			_statusLabel.text = _statusText;
	}

	static string ShortenPath( string path, int maxChars )
	{
		if ( string.IsNullOrEmpty( path ) || path.Length <= maxChars )
			return path ?? string.Empty;
		int keep = maxChars - 3;
		if ( keep < 8 )
			return path.Substring( 0, maxChars );
		return "…" + path.Substring( path.Length - keep );
	}

	static VisualElement MakeSection( string title )
	{
		var root = new VisualElement();
		root.style.flexShrink = 0;
		root.style.marginBottom = 8;
		root.style.paddingTop = 8;
		root.style.paddingBottom = 8;
		root.style.paddingLeft = 8;
		root.style.paddingRight = 8;
		root.style.backgroundColor = SectionBg;
		root.style.borderTopWidth = 1;
		root.style.borderBottomWidth = 1;
		root.style.borderLeftWidth = 1;
		root.style.borderRightWidth = 1;
		root.style.borderTopColor = SectionBorder;
		root.style.borderBottomColor = SectionBorder;
		root.style.borderLeftColor = SectionBorder;
		root.style.borderRightColor = SectionBorder;
		root.style.borderTopLeftRadius = 5;
		root.style.borderTopRightRadius = 5;
		root.style.borderBottomLeftRadius = 5;
		root.style.borderBottomRightRadius = 5;

		var heading = new Label( title );
		heading.style.unityFontStyleAndWeight = FontStyle.Bold;
		heading.style.marginBottom = 6;
		heading.style.fontSize = 12;
		root.Add( heading );
		return root;
	}

	static Button MakeButton( string text, Action onClick )
	{
		var button = new Button( onClick ) { text = text };
		button.style.marginLeft = 1;
		button.style.marginRight = 1;
		button.style.marginTop = 1;
		button.style.marginBottom = 1;
		return button;
	}

	static Button MakeWideButton( string text, Action onClick )
	{
		Button button = MakeButton( text, onClick );
		button.style.flexGrow = 1;
		button.style.marginBottom = 3;
		button.style.height = 24;
		button.style.unityTextAlign = TextAnchor.MiddleLeft;
		button.style.paddingLeft = 8;
		return button;
	}
}
#endif
