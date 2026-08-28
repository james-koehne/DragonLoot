#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ensures PauseMenu, Tutorials, TutorialPopup, and PouchSummary hierarchy exists on Interface.prefab.
/// </summary>
public static class InterfaceHudInstaller
{
	const string InterfacePrefabPath = "Assets/Addressables/Interface.prefab";
	const string MenuPath = DragonLootMenus.Root + "/UI/Install Pause & Pouch Summary On Interface";

	[MenuItem( MenuPath )]
	public static void Install()
	{
		GameObject root = PrefabUtility.LoadPrefabContents( InterfacePrefabPath );
		try
		{
			InstallPauseMenu( root.transform );
			InstallTutorialPopup( root.transform );
			InstallPouchSummary( root.transform );
			PrefabUtility.SaveAsPrefabAsset( root, InterfacePrefabPath );
			Debug.Log( "Installed PauseMenu, TutorialPopup, and PouchSummary on Interface.prefab." );
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents( root );
		}

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
	}

	static void InstallPauseMenu( Transform canvasRoot )
	{
		Transform existing = canvasRoot.Find( "PauseMenu" );
		GameObject pauseRoot = existing != null ? existing.gameObject : new GameObject( "PauseMenu", typeof( RectTransform ) );
		if ( existing == null )
			pauseRoot.transform.SetParent( canvasRoot, false );

		RectTransform pauseRect = pauseRoot.GetComponent<RectTransform>();
		pauseRect.anchorMin = Vector2.zero;
		pauseRect.anchorMax = Vector2.one;
		pauseRect.offsetMin = Vector2.zero;
		pauseRect.offsetMax = Vector2.zero;
		pauseRect.pivot = new Vector2( 0.5f, 0.5f );
		pauseRect.localScale = Vector3.one;

		CanvasGroup group = pauseRoot.GetComponent<CanvasGroup>();
		if ( group == null )
			group = pauseRoot.AddComponent<CanvasGroup>();
		group.alpha = 0f;
		group.interactable = false;
		group.blocksRaycasts = false;

		Image dim = pauseRoot.GetComponent<Image>();
		if ( dim == null )
			dim = pauseRoot.AddComponent<Image>();
		dim.color = new Color( 0f, 0f, 0f, 0.72f );
		dim.raycastTarget = true;

		Transform panelT = pauseRoot.transform.Find( "Panel" );
		GameObject panel = panelT != null ? panelT.gameObject : new GameObject( "Panel", typeof( RectTransform ) );
		if ( panelT == null )
			panel.transform.SetParent( pauseRoot.transform, false );

		RectTransform panelRect = panel.GetComponent<RectTransform>();
		panelRect.anchorMin = new Vector2( 0.5f, 0.5f );
		panelRect.anchorMax = new Vector2( 0.5f, 0.5f );
		panelRect.pivot = new Vector2( 0.5f, 0.5f );
		panelRect.sizeDelta = new Vector2( 1040f, 1240f );
		panelRect.anchoredPosition = Vector2.zero;

		Image panelBg = panel.GetComponent<Image>();
		if ( panelBg == null )
			panelBg = panel.AddComponent<Image>();
		panelBg.color = new Color( 0.08f, 0.09f, 0.12f, 0.96f );
		panelBg.raycastTarget = true;

		Font font = ResolveFont();

		Transform controlsPanelT = panel.transform.Find( "ControlsPanel" );
		GameObject controlsPanel = controlsPanelT != null
			? controlsPanelT.gameObject
			: new GameObject( "ControlsPanel", typeof( RectTransform ), typeof( CanvasGroup ) );
		if ( controlsPanelT == null )
			controlsPanel.transform.SetParent( panel.transform, false );

		RectTransform controlsRect = controlsPanel.GetComponent<RectTransform>();
		controlsRect.anchorMin = Vector2.zero;
		controlsRect.anchorMax = Vector2.one;
		controlsRect.offsetMin = Vector2.zero;
		controlsRect.offsetMax = Vector2.zero;

		CanvasGroup controlsGroup = controlsPanel.GetComponent<CanvasGroup>();
		if ( controlsGroup == null )
			controlsGroup = controlsPanel.AddComponent<CanvasGroup>();

		// Move legacy children into ControlsPanel if needed.
		MoveIfDirectChild( panel.transform, "Controls", controlsPanel.transform );
		MoveIfDirectChild( panel.transform, "ResumeButton", controlsPanel.transform );
		MoveIfDirectChild( panel.transform, "QuitButton", controlsPanel.transform );

		Text controls = EnsureChildText( controlsPanel.transform, "Controls", font, new Vector2( 0f, 72f ), new Vector2( 920f, 880f ), 36, TextAnchor.UpperLeft, "Controls" );
		Button resume = EnsureChildButton( controlsPanel.transform, "ResumeButton", "Resume", font, new Vector2( -280f, -520f ) );
		Button quit = EnsureChildButton( controlsPanel.transform, "QuitButton", "Quit", font, new Vector2( 280f, -520f ) );
		Button tutorials = EnsureChildButton( controlsPanel.transform, "TutorialsButton", "Tutorials", font, new Vector2( 0f, -520f ) );

		Transform tutorialsPanelT = panel.transform.Find( "TutorialsPanel" );
		GameObject tutorialsPanel = tutorialsPanelT != null
			? tutorialsPanelT.gameObject
			: new GameObject( "TutorialsPanel", typeof( RectTransform ), typeof( CanvasGroup ) );
		if ( tutorialsPanelT == null )
			tutorialsPanel.transform.SetParent( panel.transform, false );

		RectTransform tutorialsRect = tutorialsPanel.GetComponent<RectTransform>();
		tutorialsRect.anchorMin = Vector2.zero;
		tutorialsRect.anchorMax = Vector2.one;
		tutorialsRect.offsetMin = Vector2.zero;
		tutorialsRect.offsetMax = Vector2.zero;

		CanvasGroup tutorialsGroup = tutorialsPanel.GetComponent<CanvasGroup>();
		if ( tutorialsGroup == null )
			tutorialsGroup = tutorialsPanel.AddComponent<CanvasGroup>();
		tutorialsGroup.alpha = 0f;
		tutorialsGroup.interactable = false;
		tutorialsGroup.blocksRaycasts = false;
		tutorialsPanel.SetActive( false );

		Text tutorialsTitle = EnsureChildText( tutorialsPanel.transform, "Title", font, new Vector2( 0f, 520f ), new Vector2( 920f, 64f ), 42, TextAnchor.MiddleCenter, "Tutorials" );
		Text emptyLabel = EnsureChildText( tutorialsPanel.transform, "EmptyLabel", font, new Vector2( 0f, 80f ), new Vector2( 860f, 200f ), 30, TextAnchor.MiddleCenter, "No tutorials discovered yet." );

		Transform listT = tutorialsPanel.transform.Find( "List" );
		GameObject listGo = listT != null ? listT.gameObject : new GameObject( "List", typeof( RectTransform ) );
		if ( listT == null )
			listGo.transform.SetParent( tutorialsPanel.transform, false );
		RectTransform listRect = listGo.GetComponent<RectTransform>();
		listRect.anchorMin = new Vector2( 0.5f, 0.5f );
		listRect.anchorMax = new Vector2( 0.5f, 0.5f );
		listRect.pivot = new Vector2( 0.5f, 1f );
		listRect.anchoredPosition = new Vector2( 0f, 420f );
		listRect.sizeDelta = new Vector2( 900f, 860f );

		Button tutorialsBack = EnsureChildButton( tutorialsPanel.transform, "BackButton", "Back", font, new Vector2( 0f, -520f ) );

		PauseMenuUI ui = pauseRoot.GetComponent<PauseMenuUI>();
		if ( ui == null )
			ui = pauseRoot.AddComponent<PauseMenuUI>();

		SerializedObject so = new SerializedObject( ui );
		so.FindProperty( "group" ).objectReferenceValue = group;
		so.FindProperty( "controlsText" ).objectReferenceValue = controls;
		so.FindProperty( "resumeButton" ).objectReferenceValue = resume;
		so.FindProperty( "quitButton" ).objectReferenceValue = quit;
		so.FindProperty( "tutorialsButton" ).objectReferenceValue = tutorials;
		so.FindProperty( "tutorialsBackButton" ).objectReferenceValue = tutorialsBack;
		so.FindProperty( "controlsPanelGroup" ).objectReferenceValue = controlsGroup;
		so.FindProperty( "tutorialsPanelGroup" ).objectReferenceValue = tutorialsGroup;
		so.FindProperty( "tutorialsListRoot" ).objectReferenceValue = listGo.transform;
		so.FindProperty( "tutorialsEmptyLabel" ).objectReferenceValue = emptyLabel;
		so.ApplyModifiedPropertiesWithoutUndo();

		_ = tutorialsTitle;
	}

