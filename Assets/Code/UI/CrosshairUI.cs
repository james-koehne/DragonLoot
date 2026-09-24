using UnityEngine;
using UnityEngine.UI;

public class CrosshairUI : MonoBehaviour
{
	static readonly Color DefaultIdleColor = Color.white;
	static readonly Color DefaultFocusedColor = new Color( 1f, 0.85f, 0.2f, 1f );

	static CrosshairUI _instance;

	[SerializeField] CanvasGroup _canvasGroup;
	[SerializeField] Image _image;
	[SerializeField] Image _contextualRing;

	PlayerInteractionDefinition _definition;
	bool _visible;

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

		SetVisible( true );
		SetAlpha( 1f );
		ApplyState( false, false );
	}

	public void SetVisible( bool visible )
	{
		_visible = visible;
		if ( _image != null )
			_image.enabled = visible;
		if ( _contextualRing != null && !visible )
			_contextualRing.enabled = false;
	}

	public void SetAlpha( float alpha )
	{
		if ( _canvasGroup != null )
			_canvasGroup.alpha = Mathf.Clamp01( alpha );
	}

	void Update()
	{
		if ( !_visible )
			return;

		bool focused = false;
		bool contextual = false;
		if ( GameMode.Instance != null && GameMode.Instance.Player != null )
		{
			PlayerInteraction interaction = GameMode.Instance.Player.Interaction;
			if ( interaction != null )
			{
				focused = interaction.HasInteractableFocus;
				contextual = interaction.HasContextualInteractFocus;
			}
		}

		ApplyState( focused, contextual );
	}

	void ApplyState( bool focused, bool contextual )
	{
		Color idle = DefaultIdleColor;
		Color focusedColor = DefaultFocusedColor;
		PlayerInteractionDefinition def = Definition;
		if ( def != null )
		{
			idle = def.crosshairIdleColor;
			focusedColor = def.crosshairFocusedColor;
		}

		if ( _image != null )
			_image.color = focused ? focusedColor : idle;

		if ( _contextualRing == null )
			return;

		_contextualRing.color = focusedColor;
		_contextualRing.enabled = _visible && contextual;
	}
}
