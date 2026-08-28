using System.Collections.Generic;
using System.Text;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Escape opens a controls summary; Escape again resumes. Quit button exits the game.
/// Tutorials page lists discovered tutorials with text-only replay.
/// Wire references on the Interface prefab.
/// </summary>
public class PauseMenuUI : MonoBehaviour
{
	static PauseMenuUI s_instance;

	[SerializeField] CanvasGroup group;
	[SerializeField] Text controlsText;
	[SerializeField] Button resumeButton;
	[SerializeField] Button quitButton;
	[SerializeField] Button tutorialsButton;
	[SerializeField] Button tutorialsBackButton;
	[SerializeField] CanvasGroup controlsPanelGroup;
	[SerializeField] CanvasGroup tutorialsPanelGroup;
	[SerializeField] Transform tutorialsListRoot;
	[SerializeField] Text tutorialsEmptyLabel;

	bool _ready;
	bool _open;
	bool _tutorialsPage;
	float _timeScaleBeforePause = 1f;
	readonly StringBuilder _builder = new StringBuilder( 512 );
	readonly List<Button> _tutorialReplayButtons = new List<Button>();

	public static bool IsOpen => s_instance != null && s_instance._open;

	public void Setup()
	{
		EnsureTutorialsUi();
		WireButtons();
		RefreshControlsText();
		ShowControlsPage();
		SetOpen( false, applyGameplay: false );
		_ready = true;
	}

	void EnsureTutorialsUi()
	{
		if ( tutorialsButton != null && tutorialsPanelGroup != null && tutorialsListRoot != null )
			return;

		Transform panel = transform.Find( "Panel" );
		if ( panel == null )
			return;

		Font font = ResolveFont();

		if ( controlsPanelGroup == null )
		{
			Transform existingControls = panel.Find( "ControlsPanel" );
			GameObject controlsGo = existingControls != null
				? existingControls.gameObject
				: new GameObject( "ControlsPanel", typeof( RectTransform ), typeof( CanvasGroup ) );
			if ( existingControls == null )
			{
				controlsGo.transform.SetParent( panel, false );
				MoveDirectChild( panel, "Controls", controlsGo.transform );
				MoveDirectChild( panel, "ResumeButton", controlsGo.transform );
				MoveDirectChild( panel, "QuitButton", controlsGo.transform );
			}

			RectTransform controlsRect = controlsGo.GetComponent<RectTransform>();
			controlsRect.anchorMin = Vector2.zero;
			controlsRect.anchorMax = Vector2.one;
			controlsRect.offsetMin = Vector2.zero;
			controlsRect.offsetMax = Vector2.zero;
			controlsPanelGroup = controlsGo.GetComponent<CanvasGroup>();
			if ( controlsPanelGroup == null )
				controlsPanelGroup = controlsGo.AddComponent<CanvasGroup>();

			if ( controlsText == null )
			{
				Transform controlsT = controlsGo.transform.Find( "Controls" );
				if ( controlsT != null )
					controlsText = controlsT.GetComponent<Text>();
			}
			if ( resumeButton == null )
			{
				Transform resumeT = controlsGo.transform.Find( "ResumeButton" );
				if ( resumeT != null )
					resumeButton = resumeT.GetComponent<Button>();
			}
			if ( quitButton == null )
			{
				Transform quitT = controlsGo.transform.Find( "QuitButton" );
				if ( quitT != null )
					quitButton = quitT.GetComponent<Button>();
			}
		}

		if ( tutorialsButton == null )
		{
			Transform existing = controlsPanelGroup != null
				? controlsPanelGroup.transform.Find( "TutorialsButton" )
				: null;
			if ( existing != null )
				tutorialsButton = existing.GetComponent<Button>();
			else if ( controlsPanelGroup != null )
				tutorialsButton = CreateSimpleButton( controlsPanelGroup.transform, "TutorialsButton", "Tutorials", font, new Vector2( 0f, -520f ) );
		}

		if ( tutorialsPanelGroup == null )
		{
			Transform existingPanel = panel.Find( "TutorialsPanel" );
			GameObject tutorialsGo = existingPanel != null
				? existingPanel.gameObject
				: new GameObject( "TutorialsPanel", typeof( RectTransform ), typeof( CanvasGroup ) );
			if ( existingPanel == null )
				tutorialsGo.transform.SetParent( panel, false );

			RectTransform tutorialsRect = tutorialsGo.GetComponent<RectTransform>();
			tutorialsRect.anchorMin = Vector2.zero;
			tutorialsRect.anchorMax = Vector2.one;
			tutorialsRect.offsetMin = Vector2.zero;
			tutorialsRect.offsetMax = Vector2.zero;

			tutorialsPanelGroup = tutorialsGo.GetComponent<CanvasGroup>();
			if ( tutorialsPanelGroup == null )
				tutorialsPanelGroup = tutorialsGo.AddComponent<CanvasGroup>();

			if ( tutorialsGo.transform.Find( "Title" ) == null )
				CreateSimpleText( tutorialsGo.transform, "Title", "Tutorials", font, new Vector2( 0f, 520f ), new Vector2( 920f, 64f ), 42, TextAnchor.MiddleCenter );

			if ( tutorialsEmptyLabel == null )
			{
				Transform emptyT = tutorialsGo.transform.Find( "EmptyLabel" );
				if ( emptyT != null )
					tutorialsEmptyLabel = emptyT.GetComponent<Text>();
				else
					tutorialsEmptyLabel = CreateSimpleText( tutorialsGo.transform, "EmptyLabel", "No tutorials discovered yet.", font, new Vector2( 0f, 80f ), new Vector2( 860f, 200f ), 30, TextAnchor.MiddleCenter );
			}

			if ( tutorialsListRoot == null )
			{
				Transform listT = tutorialsGo.transform.Find( "List" );
				GameObject listGo = listT != null ? listT.gameObject : new GameObject( "List", typeof( RectTransform ) );
				if ( listT == null )
					listGo.transform.SetParent( tutorialsGo.transform, false );
				RectTransform listRect = listGo.GetComponent<RectTransform>();
				listRect.anchorMin = new Vector2( 0.5f, 0.5f );
				listRect.anchorMax = new Vector2( 0.5f, 0.5f );
				listRect.pivot = new Vector2( 0.5f, 1f );
				listRect.anchoredPosition = new Vector2( 0f, 420f );
				listRect.sizeDelta = new Vector2( 900f, 860f );
				tutorialsListRoot = listGo.transform;
			}

			if ( tutorialsBackButton == null )
			{
				Transform backT = tutorialsGo.transform.Find( "BackButton" );
				if ( backT != null )
					tutorialsBackButton = backT.GetComponent<Button>();
				else
					tutorialsBackButton = CreateSimpleButton( tutorialsGo.transform, "BackButton", "Back", font, new Vector2( 0f, -520f ) );
			}

			tutorialsGo.SetActive( false );
		}
	}