	static void InstallTutorialPopup( Transform canvasRoot )
	{
		Transform existing = canvasRoot.Find( "TutorialPopup" );
		GameObject root = existing != null ? existing.gameObject : new GameObject( "TutorialPopup", typeof( RectTransform ) );
		if ( existing == null )
			root.transform.SetParent( canvasRoot, false );

		RectTransform rootRect = root.GetComponent<RectTransform>();
		rootRect.anchorMin = new Vector2( 1f, 0f );
		rootRect.anchorMax = new Vector2( 1f, 0f );
		rootRect.pivot = new Vector2( 1f, 0f );
		rootRect.anchoredPosition = new Vector2( -36f, 36f );
		rootRect.sizeDelta = new Vector2( 520f, 280f );
		rootRect.localScale = Vector3.one;

		CanvasGroup group = root.GetComponent<CanvasGroup>();
		if ( group == null )
			group = root.AddComponent<CanvasGroup>();
		group.alpha = 0f;
		group.blocksRaycasts = false;
		group.interactable = false;

		Image bg = root.GetComponent<Image>();
		if ( bg == null )
			bg = root.AddComponent<Image>();
		bg.color = new Color( 0.07f, 0.08f, 0.11f, 0.92f );
		bg.raycastTarget = true;

		Font font = ResolveFont();
		Text title = EnsureChildText( root.transform, "Title", font, new Vector2( 0f, 96f ), new Vector2( 480f, 40f ), 30, TextAnchor.MiddleLeft, "Tutorial" );
		Text body = EnsureChildText( root.transform, "Body", font, new Vector2( 0f, 10f ), new Vector2( 480f, 120f ), 24, TextAnchor.UpperLeft, string.Empty );
		Text hint = EnsureChildText( root.transform, "Hint", font, new Vector2( 0f, -70f ), new Vector2( 480f, 36f ), 22, TextAnchor.MiddleLeft, string.Empty );
		hint.color = new Color( 0.85f, 0.9f, 1f, 0.95f );
		Text step = EnsureChildText( root.transform, "Step", font, new Vector2( 180f, 96f ), new Vector2( 120f, 36f ), 22, TextAnchor.MiddleRight, "1 / 1" );
		step.color = new Color( 1f, 1f, 1f, 0.65f );

		Button dismiss = EnsureChildButton( root.transform, "DismissButton", "OK", font, new Vector2( 170f, -110f ) );
		RectTransform dismissRect = dismiss.GetComponent<RectTransform>();
		dismissRect.sizeDelta = new Vector2( 140f, 48f );

		TutorialPopupUI ui = root.GetComponent<TutorialPopupUI>();
		if ( ui == null )
			ui = root.AddComponent<TutorialPopupUI>();

		SerializedObject so = new SerializedObject( ui );
		so.FindProperty( "group" ).objectReferenceValue = group;
		so.FindProperty( "titleText" ).objectReferenceValue = title;
		so.FindProperty( "bodyText" ).objectReferenceValue = body;
		so.FindProperty( "hintText" ).objectReferenceValue = hint;
		so.FindProperty( "stepText" ).objectReferenceValue = step;
		so.FindProperty( "dismissButton" ).objectReferenceValue = dismiss;
		so.ApplyModifiedPropertiesWithoutUndo();
	}

