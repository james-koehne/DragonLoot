using System.Text;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Bottom-right mirror of <see cref="InteractionContextUI"/> for mode / tool prompts
/// (build toggle today; more contexts can append here later).
/// </summary>
public class RightContextUI : MonoBehaviour
{
	[Header( "Layout" )]
	[SerializeField] float bottomOffset = 20f;
	[SerializeField] int fontSize = 20;

	Text _label;
	bool _ready;
	bool _cinematicHidden;
	string _lastText;
	readonly StringBuilder _builder = new StringBuilder( 96 );

	public void Setup()
	{
		EnsureUi();
		_ready = true;
		SetVisible( false );
	}

	/// <summary>Hides the hint while a cinematic owns the HUD.</summary>
	public void SetCinematicHidden( bool hidden )
	{
		_cinematicHidden = hidden;
		if ( _cinematicHidden )
			SetVisible( false );
	}

	void Update()
	{
		if ( !_ready )
			return;

		Refresh();
	}

	void Refresh()
	{
		EnsureUi();
		if ( _label == null )
			return;

		if ( _cinematicHidden )
		{
			SetVisible( false );
			return;
		}

		if ( GameMode.Instance == null || GameMode.Instance.Player == null )
		{
			SetVisible( false );
			return;
		}

		PlayerController player = GameMode.Instance.Player;
		if ( player.IsDrivingMinecart || player.IsCinematicLocked || !player.GameplayInputEnabled )
		{
			SetVisible( false );
			return;
		}

		InputController inputController = InputController.Instance;
		GameInput gameInput = inputController != null ? inputController.GameInput : null;
		if ( gameInput == null )
		{
			SetVisible( false );
			return;
		}

		_builder.Length = 0;

		AppendBuildModeLines( gameInput, player );
		// Future right-side contexts append here.

		FinishRefresh();
	}

	void AppendBuildModeLines( GameInput gameInput, PlayerController player )
	{
		PlayerBuildMode buildMode = player.BuildMode;
		if ( buildMode == null || !buildMode.HasUnlockedWorldHammer || buildMode.IsActive )
			return;

		AppendBound( gameInput.BuildModeToggle, "Build" );
	}

	void AppendBound( InputAction action, string prompt )
	{
		string binding = FormatBinding( action );
		if ( binding == null )
			return;

		AppendLine( binding, prompt );
	}

	void AppendLine( string binding, string action )
	{
		if ( _builder.Length > 0 )
			_builder.Append( '\n' );
		_builder.Append( binding );
		_builder.Append( "  " );
		_builder.Append( action );
	}

	void FinishRefresh()
	{
		if ( _builder.Length == 0 )
		{
			SetVisible( false );
			return;
		}

		string text = _builder.ToString();
		if ( _lastText != text )
		{
			_lastText = text;
			_label.text = text;
		}

		SetVisible( true );
	}

	static string FormatBinding( InputAction action )
	{
		string display = FormatBindingDisplay( action );
		if ( string.IsNullOrEmpty( display ) )
			return null;

		return "[" + display + "]";
	}

	static string FormatBindingDisplay( InputAction action )
	{
		if ( action == null || !HasBindings( action ) )
			return null;

		string display = action.GetBindingDisplayString();
		if ( string.IsNullOrEmpty( display ) )
			return null;

		int pipe = display.IndexOf( '|' );
		if ( pipe >= 0 )
			display = display.Substring( 0, pipe ).Trim();

		return string.IsNullOrEmpty( display ) ? null : display;
	}

	static bool HasBindings( InputAction action )
	{
		if ( action == null )
			return false;

		var bindings = action.bindings;
		for ( int i = 0; i < bindings.Count; i++ )
		{
			if ( !bindings[ i ].isComposite && !string.IsNullOrEmpty( bindings[ i ].effectivePath ) )
				return true;
		}

		return false;
	}

	void SetVisible( bool visible )
	{
		EnsureUi();
		if ( _label != null )
			_label.enabled = visible;
	}

	void EnsureUi()
	{
		if ( _label != null )
			return;

		_label = GetComponentInChildren<Text>( true );
		if ( _label != null )
			return;

		GameObject go = new GameObject( "Label", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Text ), typeof( Shadow ) );
		go.transform.SetParent( transform, false );

		RectTransform root = transform as RectTransform;
		if ( root != null )
		{
			root.anchorMin = new Vector2( 0f, 0f );
			root.anchorMax = new Vector2( 1f, 0f );
			root.pivot = new Vector2( 0.5f, 0f );
			root.anchoredPosition = new Vector2( 0f, bottomOffset );
			root.sizeDelta = new Vector2( -40f, 220f );
		}

		RectTransform rect = go.GetComponent<RectTransform>();
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.one;
		rect.pivot = new Vector2( 0.5f, 0.5f );
		rect.anchoredPosition = Vector2.zero;
		rect.sizeDelta = Vector2.zero;

		_label = go.GetComponent<Text>();
		Font font = null;
		InteractionContextUI leftContext = transform.parent != null
			? transform.parent.GetComponentInChildren<InteractionContextUI>( true )
			: null;
		if ( leftContext != null )
		{
			Text leftLabel = leftContext.GetComponentInChildren<Text>( true );
			if ( leftLabel != null )
				font = leftLabel.font;
		}
		if ( font == null )
			font = Resources.GetBuiltinResource<Font>( "LegacyRuntime.ttf" );
		if ( font == null )
			font = Resources.GetBuiltinResource<Font>( "Arial.ttf" );
		_label.font = font;
		_label.fontSize = fontSize;
		_label.fontStyle = FontStyle.Bold;
		_label.alignment = TextAnchor.LowerRight;
		_label.color = new Color( 1f, 1f, 1f, 0.92f );
		_label.raycastTarget = false;
		_label.horizontalOverflow = HorizontalWrapMode.Overflow;
		_label.verticalOverflow = VerticalWrapMode.Overflow;
		_label.supportRichText = false;

		Shadow shadow = go.GetComponent<Shadow>();
		if ( shadow != null )
		{
			shadow.effectColor = new Color( 0f, 0f, 0f, 1f );
			shadow.effectDistance = new Vector2( 1.5f, -0.5f );
			shadow.useGraphicAlpha = true;
		}
	}
}
