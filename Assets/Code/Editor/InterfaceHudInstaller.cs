#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ensures PauseMenu and PouchSummary hierarchy exists on Assets/Addressables/Interface.prefab.
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
			InstallPouchSummary( root.transform );
			PrefabUtility.SaveAsPrefabAsset( root, InterfacePrefabPath );
			Debug.Log( "Installed PauseMenu and PouchSummary on Interface.prefab." );
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
		Text controls = EnsureChildText( panel.transform, "Controls", font, new Vector2( 0f, 72f ), new Vector2( 920f, 960f ), 36, TextAnchor.UpperLeft, "Controls" );
		Button resume = EnsureChildButton( panel.transform, "ResumeButton", "Resume", font, new Vector2( -220f, -520f ) );
		Button quit = EnsureChildButton( panel.transform, "QuitButton", "Quit", font, new Vector2( 220f, -520f ) );

		PauseMenuUI ui = pauseRoot.GetComponent<PauseMenuUI>();
		if ( ui == null )
			ui = pauseRoot.AddComponent<PauseMenuUI>();

		SerializedObject so = new SerializedObject( ui );
		so.FindProperty( "group" ).objectReferenceValue = group;
		so.FindProperty( "controlsText" ).objectReferenceValue = controls;
		so.FindProperty( "resumeButton" ).objectReferenceValue = resume;
		so.FindProperty( "quitButton" ).objectReferenceValue = quit;
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
		rect.sizeDelta = new Vector2( 360f, 88f );

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
		text.fontSize = 40;
		text.fontStyle = FontStyle.Bold;
		text.alignment = TextAnchor.MiddleCenter;
		text.color = Color.white;
		text.raycastTarget = false;
		text.text = label;

		return button;
	}
}
#endif