	static void InstallPouchSummary( Transform canvasRoot )
	{
		Transform existing = canvasRoot.Find( "PouchSummary" );
		GameObject root = existing != null ? existing.gameObject : new GameObject( "PouchSummary", typeof( RectTransform ) );
		if ( existing == null )
			root.transform.SetParent( canvasRoot, false );

		RectTransform rootRect = root.GetComponent<RectTransform>();
		rootRect.anchorMin = new Vector2( 0f, 0.5f );
		rootRect.anchorMax = new Vector2( 0f, 0.5f );
		rootRect.pivot = new Vector2( 0f, 0.5f );
		rootRect.anchoredPosition = new Vector2( 72f, 0f );
		rootRect.sizeDelta = new Vector2( 560f, 440f );
		rootRect.localScale = Vector3.one;

		CanvasGroup group = root.GetComponent<CanvasGroup>();
		if ( group == null )
			group = root.AddComponent<CanvasGroup>();
		group.alpha = 0f;
		group.blocksRaycasts = false;
		group.interactable = false;

		Font font = ResolveFont();
		Text label = EnsureChildText( root.transform, "Label", font, Vector2.zero, Vector2.zero, 36, TextAnchor.MiddleLeft, string.Empty );
		RectTransform labelRect = label.rectTransform;
		labelRect.anchorMin = Vector2.zero;
		labelRect.anchorMax = Vector2.one;
		labelRect.offsetMin = Vector2.zero;
		labelRect.offsetMax = Vector2.zero;

		PouchSummaryUI ui = root.GetComponent<PouchSummaryUI>();
		if ( ui == null )
			ui = root.AddComponent<PouchSummaryUI>();

		SerializedObject so = new SerializedObject( ui );
		so.FindProperty( "group" ).objectReferenceValue = group;
		so.FindProperty( "label" ).objectReferenceValue = label;
		so.ApplyModifiedPropertiesWithoutUndo();
	}

