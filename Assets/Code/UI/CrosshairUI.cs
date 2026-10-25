using UnityEngine;
using UnityEngine.UI;

public class CrosshairUI : MonoBehaviour
{
	static readonly Color DefaultIdleColor = Color.white;
	static readonly Color DefaultFocusedColor = new Color( 1f, 0.85f, 0.2f, 1f );
	static readonly Color DefaultFullPouchColor = new Color( 0.95f, 0.28f, 0.32f, 1f );

	static CrosshairUI _instance;

	[SerializeField] CanvasGroup _canvasGroup;
	[SerializeField] Image _image;
	[SerializeField] Image _contextualRing;

	PlayerInteractionDefinition _definition;
	bool _visible;
	bool _cinematicHidden;

	public static CrosshairUI Instance => _instance;

	PlayerInteractionDefinition Definition => RuntimeDefinition.Resolve( ref _definition );

	void Awake()
	{
		_instance = this;
	}

	void OnDestroy()
	{
		if ( _instance == this )
			_instance = null;
	}

	public void Setup()
	{
		if ( _instance == null )
			_instance = this;

		_cinematicHidden = false;
		SetVisible( true );
		SetAlpha( 1f );
		ApplyState( false, false );
	}

	/// <summary>Hides the crosshair (and look-at context) while a cinematic owns the HUD.</summary>
	public void SetCinematicHidden( bool hidden )
	{
		_cinematicHidden = hidden;
		if ( hidden )
		{
			SetVisible( false );
			SetAlpha( 0f );
		}
		else
		{
			SetVisible( true );
			SetAlpha( 1f );
		}

		CrosshairContextUI context = GetComponent<CrosshairContextUI>();
		if ( context != null )
			context.SetCinematicHidden( hidden );
	}

	public void SetVisible( bool visible )
	{
		_visible = visible && !_cinematicHidden;
		if ( _image != null )
			_image.enabled = _visible;
		if ( _contextualRing != null && !_visible )
			_contextualRing.enabled = false;
	}

	public void SetAlpha( float alpha )
	{
		if ( _canvasGroup != null )
			_canvasGroup.alpha = _cinematicHidden ? 0f : Mathf.Clamp01( alpha );
	}

	void Update()
	{
		if ( !_visible || _cinematicHidden )
			return;

		bool focused = false;
		bool contextual = false;
		bool fullPouch = false;
		if ( GameMode.Instance != null && GameMode.Instance.Player != null )
		{
			PlayerInteraction interaction = GameMode.Instance.Player.Interaction;
			if ( interaction != null )
			{
				focused = interaction.HasInteractableFocus;
				contextual = interaction.HasContextualInteractFocus;
				fullPouch = interaction.HasFullPouchPickableFocus;
			}
		}

		ApplyState( focused, contextual, fullPouch );
	}

	void ApplyState( bool focused, bool contextual, bool fullPouch = false )
	{
		Color idle = DefaultIdleColor;
		Color focusedColor = DefaultFocusedColor;
		Color fullPouchColor = DefaultFullPouchColor;
		PlayerInteractionDefinition def = Definition;
		if ( def != null )
		{
			idle = def.crosshairIdleColor;
			focusedColor = def.crosshairFocusedColor;
			fullPouchColor = def.crosshairFullPouchColor;
		}

		Color active = idle;
		if ( fullPouch )
			active = fullPouchColor;
		else if ( focused )
			active = focusedColor;

		if ( _image != null )
			_image.color = active;

		if ( _contextualRing == null )
			return;

		_contextualRing.color = fullPouch ? fullPouchColor : focusedColor;
		_contextualRing.enabled = _visible && contextual;
	}
}