	static void MoveDirectChild( Transform parent, string childName, Transform newParent )
	{
		Transform child = parent.Find( childName );
		if ( child == null || child.parent != parent )
			return;
		if ( newParent.Find( childName ) != null )
			return;
		child.SetParent( newParent, false );
	}

	static Text CreateSimpleText( Transform parent, string name, string value, Font font, Vector2 pos, Vector2 size, int fontSize, TextAnchor align )
	{
		GameObject go = new GameObject( name, typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Text ) );
		go.transform.SetParent( parent, false );
		RectTransform rect = go.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2( 0.5f, 0.5f );
		rect.anchorMax = new Vector2( 0.5f, 0.5f );
		rect.pivot = new Vector2( 0.5f, 0.5f );
		rect.anchoredPosition = pos;
		rect.sizeDelta = size;
		Text text = go.GetComponent<Text>();
		text.font = font;
		text.fontSize = fontSize;
		text.fontStyle = FontStyle.Bold;
		text.alignment = align;
		text.color = Color.white;
		text.raycastTarget = false;
		text.horizontalOverflow = HorizontalWrapMode.Wrap;
		text.verticalOverflow = VerticalWrapMode.Overflow;
		text.text = value;
		return text;
	}

	static Button CreateSimpleButton( Transform parent, string name, string label, Font font, Vector2 pos )
	{
		GameObject go = new GameObject( name, typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ), typeof( Button ) );
		go.transform.SetParent( parent, false );
		RectTransform rect = go.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2( 0.5f, 0.5f );
		rect.anchorMax = new Vector2( 0.5f, 0.5f );
		rect.pivot = new Vector2( 0.5f, 0.5f );
		rect.anchoredPosition = pos;
		rect.sizeDelta = new Vector2( 240f, 88f );
		Image image = go.GetComponent<Image>();
		image.color = new Color( 0.22f, 0.28f, 0.38f, 1f );
		Button button = go.GetComponent<Button>();

		GameObject labelGo = new GameObject( "Label", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Text ) );
		labelGo.transform.SetParent( go.transform, false );
		RectTransform labelRect = labelGo.GetComponent<RectTransform>();
		labelRect.anchorMin = Vector2.zero;
		labelRect.anchorMax = Vector2.one;
		labelRect.offsetMin = Vector2.zero;
		labelRect.offsetMax = Vector2.zero;
		Text text = labelGo.GetComponent<Text>();
		text.font = font;
		text.fontSize = 36;
		text.fontStyle = FontStyle.Bold;
		text.alignment = TextAnchor.MiddleCenter;
		text.color = Color.white;
		text.raycastTarget = false;
		text.text = label;
		return button;
	}

	void Awake()
	{
		s_instance = this;
	}

	void OnDestroy()
	{
		if ( s_instance == this )
			s_instance = null;

		if ( _open )
			SetOpen( false, applyGameplay: true );
	}

	void Update()
	{
		if ( !_ready )
			return;

		Keyboard keyboard = Keyboard.current;
		if ( keyboard == null || !keyboard.escapeKey.wasPressedThisFrame )
			return;

		if ( DebugOverlay.IsOpen )
			return;

		if ( _open && _tutorialsPage )
		{
			ShowControlsPage();
			return;
		}

		SetOpen( !_open );
	}

	public void SetOpen( bool open )
	{
		SetOpen( open, applyGameplay: true );
	}

	void SetOpen( bool open, bool applyGameplay )
	{
		if ( _open == open )
			return;

		_open = open;

		if ( group != null )
		{
			group.alpha = open ? 1f : 0f;
			group.interactable = open;
			group.blocksRaycasts = open;
		}

		if ( open )
			ShowControlsPage();

		if ( !applyGameplay )
			return;

		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;

		if ( open )
		{
			_timeScaleBeforePause = Time.timeScale;
			if ( _timeScaleBeforePause <= 0.001f )
				_timeScaleBeforePause = 1f;
			Time.timeScale = 0f;

			if ( player != null )
				player.SetGameplayInputEnabled( false );
			else
			{
				Cursor.lockState = CursorLockMode.None;
				Cursor.visible = true;
			}
		}
		else
		{
			Time.timeScale = _timeScaleBeforePause;

			if ( DebugOverlay.IsOpen )
				return;

			if ( player != null )
				player.SetGameplayInputEnabled( true );
			else
			{
				Cursor.lockState = CursorLockMode.Locked;
				Cursor.visible = false;
			}
		}
	}

	public void QuitGame()
	{
		Time.timeScale = 1f;
#if UNITY_EDITOR
		UnityEditor.EditorApplication.isPlaying = false;
#else
		Application.Quit();
#endif
	}

	void WireButtons()
	{
		if ( resumeButton != null )
		{
			resumeButton.onClick.RemoveListener( OnResumeClicked );
			resumeButton.onClick.AddListener( OnResumeClicked );
		}

		if ( quitButton != null )
		{
			quitButton.onClick.RemoveListener( OnQuitClicked );
			quitButton.onClick.AddListener( OnQuitClicked );
		}

		if ( tutorialsButton != null )
		{
			tutorialsButton.onClick.RemoveListener( OnTutorialsClicked );
			tutorialsButton.onClick.AddListener( OnTutorialsClicked );
		}

		if ( tutorialsBackButton != null )
		{
			tutorialsBackButton.onClick.RemoveListener( OnTutorialsBackClicked );
			tutorialsBackButton.onClick.AddListener( OnTutorialsBackClicked );
		}
	}

	void OnResumeClicked()
	{
		SetOpen( false );
	}

	void OnQuitClicked()
	{
		QuitGame();
	}

	void OnTutorialsClicked()
	{
		ShowTutorialsPage();
	}

	void OnTutorialsBackClicked()
	{
		ShowControlsPage();
	}

	void ShowControlsPage()
	{
		_tutorialsPage = false;
		SetPanelVisible( controlsPanelGroup, true );
		SetPanelVisible( tutorialsPanelGroup, false );
	}

	void ShowTutorialsPage()
	{
		_tutorialsPage = true;
		SetPanelVisible( controlsPanelGroup, false );
		SetPanelVisible( tutorialsPanelGroup, true );
		RebuildTutorialsList();
	}

	static void SetPanelVisible( CanvasGroup panel, bool visible )
	{
		if ( panel == null )
			return;
		panel.alpha = visible ? 1f : 0f;
		panel.interactable = visible;
		panel.blocksRaycasts = visible;
		if ( panel.gameObject.activeSelf != visible )
			panel.gameObject.SetActive( visible );
	}

	void RebuildTutorialsList()
	{
		ClearTutorialButtons();

		TutorialManager manager = TutorialManager.Instance;
		IReadOnlyList<TutorialDefinition> entries = manager != null
			? manager.GetCatalogEntries()
			: System.Array.Empty<TutorialDefinition>();

		int shown = 0;
		for ( int i = 0; i < entries.Count; i++ )
		{
			TutorialDefinition def = entries[ i ];
			if ( def == null || string.IsNullOrEmpty( def.id ) )
				continue;

			bool discovered = manager != null && manager.IsDiscovered( def.id );
			bool completed = manager != null && manager.IsCompleted( def.id );
			if ( !discovered && !completed )
				continue;

			CreateTutorialRow( def, completed );
			shown++;
		}

		if ( tutorialsEmptyLabel != null )
		{
			tutorialsEmptyLabel.gameObject.SetActive( shown == 0 );
			if ( shown == 0 )
				tutorialsEmptyLabel.text = "No tutorials discovered yet.\nPlay to unlock tips.";
		}
	}

	void CreateTutorialRow( TutorialDefinition def, bool completed )
	{
		if ( tutorialsListRoot == null )
			return;

		Font font = ResolveFont();
		GameObject row = new GameObject( "Tutorial_" + def.id, typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
		row.transform.SetParent( tutorialsListRoot, false );

		RectTransform rowRect = row.GetComponent<RectTransform>();
		rowRect.anchorMin = new Vector2( 0f, 1f );
		rowRect.anchorMax = new Vector2( 1f, 1f );
		rowRect.pivot = new Vector2( 0.5f, 1f );
		rowRect.sizeDelta = new Vector2( 0f, 72f );

		Image bg = row.GetComponent<Image>();
		bg.color = new Color( 0.14f, 0.16f, 0.2f, 0.95f );
		bg.raycastTarget = true;

		GameObject labelGo = new GameObject( "Label", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Text ) );
		labelGo.transform.SetParent( row.transform, false );
		RectTransform labelRect = labelGo.GetComponent<RectTransform>();
		labelRect.anchorMin = new Vector2( 0f, 0f );
		labelRect.anchorMax = new Vector2( 1f, 1f );
		labelRect.offsetMin = new Vector2( 16f, 4f );
		labelRect.offsetMax = new Vector2( -180f, -4f );

		Text label = labelGo.GetComponent<Text>();
		label.font = font;
		label.fontSize = 28;
		label.fontStyle = FontStyle.Bold;
		label.alignment = TextAnchor.MiddleLeft;
		label.color = Color.white;
		label.raycastTarget = false;
		label.horizontalOverflow = HorizontalWrapMode.Wrap;
		label.verticalOverflow = VerticalWrapMode.Truncate;
		string mark = completed ? "✓ " : "";
		label.text = mark + ( string.IsNullOrEmpty( def.title ) ? def.id : def.title );

		GameObject buttonGo = new GameObject( "Replay", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ), typeof( Button ) );
		buttonGo.transform.SetParent( row.transform, false );
		RectTransform buttonRect = buttonGo.GetComponent<RectTransform>();
		buttonRect.anchorMin = new Vector2( 1f, 0.5f );
		buttonRect.anchorMax = new Vector2( 1f, 0.5f );
		buttonRect.pivot = new Vector2( 1f, 0.5f );
		buttonRect.anchoredPosition = new Vector2( -12f, 0f );
		buttonRect.sizeDelta = new Vector2( 150f, 52f );

		Image buttonImage = buttonGo.GetComponent<Image>();
		buttonImage.color = new Color( 0.22f, 0.28f, 0.38f, 1f );

		Button button = buttonGo.GetComponent<Button>();
		string tutorialId = def.id;
		button.onClick.AddListener( () => OnReplayTutorial( tutorialId ) );
		_tutorialReplayButtons.Add( button );

		GameObject buttonLabelGo = new GameObject( "Label", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Text ) );
		buttonLabelGo.transform.SetParent( buttonGo.transform, false );
		RectTransform buttonLabelRect = buttonLabelGo.GetComponent<RectTransform>();
		buttonLabelRect.anchorMin = Vector2.zero;
		buttonLabelRect.anchorMax = Vector2.one;
		buttonLabelRect.offsetMin = Vector2.zero;
		buttonLabelRect.offsetMax = Vector2.zero;

		Text buttonLabel = buttonLabelGo.GetComponent<Text>();
		buttonLabel.font = font;
		buttonLabel.fontSize = 26;
		buttonLabel.fontStyle = FontStyle.Bold;
		buttonLabel.alignment = TextAnchor.MiddleCenter;
		buttonLabel.color = Color.white;
		buttonLabel.raycastTarget = false;
		buttonLabel.text = "Replay";

		// Stack rows vertically via anchored Y from top.
		int index = tutorialsListRoot.childCount - 1;
		rowRect.anchoredPosition = new Vector2( 0f, -index * 80f );
	}

	void ClearTutorialButtons()
	{
		_tutorialReplayButtons.Clear();
		if ( tutorialsListRoot == null )
			return;

		for ( int i = tutorialsListRoot.childCount - 1; i >= 0; i-- )
		{
			Transform child = tutorialsListRoot.GetChild( i );
			if ( child != null )
				Object.Destroy( child.gameObject );
		}
	}

	void OnReplayTutorial( string tutorialId )
	{
		TutorialManager manager = TutorialManager.Instance;
		if ( manager == null )
			return;
		manager.Replay( tutorialId );
	}

	static Font ResolveFont()
	{
		Font font = Resources.GetBuiltinResource<Font>( "LegacyRuntime.ttf" );
		if ( font == null )
			font = Resources.GetBuiltinResource<Font>( "Arial.ttf" );
		return font;
	}

	void RefreshControlsText()
	{
		if ( controlsText == null )
			return;

		InputController inputController = InputController.Instance;
		GameInput gameInput = inputController != null ? inputController.GameInput : null;
		_builder.Length = 0;
		_builder.AppendLine( "Controls" );
		_builder.AppendLine();

		if ( gameInput == null )
		{
			_builder.Append( "Input not ready." );
			controlsText.text = _builder.ToString();
			return;
		}

		AppendControl( gameInput.Move, "Move" );
		AppendControl( gameInput.Jump, "Jump" );
		AppendControl( gameInput.Sprint, "Sprint / Slide" );
		AppendControl( gameInput.CameraDelta, "Look" );
		AppendControl( gameInput.Interact, "Interact" );
		AppendControl( gameInput.SecondaryInteract, "Place / Throw" );
		AppendControl( gameInput.Clean, "Clean / Polish" );
		AppendControl( gameInput.WholeStackPickup, "Pick up stack (hold)" );
		AppendControl( gameInput.WholeStackPlace, "Place stack (hold)" );
		AppendControl( gameInput.RotateLeft, "Rotate left" );
		AppendControl( gameInput.RotateRight, "Rotate right" );
		AppendControl( gameInput.ScrollWheel, "Cycle held item" );

		if ( gameInput.CategorySlots != null )
		{
			AppendControl( SlotOrNull( gameInput.CategorySlots, 0 ), "Coin pouch" );
			AppendControl( SlotOrNull( gameInput.CategorySlots, 1 ), "Gem pouch" );
			AppendControl( SlotOrNull( gameInput.CategorySlots, 2 ), "Artifact pouch" );
		}

		if ( gameInput.AbilitySlots != null )
		{
			for ( int i = 0; i < gameInput.AbilitySlots.Length; i++ )
				AppendControl( gameInput.AbilitySlots[ i ], "Ability " + ( i + 1 ) );
		}

		_builder.AppendLine();
		_builder.Append( "[Esc]  Resume" );
		controlsText.text = _builder.ToString();
	}

	void AppendControl( InputAction action, string label )
	{
		string binding = FormatBindingDisplay( action );
		if ( string.IsNullOrEmpty( binding ) )
			return;

		_builder.Append( '[' ).Append( binding ).Append( "]  " ).AppendLine( label );
	}

	static InputAction SlotOrNull( InputAction[] slots, int index )
	{
		if ( slots == null || index < 0 || index >= slots.Length )
			return null;
		return slots[ index ];
	}

	static string FormatBindingDisplay( InputAction action )
	{
		if ( action == null )
			return null;

		var bindings = action.bindings;
		bool has = false;
		for ( int i = 0; i < bindings.Count; i++ )
		{
			if ( !bindings[ i ].isComposite && !string.IsNullOrEmpty( bindings[ i ].effectivePath ) )
			{
				has = true;
				break;
			}
		}

		if ( !has )
			return null;

		string display = action.GetBindingDisplayString();
		if ( string.IsNullOrEmpty( display ) )
			return null;

		int pipe = display.IndexOf( '|' );
		if ( pipe >= 0 )
			display = display.Substring( 0, pipe ).Trim();

		return string.IsNullOrEmpty( display ) ? null : display;
	}
}