	static void MoveIfDirectChild( Transform parent, string childName, Transform newParent )
	{
		Transform child = parent.Find( childName );
		if ( child == null || child.parent != parent )
			return;
		if ( newParent.Find( childName ) != null )
			return;
		child.SetParent( newParent, false );
	}

	static Font ResolveFont()
	{
		Font font = Resources.GetBuiltinResource<Font>( "LegacyRuntime.ttf" );
		if ( font == null )
			font = Resources.GetBuiltinResource<Font>( "Arial.ttf" );
		return font;
	}

	static Text EnsureChildText(
		Transform parent,
		string name,
		Font font,
		Vector2 anchoredPos,
		Vector2 size,
		int fontSize,
		TextAnchor align,
		string text )
	{
		Transform existing = parent.Find( name );
		GameObject go = existing != null
			? existing.gameObject
			: new GameObject( name, typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Text ) );
		if ( existing == null )
			go.transform.SetParent( parent, false );

		RectTransform rect = go.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2( 0.5f, 0.5f );
		rect.anchorMax = new Vector2( 0.5f, 0.5f );
		rect.pivot = new Vector2( 0.5f, 0.5f );
		rect.anchoredPosition = anchoredPos;
		rect.sizeDelta = size;

		Text label = go.GetComponent<Text>();
		if ( label == null )
			label = go.AddComponent<Text>();
		label.font = font;
		label.fontSize = fontSize;
		label.fontStyle = FontStyle.Bold;
		label.alignment = align;
		label.color = new Color( 1f, 1f, 1f, 0.95f );
		label.raycastTarget = false;
		label.horizontalOverflow = HorizontalWrapMode.Wrap;
		label.verticalOverflow = VerticalWrapMode.Overflow;
		if ( !string.IsNullOrEmpty( text ) )
			label.text = text;
		return label;
	}

	static Button EnsureChildButton( Transform parent, string name, string label, Font font, Vector2 anchoredPos )
	{
		Transform existing = parent.Find( name );
		GameObject go = existing != null
			? existing.gameObject
			: new GameObject( name, typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ), typeof( Button ) );
		if ( existing == null )
			go.transform.SetParent( parent, false );

		RectTransform rect = go.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2( 0.5f, 0.5f );
		rect.anchorMax = new Vector2( 0.5f, 0.5f );
		rect.pivot = new Vector2( 0.5f, 0.5f );
		rect.anchoredPosition = anchoredPos;
		rect.sizeDelta = new Vector2( 240f, 88f );

		Image image = go.GetComponent<Image>();
		if ( image == null )
			image = go.AddComponent<Image>();
		image.color = new Color( 0.22f, 0.28f, 0.38f, 1f );

		Button button = go.GetComponent<Button>();
		if ( button == null )
			button = go.AddComponent<Button>();

		Transform labelT = go.transform.Find( "Label" );
		GameObject labelGo = labelT != null
			? labelT.gameObject
			: new GameObject( "Label", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Text ) );
		if ( labelT == null )
			labelGo.transform.SetParent( go.transform, false );

		RectTransform labelRect = labelGo.GetComponent<RectTransform>();
		labelRect.anchorMin = Vector2.zero;
		labelRect.anchorMax = Vector2.one;
		labelRect.offsetMin = Vector2.zero;
		labelRect.offsetMax = Vector2.zero;

		Text text = labelGo.GetComponent<Text>();
		if ( text == null )
			text = labelGo.AddComponent<Text>();
		text.font = font;
		text.fontSize = 36;
		text.fontStyle = FontStyle.Bold;
		text.alignment = TextAnchor.MiddleCenter;
		text.color = Color.white;
		text.raycastTarget = false;
		text.text = label;

		return button;
	}
}
#endif
