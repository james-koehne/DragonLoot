using System.Text;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Escape opens a controls summary; Escape again resumes. Quit button exits the game.
/// Wire <see cref="group"/>, <see cref="controlsText"/>, and buttons on the Interface prefab.
/// </summary>
public class PauseMenuUI : MonoBehaviour
{
	static PauseMenuUI s_instance;

	[SerializeField] CanvasGroup group;
	[SerializeField] Text controlsText;
	[SerializeField] Button resumeButton;
	[SerializeField] Button quitButton;

	bool _ready;
	bool _open;
	float _timeScaleBeforePause = 1f;
	readonly StringBuilder _builder = new StringBuilder( 512 );

	public static bool IsOpen => s_instance != null && s_instance._open;

	public void Setup()
	{
		WireButtons();
		RefreshControlsText();
		SetOpen( false, applyGameplay: false );
		_ready = true;
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
	}

	void OnResumeClicked()
	{
		SetOpen( false );
	}

	void OnQuitClicked()
	{
		QuitGame();
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
